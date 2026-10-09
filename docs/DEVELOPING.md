# Developing

How the launcher is built, where its parts come from, and what must not be changed when the
translation is carried forward. `README.md` is the player-facing document; this is the one for
working on the code.

`src\` is a decompile of the platform's managed front end (`FGOLocalPlatform`, V1.01, by
Cloud23333) with the compile fixes listed below applied and the user-facing strings translated.
The deliverable is a self-contained single-file host published from `src\` and named
`FGOAC scooby.exe`.

## Layout

| Path | Contents |
| --- | --- |
| `src\` | buildable C#/XAML project (`FGOLocalPlatform.csproj`). `DeckReaderUI\` and `DeckReaderUI.Kancolle\` are the author's card-reader namespaces, compiled into the same assembly; the names are kept as decompiled so a new upstream version can be diffed against them |
| `compat\` | fluphus's OpenGL compatibility layer for AMD and Intel graphics under `amd-shim\`, taken verbatim with its licence, plus `fgoglcompat.dll`, the older layer it replaced (see its README) |
| `overlay\` | English replacements for files that live outside the assembly, laid out by their path relative to the install root |
| `patch\` | `Apply-EN-Patch.ps1` (the installer players run) and `Build-Manifest.ps1` (writes the `manifest.json` it checks against) |
| `dist\` | build output, `FGOAC scooby.exe` (not tracked) |
| `docs\` | player guide and this file |
| `package\` | the README that ships inside the release zip |
| `tools\` | `Sign-Release.ps1` (makes the release keys and signs a release zip), the developer checks `Test-Updater.ps1` (the signed updater), `Test-UpgradeCheck.ps1` (the update backup, the rollback and the upgrade check), `Test-CommunityServer.ps1`, `Test-Logs.ps1` (redaction, the daily server-control.log and the Report), `Test-Input.ps1` (the bindings file, the touch gate, the chord rule and the input layer against a hidden window), `Test-DeckCode.ps1` (the deck code writer and reader), `Test-Cards.ps1` (the card folder a deck names, on throwaway installs of game 11.00 and Cloud23333's 1.10 update, the card list the Draw Rates page reads, an English name and Craft Essence effect text for every card of his 11.51 tables, the game's English set name for each card new in 11.51 and each Heroic Spirit Exhibition Attire card, and its English set effect text for each Craft Essence new or changed in 11.51), `Test-BattleEffects.ps1` (the Battle effects keys in `graphics-effects.ini` and `segatools.ini`), `Test-BattleDebug.ps1` (the Battle Debug page's `battle-debug.ini`), `Test-GpuPreference.ps1` (the Display card's GPU preference, on a throwaway registry key), `Test-GpuCard.ps1` (the graphics card pick on a game folder with Cloud23333's gpu-runtime: his probe's list parsed from fixed output, his three keys saved as his launcher saves them and resolved by his own FGO_GpuRouting.ps1) and `Test-PcPort.ps1` (the cabinet patch table read statically against the bytes of `ago.exe` 11.00 and 11.51, and its signatures found in both images under a header it does not know, needing no process; then the patcher's site-selection and refusal rules against a stand-in child process), `Test-GameBuild.ps1` (the English patch and the game language follow the game build read from `App\ago.exe`), `Test-GpuCompat.ps1` (the Graphics layer list writes only the layers it ships), `Test-MasterLevel.ps1` (Max Master Level beside a stand-in account tool), `Test-Game1151.ps1` (Play on a game folder with Cloud23333's 1.10 update, through his own launch script), `Test-Coexistence.ps1` (nothing the package carries or the launcher writes is a file of Cloud23333's 1.10 package), `Test-AccountTransfer.ps1` (Export all and Import of the local accounts through Cloud23333's own transfer tool, on an SQLite stand-in for the Aime database), `Test-Graphics.ps1` (the Display card's scene settings of Cloud23333's update, read back through his own launch script's block), `Test-EventSwitches.ps1` (the Events page's switches against Cloud23333's catalogue, read back by his own server code), `build_event_names.py` (writes `src\FGOLocalPlatform.EventNames.json`, the English titles of his catalogue's events, from the English import; run it when either changes), `Test-PhotoBlocks.ps1` (the Photo Mode panels' numeric fields and what they write into the shared-memory blocks of Cloud23333's photo hook, both versions, on blocks this process creates in his layout), `Test-Sidebar.ps1` (the sidebar's pages, each one file in its feature folder, and the Settings cards, on a main window built in memory and never shown), `Test-Mods.ps1` (the Mods page's MOD system, Cloud23333's code, against the files his hooks read: music through his ffmpeg.exe, `1_MOD\runtime.ini` byte for byte against his own `FGO_ModRuntime.ps1`, a package exported and imported again under his import checks, dialogue, portraits, Master portraits and loading images built from the game's own files), `Test-ModsPage.ps1` (the Mods page on game 11.00 and on his update, built in memory and never shown), `Test-EnvironmentCheck.ps1` (the environment check on throwaway folders under Windows PowerShell 5.1 and PowerShell 7: the graphics cards listed through Cloud23333's probe where his update installed it, and his touch key conflict and database file checks), `Test-DialogSink.ps1` (every message box, dialog, window and shell open of the launcher goes through `DialogSink`; each suite dot-sources `DialogSink.ps1`, which routes them to an in-memory list instead of the screen, and ends with the check that no window was shown), and `upgrade-check.ps1`, which proves a release keeps the protected set |
| `build.cmd` | `dotnet build -c Release` (compile check only) |
| `publish.cmd` | publish + copy to `dist\FGOAC scooby.exe` |
| `deploy.cmd` | copy the published launcher into an install |

The checks that read Cloud23333's 1.10 update find it through `FGOAC_CLOUD_UPDATE`: set it to the folder his update
unpacks into, the one that holds `文件清单.json` and `payload\`. Without it they stop with exit code 2 and name the
missing file.

## Build

```
publish.cmd
```

Requirements: the .NET SDK (10.x; it builds the `net6.0-windows`
target and restores the 6.0 reference and runtime packs from nuget.org on the first run) and
Windows 10 or 11 x64. The package dependencies, `ZstdSharp.Port` 0.8.8 and `BCnEncoder.Net` 2.3.0 (MIT,
the BC7 encoder of the Mods page's sprites), come from nuget.org too, so nothing has to be present
on the machine beyond the SDK. The published host carries its own .NET runtime, so a player does
not have to install one.

Deploy into an install:

```
deploy.cmd <install root>
```

`AssemblyName` stays `FGOLocalPlatform`: pack URIs in code and XAML are built from it, and the
process name the diagnostics tooling looks for is still `FGOLocalPlatform`. Only the file is renamed.

## Re-deriving from a new author release

When the author ships a new `FGOLocalPlatform.dll`, the English build has to be re-derived rather
than patched - his `FGOLocalPlatform.exe` is a self-contained single-file bundle that carries its own
copy of the managed assembly and ignores the sibling DLL.

1. Decompile the new assembly with `ilspycmd` 10.1, installed as a dotnet global tool
   (`dotnet tool install -g ilspycmd`):

   ```
   ilspycmd -p --decompile-baml -o <newdir> <path>\FGOLocalPlatform.dll
   ```

2. Re-apply the fixes below, then diff `<newdir>` against `src\` to see what the author changed and
   port the translation forward.

### Compile fixes the decompile needs

| # | File | Fix |
| --- | --- | --- |
| 1 | `FGOLocalPlatform.csproj` | `TargetFramework` `net6.0` -> `net6.0-windows` (`UseWPF` requires the Windows TFM) |
| 2 | `FGOLocalPlatform.csproj` | add `ApplicationDefinition` for `app.xaml` and `Page` items for every other XAML file; explicit `Compile` items with `EnableDefaultItems=false` |
| 3 | `FGOLocalPlatform.csproj` | `platform.ico` from `EmbeddedResource` to `Resource` (they are addressed by pack URI, which reads `.g.resources`). The two JSON tables stay `EmbeddedResource` - they are read with `GetManifestResourceStream` |
| 4 | XAML file names | `FGOLocalPlatform.MainWindow.xaml` -> `mainwindow.xaml`, and the same for every other XAML file: the resource key comes from the file path and the originals are `mainwindow.baml` etc. at the root |
| 5 | `app.xaml` | leave out `StartupUri` (the original sets it in the generated `App.InitializeComponent`, which the BAML decompiler does not reproduce): `App.OnStartup` in `Startup\App.cs` opens the main window itself, after the game-folder check and the update backup |
| 6 | `MainWindow.cs`, `ThemedMessageBox.cs` | 6 occurrences of `((Rect)(ref x)).Height` / `((Size)(ref x)).Width` reduced to `x.Height` / `x.Width` |
| 7 | `MainWindow.cs` | remove the generated `_CreateDelegate` helper (PresentationBuildTasks regenerates it from `mainwindow.xaml`) |
| 8 | `PhotoWindow.cs`, `MainWindow.cs` | `(val - 70 > 1 && val - 116 > 5) \|\| 1 == 0` -> `val != Key.LWin && val != Key.RWin && (val < Key.LeftShift || val > Key.RightAlt)`. Semantic fix: the original excludes `Key.LWin`/`Key.RWin` and the six modifier keys from key binding; the signed rendering rejects almost every key |
| 9 | `MainWindow.cs` | remove the dead `int num; _ = num - 1; _ = 1;` |
| 10 | `MainWindow.cs` | `new(string, double)[10]` -> `new (string Name, double Value)[10]`; the LINQ query below it addresses `item.Name` / `item.Value` |

Expect zero errors and zero warnings: the project builds with `Nullable` set to `annotations`, so the
decompile's `?` annotations compile without `CS8632`, and the only suppression left is a `#pragma` around
the XInput structures. A fresh decompile also brings back the leftovers the tree has been scrubbed of:
`//IL_` comments, numeric `Key` codes, `(DependencyObject)(object)` casts, `_ = 1;` statements and the
unwired handlers, so expect to remove them again.

### Things the translation must not change

- Every XAML `Tag` value, every `SelectedIndex` order, and the item order of any combo box whose
  index is persisted (cursor mode, input mode, movement and controller-number selectors, the two
  summon filters, photo-mode weapon mode and bone array).
- Resolution captions: they are parsed back with `^\s*(\d{3,4})\s*[xX×]\s*(\d{3,4})\s*$`, so they
  must stay in `1920x1080` form.
- The JSON keys and field names of `FGOLocalPlatform.CardNames.json` (`SVT#####` / `CE#####`,
  `Japanese` / `Chinese`) and `FGOLocalPlatform.CraftEffects.json` (`CE#####`, `Normal`,
  `NormalJapanese`, `Maximum`, `MaximumJapanese`) - English goes into the existing `Chinese`,
  `Normal` and `Maximum` fields.
- Config keys and enum values shared with `FGOAC_Launcher.ps1`, Cloud23333's `FGO_Launcher.ps1` and the author's tooling
  (`windowed` / `borderless` / `exclusive`, `keyboard` / `xinput`, `16:9`, the `graphics` block).
- Interpolation holes in format strings, `StringFormat` placeholders, and the `|` in the
  `OpenFileDialog` filter.

## Publishing a release

The launcher checks `https://api.github.com/repos/<owner>/<repo>/releases/latest` once per start and
after every game, and installs from the release's own assets, so a release has to carry all three of
them. Every release is signed, and the launcher installs it only when its `.zip.sig` verifies under a
key in `ReleaseKeys`.

1. Set the version in `src\FGOLocalPlatform.csproj` (`<Version>`). Nothing else holds a version
   number: the About page and the updater read it from the assembly, and `package.ps1` reads it back
   off the built launcher.
2. `publish.cmd`, then `package.ps1 -GameRoot <install root> -AddOnDll <build folder>\fgoacaddon.dll`.
   That writes `release\FGOAC-scooby-v<ver>.zip` and `FGOAC-scooby-v<ver>.zip.sha256` beside it.
3. Sign the zip: `pwsh -File tools\Sign-Release.ps1 -Zip release\FGOAC-scooby-v<ver>.zip -Key
   <primary key file>`. It writes `FGOAC-scooby-v<ver>.zip.sig`, one line per key file given, each
   an ECDSA P-256 signature over `FGOAC-scooby <ver> <SHA-256 of the zip>`, and rewrites the
   `.zip.sha256` beside it.
4. Tag the commit `v<ver>` (for example `v1.3.0`) and push the tag.
5. Create the GitHub release on that tag and upload **all three** files as assets: the zip, its
   `.zip.sig` and its `.zip.sha256`. The updater takes the zip only under the exact name its tag
   gives, `FGOAC-scooby-v<ver>.zip` with the tag's version written without leading zeros, and needs
   that name plus `.sig` beside it. It installs only when one line of the `.sig` verifies under one
   of `ReleaseKeys` in `src\FGOLocalPlatform\UpdateSettings.cs`; otherwise it logs why in
   `logs\update.log` and installs nothing. It never reads the `.zip.sha256`, which is kept only for
   launchers 1.2.0 and older: they check the zip against it and ignore the `.sig`.
6. `src\FGOLocalPlatform\UpdateSettings.cs` holds the owner and repository the launcher asks
   (`githubuser420x` / `FGOAC-scooby`).

Updating a running launcher: the patch script cannot overwrite the executable that is running it, so
it stages the new one as `FGOAC scooby.exe.new`. The launcher then writes `%TEMP%\update-swap.cmd`,
which waits for it to close, moves the staged file into place and starts it again.

The protected set is the player's files no release, update or start of the launcher may lose:
`Server\data\`, `Server\state\`, `DEVICE\aime.txt`, `DEVICE\community\`, `DEVICE\print\players\`,
`App\deck.json`, `App\deck-loadouts\`, `App\fgo-launcher.json`, `DEVICE\runtime\` and the player's mods under `App\1_MOD\` (not `Dialogue\Catalog\`
and `Tools\`, which Cloud23333's update ships), named in
`src\FGOLocalPlatform\Update\ProtectedSet.cs`. The updater refuses a release with an entry in it, and one with an
entry under `App\1_MOD\`, where Cloud23333's MOD page keeps the player's packages (his updater never ships or deletes
them)
(`Updater.GameFolder.cs`, mirrored in `Apply-EN-Patch.ps1`) and refuses to install while a program runs
from `App\` or `Server\` of the game folder. Before its window opens, the launcher copies the set but the
database to `backup\pre-<version>\` once per version (`ProtectedSet.TakeBackup`, called from `App`); the
launcher writes its own files in the set - the deck file, the loadouts, the settings file - through
`AtomicFile`, which keeps the previous file as `.bak`. The runtime ini is written by the launch script.

## Assets that are not code

- `src\platform.ico` is the game's own icon, icon group 0 of `App\ago.exe`, pulled out by
  `src\assets\extract-icon.ps1`. It is the executable icon, the window icon and the mark in the top
  bar. The game ships one 32x32 frame, so that is what the file holds.
- `docs\GUIDE_EN.pdf` is printed from `docs\GUIDE_EN.md`: the markdown rendered to an HTML page with the
  guide's own stylesheet, then Chrome headless with `--print-to-pdf`. Reprint it whenever the markdown
  changes; the package carries both.
- The UI font is Geist and Geist Mono, bundled under `src\Fonts` with their `OFL.txt` licence
  (`src\app.xaml:44-45`); Yu Gothic UI and Microsoft YaHei UI are the fallback for characters Geist
  does not cover.

## Overlay

`overlay\` mirrors the install root. The release packager copies it into the payload. The launch script and
its two checks carry their own names, so neither Cloud23333's updater nor this one overwrites the other's
files; the launcher runs these, and his launcher keeps running his.

| Overlay file | Why |
| --- | --- |
| `App\FGOAC_EnvironmentCheck.ps1` | its whole stdout is the environment-check panel |
| `App\FGOAC_Launcher.ps1` | the launch script Play runs on game 11.00: two lines reach the launcher log panel |
| `App\FGOAC_StartupChecks.ps1` | one line reaches the startup failure dialog |
| `Server\tools\fgoac_master_level.py` | Max Master Level: raises the Master's EXP to the top of the game's level table, loading and saving the profile through Cloud23333's `fgo_account.py` |

On game 11.00 only, the package also installs English copies of Cloud23333's 1.02 server and account scripts; his
1.10 update replaces them with his own, and the installer leaves them out on any other build (`$Game1100Only` in
`patch\Apply-EN-Patch.ps1`, `GameBuild.Only1100Files`):

| Overlay file (11.00 only) | Why |
| --- | --- |
| `Server\Start-FGOLocalServer.ps1` | three lines reach the server log panel; runs `App\FGOAC_StartupChecks.ps1` |
| `Server\Stop-FGOLocalServerWhenIdle.ps1` | also recognises `FGOAC scooby.exe` as a running front end |
| `Server\tools\fgo_account.py` | every `message` it emits is shown verbatim by the account page |
| `Server\tools\fgo_server_config.py` | port and address validation errors on the server page |
| `Server\artemis\titles\fgo\data\summon_candidates.json` | the acquisition notes shown in the Draw Rates status line. The Japanese card names and quest titles in that file stay as they are - the column they feed is labelled Japanese Name |

Apart from those, nothing in `overlay\` carries the name of a file Cloud23333's package ships or removes;
`tools\Test-Coexistence.ps1` checks that against his 1.10 file list.

On any game build but 11.00, Play runs his own `App\FGO_Launcher.ps1`, read afresh at every Play, so each launch
rule he adds is kept without a change here (`Session\LaunchScript.cs`). With the add-on installed, Play writes
`App\FGOAC_Launcher.generated.ps1`: his script with two lines added before `$launchArguments.Add($agoPath)`, the
line that adds ago.exe to the launch, which put `-k App\fgoacaddon.dll` after his hooks. With HUD and menus at the
screen edges on, two more lines there put `[touch] inputWidth` and `inputHeight` back to the game's resolution through
his own `Set-IniValue`, after his script has reset them to 16:9; when his script no longer has the names they use,
they are left out and the Play status says touch over the whole screen is off for that start. When that line is not
there exactly once his script runs as it is, and the Play status says the add-on is off for that start. The `[pcport]`,
`[widescreen]`, `[camera]` and `[cursor]` keys that `FGOAC_Launcher.ps1` writes into the runtime ini go into
`App\segatools.ini` key by key instead, which his script copies to the runtime ini. `tools\Test-Game1151.ps1` checks
this against his 1.10 scripts.

The English set for that build, `App\Localization\en-fgoac`, carries `fgoac-overrides.json`: `{"format": 1, "files":
{"rom/<path>": "<SHA-256>"}}`, each override by its path under the set with the SHA-256 of the Japanese file
`App\rom\<path>` it was made from. At every start the launcher checks them (`EnglishSet.cs`): an override whose game
file has changed is renamed `<name>.fgoac-off`, so that screen shows the game's own text, and back once the file
matches again. A game file is hashed only when its size or time differs from the last check; `App\fgoac-state.json`
keeps those with the exe header and Cloud23333's `update-*.json` markers, and a change of either is logged as his
update.

## The English patch

`patch\Apply-EN-Patch.ps1` is what a player runs, and what the launcher runs for them on first
start. It works against a release package laid out like this, which is also what unzipping the
package into the game folder produces:

```
FGOAC scooby.exe
Apply-EN-Patch.ps1
manifest.json
payload\App\zh\...
payload\App\FGOAC_EnvironmentCheck.ps1
payload\Server\...
```

`manifest.json` maps each install-relative path to its SHA-256, and carries the patch version and a
`manifestHash` over the whole list. `patch\Build-Manifest.ps1 -PackageRoot <dir> -Version <v>` writes
it from a staged package; `package.ps1` calls it, so the manifest always describes the bytes that
ship.

The apply run finds the install (its own folder, then the parent, then a scan of the fixed drives,
then a folder picker), refuses drive E: and Y:, refuses to run while the game, a launcher or the
local server is open from that folder, backs up every file it replaces to `_en-patch-backup\<timestamp>\`, copies (the 11.00 English set
`App\zh` only onto game 11.00, read from the build of `App\ago.exe`), checks the copies against the
manifest, sets `chineseEnabled` in `App\fgo-launcher.json` at a first install on game 11.00 and writes
`App\fgoac-patch.json` with the version and the manifest hash. A second run with the same manifest
does nothing. `-Rollback` restores the newest backup, using the `en-patch-restore.json` written
beside it to also remove the files the patch added, then puts back the launcher's own files the
update backup `backup\pre-<version>\` marks, keeping each file it replaces as `.bak`, and renames that
backup so the next install of the version takes a new one. Exit codes are listed at the top of the
script.

Accounts, decks, `Server\state`, the database and the rest of `App\fgo-launcher.json` are never
written, except by `-Rollback` as above; `Apply-EN-Patch.ps1` refuses a manifest that lists a path
under any of them.
