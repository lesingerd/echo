# Echo `>|<`

A Windows tray tool that sits quietly in the notification area and **types the clipboard into
another window as real keystrokes**. Useful anywhere a paste is refused or ignored: remote
consoles, VM viewers, KVM/IPMI sessions, installer fields, terminals, password-less kiosk boxes,
games, and forms that block Ctrl+V.

Echo never touches the clipboard contents — it reads the text and replays it as key presses.

## Using it

| Action | What happens |
| --- | --- |
| **Left click the tray icon** | Echo arms itself and snapshots the clipboard. Click the window you want, and Echo types into it. Left click again to stand down. |
| **Right click → Type now** | Types into whatever window is already in front. |
| **Right click → Type after a delay** | Counts down (3/5/10/30 s or a custom value), then types into whatever is in front when it reaches zero. The tray icon shows the seconds remaining. |
| **Right click → Recent clipboard** | The last ten things you copied. Click one to type it into the next window you click, or hover it for the other ways to send it. |
| **Ctrl+Alt+E** (configurable) | Same as "Type now", from the keyboard. |
| **Esc** | Cancels a countdown, or stops typing part-way. |

## Clipboard history

Echo remembers the last ten pieces of text you copied, newest first, so you can go back to
something you copied earlier without re-copying it. Each entry in the **Recent clipboard** submenu
shows a preview; hovering one opens its own menu:

| Action | |
| --- | --- |
| **Type into the next window I click** | The default — this is what a plain click on the entry does. |
| **Type now** | Sends it to the window you were in before opening the menu. |
| **Type after a delay** | Counts down first, same as the main menu. |
| **Put back on the clipboard** | Makes that entry the current clipboard again. |

Entries always type *their own* text, whatever happens to be on the clipboard at the time.

The history lives in memory only and is **never written to disk** — people copy passwords, and a
tray tool has no business keeping them. It starts empty each time Echo runs, and **Clear history**
empties it on demand. Set the size to `0` in Settings to turn the feature off entirely.

The tray icon colours tell you the state:

| Colour | State |
| --- | --- |
| Blue `>\|<` | Idle |
| Orange `>\|<` | Armed — waiting for you to click a window |
| Orange number | Counting down |
| Green `>\|<` | Typing |

The top of the right-click menu always shows a preview of what Echo would type.

## Safety behaviour

- Echo only types into the window it was aimed at. If the active window changes part-way, it
  **stops** rather than spraying the rest of your clipboard into the wrong place.
- A window that briefly loses focus (a notification, a slow-opening app) gets a grace period
  before Echo gives up, so transient focus changes do not silently cancel the job.
- The taskbar, Start menu and other shell surfaces are never treated as a target window.
- If you triggered Echo with the keyboard, it waits for you to let go of Ctrl/Alt/Shift before
  typing, so your text does not turn into a string of keyboard shortcuts.

## Building and running

Requires the .NET 9 SDK (or Visual Studio 2022 17.12+) to build, and the
[.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0) to run.

```bash
dotnet build
```

Produce a single self-contained-ish `Echo.exe` (about 300 KB, uses the installed runtime):

```bash
dotnet publish Echo.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

If you would rather it not depend on an installed runtime at all (bigger, roughly 150 MB):

```bash
dotnet publish Echo.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish
```

Then run `publish\Echo.exe`. Only one copy runs at a time.

**Echo adds itself to Windows startup the first time it runs**, so it is there after a reboot. It
only does this once: if you turn **Start with Windows** off in the right-click menu, it stays off.
If the registered copy is later moved or deleted, the next run repairs the entry to point at
itself; an entry that still points at a real file is left alone.

> **Windows 11 hides new tray icons by default.** If you do not see `>|<`, click the `^` chevron in
> the taskbar corner and drag Echo out, or turn it on under
> Settings → Personalisation → Taskbar → Other system tray icons.

## Settings

Right-click the tray icon → **Settings**. Stored as JSON at `%APPDATA%\Echo\settings.json`.

| Setting | Default | Notes |
| --- | --- | --- |
| Delay between characters | 50 ms | Steady, deliberate typing that slow fields keep up with. Lower it to go faster; `0` sends characters in batches, as fast as the target accepts them. |
| Random variation | 10 % | Jitters the delay so the typing is not perfectly metronomic. |
| Pause before typing starts | 400 ms | Lets the window you clicked settle and take focus. |
| Line breaks | Press Enter | Or Shift+Enter, replace with a space, or skip — chat boxes usually want Shift+Enter. |
| Tabs | Press Tab | Or expand to spaces, or skip. Tab moves between fields in many apps. |
| Stop waiting after | 60 s | How long Echo stays armed before standing down. |
| Delay menu presets | 3, 5, 10, 30 | The entries in the "Type after a delay" submenu. |
| Clipboard history | 10 | Entries kept in memory. `0` turns the history off. |
| Stop if the active window changes | on | The safety guard described above. |
| Esc cancels | on | |
| Release held Ctrl/Alt/Shift first | on | |
| Show notifications | on | Errors are shown regardless. |
| Global shortcut | Ctrl+Alt+E | Needs at least one modifier. |

## Limitations

- **Elevated windows.** Windows blocks synthetic input sent from a normal process to a window
  running as administrator. Echo notices this and says so; run Echo as administrator to type into
  elevated apps.
- **Text only.** Files, images and other clipboard formats are ignored.
- Characters are sent as Unicode key events, so they do not depend on your keyboard layout.
  Emoji and accented characters work.

## Project layout

```
Echo.sln / Echo.csproj    WinForms app, net9.0-windows, no NuGet dependencies
src/Program.cs            Entry point, single-instance guard
src/EchoTrayContext.cs    Tray icon, menu, and the idle/armed/counting/typing state machine
src/TypingEngine.cs       SendInput keystroke replay, pacing, cancellation, focus guarding
src/ClipboardHistory.cs   The last few copied strings, in memory only
src/ForegroundWatcher.cs  SetWinEventHook wrapper for "the next window you click"
src/WindowUtil.cs         What counts as a real target window
src/TrayIconFactory.cs    Draws the >|< mark at the shell's icon size, tinted by state
src/SettingsForm.cs       Preferences dialog (built in code, no designer files)
src/AppSettings.cs        Settings model, JSON load/save
tools/generate-icon.ps1   Regenerates assets/echo.ico (run with Windows PowerShell)
```

The tray icon is drawn at runtime so it stays sharp at any DPI; `assets/echo.ico` is the matching
application icon and is regenerated by the script above.
