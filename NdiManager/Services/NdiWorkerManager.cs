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
                string targetHost = string.IsNullOrEmpty(w.Config.DestIp) || w.Config.DestIp.StartsWith("239.") ? "10.10.1.1" : w.Config.DestIp;
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

        private async Task<WorkerResponse> FormatContainerWorkerAsync(ContainerListResponse c)
        {
            var config = new WorkerConfig
            {
                StreamName = c.Names.FirstOrDefault()?.TrimStart('/') ?? c.ID,
            };

            bool isRunning = c.State == "running";
            string targetHost = string.IsNullOrEmpty(config.DestIp) || config.DestIp.StartsWith("239.") ? "10.10.1.1" : config.DestIp;
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
            string id = $"ndi-{Guid.NewGuid().ToString("N")[..8]}";
            string containerName = $"ndi-worker-{config.StreamName.ToLower().Replace(' ', '-')}-{Guid.NewGuid().ToString("N")[..4]}";

            if (_dockerAvailable && _dockerClient != null)
            {
                try
                {
                    var response = await _dockerClient.Containers.CreateContainerAsync(new CreateContainerParameters
                    {
                        Image = "ndi-worker:latest",
                        Name = containerName,
                        Labels = new Dictionary<string, string> { ["type"] = "ndi-worker" },
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
                        HostConfig = new HostConfig { NetworkMode = "host" }
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
