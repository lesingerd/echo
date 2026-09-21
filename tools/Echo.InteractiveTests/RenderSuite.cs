using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using EchoTray;

namespace EchoTray.InteractiveTests;

/// <summary>
/// Writes pictures of the tray icon states and the settings dialog for eyeballing. The icon
/// geometry itself is asserted in the unit tests; these images are for judging how it looks.
/// </summary>
internal static class RenderSuite
{
    public static void RenderIcons(string outDir)
    {
        var states = new (EchoState State, int? Seconds)[]
        {
            (EchoState.Idle, null),
            (EchoState.Armed, null),
            (EchoState.Counting, 9),
            (EchoState.Counting, 27),
            (EchoState.Counting, 5),
            (EchoState.Typing, null),
        };

        int[] sizes = { 16, Math.Max(16, SystemInformation.SmallIconSize.Width) };
        const int Zoom = 4;
        const int Drawn = 16 * Zoom; // same on-screen size so the two DPIs compare directly
        int cell = Drawn + 12;

        using var sheet = new Bitmap((states.Length * cell) + 12, sizes.Length * 2 * cell + 12);
        using (var g = Graphics.FromImage(sheet))
        {
            g.Clear(Color.FromArgb(32, 32, 34));
            g.FillRectangle(Brushes.WhiteSmoke, 0, sheet.Height / 2, sheet.Width, sheet.Height / 2);
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            for (int s = 0; s < sizes.Length; s++)
            {
                int x = 12;
                foreach ((EchoState state, int? seconds) in states)
                {
                    (Icon icon, IntPtr handle) = TrayIconFactory.Create(state, seconds, sizes[s]);
                    using (Bitmap bmp = icon.ToBitmap())
                    {
                        g.DrawImage(bmp, x, 12 + (s * cell), Drawn, Drawn);
                        g.DrawImage(bmp, x, (sheet.Height / 2) + 12 + (s * cell), Drawn, Drawn);
                    }

                    icon.Dispose();
                    NativeMethods.DestroyIcon(handle);
                    x += cell;
                }
            }
        }

        string path = Path.Combine(outDir, "tray-states.png");
        sheet.Save(path, ImageFormat.Png);
        Harness.Check("tray icons render", true,
            $"sizes {string.Join(" and ", sizes)}px, shell uses {SystemInformation.SmallIconSize.Width}px -> {path}");
    }

    /// <summary>Builds the settings dialog off screen, saves a picture, and round-trips its values.</summary>
    public static void RenderSettingsDialog(string outDir)
    {
        var defaults = new AppSettings();
        Harness.Check("shipped defaults", defaults.CharDelayMs == 50 && defaults.JitterPercent == 10,
            $"{defaults.CharDelayMs}ms, {defaults.JitterPercent}% variation");

        try
        {
            var settings = new AppSettings
            {
                CharDelayMs = 7,
                JitterPercent = 40,
                Newlines = NewlineMode.ShiftEnter,
                Tabs = TabMode.Spaces,
                TabSpaces = 3,
                DelayPresets = new List<int> { 2, 5, 15 },
                Hotkey = "Ctrl+Shift+V",
            };

            using var form = new SettingsForm(settings.Clone());
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-4000, -4000);
            form.Show();
            Application.DoEvents();

            Harness.Check("settings dialog lays out",
                form.ClientSize.Width > 300 && form.ClientSize.Height > 300,
                $"{form.ClientSize.Width}x{form.ClientSize.Height}");

            using (var bmp = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                bmp.Save(Path.Combine(outDir, "settings-dialog.png"), ImageFormat.Png);
            }

            // Saving without touching anything must preserve what was loaded. TrySaveInto is
            // internal, so this needs no reflection.
            var roundTripped = new AppSettings();
            bool saved = form.TrySaveInto(roundTripped);

            form.Close();

            Harness.Check("settings dialog round-trip", saved
                && roundTripped.CharDelayMs == 7
                && roundTripped.JitterPercent == 40
                && roundTripped.Newlines == NewlineMode.ShiftEnter
                && roundTripped.Tabs == TabMode.Spaces
                && roundTripped.TabSpaces == 3
                && roundTripped.Hotkey == "Ctrl+Shift+V"
                && string.Join(",", roundTripped.DelayPresets) == "2,5,15",
                $"{roundTripped.CharDelayMs}ms, {roundTripped.Newlines}, {roundTripped.Tabs}x{roundTripped.TabSpaces}, "
                + $"{roundTripped.Hotkey}, [{string.Join(",", roundTripped.DelayPresets)}]");
        }
        catch (Exception ex)
        {
            Harness.Check("settings dialog lays out", false, ex.ToString());
        }
    }

    /// <summary>Logs every foreground change for 10s, to see what holds focus while Echo starts.</summary>
    public static void WatchForeground()
    {
        string last = string.Empty;
        var sw = Stopwatch.StartNew();

        while (sw.ElapsedMilliseconds < 10000)
        {
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);

            string line = $"0x{hwnd:X}  {WindowUtil.GetProcessName(pid),-24} [{WindowUtil.GetClassName(hwnd)}]  {WindowUtil.GetTitle(hwnd)}";
            if (line != last)
            {
                Console.WriteLine($"{sw.ElapsedMilliseconds,6}ms  eligible={WindowUtil.IsEligibleTarget(hwnd),-5} {line}");
                last = line;
            }

            Thread.Sleep(150);
        }
    }
}
