using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace EchoTray;

internal enum EchoState
{
    Idle,
    Armed,
    Counting,
    Typing,
}

/// <summary>
/// Draws the ">|<" mark at the shell's small-icon size so it stays crisp on any DPI,
/// and tints it by state. The same geometry is in tools/generate-icon.ps1 for the .ico.
/// </summary>
internal static class TrayIconFactory
{
    private static readonly Color Idle = Color.FromArgb(33, 150, 243);
    private static readonly Color Waiting = Color.FromArgb(245, 124, 0);
    private static readonly Color Working = Color.FromArgb(46, 158, 79);

    /// <summary>
    /// The caller owns the returned handle and must DestroyIcon it once the icon is replaced.
    /// <paramref name="forcedSize"/> is only for previewing other DPIs; 0 means the shell's own size.
    /// </summary>
    public static (Icon Icon, IntPtr Handle) Create(EchoState state, int? countdownSeconds, int forcedSize = 0)
    {
        int size = forcedSize > 0 ? forcedSize : Math.Max(16, SystemInformation.SmallIconSize.Width);

        using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            g.Clear(Color.Transparent);

            if (state == EchoState.Counting && countdownSeconds is > 0 and <= 99)
            {
                DrawCountdown(g, size, countdownSeconds.Value);
            }
            else
            {
                DrawMark(g, size, ColorFor(state));
            }
        }

        IntPtr handle = bitmap.GetHicon();
        return (Icon.FromHandle(handle), handle);
    }

    private static Color ColorFor(EchoState state) => state switch
    {
        EchoState.Armed => Waiting,
        EchoState.Counting => Waiting,
        EchoState.Typing => Working,
        _ => Idle,
    };

    private static void DrawMark(Graphics g, int size, Color color)
    {
        float s = size / 32f;
        using var pen = new Pen(color, Math.Max(1.3f, 2.6f * s))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };

        g.DrawLines(pen, new[]
        {
            new PointF(4.5f * s, 9f * s),
            new PointF(9.5f * s, 16f * s),
            new PointF(4.5f * s, 23f * s),
        });

        g.DrawLine(pen, new PointF(16f * s, 5.5f * s), new PointF(16f * s, 26.5f * s));

        g.DrawLines(pen, new[]
        {
            new PointF(27.5f * s, 9f * s),
            new PointF(22.5f * s, 16f * s),
            new PointF(27.5f * s, 23f * s),
        });
    }

    private static void DrawCountdown(Graphics g, int size, int seconds)
    {
        string text = seconds.ToString();

        using var brush = new SolidBrush(Waiting);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
            Trimming = StringTrimming.None,
        };

        Font font = new("Segoe UI", size * (text.Length > 1 ? 0.62f : 0.86f), FontStyle.Bold, GraphicsUnit.Pixel);
        try
        {
            // Two digits can still overrun a 16px icon, so shrink to whatever actually fits.
            SizeF measured = g.MeasureString(text, font, PointF.Empty, format);
            if (measured.Width > size - 1)
            {
                float fitted = Math.Max(6f, font.Size * (size - 1) / measured.Width);
                font.Dispose();
                font = new Font("Segoe UI", fitted, FontStyle.Bold, GraphicsUnit.Pixel);
            }

            g.DrawString(text, font, brush, new RectangleF(0, 0, size, size), format);
        }
        finally
        {
            font.Dispose();
        }
    }
}
