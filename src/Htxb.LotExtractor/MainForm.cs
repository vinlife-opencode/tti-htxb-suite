using Htxb.Host;

namespace Htxb.LotExtractor;

public sealed class MainForm : HostForm
{
    public MainForm()
        : base(new HostOptions
        {
            WindowTitle = "TTI COBIS-II HTXB Lot Extractor",
            ToolId = "lot-extractor",
            WindowSize = new System.Drawing.Size(1280, 900),
            Rewrites = new[]
            {
                ("images/Company Logos/Favicon.ico", "assets/Favicon.ico"),
                ("images/Company Logos/Trio-Tech Logo.png", "assets/Trio-Tech Logo.png"),
            },
            PolyfillResourceName = "polyfill.js",
        })
    {
    }
}