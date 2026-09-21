using System.Diagnostics;
using EchoTray;

namespace EchoTray.InteractiveTests;

/// <summary>
/// Drives Echo's real tray menu with the mouse: hover to open the history submenus, click a
/// remembered entry, and check the menu-driven arm does not fire at the window behind it.
/// </summary>
internal static class MenuSuite
{
    private const string First = "history entry ONE";
    private const string Second = "history entry TWO";

    public static void Run(string outDir)
    {
        ClipboardHelper.SetText(First);
        Harness.Pump(200);

        using var startupGuard = new StartupGuard();
        var context = new EchoTrayContext();
        Harness.Pump(300);

        ClipboardHelper.SetText(Second);
        Harness.Pump(600); // let WM_CLIPBOARDUPDATE arrive

        ClipboardHistory history = ContextProbe.History(context);
        Harness.Check("clipboard listener records history",
            history.Entries.Count == 2 && history.Entries[0] == Second && history.Entries[1] == First,
            string.Join(" | ", history.Entries.Select(e => ClipboardHelper.Preview(e, 20))));

        ContextMenuStrip menu = ContextProbe.Menu(context);
        ToolStripMenuItem historyItem = ContextProbe.HistoryItem(context);

        // A target window of our own takes focus first, so the menu's focus bounce lands on it
        // rather than on whatever app the user happens to have open.
        string resultFile = Harness.TargetResultFile(outDir);
        File.Delete(resultFile);
        Process? behind = Harness.StartTargetWindow(outDir);
        Harness.Pump(2500);

        var anchor = new Point(Screen.PrimaryScreen!.Bounds.Width / 2, Screen.PrimaryScreen.Bounds.Height / 2);

        // --- hovering opens the submenus ---
        menu.Show(anchor);
        Harness.Pump(300);

        Harness.HoverAt(Harness.CentreOf(menu, historyItem), 700);
        Harness.Check("hovering opens the history submenu", historyItem.DropDown.Visible,
            $"{historyItem.DropDownItems.Count} items");

        ToolStripMenuItem? firstEntry = FindEntry(historyItem, First);
        if (firstEntry is null)
        {
            Harness.Check("history entry is listed", false, DescribeItems(historyItem));
            menu.Close();
            Harness.Kill(behind);
            context.Dispose();
            return;
        }

        Harness.HoverAt(Harness.CentreOf(historyItem.DropDown, firstEntry), 700);
        Harness.Check("hovering an entry opens its action submenu", firstEntry.DropDown.Visible,
            DescribeItems(firstEntry));

        // --- clicking the entry itself runs the default action ---
        Harness.ClickAt(Harness.CentreOf(historyItem.DropDown, firstEntry));
        Harness.Pump(600);

        Harness.Check("clicking an entry runs the default action",
            ContextProbe.State(context) == EchoState.Armed && ContextProbe.PendingText(context) == First,
            $"state={ContextProbe.State(context)}, pending=\"{ClipboardHelper.Preview(ContextProbe.PendingText(context), 24)}\"");
        Harness.Check("clicking an entry closes the menu", !menu.Visible, $"menu.Visible={menu.Visible}");

        // --- an armed Echo must not fire at the window that regains focus when the menu closes ---
        Harness.Pump(2000);
        Harness.Check("menu close does not trigger the armed echo",
            ContextProbe.State(context) == EchoState.Armed, ContextProbe.State(context).ToString());
        Harness.Check("nothing was typed into the window behind the menu",
            Harness.ReadTargetResult(outDir).Length == 0,
            File.Exists(resultFile) ? $"\"{Harness.ReadTargetResult(outDir)}\"" : "(no file)");

        // --- and the next window the user picks gets the entry's own text, not the clipboard ---
        // The first window stays open: closing it would hand focus to some third app, and the
        // armed Echo would quite correctly fire at that instead.
        Process? window = Harness.StartTargetWindow(outDir);

        string typed = string.Empty;
        for (int i = 0; i < 80 && typed != First; i++)
        {
            Harness.Pump(250);
            typed = Harness.ReadTargetResult(outDir);
        }

        Harness.Check("a history entry types its own text, not the clipboard", typed == First,
            $"clipboard held \"{Second}\", window received \"{typed}\"");

        CheckTypeNowFromTheMenu(menu, historyItem, anchor, outDir);

        Harness.Kill(window);
        Harness.Kill(behind);
        Harness.Pump(200);
        context.Dispose();
    }

    /// <summary>"Type now" must find the window behind the menu, not Echo's own menu window.</summary>
    private static void CheckTypeNowFromTheMenu(
        ContextMenuStrip menu, ToolStripMenuItem historyItem, Point anchor, string outDir)
    {
        File.Delete(Harness.TargetResultFile(outDir));
        Harness.Pump(400);

        menu.Show(anchor);
        Harness.Pump(300);
        Harness.HoverAt(Harness.CentreOf(menu, historyItem), 700);

        ToolStripMenuItem? secondEntry = FindEntry(historyItem, Second);
        if (secondEntry is null)
        {
            Harness.Check("second history entry is listed", false, DescribeItems(historyItem));
            menu.Close();
            return;
        }

        Harness.HoverAt(Harness.CentreOf(historyItem.DropDown, secondEntry), 700);

        ToolStripMenuItem? typeNow = secondEntry.DropDownItems
            .OfType<ToolStripMenuItem>()
            .FirstOrDefault(i => i.Text == "Type now");

        if (typeNow is null)
        {
            Harness.Check("submenu offers Type now", false, DescribeItems(secondEntry));
            menu.Close();
            return;
        }

        Harness.ClickAt(Harness.CentreOf(secondEntry.DropDown, typeNow));

        // The window still holds the text from the previous step, so the new text lands after
        // it -- what matters is that exactly Second was appended.
        string expected = First + Second;
        string nowTyped = string.Empty;
        for (int i = 0; i < 60 && nowTyped != expected; i++)
        {
            Harness.Pump(250);
            nowTyped = Harness.ReadTargetResult(outDir);
        }

        Harness.Check("Type now from the menu reaches the window behind it", nowTyped == expected,
            $"window received \"{nowTyped}\"");
    }

    private static ToolStripMenuItem? FindEntry(ToolStripMenuItem historyItem, string text) =>
        historyItem.DropDownItems
            .OfType<ToolStripMenuItem>()
            .FirstOrDefault(i => i.Text?.Contains(text, StringComparison.Ordinal) == true);

    private static string DescribeItems(ToolStripMenuItem item) =>
        string.Join(" / ", item.DropDownItems.OfType<ToolStripItem>().Select(i => i.Text));
}
