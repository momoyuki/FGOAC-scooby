# Security

FGOAC scooby runs with administrator rights because the game requires them; without them the game
stops with ERROR 4105 shortly after boot.

What the launcher changes:

- files in the FGO Arcade install it sits in: `App\fgo-launcher.json`, the English files, the local
  server under `Server\` and the log folder;
- three inbound Windows Firewall rules, for the local server, the game and the cabinet service;
- `HKCU\Software\Microsoft\DirectX\UserGpuPreferences`, for the graphics card pick on the game
  without Cloud23333's latest update;
- the running game's memory, for the Settings > PC play switches that are on: only on the game build
  they were written for and only where the game's bytes are the expected ones. Game files on disk are
  not changed.

It loads `App\fgoacaddon.dll` into the game for the widescreen picture and the story pages that wait
for a press.

Network: the launcher contacts the GitHub Releases API to check for updates, and nothing else.

Updates: a release is installed only when its signature, ECDSA P-256 over
`FGOAC-scooby <version> <SHA-256 of the zip>`, verifies under a release key built into the launcher.
An unsigned or changed zip is refused before anything is unpacked.

The installer (`patch\Apply-EN-Patch.ps1`) backs up every file it replaces, checks every file it copies
against a SHA-256 manifest, refuses any entry that points at accounts, decks, the server state or the
database, and undoes itself with `-Rollback`.

## Reporting

Open an issue, or post on the project's Discord, with what you found and how to reproduce it. There is
no private disclosure channel and no bounty.
