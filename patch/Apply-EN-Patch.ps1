<#
Applies the FGOAC scooby English patch to an FGO Arcade local-platform install.

The script is shipped at the root of the release package, next to "FGOAC scooby.exe",
manifest.json and the payload folder. Unzipping the package into the game folder already puts
every file beside the install, so a normal run copies the payload into place, checks every file
against manifest.json and writes the marker App\fgoac-patch.json. Re-running the same version
does nothing, unless a file of the payload has since been replaced or removed; then the files that
differ are copied back. Two parts of the payload are copied only onto game 11.00, read from the build
of App\ago.exe: its English set App\zh, and English copies of Cloud23333's 1.02 server and account
scripts. On any other build, such as the one his 1.10 update installs, those are left out and no file
of his package is written.

Usage
  .\Apply-EN-Patch.ps1
  .\Apply-EN-Patch.ps1 -InstallRoot D:\FGOA
  .\Apply-EN-Patch.ps1 -InstallRoot D:\FGOA -NonInteractive
  .\Apply-EN-Patch.ps1 -InstallRoot D:\FGOA -Rollback

The script asks Windows for administrator rights only when the game folder cannot be written to
as it stands, so a normal install on a data drive applies the patch without a prompt.

What it never touches: accounts, decks, Server\state, the database, and App\fgo-launcher.json
apart from the English switches. A first install sets gameLanguage to "en-fgoac", which makes
Cloud23333's launch scripts from his 1.10 update on load App\Localization\en-fgoac, and on game 11.00
chineseEnabled to true, which makes the game load App\zh. An update leaves both as they are, and
gives a settings file without gameLanguage the language its chineseEnabled meant.

An earlier FGOAC scooby replaced some of Cloud23333's scripts with copies of its own. Every run puts back each one that
still holds such a copy, from the backup that install took of his file; on a build other than 11.00 it also moves the
11.00 English set App\zh, which his update removes, into a backup.

On a build other than 11.00, -Rollback turns the English text off instead: gameLanguage "ja", the English set
App\Localization\en-fgoac moved into a backup, none of his files changed.

-Rollback also puts back the launcher's own files - the deck file, the loadouts, the settings file
and the runtime ini - as the update backup holds them: backup\pre-<version>, which the launcher
takes before the first start of the version rolled back. Each file it replaces is kept as .bak, and
the backup is renamed pre-<version>-rolled-back-<time> so the next install takes a new one.

Exit codes
  0  the patch was applied, was already up to date, or the rollback finished
  1  an unexpected error; the message says what failed
  2  no FGO Arcade install was found, or the folder given is not one
  3  the install is on drive E: or Y:, where the game cannot run
  4  the game, the server or a launcher is still running from that folder
  5  the patch package is incomplete, or lists a file the patch is not allowed to write
  6  a file did not match its checksum after it was copied
  7  -Rollback found no backup to restore
  8  cancelled: no folder was chosen
  9  the install has not had Cloud23333's V1.01 update, which the English scripts need
#>
[CmdletBinding()]
param(
    [string]$InstallRoot = '',
    [string]$PackageRoot = '',
    [switch]$Rollback,
    [switch]$Force,
    [switch]$NonInteractive,
    [int]$IgnoreProcessId = 0
)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

$scriptHost = [IO.Path]::Combine($PSHOME, 'pwsh.exe')
if (!(Test-Path -LiteralPath $scriptHost -PathType Leaf)) { $scriptHost = [IO.Path]::Combine($PSHOME, 'powershell.exe') }
if ([string]::IsNullOrWhiteSpace($PackageRoot)) { $PackageRoot = $PSScriptRoot }

$ProtectedPrefixes = @('Server\state\', 'Server\data\', 'DEVICE\', 'AMFS\', 'GameData\', '_en-patch-backup\', '_update-backup\', 'backup\', 'App\deck-loadouts\', 'Server\artemis\config\summon-presets\', 'App\1_MOD\')
$ProtectedFiles = @('App\fgo-launcher.json', 'App\fgo-launcher.json.bak', 'App\deck.json', 'App\deck.json.bak')
# "FGOA scooby.exe" is the name this launcher shipped under before 1.1.0. It stays in the list so an
# upgrade still refuses to run while the old build is open.
$RunningNames = @('ago.exe', 'amdaemon.exe', 'inject.exe', 'FGOLocalPlatform.exe', 'FGOAC scooby.exe', 'FGOA scooby.exe')
$RetiredLauncherName = 'FGOA scooby.exe'

function Test-FgoInstallRoot {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    return ([IO.File]::Exists([IO.Path]::Combine($Path, 'App\ago.exe')) -and
        [IO.File]::Exists([IO.Path]::Combine($Path, 'App\fgo-launcher.json')))
}

function Test-FgoGame1100 {
    param([string]$Root)
    if ([IO.File]::Exists([IO.Path]::Combine($Root, 'update-1.10.json'))) { return $false }
    try {
        $stream = [IO.File]::OpenRead([IO.Path]::Combine($Root, 'App\ago.exe'))
        try {
            $header = [byte[]]::new(4096)
            $read = 0
            while ($read -lt $header.Length -and ($count = $stream.Read($header, $read, $header.Length - $read)) -gt 0) { $read += $count }
        } finally { $stream.Dispose() }
    } catch { return $false }
    if ($read -lt 0x40 -or $header[0] -ne 0x4D -or $header[1] -ne 0x5A) { return $false }
    $pe = [BitConverter]::ToInt32($header, 0x3C)
    if ($pe -lt 0x40 -or $pe + 24 + 60 -gt $read -or [BitConverter]::ToUInt32($header, $pe) -ne 0x00004550) { return $false }
    return ([BitConverter]::ToUInt32($header, $pe + 8) -eq 0x643F4ECD -and [BitConverter]::ToUInt32($header, $pe + 24 + 56) -eq 0x2034000)
}

# What is installed only on game 11.00: its English set, and English copies of Cloud23333's 1.02 server and account
# scripts, which his 1.10 update replaces with his own. A name ending in \ is a folder. Mirrors GameBuild.Only1100Files.
$Game1100Only = @('App\zh\', 'Server\Start-FGOLocalServer.ps1', 'Server\Stop-FGOLocalServerWhenIdle.ps1', 'Server\tools\fgo_account.py',
    'Server\tools\fgo_server_config.py', 'Server\artemis\titles\fgo\data\summon_candidates.json')

function Test-FgoGame1100Only {
    param([string]$Relative)
    foreach ($name in $Game1100Only) {
        if ($name.EndsWith('\')) { if ($Relative.StartsWith($name, [StringComparison]::OrdinalIgnoreCase)) { return $true } }
        elseif ($Relative.Equals($name, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    }
    return $false
}

# Cloud23333's scripts that FGOAC scooby releases replaced with their own copies, and the SHA-256 of every copy they
# installed. A file that still holds one of these is ours, not his. On game 11.00 the server and account scripts among
# them are the English copies this package installs there.
$RetiredCopies = @{
    'App\FGO_EnvironmentCheck.ps1' = @('8d0dd9c4a7938c39e7a98dce1c67722e6ca10867ae00caff4c6f0c2d2353bd95', 'e73df3f89e3d366c0332deb256ec04ee162cd073e8b3389a7565c7037d368dbd')
    'App\FGO_Launcher.ps1' = @('7be7444fb5bbd870776076e1af2cf1bb79cfd1787f40a269a395555eeb26d479', 'c0ffabe279c69c3b65a0632d1695dfa86cb2df4b024d666666681a78e40e7590', 'ec2859f408387b974afd7e289ff99d21502e3afb8033b02aec5da627abbc02f0')
    'App\FGO_StartupChecks.ps1' = @('6351cb573fe9ede2fee72cc5f8320ae39d90f9671e2fbf0d6d56575ac8f17c4a', '6eb208996f094c9dff1a277caf4173436629e9f8818244e827d46b08d48f701f')
    'Server\Start-FGOLocalServer.ps1' = @('818d5e41989b24ec16314e04db9eb8c27bd852026c90af6abd794fa37963f18c', 'd3f4e123561fb4c1768bea63cdce842aaad6675fae7f247976610b7c6fd6cc06')
    'Server\Stop-FGOLocalServerWhenIdle.ps1' = @('3b5de60a1f562c5c40cbf2d7a166a833e87e4faa984e21418d06099cb177f3f5', '4d650d52b30d08fb01fe0cdb6811183986d6d1b259f3d77cc840469ef21de691', '5b9f4081def47493f59fc709982e81594fe8bac511d95508cf5d414665460799')
    'Server\artemis\titles\fgo\data\summon_candidates.json' = @('10e5128293f4a8f7f97708503adbc0b9f6cffba896e841457585f4d340b637e1')
    'Server\tools\fgo_account.py' = @('8d9d410618207b01ded1f390adc80f983de7b35da88ed70c0733365e387818eb', 'bb6db19d51e0421c7806029ae860e25eee834201ee02c6955ff83db407ad7cfa', 'e6e0aeb28dd8963e98154a37a492a327d6e792586cae78fe178855566b185e17')
    'Server\tools\fgo_server_config.py' = @('052f4551fe506788259bdcfafa461f7225a5500fdbd8c5f03e75dbe85c49ce65')
}

# Puts back each of Cloud23333's scripts that still holds a copy an earlier FGOAC scooby installed, from the backup that
# earlier install took of his file: the newest copy that is not one of ours, taken after his last update on a build
# other than 11.00. The copy of ours goes into a new backup first. On 11.00 the files installed only there stay ours. On a
# build other than 11.00, the 11.00 English set App\zh, which his update removes, is moved into that backup too. Returns
# the sentence for a script left as it is.
function Repair-FgoCopies {
    param([string]$Root, [string]$BackupRoot, [bool]$Game1100)
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $aside = [IO.Path]::Combine($BackupRoot, $stamp + '-set-aside')
    $updated = [IO.Path]::Combine($Root, 'update-1.10.json')
    $since = if (!$Game1100 -and [IO.File]::Exists($updated)) { [IO.File]::GetLastWriteTime($updated) } else { [DateTime]::MinValue }
    # Backup folders are named to the second.
    $since = $since.AddTicks(-($since.Ticks % [TimeSpan]::TicksPerSecond))
    $backups = @(if ([IO.Directory]::Exists($BackupRoot)) { Get-ChildItem -LiteralPath $BackupRoot -Directory | Where-Object { $_.Name -match '^\d{8}-\d{6}$' } | Sort-Object -Property Name -Descending })
    $left = New-Object 'System.Collections.Generic.List[string]'
    foreach ($relative in $RetiredCopies.Keys) {
        if ($Game1100 -and (Test-FgoGame1100Only -Relative $relative)) { continue }
        $path = [IO.Path]::Combine($Root, $relative)
        if (!([IO.File]::Exists($path)) -or $RetiredCopies[$relative] -notcontains (Get-FgoFileHash -Path $path)) { continue }
        $his = $null
        foreach ($backup in $backups) {
            $taken = [DateTime]::ParseExact($backup.Name, 'yyyyMMdd-HHmmss', [Globalization.CultureInfo]::InvariantCulture)
            if ($taken -lt $since) { break }
            $saved = [IO.Path]::Combine($backup.FullName, $relative)
            if ([IO.File]::Exists($saved) -and $RetiredCopies[$relative] -notcontains (Get-FgoFileHash -Path $saved)) { $his = $saved; break }
        }
        if (!$his) { $left.Add($relative); continue }
        Copy-FgoFile -Source $path -Destination ([IO.Path]::Combine($aside, $relative))
        Copy-FgoFile -Source $his -Destination $path
        Write-Host "Put back Cloud23333's $relative from $(Split-Path -Parent ($his.Substring(0, $his.Length - $relative.Length)))."
    }
    $stale = [IO.Path]::Combine($Root, 'App\zh')
    if (!$Game1100 -and [IO.Directory]::Exists($stale)) {
        $target = [IO.Path]::Combine($aside, 'App\zh')
        [void][IO.Directory]::CreateDirectory((Split-Path -Parent $target))
        [IO.Directory]::Move($stale, $target)
        Write-Host "Moved App\zh, the English files made for the earlier game, to $target."
    }
    if ($left.Count -eq 0) { return '' }
    return "These files of Cloud23333's still hold an older FGOAC scooby's copy, and no backup of his was found: $($left -join ', '). Run his updater again to put them back."
}

function Find-FgoInstallRoot {
    foreach ($candidate in @($PackageRoot, (Split-Path -Parent $PackageRoot))) {
        if (Test-FgoInstallRoot -Path $candidate) { return (Resolve-Path -LiteralPath $candidate).Path }
    }
    $skip = @('Windows', 'Program Files', 'Program Files (x86)', 'ProgramData', '$Recycle.Bin', 'System Volume Information')
    foreach ($drive in (Get-CimInstance Win32_LogicalDisk -Filter 'DriveType=3' | Select-Object -ExpandProperty DeviceID)) {
        $root = $drive + '\'
        if (Test-FgoInstallRoot -Path $root) { return $root }
        $first = @(Get-ChildItem -LiteralPath $root -Directory -Force -ErrorAction SilentlyContinue |
            Where-Object { $skip -notcontains $_.Name })
        foreach ($level1 in $first) {
            if (Test-FgoInstallRoot -Path $level1.FullName) { return $level1.FullName }
        }
        foreach ($level1 in $first) {
            foreach ($level2 in @(Get-ChildItem -LiteralPath $level1.FullName -Directory -Force -ErrorAction SilentlyContinue)) {
                if (Test-FgoInstallRoot -Path $level2.FullName) { return $level2.FullName }
            }
        }
    }
    return ''
}

function Select-FgoInstallRoot {
    Add-Type -AssemblyName System.Windows.Forms
    [Windows.Forms.Application]::EnableVisualStyles()
    $picker = New-Object Windows.Forms.FolderBrowserDialog
    $picker.Description = 'Select your FGO Arcade folder - the one that holds the App folder.'
    $picker.ShowNewFolderButton = $false
    try {
        while ($picker.ShowDialog() -eq [Windows.Forms.DialogResult]::OK) {
            if (Test-FgoInstallRoot -Path $picker.SelectedPath) { return $picker.SelectedPath }
            [void][Windows.Forms.MessageBox]::Show(
                "That folder is not an FGO Arcade install. Choose the folder that holds App\ago.exe and App\fgo-launcher.json." + [Environment]::NewLine + [Environment]::NewLine + "You chose: " + $picker.SelectedPath,
                'FGOAC scooby - English patch', [Windows.Forms.MessageBoxButtons]::OK, [Windows.Forms.MessageBoxIcon]::Warning)
        }
        return ''
    } finally { $picker.Dispose() }
}

function Get-FgoDestinationPath {
    param([string]$Root, [string]$Relative)
    if ($Relative -match '^[A-Za-z]:' -or $Relative.StartsWith('\') -or $Relative.Split('\') -contains '..') {
        throw "The patch package lists an invalid path: $Relative"
    }
    foreach ($prefix in $ProtectedPrefixes) {
        if ($Relative.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "The patch package lists a file the patch is not allowed to write: $Relative"
        }
    }
    foreach ($protected in $ProtectedFiles) {
        if ($Relative.Equals($protected, [StringComparison]::OrdinalIgnoreCase)) {
            throw "The patch package lists a file the patch is not allowed to write: $Relative"
        }
    }
    $full = [IO.Path]::GetFullPath([IO.Path]::Combine($Root, $Relative))
    if (!$full.StartsWith($Root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "The patch package lists a path outside the game folder: $Relative"
    }
    return $full
}

function Get-FgoFileHash {
    param([string]$Path)
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Clear-FgoReadOnly {
    param([string]$Path)
    if ([IO.File]::Exists($Path)) {
        $attributes = [IO.File]::GetAttributes($Path)
        if ($attributes -band [IO.FileAttributes]::ReadOnly) {
            [IO.File]::SetAttributes($Path, $attributes -band (-bnot [IO.FileAttributes]::ReadOnly))
        }
    }
}

function Copy-FgoFile {
    param([string]$Source, [string]$Destination)
    $parent = Split-Path -Parent $Destination
    if (!([IO.Directory]::Exists($parent))) { [void][IO.Directory]::CreateDirectory($parent) }
    Clear-FgoReadOnly -Path $Destination
    Copy-Item -LiteralPath $Source -Destination $Destination -Force
    Clear-FgoReadOnly -Path $Destination
}

# Windows Defender keeps reading a file this large for some seconds after it is written, and a rename
# during that time fails. Waits until the file can be opened with no sharing, or gives up quietly.
function Wait-FgoFileReleased {
    param([string]$Path, [int]$Seconds)
    $deadline = (Get-Date).AddSeconds($Seconds)
    while ($true) {
        try {
            $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
            $stream.Dispose()
            return
        } catch {
            if ((Get-Date) -gt $deadline) { return }
            Start-Sleep -Milliseconds 500
        }
    }
}

function Write-FgoJson {
    param([string]$Path, $Value)
    $parent = Split-Path -Parent $Path
    if (!([IO.Directory]::Exists($parent))) { [void][IO.Directory]::CreateDirectory($parent) }
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 50), [Text.UTF8Encoding]::new($false))
}

# Writes one of the launcher's own files the way the launcher does: into a temporary file beside it,
# then swapped in with the file it replaces kept as .bak.
function Write-FgoOwnFile {
    param([string]$Path, [byte[]]$Bytes)
    $parent = Split-Path -Parent $Path
    if (!([IO.Directory]::Exists($parent))) { [void][IO.Directory]::CreateDirectory($parent) }
    $temporary = $Path + '.' + [guid]::NewGuid().ToString('N') + '.tmp'
    try {
        $stream = [IO.FileStream]::new($temporary, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None, 4096, [IO.FileOptions]::WriteThrough)
        try { $stream.Write($Bytes, 0, $Bytes.Length); $stream.Flush($true) } finally { $stream.Dispose() }
        Clear-FgoReadOnly -Path $Path
        if ([IO.File]::Exists($Path)) { [IO.File]::Replace($temporary, $Path, $Path + '.bak', $true) }
        else { [IO.File]::Move($temporary, $Path) }
    } finally {
        if ([IO.File]::Exists($temporary)) { [IO.File]::Delete($temporary) }
    }
}

function Test-FgoWritable {
    param([string]$Path)
    if (!([IO.Directory]::Exists($Path))) { return $false }
    $probe = [IO.Path]::Combine($Path, '.fgoa-scooby-' + [guid]::NewGuid().ToString('N'))
    try {
        [IO.File]::WriteAllText($probe, 'write check')
        [IO.File]::Delete($probe)
        return $true
    } catch { return $false }
}

function Stop-WithMessage {
    param([string]$Message, [int]$Code)
    Write-Host $Message
    exit $Code
}

# The folder picker needs an STA thread, the same way the author's updater gets one.
if ([string]::IsNullOrWhiteSpace($InstallRoot) -and !$NonInteractive -and
    [Threading.Thread]::CurrentThread.GetApartmentState() -ne [Threading.ApartmentState]::STA) {
    $arguments = @('-NoLogo', '-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', $PSCommandPath)
    if ($PackageRoot -ne $PSScriptRoot) { $arguments += @('-PackageRoot', $PackageRoot) }
    if ($Rollback) { $arguments += '-Rollback' }
    if ($Force) { $arguments += '-Force' }
    & $scriptHost @arguments
    exit $LASTEXITCODE
}

if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
    Write-Host 'Looking for your FGO Arcade folder...'
    $InstallRoot = Find-FgoInstallRoot
    if ([string]::IsNullOrWhiteSpace($InstallRoot) -and !$NonInteractive) { $InstallRoot = Select-FgoInstallRoot }
    if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
        Stop-WithMessage 'No FGO Arcade folder was chosen, so nothing was changed.' 8
    }
}
if (!(Test-FgoInstallRoot -Path $InstallRoot)) {
    Stop-WithMessage "That folder is not an FGO Arcade install: $InstallRoot. Choose the folder that holds App\ago.exe and App\fgo-launcher.json." 2
}
$InstallRoot = (Resolve-Path -LiteralPath $InstallRoot).Path.TrimEnd('\')
$PackageRoot = (Resolve-Path -LiteralPath $PackageRoot).Path.TrimEnd('\')
$rootPrefix = $InstallRoot + '\'
$driveLetter = $InstallRoot.Substring(0, 1).ToUpperInvariant()
if ($driveLetter -eq 'E' -or $driveLetter -eq 'Y') {
    Stop-WithMessage "The game cannot run from drive ${driveLetter}: - its own file hook sends every ${driveLetter}: path to the cabinet data mount, so the game stops with ERROR 4104. Move the whole $InstallRoot folder to another drive, such as D:, and run the patch again." 3
}

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$elevationNeeded = !(Test-FgoWritable -Path $InstallRoot) -or !(Test-FgoWritable -Path ([IO.Path]::Combine($InstallRoot, 'App')))
if ($elevationNeeded -and ![Security.Principal.WindowsPrincipal]::new($identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    $command = "& '" + $PSCommandPath.Replace("'", "''") + "' -InstallRoot '" + $InstallRoot.Replace("'", "''") + "' -PackageRoot '" + $PackageRoot.Replace("'", "''") + "' -NonInteractive"
    if ($Rollback) { $command += ' -Rollback' }
    if ($Force) { $command += ' -Force' }
    $transcript = [IO.Path]::Combine([IO.Path]::GetTempPath(), 'fgoa-scooby-patch-' + [guid]::NewGuid().ToString('N') + '.log')
    $quoted = $transcript.Replace("'", "''")
    $command = "try { $command *> '$quoted' } catch { " + '$_' + " | Out-String | Out-File -LiteralPath '$quoted' -Append; exit 1 }"
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $elevated = Start-Process -FilePath $scriptHost -Verb RunAs -WindowStyle Hidden -Wait -PassThru -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-EncodedCommand', $encoded)
    if (Test-Path -LiteralPath $transcript) {
        Get-Content -LiteralPath $transcript
        Remove-Item -LiteralPath $transcript -Force
    }
    exit $elevated.ExitCode
}

# Anything running from Server\ is the local server: its database, its services or the account tool.
$running = @(Get-CimInstance Win32_Process | Where-Object {
    $_.ProcessId -ne $IgnoreProcessId -and $_.ExecutablePath -and
    $_.ExecutablePath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase) -and
    ($RunningNames -contains $_.Name -or $_.ExecutablePath.StartsWith($rootPrefix + 'Server\', [StringComparison]::OrdinalIgnoreCase))
})
if ($running) {
    Stop-WithMessage ("Close the game, the launcher and the local server in $InstallRoot first, then run the patch again. Still running: " + (($running | ForEach-Object { $_.Name }) -join ', ')) 4
}

$backupRoot = [IO.Path]::Combine($InstallRoot, '_en-patch-backup')
$markerPath = [IO.Path]::Combine($InstallRoot, 'App\fgoac-patch.json')
# Where launchers up to 1.3.0 kept the marker; his 1.10 updater deletes App\zh with it.
$legacyMarkerPath = [IO.Path]::Combine($InstallRoot, 'App\zh\en-patch.json')
# No marker yet: this run is the first install, not an update.
$firstInstall = !([IO.File]::Exists($markerPath) -or [IO.File]::Exists($legacyMarkerPath))
$game1100 = Test-FgoGame1100 -Root $InstallRoot

if ($Rollback) {
    # The backups hold 11.00 files; putting them back over a newer build mixes the two. On that build the rollback
    # turns English off instead: Japanese in the settings, the launcher's English set moved into a backup.
    if (!$game1100) {
        $english = [IO.Path]::Combine($InstallRoot, 'App\Localization\en-fgoac')
        if ([IO.Directory]::Exists($english)) {
            $target = [IO.Path]::Combine($backupRoot, (Get-Date -Format 'yyyyMMdd-HHmmss') + '-set-aside', 'App\Localization\en-fgoac')
            [void][IO.Directory]::CreateDirectory((Split-Path -Parent $target))
            [IO.Directory]::Move($english, $target)
        }
        if ([IO.File]::Exists($markerPath)) { [IO.File]::Delete($markerPath) }
        $configurationPath = [IO.Path]::Combine($InstallRoot, 'App\fgo-launcher.json')
        $configuration = Get-Content -LiteralPath $configurationPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $configuration | Add-Member -NotePropertyName 'gameLanguage' -NotePropertyValue 'ja' -Force
        Write-FgoOwnFile -Path $configurationPath -Bytes ([Text.UTF8Encoding]::new($false).GetBytes(($configuration | ConvertTo-Json -Depth 50)))
        Write-Host "This game folder has Cloud23333's latest update, so the rollback turned the English text off and moved the English files of FGOAC scooby into $backupRoot. None of his files were changed."
        exit 0
    }
    if (!([IO.Directory]::Exists($backupRoot))) { Stop-WithMessage "There is no patch backup to restore in $backupRoot." 7 }
    $backup = Get-ChildItem -LiteralPath $backupRoot -Directory | Where-Object { $_.Name -match '^\d{8}-\d{6}$' } | Sort-Object -Property Name | Select-Object -Last 1
    if (!$backup) { Stop-WithMessage "There is no patch backup to restore in $backupRoot." 7 }
    $recordPath = [IO.Path]::Combine($backup.FullName, 'en-patch-restore.json')
    if (!([IO.File]::Exists($recordPath))) {
        Stop-WithMessage "The backup in $($backup.FullName) has no en-patch-restore.json, so it cannot be rolled back automatically." 7
    }
    $record = Get-Content -LiteralPath $recordPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Write-Host "Restoring the files saved in $($backup.FullName)..."
    $restored = 0
    foreach ($relative in @($record.replaced)) {
        $saved = [IO.Path]::Combine($backup.FullName, $relative)
        if ([IO.File]::Exists($saved)) {
            Copy-FgoFile -Source $saved -Destination ([IO.Path]::Combine($InstallRoot, $relative))
            $restored++
        }
    }
    $removed = 0
    foreach ($relative in @($record.added)) {
        $added = [IO.Path]::Combine($InstallRoot, $relative)
        if ([IO.File]::Exists($added)) {
            Clear-FgoReadOnly -Path $added
            [IO.File]::Delete($added)
            $removed++
        }
    }
    foreach ($marked in @($markerPath, $legacyMarkerPath)) {
        $savedMarker = [IO.Path]::Combine($backup.FullName, $marked.Substring($rootPrefix.Length))
        if ([IO.File]::Exists($savedMarker)) { Copy-FgoFile -Source $savedMarker -Destination $marked }
        elseif ([IO.File]::Exists($marked)) { [IO.File]::Delete($marked) }
    }
    Write-Host "Rollback finished: $restored files restored, $removed files removed."
    # The launcher's own files as the update backup holds them, taken before the first start of the
    # version rolled back. Cards, accounts, prints and the server's state are left as they are.
    $updateBackup = [IO.Path]::Combine($InstallRoot, 'backup', 'pre-' + [string]$record.version)
    $updateManifestPath = [IO.Path]::Combine($updateBackup, 'manifest.json')
    if (!([IO.File]::Exists($updateManifestPath))) {
        Write-Host 'The launcher settings were left as they are; turn English Text off in the launcher if you want the Chinese set back.'
        exit 0
    }
    $updateManifest = Get-Content -LiteralPath $updateManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $put = 0
    foreach ($entry in @($updateManifest.files | Where-Object { $_.restore -eq $true })) {
        $relative = [string]$entry.path
        $saved = [IO.Path]::Combine($updateBackup, 'files', $relative)
        $target = [IO.Path]::GetFullPath([IO.Path]::Combine($InstallRoot, $relative))
        if (!$target.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) { continue }
        if (!([IO.File]::Exists($saved)) -or (Get-FgoFileHash -Path $saved) -ne [string]$entry.sha256) {
            Write-Host "$relative in $updateBackup does not match its manifest, so it was not put back."
            continue
        }
        if ([IO.File]::Exists($target) -and (Get-FgoFileHash -Path $target) -eq [string]$entry.sha256) { continue }
        Write-FgoOwnFile -Path $target -Bytes ([IO.File]::ReadAllBytes($saved))
        $put++
    }
    # Set aside, so the next install of this version takes a new update backup.
    $setAside = $updateBackup + '-rolled-back-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
    [IO.Directory]::Move($updateBackup, $setAside)
    Write-Host "Put back $put of the launcher's own files from $setAside; each file it replaced is kept as .bak."
    exit 0
}

# App\FGO_Runtime.dll came with Cloud23333's V1.01 update, and the English launch and server
# scripts in this package load it. On the original 11.00 package they would leave the server
# unable to start, so the patch stops here instead.
if (!([IO.File]::Exists([IO.Path]::Combine($InstallRoot, 'App\FGO_Runtime.dll')))) {
    Stop-WithMessage "This game folder has not had Cloud23333's V1.01 update yet: App\FGO_Runtime.dll is missing, and the English patch needs it. Apply his V1.01 or V1.02 update to $InstallRoot first, then run FGOAC scooby again." 9
}

$left = Repair-FgoCopies -Root $InstallRoot -BackupRoot $backupRoot -Game1100 $game1100
if ($left) { Write-Host $left }

$manifestPath = [IO.Path]::Combine($PackageRoot, 'manifest.json')
if (!([IO.File]::Exists($manifestPath))) {
    Stop-WithMessage "The patch package is incomplete: manifest.json is missing from $PackageRoot. Unzip the whole package again." 5
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$version = [string]$manifest.version
$manifestHash = [string]$manifest.manifestHash
$entries = @($manifest.files.PSObject.Properties)
if ($entries.Count -eq 0) { Stop-WithMessage 'The patch package is incomplete: manifest.json lists no files.' 5 }
# Server\ files are the local server's; an install without the Server folder gets none of them.
if (!([IO.Directory]::Exists([IO.Path]::Combine($InstallRoot, 'Server')))) {
    $entries = @($entries | Where-Object { !$_.Name.StartsWith('Server\', [StringComparison]::OrdinalIgnoreCase) })
}
# The files for game 11.00 only. Cloud23333's 1.10 update moves the game to 11.51, deletes App\zh and ships his own
# server and account scripts; on that build or any other they are left out.
if (!$game1100) {
    $entries = @($entries | Where-Object { !(Test-FgoGame1100Only -Relative $_.Name) })
    Write-Host 'This game folder has Cloud23333''s latest update, so the English files made for the earlier game are left out.'
}

if (!$Force -and [IO.File]::Exists($markerPath)) {
    try {
        $marker = Get-Content -LiteralPath $markerPath -Raw -Encoding UTF8 | ConvertFrom-Json
        if ([string]$marker.version -eq $version -and [string]$marker.manifestHash -eq $manifestHash) {
            # The marker on its own is not proof: a file can be replaced or removed after it was
            # written. The files outside the rom folders are few and small, so checking them on
            # every run is cheap and enough.
            $replaced = ''
            foreach ($entry in $entries) {
                if ($entry.Name.StartsWith('App\zh\rom\', [StringComparison]::OrdinalIgnoreCase) -or $entry.Name.StartsWith('App\Localization\en-fgoac\rom\', [StringComparison]::OrdinalIgnoreCase) -or $entry.Name -eq 'FGOAC scooby.exe') { continue }
                $installed = [IO.Path]::Combine($InstallRoot, $entry.Name)
                if (!([IO.File]::Exists($installed)) -or (Get-FgoFileHash -Path $installed) -ne [string]$entry.Value) { $replaced = $entry.Name; break }
            }
            if ($replaced -eq '') {
                Write-Host "The English patch version $version is already installed in $InstallRoot. Nothing was changed."
                exit 0
            }
            Write-Host "The English patch version $version is recorded as installed, but $replaced has been replaced since, usually by a platform update, so the patch is applied again."
        }
    } catch {
        Write-Host 'The patch marker already there could not be read, so the patch will be applied again.'
    }
}

Write-Host "Applying the English patch version $version to $InstallRoot"
$payloadRoot = [IO.Path]::Combine($PackageRoot, 'payload')
$plan = New-Object 'System.Collections.Generic.List[object]'
foreach ($entry in $entries) {
    $relative = $entry.Name
    try { $destination = Get-FgoDestinationPath -Root $InstallRoot -Relative $relative }
    catch { Stop-WithMessage $_.Exception.Message 5 }
    $source = [IO.Path]::Combine($payloadRoot, $relative)
    if (!([IO.File]::Exists($source))) { $source = [IO.Path]::Combine($PackageRoot, $relative) }
    if (!([IO.File]::Exists($source))) {
        Stop-WithMessage "The patch package is incomplete: $relative is missing. Unzip the whole package again." 5
    }
    $plan.Add([pscustomobject]@{
        Relative    = $relative
        Source      = $source
        Destination = $destination
        Expected    = [string]$entry.Value
        InPlace     = $source.Equals($destination, [StringComparison]::OrdinalIgnoreCase)
        IsLauncher  = [IO.Path]::GetFileName($destination) -eq 'FGOAC scooby.exe'
    })
}

# When the package sits somewhere else, as it does when a launcher's updater unpacks it into the
# temporary folder, the game folder gets its own copy of the package first - installer, manifest,
# payload, the rest - the way an unzip by hand leaves it, so the next start finds the version it is
# on and a later re-apply or -Rollback has its files. The launcher is left to its manifest entry,
# which stages it as .new while it runs; manifest.json goes last, so a copy that stops half way
# still describes the previous package.
if (!$PackageRoot.Equals($InstallRoot, [StringComparison]::OrdinalIgnoreCase)) {
    Write-Host 'Copying the package into the game folder...'
    $packagePrefix = $PackageRoot + '\'
    foreach ($file in (Get-ChildItem -LiteralPath $PackageRoot -File -Recurse)) {
        if ($file.Name -eq 'FGOAC scooby.exe' -or $file.FullName.Equals($manifestPath, [StringComparison]::OrdinalIgnoreCase)) { continue }
        Copy-FgoFile -Source $file.FullName -Destination ([IO.Path]::Combine($InstallRoot, $file.FullName.Substring($packagePrefix.Length)))
    }
    Copy-FgoFile -Source $manifestPath -Destination ([IO.Path]::Combine($InstallRoot, 'manifest.json'))
}

Write-Host "Checking $($plan.Count) files against the package..."
$toCopy = New-Object 'System.Collections.Generic.List[object]'
$checked = 0
foreach ($item in $plan) {
    $checked++
    if (($checked % 400) -eq 0) { Write-Host "  checked $checked of $($plan.Count) files" }
    if ($item.InPlace) { continue }
    if ([IO.File]::Exists($item.Destination) -and (Get-FgoFileHash -Path $item.Destination) -eq $item.Expected) { continue }
    # An English override the launcher turned off because its game file changed stays off.
    $off = $item.Destination + '.fgoac-off'
    if (!([IO.File]::Exists($item.Destination)) -and [IO.File]::Exists($off) -and (Get-FgoFileHash -Path $off) -eq $item.Expected) { continue }
    $toCopy.Add($item)
}

if ($toCopy.Count -eq 0) {
    Write-Host 'Every file was already in place, so only the marker needed writing.'
    # A new marker with no file change still needs its own rollback point.
    $previous = $null
    if ([IO.File]::Exists($markerPath)) {
        try { $previous = Get-Content -LiteralPath $markerPath -Raw -Encoding UTF8 | ConvertFrom-Json } catch { $previous = $null }
    }
    if ($previous -and ([string]$previous.version -ne $version -or [string]$previous.manifestHash -ne $manifestHash)) {
        $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
        $backup = [IO.Path]::Combine($backupRoot, $stamp)
        [void][IO.Directory]::CreateDirectory($backup)
        Copy-FgoFile -Source $markerPath -Destination ([IO.Path]::Combine($backup, 'App\fgoac-patch.json'))
        Write-FgoJson -Path ([IO.Path]::Combine($backup, 'en-patch-restore.json')) -Value ([ordered]@{
            version  = $version
            savedUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
            replaced = @()
            added    = @()
        })
        Write-Host "Backup of the marker this replaces: $backup"
    }
} else {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $backup = [IO.Path]::Combine($backupRoot, $stamp)
    [void][IO.Directory]::CreateDirectory($backup)
    $replaced = New-Object 'System.Collections.Generic.List[string]'
    $added = New-Object 'System.Collections.Generic.List[string]'
    Write-Host "Backing up the files that will be replaced to $backup"
    foreach ($item in $toCopy) {
        if ([IO.File]::Exists($item.Destination)) {
            Copy-FgoFile -Source $item.Destination -Destination ([IO.Path]::Combine($backup, $item.Relative))
            $replaced.Add($item.Relative)
        } else {
            $added.Add($item.Relative)
        }
    }
    if ([IO.File]::Exists($markerPath)) {
        Copy-FgoFile -Source $markerPath -Destination ([IO.Path]::Combine($backup, 'App\fgoac-patch.json'))
    }
    Write-FgoJson -Path ([IO.Path]::Combine($backup, 'en-patch-restore.json')) -Value ([ordered]@{
        version  = $version
        savedUtc = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
        replaced = @($replaced)
        added    = @($added)
    })

    Write-Host "Copying $($toCopy.Count) files..."
    $copied = 0
    foreach ($item in $toCopy) {
        try {
            Copy-FgoFile -Source $item.Source -Destination $item.Destination
        } catch {
            if (!$item.IsLauncher) { throw }
            # A running program cannot overwrite its own file, so the new launcher is left beside the
            # old one. The launcher swaps it in and restarts itself when it closes.
            $item.Destination = $item.Destination + '.new'
            Copy-FgoFile -Source $item.Source -Destination $item.Destination
            Write-Host 'The launcher is open, so the new one was staged next to it and is swapped in when it closes.'
        }
        $copied++
        if (($copied % 200) -eq 0) { Write-Host "  copied $copied of $($toCopy.Count) files" }
    }

    Write-Host 'Checking the copied files...'
    $bad = New-Object 'System.Collections.Generic.List[string]'
    foreach ($item in $toCopy) {
        if ((Get-FgoFileHash -Path $item.Destination) -ne $item.Expected) { $bad.Add($item.Relative) }
    }
    if ($bad.Count -gt 0) {
        Write-Host 'These files did not match their checksum after copying, so the patch is not complete:'
        foreach ($relative in $bad) { Write-Host "  $relative" }
        Write-Host "Run the patch again; if it keeps failing, check your antivirus and the free space on drive ${driveLetter}:. The replaced files are in $backup, and -Rollback puts them back."
        exit 6
    }
    Write-Host "Backup of the replaced files: $backup"
    # The launcher swaps the staged build in the moment it closes; that has to find the file free.
    foreach ($item in $toCopy) {
        if ($item.IsLauncher -and $item.Destination.EndsWith('.new')) { Wait-FgoFileReleased -Path $item.Destination -Seconds 90 }
    }
}

# Before 1.1.0 the launcher was called "FGOA scooby.exe". Once the new one is in place the old file is
# only a second icon that starts an out-of-date build, so it goes.
$retiredLauncher = [IO.Path]::Combine($InstallRoot, $RetiredLauncherName)
if ([IO.File]::Exists($retiredLauncher) -and [IO.File]::Exists([IO.Path]::Combine($InstallRoot, 'FGOAC scooby.exe'))) {
    try {
        Clear-FgoReadOnly -Path $retiredLauncher
        Remove-Item -LiteralPath $retiredLauncher -Force
        Write-Host "Removed $RetiredLauncherName, which this version replaces."
    } catch {
        Write-Host "$RetiredLauncherName is still there and could not be removed: $($_.Exception.Message). Delete it yourself so you do not start the old build by mistake."
    }
}

# The English switches. gameLanguage "en-fgoac" makes Cloud23333's launch scripts from his 1.10 update on load
# App\Localization\en-fgoac; chineseEnabled makes game 11.00 read App\zh, and his 1.10 scripts read it as Chinese.
# A first install turns English on, chineseEnabled only on 11.00; an update leaves the player's choice and gives a
# settings file without gameLanguage the language its chineseEnabled meant.
$configurationPath = [IO.Path]::Combine($InstallRoot, 'App\fgo-launcher.json')
try {
    $configuration = Get-Content -LiteralPath $configurationPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $changed = $false
    if ($firstInstall -and $game1100 -and $configuration.chineseEnabled -ne $true) {
        $configuration | Add-Member -NotePropertyName 'chineseEnabled' -NotePropertyValue $true -Force
        $changed = $true
    }
    if ($firstInstall -and [string]$configuration.gameLanguage -cne 'en-fgoac') {
        $configuration | Add-Member -NotePropertyName 'gameLanguage' -NotePropertyValue 'en-fgoac' -Force
        $changed = $true
    } elseif ($null -eq $configuration.gameLanguage) {
        $configuration | Add-Member -NotePropertyName 'gameLanguage' -NotePropertyValue $(if ($configuration.chineseEnabled -eq $true) { 'en-fgoac' } else { 'ja' }) -Force
        $changed = $true
    }
    if ($changed) {
        Write-FgoOwnFile -Path $configurationPath -Bytes ([Text.UTF8Encoding]::new($false).GetBytes(($configuration | ConvertTo-Json -Depth 50)))
        if ($firstInstall) { Write-Host 'Turned the English text on in the launcher settings.' }
    }
} catch {
    Write-Host "The English text switch could not be set in App\fgo-launcher.json: $($_.Exception.Message)"
    Write-Host 'Open the launcher and turn English Text on yourself.'
}

# Windows marks every file that came out of a downloaded zip, and Windows PowerShell then refuses
# to load App\FGO_Runtime.dll (0x80131515). A platform update puts the mark back, so this runs on
# every pass, not only when files were copied.
$marked = @(Get-ChildItem -LiteralPath ([IO.Path]::Combine($InstallRoot, 'App')) -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Extension -in '.dll', '.exe', '.ps1' }) +
    @(Get-ChildItem -LiteralPath ([IO.Path]::Combine($InstallRoot, 'Server')) -Filter '*.ps1' -File -ErrorAction SilentlyContinue)
foreach ($file in $marked) { Unblock-File -LiteralPath $file.FullName -ErrorAction SilentlyContinue }

Write-FgoJson -Path $markerPath -Value ([ordered]@{
    version      = $version
    appliedUtc   = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    manifestHash = $manifestHash
})
Write-Host "The English patch version $version is installed in $InstallRoot."
exit 0
