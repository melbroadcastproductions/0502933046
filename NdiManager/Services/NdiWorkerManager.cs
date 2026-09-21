using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet;
using Docker.DotNet.Models;
using NdiManager.Models;

namespace NdiManager.Services
{
    public class NdiWorkerManager : INdiWorkerManager
    {
        private readonly DockerClient? _dockerClient;
        private readonly bool _dockerAvailable;
        private readonly HttpClient _httpClient;

        private class ProcessWorkerState
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public WorkerConfig Config { get; set; } = new WorkerConfig();
            public Process? Process { get; set; }
            public string LogFilePath { get; set; } = "";
            public DateTime CreatedAt { get; set; }
        }

        private static readonly ConcurrentDictionary<string, ProcessWorkerState> _processWorkers = new();
        private static readonly object _logLock = new object();

        private static void AppendLog(string filePath, string text)
        {
            try
            {
                lock (_logLock)
                {
                    File.AppendAllText(filePath, text + "\n");
                }
            }
            catch { }
        }

        public NdiWorkerManager()
        {
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            try
            {
                var dockerUri = OperatingSystem.IsWindows()
                    ? new Uri("npipe://./pipe/docker_engine")
                    : new Uri("unix:///var/run/docker.sock");

                _dockerClient = new DockerClientConfiguration(dockerUri).CreateClient();
                _dockerClient.System.PingAsync().GetAwaiter().GetResult();
                _dockerAvailable = true;
            }
            catch
            {
                _dockerAvailable = false;
                _dockerClient = null;
            }
        }

        public async Task<List<WorkerResponse>> ListWorkersAsync()
        {
            var result = new List<WorkerResponse>();

            if (_dockerAvailable && _dockerClient != null)
            {
                try
                {
                    var containers = await _dockerClient.Containers.ListContainersAsync(new ContainersListParameters
                    {
                        All = true,
                        Filters = new Dictionary<string, IDictionary<string, bool>>
                        {
                            ["label"] = new Dictionary<string, bool> { ["type=ndi-worker"] = true }
                        }
                    });

                    foreach (var c in containers)
                    {
                        result.Add(await FormatContainerWorkerAsync(c));
                    }
                }
                catch { }
            }

            // Always also include active process workers
            foreach (var kvp in _processWorkers)
            {
                var w = kvp.Value;
                bool isRunning = w.Process != null && !w.Process.HasExited;
                string targetHost = string.IsNullOrEmpty(w.Config.DestIp) || w.Config.DestIp.StartsWith("239.") ? "10.10.1.2" : w.Config.DestIp;
                object? health = isRunning ? await FetchHealthAsync(w.Config.HealthPort, targetHost) : null;

                result.Add(new WorkerResponse
                {
                    Id = w.Id,
                    Name = w.Name,
                    Status = isRunning ? "running" : "stopped",
                    Image = "ndi-worker:latest",
                    CreatedAt = w.CreatedAt.ToString("s"),
                    Mode = "process",
                    Config = w.Config,
                    Health = health
                });
            }

            return result;
        }

        private static string GetLabel(IDictionary<string, string> dict, string key, string fallback = "")
        {
            return dict != null && dict.TryGetValue(key, out var val) ? val : fallback;
        }

        private async Task<WorkerResponse> FormatContainerWorkerAsync(ContainerListResponse c)
        {
            var labels = c.Labels ?? new Dictionary<string, string>();
            string fallbackName = c.Names.FirstOrDefault()?.TrimStart('/') ?? c.ID;
            string streamName = GetLabel(labels, "stream_name", fallbackName);

            var config = new WorkerConfig
            {
                StreamName = streamName,
                SourceType = GetLabel(labels, "source_type", "ColorBars"),
                SourceUri = GetLabel(labels, "source_uri", ""),
                Resolution = GetLabel(labels, "resolution", "1920x1080"),
                Fps = GetLabel(labels, "fps", "30"),
                Pattern = GetLabel(labels, "pattern", "smptebars"),
                AudioFreq = int.TryParse(GetLabel(labels, "audio_freq", "1000"), out var af) ? af : 1000,
                OverlayText = GetLabel(labels, "overlay_text", "LIVE FINANCIAL NDI"),
                DestIp = GetLabel(labels, "dest_ip", "239.255.0.1"),
                DestPort = int.TryParse(GetLabel(labels, "dest_port", "5004"), out var dp) ? dp : 5004,
                HealthPort = int.TryParse(GetLabel(labels, "health_port", "8080"), out var hp) ? hp : 8080,
                DiscoveryMode = GetLabel(labels, "discovery_mode", "Bonjour"),
                DiscoveryServerIp = GetLabel(labels, "discovery_server_ip", "10.10.1.1"),
                DiscoveryServerPort = int.TryParse(GetLabel(labels, "discovery_server_port", "5959"), out var dsp) ? dsp : 5959,
                TallyState = GetLabel(labels, "tally_state", "Off"),
                UmdText = GetLabel(labels, "umd_text", streamName)
            };

            bool isRunning = c.State == "running";
            string targetHost = string.IsNullOrEmpty(config.DestIp) || config.DestIp.StartsWith("239.") ? "10.10.1.10" : config.DestIp;
            object? health = isRunning ? await FetchHealthAsync(config.HealthPort, targetHost) : null;

            return new WorkerResponse
            {
                Id = c.ID.Length >= 12 ? c.ID.Substring(0, 12) : c.ID,
                Name = c.Names.FirstOrDefault()?.TrimStart('/') ?? c.ID,
                Status = isRunning ? "running" : "stopped",
                Image = c.Image,
                CreatedAt = c.Created.ToString("s"),
                Mode = "docker",
                Config = config,
                Health = health
            };
        }

        private async Task<object?> FetchHealthAsync(int port, string host = "10.10.1.1")
        {
            try {
                var json = await _httpClient.GetStringAsync($"http://{host}:{port}/health");
                return JsonSerializer.Deserialize<object>(json);
            }
            catch {
                if (host != "127.0.0.1")
                {
                    try {
                        var json = await _httpClient.GetStringAsync($"http://127.0.0.1:{port}/health");
                        return JsonSerializer.Deserialize<object>(json);
                    }
                    catch { return null; }
                }
                return null;
            }
        }

        public async Task<WorkerResponse?> GetWorkerAsync(string workerId)
        {
            var workers = await ListWorkersAsync();
            return workers.FirstOrDefault(w => w.Id == workerId || w.Name == workerId);
        }

        public async Task<WorkerResponse> CreateWorkerAsync(WorkerConfig config)
        {
            var existingWorkers = await ListWorkersAsync();

            // Auto-increment health_port if default or colliding
            int usedHealthPort = config.HealthPort;
            while (existingWorkers.Any(w => w.Config.HealthPort == usedHealthPort))
            {
                usedHealthPort++;
            }
            config.HealthPort = usedHealthPort;

            // Auto-increment dest_port if default or colliding
            int usedDestPort = config.DestPort;
            while (existingWorkers.Any(w => w.Config.DestPort == usedDestPort))
            {
                usedDestPort++;
            }
            config.DestPort = usedDestPort;

            string id = $"ndi-{Guid.NewGuid().ToString("N")[..8]}";
            string containerName = $"ndi-worker-{config.StreamName.ToLower().Replace(' ', '-')}-{Guid.NewGuid().ToString("N")[..4]}";

            if (_dockerAvailable && _dockerClient != null)
            {
                try
                {
                    var labels = new Dictionary<string, string>
                    {
                        ["type"] = "ndi-worker",
                        ["stream_name"] = config.StreamName,
                        ["source_type"] = config.SourceType,
                        ["source_uri"] = config.SourceUri,
                        ["resolution"] = config.Resolution,
                        ["fps"] = config.Fps,
                        ["pattern"] = config.Pattern,
                        ["audio_freq"] = config.AudioFreq.ToString(),
                        ["overlay_text"] = config.OverlayText,
                        ["dest_ip"] = config.DestIp,
                        ["dest_port"] = config.DestPort.ToString(),
                        ["health_port"] = config.HealthPort.ToString(),
                        ["discovery_mode"] = config.DiscoveryMode,
                        ["discovery_server_ip"] = config.DiscoveryServerIp,
                        ["discovery_server_port"] = config.DiscoveryServerPort.ToString(),
                        ["tally_state"] = config.TallyState,
                        ["umd_text"] = config.UmdText
                    };

                    // IP allocation starts at 10.10.1.10 up to 10.10.1.250 (reserving 10.10.1.1 exclusively for NDI Discovery Server)
                    int ipSuffix = 10 + (existingWorkers.Count % 240);
                    string assignedWorkerIp = $"10.10.1.{ipSuffix}";

                    var response = await _dockerClient.Containers.CreateContainerAsync(new CreateContainerParameters
                    {
                        Image = "ndi-worker:latest",
                        Name = containerName,
                        Labels = labels,
                        Env = new List<string>
                        {
                            $"STREAM_NAME={config.StreamName}",
                            $"SOURCE_TYPE={config.SourceType}",
                            $"SOURCE_URI={config.SourceUri}",
                            $"RESOLUTION={config.Resolution}",
                            $"FPS={config.Fps}",
                            $"PATTERN={config.Pattern}",
                            $"AUDIO_FREQ={config.AudioFreq}",
                            $"OVERLAY_TEXT={config.OverlayText}",
                            $"DEST_IP={config.DestIp}",
                            $"DEST_PORT={config.DestPort}",
                            $"HEALTH_PORT={config.HealthPort}",
                            $"DISCOVERY_MODE={config.DiscoveryMode}",
                            $"DISCOVERY_SERVER_IP={config.DiscoveryServerIp}",
                            $"DISCOVERY_SERVER_PORT={config.DiscoveryServerPort}",
                            $"TALLY_STATE={config.TallyState}",
                            $"UMD_TEXT={config.UmdText}"
                        },
                        NetworkingConfig = new NetworkingConfig
                        {
                            EndpointsConfig = new Dictionary<string, EndpointSettings>
                            {
                                ["ndi_net"] = new EndpointSettings
                                {
                                    IPAMConfig = new EndpointIPAMConfig
                                    {
                                        IPv4Address = assignedWorkerIp
                                    }
                                }
                            }
                        }
                    });

                    await _dockerClient.Containers.StartContainerAsync(response.ID, new ContainerStartParameters());
                    return new WorkerResponse
                    {
                        Id = response.ID[..12],
                        Name = containerName,
                        Status = "running",
                        Image = "ndi-worker:latest",
                        CreatedAt = DateTime.UtcNow.ToString("s"),
                        Mode = "docker",
                        Config = config
                    };
                }
                catch { }
            }

            // Fallback Process Runner
            string logPath = Path.Combine(Path.GetTempPath(), $"{id}.log");
            string rootDir = Directory.GetParent(Directory.GetCurrentDirectory())?.FullName ?? Directory.GetCurrentDirectory();
            string workerDll = Path.Combine(rootDir, "NdiWorker", "bin", "Debug", "net10.0", "NdiWorker.dll");
            if (!File.Exists(workerDll))
            {
                workerDll = Path.Combine(rootDir, "NdiWorker", "bin", "Release", "net10.0", "NdiWorker.dll");
            }
            if (!File.Exists(workerDll))
            {
                workerDll = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "NdiWorker.dll");
            }

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"\"{workerDll}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            psi.EnvironmentVariables["STREAM_NAME"] = config.StreamName;
            psi.EnvironmentVariables["SOURCE_TYPE"] = config.SourceType;
            psi.EnvironmentVariables["SOURCE_URI"] = config.SourceUri;
            psi.EnvironmentVariables["RESOLUTION"] = config.Resolution;
            psi.EnvironmentVariables["FPS"] = config.Fps;
            psi.EnvironmentVariables["PATTERN"] = config.Pattern;
            psi.EnvironmentVariables["AUDIO_FREQ"] = config.AudioFreq.ToString();
            psi.EnvironmentVariables["OVERLAY_TEXT"] = config.OverlayText;
            psi.EnvironmentVariables["DEST_IP"] = config.DestIp;
            psi.EnvironmentVariables["DEST_PORT"] = config.DestPort.ToString();
            psi.EnvironmentVariables["HEALTH_PORT"] = config.HealthPort.ToString();
            psi.EnvironmentVariables["DISCOVERY_MODE"] = config.DiscoveryMode;
            psi.EnvironmentVariables["DISCOVERY_SERVER_IP"] = config.DiscoveryServerIp;
            psi.EnvironmentVariables["DISCOVERY_SERVER_PORT"] = config.DiscoveryServerPort.ToString();
            psi.EnvironmentVariables["TALLY_STATE"] = config.TallyState;
            psi.EnvironmentVariables["UMD_TEXT"] = config.UmdText;

            var proc = Process.Start(psi);
            if (proc != null)
            {
                proc.OutputDataReceived += (s, e) => { if (e.Data != null) AppendLog(logPath, e.Data); };
                proc.ErrorDataReceived += (s, e) => { if (e.Data != null) AppendLog(logPath, e.Data); };
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
            }

            var state = new ProcessWorkerState
            {
                Id = id,
                Name = containerName,
                Config = config,
                Process = proc,
                LogFilePath = logPath,
                CreatedAt = DateTime.UtcNow
            };
            _processWorkers[id] = state;

            return new WorkerResponse
            {
                Id = id,
                Name = containerName,
                Status = "running",
                Image = "ndi-worker:latest",
                CreatedAt = state.CreatedAt.ToString("s"),
                Mode = "process",
                Config = config
            };
        }

        public async Task<bool> StartWorkerAsync(string workerId)
        {
            if (_dockerAvailable && _dockerClient != null)
            {
                try
                {
                    await _dockerClient.Containers.StartContainerAsync(workerId, new ContainerStartParameters());
                    return true;
                }
                catch { }
            }

            if (_processWorkers.TryGetValue(workerId, out var state))
            {
                if (state.Process == null || state.Process.HasExited)
                {
                    var created = await CreateWorkerAsync(state.Config);
                    state.Process = _processWorkers.GetValueOrDefault(created.Id)?.Process;
                    return true;
                }
            }
            return false;
        }

        public async Task<bool> StopWorkerAsync(string workerId)
        {
            if (_dockerAvailable && _dockerClient != null)
            {
                try
                {
                    await _dockerClient.Containers.StopContainerAsync(workerId, new ContainerStopParameters { WaitBeforeKillSeconds = 3 });
                    return true;
                }
                catch { }
            }

            if (_processWorkers.TryGetValue(workerId, out var state))
            {
                if (state.Process != null && !state.Process.HasExited)
                {
                    try { state.Process.Kill(); } catch { }
                }
                state.Process = null;
                return true;
            }
            return false;
        }

        public async Task<bool> RestartWorkerAsync(string workerId)
        {
            await StopWorkerAsync(workerId);
            await Task.Delay(500);
            return await StartWorkerAsync(workerId);
        }

        public async Task<bool> DeleteWorkerAsync(string workerId)
        {
            await StopWorkerAsync(workerId);
            if (_dockerAvailable && _dockerClient != null)
            {
                try
                {
                    await _dockerClient.Containers.RemoveContainerAsync(workerId, new ContainerRemoveParameters { Force = true });
                    return true;
                }
                catch { }
            }

            _processWorkers.TryRemove(workerId, out _);
            return true;
        }

        public async Task<string> GetWorkerLogsAsync(string workerId, int lines = 100)
        {
            if (_dockerAvailable && _dockerClient != null)
            {
                try
                {
                    var logStream = await _dockerClient.Containers.GetContainerLogsAsync(workerId, false, new ContainerLogsParameters
                    {
                        ShowStdout = true,
                        ShowStderr = true,
                        Tail = lines.ToString()
                    });
                    var (stdout, stderr) = await logStream.ReadOutputToEndAsync(CancellationToken.None);
                    return stdout + stderr;
                }
                catch { }
            }

            if (_processWorkers.TryGetValue(workerId, out var state) && File.Exists(state.LogFilePath))
            {
                string[] content;
                lock (_logLock)
                {
                    content = File.ReadAllLines(state.LogFilePath);
                }
                return string.Join("\n", content.TakeLast(lines));
            }

            return $"Logs for worker {workerId} not found.";
        }

        public async Task<SystemStatusResponse> GetSystemStatusAsync()
        {
            var workers = await ListWorkersAsync();
            int running = workers.Count(w => w.Status == "running");
            int stopped = workers.Count - running;
            var currentProc = Process.GetCurrentProcess();

            return new SystemStatusResponse
            {
                DockerAvailable = _dockerAvailable,
                ActiveMode = _dockerAvailable ? "docker" : "process",
                TotalWorkers = workers.Count,
                RunningWorkers = running,
                StoppedWorkers = stopped,
                CpuUsagePercent = 0.5,
                MemoryUsageMb = Math.Round((double)currentProc.WorkingSet64 / (1024 * 1024), 2)
            };
        }
    }
}
