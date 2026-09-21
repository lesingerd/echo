using System.Windows.Forms;
using EchoTray;
using Xunit;

namespace EchoTray.Tests;

public class HotkeyParserTests
{
    [Fact]
    public void ParsesModifiersAndKey()
    {
        Assert.True(HotkeyParser.TryParse("Ctrl+Alt+E", out uint modifiers, out uint key));
        Assert.Equal(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, modifiers);
        Assert.Equal((uint)Keys.E, key);
    }

    [Theory]
    [InlineData("ctrl+alt+e")]
    [InlineData("  Ctrl + Alt + E  ")]
    [InlineData("CTRL+ALT+E")]
    public void IgnoresCaseAndSpacing(string text)
    {
        Assert.True(HotkeyParser.TryParse(text, out uint modifiers, out uint key));
        Assert.Equal(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, modifiers);
        Assert.Equal((uint)Keys.E, key);
    }

    [Fact]
    public void ParsesWindowsModifier()
    {
        Assert.True(HotkeyParser.TryParse("Win+Shift+V", out uint modifiers, out uint key));
        Assert.Equal(NativeMethods.MOD_WIN | NativeMethods.MOD_SHIFT, modifiers);
        Assert.Equal((uint)Keys.V, key);
    }

    [Fact]
    public void ParsesDigitsAsNumberRowKeys()
    {
        // Enum.TryParse would read a bare "1" as the numeric enum value (Keys.LButton).
        Assert.True(HotkeyParser.TryParse("Ctrl+Shift+1", out _, out uint key));
        Assert.Equal((uint)Keys.D1, key);
    }

    [Fact]
    public void ParsesFunctionKeys()
    {
        Assert.True(HotkeyParser.TryParse("Alt+F4", out uint modifiers, out uint key));
        Assert.Equal(NativeMethods.MOD_ALT, modifiers);
        Assert.Equal((uint)Keys.F4, key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("E")]           // a bare key would swallow that key system-wide
    [InlineData("Ctrl")]        // modifier with nothing to press
    [InlineData("Ctrl+Nope")]
    [InlineData("Ctrl+A+B")]    // two keys
    public void RejectsUnusableCombinations(string text)
    {
        Assert.False(HotkeyParser.TryParse(text, out _, out _));
    }

    [Fact]
    public void FormatsInTheOrderItParses()
    {
        Assert.Equal("Ctrl+Alt+E", HotkeyParser.Format(Keys.E | Keys.Control | Keys.Alt));
        Assert.Equal("Ctrl+Shift+1", HotkeyParser.Format(Keys.D1 | Keys.Control | Keys.Shift));
        Assert.Equal("Alt+F4", HotkeyParser.Format(Keys.F4 | Keys.Alt));
    }

    [Theory]
    [InlineData(Keys.E | Keys.Control | Keys.Alt)]
    [InlineData(Keys.D1 | Keys.Control | Keys.Shift)]
    [InlineData(Keys.F4 | Keys.Alt)]
    [InlineData(Keys.V | Keys.Control | Keys.Shift | Keys.Alt)]
    public void FormattedTextParsesBackToTheSameKeys(Keys keyData)
    {
        string formatted = HotkeyParser.Format(keyData);

        Assert.True(HotkeyParser.TryParse(formatted, out uint modifiers, out uint key));
        Assert.Equal((uint)(keyData & Keys.KeyCode), key);

        uint expected = 0;
        if ((keyData & Keys.Control) == Keys.Control)
        {
            expected |= NativeMethods.MOD_CONTROL;
        }

        if ((keyData & Keys.Alt) == Keys.Alt)
        {
            expected |= NativeMethods.MOD_ALT;
        }

        if ((keyData & Keys.Shift) == Keys.Shift)
        {
            expected |= NativeMethods.MOD_SHIFT;
        }

        Assert.Equal(expected, modifiers);
    }
}
