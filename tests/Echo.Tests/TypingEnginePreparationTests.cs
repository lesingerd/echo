using EchoTray;
using Xunit;

namespace EchoTray.Tests;

/// <summary>
/// Covers what Echo decides to type, which is the half of TypingEngine that needs no window.
/// Actually delivering the keystrokes is exercised by the interactive suite under tools/.
/// </summary>
public class TypingEnginePreparationTests
{
    private static AppSettings Defaults() => new()
    {
        Newlines = NewlineMode.Enter,
        Tabs = TabMode.Tab,
        TabSpaces = 4,
        TrimTrailingNewlines = true,
    };

    [Theory]
    [InlineData("a\r\nb")]
    [InlineData("a\rb")]
    [InlineData("a\nb")]
    public void EveryLineEndingBecomesOneNewline(string text)
    {
        Assert.Equal("a\nb", TypingEngine.Prepare(text, Defaults()));
    }

    [Fact]
    public void TrailingBlankLinesAreDroppedWhenAsked()
    {
        AppSettings settings = Defaults();

        Assert.Equal("text", TypingEngine.Prepare("text\r\n\r\n\r\n", settings));

        settings.TrimTrailingNewlines = false;
        Assert.Equal("text\n\n\n", TypingEngine.Prepare("text\r\n\r\n\r\n", settings));
    }

    [Fact]
    public void TrailingSpacesAreKept()
    {
        // Only newlines are trimmed; a trailing space can be deliberate.
        Assert.Equal("text  ", TypingEngine.Prepare("text  \n", Defaults()));
    }

    [Fact]
    public void NewlinesCanBecomeSpaces()
    {
        AppSettings settings = Defaults();
        settings.Newlines = NewlineMode.Space;

        Assert.Equal("one two three", TypingEngine.Prepare("one\ntwo\r\nthree", settings));
    }

    [Fact]
    public void NewlinesCanBeSkipped()
    {
        AppSettings settings = Defaults();
        settings.Newlines = NewlineMode.Skip;

        Assert.Equal("onetwo", TypingEngine.Prepare("one\ntwo", settings));
    }

    [Fact]
    public void ShiftEnterStillLeavesANewlineToSend()
    {
        AppSettings settings = Defaults();
        settings.Newlines = NewlineMode.ShiftEnter;

        // The distinction between Enter and Shift+Enter happens at the key press, not here.
        Assert.Equal("one\ntwo", TypingEngine.Prepare("one\ntwo", settings));
    }

    [Fact]
    public void TabsCanBecomeSpaces()
    {
        AppSettings settings = Defaults();
        settings.Tabs = TabMode.Spaces;
        settings.TabSpaces = 3;

        Assert.Equal("a   b", TypingEngine.Prepare("a\tb", settings));
    }

    [Fact]
    public void TabsCanBeSkipped()
    {
        AppSettings settings = Defaults();
        settings.Tabs = TabMode.Skip;

        Assert.Equal("ab", TypingEngine.Prepare("a\tb", settings));
    }

    [Fact]
    public void TabsAreKeptByDefault()
    {
        Assert.Equal("a\tb", TypingEngine.Prepare("a\tb", Defaults()));
    }

    [Fact]
    public void CombinedNewlineAndTabPreferencesMatchTheInteractiveSuite()
    {
        AppSettings settings = Defaults();
        settings.Newlines = NewlineMode.Space;
        settings.Tabs = TabMode.Spaces;
        settings.TabSpaces = 3;

        Assert.Equal("a   b c d", TypingEngine.Prepare("a\tb\nc\r\nd\n\n", settings));
    }

    [Fact]
    public void OtherControlCharactersAreDropped()
    {
        Assert.Equal("ab", TypingEngine.Prepare("a\0\a\b\vb", Defaults()));
    }

    [Fact]
    public void AccentsAndEmojiSurvive()
    {
        const string Text = "ünïcödé \U0001F600 — done";

        Assert.Equal(Text, TypingEngine.Prepare(Text, Defaults()));
    }

    [Fact]
    public void EmptyInputStaysEmpty()
    {
        Assert.Equal(string.Empty, TypingEngine.Prepare(string.Empty, Defaults()));
        Assert.Equal(string.Empty, TypingEngine.Prepare("\r\n\r\n", Defaults()));
    }
}
