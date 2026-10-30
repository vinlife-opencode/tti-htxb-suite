using Htxb.Host;

namespace Htxb.BiDFAnalysis;

public sealed class MainForm : HostForm
{
    public MainForm()
        : base(new HostOptions
        {
            WindowTitle = "TTI COBIS-II HTXB BiDF Analysis",
            ToolId = "bidf-analysis",
            WindowSize = new System.Drawing.Size(1280, 900),
            Rewrites = new[]
            {
                ("https://cdn.jsdelivr.net/npm/sql.js@1.8.0/dist/sql-wasm.js", "libs/sql-wasm.js"),
                ("https://cdn.jsdelivr.net/npm/xlsx-js-style@1.2.0/dist/xlsx.min.js", "libs/xlsx-js-style.min.js"),
                ("https://cdn.jsdelivr.net/npm/chart.js@4.4.0/dist/chart.umd.min.js", "libs/chart.umd.min.js"),
                // sql.js locateFile template literal -> serve the WASM from data2\lib
                ("https://cdn.jsdelivr.net/npm/sql.js@1.8.0/dist/", "libs/"),
                ("images/Company Logos/Favicon.ico", "assets/Favicon.ico"),
                ("images/Company Logos/Trio-Tech Logo.png", "assets/Trio-Tech Logo.png"),
                ("images/Company Logos/Customer.png", "assets/Customer.png"),
                ("images/DUT Packages/", "assets/dut_package/"),
            },
            PolyfillResourceName = "polyfill.js",
        })
    {
    }
}