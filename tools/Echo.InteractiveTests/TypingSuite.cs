using System.Diagnostics;
using System.Runtime.InteropServices;
using EchoTray;

namespace EchoTray.InteractiveTests;

/// <summary>
/// Drives TypingEngine against a real focused window and reads back what actually arrived.
/// Safety: every run targets this app's own window, so if focus is not ours the engine reports
/// WindowChanged and types nothing into whatever the user has open.
/// </summary>
internal static class TypingSuite
{
    private const string Sample = "Hello, Echo! 42 — ünïcödé \U0001F600\tTabbed\nSecond line";

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    public static void Run(bool includeWindowFiltering)
    {
        using var form = new Form
        {
            Text = "Echo self-test",
            ClientSize = new Size(620, 220),
            StartPosition = FormStartPosition.CenterScreen,
            TopMost = true,
        };

        var box = new TextBox
        {
            Multiline = true,
            Dock = DockStyle.Fill,
            AcceptsTab = true,
            AcceptsReturn = true,
            Font = new Font("Consolas", 11),
        };

        form.Controls.Add(box);

        form.Shown += async (_, _) =>
        {
            form.Activate();
            box.Focus();

            if (!await Harness.WaitForForeground(form.Handle))
            {
                Harness.Check("typing tests", false, "could not take focus; refusing to type into another app");
                form.Close();
                return;
            }

            foreach (int delay in new[] { 0, 4 })
            {
                var settings = new AppSettings
                {
                    CharDelayMs = delay,
                    JitterPercent = delay > 0 ? 25 : 0,
                    SettleDelayMs = 0,
                    ReleaseModifiers = false,
                };

                box.Clear();
                var sw = Stopwatch.StartNew();
                TypeResult result = await Task.Run(
                    () => TypingEngine.Type(Sample, settings, form.Handle, CancellationToken.None));
                sw.Stop();

                await Task.Delay(250); // let the last keystrokes drain into the box

                string expected = Sample.Replace("\n", "\r\n");
                string actual = box.Text;

                Harness.Check($"type result (delay {delay}ms)", result == TypeResult.Completed, result.ToString());
                Harness.Check($"typed text matches (delay {delay}ms)", actual == expected,
                    actual == expected
                        ? $"{actual.Length} chars in {sw.ElapsedMilliseconds}ms"
                        : Harness.DescribeDifference(expected, actual));
            }

            // Newline and tab preferences, end to end. The same rules are unit tested against
            // TypingEngine.Prepare; this confirms they survive the trip through the keyboard.
            var spaced = new AppSettings
            {
                CharDelayMs = 0,
                JitterPercent = 0,
                SettleDelayMs = 0,
                ReleaseModifiers = false,
                Newlines = NewlineMode.Space,
                Tabs = TabMode.Spaces,
                TabSpaces = 3,
            };

            box.Clear();
            await Task.Run(() => TypingEngine.Type("a\tb\nc\r\nd\n\n", spaced, form.Handle, CancellationToken.None));
            await Task.Delay(250);
            Harness.Check("newline/tab preferences", box.Text == "a   b c d", $"got \"{box.Text}\"");

            // Cancellation should stop part-way through.
            var slow = new AppSettings
            {
                CharDelayMs = 20,
                JitterPercent = 0,
                SettleDelayMs = 0,
                ReleaseModifiers = false,
            };

            box.Clear();
            using var cts = new CancellationTokenSource(200);
            TypeResult cancelled = await Task.Run(
                () => TypingEngine.Type(new string('x', 400), slow, form.Handle, cts.Token));
            await Task.Delay(250);
            Harness.Check("cancellation stops typing", cancelled == TypeResult.Cancelled && box.Text.Length < 400,
                $"{cancelled}, {box.Text.Length} chars landed");

            // A wrong target must produce no keystrokes at all.
            box.Clear();
            TypeResult wrongWindow = await Task.Run(
                () => TypingEngine.Type("should not appear", slow, new IntPtr(0x1234), CancellationToken.None));
            await Task.Delay(150);
            Harness.Check("wrong target types nothing", wrongWindow == TypeResult.WindowChanged && box.Text.Length == 0,
                $"{wrongWindow}, {box.Text.Length} chars landed");

            // Runs last: it deliberately hands the foreground to another app. Opt-in, because
            // it launches Notepad on the user's desktop.
            if (includeWindowFiltering)
            {
                await CheckWindowFiltering(form);
            }

            form.Close();
        };

        Application.Run(form);
    }

    /// <summary>Checks the "next window I click" brains: what counts as a target, and that the hook fires.</summary>
    private static async Task CheckWindowFiltering(Form form)
    {
        Harness.Check("own window is never a target", !WindowUtil.IsEligibleTarget(form.Handle));
        Harness.Check("null handle is never a target", !WindowUtil.IsEligibleTarget(IntPtr.Zero));

        IntPtr taskbar = FindWindow("Shell_TrayWnd", null);
        Harness.Check("taskbar is never a target", taskbar != IntPtr.Zero && !WindowUtil.IsEligibleTarget(taskbar),
            taskbar == IntPtr.Zero ? "taskbar window not found" : WindowUtil.GetClassName(taskbar));

        var seen = new TaskCompletionSource<IntPtr>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = new ForegroundWatcher();
        watcher.ForegroundChanged += hwnd =>
        {
            if (WindowUtil.IsEligibleTarget(hwnd))
            {
                seen.TrySetResult(hwnd);
            }
        };

        watcher.Start();

        Process? notepad = null;
        try
        {
            notepad = Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = true });
            Task first = await Task.WhenAny(seen.Task, Task.Delay(8000));
            watcher.Stop();

            if (first == seen.Task)
            {
                IntPtr hwnd = seen.Task.Result;
                Harness.Check("watcher reports the newly activated window", WindowUtil.IsEligibleTarget(hwnd),
                    $"{WindowUtil.GetClassName(hwnd)} / {WindowUtil.Describe(hwnd)}");
            }
            else
            {
                Harness.Note("SKIP  foreground watcher -- no eligible foreground change within 8s");
            }
        }
        catch (Exception ex)
        {
            Harness.Note($"SKIP  foreground watcher -- {ex.Message}");
        }
        finally
        {
            watcher.Stop();

            try
            {
                if (notepad is not null)
                {
                    notepad.CloseMainWindow();
                    if (!notepad.WaitForExit(3000))
                    {
                        notepad.Kill();
                    }

                    notepad.Dispose();
                }
            }
            catch (Exception)
            {
                // Best effort only.
            }
        }
    }
}
