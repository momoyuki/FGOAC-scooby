# FGOAC scooby {{VERSION}} - FGO Arcade in English

An English patch for the FGO Arcade local platform. It puts the game itself into English - menus,
tutorial, story text, battle screens, shops, help - and replaces the Chinese front end with an
English one called **FGOAC scooby**, which starts the server, manages your account and builds your
card deck.

This is not a game download. It does nothing on its own: you need an existing FGO Arcade local
platform install (Cloud23333's package at V1.01 or V1.02, with or without his latest update; V1.02 is
the one to be on), and the patch is applied on top of it.

## Install

1. Unzip this package into your FGO Arcade folder - the one that holds `App` and `Server`.
2. Run **FGOAC scooby**.
3. Click Yes on the Windows permission prompt.

The first start does the rest: it installs the English files, checks that the game can write to its
folders, allows the game and the local server through Windows Firewall, creates the account
**Master** with a full Servant and Craft Essence roster, and sets the display to windowed 1280x720
on your main monitor. Then press **Play**. The game needs about a minute to reach the title screen.

If the firewall rules could not be created - a company policy or security suite can block that -
Windows asks you instead the first time you press Play: once for the local server
(`Server\python\python.exe`) and once for the game (`App\ago.exe`). Allow both. Those Windows
questions can open **behind** the game window, so if the game seems stuck at a black screen, look
for them in the task bar.

Later starts skip all of that and go straight to Play.

**GUIDE_EN.pdf** in this folder is the full guide: requirements, how to get Servants, controls, the
exchange shops, and troubleshooting.

Three things to know before you start:

- **The game cannot run from drive E: or Y:.** Its own file hook sends every path on those drives to
  the cabinet data mount, and the game stops with ERROR 4104. Any other drive is fine.
- **The game has to run as administrator.** That is the one Windows prompt you cannot skip; without
  it the game shows ERROR 4105 about ninety seconds after boot.
- **The game needs a CPU with F16C.** Intel Core 3rd generation (2012) or newer, and any Ryzen, has
  it; older Pentium and Celeron chips do not, and the game stops with 0xC000001D at start. There is
  no fix on that CPU.

If you would rather install the files without the launcher, run `Apply-EN-Patch.ps1` yourself - it
takes `-InstallRoot`, and `-Rollback` puts back everything it replaced. Once you are happy with the
install you can delete `payload\`, `Apply-EN-Patch.ps1` and `manifest.json` to get the space back,
but keep them if you may want to roll back later.

## What works

- The game text: over 65,000 translated lines - story, quests, Servant profiles, skills, items, menus.
- The game artwork: over 220 rebuilt sprite archives - title, tutorial, terminal, formation, battle
  HUD, results, shops, synthesis, present box, master missions, rankings, help pages, title editor.
- The launcher: Play, Cards, Photo Mode, Mods, Events, Accounts, Battle Debug, Controls, Settings and
  About, all in English, with the official English card names and Craft Essence effects in the card
  library.
- Offline single player: the tutorial, solo sorties, the terminal, the exchange shops, synthesis,
  My Room, rankings and the title editor.
- The in-game summon, drawing from the local server with the weights set on the launcher's Draw
  Rates page. The Account page can also grant a full roster in one click.

## Using the launcher

- **Play.** Play plays on the server on this PC. The Status sentence says where things stand -
  whether the local server is running - or, in one sentence, why Play cannot start yet.
- **The Master menu.** Click the Master line on Play: it lists your local accounts to switch between,
  with New account at the bottom; Manage beside the line opens the accounts page.
- **Deck codes.** Copy code on Cards puts the deck on the clipboard as one line of text to paste in
  chat. Paste a code into the box beside it and press Import code: the deck loads and is saved as a
  new loadout, never over one you have.
- **Save a report.** Open the Status menu, the button at the end of the Status line on Play, and
  choose Save a report. It saves one zip, on the Desktop unless you pick another place, with your
  card number taken out. Nothing is uploaded: you attach the file yourself.
- **Controls.** Input picks keyboard, an Xbox or other controller, or a PlayStation 5 controller,
  with dead zone, rumble and a controller test. Bindings ties a key, a controller button or a
  combination such as Ctrl+F5 to a button on the game's screen: click a box and press what you want.
  Cursor sets the pointer and the mouse button that taps the screen. With Cloud23333's latest update
  the camera is his: Battles in free camera turns the view with the mouse in battle, and Controller
  camera turns it with the right stick, with a lock-on button. The controller methods of his update
  hide the mouse pointer in battle, story scenes included; choose Keyboard and mouse to use the mouse
  there. Changes count from the next Play.
- **PC play.** Settings > PC play: after a lost quest Continue and Withdraw wait for you, and popups
  stay open until you answer them; both are on. No GP purchase screen, Story pages wait for you and
  No menu countdown are off until they have been tried in the game; turn them on to try them.
  Changes count from the next Play.
- **Widescreen.** Settings > Display > Widescreen: Widescreen picture fits the picture to screens
  that are not 16:9, and Full width on taller screens keeps the whole width on 16:10 and 4:3; both
  are on. HUD and menus at the screen edges is off while it is being tried.
- **The other Settings cards.** Display also has a graphics card pick. Battle effects hides the dark
  or white screen during Noble Phantasms, hides the white screen flash, and has an Effect opacity
  slider for how strongly the game's own battle effects are drawn. Audio. Local server has the local
  server and its settings. Diagnostics and Help lists every game error code with its fix. The
  Operator menu keys (F1, F2) switch for ERROR 8404 is on the Controls page.
- **Pages of their own.** Photo Mode, Mods (the MOD folder of Cloud23333's latest update), Events
  (which limited-time events the server on this PC offers), Accounts and Battle Debug (battle test
  switches for local play, including Stop the battle timer) each have a page in the sidebar.
- **Logs.** Logs on Play opens the drawer, which shows one log at a time: Launcher, Game or Server.
  It remembers your choice.

## What does not work

- **No online service.** Everything runs against the local server on your PC.
- **Some artwork is still Japanese**: event and boss banners, parts of the event screens and shops,
  and some buttons and labels. They are pictures, not text; the game's text is English.
- The game is built for NVIDIA cards and wants a current driver; a black screen usually means the
  driver, not the patch. AMD and Intel graphics run through a compatibility layer, picked under
  Settings > Display, "Graphics layer". Cloud23333's layer from his latest update is the default, on
  his update and without it (the launcher puts it in by itself on a PC with no NVIDIA card); the layer
  FGOAC scooby shipped with 1.1.0 and fluphus's AMD layer are there to choose. fluphus's layer holds
  the game at 60 FPS; set AMD Wait for Vertical Refresh to Off, Unless Application Specifies.

## Reporting a problem

Say which screen you were on and what you expected, and attach the zip from **Save a report** in
the Status menu on Play. It holds the logs from the `logs` folder next to `App`, among them:

| File | What it shows |
| --- | --- |
| `fgo-last-launch.log` | the launch script, from start to exit code |
| `fgozh.log` | the English file hook: which files the game actually loaded |
| `server-control.log`, `artemis-stderr.log`, `mariadb.log` | the local server and its database |
| `environment-check.txt` | the environment check, written by the Diagnostics and Help page |

**Settings > Diagnostics and Help** in the launcher lists the game's error codes with the fix for
each one; check it first, the answer is usually there.

## Credits

- **Cloud23333** wrote the FGO Arcade local platform - the server package, the launcher
  (`FGOLocalPlatform`) that FGOAC scooby is built from, and the file hook this patch loads its English
  through. None of this exists without his work. His package is free; if anyone sold it to you, ask
  for your money back.
- The **FGO Arcade wiki** and **Atlas Academy** were used for the official English names of
  Servants, Craft Essences, skills and items, so the game and the launcher call everything what the
  English release calls it.
- FGO Arcade is Sega's and TYPE-MOON's. This is a fan translation, applied to files you already
  have, and it is not sold.
