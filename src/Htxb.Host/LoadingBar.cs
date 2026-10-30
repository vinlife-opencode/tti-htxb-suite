using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Htxb.Host;

/// <summary>
/// Windows 11-style rounded progress bar for the loading splash. Determinate
/// fill driven by <see cref="TickAdvance"/>: moves quickly at first and eases
/// toward a soft cap, approximating the expected remaining wait without knowing
/// the true load duration.
/// </summary>
internal sealed class LoadingBar : Control
{
    private const int BarHeight = 6;
    private const double SoftCap = 0.92;
    private double _progress;

    public LoadingBar()
    {
        SetStyle(ControlStyles.UserPaint
                 | ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.ResizeRedraw, true);
        BackColor = Color.White;
    }

    /// <summary>Call from a timer (~50 ms) while the page loads.</summary>
    public void TickAdvance()
    {
        if (_progress >= SoftCap) return;
        _progress = Math.Min(SoftCap, _progress + (SoftCap - _progress) * 0.03 + 0.001);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(BackColor);

        var track = new Rectangle(0, (Height - BarHeight) / 2, Width, BarHeight);
        using (var path = Rounded(track, BarHeight / 2f))
        using (var brush = new SolidBrush(Color.FromArgb(229, 229, 229)))
            g.FillPath(brush, path);

        int fillW = (int)Math.Round(Width * _progress);
        if (fillW > BarHeight)
        {
            var fill = new Rectangle(track.X, track.Y, Math.Min(fillW, Width), BarHeight);
            using var path = Rounded(fill, BarHeight / 2f);
            using var brush = new SolidBrush(Color.FromArgb(0, 103, 192));
            g.FillPath(brush, path);
        }
    }

    private static GraphicsPath Rounded(Rectangle r, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}