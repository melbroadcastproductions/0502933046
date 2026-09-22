using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WebToNdi
{
    /// <summary>
    /// Tiny built-in HTTP server (System.Net.HttpListener, no extra dependencies)
    /// so the otherwise-invisible capture process can be checked on and controlled:
    /// GET /        - HTML dashboard
    /// GET /status  - JSON status
    /// POST /navigate (form field "url") - change the page being captured
    /// POST /exit   - shuts the app down
    /// </summary>
    internal sealed class ManagementServer
    {
        private readonly HttpListener _listener = new();
        private readonly Action<string> _onNavigate;
        private readonly Func<CaptureStatus> _getStatus;
        private readonly Action _onExitRequested;
        private CancellationTokenSource? _cts;

        public ManagementServer(string host, int port, Action<string> onNavigate, Func<CaptureStatus> getStatus, Action onExitRequested)
        {
            _onNavigate = onNavigate;
            _getStatus = getStatus;
            _onExitRequested = onExitRequested;
            _listener.Prefixes.Add($"http://{host}:{port}/");
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _listener.Start();
            _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            _cts?.Cancel();
            try { _listener.Stop(); } catch { /* already stopped */ }
        }

        private async Task AcceptLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _listener.GetContextAsync();
                }
                catch
                {
                    break; // listener was stopped
                }

                _ = Task.Run(() => HandleAsync(ctx));
            }
        }

        private async Task HandleAsync(HttpListenerContext ctx)
        {
            try
            {
                var req = ctx.Request;
                var res = ctx.Response;
                var path = req.Url?.AbsolutePath ?? "/";

                if (req.HttpMethod == "GET" && path == "/")
                {
                    await WriteAsync(res, "text/html; charset=utf-8", RenderDashboard(_getStatus()));
                }
                else if (req.HttpMethod == "GET" && path == "/status")
                {
                    await WriteAsync(res, "application/json; charset=utf-8", JsonSerializer.Serialize(_getStatus()));
                }
                else if (req.HttpMethod == "POST" && path == "/navigate")
                {
                    using var reader = new StreamReader(req.InputStream);
                    var body = await reader.ReadToEndAsync();
                    var url = ParseFormValue(body, "url");
                    if (!string.IsNullOrWhiteSpace(url))
                        _onNavigate(url);
                    Redirect(res, "/");
                }
                else if (req.HttpMethod == "POST" && path == "/exit")
                {
                    await WriteAsync(res, "text/html; charset=utf-8", "<html><body>Shutting down&hellip;</body></html>");
                    _onExitRequested();
                }
                else
                {
                    res.StatusCode = 404;
                    res.Close();
                }
            }
            catch
            {
                try { ctx.Response.Close(); } catch { /* connection already gone */ }
            }
        }

        private static string? ParseFormValue(string body, string key)
        {
            foreach (var pair in body.Split('&'))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length == 2 && Uri.UnescapeDataString(kv[0]) == key)
                    return Uri.UnescapeDataString(kv[1].Replace("+", "%20"));
            }
            return null;
        }

        private static void Redirect(HttpListenerResponse res, string location)
        {
            res.StatusCode = 302;
            res.RedirectLocation = location;
            res.Close();
        }

        private static async Task WriteAsync(HttpListenerResponse res, string contentType, string body)
        {
            var bytes = Encoding.UTF8.GetBytes(body);
            res.ContentType = contentType;
            res.ContentLength64 = bytes.Length;
            await res.OutputStream.WriteAsync(bytes);
            res.Close();
        }

        private static string RenderDashboard(CaptureStatus status) => """
            <html>
            <head>
            <meta charset="utf-8" />
            <title>WebToNdi</title>
            <style>
              body {{font-family:Segoe UI, sans-serif; margin: 2rem; max-width: 480px; color: #222; }}
              h1 {{font-size: 1.3rem; }}
              label {{display: block; margin-top: 1rem; font-weight: 600; }}
              input[type=text] {{ width: 100%; padding: 0.4rem; box-sizing: border-box; }}
              button {{ margin-top: 1rem; padding: 0.5rem 1rem; }}
              .exit {{ background: #b00020; color: white; border: none; }}
              dl {{ display: grid; grid-template-columns: auto 1fr; gap: 0.25rem 1rem; }}
              dt {{ color: #666; }}
            </style>
            </head>
            <body>
              <h1>WebToNdi &mdash; {status.NdiName}</h1>
              <dl>
                <dt>Current URL</dt><dd>{status.Url}</dd>
                <dt>Resolution</dt><dd>{status.Width}x{status.Height}</dd>
                <dt>Target FPS</dt><dd>{status.TargetFps}</dd>
                <dt>Measured FPS</dt><dd>{status.MeasuredFps}</dd>
              </dl>
              <form method="post" action="/navigate">
                <label for="url">Navigate to a new page</label>
                <input type="text" id="url" name="url" placeholder="https://..." />
                <button type="submit">Go</button>
              </form>
              <p style="color:#666; font-size:0.9rem;">
                Resolution, FPS, NDI name and other settings live in
                <code>config.json</code> next to WebToNdi.exe &mdash; edit that
                and restart the app to change them.
              </p>
              <form method="post" action="/exit" onsubmit="return confirm('Exit WebToNdi?');">
                <button type="submit" class="exit">Exit app</button>
              </form>
            </body>
            </html>
            """;
    }
}
