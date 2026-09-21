using EchoTray;
using Xunit;

namespace EchoTray.Tests;

public class ClipboardHistoryTests
{
    [Fact]
    public void KeepsNewestFirst()
    {
        var history = new ClipboardHistory();

        Assert.True(history.Add("one"));
        Assert.True(history.Add("two"));
        Assert.True(history.Add("three"));

        Assert.Equal(new[] { "three", "two", "one" }, history.Entries);
    }

    [Fact]
    public void ReCopyingMovesAnEntryBackToTheTop()
    {
        var history = new ClipboardHistory();
        history.Add("one");
        history.Add("two");
        history.Add("three");

        Assert.True(history.Add("one"));

        Assert.Equal(new[] { "one", "three", "two" }, history.Entries);
        Assert.Equal(3, history.Entries.Count);
    }

    [Fact]
    public void CopyingWhatIsAlreadyOnTopChangesNothing()
    {
        var history = new ClipboardHistory();
        history.Add("one");

        Assert.False(history.Add("one"));
        Assert.Single(history.Entries);
    }

    [Fact]
    public void DropsTheOldestOnceFull()
    {
        var history = new ClipboardHistory { Capacity = 3 };

        for (int i = 1; i <= 5; i++)
        {
            history.Add($"entry {i}");
        }

        Assert.Equal(new[] { "entry 5", "entry 4", "entry 3" }, history.Entries);
    }

    [Fact]
    public void LoweringCapacityTrimsImmediately()
    {
        var history = new ClipboardHistory();
        for (int i = 1; i <= 8; i++)
        {
            history.Add($"entry {i}");
        }

        history.Capacity = 2;

        Assert.Equal(new[] { "entry 8", "entry 7" }, history.Entries);
    }

    [Fact]
    public void CapacityIsClamped()
    {
        var history = new ClipboardHistory { Capacity = 9999 };
        Assert.Equal(25, history.Capacity);

        history.Capacity = -4;
        Assert.Equal(0, history.Capacity);
    }

    [Fact]
    public void ZeroCapacityTurnsTheHistoryOff()
    {
        var history = new ClipboardHistory();
        history.Add("remembered");

        history.Capacity = 0;
        Assert.Empty(history.Entries);

        Assert.False(history.Add("ignored"));
        Assert.Empty(history.Entries);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t ")]
    public void IgnoresTextWithNothingInIt(string? text)
    {
        var history = new ClipboardHistory();

        Assert.False(history.Add(text));
        Assert.Empty(history.Entries);
    }

    [Fact]
    public void IgnoresEntriesTooLargeToBeWorthKeeping()
    {
        var history = new ClipboardHistory();

        Assert.False(history.Add(new string('x', 1_000_001)));
        Assert.Empty(history.Entries);

        Assert.True(history.Add(new string('x', 1_000_000)));
        Assert.Single(history.Entries);
    }

    [Fact]
    public void ComparesEntriesExactly()
    {
        var history = new ClipboardHistory();
        history.Add("Value");

        Assert.True(history.Add("value"));
        Assert.Equal(2, history.Entries.Count);
    }

    [Fact]
    public void ClearEmptiesEverything()
    {
        var history = new ClipboardHistory();
        history.Add("one");
        history.Add("two");

        history.Clear();

        Assert.Empty(history.Entries);
        Assert.True(history.Add("three"));
    }
}
