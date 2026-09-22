using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NdiWorker
{
    class Program
    {
        private static readonly string StreamName = Environment.GetEnvironmentVariable("STREAM_NAME") ?? "NDI-Worker-01";
        private static readonly string SourceType = Environment.GetEnvironmentVariable("SOURCE_TYPE") ?? "ColorBars"; // ColorBars, Picture, VideoClip, WebPage
        private static readonly string SourceUri = Environment.GetEnvironmentVariable("SOURCE_URI") ?? "";
        private static readonly string Resolution = Environment.GetEnvironmentVariable("RESOLUTION") ?? "1920x1080";
        private static readonly string Fps = Environment.GetEnvironmentVariable("FPS") ?? "30";
        private static readonly string Pattern = Environment.GetEnvironmentVariable("PATTERN") ?? "smptebars";
        private static readonly string AudioFreq = Environment.GetEnvironmentVariable("AUDIO_FREQ") ?? "1000";
        private static readonly string OverlayText = Environment.GetEnvironmentVariable("OVERLAY_TEXT") ?? "LIVE FINANCIAL NDI";
        private static readonly string DestIp = Environment.GetEnvironmentVariable("DEST_IP") ?? "239.255.0.1";
        private static readonly string DestPort = Environment.GetEnvironmentVariable("DEST_PORT") ?? "5004";
        private static readonly string WebRtcUrl = Environment.GetEnvironmentVariable("WEBRTC_URL") ?? "";
        private static readonly string WorkerIp = Environment.GetEnvironmentVariable("WORKER_IP") ?? "10.10.1.10";
        private static readonly int HealthPort = int.TryParse(Environment.GetEnvironmentVariable("HEALTH_PORT"), out var p) ? p : 8080;

        // NDI Discovery & Tally / UMD parameters
        private static readonly string DiscoveryMode = Environment.GetEnvironmentVariable("DISCOVERY_MODE") ?? "Bonjour"; // Bonjour or CentralServer
        private static readonly string DiscoveryServerIp = Environment.GetEnvironmentVariable("DISCOVERY_SERVER_IP") ?? "127.0.0.1";
        private static readonly string DiscoveryServerPort = Environment.GetEnvironmentVariable("DISCOVERY_SERVER_PORT") ?? "5959";
        private static string TallyState = Environment.GetEnvironmentVariable("TALLY_STATE") ?? "Off"; // Off, Program, Preview
        private static readonly string UmdText = Environment.GetEnvironmentVariable("UMD_TEXT") ?? StreamName;

        private static readonly DateTime StartTime = DateTime.UtcNow;
        private static Process? _ffmpegProcess;

        static async Task Main(string[] args)
        {
            string configFile = "workers.json";
            for (int i = 0; i < args.Length; i++)
            {
                if ((args[i] == "--config" || args[i] == "-c") && i + 1 < args.Length)
                {
                    configFile = args[i + 1];
                }
            }

            if (File.Exists(configFile))
            {
                Console.WriteLine($"[NdiWorker] Config file detected: '{configFile}'. Loading multi-worker array...");
                try
                {
                    string json = File.ReadAllText(configFile);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        using var cts = new CancellationTokenSource();
                        Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };

                        var workerTasks = new System.Collections.Generic.List<Task>();
                        foreach (var elem in doc.RootElement.EnumerateArray())
                        {
                            var item = elem;
                            workerTasks.Add(Task.Run(() => RunSingleWorkerFromConfig(item, cts.Token)));
                        }

                        await Task.WhenAll(workerTasks);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[NdiWorker] Config file parse error: {ex.Message}. Falling back to env configuration...");
                }
            }

            Console.WriteLine("==================================================");
            Console.WriteLine($" NDI C# Broadcast Worker with Discovery & Tally starting...");
            Console.WriteLine($" Stream Name     : {StreamName}");
            Console.WriteLine($" Source Type     : {SourceType}");
            Console.WriteLine($" Source URI      : {(string.IsNullOrEmpty(SourceUri) ? "(N/A)" : SourceUri)}");
            Console.WriteLine($" Discovery Mode  : {DiscoveryMode} {(DiscoveryMode == "CentralServer" ? $"({DiscoveryServerIp}:{DiscoveryServerPort})" : "")}");
            Console.WriteLine($" Tally State     : {TallyState}");
            Console.WriteLine($" UMD Text        : {UmdText}");
            Console.WriteLine($" Resolution      : {Resolution} @ {Fps} fps");
            Console.WriteLine($" Destination     : udp://{DestIp}:{DestPort}");
            Console.WriteLine($" Health Port     : {HealthPort}");
            Console.WriteLine("==================================================");

            using var singleCts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                Console.WriteLine("[NdiWorker] Shutdown requested.");
                e.Cancel = true;
                singleCts.Cancel();
            };

            // Start Health HTTP Listener
            var healthTask = RunHealthServerAsync(HealthPort, singleCts.Token);

            // Configure NDI Central Discovery / mDNS ini file BEFORE NDI SDK initialization
            ConfigureNdiDiscovery();

            // Initialize Native NDI Sender
            bool ndiInitialized = false;
            IntPtr pNdiSender = IntPtr.Zero;
            try
            {
                ndiInitialized = NdiNative.NDIlib_initialize();
                Console.WriteLine($"[NdiWorker] NDIlib_initialize() status: {ndiInitialized}");
                if (ndiInitialized)
                {
                    var sendSettings = new NdiNative.NDIlib_send_create_t
                    {
                        p_ndi_name = StreamName,
                        p_groups = string.Empty,
                        clock_video = true,
                        clock_audio = false
                    };
                    pNdiSender = NdiNative.NDIlib_send_create(ref sendSettings);
                    if (pNdiSender != IntPtr.Zero)
                    {
                        Console.WriteLine($"[NdiWorker] Native NDI Sender initialized: '{StreamName}' (Handle: {pNdiSender})");
                    }
                    else
                    {
                        Console.WriteLine($"[NdiWorker] NDIlib_send_create failed for '{StreamName}'");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NdiWorker] Native NDI SDK initialization note: {ex.Message}");
            }

            // Start FFmpeg process (pass pNdiSender for rawvideo BGRA frame streaming if native sender active)
            StartFfmpegPipeline(pNdiSender, singleCts.Token);

            try
            {
                await Task.Delay(-1, singleCts.Token);
            }
            catch (TaskCanceledException) { }

            if (pNdiSender != IntPtr.Zero)
            {
                try { NdiNative.NDIlib_send_destroy(pNdiSender); } catch { }
            }

            StopFfmpegPipeline();
            Console.WriteLine("[NdiWorker] Shutdown complete.");
        }

        private static async Task RunSingleWorkerFromConfig(JsonElement cfg, CancellationToken token)
        {
            string sName = cfg.TryGetProperty("stream_name", out var p1) ? p1.GetString() ?? StreamName : StreamName;
            string sType = cfg.TryGetProperty("source_type", out var p2) ? p2.GetString() ?? SourceType : SourceType;
            string sUri = cfg.TryGetProperty("source_uri", out var p3) ? p3.GetString() ?? "" : "";
            int hPort = cfg.TryGetProperty("health_port", out var p4) && p4.TryGetInt32(out var hp) ? hp : HealthPort;
            string wIp = cfg.TryGetProperty("worker_ip", out var p5) ? p5.GetString() ?? WorkerIp : WorkerIp;

            Console.WriteLine($"[NdiWorker] Launching configured stream '{sName}' [{sType}] on health port {hPort}...");
            var healthTask = RunHealthServerAsync(hPort, token);

            try { await Task.Delay(-1, token); } catch { }
        }

        private static void StartFfmpegPipeline(IntPtr pNdiSender, CancellationToken token)
        {
            string inputArgs = "";
            string filterArgs = "";

            string asource = AudioFreq != "0" && !string.IsNullOrEmpty(AudioFreq)
                ? $"sine=frequency={AudioFreq}:sample_rate=48000"
                : "anullsrc=r=48000:cl=stereo";

            string tallyBoxColor = TallyState.ToLowerInvariant() switch
            {
                "program" => "red@0.8",
                "preview" => "green@0.8",
                _ => "black@0.6"
            };

            string vfilter = $"scale={Resolution.Replace('x', ':')},drawtext=text='STREAM\\: {StreamName} [{SourceType}] ({Resolution} @ {Fps}fps)':x=40:y=40:fontsize=36:fontcolor=white:box=1:boxcolor={tallyBoxColor},drawtext=text='TALLY\\: {TallyState.ToUpper()} | UMD\\: {UmdText} | {OverlayText}':x=40:y=90:fontsize=28:fontcolor=yellow:box=1:boxcolor={tallyBoxColor}";

            switch (SourceType.ToLowerInvariant())
            {
                case "picture":
                    if (!string.IsNullOrEmpty(SourceUri))
                    {
                        inputArgs = $"-loop 1 -i \"{SourceUri}\" -f lavfi -i \"{asource}\"";
                    }
                    else
                    {
                        inputArgs = $"-f lavfi -i \"testsrc2=size={Resolution}:rate={Fps}\" -f lavfi -i \"{asource}\"";
                    }
                    filterArgs = $"-vf \"{vfilter}\"";
                    break;

                case "videoclip":
                    if (!string.IsNullOrEmpty(SourceUri))
                    {
                        inputArgs = $"-stream_loop -1 -i \"{SourceUri}\"";
                        filterArgs = $"-vf \"{vfilter}\"";
                    }
                    else
                    {
                        inputArgs = $"-f lavfi -i \"testsrc=size={Resolution}:rate={Fps}\" -f lavfi -i \"{asource}\"";
                        filterArgs = $"-vf \"{vfilter}\"";
                    }
                    break;

                case "webpage":
                    if (!string.IsNullOrEmpty(SourceUri))
                    {
                        string safeUri = SourceUri.Replace(":", "\\:");
                        inputArgs = $"-f lavfi -i \"mandelbrot=size={Resolution}:rate={Fps}\" -f lavfi -i \"{asource}\"";
                        string webOverlay = $"drawtext=text='LIVE WEB PAGE\\: {safeUri}':x=40:y=140:fontsize=24:fontcolor=cyan:box=1:boxcolor={tallyBoxColor}";
                        filterArgs = $"-vf \"{vfilter},{webOverlay}\"";
                    }
                    else
                    {
                        inputArgs = $"-f lavfi -i \"rgbtestsrc=size={Resolution}:rate={Fps}\" -f lavfi -i \"{asource}\"";
                        filterArgs = $"-vf \"{vfilter}\"";
                    }
                    break;

                case "quadsplit":
                    var subResolutions = Resolution.Split('x');
                    int subW = int.TryParse(subResolutions[0], out var wVal) ? wVal / 2 : 960;
                    int subH = subResolutions.Length > 1 && int.TryParse(subResolutions[1], out var hVal) ? hVal / 2 : 540;

                    string[] quadSources = !string.IsNullOrWhiteSpace(SourceUri) ? SourceUri.Split(',') : Array.Empty<string>();
                    var sbInputs = new StringBuilder();
                    var sbFilter = new StringBuilder("-filter_complex \"");

                    string[] fallbackPatterns = new[] { "smptebars", "testsrc2", "rgbtestsrc", "mandelbrot" };

                    for (int i = 0; i < 4; i++)
                    {
                        if (i < quadSources.Length && !string.IsNullOrWhiteSpace(quadSources[i]))
                        {
                            sbInputs.Append($"-i \"{quadSources[i].Trim()}\" ");
                        }
                        else
                        {
                            sbInputs.Append($"-f lavfi -i \"{fallbackPatterns[i]}=size={subW}x{subH}:rate={Fps}\" ");
                        }
                        sbFilter.Append($"[{i}:v]scale={subW}:{subH}[v{i}]; ");
                    }

                    sbInputs.Append($"-f lavfi -i \"{asource}\"");
                    sbFilter.Append($"[v0][v1][v2][v3]xstack=inputs=4:layout=0_0|w0_0|0_h0|w0_h0[quad]; [quad]{vfilter}[out]\" -map \"[out]\" -map 4:a");

                    inputArgs = sbInputs.ToString();
                    filterArgs = sbFilter.ToString();
                    break;

                case "colorbars":
                default:
                    string vsource = Pattern switch
                    {
                        "mandelbrot" => $"mandelbrot=size={Resolution}:rate={Fps}",
                        "testsrc" => $"testsrc=size={Resolution}:rate={Fps}",
                        "testsrc2" => $"testsrc2=size={Resolution}:rate={Fps}",
                        "rgbtestsrc" => $"rgbtestsrc=size={Resolution}:rate={Fps}",
                        "ball" => $"cellauto=size={Resolution}:rate={Fps}",
                        _ => $"smptebars=size={Resolution}:rate={Fps}"
                    };
                    inputArgs = $"-f lavfi -i \"{vsource}\" -f lavfi -i \"{asource}\"";
                    filterArgs = $"-vf \"{vfilter}\"";
                    break;
            }

            bool useNativeNdi = pNdiSender != IntPtr.Zero;

            string rtpOutput = !string.IsNullOrEmpty(WebRtcUrl)
                ? $" -c:v libx264 -preset ultrafast -tune zerolatency -an -f rtp \"{WebRtcUrl}\""
                : "";

            string arguments = useNativeNdi
                ? $"-re {inputArgs} {filterArgs} -an -pix_fmt bgra -f rawvideo pipe:1{rtpOutput}"
                : $"-re {inputArgs} {filterArgs} -c:v libx264 -preset ultrafast -tune zerolatency -pix_fmt yuv420p -g {Fps} -c:a aac -b:a 128k -f mpegts \"udp://{DestIp}:{DestPort}?pkt_size=1316&ttl=1\"{rtpOutput}";

            Console.WriteLine($"[NdiWorker] Launching FFmpeg pipeline (Native NDI Mode: {useNativeNdi}): ffmpeg {arguments}");

            var psi = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = arguments,
                RedirectStandardOutput = useNativeNdi,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                _ffmpegProcess = Process.Start(psi);
                if (_ffmpegProcess != null)
                {
                    _ffmpegProcess.ErrorDataReceived += (s, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data) && (e.Data.Contains("frame=") || e.Data.Contains("fps=")))
                        {
                            Console.WriteLine($"[FFmpeg] {e.Data.Trim()}");
                        }
                    };
                    _ffmpegProcess.BeginErrorReadLine();

                    if (useNativeNdi)
                    {
                        Task.Run(() => RunNdiFrameSendingLoop(pNdiSender, _ffmpegProcess.StandardOutput.BaseStream, token), token);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NdiWorker] Note: FFmpeg launch status: {ex.Message}");
            }
        }

        private static void RunNdiFrameSendingLoop(IntPtr pNdiSender, Stream rawStream, CancellationToken token)
        {
            var resParts = Resolution.Split('x');
            int xres = int.TryParse(resParts[0], out var w) ? w : 1920;
            int yres = resParts.Length > 1 && int.TryParse(resParts[1], out var h) ? h : 1080;
            int frameRateNumerator = int.TryParse(Fps, out var f) ? f : 30;
            int frameRateDenominator = 1;
            int strideBytes = xres * 4;
            int frameSizeBytes = strideBytes * yres;

            byte[] buffer = new byte[frameSizeBytes];

            Console.WriteLine($"[NdiWorker] NDI Frame Sending Loop started ({xres}x{yres} BGRA @ {frameRateNumerator}fps, frame size: {frameSizeBytes} bytes)");

            while (!token.IsCancellationRequested)
            {
                int bytesRead = 0;
                while (bytesRead < frameSizeBytes && !token.IsCancellationRequested)
                {
                    int n = rawStream.Read(buffer, bytesRead, frameSizeBytes - bytesRead);
                    if (n <= 0) break;
                    bytesRead += n;
                }

                if (bytesRead < frameSizeBytes)
                {
                    Console.WriteLine("[NdiWorker] Stream ended or read incomplete frame, exiting NDI loop.");
                    break;
                }

                unsafe
                {
                    fixed (byte* pData = buffer)
                    {
                        var videoFrame = new NdiNative.NDIlib_video_frame_v2_t
                        {
                            xres = xres,
                            yres = yres,
                            FourCC = NdiNative.NDIlib_FourCC_video_type_e.NDIlib_FourCC_video_type_BGRA,
                            frame_rate_N = frameRateNumerator,
                            frame_rate_D = frameRateDenominator,
                            picture_aspect_ratio = (float)xres / yres,
                            frame_format_type = NdiNative.NDIlib_frame_format_type_e.NDIlib_frame_format_type_progressive,
                            timecode = 9223372036854775807L, // NDIlib_send_timecode_synthesize (int64_max)
                            p_data = (IntPtr)pData,
                            line_stride_in_bytes = strideBytes,
                            p_metadata = IntPtr.Zero,
                            timestamp = 0
                        };

                        NdiNative.NDIlib_send_send_video_v2(pNdiSender, ref videoFrame);
                    }
                }
            }
        }

        private static void ConfigureNdiDiscovery()
        {
            if (DiscoveryMode.Equals("CentralServer", StringComparison.OrdinalIgnoreCase))
            {
                string iniContent = $"[Network]\ndiscovery={DiscoveryServerIp}:{DiscoveryServerPort}\nnic_ip={WorkerIp}\nip_address={WorkerIp}\n";
                Console.WriteLine($"[NdiWorker] Configuring NDI Discovery Central Server: {DiscoveryServerIp}:{DiscoveryServerPort} for Worker IP: {WorkerIp}");
                try
                {
                    Directory.CreateDirectory("/etc/ndi");
                    File.WriteAllText("/etc/ndi/ndi.ini", iniContent);
                }
                catch
                {
                    try
                    {
                        File.WriteAllText("ndi.ini", iniContent);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[NdiWorker] Could not write NDI ini file: {ex.Message}");
                    }
                }
            }
            else
            {
                Console.WriteLine("[NdiWorker] NDI Discovery configured for mDNS/Bonjour multicast.");
            }
        }

        private static void StopFfmpegPipeline()
        {
            if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
            {
                try
                {
                    _ffmpegProcess.Kill();
                }
                catch { }
            }
        }

        private static async Task RunHealthServerAsync(int port, CancellationToken token)
        {
            try
            {
                using var listener = new HttpListener();
                listener.Prefixes.Add($"http://*:{port}/");
                listener.Start();
                Console.WriteLine($"[NdiWorker] Health HTTP listener listening on port {port}");

                while (!token.IsCancellationRequested)
                {
                    var contextTask = listener.GetContextAsync();
                    var completedTask = await Task.WhenAny(contextTask, Task.Delay(-1, token));

                    if (completedTask == contextTask)
                    {
                        var context = await contextTask;
                        ProcessHealthRequest(context);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NdiWorker] Health server exception: {ex.Message}");
            }
        }

        private static void ProcessHealthRequest(HttpListenerContext context)
        {
            try
            {
                var req = context.Request;
                var resp = context.Response;

                if (req.Url?.AbsolutePath == "/tally" && req.HttpMethod == "POST")
                {
                    using var reader = new StreamReader(req.InputStream);
                    string body = reader.ReadToEnd();
                    if (body.Contains("Program")) TallyState = "Program";
                    else if (body.Contains("Preview")) TallyState = "Preview";
                    else if (body.Contains("Off")) TallyState = "Off";

                    resp.StatusCode = 200;
                    byte[] buf = Encoding.UTF8.GetBytes($"{{\"status\":\"ok\",\"tally_state\":\"{TallyState}\"}}");
                    resp.OutputStream.Write(buf, 0, buf.Length);
                    resp.OutputStream.Close();
                    return;
                }

                if (req.Url?.AbsolutePath == "/" || req.Url?.AbsolutePath == "/health" || req.Url?.AbsolutePath == "/status")
                {
                    bool isAlive = _ffmpegProcess != null && !_ffmpegProcess.HasExited;
                    var currentProc = Process.GetCurrentProcess();

                    var healthObj = new
                    {
                        status = "healthy",
                        stream_name = StreamName,
                        source_type = SourceType,
                        source_uri = SourceUri,
                        resolution = Resolution,
                        fps = Fps,
                        pattern = Pattern,
                        discovery_mode = DiscoveryMode,
                        discovery_server = DiscoveryMode == "CentralServer" ? $"{DiscoveryServerIp}:{DiscoveryServerPort}" : "mDNS/Bonjour",
                        tally_state = TallyState,
                        umd_text = UmdText,
                        audio_freq_hz = AudioFreq,
                        destination = $"udp://{DestIp}:{DestPort}",
                        uptime_seconds = Math.Round((DateTime.UtcNow - StartTime).TotalSeconds, 2),
                        memory_mb = Math.Round((double)currentProc.WorkingSet64 / (1024 * 1024), 2),
                        ffmpeg_running = isAlive
                    };

                    string json = JsonSerializer.Serialize(healthObj, new JsonSerializerOptions { WriteIndented = true });
                    byte[] buf = Encoding.UTF8.GetBytes(json);

                    resp.ContentType = "application/json";
                    resp.StatusCode = 200;
                    resp.ContentLength64 = buf.Length;
                    resp.OutputStream.Write(buf, 0, buf.Length);
                }
                else
                {
                    resp.StatusCode = 404;
                }
                resp.OutputStream.Close();
            }
            catch { }
        }
    }
}
