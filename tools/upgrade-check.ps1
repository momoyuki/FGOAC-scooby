<#
Checks that a release keeps a player's protected set: the local database, the player list and
prints, the cards, the deck file and loadouts, the settings file and the runtime ini.

  pwsh -File tools\upgrade-check.ps1 -Install <game folder> -Release release\FGOAC-scooby-vX.Y.Z.zip

On a copy of the played install named by -Install, which is only read, it hashes every file of the
protected set, installs the release the way the launcher's updater does (the updater's own
extraction with its protected-path refusal, then the Apply-EN-Patch.ps1 the release brought), takes
the update backup the way the new launcher's first start does, and rolls back with
Apply-EN-Patch.ps1 -Rollback, hashing the set after each step. The updater's extraction, the
protected set and the update backup come from the launcher assembly in src\bin\Release, so run
build.cmd first on the source the release was published from. The release's signature is checked
separately, by Test-Updater.ps1 -Release.

Without -Boot the copy holds the protected set and the files the installer looks for (App\ago.exe,
App\FGO_Runtime.dll, App\fgo-launcher.json and the patch marker). With -Boot the whole game
folder is copied so the game can run from it: choose a -Scratch folder on a drive with room, not E:
or Y:. After the install the check stops and asks you to start FGOAC scooby.exe in the copy, choose
Local, press Play, reach My Room, look at the Master, the deck and the loadouts, then close the game
and the launcher and answer y if they were the ones you had. Close the game and the local server of
your own install first: the copy uses the same ports. The check never starts the game itself.

One line per file of the set, PASS or FAIL:
- the install changes nothing, except the launcher's own files (the deck file, the loadouts, the
  settings file, the runtime ini), which its installer may edit;
- during the boot the launcher's own files, the local database, the player list and the prints may
  change, since the launcher, the game and the local server ran;
- the rollback puts each own file the update backup marks back as the backup holds it and leaves
  every other file as it found it;
- the update backup lists every file of the set present after the install but the local database,
  as it was then, with its size and SHA-256.

Exit codes: 0 every file passed, 1 a file or the boot failed, 2 a prerequisite is missing or a step
could not run.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Install,
    [Parameter(Mandatory = $true)][string]$Release,
    [string]$Scratch = $env:TEMP,
    [switch]$Boot,
    [switch]$KeepFiles
)
$ErrorActionPreference = 'Stop'

$repository = Split-Path -Parent $PSScriptRoot
$assembly = Join-Path $repository 'src\bin\Release\net6.0-windows\FGOLocalPlatform.dll'
if ($PSVersionTable.PSVersion.Major -lt 7) { Write-Host 'Run this with pwsh 7: the launcher assembly targets .NET 6.'; exit 2 }
if (!(Test-Path -LiteralPath $assembly)) { Write-Host "Missing $assembly. Run build.cmd first."; exit 2 }
if (!(Test-Path -LiteralPath (Join-Path $Install 'App\fgo-launcher.json'))) { Write-Host "$Install is not a game folder: it has no App\fgo-launcher.json."; exit 2 }
if (!(Test-Path -LiteralPath $Release)) { Write-Host "Missing $Release."; exit 2 }
if ([IO.Path]::GetFileName($Release) -notmatch '^FGOAC-scooby-v(\d+\.\d+\.\d+)\.zip$') { Write-Host "$Release is not named FGOAC-scooby-vX.Y.Z.zip."; exit 2 }
$version = $Matches[1]
$Install = [IO.Path]::GetFullPath($Install).TrimEnd('\')
$work = Join-Path ([IO.Path]::GetFullPath($Scratch)) ('fgoac-upgrade-check-' + [Guid]::NewGuid().ToString('N'))
if ($work.StartsWith($Install + '\', [StringComparison]::OrdinalIgnoreCase)) { Write-Host "The scratch folder $Scratch is inside $Install. Choose one outside it."; exit 2 }
$copy = Join-Path $work 'game'

$loaded = [Reflection.Assembly]::LoadFrom($assembly)
$set = $loaded.GetType('FGOLocalPlatform.ProtectedSet', $true)
$extract = $loaded.GetType('FGOLocalPlatform.Updater', $true).GetMethod('ExtractIntoGameFolder', [Reflection.BindingFlags]'NonPublic, Static')
$played = 'Server\data\', 'Server\state\', 'DEVICE\print\players\'

function Get-SetHashes([string]$Root) {
    $hashes = [ordered]@{}
    foreach ($relative in $set::Files($Root, $true)) {
        $hashes[$relative] = (Get-FileHash -LiteralPath (Join-Path $Root $relative) -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    return $hashes
}

function Get-Change([Collections.IDictionary]$Before, [Collections.IDictionary]$After, [string]$Relative) {
    if (!$Before.Contains($Relative) -and !$After.Contains($Relative)) { return $null }
    if (!$Before.Contains($Relative)) { return 'added' }
    if (!$After.Contains($Relative)) { return 'removed' }
    if ($Before[$Relative] -ne $After[$Relative]) { return 'changed' }
    return $null
}

function Invoke-Installer([string[]]$Arguments) {
    $output = @(& (Join-Path $PSHOME 'pwsh.exe') -NoProfile -ExecutionPolicy Bypass -File (Join-Path $copy 'Apply-EN-Patch.ps1') -InstallRoot $copy @Arguments 2>&1 | ForEach-Object { "$_" })
    return [pscustomobject]@{ Exit = $LASTEXITCODE; Lines = $output }
}

function Stop-Check([string]$Message, [string[]]$Lines, [int]$Code) {
    foreach ($line in $Lines) { Write-Host "  $line" }
    Write-Host $Message
    exit $Code
}

try {
    Write-Host "Copying $Install to $copy..."
    if ($Boot) {
        $null = & robocopy $Install $copy /E /COPY:DAT /R:1 /W:1 /NFL /NDL /NJH /NJS /NP
        if ($LASTEXITCODE -ge 8) { Stop-Check "Copying $Install stopped with robocopy code $LASTEXITCODE." @() 2 }
    }
    else {
        $needed = @($set::Files($Install, $true)) + 'App\ago.exe', 'App\FGO_Runtime.dll', 'App\fgoac-patch.json', 'App\zh\en-patch.json'
        foreach ($relative in $needed) {
            $source = Join-Path $Install $relative
            if (!(Test-Path -LiteralPath $source -PathType Leaf)) { continue }
            $target = Join-Path $copy $relative
            New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
            Copy-Item -LiteralPath $source -Destination $target
        }
    }
    $before = Get-SetHashes $copy
    Write-Host "$($before.Count) files in the protected set."

    Write-Host "Installing $([IO.Path]::GetFileName($Release)) the way the updater does..."
    $stream = [IO.File]::OpenRead($Release)
    try { [void]$extract.Invoke($null, @($stream, [string]$copy)) }
    catch {
        $reason = $_.Exception
        while ($reason.InnerException) { $reason = $reason.InnerException }
        Stop-Check "FAIL install: the updater refuses the release: $($reason.Message)" @() 1
    }
    finally { $stream.Dispose() }
    $installer = Invoke-Installer @('-PackageRoot', $copy, '-NonInteractive')
    if ($installer.Exit -ne 0) { Stop-Check "FAIL install: Apply-EN-Patch.ps1 stopped with code $($installer.Exit)." $installer.Lines 1 }
    # What update-swap.cmd does when the launcher closes.
    $staged = Join-Path $copy 'FGOAC scooby.exe.new'
    if (Test-Path -LiteralPath $staged) { Move-Item -LiteralPath $staged -Destination (Join-Path $copy 'FGOAC scooby.exe') -Force }
    $installed = Get-SetHashes $copy

    # The first start of the new launcher.
    $backupFolder = Join-Path $copy "backup\pre-$version"
    if (Test-Path -LiteralPath $backupFolder) { Remove-Item -LiteralPath $backupFolder -Recurse -Force }
    [void]$set::TakeBackup($copy, $version)
    $manifest = Get-Content -LiteralPath (Join-Path $backupFolder 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    $backedUp = @{}
    # Whether each copy matches its manifest line, read now: the rollback sets the backup aside.
    $copyMatches = @{}
    foreach ($entry in @($manifest.files)) {
        $backedUp[[string]$entry.path] = $entry
        $kept = Join-Path $backupFolder "files\$($entry.path)"
        $copyMatches[[string]$entry.path] = (Test-Path -LiteralPath $kept) -and [long]$entry.size -eq (Get-Item -LiteralPath $kept).Length -and
            (Get-FileHash -LiteralPath $kept -Algorithm SHA256).Hash.ToLowerInvariant() -eq [string]$entry.sha256
    }

    $answer = $null
    $booted = $installed
    if ($Boot) {
        Write-Host ''
        Write-Host "Start $(Join-Path $copy 'FGOAC scooby.exe'), choose Local, press Play and play to My Room."
        Write-Host 'Look at the Master, the deck and the loadouts, then close the game and the launcher.'
        $answer = Read-Host 'Were the Master, the deck and the loadouts the ones you had? (y/n)'
        $booted = Get-SetHashes $copy
    }

    Write-Host 'Rolling back with Apply-EN-Patch.ps1 -Rollback...'
    $rollback = Invoke-Installer @('-Rollback', '-NonInteractive')
    if ($rollback.Exit -ne 0) { Stop-Check "FAIL rollback: Apply-EN-Patch.ps1 -Rollback stopped with code $($rollback.Exit)." $rollback.Lines 1 }
    $rolled = Get-SetHashes $copy

    $failed = 0
    $paths = @($before.Keys) + @($installed.Keys) + @($booted.Keys) + @($rolled.Keys) + @($backedUp.Keys) | Sort-Object -Unique
    foreach ($relative in $paths) {
        $own = [bool]$set::IsOwnFile($relative)
        $problems = [Collections.Generic.List[string]]::new()
        $notes = [Collections.Generic.List[string]]::new()
        $change = Get-Change $before $installed $relative
        if ($change) { if ($own) { $notes.Add("install $change it, the launcher's own file") } else { $problems.Add("install $change it") } }
        if ($Boot) {
            $change = Get-Change $installed $booted $relative
            if ($change) {
                if ($own -or @($played | Where-Object { $relative.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0) { $notes.Add("$change while the game ran") }
                else { $problems.Add("the boot $change it") }
            }
        }
        $saved = $backedUp[$relative]
        if ($saved -and $saved.restore -eq $true) {
            if ($rolled[$relative] -ne [string]$saved.sha256) { $problems.Add('rollback left it different from the update backup') }
            elseif ($booted[$relative] -ne $rolled[$relative]) { $notes.Add('rollback put back the update backup''s copy') }
        }
        else {
            $change = Get-Change $booted $rolled $relative
            if ($change) { if ($own) { $notes.Add("rollback $change it, the launcher's own file") } else { $problems.Add("rollback $change it") } }
        }
        if (!$relative.StartsWith('Server\data\', [StringComparison]::OrdinalIgnoreCase)) {
            if (!$saved -and $installed.Contains($relative)) { $problems.Add('the update backup does not list it') }
            elseif ($saved -and (!$copyMatches[$relative] -or [string]$saved.sha256 -ne $installed[$relative])) {
                $problems.Add('the update backup does not hold it as it was after the install')
            }
        }
        $detail = if ($problems.Count -gt 0) { ($problems + $notes) -join '; ' } elseif ($notes.Count -gt 0) { $notes -join '; ' } else { 'unchanged' }
        Write-Host ("{0} {1}: {2}" -f $(if ($problems.Count -gt 0) { 'FAIL' } else { 'PASS' }), $relative, $detail)
        if ($problems.Count -gt 0) { $failed++ }
    }
    if ($Boot) {
        $same = $answer -match '^\s*(y|yes)\s*$'
        Write-Host ("{0} boot: {1}" -f $(if ($same) { 'PASS' } else { 'FAIL' }), $(if ($same) { 'the Master, the deck and the loadouts were the ones you had' } else { "answered '$answer'" }))
        if (!$same) { $failed++ }
    }
    Write-Host "$($paths.Count) files checked, $failed failed."
    exit $(if ($failed -gt 0) { 1 } else { 0 })
}
finally {
    if ($KeepFiles) { Write-Host "Kept $work" } elseif (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}
