namespace EchoTray;

/// <summary>Converts between "Ctrl+Alt+E" and the modifier/virtual-key pair RegisterHotKey wants.</summary>
internal static class HotkeyParser
{
    public static bool TryParse(string text, out uint modifiers, out uint virtualKey)
    {
        modifiers = 0;
        virtualKey = 0;

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var key = Keys.None;
        foreach (string token in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (token.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= NativeMethods.MOD_CONTROL;
                    break;

                case "alt":
                    modifiers |= NativeMethods.MOD_ALT;
                    break;

                case "shift":
                    modifiers |= NativeMethods.MOD_SHIFT;
                    break;

                case "win":
                case "windows":
                    modifiers |= NativeMethods.MOD_WIN;
                    break;

                default:
                    if (key != Keys.None || !TryParseKey(token, out key))
                    {
                        return false;
                    }

                    break;
            }
        }

        // A bare key would swallow that key system-wide, so at least one modifier is required.
        if (key == Keys.None || modifiers == 0)
        {
            return false;
        }

        virtualKey = (uint)key;
        return true;
    }

    public static string Format(Keys keyData)
    {
        var parts = new List<string>(4);

        if ((keyData & Keys.Control) == Keys.Control)
        {
            parts.Add("Ctrl");
        }

        if ((keyData & Keys.Alt) == Keys.Alt)
        {
            parts.Add("Alt");
        }

        if ((keyData & Keys.Shift) == Keys.Shift)
        {
            parts.Add("Shift");
        }

        parts.Add(KeyName(keyData & Keys.KeyCode));
        return string.Join("+", parts);
    }

    private static bool TryParseKey(string token, out Keys key)
    {
        key = Keys.None;

        // Enum.TryParse would read a bare "1" as the numeric enum value, so digits are handled first.
        if (token.Length == 1 && char.IsAsciiDigit(token[0]))
        {
            key = Keys.D0 + (token[0] - '0');
            return true;
        }

        return Enum.TryParse(token, ignoreCase: true, out key) && key != Keys.None;
    }

    private static string KeyName(Keys code) => code switch
    {
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (code - Keys.D0))).ToString(),
        _ => code.ToString(),
    };
}
