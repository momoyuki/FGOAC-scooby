# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project follows
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Version numbers cover the whole release: the launcher, the English game files it installs, and the
installer that puts them there.

## [Unreleased]

Works on Cloud23333's V1.01 and V1.02, with or without his latest update. Unzip this release over
your game folder and run the launcher, or take it through Install on Play. Your accounts, decks,
settings and whichever graphics layer you have stay as they are.

### Added

- Cloud23333's latest update is supported. On a game folder that has it, the game is in English
  through an English set of the launcher's own that his updater never touches, and the launcher's
  pages offer his new settings as described below.
- Accounts has Export all and Import on a game folder that has Cloud23333's latest update: every local
  account, with its card, whole save and pending gifts, goes into one .fgoaccounts.json file, the same
  file his launcher writes and reads. Import first says how many accounts the file holds and which of
  them replace the save of an account with the same card here, backs everything up, keeps the other
  accounts, and gives an account a new number when its number belongs to another card here.
- Events has Cloud23333's event switches on a game folder that has his latest update: his event list
  grouped into original events, reruns and permanent extra episodes and by kind, in English where the
  launcher's English text has the title, a tick per event, its place inside its kind, All on, All off
  and Release order. Each change is saved at once into the file his server and his launcher read.
- Settings > Display has the scene settings of Cloud23333's latest update on a game folder that has it:
  fog, reflections, draw distance up to 10 times as far, ultra-low quality for a PC that struggles, and
  the size and opacity of the treasure chests in battle. They are his own settings, with his defaults,
  and take effect the next time the game starts, from either launcher.
- Photo Mode works with the photo hook of Cloud23333's latest update as well as with the one the
  earlier game runs. With his latest update, Model and Weapons adds each eye's gaze, iris size and
  eyelid, the mouth's A, E, I, O and U or a preset, face, mouth and teeth shapes, the model's scale, a
  green screen in any color that keeps the characters and their effects, and the characters' shading
  and outlines; those controls stay hidden on the earlier game. With his latest update, Body Animation
  also lists the motions of the character's other motion packages and leaves out those of another
  form. Face Animation names each character by its Servant's name and reads its expressions without
  holding up the panel. The number fields can be dragged sideways to fine-tune, and the position
  sliders reach further and widen at their ends.
- Mods has Cloud23333's MOD workbench on a game folder that has his latest update, his own code in the
  launcher's look: music pools per scene with the list of where the game plays each track, matched
  loudness, loop points found on import and set on a waveform without re-encoding; dialogue on a quest's
  start, waves or end or on an event banner, once per account or every time, with an optional tap to
  continue screen; a portrait library that keeps a portrait while a line uses it; Master portraits and
  result portraits; loading images per quest; export of music, dialogue or Master portraits as a package,
  and import with a choice of package per kind. Everything is written in his file formats under
  App\1_MOD, so his launcher and his hooks read it. Music needs the converter his update installs.
- Controls has Cloud23333's controller features on a game folder that has his latest update: card
  selection with the controller, his controller camera with its sensitivity, vertical invert and
  lock-on button, his keyboard battle mode that turns the view with the mouse, and his controller
  mouse with its toggle, click, stick and speeds, each with his defaults and saved where his launcher
  saves them. Keep the mouse inside the game window, off by default, holds the pointer inside the game
  window while the game is in front.
- A button beside the Status sentence on Play does the step the sentence names: Start server or
  Stop server while the game is not running, Install when a newer version is out, and What's new
  after an update.
- Manage beside the Master line on Play opens Accounts in one click.
- Each card in the deck has - and + to take out or add one copy, and Remove all copies over its art
  when the pointer is on it.
- Each saved loadout has a button above the deck that loads it in one click.
- Deck codes: Copy code on the Cards page puts the deck on the clipboard as one line of text to paste
  in chat, with its order, copies and loadout name. Paste a code in the box beside it and press Import
  code or Enter: the deck is loaded with the cards your card folder has, the Status says how many were
  left out, and the deck is saved as a loadout under the code's name, or Imported and the date, never
  over a loadout you have. A damaged code, or one from a newer launcher, is refused with a sentence
  saying which.
- Controls has an Operator menu keys (F1, F2) switch, off by default. Turn it on only if the game
  shows ERROR 8404; the help for that error says how.
- Before a new version of the launcher first starts, it copies your deck, loadouts, settings, cards,
  player list and prints to backup\pre-<version> in the game folder, with a list of each file's size
  and checksum. Rolling the version back with Apply-EN-Patch.ps1 -Rollback puts your deck, loadouts
  and settings back from it, keeping the ones it replaces as .bak.
- Save a report, in the Status menu on Play, saves one zip to attach when you ask for help: the game
  log, the logs folder's logs, the settings, the newest crash dumps, Cloud23333's render logs and the
  size, version and SHA-256 of the graphics and hook files in App, with your card number taken out. It
  is saved where you choose, the Desktop by default, as FGOAC-report-<date>-<time>.zip. The report is
  never uploaded, and the launcher has no telemetry. The Status then names the file, with Open folder
  beside it.
- The Logs drawer shows one log at a time, picked with Launcher, Game or Server: the launcher's
  own lines, the game log, or the local server's log. It remembers the last choice.
- Bindings, on Controls, ties a key, a controller button or a chord (Ctrl+F5, LB+A) to a button on the
  game's screen - the title screen, the card check's Yes, the 300 GP card, Close on a popup, Grail
  War, Nationwide Match and Start - so pressing it taps that button while the game window is in
  front. Click a box and press to bind; Clear unbinds; every change saves at once. Keys and controller
  buttons that Input already uses for the game's own actions cannot be bound, and the card says which
  action has them.
- Settings > PC play: the continue window after a lost quest waits for Continue or Withdraw, with no
  countdown and no GP line, and popups stay open until you answer them. Both are on by default. The
  launcher makes these changes in the running game once its window appears, only on the game build
  they were written for and only where the game's bytes are the expected ones, and logs each one;
  the game files are not changed. Three more switches start off until they are tried in the game: no
  GP purchase screen at each play, story pages that wait for a click, Space or pad A, and no "Time
  Remaining" and "Returns to Title" countdown on the game's own menus.
- Settings > Display > Widescreen: the picture fits screens that are not 16:9, and on 16:10 and 4:3
  screens the scene keeps its full width, showing more above and below instead of cutting the sides.
  Both are on by default; Noble Phantasm screens are filled the same way by default. A third switch,
  off by default while it is being tried, moves the HUD to the screen edges using the add-on's
  built-in battle layout; DEVICE\runtime\widescreen-anchors.txt, a file the launcher creates once
  with instructions at the top, only adds to it or overrides one of its entries.
- Settings > Battle effects: Hide the dark or white screen during Noble Phantasms keeps the battle in
  full colour while a Noble Phantasm plays, whatever tint the servant's Noble Phantasm lays over the
  screen, and Hide the white screen flash stops the game's full-screen white fade. Both are off by
  default, the game's own look. An Effect opacity slider sets how strongly the game's own battle
  effects (flashes, tints) are drawn, 100 the game's own look by default; each saves as you tick or
  change it and counts from the next Play.
- Settings > Display has a Graphics card pick. On the earlier game it is the same setting as Windows'
  own Graphics settings page, set when you save or press Play and used the next time the game starts;
  when no pick is saved for the game, the launcher's first start sets High performance once. On a
  game folder that has Cloud23333's latest update it is his card pick: the cards his update's probe
  finds, with Refresh, saved the way his launcher saves them, so both launchers and his launch script
  use the same card; Automatic leaves the choice to the game's usual start.
- Cursor, on Controls, has a Tap with pick (left, right, middle or the two side buttons), the mouse
  button that taps the screen.
- Battle Debug has a Stop the battle timer switch, local play only: the battle's countdown stops while
  it is on.
- Input and Bindings refuse a key or controller button, including a pad combo, that the other card
  already uses for something else, with the reason.
- Turning the English patch off now stays off across an update; it is only turned on by itself the
  first time the launcher installs into a folder.
- Grant bonus items shows the result on Accounts instead of a dialog you had to close.
- The add-on, App\fgoacaddon.dll, which carries the widescreen picture and the story pages that wait
  for you, is loaded after the game's other hooks whenever it is in the App folder.

### Changed

- The pages are Play, Cards, Photo Mode, Mods, Events, Accounts, Battle Debug, Controls, Settings and
  About, in a sidebar. Advanced is gone: the local server and Diagnostics and Help are cards on
  Settings, with a box at the top that filters them, and Banners is on Events. Controls holds the
  Input, Bindings and Cursor cards.
- On a game folder that has Cloud23333's latest update, Play starts the game through his own launch
  script, so every launch rule of his update applies, with the add-on added to it. If his script has
  changed in a way the launcher does not recognise, the game starts with his script as it is and the
  status line says the add-on is off. With HUD and menus at the screen edges on, touch still works over
  the whole screen there, as it does on the earlier game.
- On a game folder that has Cloud23333's latest update, the Banners patch is gone, as his update brings
  its own event switches and replaces the server files it changed, and the Easter egg card shows only
  while its files are there.
- The package no longer replaces Cloud23333's launch scripts. The launcher runs its own launch script
  beside his, and Max Master Level has a script of its own. On a game folder that has his latest
  update, his server and account scripts stay his own too; on the earlier game the English copies of
  them are installed as before.
- fluphus's AMD layer is his latest build, Last Encore (2026-10-06): it holds the game at 60 FPS and
  caches translated shaders in memory. An install running a build an earlier FGOAC scooby shipped is
  brought up to it when the launcher starts; a build put there by hand is left as it is.
- Settings > Display has a Graphics layer list in place of the compatibility layer switch. Cloud23333's
  layer from his latest update is the default: on a game folder that has his update it is the one
  that runs, with fluphus's AMD layer as the other choice (no graphics card can be picked while it is
  on); on the game without his update the list offers Off, his layer, the layer FGOAC scooby shipped
  with 1.1.0 and fluphus's, and the first run of a fresh install on a PC with no NVIDIA card puts in
  his. Installs keep the layer they have, and a layer file FGOAC scooby did not ship is left as it is.
- When an update of Cloud23333's changes a game file one of the English files was made from, the
  launcher turns that English file off at its next start, so that screen shows the game's own text
  and the game starts as before; the first-run summary says how many, the launcher log lists them.
- The volume settings keep the 4-channel audio output Cloud23333's launcher saves in the same file, and
  on a game folder that has his latest update, Settings > Audio picks 2 or 4 output channels. Audio
  has no Apply button: moving a volume slider already saves it and applies it live.
- The launcher looks for a newer version when it starts and after every game. The Status on Play
  offers it with Install instead of a bar; Later in the Status menu hides it until the next start.
  Check for updates stays on About.
- After an update the Status says "Updated to" the new version, and What's new opens the notes,
  instead of a window opening by itself.
- F1 and F2 no longer open the game's operator menu during play unless the Operator menu keys switch
  is on.
- Controls no longer offers the two operator test and service key rows to rebind; the keys keep
  their same defaults, only the page rows are gone.
- Accounts starts with the selected account's details. Print History is now Summon results, and the
  printed-card and pending-print counts are gone. Repair Past Results and the growth and grant-all
  buttons run at once; Reset, Delete and Clear Present Box still ask first.
- Plainer labels: Game text: Japanese or English; the frame rate list no longer claims 60 is the
  stable choice and says the game runs at 60 whatever is picked; edge smoothing and texture
  sharpness beside SMAA and anisotropic filtering; the ports as title, billing and card reader; Set
  up banner switching; Card number; the account's card by its last four digits instead of the access
  code.
- All text in the launcher is at least 14 px, and long lines wrap instead of being cut off.
- An update is not installed while the game or the local server is running: Install says "Stop the
  game and the local server, then install the update." The English patch also stops while the local
  server runs.
- Importing a loadout over one of the same name keeps the old one as .bak, as Save as does, and the
  English patch keeps the previous settings file as fgo-launcher.json.bak when it turns English on.
- server-control.log holds today's lines. Each earlier day moves to server-control-<date>.log, and
  those older than 14 days are deleted.
- Cards > Deck scrolls as a page on a short window, so the library keeps a full row of cards.

### Security

- Updates install only when they are signed with the project's release key. The launcher downloads
  the release zip with its signature and checks the signature over the version and the zip's SHA-256
  before anything is unpacked; a release that is not signed, or whose signature does not match, is
  not installed and the launcher says so. The zip stays locked from the download to the end of the
  unpacking, so no other program can swap it in between, and an update may no longer write outside
  the game folder or over your settings, accounts, decks or the server's state.
- Your card number never reaches the logs: server-control.log, the game log and the server log in
  the Logs drawer show <redacted> in its place.

### Fixed

- A new install no longer starts in controller mode, which hides the mouse pointer in quests, when no
  controller is connected: the launcher's first start in a game folder sets keyboard and mouse and
  says so. Installs the launcher has set up before keep their controls.
- Play with no DEVICE\aime.txt ended the launch script with an unexplained error. It now says that
  no account is selected and to select one on Accounts.
- A missing server stop script no longer keeps the launcher open when it closes.
- Saving Battle Debug no longer drops the other sections of App\battle-debug.ini: the page and Play
  rewrite only its [battle_debug] part.
- Maximized, the launcher fills the screen's work area instead of running under the taskbar.
- Accounts names its card counts for what they count: the latest cards acquired and the cards
  printed, and the cards owned on the line under them.
- Help that named a removed button (the AMD crash), said a skill's target is picked on the touch
  screen only, or read as a contradiction (the English switch on Cloud23333's latest update) says
  what holds now.
- Cut-off and crowded parts: the Draw Rates filter, the Local server address label, the Photo Mode
  panel key, and the Dialogue quest search and portrait folder boxes, which now say what they hold.
- Every drop-down, slider and text box on the pages has a name a screen reader can say.
- Hide NP screen stays on after Cloud23333's launcher was used: his launcher writes the battle effects
  file without it, and Play sets it again.
- Settings changed in Cloud23333's launcher, or by his updater, while FGOAC scooby was open are no
  longer written back over: when its window comes back, the pages read the files again.
- Battle Debug switches and the PvP map set in Cloud23333's launcher are kept; the next Play no longer
  puts back the ones saved here.
- Cloud23333's launch scripts that an earlier FGOAC scooby replaced with its own copies are put back
  from the backup that install took, and on a game folder that has his latest update his server and
  account scripts as well, also where the earlier version was applied again after his latest update;
  the English files it left there for the earlier game are moved into a backup. Where no backup of his
  file exists, the installer says to run his updater again.
- On a game folder that has Cloud23333's latest update, -Rollback turns the English text off and
  leaves every file of his as it is.
- On a game folder that has Cloud23333's latest update, the launcher and Apply-EN-Patch.ps1 no longer
  copy the English files made for the earlier game over it, and -Rollback no longer puts them back.
- The English switch now also sets the game language Cloud23333's latest update reads, to an English
  set of the launcher's own that his updater never touches, and his launch script no longer reads the
  old switch as Chinese. Off sets Japanese. Start the game from FGOAC scooby for this English:
  Cloud23333's launcher starts his own English.
- On a game folder that has Cloud23333's latest update, story dialogue no longer runs past the
  dialogue box or off the screen: long lines wrap inside the box, and the few that cannot fit two
  lines are worded shorter.
- On a game folder that has Cloud23333's latest update, the Grail War, Grand Order, Team Sortie and
  sortie method buttons are lettered cleanly in English.

## [1.2.0] - 2026-09-21

Works on Cloud23333's V1.01 and V1.02. Unzip this release over your game folder and run the
launcher, or take it through Check for updates. Your accounts, decks, settings and whichever
graphics layer you have stay as they are.

### Fixed

- Apply-EN-Patch.ps1 -Rollback after an update that changed no game files restored the previous
  release's backup. It now restores the marker alone.

### Added

- Banners (Advanced): choose which limited-time events appear on the Terminal page. Press Apply FGO
  Event Toggle Patch once, tick the events to keep and press Save, then stop and start the local
  server from the Play page. With nothing ticked every banner appears. Contributed by Silvarrn.

## [1.1.2] - 2026-09-16

Works on Cloud23333's V1.01 and V1.02. Unzip this release over your game folder and run the
launcher, or take it through Check for updates. Your accounts, decks, settings and whichever
graphics layer you have stay as they are.

### Fixed

- The older AMD graphics layer is back in the package. A fresh install without an NVIDIA card
  gets it by itself, since the newer layer crashes at the first battle on RX 500, RX 6000 and
  RX 7600 cards. An install that already has a layer keeps it; if you turn the layer off and on
  again, the fresh choice is the older one, and the newer one is a click away.
- "Go back to the older layer" now also shows when App\opengl32.dll is a copy the launcher did
  not put there, and the Display page says when a fgoglcompat.dll sits where the game cannot use it.
- A game folder whose fgohook.dll is still the 11.00 build is refused with a message that says
  to apply V1.02 again, instead of crashing at start.
- Play works from a folder whose name has square brackets.
- The help page explains ERROR 6401, the 0xC000001D stop on CPUs without F16C, and the AMD
  first-battle crash.

### Added

- Deck loadouts: save the deck under a name, keep as many as you like, load any of them, and
  Export or Import to share with other players (Cards and Deck).
- Draw-rate presets: the same for the Draw Rates table.
- Sort the card list by name or card number.
- Max Master Level on the Account page.
- A short "what's new" window the first time the launcher starts after an update.
- A newer build of either graphics layer can be dropped into the compat folder and installed from
  Settings > Display, so a layer update no longer waits for a launcher release. The Display page
  now says up front that both layers are community work and not fully optimized yet.

## [1.1.1] - 2026-09-15

Works on Cloud23333's V1.01 and V1.02. V1.02 is the one to be on: it fixes ERROR 4102, the blank
Servant records and the sync error after enhancing a Servant. Unzip this release over your game
folder and run the launcher, or take it through Check for updates. Nothing to do by hand; your
accounts, decks and settings stay as they are.

### Fixed

- Updating through the launcher left the previous installer and file list in the game folder, so
  the next start put the previous version's files back. The launcher now unpacks the release into
  the game folder the way you would by hand.
- After a platform update (V1.01 or V1.02) the English scripts and text were gone until the patch
  marker was deleted by hand. The launcher now notices and puts them back on its own.
- A game folder that never had V1.01 is refused with a message that says so. Before, the launcher
  reported missing files and then crashed.
- `App\FGO_Runtime.dll` fresh out of a zip is unblocked on every start, so PowerShell no longer
  stops with 0x80131515 at Play.
- The Cards and Deck page says when the card images folder is missing, instead of listing zero
  Servants.
- The first-time hints on the Play page no longer clip at narrow window widths, and the card
  library toolbar no longer overlaps its own buttons with the log panel open.
- Diagnostics and Help, the guide and the README cover ERROR 8404, "Cannot use Aime card" at the
  title, 0x80131515, the missing-files dialog and the crash right after the game window opens.
- The bond talk scenes V1.02 restores for swimsuit Musashi, swimsuit Jeanne and Koyanskaya of Light
  are in English.

### Changed

- The AMD and Intel layer is now fluphus's open-source shim (`compat\amd-shim`, MIT). An install
  that already runs the older layer keeps it; Settings > Display shows which layer is on and
  switches to the newer one in one click, and back. A fresh install on a PC without an NVIDIA card
  gets the new layer on its first run. An update never adds, removes or replaces a layer on its own.
- The layer switch is no longer greyed out on NVIDIA PCs or when the shim's own installer put an
  `App\opengl32.dll` there; the line under it says what to expect, and the choice is yours.
- The launch log no longer prints a cabinet mode line; the game ignores that setting.

## [1.1.0] - 2026-09-14

First public release, for Cloud23333's FGO Arcade local platform V1.01. Earlier version numbers were
internal builds.

### What is in it

- **The game in English.** 65,266 translated text rows - story, quests, tutorial, Servant and Craft
  Essence profiles, skills, items, missions and every menu - and 240 rebuilt sprite archives:
  title, card read, main menu, terminal, formation, battle HUD, results, shops, synthesis, present
  box, master missions, matching, rankings, grail, help pages, title editor, on-screen keyboards and
  the summon screens. The launch scripts, the environment check, the account and server tools are
  English too, so every line that reaches the screen is English wherever it comes from.
- **The launcher**, an English front end for the platform on five pages: Play, Account, Cards and
  Deck, Settings, Advanced. One self-contained executable; no .NET runtime to install.
- **A first run that sets everything up**: installs the English files, checks the game can write to
  its folders, adds the Windows Firewall rules for the local server, the game and the cabinet
  service, creates the account Master with a full Servant and Craft Essence roster, and fills in
  the launch settings that are missing (windowed 1280x720 on the primary monitor). Nothing at all on
  later starts.
- **The deck editor**: 1,384 cards with their English names and 1,264 Craft Essence effects, searchable.
  Drag a card into the deck to add one copy, drag a deck card to reorder it, drag it back out to
  remove it; double-click picks the art and the number of copies. The deck reaches the game on Play.
- **Account tools**: create and select accounts, grant a full roster, all Craft Essences, max bond,
  materials and costumes in one click each.
- **Error codes and help** rewritten as one sentence and the fix, including 4102, 4104 on drives E:
  and Y:, 4105 without administrator rights, and the 0xC0000005 crash caused by Windows Defender
  Controlled Folder Access.
- **AMD and Intel graphics** through a bundled OpenGL compatibility layer (`compat\fgoglcompat.dll`)
  that translates the game's NVIDIA-only extensions. Turned on by itself on a PC without an NVIDIA
  card; greyed out on NVIDIA, where the layer only produces a white window.
- **An updater**: the launcher asks GitHub Releases whether a newer version exists and offers to
  fetch and apply it.
- **The installer** behind the first run, `Apply-EN-Patch.ps1`: finds the install, refuses drives
  E: and Y: and a running game, backs up every file it replaces, checks every file it copies
  against a SHA-256 manifest, never touches accounts, decks or the database, and undoes itself
  with `-Rollback`.
- **The player guide** inside the package as `GUIDE_EN.md` and `GUIDE_EN.pdf`: install, first run,
  controls, a sortie step by step, the shops, every error code.

### Known limits

- No online play and no matchmaking; everything runs against the local server.
- The co-op event banners, the co-op result screens, the later event shops and the summon
  screen's title art are still Japanese. They are artwork, not text. A few lines in event stories
  are still Japanese as well.
