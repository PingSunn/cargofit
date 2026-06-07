using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;

namespace CargoFit;

/// <summary>
/// Tiny in-process static-file server for the web product editor (CargoFit/WebEditor/*).
/// Serves the bundled HTML/JS/CSS from embedded resources over http://127.0.0.1:&lt;port&gt; so the
/// page runs in a *secure context* — required for the browser File System Access API (which is
/// disabled on file://). The server has no app logic and never touches products.json; the browser
/// reads/writes the file directly via FS Access. Binds 127.0.0.1 (loopback only) on a FIXED port so
/// the origin is stable and the persisted file handle/permission survive across sessions.
/// </summary>
internal static class WebEditorServer
{
    private const int Port = 8753;          // fixed → stable origin for persisted FS-Access handle
    private static HttpListener? _listener;
    private static readonly object Gate = new();

    internal static string Url => $"http://127.0.0.1:{Port}/";

    // request path → (embedded resource logical name, content type)
    private static readonly Dictionary<string, (string Res, string Mime)> Routes = new()
    {
        ["/"]                  = ("CargoFit.WebEditor.index.html",          "text/html; charset=utf-8"),
        ["/index.html"]        = ("CargoFit.WebEditor.index.html",          "text/html; charset=utf-8"),
        ["/app.js"]            = ("CargoFit.WebEditor.app.js",              "application/javascript; charset=utf-8"),
        ["/styles.css"]        = ("CargoFit.WebEditor.styles.css",          "text/css; charset=utf-8"),
        ["/vendor/three.min.js"] = ("CargoFit.WebEditor.vendor.three.min.js", "application/javascript; charset=utf-8"),
    };

    internal static void EnsureStarted()
    {
        lock (Gate)
        {
            if (_listener is { IsListening: true }) return;
            var listener = new HttpListener();
            listener.Prefixes.Add(Url);
            listener.Start();
            _listener = listener;
            _ = Task.Run(() => Loop(listener));
        }
    }

    private static async Task Loop(HttpListener listener)
    {
        while (listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync(); }
            catch { break; } // listener stopped/disposed
            _ = Task.Run(() => Handle(ctx));
        }
    }

    private static void Handle(HttpListenerContext ctx)
    {
        try
        {
            var path = ctx.Request.Url?.AbsolutePath ?? "/";
            if (!Routes.TryGetValue(path, out var route))
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return;
            }

            using var stream = typeof(WebEditorServer).Assembly.GetManifestResourceStream(route.Res);
            if (stream is null)
            {
                ctx.Response.StatusCode = 404;
                ctx.Response.Close();
                return;
            }

            ctx.Response.ContentType = route.Mime;
            ctx.Response.Headers["Cache-Control"] = "no-store";
            stream.CopyTo(ctx.Response.OutputStream);
            ctx.Response.OutputStream.Close();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[WebEditorServer.Handle] {ex}");
            try { ctx.Response.Abort(); } catch { /* ignore */ }
        }
    }

    internal static void Stop()
    {
        lock (Gate)
        {
            try { _listener?.Stop(); _listener?.Close(); } catch { /* ignore */ }
            _listener = null;
        }
    }

    /// <summary>Starts the server (idempotent) and opens the default browser at the editor URL.</summary>
    internal static void OpenInBrowser()
    {
        EnsureStarted();
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo(Url) { UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", Url);
            else
                Process.Start("xdg-open", Url);
        }
        catch (Exception ex) { Debug.WriteLine($"[WebEditorServer.Open] {ex}"); }
    }
}
