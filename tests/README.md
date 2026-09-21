# Testing Echo

Echo is tested in two halves, because half of what it does cannot be checked without a real
desktop: replaying keystrokes, taking focus, and driving a tray menu with the mouse.

| | Where | Needs a desktop | Runs in CI |
| --- | --- | --- | --- |
| Unit tests | `tests/Echo.Tests` | no | yes |
| Interactive suites | `tools/Echo.InteractiveTests` | **yes** | never |

Both see Echo's `internal` types through `InternalsVisibleTo` in `Echo.csproj`, and reference the
Echo project rather than compiling its sources a second time.

## Unit tests

```bash
dotnet test tests/Echo.Tests/Echo.Tests.csproj
```

Fast, headless and safe to run anywhere. They cover the parts of Echo that are decisions rather
than side effects:

- `HotkeyParserTests` — parsing and formatting shortcuts, and refusing unusable ones
- `AppSettingsTests` — defaults, clamping, preset tidying, cloning, JSON round-trip
- `ClipboardHistoryTests` — ordering, de-duplication, capacity and size limits
- `TypingEnginePreparationTests` — what Echo decides to type, before any key is pressed
- `TrayIconFactoryTests` — icon drawing, including that two digit countdowns draw both digits

These never call `AppSettings.Load` or `Save`. Those read and write the real
`%APPDATA%\Echo\settings.json`, and a test run has no business touching the settings of whoever
is running it — the JSON round-trip goes through `ToJson`/`TryFromJson` instead.

## Interactive suites

> **These take over the machine while they run.** They steal focus, move the mouse pointer,
> synthesise key presses and open windows. Do not run them on a machine you are using for
> anything else, and do not run them over a connection where focus is shared. Every suite types
> only into windows it created itself: if it cannot take focus, it reports a failure rather than
> typing into whatever you had open.

```bash
dotnet build Echo.sln
tools\Echo.InteractiveTests\bin\Debug\net9.0-windows\Echo.InteractiveTests.exe <outDir> [mode]
```

`<outDir>` receives the log and any screenshots. The exit code is the number of failed checks.

| Mode | What it does |
| --- | --- |
| *(none)* | Typing accuracy at two pacing modes, newline and tab preferences, cancellation part way, wrong-target safety, and the settings dialog. Types only into its own window. |
| `--full` | The above plus the window filter check, **which launches Notepad**. |
| `--arm` | End to end: arms Echo, lets another window take the foreground, checks the text lands there and Echo returns to idle. |
| `--menu` | Drives the real tray menu with synthesised mouse input: hovering opens the history submenus, clicking an entry runs the default action and closes the menu, the focus bounce when the menu closes does not fire an armed Echo, and "Type now" reaches the window behind the menu. |
| `--live` | Drives an already running `Echo.exe` through its global shortcut, holding Ctrl+Alt down past the point Echo starts typing so the modifier-release path is exercised. Start Echo first. |
| `--render` | Writes `tray-states.png` and `settings-dialog.png` for eyeballing. No input, no focus. |
| `--target` | Internal: the stand-in window the other suites type into. |
| `--watch` | Logs foreground window changes for ten seconds. Useful when a focus problem is suspected. |

### Two things worth knowing before changing these

**The startup guard is not optional.** Constructing an `EchoTrayContext` runs Echo's first-run
startup registration, which points the `HKCU\...\Run` key at whatever executable is running — the
test binary, if nothing intervenes. Any suite that creates a context must wrap it in a
`StartupGuard`, which snapshots that value and puts it back.

**Reflection is confined to `ContextProbe`.** `InternalsVisibleTo` covers Echo's internal types,
so nothing needs reflection for visibility. A few members the suites read — `_state`,
`_pendingText`, `_history` — are genuinely private: they are the running state machine, not an
interface, and widening them for tests would leak test concerns into the app. That reflection
lives in one file so a rename fails in a single obvious place.

## CI

`.github/workflows/build.yml` builds the whole solution on `windows-latest` and runs **only** the
unit tests. The interactive project is built, never executed: it is marked `IsTestProject=false`
so `dotnet test` cannot pick it up, and CI invokes the unit test project by path in any case.
