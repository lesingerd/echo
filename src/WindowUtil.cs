using System.Diagnostics;
using System.Text;

namespace EchoTray;

/// <summary>Decides what counts as a real "next window" and describes it for the user.</summary>
internal static class WindowUtil
{
    // Taskbar, start menu, alt-tab and desktop chrome. Activating these is not a target choice.
    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland",
        "Progman",
        "WorkerW",
        "ForegroundStaging",
        "MultitaskingViewFrame",
        "XamlExplorerHostIslandWindow",
        "TaskListThumbnailWnd",
        "Windows.UI.Input.InputSite.WindowClass",
    };

    // CoreWindow is used by ordinary store apps too, so it is only excluded for these hosts.
    private static readonly HashSet<string> ShellProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "StartMenuExperienceHost",
        "SearchHost",
        "SearchApp",
        "ShellExperienceHost",
        "TextInputHost",
        "LockApp",
    };

    /// <remarks>
    /// Deliberately does not test IsWindowVisible. The foreground event fires while a window is
    /// still being shown, so a window the user just opened or restored reports itself as not
    /// visible and would be skipped. Echo confirms the target is still in front after the settle
    /// delay instead, which covers anything that only flickers into the foreground.
    /// </remarks>
    public static bool IsEligibleTarget(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0 || pid == (uint)Environment.ProcessId)
        {
            return false;
        }

        string className = GetClassName(hwnd);
        if (ShellClasses.Contains(className))
        {
            return false;
        }

        return !className.Equals("Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase)
               || !ShellProcesses.Contains(GetProcessName(pid));
    }

    public static string GetClassName(IntPtr hwnd)
    {
        var buffer = new StringBuilder(256);
        int length = NativeMethods.GetClassName(hwnd, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString(0, length) : string.Empty;
    }

    public static string GetTitle(IntPtr hwnd)
    {
        var buffer = new StringBuilder(512);
        int length = NativeMethods.GetWindowText(hwnd, buffer, buffer.Capacity);
        return length > 0 ? buffer.ToString(0, length) : string.Empty;
    }

    public static string GetProcessName(uint pid)
    {
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.ProcessName;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    /// <summary>Short human label for a window, e.g. "Notepad" or a trimmed title.</summary>
    public static string Describe(IntPtr hwnd)
    {
        string title = GetTitle(hwnd);
        if (title.Length > 40)
        {
            title = string.Concat(title.AsSpan(0, 39).TrimEnd(), "…");
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        string process = GetProcessName(pid);
        return string.IsNullOrEmpty(process) ? "that window" : process;
    }
}
