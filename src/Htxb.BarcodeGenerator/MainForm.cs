using Htxb.Host;

namespace Htxb.BarcodeGenerator;

public sealed class MainForm : HostForm
{
    public MainForm()
        : base(new HostOptions
        {
            WindowTitle = "TTI Barcode Generator",
            ToolId = "barcode-generator",
            WindowSize = new System.Drawing.Size(660, 640),
            MinWindowSize = new System.Drawing.Size(480, 480),
            PolyfillResourceName = "polyfill.js",
        })
    {
    }
}
