using System.Drawing;
using System.Reflection;

namespace Htxb.Host;

/// <summary>
/// Per-tool configuration passed to a <see cref="HostForm"/>. Tool wrappers
/// only need to build one of these; the shared host does the rest.
/// </summary>
public sealed class HostOptions
{
    /// <summary>
    /// Env-var set by a launcher-style host when it spawns a sibling EXE:
    /// the pixel width the child should keep free on the left of the primary
    /// screen (launcher occupies that strip). Read by <see cref="HostForm"/>.
    /// </summary>
    public const string ReserveLeftEnvVar = "TTI_HTXB_RESERVE_LEFT_PX";

    /// <summary>Window title-bar text.</summary>
    public required string WindowTitle { get; init; }

    /// <summary>Stable id matching <c>data2\manifest.json</c> "tools" key, e.g. "lot-extractor".</summary>
    public string? ToolId { get; init; }

    /// <summary>Optional window size; defaults to 1280x900.</summary>
    public Size? WindowSize { get; init; }

    /// <summary>Optional minimum window size; defaults to 960x640.</summary>
    public Size? MinWindowSize { get; init; }

    /// <summary>
    /// Place the window at the screen's top-left corner instead of centering it
    /// (useful for slim launcher-style windows that must not cover tool windows).
    /// </summary>
    public bool StartAtScreenTopLeft { get; init; }

    /// <summary>
    /// Collapse the window into a small tab pinned at the top-left corner
    /// once loading completes (mouse-out dwell; focus not required);
    /// hovering/clicking the tab or activating the window expands it back
    /// to full size (for launcher-style windows that should stay out of the
    /// way until needed).
    /// </summary>
    public bool EnableEdgeAutoHide { get; init; }

    /// <summary>
    /// Never show a taskbar button for this window (resident launcher-style
    /// windows that live as a corner tab). Applied before the handle is
    /// created, so no handle recreation. Alt+Tab still lists the window.
    /// </summary>
    public bool HideTaskbarIcon { get; init; }

    /// <summary>Assembly containing the embedded resources (defaults to the entry assembly).</summary>
    public Assembly Assembly { get; init; } =
        Assembly.GetEntryAssembly() ?? typeof(HostForm).Assembly;

    /// <summary>Manifest resource name of the embedded index.html (default "index.html").</summary>
    public string HtmlResourceName { get; init; } = "index.html";

    /// <summary>Optional manifest resource name of a per-tool polyfill script (default none).</summary>
    public string? PolyfillResourceName { get; init; }

    /// <summary>
    /// String replacements applied to the embedded HTML before serving,
    /// e.g. CDN script URLs -> <c>libs/...</c> and local image paths -> <c>assets/...</c>.
    /// </summary>
    public IReadOnlyList<(string From, string To)>? Rewrites { get; init; }

    /// <summary>Override data2 root (dev). Defaults to AppContext.BaseDirectory\data2.</summary>
    public string? Data2Directory { get; init; }
}