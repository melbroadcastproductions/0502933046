using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WebToNdi
{
    /// <summary>
    /// All user-facing settings, loaded from (and, for the URL, saved back to)
    /// config.json sitting next to the .exe — no command-line flags or
    /// environment variables required. If config.json doesn't exist yet, a
    /// default one is written out so there's something to edit.
    /// </summary>
    internal sealed class AppConfig
    {
        public string Url { get; set; } = "https://example.com";
        public int Width { get; set; } = 1920;
        public int Height { get; set; } = 1080;
        public int Fps { get; set; } = 30;
        public string NdiName { get; set; } = "Web Page";

        /// <summary>
        /// Optional NDI Discovery Server address ("host" or "host:port"). Leave
        /// blank/null to just use normal mDNS LAN discovery — that's fine for
        /// almost everyone.
        /// </summary>
        public string? DiscoveryServer { get; set; }

        /// <summary>
        /// "127.0.0.1" = only this machine can open the dashboard, no extra
        /// setup needed. Use "0.0.0.0" to allow other machines on the network
        /// in — see README for the one-time Windows command that requires.
        /// </summary>
        public string ManagementHost { get; set; } = "127.0.0.1";

        public int ManagementPort { get; set; } = 8080;

        [JsonIgnore]
        public string ConfigFilePath { get; private set; } = string.Empty;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public static AppConfig Load()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "config.json");
            AppConfig config;

            if (!File.Exists(path))
            {
                config = new AppConfig { ConfigFilePath = path };
                config.Save(); // write the defaults out so there's a file to edit
                return config;
            }

            try
            {
                var json = File.ReadAllText(path);
                config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? new AppConfig();
            }
            catch
            {
                // Malformed config.json shouldn't stop the app from starting —
                // fall back to defaults rather than crash on launch.
                config = new AppConfig();
            }

            config.ConfigFilePath = path;
            return config;
        }

        /// <summary>Best-effort write-back (e.g. after a dashboard navigation). Never throws.</summary>
        public void Save()
        {
            try
            {
                File.WriteAllText(ConfigFilePath, JsonSerializer.Serialize(this, JsonOptions));
            }
            catch
            {
                // e.g. read-only folder — app keeps running with in-memory settings.
            }
        }
    }
}
