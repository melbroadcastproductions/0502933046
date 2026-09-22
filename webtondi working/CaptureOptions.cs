namespace WebToNdi
{
    internal sealed class CaptureOptions
    {
        public string Url { get; init; } = "https://example.com";
        public int Width { get; init; } = 1920;
        public int Height { get; init; } = 1080;
        public int Fps { get; init; } = 30;
        public string NdiName { get; init; } = "Web Page";

        /// <summary>
        /// Parses: --url &lt;url&gt; --width &lt;n&gt; --height &lt;n&gt; --fps &lt;n&gt; --name &lt;ndi source name&gt;
        /// Any omitted flag falls back to the default above.
        /// </summary>
        public static CaptureOptions Parse(string[] args)
        {
            string? url = null, name = null;
            int? width = null, height = null, fps = null;

            for (var i = 0; i < args.Length - 1; i++)
            {
                switch (args[i].ToLowerInvariant())
                {
                    case "--url": url = args[++i]; break;
                    case "--width": width = int.Parse(args[++i]); break;
                    case "--height": height = int.Parse(args[++i]); break;
                    case "--fps": fps = int.Parse(args[++i]); break;
                    case "--name": name = args[++i]; break;
                }
            }

            return new CaptureOptions
            {
                Url = url ?? "https://example.com",
                Width = width ?? 1920,
                Height = height ?? 1080,
                Fps = fps ?? 30,
                NdiName = name ?? "Web Page"
            };
        }
    }
}
