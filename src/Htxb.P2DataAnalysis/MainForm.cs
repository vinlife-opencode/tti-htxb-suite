using Htxb.Host;

namespace Htxb.P2DataAnalysis;

public sealed class MainForm : HostForm
{
    public MainForm()
        : base(new HostOptions
        {
            WindowTitle = "TTI HTXB P2 Data Analysis",
            ToolId = "p2-analysis",
            WindowSize = new System.Drawing.Size(1280, 900),
            Rewrites = new[]
            {
                ("https://cdn.jsdelivr.net/npm/xlsx-js-style@1.2.0/dist/xlsx.min.js", "libs/xlsx-js-style.min.js"),
            },
            PolyfillResourceName = "polyfill.js",
        })
    {
    }
}