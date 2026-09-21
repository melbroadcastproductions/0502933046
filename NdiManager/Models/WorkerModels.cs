using System.Text.Json.Serialization;

namespace NdiManager.Models
{
    public class WorkerConfig
    {
        [JsonPropertyName("stream_name")]
        public string StreamName { get; set; } = "NDI-Stream-01";

        [JsonPropertyName("source_type")]
        public string SourceType { get; set; } = "ColorBars"; // ColorBars, Picture, VideoClip, WebPage

        [JsonPropertyName("source_uri")]
        public string SourceUri { get; set; } = "";

        [JsonPropertyName("resolution")]
        public string Resolution { get; set; } = "1920x1080";

        [JsonPropertyName("fps")]
        public string Fps { get; set; } = "30";

        [JsonPropertyName("pattern")]
        public string Pattern { get; set; } = "smptebars";

        [JsonPropertyName("audio_freq")]
        public int AudioFreq { get; set; } = 1000;

        [JsonPropertyName("overlay_text")]
        public string OverlayText { get; set; } = "LIVE FINANCIAL NDI";

        [JsonPropertyName("dest_ip")]
        public string DestIp { get; set; } = "239.255.0.1";

        [JsonPropertyName("dest_port")]
        public int DestPort { get; set; } = 5004;

        [JsonPropertyName("health_port")]
        public int HealthPort { get; set; } = 8080;

        [JsonPropertyName("discovery_mode")]
        public string DiscoveryMode { get; set; } = "Bonjour"; // Bonjour or CentralServer

        [JsonPropertyName("discovery_server_ip")]
        public string DiscoveryServerIp { get; set; } = "127.0.0.1";

        [JsonPropertyName("discovery_server_port")]
        public int DiscoveryServerPort { get; set; } = 5959;

        [JsonPropertyName("tally_state")]
        public string TallyState { get; set; } = "Off"; // Off, Program, Preview

        [JsonPropertyName("umd_text")]
        public string UmdText { get; set; } = "CAM 1";
    }

    public class WorkerResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("status")]
        public string Status { get; set; } = "running";

        [JsonPropertyName("image")]
        public string Image { get; set; } = "ndi-worker:latest";

        [JsonPropertyName("created_at")]
        public string CreatedAt { get; set; } = "";

        [JsonPropertyName("mode")]
        public string Mode { get; set; } = "docker";

        [JsonPropertyName("config")]
        public WorkerConfig Config { get; set; } = new WorkerConfig();

        [JsonPropertyName("health")]
        public object? Health { get; set; }
    }

    public class WorkerActionResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = "";

        [JsonPropertyName("worker_id")]
        public string WorkerId { get; set; } = "";
    }

    public class SystemStatusResponse
    {
        [JsonPropertyName("docker_available")]
        public bool DockerAvailable { get; set; }

        [JsonPropertyName("active_mode")]
        public string ActiveMode { get; set; } = "process";

        [JsonPropertyName("total_workers")]
        public int TotalWorkers { get; set; }

        [JsonPropertyName("running_workers")]
        public int RunningWorkers { get; set; }

        [JsonPropertyName("stopped_workers")]
        public int StoppedWorkers { get; set; }

        [JsonPropertyName("cpu_usage_percent")]
        public double CpuUsagePercent { get; set; }

        [JsonPropertyName("memory_usage_mb")]
        public double MemoryUsageMb { get; set; }
    }
}
