using EchoTray;
using Xunit;

namespace EchoTray.Tests;

/// <summary>
/// These never call Load or Save: those read and write the real %APPDATA%\Echo\settings.json,
/// and a test run has no business touching the settings of whoever is running it.
/// </summary>
public class AppSettingsTests
{
    [Fact]
    public void ShipsTheDocumentedDefaults()
    {
        var settings = new AppSettings();

        Assert.Equal(50, settings.CharDelayMs);
        Assert.Equal(10, settings.JitterPercent);
        Assert.Equal(400, settings.SettleDelayMs);
        Assert.Equal(60, settings.ArmTimeoutSeconds);
        Assert.Equal(10, settings.HistorySize);
        Assert.Equal("Ctrl+Alt+E", settings.Hotkey);
        Assert.True(settings.HotkeyEnabled);
        Assert.True(settings.StopIfWindowChanges);
        Assert.True(settings.EscapeCancels);
        Assert.True(settings.ReleaseModifiers);
        Assert.False(settings.StartupConfigured);
        Assert.Equal(new[] { 3, 5, 10, 30 }, settings.DelayPresets);
    }

    [Fact]
    public void NormalizeClampsEveryNumericRange()
    {
        var settings = new AppSettings
        {
            CharDelayMs = 99_999,
            JitterPercent = -5,
            SettleDelayMs = 99_999,
            ArmTimeoutSeconds = 1,
            CustomDelaySeconds = 99_999,
            TabSpaces = 99,
            HistorySize = 99,
        };

        settings.Normalize();

        Assert.Equal(1000, settings.CharDelayMs);
        Assert.Equal(0, settings.JitterPercent);
        Assert.Equal(10_000, settings.SettleDelayMs);
        Assert.Equal(5, settings.ArmTimeoutSeconds);
        Assert.Equal(3600, settings.CustomDelaySeconds);
        Assert.Equal(16, settings.TabSpaces);
        Assert.Equal(25, settings.HistorySize);
    }

    [Fact]
    public void NormalizeTidiesDelayPresets()
    {
        var settings = new AppSettings
        {
            DelayPresets = new List<int> { 30, 5, 5, 0, -2, 10, 99_999, 3 },
        };

        settings.Normalize();

        Assert.Equal(new[] { 3, 5, 10, 30 }, settings.DelayPresets);
    }

    [Fact]
    public void NormalizeRestoresPresetsWhenNoneSurvive()
    {
        var settings = new AppSettings { DelayPresets = new List<int> { 0, -1, 99_999 } };

        settings.Normalize();

        Assert.Equal(new[] { 3, 5, 10, 30 }, settings.DelayPresets);
    }

    [Fact]
    public void NormalizeCapsThePresetCount()
    {
        var settings = new AppSettings
        {
            DelayPresets = Enumerable.Range(1, 20).ToList(),
        };

        settings.Normalize();

        Assert.Equal(8, settings.DelayPresets.Count);
    }

    [Fact]
    public void NormalizeDisablesAnEmptyShortcut()
    {
        var settings = new AppSettings { Hotkey = "   ", HotkeyEnabled = true };

        settings.Normalize();

        Assert.False(settings.HotkeyEnabled);
        Assert.Equal("Ctrl+Alt+E", settings.Hotkey);
    }

    [Fact]
    public void CloneDoesNotShareThePresetList()
    {
        var original = new AppSettings { DelayPresets = new List<int> { 3, 5 } };

        AppSettings copy = original.Clone();
        copy.DelayPresets.Add(10);
        copy.CharDelayMs = 999;

        Assert.Equal(new[] { 3, 5 }, original.DelayPresets);
        Assert.Equal(50, original.CharDelayMs);
    }

    [Fact]
    public void JsonRoundTripPreservesEverySetting()
    {
        var original = new AppSettings
        {
            CharDelayMs = 7,
            JitterPercent = 40,
            SettleDelayMs = 250,
            ArmTimeoutSeconds = 120,
            CustomDelaySeconds = 42,
            DelayPresets = new List<int> { 2, 5, 15 },
            Newlines = NewlineMode.ShiftEnter,
            Tabs = TabMode.Spaces,
            TabSpaces = 3,
            TrimTrailingNewlines = false,
            ShowBalloons = false,
            ReleaseModifiers = false,
            EscapeCancels = false,
            StopIfWindowChanges = false,
            HotkeyEnabled = false,
            Hotkey = "Ctrl+Shift+V",
            HistorySize = 4,
            StartupConfigured = true,
        };

        AppSettings? restored = AppSettings.TryFromJson(original.ToJson());

        Assert.NotNull(restored);
        Assert.Equal(original.CharDelayMs, restored!.CharDelayMs);
        Assert.Equal(original.JitterPercent, restored.JitterPercent);
        Assert.Equal(original.SettleDelayMs, restored.SettleDelayMs);
        Assert.Equal(original.ArmTimeoutSeconds, restored.ArmTimeoutSeconds);
        Assert.Equal(original.CustomDelaySeconds, restored.CustomDelaySeconds);
        Assert.Equal(original.DelayPresets, restored.DelayPresets);
        Assert.Equal(original.Newlines, restored.Newlines);
        Assert.Equal(original.Tabs, restored.Tabs);
        Assert.Equal(original.TabSpaces, restored.TabSpaces);
        Assert.Equal(original.TrimTrailingNewlines, restored.TrimTrailingNewlines);
        Assert.Equal(original.ShowBalloons, restored.ShowBalloons);
        Assert.Equal(original.ReleaseModifiers, restored.ReleaseModifiers);
        Assert.Equal(original.EscapeCancels, restored.EscapeCancels);
        Assert.Equal(original.StopIfWindowChanges, restored.StopIfWindowChanges);
        Assert.Equal(original.HotkeyEnabled, restored.HotkeyEnabled);
        Assert.Equal(original.Hotkey, restored.Hotkey);
        Assert.Equal(original.HistorySize, restored.HistorySize);
        Assert.True(restored.StartupConfigured);
    }

    [Fact]
    public void EnumsAreStoredByName()
    {
        string json = new AppSettings { Newlines = NewlineMode.ShiftEnter, Tabs = TabMode.Skip }.ToJson();

        Assert.Contains("\"ShiftEnter\"", json, StringComparison.Ordinal);
        Assert.Contains("\"Skip\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingFieldsFallBackToDefaults()
    {
        AppSettings? restored = AppSettings.TryFromJson("{ \"CharDelayMs\": 12 }");

        Assert.NotNull(restored);
        Assert.Equal(12, restored!.CharDelayMs);
        Assert.Equal(10, restored.JitterPercent);
        Assert.Equal("Ctrl+Alt+E", restored.Hotkey);
    }

    [Fact]
    public void ValuesAreNormalizedOnTheWayIn()
    {
        AppSettings? restored = AppSettings.TryFromJson("{ \"CharDelayMs\": 99999, \"HistorySize\": -3 }");

        Assert.NotNull(restored);
        Assert.Equal(1000, restored!.CharDelayMs);
        Assert.Equal(0, restored.HistorySize);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{ \"CharDelayMs\": }")]
    [InlineData("{ \"CharDelayMs\": \"fifty\" }")]
    public void UnusableJsonIsRejectedRatherThanThrown(string json)
    {
        Assert.Null(AppSettings.TryFromJson(json));
    }
}
