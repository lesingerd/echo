using Microsoft.Win32;

namespace EchoTray.InteractiveTests;

/// <summary>
/// Constructing an EchoTrayContext runs Echo's first-run startup registration, which would
/// otherwise point the HKCU Run key at this test binary instead of the real Echo.exe. Every
/// suite that creates a context must wrap it in one of these.
/// </summary>
internal sealed class StartupGuard : IDisposable
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Echo";

    private readonly string? _original;

    public StartupGuard()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        _original = key?.GetValue(ValueName) as string;
    }

    public void Dispose()
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (_original is null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        else
        {
            key.SetValue(ValueName, _original, RegistryValueKind.String);
        }
    }
}
