using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using static EchoTray.NativeMethods;

namespace EchoTray;

internal enum TypeResult
{
    Completed,
    Cancelled,
    WindowChanged,
    Blocked,
    NothingToType,
    Failed,
}

/// <summary>
/// Replays text as real keystrokes with SendInput, so it lands in fields that refuse a paste
/// (remote consoles, VM viewers, password-style boxes, terminals).
/// </summary>
internal static class TypingEngine
{
    private static readonly int InputSize = Marshal.SizeOf<INPUT>();
    private const int MaxBatch = 64;

    /// <summary>
    /// How long the target may lose the foreground before Echo gives up. Toasts, tooltips and
    /// slow-activating windows steal it for a moment; keystrokes are held back until it returns
    /// rather than sprayed at whatever is in front.
    /// </summary>
    private const int FocusGraceMs = 1500;

    /// <summary>Runs on a worker thread. <paramref name="target"/> is the window the keystrokes are meant for.</summary>
    public static TypeResult Type(string text, AppSettings settings, IntPtr target, CancellationToken token)
    {
        string body = Prepare(text, settings);
        if (body.Length == 0)
        {
            return TypeResult.NothingToType;
        }

        if (settings.ReleaseModifiers)
        {
            // A hotkey leaves Ctrl/Alt physically down; typing under them would fire shortcuts instead.
            ReleaseHeldModifiers(token);
        }

        _ = GetAsyncKeyState(VK_ESCAPE);

        var batch = new List<INPUT>(MaxBatch * 2);
        int i = 0;

        while (i < body.Length)
        {
            if (token.IsCancellationRequested)
            {
                return TypeResult.Cancelled;
            }

            if (settings.EscapeCancels && IsKeyDown(VK_ESCAPE))
            {
                return TypeResult.Cancelled;
            }

            if (settings.StopIfWindowChanges && !WaitForTarget(target, token))
            {
                return TypeResult.WindowChanged;
            }

            char c = body[i];
            if (c == '\n')
            {
                if (!SendNewline(settings.Newlines))
                {
                    return TypeResult.Blocked;
                }

                i++;
            }
            else if (c == '\t')
            {
                if (!SendVirtualKey(VK_TAB))
                {
                    return TypeResult.Blocked;
                }

                i++;
            }
            else
            {
                // With no per-character delay the characters go out in batches, which is much faster.
                batch.Clear();
                int units = 0;
                int limit = settings.CharDelayMs > 0 ? 1 : MaxBatch;

                while (i < body.Length && units < limit)
                {
                    char ch = body[i];
                    if (ch == '\n' || ch == '\t')
                    {
                        break;
                    }

                    AddUnicode(batch, ch);
                    i++;

                    if (char.IsHighSurrogate(ch) && i < body.Length && char.IsLowSurrogate(body[i]))
                    {
                        AddUnicode(batch, body[i]);
                        i++;
                    }

                    units++;
                }

                if (!Send(batch))
                {
                    return TypeResult.Blocked;
                }
            }

            Pace(settings, token);
        }

        return TypeResult.Completed;
    }

    /// <summary>
    /// Normalises line endings and applies the newline/tab preferences up front.
    /// Internal so it can be tested without a window to type into.
    /// </summary>
    internal static string Prepare(string text, AppSettings settings)
    {
        string normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        if (settings.TrimTrailingNewlines)
        {
            normalized = normalized.TrimEnd('\n');
        }

        var sb = new StringBuilder(normalized.Length);
        foreach (char c in normalized)
        {
            switch (c)
            {
                case '\n':
                    if (settings.Newlines != NewlineMode.Skip)
                    {
                        sb.Append(settings.Newlines == NewlineMode.Space ? ' ' : '\n');
                    }

                    break;

                case '\t':
                    if (settings.Tabs == TabMode.Spaces)
                    {
                        sb.Append(' ', settings.TabSpaces);
                    }
                    else if (settings.Tabs == TabMode.Tab)
                    {
                        sb.Append('\t');
                    }

                    break;

                default:
                    if (!char.IsControl(c))
                    {
                        sb.Append(c);
                    }

                    break;
            }
        }

        return sb.ToString();
    }

    private static void Pace(AppSettings settings, CancellationToken token)
    {
        int delay = settings.CharDelayMs;
        if (delay <= 0)
        {
            return;
        }

        if (settings.JitterPercent > 0)
        {
            int jitter = Math.Max(1, delay * settings.JitterPercent / 100);
            delay = Math.Max(0, delay + Random.Shared.Next(-jitter, jitter + 1));
        }

        if (delay <= 0)
        {
            return;
        }

        if (delay >= 16)
        {
            token.WaitHandle.WaitOne(delay);
            return;
        }

        // Below a scheduler tick, sleeping overshoots badly -- spin instead.
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalMilliseconds < delay && !token.IsCancellationRequested)
        {
            Thread.SpinWait(120);
        }
    }

    /// <summary>True once the target is in front again; false if it does not come back in time.</summary>
    private static bool WaitForTarget(IntPtr target, CancellationToken token)
    {
        if (target == IntPtr.Zero || GetForegroundWindow() == target)
        {
            return true;
        }

        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < FocusGraceMs)
        {
            if (token.IsCancellationRequested)
            {
                return false;
            }

            token.WaitHandle.WaitOne(25);

            if (GetForegroundWindow() == target)
            {
                return true;
            }
        }

        return false;
    }

    private static void ReleaseHeldModifiers(CancellationToken token)
    {
        ushort[] modifiers = { VK_SHIFT, VK_CONTROL, VK_MENU, VK_LWIN, VK_RWIN };

        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 700
               && !token.IsCancellationRequested
               && modifiers.Any(m => IsKeyDown(m)))
        {
            Thread.Sleep(15);
        }

        var stuck = new List<INPUT>(modifiers.Length);
        foreach (ushort modifier in modifiers)
        {
            if (IsKeyDown(modifier))
            {
                AddVirtual(stuck, modifier, up: true);
            }
        }

        Send(stuck);
    }

    private static bool SendNewline(NewlineMode mode)
    {
        if (mode != NewlineMode.ShiftEnter)
        {
            return SendVirtualKey(VK_RETURN);
        }

        var sequence = new List<INPUT>(4);
        AddVirtual(sequence, VK_SHIFT, up: false);
        AddVirtual(sequence, VK_RETURN, up: false);
        AddVirtual(sequence, VK_RETURN, up: true);
        AddVirtual(sequence, VK_SHIFT, up: true);
        return Send(sequence);
    }

    private static bool SendVirtualKey(ushort vk)
    {
        var sequence = new List<INPUT>(2);
        AddVirtual(sequence, vk, up: false);
        AddVirtual(sequence, vk, up: true);
        return Send(sequence);
    }

    private static void AddUnicode(List<INPUT> inputs, char c)
    {
        inputs.Add(KeyboardInput(0, c, KEYEVENTF_UNICODE));
        inputs.Add(KeyboardInput(0, c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
    }

    private static void AddVirtual(List<INPUT> inputs, ushort vk, bool up)
    {
        inputs.Add(KeyboardInput(vk, 0, up ? KEYEVENTF_KEYUP : 0));
    }

    private static INPUT KeyboardInput(ushort vk, ushort scan, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        u = new INPUTUNION
        {
            ki = new KEYBDINPUT
            {
                wVk = vk,
                wScan = scan,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = IntPtr.Zero,
            },
        },
    };

    private static bool Send(List<INPUT> inputs)
    {
        if (inputs.Count == 0)
        {
            return true;
        }

        INPUT[] array = inputs.ToArray();
        uint sent = SendInput((uint)array.Length, array, InputSize);

        // A short count means the input was blocked -- almost always UIPI against an elevated window.
        return sent == (uint)array.Length;
    }
}
