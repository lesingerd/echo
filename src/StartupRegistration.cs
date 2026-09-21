using Microsoft.Win32;

namespace EchoTray;

/// <summary>Per-user "start with Windows" entry. No elevation, no scheduled task.</summary>
internal static class StartupRegistration
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Echo";

    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is string value && value.Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Repairs a startup entry whose target no longer exists, for example after the exe was
    /// moved or republished elsewhere. An entry that still points at a real file is left alone,
    /// so simply running a second copy of Echo does not quietly take over startup.
    /// </summary>
    public static void RefreshPath()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
            if (key?.GetValue(ValueName) is not string existing || Environment.ProcessPath is not string exe)
            {
                return;
            }

            if (File.Exists(existing.Trim().Trim('"')))
            {
                return;
            }

            key.SetValue(ValueName, $"\"{exe}\"", RegistryValueKind.String);
        }
        catch (Exception)
        {
            // A stale path is better than a crash on startup.
        }
    }

    /// <summary>Returns null on success, or a message describing why it failed.</summary>
    public static string? SetEnabled(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return null;
            }

            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                return "Echo could not work out its own path.";
            }

            key.SetValue(ValueName, $"\"{exePath}\"", RegistryValueKind.String);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
