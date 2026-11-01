using Htxb.Host;

namespace Htxb.Launcher;

public sealed class MainForm : HostForm
{
    public MainForm()
        : base(new HostOptions
        {
            WindowTitle = "TTI HTXB Tools & Utilities",
            ToolId = "launcher",
            // Slim corner window, full working-area height: stays out of the way
            // of the tool windows it launches.
            WindowSize = new System.Drawing.Size(460, FullWorkingAreaHeight()),
            MinWindowSize = new System.Drawing.Size(380, 300),
            StartAtScreenTopLeft = true,
            // Keep the 460 px corner strip even on small screens: the launcher is
            // wide on purpose (cards + edge tab), so the oversize-width trim that
            // applies to tool windows must not touch it.
            ShrinkOversizeWidth = false,
            // Collapse to a corner tab after load; hovering/clicking the tab
            // expands back to full height.
            EnableEdgeAutoHide = true,
            // Resident window: no taskbar button (minimize docks to the tab).
            HideTaskbarIcon = true,
            Rewrites = new[]
            {
                ("images/Company Logos/Favicon.ico", "assets/Favicon.ico"),
            },
            PolyfillResourceName = "polyfill.js",
        })
    {
        // Hidden task: write unique hardware info to configuration\pc_info.ini.
        PcInfo.RunDetached();
    }

    private static int FullWorkingAreaHeight()
    {
        try
        {
            var h = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea.Height ?? 0;
            return h > 0 ? h : 780;
        }
        catch
        {
            return 780;
        }
    }
}