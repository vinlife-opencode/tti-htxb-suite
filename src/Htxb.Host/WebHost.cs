using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Web.WebView2.Core;

namespace Htxb.Host;

/// <summary>
/// Virtual-host resource server. Embeds the tool HTML (protected), serves the
/// shared <c>data2</c> libraries/assets from disk, and injects per-tool
/// polyfill scripts. URL scheme: <c>https://app/</c>.
///
/// Routing:
///   /              -> embedded index.html (rewritten + polyfill-injected)
///   /polyfill.js   -> embedded polyfill script
///   /favicon.ico   -> data2\assets\Favicon.ico
///   /manifest.json -> data2\manifest.json
///   /libs/*        -> data2\lib\*
///   /assets/*      -> data2\assets\*
/// </summary>
public static class WebHost
{
    public const string BaseUrl = "https://app";

    public static void Attach(CoreWebView2 core, Data2Loader data2, HostOptions options)
    {
        core.AddWebResourceRequestedFilter($"{BaseUrl}/*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += (_, e) => Serve(e, core, data2, options);
    }

    private static void Serve(CoreWebView2WebResourceRequestedEventArgs e,
        CoreWebView2 core, Data2Loader data2, HostOptions options)
    {
        string path = Uri.UnescapeDataString(new Uri(e.Request.Uri).AbsolutePath)
            .TrimStart('/');

        switch (path)
        {
            case "":
            case "index.html":
                ServeIndex(e, core, data2, options);
                return;

            case "polyfill.js":
                ServeEmbedded(e, core, options.Assembly, options.PolyfillResourceName, "text/javascript");
                return;

            case "favicon.ico":
                ServeData2(e, core, data2, "assets/Favicon.ico");
                return;

            case "manifest.json":
                ServeData2(e, core, data2, "manifest.json");
                return;
        }

        if (path.StartsWith("libs/", StringComparison.OrdinalIgnoreCase))
        {
            ServeData2(e, core, data2, "lib/" + path["libs/".Length..]);
            return;
        }

        if (path.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
        {
            string relative = "assets/" + path["assets/".Length..];
            // DUT package images live in data2\assets\dut_package and may be
            // .png, .jpg or .jpeg; fall back across those extensions so the
            // page can request any of them regardless of the real file type.
            if (relative.StartsWith("assets/dut_package/", StringComparison.OrdinalIgnoreCase))
            {
                ServeDutPackageAsset(e, core, data2, relative);
                return;
            }
            ServeData2(e, core, data2, relative);
            return;
        }

        NotFound(e, core);
    }

    private static void ServeIndex(CoreWebView2WebResourceRequestedEventArgs e,
        CoreWebView2 core, Data2Loader data2, HostOptions options)
    {
        using var stream = options.Assembly.GetManifestResourceStream(options.HtmlResourceName);
        if (stream is null)
        {
            NotFound(e, core);
            return;
        }

        string html = new StreamReader(stream, Encoding.UTF8).ReadToEnd();

        if (options.Rewrites is not null)
        {
            foreach (var (from, to) in options.Rewrites)
                html = html.Replace(from, to);
        }

        if (!string.IsNullOrEmpty(options.PolyfillResourceName)
            && options.Assembly.GetManifestResourceStream(options.PolyfillResourceName) is not null)
        {
            // Expose the COM bridge under the name the tool shims expect.
            // WebView2 hosts objects at chrome.webview.hostObjects.{name} (async
            // proxies); the .sync container gives synchronous proxies, which is
            // what the polyfills require (they read return values directly).
            html = html.Replace(
                "<head>",
                "<head>\n<script>window.tools = window.chrome.webview.hostObjects.sync.tools;</script>"
                + "\n<script src=\"/polyfill.js\"></script>",
                StringComparison.OrdinalIgnoreCase);
        }

        var bytes = Encoding.UTF8.GetBytes(html);
        e.Response = core.Environment.CreateWebResourceResponse(
            new MemoryStream(bytes), 200, "OK", "Content-Type: text/html; charset=utf-8");
    }

    private static void ServeData2(CoreWebView2WebResourceRequestedEventArgs e,
        CoreWebView2 core, Data2Loader data2, string relativePath)
    {
        byte[]? bytes = data2.ReadBytes(relativePath);
        if (bytes is null)
        {
            NotFound(e, core);
            return;
        }

        e.Response = core.Environment.CreateWebResourceResponse(
            new MemoryStream(bytes), 200, "OK",
            $"Content-Type: {Data2Loader.MimeFor(relativePath)}");
    }

    /// <summary>
    /// Serves a DUT package image from data2\assets\dut_package. The requested
    /// file may name any of .png/.jpg/.jpeg; the actual file on disk may use any
    /// of those extensions, so try the requested path first then the rest.
    /// </summary>
    private static void ServeDutPackageAsset(CoreWebView2WebResourceRequestedEventArgs e,
        CoreWebView2 core, Data2Loader data2, string relativePath)
    {
        string bare = relativePath.Substring(0, relativePath.LastIndexOf('.'));
        foreach (string ext in new[] { ".png", ".jpg", ".jpeg" })
        {
            string candidate = bare + ext;
            if (data2.FileExists(candidate))
            {
                ServeData2(e, core, data2, candidate);
                return;
            }
        }
        NotFound(e, core);
    }

    private static void ServeEmbedded(CoreWebView2WebResourceRequestedEventArgs e,
        CoreWebView2 core, Assembly asm, string? resourceName, string mime)
    {
        if (string.IsNullOrEmpty(resourceName))
        {
            NotFound(e, core);
            return;
        }

        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            NotFound(e, core);
            return;
        }

        var ms = new MemoryStream();
        stream.CopyTo(ms);
        ms.Position = 0;
        e.Response = core.Environment.CreateWebResourceResponse(ms, 200, "OK", $"Content-Type: {mime}");
    }

    private static void NotFound(CoreWebView2WebResourceRequestedEventArgs e, CoreWebView2 core)
        => e.Response = core.Environment.CreateWebResourceResponse(
            new MemoryStream(Array.Empty<byte>()), 404, "Not Found", "Content-Type: text/plain");
}