using Htxb.Host;

namespace Htxb.RecipeTranslator;

public sealed class MainForm : HostForm
{
    public MainForm()
        : base(new HostOptions
        {
            WindowTitle = "TTI COBIS-II HTXB Recipe Translator",
            ToolId = "recipe-translator",
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