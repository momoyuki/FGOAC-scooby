<#
Checks tools\upgrade-check.ps1, the update backup and the rollback of the launcher's own files,
without starting the launcher or the game.

  pwsh -File tools\Test-UpgradeCheck.ps1

It builds a small played install under %TEMP% (the protected set, a local server script and the
files the installer looks for) and release zips of version 9.9.0 made with Build-Manifest.ps1 around
the real Apply-EN-Patch.ps1, and loads the launcher assembly from src\bin\Release (run build.cmd
first).

Cases: the update backup of the launcher assembly copies every file of the protected set but the
local database, with its size and SHA-256, marks the launcher's own files for the rollback, is taken
once, and is not taken in a folder that is not a game folder. Apply-EN-Patch.ps1 -Rollback puts the
launcher's own files back from the update backup, keeping the edited file as .bak, and leaves the
cards and the server's state as they are; the installer refuses to run while a program runs from
Server\ of the game folder, and turns the English text on at a first install but leaves it off at an
update that finds it off. The upgrade check passes a release that changes nothing of the set and
one whose installer changes only the settings file, and one whose rollback adds a .bak of an own file
that did not exist at the install; it fails a release whose installer changes the
player list, and one that carries a file of the set, which the updater refuses. The install it is
given is never written. With -Boot it asks whether the Master, the deck and the loadouts were the
same, and fails on no.

Exit codes: 0 every check passed, 1 a check failed, 2 a prerequisite is missing.
#>
[CmdletBinding()]
param([switch]$KeepFiles)
$ErrorActionPreference = 'Stop'

$repository = Split-Path -Parent $PSScriptRoot
$assembly = Join-Path $repository 'src\bin\Release\net6.0-windows\FGOLocalPlatform.dll'
$check = Join-Path $PSScriptRoot 'upgrade-check.ps1'
$patch = Join-Path $repository 'patch\Apply-EN-Patch.ps1'
$buildManifest = Join-Path $repository 'patch\Build-Manifest.ps1'
$pwsh = Join-Path $PSHOME 'pwsh.exe'
if ($PSVersionTable.PSVersion.Major -lt 7) { Write-Host 'Run this with pwsh 7: the launcher assembly targets .NET 6.'; exit 2 }
if (!(Test-Path -LiteralPath $assembly)) { Write-Host "Missing $assembly. Run build.cmd first."; exit 2 }
# Every message box, dialog and window of the launcher goes to the sink, never to the screen.
. (Join-Path $PSScriptRoot 'DialogSink.ps1'); Enable-DialogSink $assembly

Add-Type -AssemblyName System.IO.Compression.FileSystem

$script:failed = 0
function Test-Result([string]$Name, [bool]$Passed, [string]$Detail) {
    Write-Host ("{0} {1}: {2}" -f $(if ($Passed) { 'PASS' } else { 'FAIL' }), $Name, $Detail)
    if (!$Passed) { $script:failed++ }
}

$work = Join-Path $env:TEMP ('fgoac-upgrade-check-test-' + [Guid]::NewGuid().ToString('N'))
$version = '9.9.0'

function Write-Text([string]$Path, [string]$Text) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    [IO.File]::WriteAllText($Path, $Text)
}

# A played install: the protected set, a .bak beside the deck file, a local server script a release
# replaces, and App\ago.exe, App\FGO_Runtime.dll and the settings file the installer looks for.
function New-Install([string]$Root, [bool]$English = $true) {
    $files = [ordered]@{
        'App\ago.exe' = 'game'
        'App\FGO_Runtime.dll' = 'runtime'
        'App\fgo-launcher.json' = '{ "chineseEnabled": ' + $English.ToString().ToLowerInvariant() + ' }'
        'App\deck.json' = '{ "SelectedCards": [ "a.png" ] }'
        'App\deck.json.bak' = '{ "SelectedCards": [] }'
        'App\deck-loadouts\Mine.json' = '{ "name": "Mine" }'
        'App\deck-loadouts\Mine.json.bak' = '{ "name": "Mine, older" }'
        'App\1_MOD\runtime.ini' = '[bgm]'
        'App\1_MOD\packages.json' = '[]'
        'App\1_MOD\BGM\0123456789abcdef0123456789abcdef.ogg' = 'music'
        'App\1_MOD\Packages\0123456789abcdef0123456789abcdef\Master\a.png' = 'portrait'
        'App\1_MOD\Dialogue\lines.json' = '{}'
        'App\1_MOD\Dialogue\Catalog\portraits.json' = '{}'
        'App\1_MOD\Tools\ffmpeg.exe' = 'converter'
        'DEVICE\aime.txt' = "00000000000000000001`r`n"
        'DEVICE\community\play.example.org\aime.txt' = "00000000000000000002`r`n"
        'DEVICE\print\players\aime-1\print.png' = 'print'
        'DEVICE\runtime\bindings.ini' = '[profile]'
        'DEVICE\runtime\segatools.runtime.ini' = '[aime]'
        'Server\state\fgo-players.json' = '{ "aime:1": {} }'
        'Server\data\fgo-master\players.ibd' = 'database'
        'Server\Start-FGOLocalServer.ps1' = 'old server script'
    }
    foreach ($name in $files.Keys) { Write-Text (Join-Path $Root $name) $files[$name] }
    Write-Game1100 (Join-Path $Root 'App\ago.exe')
}

# A PE header with the 11.00 build's TimeDateStamp and SizeOfImage, the two fields the installer's build check reads.
function Write-Game1100([string]$Path) {
    $bytes = [byte[]]::new(0x400)
    $bytes[0] = 0x4D; $bytes[1] = 0x5A
    [BitConverter]::GetBytes([int32]0x130).CopyTo($bytes, 0x3C)
    [BitConverter]::GetBytes([uint32]0x00004550).CopyTo($bytes, 0x130)
    [BitConverter]::GetBytes([uint32]0x643F4ECD).CopyTo($bytes, 0x138)
    [BitConverter]::GetBytes([uint32]0x2034000).CopyTo($bytes, 0x148 + 56)
    [IO.File]::WriteAllBytes($Path, $bytes)
}

# A release zip laid out as package.ps1 lays one out, with Build-Manifest.ps1's manifest.json.
function New-Release([string]$Name, [string]$Installer, [Collections.IDictionary]$Extra) {
    $stage = Join-Path $work "releases\$Name"
    Write-Text (Join-Path $stage 'FGOAC scooby.exe') "launcher $version"
    if ($Installer) { Write-Text (Join-Path $stage 'Apply-EN-Patch.ps1') $Installer }
    else { Copy-Item -LiteralPath $patch -Destination (Join-Path $stage 'Apply-EN-Patch.ps1') }
    Write-Text (Join-Path $stage 'payload\App\zh\text.txt') 'english'
    Write-Text (Join-Path $stage 'payload\Server\Start-FGOLocalServer.ps1') 'new server script'
    & $buildManifest -PackageRoot $stage -Version $version *> $null
    if ($LASTEXITCODE -ne 0) { throw "Build-Manifest.ps1 exited with $LASTEXITCODE" }
    if ($Extra) { foreach ($entry in $Extra.Keys) { Write-Text (Join-Path $stage $entry) $Extra[$entry] } }
    $zip = Join-Path $work "releases\FGOAC-scooby-v$version-$Name\FGOAC-scooby-v$version.zip"
    New-Item -ItemType Directory -Path (Split-Path -Parent $zip) -Force | Out-Null
    [IO.Compression.ZipFile]::CreateFromDirectory($stage, $zip)
    return $zip
}

function Get-TreeHash([string]$Root) {
    return (Get-ChildItem -LiteralPath $Root -Recurse -File | Sort-Object FullName | ForEach-Object {
        [IO.Path]::GetRelativePath($Root, $_.FullName) + '|' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }) -join "`n"
}

function Get-Hash([string]$Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() }

function Invoke-Check([string]$Install, [string]$Release, [string]$Answer, [switch]$Boot) {
    $arguments = @('-NoProfile', '-File', $check, '-Install', $Install, '-Release', $Release, '-Scratch', (Join-Path $work 'scratch'))
    if ($Boot) { $arguments += '-Boot' }
    $output = @(if ($Boot) { $Answer | & $pwsh @arguments 2>&1 } else { & $pwsh @arguments 2>&1 }) | ForEach-Object { "$_" }
    return [pscustomobject]@{ Exit = $LASTEXITCODE; Lines = $output }
}

try {
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    $loaded = [Reflection.Assembly]::LoadFrom($assembly)
    $set = $loaded.GetType('FGOLocalPlatform.ProtectedSet', $true)

    # The update backup, as the launcher takes it at the first start of a version.
    $game = Join-Path $work 'backup-case'
    New-Install $game
    $folder = $set::TakeBackup($game, $version)
    $expected = 'App\1_MOD\BGM\0123456789abcdef0123456789abcdef.ogg', 'App\1_MOD\Dialogue\lines.json', 'App\1_MOD\Packages\0123456789abcdef0123456789abcdef\Master\a.png',
        'App\1_MOD\packages.json', 'App\1_MOD\runtime.ini', 'App\deck-loadouts\Mine.json', 'App\deck-loadouts\Mine.json.bak', 'App\deck.json', 'App\fgo-launcher.json',
        'DEVICE\aime.txt', 'DEVICE\community\play.example.org\aime.txt', 'DEVICE\print\players\aime-1\print.png',
        'DEVICE\runtime\bindings.ini', 'DEVICE\runtime\segatools.runtime.ini', 'Server\state\fgo-players.json'
    $manifestPath = Join-Path $game "backup\pre-$version\manifest.json"
    $manifest = if (Test-Path -LiteralPath $manifestPath) { Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json } else { $null }
    $listed = @($manifest.files | ForEach-Object { $_.path } | Sort-Object)
    Test-Result 'update backup, the files of the set, the mods but not his catalog and tools' ($folder -eq (Join-Path $game "backup\pre-$version") -and ($listed -join ',') -eq (($expected | Sort-Object) -join ',')) "returned $folder; listed: $($listed -join ', ')"
    $wrong = @($manifest.files | Where-Object {
        $original = Join-Path $game $_.path
        $copy = Join-Path $game "backup\pre-$version\files\$($_.path)"
        !(Test-Path -LiteralPath $copy) -or $_.sha256 -ne (Get-Hash $original) -or $_.sha256 -ne (Get-Hash $copy) -or $_.size -ne (Get-Item -LiteralPath $original).Length
    } | ForEach-Object { $_.path })
    Test-Result 'update backup, sizes and SHA-256 of the copies' ($manifest -and $wrong.Count -eq 0 -and $manifest.version -eq $version) "wrong: $($wrong -join ', ')"
    $restored = @($manifest.files | Where-Object { $_.restore } | ForEach-Object { $_.path } | Sort-Object)
    Test-Result 'update backup, the own files marked for the rollback' (($restored -join ',') -eq 'App\deck-loadouts\Mine.json,App\deck.json,App\fgo-launcher.json,DEVICE\runtime\bindings.ini,DEVICE\runtime\segatools.runtime.ini') "marked: $($restored -join ', ')"
    $partial = @(Get-ChildItem -LiteralPath (Join-Path $game 'backup') -Directory -Filter '*.partial')
    Test-Result 'update backup, nothing partial left' ($partial.Count -eq 0) "partial folders: $($partial.Count)"
    $before = Get-TreeHash (Join-Path $game 'backup')
    Write-Text (Join-Path $game 'App\deck.json') '{ "SelectedCards": [ "b.png" ] }'
    $again = $set::TakeBackup($game, $version)
    Test-Result 'update backup, taken once' ($null -eq $again -and (Get-TreeHash (Join-Path $game 'backup')) -eq $before) "second call returned '$again'; backup unchanged: $((Get-TreeHash (Join-Path $game 'backup')) -eq $before)"
    $notGame = Join-Path $work 'not-a-game-folder'
    Write-Text (Join-Path $notGame 'DEVICE\aime.txt') '00000000000000000001'
    $none = $set::TakeBackup($notGame, $version)
    Test-Result 'update backup, only in a game folder' ($null -eq $none -and !(Test-Path -LiteralPath (Join-Path $notGame 'backup'))) "returned '$none'"

    # Apply-EN-Patch.ps1 -Rollback with an update backup of the version it rolls back.
    $clean = New-Release 'clean'
    $game = Join-Path $work 'rollback-case'
    New-Install $game
    [IO.Compression.ZipFile]::ExtractToDirectory($clean, $game, $true)
    $output = @(& $pwsh -NoProfile -File (Join-Path $game 'Apply-EN-Patch.ps1') -InstallRoot $game -NonInteractive 2>&1 | ForEach-Object { "$_" })
    Test-Result 'installer, a clean release' ($LASTEXITCODE -eq 0 -and [IO.File]::ReadAllText((Join-Path $game 'Server\Start-FGOLocalServer.ps1')) -eq 'new server script') "exit $LASTEXITCODE; $($output[-1])"
    [void]$set::TakeBackup($game, $version)
    $deckBackup = Get-Hash (Join-Path $game "backup\pre-$version\files\App\deck.json")
    $loadoutBackup = Get-Hash (Join-Path $game "backup\pre-$version\files\App\deck-loadouts\Mine.json")
    Write-Text (Join-Path $game 'App\deck.json') '{ "SelectedCards": [ "edited.png" ] }'
    Write-Text (Join-Path $game 'App\deck-loadouts\Mine.json') '{ "name": "Mine, edited" }'
    Write-Text (Join-Path $game 'DEVICE\community\play.example.org\aime.txt') "00000000000000000003`r`n"
    Write-Text (Join-Path $game 'Server\state\fgo-players.json') '{ "aime:1": {}, "aime:2": {} }'
    $output = @(& $pwsh -NoProfile -File (Join-Path $game 'Apply-EN-Patch.ps1') -InstallRoot $game -Rollback -NonInteractive 2>&1 | ForEach-Object { "$_" })
    Test-Result 'rollback, exit' ($LASTEXITCODE -eq 0) "exit $LASTEXITCODE; $($output -join ' | ')"
    Test-Result 'rollback, the release''s files back' ([IO.File]::ReadAllText((Join-Path $game 'Server\Start-FGOLocalServer.ps1')) -eq 'old server script' -and !(Test-Path -LiteralPath (Join-Path $game 'App\zh\text.txt'))) 'Start-FGOLocalServer.ps1 restored, App\zh\text.txt removed'
    Test-Result 'rollback, the deck file from the update backup' ((Get-Hash (Join-Path $game 'App\deck.json')) -eq $deckBackup -and [IO.File]::ReadAllText((Join-Path $game 'App\deck.json.bak')) -eq '{ "SelectedCards": [ "edited.png" ] }') 'deck.json as backed up, the edited one kept as deck.json.bak'
    Test-Result 'rollback, the loadout from the update backup' ((Get-Hash (Join-Path $game 'App\deck-loadouts\Mine.json')) -eq $loadoutBackup -and [IO.File]::ReadAllText((Join-Path $game 'App\deck-loadouts\Mine.json.bak')) -eq '{ "name": "Mine, edited" }') 'Mine.json as backed up, the edited one kept as Mine.json.bak'
    $setAside = @(Get-ChildItem -LiteralPath (Join-Path $game 'backup') -Directory -Filter "pre-$version-rolled-back-*")
    Test-Result 'rollback, the update backup set aside for the next install' (!(Test-Path -LiteralPath (Join-Path $game "backup\pre-$version")) -and $setAside.Count -eq 1) "backup folders: $((Get-ChildItem -LiteralPath (Join-Path $game 'backup') -Directory).Name -join ', ')"
    Test-Result 'rollback, cards and server state kept' ([IO.File]::ReadAllText((Join-Path $game 'DEVICE\community\play.example.org\aime.txt')) -eq "00000000000000000003`r`n" -and [IO.File]::ReadAllText((Join-Path $game 'Server\state\fgo-players.json')) -eq '{ "aime:1": {}, "aime:2": {} }') 'the community card and the player list are the edited ones'

    # The installer while a program runs from Server\ of the game folder.
    $game = Join-Path $work 'server-running-case'
    New-Install $game
    [IO.Compression.ZipFile]::ExtractToDirectory($clean, $game, $true)
    $program = Join-Path $game 'Server\mariadb\bin\mariadbd.exe'
    New-Item -ItemType Directory -Path (Split-Path -Parent $program) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $env:SystemRoot 'System32\PING.EXE') -Destination $program
    $process = Start-Process -FilePath $program -ArgumentList '-n', '60', '127.0.0.1' -WindowStyle Hidden -PassThru
    try { $output = @(& $pwsh -NoProfile -File (Join-Path $game 'Apply-EN-Patch.ps1') -InstallRoot $game -NonInteractive 2>&1 | ForEach-Object { "$_" }); $exit = $LASTEXITCODE }
    finally { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue; $process.WaitForExit() }
    Test-Result 'installer, the local server running' ($exit -eq 4 -and [IO.File]::ReadAllText((Join-Path $game 'Server\Start-FGOLocalServer.ps1')) -eq 'old server script') "exit $exit; $($output[-1])"

    # The English text switch: on at a first install, left as the player set it at an update.
    $game = Join-Path $work 'first-install-case'
    New-Install $game -English $false
    [IO.Compression.ZipFile]::ExtractToDirectory($clean, $game, $true)
    $output = @(& $pwsh -NoProfile -File (Join-Path $game 'Apply-EN-Patch.ps1') -InstallRoot $game -NonInteractive 2>&1 | ForEach-Object { "$_" })
    $exit = $LASTEXITCODE
    $launcherSettings = Get-Content -LiteralPath (Join-Path $game 'App\fgo-launcher.json') -Raw | ConvertFrom-Json
    Test-Result 'installer, English on at a first install' ($exit -eq 0 -and $launcherSettings.chineseEnabled -eq $true) "exit $exit; chineseEnabled $($launcherSettings.chineseEnabled); $($output[-1])"
    $game = Join-Path $work 'update-case'
    New-Install $game -English $false
    Write-Text (Join-Path $game 'App\zh\en-patch.json') '{ "version": "9.8.0", "manifestHash": "older" }'
    [IO.Compression.ZipFile]::ExtractToDirectory($clean, $game, $true)
    $output = @(& $pwsh -NoProfile -File (Join-Path $game 'Apply-EN-Patch.ps1') -InstallRoot $game -NonInteractive 2>&1 | ForEach-Object { "$_" })
    $exit = $LASTEXITCODE
    $launcherSettings = Get-Content -LiteralPath (Join-Path $game 'App\fgo-launcher.json') -Raw | ConvertFrom-Json
    $marker = Get-Content -LiteralPath (Join-Path $game 'App\fgoac-patch.json') -Raw | ConvertFrom-Json
    Test-Result 'installer, English left off at an update' ($exit -eq 0 -and $launcherSettings.chineseEnabled -eq $false -and $marker.version -eq $version) "exit $exit; chineseEnabled $($launcherSettings.chineseEnabled); marker version $($marker.version); $($output[-1])"

    # The upgrade check.
    $install = Join-Path $work 'played'
    New-Install $install
    $original = Get-TreeHash $install
    $result = Invoke-Check $install $clean
    $failures = @($result.Lines -like 'FAIL *')
    # The installer gives a settings file without gameLanguage the language its chineseEnabled meant, once.
    $settings = @($result.Lines -like "PASS App\fgo-launcher.json: install changed it, the launcher's own file*")
    Test-Result 'check, a clean release passes' ($result.Exit -eq 0 -and $failures.Count -eq 0 -and $settings.Count -eq 1) "exit $($result.Exit); FAIL lines: $($failures.Count); $($result.Lines[-1])"
    Test-Result 'check, one line per file of the set' (@($result.Lines -match '^(PASS|FAIL) (App|DEVICE|Server)\\').Count -eq 16) "file lines: $(@($result.Lines -match '^(PASS|FAIL) (App|DEVICE|Server)\\').Count)"
    Test-Result 'check, the install given is not written' ((Get-TreeHash $install) -eq $original) 'every file of the played install as it was'
    $scratch = @(Get-ChildItem -LiteralPath (Join-Path $work 'scratch') -Force)
    Test-Result 'check, the copy removed' ($scratch.Count -eq 0) "left in the scratch folder: $($scratch.Count)"

    $english = Join-Path $work 'played-english-off'
    New-Install $english -English $false
    $result = Invoke-Check $english $clean
    $settings = @($result.Lines -like 'PASS App\fgo-launcher.json: *install changed it*')
    Test-Result 'check, only the settings file changed passes' ($result.Exit -eq 0 -and $settings.Count -eq 1 -and @($result.Lines -like 'FAIL *').Count -eq 0) "exit $($result.Exit); $($settings -join ' | ')"

    $rogue = New-Release 'rogue' @'
param([string]$InstallRoot, [string]$PackageRoot, [switch]$Rollback, [switch]$NonInteractive, [int]$IgnoreProcessId)
if (!$Rollback) { [IO.File]::WriteAllText((Join-Path $InstallRoot 'Server\state\fgo-players.json'), 'changed by the release') }
exit 0
'@
    $result = Invoke-Check $install $rogue
    $state = @($result.Lines -like 'FAIL Server\state\fgo-players.json: *install changed it*')
    Test-Result 'check, the player list changed fails' ($result.Exit -eq 1 -and $state.Count -eq 1) "exit $($result.Exit); $($state -join ' | ')"

    $carrying = New-Release 'carrying' $null ([ordered]@{ 'DEVICE\aime.txt' = '99999999999999999999' })
    $result = Invoke-Check $install $carrying
    $refused = @($result.Lines -like '*not allowed to write*')
    Test-Result 'check, a release carrying a file of the set fails' ($result.Exit -eq 1 -and $refused.Count -ge 1) "exit $($result.Exit); $($result.Lines[-1])"
    Test-Result 'check, the install given is still not written' ((Get-TreeHash $install) -eq $original) 'every file of the played install as it was'

    $keeping = New-Release 'keeping' @'
param([string]$InstallRoot, [string]$PackageRoot, [switch]$Rollback, [switch]$NonInteractive, [int]$IgnoreProcessId)
if ($Rollback) { [IO.File]::WriteAllText((Join-Path $InstallRoot 'DEVICE\runtime\segatools.runtime.ini.bak'), '[aime] replaced') }
exit 0
'@
    $result = Invoke-Check $install $keeping
    $added = @($result.Lines -like 'PASS DEVICE\runtime\segatools.runtime.ini.bak: rollback added it*')
    Test-Result 'check, an own file added after the install passes' ($result.Exit -eq 0 -and $added.Count -eq 1) "exit $($result.Exit); $(@($result.Lines -like '* DEVICE\runtime\segatools.runtime.ini.bak:*') -join ' | ')"

    $result = Invoke-Check $install $clean 'y' -Boot
    Test-Result 'check -Boot, the same Master, deck and loadouts' ($result.Exit -eq 0 -and @($result.Lines -like 'PASS boot:*').Count -eq 1) "exit $($result.Exit); $($result.Lines[-1])"
    $result = Invoke-Check $install $clean 'n' -Boot
    Test-Result 'check -Boot, not the same' ($result.Exit -eq 1 -and @($result.Lines -like 'FAIL boot:*').Count -eq 1) "exit $($result.Exit); $($result.Lines[-1])"
}
finally {
    if ($KeepFiles) { Write-Host "Kept $work" } elseif (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}

$sink = Get-DialogSink
Test-Result 'no window shown' ($sink.Shown -eq 0) $sink.Detail
if ($script:failed -gt 0) { Write-Host "$script:failed check(s) failed."; exit 1 }
Write-Host 'Every check passed.'
exit 0
