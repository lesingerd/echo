using System.Runtime.InteropServices;

namespace EchoTray;

/// <summary>The clipboard is shared state and another process can hold it open, so every read retries.</summary>
internal static class ClipboardHelper
{
    public static string GetText()
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                return Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
            }
            catch (ExternalException)
            {
                Thread.Sleep(40);
            }
        }

        return string.Empty;
    }

    public static bool SetText(string text)
    {
        for (int attempt = 0; attempt < 6; attempt++)
        {
            try
            {
                Clipboard.SetText(text);
                return true;
            }
            catch (ExternalException)
            {
                Thread.Sleep(40);
            }
        }

        return false;
    }

    /// <summary>A one-line preview for the tray menu and balloon tips.</summary>
    public static string Preview(string text, int maxLength = 48)
    {
        string flat = text.Replace("\r\n", " ").Replace('\n', ' ').Replace('\t', ' ').Trim();
        while (flat.Contains("  ", StringComparison.Ordinal))
        {
            flat = flat.Replace("  ", " ", StringComparison.Ordinal);
        }

        if (flat.Length > maxLength)
        {
            flat = string.Concat(flat.AsSpan(0, maxLength - 1).TrimEnd(), "…");
        }

        return flat;
    }
}
