using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using EchoTray;

namespace EchoTray.InteractiveTests;

/// <summary>Result collection, timing, and the synthetic input the suites are built on.</summary>
internal static class Harness
{
    private static readonly List<string> Results = new();

    public static int Failures { get; private set; }

    public static void Check(string name, bool ok, string detail = "")
    {
        if (!ok)
        {
            Failures++;
        }

        Results.Add($"{(ok ? "PASS" : "FAIL")}  {name}{(detail.Length > 0 ? "  -- " + detail : string.Empty)}");
    }

    public static void Note(string line) => Results.Add(line);

    public static void Report(string outDir, string logName)
    {
        string text = string.Join(Environment.NewLine, Results);
        Console.WriteLine(text);

        try
        {
            Directory.CreateDirectory(outDir);
            File.WriteAllText(Path.Combine(outDir, logName), text);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"(could not write {logName}: {ex.Message})");
        }
    }

    // ---- synthetic input -------------------------------------------------

    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;

    public static void SendKey(ushort virtualKey, bool up)
    {
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.INPUTUNION
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = virtualKey,
                    dwFlags = up ? NativeMethods.KEYEVENTF_KEYUP : 0,
                },
            },
        };

        NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static void SendMouse(uint flags)
    {
        var input = new NativeMethods.INPUT
        {
            type = 0, // INPUT_MOUSE
            u = new NativeMethods.INPUTUNION { mi = new NativeMethods.MOUSEINPUT { dwFlags = flags } },
        };

        NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    public static void HoverAt(Point screenPoint, int settleMs)
    {
        Cursor.Position = screenPoint;
        Pump(settleMs);
    }

    public static void ClickAt(Point screenPoint)
    {
        Cursor.Position = screenPoint;
        Pump(80);
        SendMouse(MouseLeftDown);
        Pump(60);
        SendMouse(MouseLeftUp);
        Pump(200);
    }

    /// <summary>Keeps a menu's message loop alive while the suite drives it.</summary>
    public static void Pump(int milliseconds)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < milliseconds)
        {
            Application.DoEvents();
            Thread.Sleep(15);
        }
    }

    public static Point CentreOf(ToolStrip owner, ToolStripItem item)
    {
        Rectangle bounds = item.Bounds;
        return owner.PointToScreen(new Point(bounds.Left + (bounds.Width / 2), bounds.Top + (bounds.Height / 2)));
    }

    // ---- windows ---------------------------------------------------------

    public static async Task<bool> WaitForForeground(IntPtr handle)
    {
        for (int i = 0; i < 30; i++)
        {
            if (NativeMethods.GetForegroundWindow() == handle)
            {
                return true;
            }

            await Task.Delay(100);
        }

        return false;
    }

    /// <summary>Launches this driver again in --target mode, standing in for a window the user picks.</summary>
    public static Process? StartTargetWindow(string outDir) =>
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
        {
            ArgumentList = { "--target", outDir },
            UseShellExecute = true,
        });

    public static string TargetResultFile(string outDir) => Path.Combine(outDir, "arm-target.txt");

    public static string ReadTargetResult(string outDir)
    {
        string path = TargetResultFile(outDir);
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    public static void Kill(Process? process)
    {
        try
        {
            if (process is { HasExited: false })
            {
                process.Kill();
            }
        }
        catch (Exception)
        {
            // Best effort only.
        }
    }

    /// <summary>Echo is ready once our own attempt to grab the shortcut is refused.</summary>
    public static bool WaitForEchoToClaimHotkey(int timeoutMs)
    {
        const int ProbeId = 0x4321;
        var sw = Stopwatch.StartNew();

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (!NativeMethods.RegisterHotKey(IntPtr.Zero, ProbeId, NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, (uint)Keys.E))
            {
                return true;
            }

            NativeMethods.UnregisterHotKey(IntPtr.Zero, ProbeId);
            Thread.Sleep(150);
        }

        return false;
    }

    public static string DescribeDifference(string expected, string actual)
    {
        var sb = new StringBuilder();
        sb.Append($"expected {expected.Length} chars, got {actual.Length}; ");

        int limit = Math.Min(expected.Length, actual.Length);
        for (int i = 0; i < limit; i++)
        {
            if (expected[i] != actual[i])
            {
                sb.Append($"first difference at {i}: expected U+{(int)expected[i]:X4}, got U+{(int)actual[i]:X4}");
                return sb.ToString();
            }
        }

        sb.Append("common prefix matches");
        return sb.ToString();
    }
}
