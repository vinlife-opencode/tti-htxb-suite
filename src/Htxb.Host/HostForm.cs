using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Htxb.Host;

/// <summary>
/// Shared WinForms host: a full-dock WebView2 window wired up to the
/// <see cref="WebHost"/> virtual resource server, the <see cref="NativeBridge"/>
/// (window.tools), and <see cref="data2"/>. Each tool project subclasses this
/// and only supplies a <see cref="HostOptions"/>.
/// </summary>
public class HostForm : Form
{
    protected readonly WebView2 WebView = new();
    protected readonly HostOptions Options;
    protected readonly Data2Loader Data2;
    protected readonly NativeBridge Bridge;

    private bool _coreReady;
    private Panel? _loadingPanel;
    private LoadingBar? _loadingBar;
    private System.Windows.Forms.Timer? _loadingTimer;

    // ---- Oversize-width trim (HostOptions.ShrinkOversizeWidth)
    private const double OversizeShare = 0.25;   // trim above 25% of working-area width
    private const double OversizeTrim = 0.40;    // remove 40% of the width when trimming

    // ---- Edge auto-hide: collapse to a corner tab (opt-in via HostOptions.EnableEdgeAutoHide)
    private const int EdgeTabWidth = 170;     // collapsed tab size, pinned at the corner
    private const int EdgeTabHeight = 72;
    private const int EdgeDockTolerance = 8;  // max offset still considered "docked"
    private const int EdgeHotZoneExtra = 10;  // hover grace around the tab
    private const int EdgeHideDelayMs = 900;  // mouse-out dwell before collapsing
    private const int EdgeInitialGraceMs = 2500; // grace after load before first collapse

    private System.Windows.Forms.Timer? _edgePollTimer;
    private bool _edgeArmed;
    private DateTime _edgeArmedAt;
    private DateTime _edgeMouseLeftAt = DateTime.MinValue;
    private bool _edgeCollapsed;
    private Size _edgeFullSize;
    private Point _edgeFullLocation;
    private Size _edgeFullMinSize;
    private Panel? _edgeTab;

    /// <summary>
    /// Width sibling tools should keep clear on the left. While collapsed this
    /// reports the remembered full width so launched tools never end up under
    /// the expanded launcher.
    /// </summary>
    internal int LaunchReserveWidth => _edgeCollapsed ? _edgeFullSize.Width : Width;

    /// <summary>
    /// Set by the page via <c>window.tools.SetBusy</c> while an in-page overlay
    /// is open for typing; edge auto-hide stays expanded until cleared.
    /// </summary>
    internal bool PageBusy { get; set; }

    /// <summary>
    /// Count of native modal dialogs currently open (bumped by
    /// <see cref="NativeBridge"/> around each dialog); edge auto-hide stays
    /// expanded while non-zero.
    /// </summary>
    internal int AutoHideSuppress;

    private bool AutoHideBlocked => PageBusy || AutoHideSuppress != 0;

    /// <summary>
    /// Drop the window width by <see cref="OversizeTrim"/> when the form would
    /// otherwise cover more than <see cref="OversizeShare"/> of the primary
    /// screen's working-area width. Height is deliberately left alone, so the
    /// window keeps its full vertical extent and only gets narrower.
    /// The minimum width is reduced first: WinForms clamps <see cref="Form.Size"/>
    /// straight back up to <see cref="Form.MinimumSize"/>, which would silently
    /// cancel the trim on every tool that keeps the default 960 px minimum.
    /// </summary>
    private void TrimOversizeWidth()
    {
        int areaWidth = Screen.PrimaryScreen?.WorkingArea.Width ?? 0;
        if (areaWidth <= 0)
            return;

        if (Width <= areaWidth * OversizeShare)
            return;

        int trimmed = (int)Math.Round(Width * (1.0 - OversizeTrim));
        if (trimmed < 1 || trimmed == Width)
            return;

        if (MinimumSize.Width > trimmed)
            MinimumSize = new Size(trimmed, MinimumSize.Height);

        Size = new Size(trimmed, Height);
    }

    public HostForm(HostOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        // Resident window: no taskbar button (set before handle creation).
        // Alt+Tab still lists it; minimizing docks it to the corner tab.
        ShowInTaskbar = !options.HideTaskbarIcon;
        Data2 = new Data2Loader(options.Data2Directory);
        Bridge = new NativeBridge(this, options.ToolId);

        Text = options.WindowTitle;
        Size = options.WindowSize ?? new Size(1280, 900);
        MinimumSize = options.MinWindowSize ?? new Size(960, 640);
        if (options.ShrinkOversizeWidth)
            TrimOversizeWidth();
        if (options.StartAtScreenTopLeft)
        {
            StartPosition = FormStartPosition.Manual;
            Location = new Point(0, 0);
        }
        else
        {
            // Launched from a corner-docked launcher? Center inside the free
            // area right of the reserved strip so the two never overlap.
            var wa = Screen.PrimaryScreen.WorkingArea;
            var reserveRaw = Environment.GetEnvironmentVariable(HostOptions.ReserveLeftEnvVar);
            if (int.TryParse(reserveRaw, out int reserve) && reserve > 0
                && Width < wa.Width - reserve)
            {
                StartPosition = FormStartPosition.Manual;
                int x = wa.Left + reserve + (wa.Width - reserve - Width) / 2;
                int y = wa.Top + Math.Max(0, (wa.Height - Height) / 2);
                Location = new Point(x, y);
            }
            else
            {
                StartPosition = FormStartPosition.CenterScreen;
            }
        }

        LoadAppIcon();

        WebView.Dock = DockStyle.Fill;
        Controls.Add(WebView);

        // Keep WebView2 profile data out of the EXE directory so dist\ stays clean
        // (only EXEs + shared data2). Profile lives under %LOCALAPPDATA%.
        string profileDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TTI HTXB", options.ToolId ?? "default");

        WebView.CoreWebView2InitializationCompleted += OnWebViewReady;
        _ = InitializeWebViewAsync(profileDir);

        ShowLoading();
    }

    private void ShowLoading()
    {
        _loadingPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };

        var label = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomCenter,
            Font = new Font("Segoe UI", 18, FontStyle.Bold),
            ForeColor = Color.FromArgb(31, 78, 121),
            BackColor = Color.White,
            Text = "UI is loading  ...  ...\nPlease wait  ...  ...",
        };

        var barHost = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };
        _loadingBar = new LoadingBar { Size = new Size(280, 24) };
        barHost.Controls.Add(_loadingBar);
        barHost.Resize += (_, _) =>
            _loadingBar.Location = new Point((barHost.Width - _loadingBar.Width) / 2,
                                             (barHost.Height - _loadingBar.Height) / 2);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.White,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 58f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 42f));
        layout.Controls.Add(label, 0, 0);
        layout.Controls.Add(barHost, 0, 1);

        _loadingPanel.Controls.Add(layout);
        Controls.Add(_loadingPanel);
        _loadingPanel.BringToFront();

        _loadingTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _loadingTimer.Tick += (_, _) => _loadingBar.TickAdvance();
        _loadingTimer.Start();
    }

    private void HideLoading()
    {
        _loadingTimer?.Stop();
        _loadingTimer?.Dispose();
        _loadingTimer = null;
        if (_loadingPanel is null) return;
        Controls.Remove(_loadingPanel);
        _loadingPanel.Dispose();
        _loadingPanel = null;
        _loadingBar = null;
    }

    private void LoadAppIcon()
    {
        try
        {
            byte[]? bytes = Data2.ReadBytes("assets/Favicon.ico");
            if (bytes is { Length: > 0 })
            {
                using var ms = new MemoryStream(bytes);
                Icon = new Icon(ms);
            }
        }
        catch
        {
            // keep default icon
        }
    }

    /// <summary>
    /// Creates the WebView2 environment explicitly so background browser
    /// networking can be switched off. Every tool page is served locally
    /// (embedded HTML + <c>data2</c>), so there is no legitimate network use —
    /// skipping proxy auto-detect, component update and sync traffic removes
    /// seconds of first-navigation stall, especially on offline/slow networks.
    /// </summary>
    private async Task InitializeWebViewAsync(string profileDir)
    {
        try
        {
            var options = new CoreWebView2EnvironmentOptions(
                "--no-proxy-server --disable-background-networking " +
                "--disable-component-update --disable-sync " +
                "--host-resolver-rules=\"MAP app 127.0.0.1\"");
            var env = await CoreWebView2Environment.CreateAsync(null, profileDir, options);
            await WebView.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex)
        {
            MessageBox.Show("WebView2 failed to initialize:\n\n" + ex.Message,
                "TTI HTXB", MessageBoxButtons.OK, MessageBoxIcon.Error);
            try { Close(); } catch { /* best-effort */ }
        }
    }

    private void OnWebViewReady(object? sender, EventArgs e)
    {
        if (_coreReady) return;
        _coreReady = true;

        if (e is CoreWebView2InitializationCompletedEventArgs { IsSuccess: false } initArgs)
        {
            MessageBox.Show("WebView2 failed to initialize:\n\n" + initArgs.InitializationException,
                "TTI HTXB", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }

        var core = WebView.CoreWebView2;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.AreBrowserAcceleratorKeysEnabled = false;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.IsZoomControlEnabled = true;
        core.Settings.IsStatusBarEnabled = false;

        core.AddHostObjectToScript("tools", Bridge);
        WebHost.Attach(core, Data2, Options);

        // Never surface permission prompts in the EXE. The tools only talk to
        // the disk through the native bridge (window.tools), so no WebView2
        // permission is ever legitimately needed; deny file-system access and
        // unknown requests outright so dialogs like "Allow this site to view and
        // copy files?" can never appear.
        core.PermissionRequested += (_, args) =>
        {
            if (args.PermissionKind == CoreWebView2PermissionKind.FileReadWrite
                || args.PermissionKind == CoreWebView2PermissionKind.UnknownPermission)
            {
                args.State = CoreWebView2PermissionState.Deny;
            }
        };

        // Open external-site links (target=_blank) in the system default browser instead of a host window.
        core.NewWindowRequested += (_, args) =>
        {
            if (!string.IsNullOrEmpty(args.Uri))
            {
                args.Handled = true;
                try { Process.Start(new ProcessStartInfo(args.Uri) { UseShellExecute = true }); }
                catch { /* best-effort */ }
            }
        };

        core.Navigate($"{WebHost.BaseUrl}/index.html");

        // Remove the loading message once the first navigation finishes (success or not).
        // The initial progress is complete at this point, so arm edge auto-hide here.
        core.NavigationCompleted += (_, _) =>
        {
            HideLoading();
            PageBusy = false; // page reloaded: any overlay busy-state is gone
            ArmEdgeAutoHide();
        };
    }

    /// <summary>
    /// Starts the edge auto-hide controller. Called once the initial page
    /// load completes (first <c>NavigationCompleted</c>); no-op unless
    /// <see cref="HostOptions.EnableEdgeAutoHide"/> is set.
    /// </summary>
    private void ArmEdgeAutoHide()
    {
        if (!Options.EnableEdgeAutoHide || _edgeArmed) return;
        _edgeArmed = true;
        _edgeArmedAt = DateTime.UtcNow;
        Activated += EdgeActivated;
        _edgePollTimer = new System.Windows.Forms.Timer { Interval = 150 };
        _edgePollTimer.Tick += (_, _) => EdgePoll();
        _edgePollTimer.Start();
    }

    /// <summary>
    /// The window was explicitly brought forward (Alt+Tab, taskbar click):
    /// expand the tab if collapsed, otherwise just restart the grace period
    /// so the user gets time to move the mouse in.
    /// </summary>
    private void EdgeActivated(object? sender, EventArgs e)
    {
        if (!_edgeArmed || IsDisposed || Disposing) return;
        if (_edgeCollapsed) ExpandEdgeTab();
        else { _edgeMouseLeftAt = DateTime.MinValue; _edgeArmedAt = DateTime.UtcNow; }
    }

    private void EdgePoll()
    {
        if (!_edgeArmed || IsDisposed || Disposing) return;
        if (!IsHandleCreated || WindowState != FormWindowState.Normal) return;

        Screen scr;
        try { scr = Screen.FromHandle(Handle); }
        catch { return; }
        int cornerX = scr.WorkingArea.Left;
        int cornerY = scr.WorkingArea.Top;

        // The user moved the window away from the corner: leave it as-is
        // until it is docked back near the top-left corner.
        if (Left > cornerX + EdgeDockTolerance || Top > cornerY + EdgeDockTolerance) return;

        Point mouse;
        try { mouse = Cursor.Position; }
        catch { return; }

        if (_edgeCollapsed)
        {
            var hot = new Rectangle(Left - EdgeHotZoneExtra, Top - EdgeHotZoneExtra,
                Width + EdgeHotZoneExtra * 2, Height + EdgeHotZoneExtra * 2);
            if (hot.Contains(mouse)) ExpandEdgeTab();
            return;
        }

        // Full size: reset the mouse-out dwell while the cursor is over the
        // window or the page/native side is busy (overlay open, dialog up);
        // collapse only after the dwell elapses. Focus is deliberately NOT
        // consulted: the launcher commonly stays foreground while the mouse
        // is elsewhere, and that must still collapse.
        if (Bounds.Contains(mouse) || AutoHideBlocked)
        {
            _edgeMouseLeftAt = DateTime.MinValue;
            return;
        }
        if (DateTime.UtcNow - _edgeArmedAt < TimeSpan.FromMilliseconds(EdgeInitialGraceMs))
            return;
        if (_edgeMouseLeftAt == DateTime.MinValue)
        {
            _edgeMouseLeftAt = DateTime.UtcNow;
            return;
        }
        if (DateTime.UtcNow - _edgeMouseLeftAt >= TimeSpan.FromMilliseconds(EdgeHideDelayMs))
        {
            _edgeMouseLeftAt = DateTime.MinValue;
            CollapseEdgeTab();
        }
    }

    private void EnsureEdgeTab()
    {
        if (_edgeTab is not null) return;
        var tab = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Cursor = Cursors.Hand,
            Visible = false,
        };
        var label = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = new Font("Segoe UI", 10, FontStyle.Bold),
            ForeColor = Color.FromArgb(0, 103, 192),
            BackColor = Color.White,
            Text = "» TTI Tools",
        };
        label.Click += (_, _) => ExpandEdgeTab();
        tab.Click += (_, _) => ExpandEdgeTab();
        tab.Controls.Add(label);
        Controls.Add(tab);
        tab.BringToFront();
        _edgeTab = tab;
    }

    private void CollapseEdgeTab()
    {
        if (_edgeCollapsed || !IsHandleCreated) return;
        Screen scr;
        try { scr = Screen.FromHandle(Handle); }
        catch { return; }
        _edgeFullSize = Size;
        _edgeFullLocation = Location;
        _edgeFullMinSize = MinimumSize;
        _edgeCollapsed = true;
        EnsureEdgeTab();
        try
        {
            // Lower the minimum first or the shrink is clamped.
            MinimumSize = new Size(EdgeTabWidth, EdgeTabHeight);
            WebView.Visible = false;
            if (_edgeTab is not null) { _edgeTab.Visible = true; _edgeTab.BringToFront(); }
            Location = new Point(scr.WorkingArea.Left, scr.WorkingArea.Top);
            Size = new Size(EdgeTabWidth, EdgeTabHeight);
        }
        catch { /* best-effort */ }
    }

    private void ExpandEdgeTab()
    {
        if (!_edgeCollapsed) return;
        _edgeCollapsed = false;
        _edgeMouseLeftAt = DateTime.MinValue;
        _edgeArmedAt = DateTime.UtcNow; // grace so the user can move the mouse in
        try
        {
            // Expand to the full working-area height at the corner (width
            // remembered); the tab never restores a short window.
            Screen scr = Screen.FromHandle(Handle);
            int cornerX = scr.WorkingArea.Left;
            int cornerY = scr.WorkingArea.Top;
            if (_edgeTab is not null) _edgeTab.Visible = false;
            // Size first while the minimum is still the tiny tab size, then
            // raise the minimum: raising it first would instantly clamp the
            // tab up to the minimum and leave a wrong-size window if the
            // Size assignment ever failed to follow.
            Size = new Size(_edgeFullSize.Width, scr.WorkingArea.Height);
            MinimumSize = _edgeFullMinSize;
            Location = new Point(cornerX, cornerY);
            WebView.Visible = true;
        }
        catch
        {
            // Fallback: restore exactly what was remembered.
            try
            {
                Size = _edgeFullSize;
                MinimumSize = _edgeFullMinSize;
                Location = _edgeFullLocation;
                WebView.Visible = true;
            }
            catch { /* best-effort */ }
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        // No taskbar button means a minimized window would strand with no way
        // back except Alt+Tab: dock it to the corner tab instead.
        if (Options.HideTaskbarIcon && Options.EnableEdgeAutoHide && _edgeArmed
            && WindowState == FormWindowState.Minimized && IsHandleCreated && !IsDisposed && !Disposing)
        {
            WindowState = FormWindowState.Normal;
            CollapseEdgeTab();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _edgePollTimer?.Stop();
        _edgePollTimer?.Dispose();
        _edgePollTimer = null;
        try { WebView.Dispose(); } catch { /* best-effort */ }
        base.OnFormClosed(e);
    }
}