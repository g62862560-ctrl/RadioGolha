# Radio Golha - C# edition

A C# / WinForms port of `radio_golha.py` (Python + Tkinter + python-vlc),
playing the same stream with the same behaviour:

* default stream: `mms://www.radiogolha.net/RadioGolha`
* volume slider applied live, last URL remembered in `%USERPROFILE%\.radio_golha_url`
* station list (seeded from `radios.json`): pick a station from the combo box to
  fill the URL; add your own, import a list, remove, and favorite stations (★)
  into a favorites-only filter
* optional auto-play: "Auto-play on select" plays a station as soon as you pick
  it, "Auto-play on startup" plays the last station when the app opens
  (both persisted in `%USERPROFILE%\.radio_golha_settings.json`)
* "Open in VLC" button that launches the installed VLC player
* buffering percentage from VLC events, shown centered inside the progress bar
  only while the stream is really opening (indeterminate bar before that)
* "really playing" detection for mms live streams (playback clock / media stats)
* "Still connecting... (slow stream)" hint after 30 s
* elapsed listening time centered inside the progress bar while playing (live
  streams report no duration, so it shows how long you have been listening)
* builds to a single self-contained `RadioGolha.exe` - no .NET runtime needed
  on the target machine
* only one instance can run at a time; launching again focuses the existing
  window instead of starting a second copy

## UI

The window is a compact 446 x 269 fixed-size dialog built from stock Windows
controls only: menu bar, station combo box + favorite star, label + text box,
native track bar, standard push buttons, progress bar and a status strip.
The percentage / elapsed time is painted in the middle of the progress bar
itself, so there is no separate readout taking up room. No custom colors,
fonts or drawn buttons - it follows the current Windows theme and DPI setting.

## Requirements

1. **VLC Media Player (64-bit)** - https://www.videolan.org/
   The app loads `libvlc.dll` from the installed VLC, so VLC must be installed
   and must match the app bitness (64-bit app -> 64-bit VLC).

   VLC is looked for in this order:
   1. the `InstallDir` value the VLC installer writes to the registry
      (`HKLM` and `HKCU`, both 64-bit and 32-bit views of `SOFTWARE\VideoLAN\VLC`)
   2. `C:\Program Files\VideoLAN\VLC`
   3. the `Program Files`, `Program Files (x86)` and `ProgramW6432` folders
   4. every directory on `PATH`

   The default install at `C:\Program Files\VideoLAN\VLC` is therefore found
   even when the registry entry is missing.
2. **.NET 8 SDK** - only to build. The produced exe is self-contained and needs
   no .NET runtime on the machine that runs it.

## Run

Double-click `run_radio_csharp.bat` in the parent folder. It always rebuilds
the standalone exe and then starts it:

```
RadioGolha\dist\RadioGolha.exe
```

## Build (standalone exe)

Double-click `build_radio_csharp.bat` in the parent folder, or:

```bat
dotnet publish RadioGolha\src\RadioGolha\RadioGolha.csproj -c Release -o RadioGolha\dist
```

`RadioGolha.csproj` is configured so every publish is already standalone:

* `win-x64`, `SelfContained=true` - the .NET runtime is bundled
* `PublishSingleFile=true` - one `.exe`, no other files
* `IncludeNativeLibrariesForSelfExtract=true` + compression

The result is the single file `RadioGolha\dist\RadioGolha.exe` (about 65 MB).
Copy it anywhere and run it; only **VLC Media Player (64-bit)** has to be
installed on that PC.

> `dotnet build` still works for quick compile checks, but it produces the
> usual multi-file `bin` folder. Use `dotnet publish` (or the batch file) when
> you want the standalone exe.

## Android version

`android/` is a native Kotlin port using **LibVLC for Android** (the same
engine, so `mms://` still works). It has the same station list (seeded from
`src/RadioGolha/radios.json` at build time), favorites, add / import / remove,
volume, auto-play options, buffering % / elapsed time in the progress bar and the
"really playing" mms detection. Playback runs in a foreground service, so it
keeps playing with the screen off. A notification has a Stop button. "Open in VLC"
hands the URL to VLC for Android if it is installed.

Build (JDK 17 + Android SDK; `local.properties` points at the SDK):

```bat
cd android
gradlew.bat testDebugUnitTest assembleRelease
```

The APKs end up in `android/app/build/outputs/apk/release/`, one per CPU type.
Most phones need `app-arm64-v8a-release.apk` (about 45 MB). Release builds are
signed with the debug key, so you can sideload them but not publish them to a store.

## Project layout

| File | Purpose | Python equivalent |
| --- | --- | --- |
| `Program.cs` | entry point, single-instance + focus, DPI/visual styles | `main()` |
| `RadioApp.cs` | window, playback, polling, progress bar, station list | `class RadioApp` |
| `TextProgressBar.cs` | stock progress bar with centered percentage/time text | (none) |
| `RadioStation.cs` | station model (name, url, favorite) | (none) |
| `StationStore.cs` | load/save/parse/merge station JSON | (none) |
| `StationDialog.cs` | Add station dialog (name + URL) | (none) |
| `VlcLocator.cs` | finds `libvlc.dll` / `vlc.exe` | `find_vlc_exe()` |
| `TimeText.cs` | milliseconds -> `mm:ss` / `hh:mm:ss` | `fmt_time()` |
| `radios.json` | seed station list (embedded resource) | (none) |

## Station list

Stations live in `%USERPROFILE%\\.radio_golha_stations.json` (a JSON array of
`{ "name", "url", "favorite" }`). The first run seeds that file from the
embedded `radios.json` (or from a `radios.json` placed next to the exe).

* **Add station...** (Ctrl+N) - enter a name and a stream URL.
* **Import stations...** (Ctrl+I) - merge a JSON file (plain array or
  `{ "stations": [...] }`). Duplicates (by URL, case-insensitive) are skipped;
  an imported station can mark an existing one as a favorite.
* **Remove station** - deletes the selected station.
* **★ / ☆** button - toggles the favorite flag of the selected station.
* **Show favorites only** - filters the combo box to favorites (not persisted).

Favorites are sorted to the top of the list and shown with a `★ ` prefix.

## Notes on the port

* Tkinter is replaced by WinForms, `python-vlc` by **LibVLCSharp** (same libVLC
  engine underneath, so `mms://` support and behaviour are unchanged).
* The URL is opened as an MRL when it has a scheme (`mms:`, `http:`, ...) and as
  a plain file path otherwise - the same rule python-vlc's `media_new()` uses.
* When VLC cannot be found, a normal warning dialog explains what to install,
  the status bar shows "VLC Media Player not found" and the Play/Stop buttons
  plus the volume slider are disabled.
