using System.Diagnostics;
using EchoTray;

namespace EchoTray.InteractiveTests;

/// <summary>
/// The headline path: arm Echo (what a left click on the tray does), then let another process
/// take the foreground and check the text lands in it.
/// </summary>
internal static class ArmSuite
{
    private const string Payload = "Armed echo -> next window. 99 éà ok";

    public static void Run(string outDir)
    {
        string resultFile = Harness.TargetResultFile(outDir);
        File.Delete(resultFile);

        var startupGuard = new StartupGuard();
        var context = new EchoTrayContext();

        var start = new System.Windows.Forms.Timer { Interval = 900 };
        var poll = new System.Windows.Forms.Timer { Interval = 400 };
        Process? target = null;
        int waited = 0;

        var trail = new List<string>();
        string lastSeen = string.Empty;

        start.Tick += (_, _) =>
        {
            start.Stop();

            ClipboardHelper.SetText(Payload);
            ContextProbe.ArmForNextWindow(context);

            Harness.Check("arm switches Echo out of idle",
                ContextProbe.State(context) == EchoState.Armed, ContextProbe.State(context).ToString());

            target = Harness.StartTargetWindow(outDir);
            poll.Start();
        };

        poll.Tick += (_, _) =>
        {
            waited += poll.Interval;

            IntPtr foreground = NativeMethods.GetForegroundWindow();
            string snapshot = $"{ContextProbe.State(context)} fg=\"{WindowUtil.GetTitle(foreground)}\"";
            if (snapshot != lastSeen)
            {
                trail.Add($"{waited}ms {snapshot}");
                lastSeen = snapshot;
            }

            string got = Harness.ReadTargetResult(outDir);
            if (got != Payload && waited <= 25000)
            {
                return;
            }

            poll.Stop();
            Harness.Check("armed Echo types into the next window", got == Payload, $"got \"{got}\" after {waited}ms");
            Harness.Check("Echo returns to idle afterwards",
                ContextProbe.State(context) == EchoState.Idle, ContextProbe.State(context).ToString());
            Harness.Note("      trail: " + string.Join(" | ", trail));

            Harness.Kill(target);
            context.ExitThread();
        };

        start.Start();
        Application.Run(context);
        context.Dispose();
        startupGuard.Dispose();
    }
}
