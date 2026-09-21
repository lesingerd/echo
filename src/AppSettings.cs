using System.Text.Json;
using System.Text.Json.Serialization;

namespace EchoTray;

internal enum NewlineMode
{
    Enter,
    ShiftEnter,
    Space,
    Skip,
}

internal enum TabMode
{
    Tab,
    Spaces,
    Skip,
}

/// <summary>User preferences, stored as JSON under %APPDATA%\Echo.</summary>
internal sealed class AppSettings
{
    public int CharDelayMs { get; set; } = 50;
    public int JitterPercent { get; set; } = 10;
    public int SettleDelayMs { get; set; } = 400;
    public int ArmTimeoutSeconds { get; set; } = 60;
    public int CustomDelaySeconds { get; set; } = 15;
    public List<int> DelayPresets { get; set; } = new() { 3, 5, 10, 30 };

    public NewlineMode Newlines { get; set; } = NewlineMode.Enter;
    public TabMode Tabs { get; set; } = TabMode.Tab;
    public int TabSpaces { get; set; } = 4;
    public bool TrimTrailingNewlines { get; set; } = true;

    public bool ShowBalloons { get; set; } = true;
    public bool ReleaseModifiers { get; set; } = true;
    public bool EscapeCancels { get; set; } = true;
    public bool StopIfWindowChanges { get; set; } = true;

    public bool HotkeyEnabled { get; set; } = true;
    public string Hotkey { get; set; } = "Ctrl+Alt+E";

    /// <summary>How many past clipboard entries the tray menu offers. 0 turns the history off.</summary>
    public int HistorySize { get; set; } = 10;

    /// <summary>Set once, the first time Echo runs, after it adds itself to Windows startup.</summary>
    public bool StartupConfigured { get; set; }

    [JsonIgnore]
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Echo", "settings.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options);
                if (loaded is not null)
                {
                    loaded.Normalize();
                    return loaded;
                }
            }
        }
        catch (Exception)
        {
            // A corrupt or unreadable settings file must never stop Echo from starting.
        }

        return new AppSettings();
    }

    public void Save()
    {
        Normalize();
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
    }

    public AppSettings Clone()
    {
        var copy = (AppSettings)MemberwiseClone();
        copy.DelayPresets = new List<int>(DelayPresets);
        return copy;
    }

    public void Normalize()
    {
        CharDelayMs = Math.Clamp(CharDelayMs, 0, 1000);
        JitterPercent = Math.Clamp(JitterPercent, 0, 100);
        SettleDelayMs = Math.Clamp(SettleDelayMs, 0, 10_000);
        ArmTimeoutSeconds = Math.Clamp(ArmTimeoutSeconds, 5, 3600);
        CustomDelaySeconds = Math.Clamp(CustomDelaySeconds, 1, 3600);
        TabSpaces = Math.Clamp(TabSpaces, 1, 16);
        HistorySize = Math.Clamp(HistorySize, 0, 25);

        DelayPresets = DelayPresets
            .Where(v => v > 0 && v <= 3600)
            .Distinct()
            .Order()
            .Take(8)
            .ToList();

        if (DelayPresets.Count == 0)
        {
            DelayPresets = new List<int> { 3, 5, 10, 30 };
        }

        if (string.IsNullOrWhiteSpace(Hotkey))
        {
            HotkeyEnabled = false;
            Hotkey = "Ctrl+Alt+E";
        }
    }
}
