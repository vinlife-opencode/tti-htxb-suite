using Htxb.Host;

namespace Htxb.P2DataAnalysisTime;

public sealed class MainForm : HostForm
{
    public MainForm()
        : base(new HostOptions
        {
            WindowTitle = "TTI HTXB P2 Data Analysis - Time",
            ToolId = "p2-analysis-time",
            WindowSize = new System.Drawing.Size(1120, 900),
            Rewrites = new[]
            {
                ("images/Company Logos/Favicon.ico", "assets/Favicon.ico"),
            },
            PolyfillResourceName = "polyfill.js",
        })
    {
    }
}
