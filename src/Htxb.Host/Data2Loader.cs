using System.IO;

namespace Htxb.Host;

/// <summary>
/// Resolves and serves files from the shared <c>data2</c> runtime folder.
/// The folder lives next to the EXE (AppContext.BaseDirectory\data2) and is
/// shared by every tool EXE. HTML stays embedded in each EXE; only libraries,
/// assets and the manifest are read from disk through this loader.
/// </summary>
public sealed class Data2Loader
{
    private readonly string _data2Root;

    public Data2Loader(string? data2Root = null)
    {
        _data2Root = Path.GetFullPath(data2Root ?? Path.Combine(AppContext.BaseDirectory, "data2"));
    }

    public string Root => _data2Root;

    public bool Exists => Directory.Exists(_data2Root);

    public string Resolve(string relativePath)
    {
        string full = Path.GetFullPath(Path.Combine(_data2Root, relativePath));
        string root = _data2Root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Path escapes data2 root.", nameof(relativePath));
        return full;
    }

    public bool FileExists(string relativePath) => File.Exists(Resolve(relativePath));

    public byte[]? ReadBytes(string relativePath)
    {
        string path = Resolve(relativePath);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    public string? ReadText(string relativePath)
    {
        string path = Resolve(relativePath);
        return File.Exists(path) ? File.ReadAllText(path) : null;
    }

    public static string MimeFor(string relativePath)
    {
        string ext = Path.GetExtension(relativePath).ToLowerInvariant();
        return ext switch
        {
            ".js" or ".mjs" => "text/javascript",
            ".json" => "application/json",
            ".wasm" => "application/wasm",
            ".html" or ".htm" => "text/html; charset=utf-8",
            ".css" => "text/css",
            ".png" => "image/png",
            ".ico" => "image/x-icon",
            ".svg" => "image/svg+xml",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".woff" or ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            _ => "application/octet-stream",
        };
    }
}