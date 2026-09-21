namespace EchoTray.InteractiveTests;

/// <summary>
/// Echo's interactive test driver. Every mode here needs a real, unlocked desktop session: the
/// suites take the foreground, move the mouse and synthesise key presses. Nothing in this
/// project runs under "dotnet test" -- see tests/README.md.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string outDir = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal))
            ?? Path.Combine(Path.GetTempPath(), "echo-interactive-tests");

        Directory.CreateDirectory(outDir);

        // The same set-up ApplicationConfiguration.Initialize() generates, spelled out: Echo's
        // own generated copy is visible here through InternalsVisibleTo, which makes the
        // unqualified name ambiguous. Matching the app matters -- the DPI mode decides what
        // sizes the suites see when they measure and click menu items.
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.SetDefaultFont(new Font("Segoe UI", 9f));

        // Modes that produce no pass/fail results of their own.
        if (args.Contains("--help") || args.Contains("-h"))
        {
            Console.WriteLine(Usage);
            return 0;
        }

        if (args.Contains("--target"))
        {
            TargetWindow.Run(Harness.TargetResultFile(outDir));
            return 0;
        }

        if (args.Contains("--watch"))
        {
            RenderSuite.WatchForeground();
            return 0;
        }

        string log = "interactive.log";

        if (args.Contains("--render"))
        {
            RenderSuite.RenderIcons(outDir);
            RenderSuite.RenderSettingsDialog(outDir);
            log = "render.log";
        }
        else if (args.Contains("--menu"))
        {
            MenuSuite.Run(outDir);
            log = "menu.log";
        }
        else if (args.Contains("--arm"))
        {
            ArmSuite.Run(outDir);
            log = "arm.log";
        }
        else if (args.Contains("--live"))
        {
            LiveSuite.Run();
            log = "live.log";
        }
        else
        {
            RenderSuite.RenderSettingsDialog(outDir);
            TypingSuite.Run(includeWindowFiltering: args.Contains("--full"));
            log = "typing.log";
        }

        Harness.Report(outDir, log);
        return Harness.Failures;
    }

    private const string Usage = """
        Echo interactive tests -- needs a real desktop session; steals focus and types.

          Echo.InteractiveTests [outDir] [mode]

        Modes:
          (none)     Typing accuracy, newline and tab handling, cancellation, wrong-target
                     safety, and the settings dialog. Types only into its own window.
          --full     As above, plus the window filter check, which launches Notepad.
          --arm      End to end: arm Echo, let another window take focus, check the text lands.
          --menu     Drives the real tray menu with the mouse: history submenus, default action,
                     the focus bounce when the menu closes, and "Type now".
          --live     Drives an already running Echo.exe through its global shortcut.
          --render   Writes tray-states.png and settings-dialog.png for eyeballing. No input.
          --target   Internal: the stand-in window the other suites type into.
          --watch    Logs foreground window changes for 10 seconds.

        Exit code is the number of failed checks. Results are also written to <outDir>.
        """;
}
