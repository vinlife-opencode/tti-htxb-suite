using Htxb.Host;

namespace Htxb.RcpStdTable;

public sealed class MainForm : HostForm
{
    public MainForm()
        : base(new HostOptions
        {
            WindowTitle = "TTI HTXB/P2 BI Recipe Standard Conversion",
            ToolId = "rcp-std-table",
            WindowSize = new System.Drawing.Size(1280, 900),
            Rewrites = new[]
            {
                ("https://cdn.jsdelivr.net/npm/xlsx-js-style@1.2.0/dist/xlsx.min.js", "libs/xlsx-js-style.min.js"),
                ("images/Company Logos/Favicon.ico", "assets/Favicon.ico"),
                ("images/Company Logos/Trio-Tech Logo.png", "assets/Trio-Tech Logo.png"),
            },
            PolyfillResourceName = "polyfill.js",
        })
    {
    }
}