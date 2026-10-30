using Htxb.Host;

namespace Htxb.TempUniformity;

public sealed class MainForm : HostForm
{
    public MainForm()
        : base(new HostOptions
        {
            WindowTitle = "TTI Temperature Uniformity Graph",
            ToolId = "temp-uniformity",
            WindowSize = new System.Drawing.Size(1280, 900),
            Rewrites = new[]
            {
                ("https://cdn.jsdelivr.net/npm/xlsx@0.18.5/dist/xlsx.full.min.js", "libs/xlsx.full.min.js"),
                ("https://cdn.jsdelivr.net/npm/chart.js@4.4.1/dist/chart.umd.min.js", "libs/chart.umd.min.js"),
                ("images/Company Logos/Favicon.ico", "assets/Favicon.ico"),
                ("images/Company Logos/Trio-Tech Logo.png", "assets/Trio-Tech Logo.png"),
            },
            PolyfillResourceName = "polyfill.js",
        })
    {
    }
}