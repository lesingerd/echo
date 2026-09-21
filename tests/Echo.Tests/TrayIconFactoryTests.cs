using System.Drawing;
using EchoTray;
using Xunit;

namespace EchoTray.Tests;

/// <summary>
/// Drawing needs GDI+ but no desktop session, input or focus, so these are safe in CI.
/// Every test passes an explicit size so nothing depends on the machine's DPI.
/// </summary>
public class TrayIconFactoryTests
{
    private static Bitmap Render(EchoState state, int? seconds, int size)
    {
        (Icon icon, IntPtr handle) = TrayIconFactory.Create(state, seconds, size);
        try
        {
            return icon.ToBitmap();
        }
        finally
        {
            icon.Dispose();
            NativeMethods.DestroyIcon(handle);
        }
    }

    /// <summary>The box around everything actually drawn, or Empty if the icon came out blank.</summary>
    private static Rectangle InkBounds(Bitmap bitmap)
    {
        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;

        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).A > 32)
                {
                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        return right < left ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
    }

    // EchoState is internal, so it cannot appear in a public test signature: the states are
    // walked inside the test instead of being passed in as theory data.
    [Fact]
    public void EveryStateDrawsSomethingAtTheRequestedSize()
    {
        foreach (EchoState state in Enum.GetValues<EchoState>())
        {
            using Bitmap bitmap = Render(state, state == EchoState.Counting ? 5 : null, 16);

            Assert.Equal(16, bitmap.Width);
            Assert.Equal(16, bitmap.Height);
            Assert.True(InkBounds(bitmap) != Rectangle.Empty, $"{state} drew nothing");
        }
    }

    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    public void TheMarkFillsTheIconAtEverySize(int size)
    {
        using Bitmap bitmap = Render(EchoState.Idle, null, size);
        Rectangle ink = InkBounds(bitmap);

        // The ">|<" should use most of the width and height, not huddle in a corner.
        Assert.True(ink.Width >= size * 0.6, $"ink was only {ink.Width}px wide at {size}px");
        Assert.True(ink.Height >= size * 0.5, $"ink was only {ink.Height}px tall at {size}px");
        Assert.True(ink.Right <= size && ink.Bottom <= size);
    }

    [Fact]
    public void StatesAreTintedDifferently()
    {
        using Bitmap idle = Render(EchoState.Idle, null, 32);
        using Bitmap armed = Render(EchoState.Armed, null, 32);
        using Bitmap typing = Render(EchoState.Typing, null, 32);

        Color idleInk = BrightestPixel(idle);
        Color armedInk = BrightestPixel(armed);
        Color typingInk = BrightestPixel(typing);

        Assert.NotEqual(idleInk, armedInk);
        Assert.NotEqual(idleInk, typingInk);
        Assert.NotEqual(armedInk, typingInk);
    }

    /// <summary>
    /// Guards a real regression: a two digit countdown used to be laid out with wrapping
    /// enabled, so "27" rendered as a clipped "2".
    /// </summary>
    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    public void TwoDigitCountdownsDrawBothDigits(int size)
    {
        using Bitmap one = Render(EchoState.Counting, 2, size);
        using Bitmap two = Render(EchoState.Counting, 27, size);

        int oneWide = InkBounds(one).Width;
        int twoWide = InkBounds(two).Width;

        Assert.True(twoWide > oneWide, $"\"27\" drew {twoWide}px wide, \"2\" drew {oneWide}px at {size}px");
        Assert.True(InkBounds(two).Right <= size, "the second digit ran off the edge of the icon");
    }

    [Fact]
    public void LongCountdownsFallBackToTheMark()
    {
        // Past 99 the number stops fitting, so the icon shows the mark instead.
        using Bitmap late = Render(EchoState.Counting, 120, 32);
        using Bitmap mark = Render(EchoState.Armed, null, 32);

        Assert.Equal(InkBounds(mark).Width, InkBounds(late).Width);
    }

    [Fact]
    public void CallerGetsAHandleItCanDestroy()
    {
        (Icon icon, IntPtr handle) = TrayIconFactory.Create(EchoState.Idle, null, 16);

        Assert.NotEqual(IntPtr.Zero, handle);
        Assert.NotNull(icon);

        icon.Dispose();
        Assert.True(NativeMethods.DestroyIcon(handle));
    }

    private static Color BrightestPixel(Bitmap bitmap)
    {
        Color best = Color.Transparent;
        int bestScore = -1;

        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                Color pixel = bitmap.GetPixel(x, y);
                if (pixel.A < 250)
                {
                    continue;
                }

                int score = pixel.R + pixel.G + pixel.B;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = Color.FromArgb(255, pixel.R, pixel.G, pixel.B);
                }
            }
        }

        return best;
    }
}
