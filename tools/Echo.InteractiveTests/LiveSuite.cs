using System.Diagnostics;
using EchoTray;

namespace EchoTray.InteractiveTests;

/// <summary>
/// Drives the real Echo.exe rather than an in-process context: puts text on the clipboard,
/// presses the global shortcut while holding Ctrl+Alt down past the point Echo starts typing,
/// and checks what lands. Requires Echo to already be running.
/// </summary>
internal static class LiveSuite
{
    private const string Payload = "Echo live check: 1234 éà — done";

    public static void Run()
    {
        using var form = new Form
        {
            Text = "Echo live target",
            ClientSize = new Size(620, 180),
            StartPosition = FormStartPosition.CenterScreen,
            TopMost = true,
        };

        var box = new TextBox { Multiline = true, Dock = DockStyle.Fill, Font = new Font("Consolas", 11) };
        form.Controls.Add(box);

        form.Shown += async (_, _) =>
        {
            ClipboardHelper.SetText(Payload);
            form.Activate();
            box.Focus();

            if (!await Harness.WaitForForeground(form.Handle))
            {
                Harness.Check("live hotkey", false, "could not take focus");
                form.Close();
                return;
            }

            // A freshly built Echo.exe starts slowly (antivirus scans it), so wait until it
            // actually owns the shortcut rather than racing its startup.
            if (!await Task.Run(() => Harness.WaitForEchoToClaimHotkey(20000)))
            {
                Harness.Check("live hotkey", false, "Echo is not running, or never registered Ctrl+Alt+E");
                form.Close();
                return;
            }

            var timeline = new List<string>();
            var clock = Stopwatch.StartNew();
            using var watching = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                string last = string.Empty;
                while (!watching.IsCancellationRequested)
                {
                    IntPtr foreground = NativeMethods.GetForegroundWindow();
                    string line = $"0x{foreground:X} {WindowUtil.GetClassName(foreground)} \"{WindowUtil.GetTitle(foreground)}\"";
                    if (line != last)
                    {
                        timeline.Add($"{clock.ElapsedMilliseconds}ms {(foreground == form.Handle ? "[TARGET] " : string.Empty)}{line}");
                        last = line;
                    }

                    await Task.Delay(80);
                }
            });

            // Ctrl+Alt+E, with the modifiers deliberately held down for 1.2s afterwards so that
            // Echo's "release held modifiers" path is exercised the way a real key press would.
            Harness.SendKey(NativeMethods.VK_CONTROL, up: false);
            Harness.SendKey(NativeMethods.VK_MENU, up: false);
            Harness.SendKey((ushort)Keys.E, up: false);
            Harness.SendKey((ushort)Keys.E, up: true);
            await Task.Delay(1200);
            Harness.SendKey(NativeMethods.VK_MENU, up: true);
            Harness.SendKey(NativeMethods.VK_CONTROL, up: true);

            for (int i = 0; i < 60 && box.Text != Payload; i++)
            {
                await Task.Delay(250);
            }

            watching.Cancel();
            Harness.Check("live hotkey types the clipboard", box.Text == Payload, $"got \"{box.Text}\"");
            Harness.Note("      foreground timeline: " + string.Join(" | ", timeline));
            form.Close();
        };

        Application.Run(form);
    }
}
