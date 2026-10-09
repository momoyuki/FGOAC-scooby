<#
Checks that FGOAC scooby and Cloud23333's 1.10 package live in one game folder without overwriting each other,
without starting the launcher, the game or any program of his. Every file is written under a throwaway folder in
%TEMP%, deleted when the check ends.

  pwsh -File tools\Test-Coexistence.ps1 [-CloudManifest <his update folder>\文件清单.json] [-Release120 <the 1.2.0 package folder>]

-CloudManifest is the file list of his 1.10 update: every path it ships ("files"), removes ("remove") or removes
when unmodified ("removeIfUnmodified") is his. No file the launcher writes may be one of them, and no file the package
carries except those it installs only on game 11.00, where his 1.10 list does not apply; the settings files both
launchers share are written key by key and are not on his list.

Exit codes: 0 every check passed, 1 a check failed, 2 a prerequisite is missing.
#>
[CmdletBinding()]
param(
    [string]$CloudManifest = "$env:FGOAC_CLOUD_UPDATE\文件清单.json",
    [string]$Release120 = (Join-Path (Split-Path -Parent $PSScriptRoot) 'release\FGOAC-scooby-v1.2.0')
)
$ErrorActionPreference = 'Stop'

$repository = Split-Path -Parent $PSScriptRoot
if ($PSVersionTable.PSVersion.Major -lt 7) { Write-Host 'Run this with pwsh 7.'; exit 2 }
if (!(Test-Path -LiteralPath $CloudManifest)) { Write-Host "Missing $CloudManifest."; exit 2 }
if (!(Test-Path -LiteralPath (Join-Path $Release120 'Apply-EN-Patch.ps1'))) { Write-Host "Missing the 1.2.0 package at $Release120."; exit 2 }

$script:failed = 0
function Test-Result([string]$Name, [bool]$Passed, [string]$Detail) {
    Write-Host ("{0} {1}: {2}" -f $(if ($Passed) { 'PASS' } else { 'FAIL' }), $Name, $Detail)
    if (!$Passed) { $script:failed++ }
}

# His paths, as install-relative paths with backslashes, compared without case.
$manifest = Get-Content -LiteralPath $CloudManifest -Raw -Encoding UTF8 | ConvertFrom-Json
$his = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($path in @($manifest.files.path) + @($manifest.remove) + @($manifest.removeIfUnmodified.path)) { [void]$his.Add(([string]$path).Replace('/', '\')) }
Test-Result 'his file list read' ($his.Count -gt 18000) "$($his.Count) paths"

# The files installed only on game 11.00: its English set, and English copies of his 1.02 server and account scripts,
# which his 1.10 update replaces with his own. His 1.10 file list does not apply on 11.00.
$only1100 = @('App\zh\', 'Server\Start-FGOLocalServer.ps1', 'Server\Stop-FGOLocalServerWhenIdle.ps1', 'Server\tools\fgo_account.py',
    'Server\tools\fgo_server_config.py', 'Server\artemis\titles\fgo\data\summon_candidates.json')
function Test-Only1100([string]$Relative) { return @($only1100 | Where-Object { if ($_.EndsWith('\')) { $Relative.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) } else { $Relative -ieq $_ } }).Count -gt 0 }

# K1: what the package carries. The overlay folder, the two English sets, the add-on and the files at the package root.
$overlay = Join-Path $repository 'overlay'
$carried = @(Get-ChildItem -LiteralPath $overlay -File -Recurse | ForEach-Object { [IO.Path]::GetRelativePath($overlay, $_.FullName) })
$carried += @('App\fgoacaddon.dll', 'FGOAC scooby.exe', 'Apply-EN-Patch.ps1', 'manifest.json', 'README.md', 'CHANGELOG.md', 'GUIDE_EN.md', 'GUIDE_EN.pdf', 'SHA256SUMS.txt',
    'compat\fgoglcompat.dll', 'compat\fgoglcompat-1.1.0.dll', 'compat\amd-shim\opengl32.dll', 'compat\amd-shim\amdcfg\amdOglpSettings.cfg', 'compat\amd-shim\LICENSE')
$clash = @($carried | Where-Object { $his.Contains($_) -and !(Test-Only1100 $_) })
Test-Result 'the package carries none of his files for his build' ($clash.Count -eq 0) "$($carried.Count) files checked; his: $($clash -join ', ')"
$english1100 = @($only1100 | Where-Object { !$_.EndsWith('\') -and !(Test-Path -LiteralPath (Join-Path $overlay $_)) })
Test-Result 'the package carries the English server scripts for 11.00' ($english1100.Count -eq 0) "missing from overlay\: $($english1100 -join ', ')"
$sets = @('App\zh\', 'App\Localization\en-fgoac\')
$inSets = @($his | Where-Object { $path = $_; @($sets | Where-Object { $path.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }).Count -gt 0 })
Test-Result 'the English sets are folders he never ships' ($inSets.Count -eq 0) "his paths under $($sets -join ' or '): $($inSets.Count)"

# K1: what the launcher writes. Its own files, the shared settings files it merges key by key, and the layer files.
$written = @('App\fgo-launcher.json', 'App\deck.json', 'App\segatools.ini', 'App\battle-debug.ini', 'App\graphics-effects.ini', 'App\audio-volume.ini',
    'App\opengl32.dll', 'App\opengl32real.dll', 'App\amdcfg\amdOglpSettings.cfg', 'App\fgoac-patch.json', 'App\fgoac-state.json', 'App\FGOAC_Launcher.generated.ps1')
$clash = @($written | Where-Object { $his.Contains($_) })
Test-Result 'the launcher writes none of his files' ($clash.Count -eq 0) "$($written.Count) paths checked; his: $($clash -join ', ')"

# K1: the settings files both launchers write are merged key by key: a key only his launcher writes stays.
$assembly = Join-Path $repository 'src\bin\Release\net6.0-windows\FGOLocalPlatform.dll'
if (!(Test-Path -LiteralPath $assembly)) { Write-Host "Missing $assembly. Run build.cmd first."; exit 2 }
# Every message box, dialog and window of the launcher goes to the sink, never to the screen.
. (Join-Path $PSScriptRoot 'DialogSink.ps1'); Enable-DialogSink $assembly
$launcher = [Reflection.Assembly]::LoadFrom($assembly)
$listed = $launcher.GetType('FGOLocalPlatform.GameBuild').GetField('Only1100Files', [Reflection.BindingFlags]'NonPublic, Public, Static')
$listed = if ($listed) { @($listed.GetValue($null)) } else { @() }
Test-Result 'the first run skips the same 11.00 set' ((($listed | Sort-Object) -join ',') -ceq (($only1100 | Sort-Object) -join ',')) "GameBuild.Only1100Files: $($listed -join ', ')"
$shared = Join-Path ([IO.Path]::GetTempPath()) ('fgoac-shared-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $shared | Out-Null
try {
    $audio = Join-Path $shared 'audio-volume.ini'
    [IO.File]::WriteAllText($audio, "[audio]`r`nbgm=80`r`nvoice=70`r`neffects=60`r`noutputChannels=4`r`n")
    $merge = $launcher.GetType('FGOLocalPlatform.AudioSettingsView').GetMethod('Merge', [Reflection.BindingFlags]'NonPublic, Public, Static')
    $text = if ($merge) { $merge.Invoke($null, @([IO.File]::ReadAllText($audio), 10, 20, 30)) } else { 'Merge is missing' }
    Test-Result 'audio-volume.ini keeps outputChannels' ($text -match '(?m)^outputChannels=4\r?$' -and $text -match '(?m)^bgm=10\r?$' -and $text -match '(?m)^voice=20\r?$' -and $text -match '(?m)^effects=30\r?$') ($text -replace "`r", '\r' -replace "`n", '\n')
    $channels = $launcher.GetType('FGOLocalPlatform.AudioSettingsView').GetMethod('WithChannels', [Reflection.BindingFlags]'NonPublic, Public, Static')
    $read = $launcher.GetType('FGOLocalPlatform.AudioSettingsView').GetMethod('Channels', [Reflection.BindingFlags]'NonPublic, Public, Static')
    if (!$channels -or !$read) {
        Test-Result 'audio-volume.ini outputChannels picked' $false 'AudioSettingsView.WithChannels or Channels is missing'
    } else {
        $two = $channels.Invoke($null, @([IO.File]::ReadAllText($audio), 2))
        $four = $channels.Invoke($null, @("[audio]`nbgm=80`n[other]`nkey=1`n", 4))
        $reads = foreach ($text in $two, $four, "[audio]`nbgm=1`n", '', "[audio]`noutputChannels=6`n") { $read.Invoke($null, @([string]$text)) }
        Test-Result 'audio-volume.ini outputChannels picked' ($two -match '(?m)^outputChannels=2\r?$' -and $two -match '(?m)^bgm=80\r?$' -and $four.IndexOf('outputChannels=4') -gt $four.IndexOf('[audio]') -and $four.IndexOf('outputChannels=4') -lt $four.IndexOf('[other]') -and $four -match '(?m)^bgm=80\r?$' -and $four -match '(?m)^key=1\r?$' -and ($reads -join ',') -ceq '2,4,2,2,2') "$($four -replace "`n", '\n'); read: $($reads -join ',')"
    }
    $fresh = if ($merge) { $merge.Invoke($null, @('', 100, 100, 100)) } else { 'Merge is missing' }
    Test-Result 'audio-volume.ini written new as before' ($fresh -ceq "[audio]`nbgm=100`nvoice=100`neffects=100`n") ($fresh -replace "`r", '\r' -replace "`n", '\n')

    $effects = Join-Path $shared 'graphics-effects.ini'
    $his = "[effects]`r`nhide_dark_mask=0`r`nopacity=100`r`nfog=1`r`nreflections=0`r`n[photo]`r`nbackdrop=2`r`n"
    [IO.File]::WriteAllText($effects, $his)
    [IO.File]::WriteAllText((Join-Path $shared 'segatools.ini'), "[effects]`r`n")
    $battle = $launcher.GetType('FGOLocalPlatform.BattleEffects')
    $battle.GetConstructors()[0].Invoke(@($true, $false, 80)).Write($shared)
    $after = [IO.File]::ReadAllText($effects)
    Test-Result 'graphics-effects.ini edited in place' ($after -match '(?m)^hide_dark_mask=1\r?$' -and $after -match '(?m)^opacity=80\r?$' -and $after -match '(?m)^fog=1\r?$' -and $after -match '(?m)^reflections=0\r?$' -and $after -match '(?m)^backdrop=2\r?$') ($after -replace "`r", '\r' -replace "`n", '\n')
} finally {
    Remove-Item -LiteralPath $shared -Recurse -Force -ErrorAction SilentlyContinue
}

# The scenarios, on fake installs. "His" files hold text that names their version; his 1.10 updater is played by
# Invoke-HisUpdate, which does what his Engine.cs does to the folder: App\zh, App\EasterEgg and App\BGM go, his files are
# written over whatever is there, his English skeleton goes into App\Localization\en, fgo-launcher.json gains only the
# keys it lacks (gameVersion and the reset switches forced), the exe becomes 11.51 and update-1.10.json is left.
$work = Join-Path ([IO.Path]::GetTempPath()) ('fgoac-coexist-' + [guid]::NewGuid().ToString('N'))
$pwsh = Join-Path $PSHOME 'pwsh.exe'
$hisNames = @('App\FGO_Launcher.ps1', 'App\FGO_EnvironmentCheck.ps1', 'App\FGO_StartupChecks.ps1', 'Server\Start-FGOLocalServer.ps1',
    'Server\Stop-FGOLocalServerWhenIdle.ps1', 'Server\tools\fgo_account.py', 'Server\tools\fgo_server_config.py',
    'Server\artemis\titles\fgo\data\summon_candidates.json')

function Write-Text([string]$Path, [string]$Text) {
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    [IO.File]::WriteAllText($Path, $Text)
}
function Write-FakeExe([string]$Path, [uint32]$TimeDateStamp, [uint32]$SizeOfImage) {
    $bytes = [byte[]]::new(0x400)
    $bytes[0] = 0x4D; $bytes[1] = 0x5A
    [BitConverter]::GetBytes([int32]0x130).CopyTo($bytes, 0x3C)
    [BitConverter]::GetBytes([uint32]0x00004550).CopyTo($bytes, 0x130)
    [BitConverter]::GetBytes($TimeDateStamp).CopyTo($bytes, 0x138)
    [BitConverter]::GetBytes($SizeOfImage).CopyTo($bytes, 0x148 + 56)
    New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force | Out-Null
    [IO.File]::WriteAllBytes($Path, $bytes)
}
function Get-Hash([string]$Path) { if (Test-Path -LiteralPath $Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant() } return 'missing' }
function Get-Settings([string]$Root) { return Get-Content -LiteralPath (Join-Path $Root 'App\fgo-launcher.json') -Raw | ConvertFrom-Json }

# A game folder as his package leaves it: 1.02 (game 11.00) or 1.10 (game 11.51).
function New-Install([string]$Root, [string]$Version) {
    foreach ($name in $hisNames) { Write-Text (Join-Path $Root $name) "his $Version $name" }
    Write-Text (Join-Path $Root 'App\FGO_Runtime.dll') 'runtime'
    Write-Text (Join-Path $Root 'App\fgo-launcher.json') '{ "serverHost": "auto" }'
    Write-Text (Join-Path $Root 'DEVICE\aime.txt') "00000000000000000001`r`n"
    if ($Version -eq '1.10') {
        Write-FakeExe (Join-Path $Root 'App\ago.exe') 0x65C5AF1E 0x2039000
        Write-Text (Join-Path $Root 'App\Localization\en\language.json') 'his 1.10 en skeleton'
        Write-Text (Join-Path $Root 'App\fgoglcompat.dll') 'his 1.10 AFT'
        Write-Text (Join-Path $Root 'update-1.10.json') '{"version":"1.10"}'
    } else {
        Write-FakeExe (Join-Path $Root 'App\ago.exe') 0x643F4ECD 0x2034000
    }
}

# His files, by path and hash: the eight script names, his English skeleton and his layer, where present.
function Get-HisFiles([string]$Root) {
    $result = [ordered]@{}
    foreach ($name in @($hisNames) + 'App\Localization\en\language.json' + 'App\fgoglcompat.dll') { $result[$name] = Get-Hash (Join-Path $Root $name) }
    return $result
}
function Get-Changed($Before, $After) { return @($Before.Keys | Where-Object { $Before[$_] -ne $After[$_] }) }

function Invoke-HisUpdate([string]$Root) {
    foreach ($folder in 'App\zh', 'App\EasterEgg', 'App\BGM') { Remove-Item -LiteralPath (Join-Path $Root $folder) -Recurse -Force -ErrorAction SilentlyContinue }
    foreach ($name in $hisNames) { Write-Text (Join-Path $Root $name) "his 1.10 $name" }
    Write-Text (Join-Path $Root 'App\Localization\en\language.json') 'his 1.10 en skeleton'
    Write-Text (Join-Path $Root 'App\fgoglcompat.dll') 'his 1.10 AFT'
    $settingsPath = Join-Path $Root 'App\fgo-launcher.json'
    $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    foreach ($pair in @(@('localServerLauncher', '..\Server\Start-FGOLocalServer.ps1'), @('autoStartLocalServer', $true), @('wasapiShared', $true))) {
        if ($null -eq $settings.($pair[0])) { $settings | Add-Member -NotePropertyName $pair[0] -NotePropertyValue $pair[1] -Force }
    }
    foreach ($pair in @(@('gameVersion', '11.51'), @('audioHook', $false), @('setupCompleted', $false), @('diagnostics', $false))) { $settings | Add-Member -NotePropertyName $pair[0] -NotePropertyValue $pair[1] -Force }
    [IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
    Write-FakeExe (Join-Path $Root 'App\ago.exe') 0x65C5AF1E 0x2039000
    Write-Text (Join-Path $Root 'update-1.10.json') '{"version":"1.10"}'
}

# The new package, laid out as package.ps1 lays it out: overlay\, both English sets, the add-on, the installer.
function New-NewPackage([string]$Stage) {
    $overlay = Join-Path $repository 'overlay'
    foreach ($file in (Get-ChildItem -LiteralPath $overlay -File -Recurse)) {
        $target = Join-Path $Stage ('payload\' + [IO.Path]::GetRelativePath($overlay, $file.FullName))
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target
    }
    Write-Text (Join-Path $Stage 'payload\App\zh\text.txt') 'english for 11.00'
    Write-Text (Join-Path $Stage 'payload\App\Localization\en-fgoac\language.json') 'english for 11.51'
    Write-Text (Join-Path $Stage 'payload\App\fgoacaddon.dll') 'add-on'
    Write-Text (Join-Path $Stage 'FGOAC scooby.exe') 'launcher new'
    Copy-Item -LiteralPath (Join-Path $repository 'patch\Apply-EN-Patch.ps1') -Destination $Stage
    & (Join-Path $repository 'patch\Build-Manifest.ps1') -PackageRoot $Stage -Version '9.9.9' *> $null
}

# 1.2.0 as released: its installer, its eight scripts and one file of its App\zh set.
function New-OldPackage([string]$Stage) {
    foreach ($name in $hisNames) {
        $target = Join-Path $Stage "payload\$name"
        New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $Release120 "payload\$name") -Destination $target
    }
    Write-Text (Join-Path $Stage 'payload\App\zh\text.txt') 'english from 1.2.0'
    Write-Text (Join-Path $Stage 'FGOAC scooby.exe') 'launcher 1.2.0'
    Copy-Item -LiteralPath (Join-Path $Release120 'Apply-EN-Patch.ps1') -Destination $Stage
    & (Join-Path $repository 'patch\Build-Manifest.ps1') -PackageRoot $Stage -Version '1.2.0' *> $null
}

function Invoke-Patch([string]$Package, [string]$Root, [switch]$Rollback) {
    $installer = Join-Path $Package 'Apply-EN-Patch.ps1'
    $output = if ($Rollback) { @(& $pwsh -NoProfile -File $installer -InstallRoot $Root -PackageRoot $Package -NonInteractive -Rollback *>&1 | ForEach-Object { "$_" }) }
        else { @(& $pwsh -NoProfile -File $installer -InstallRoot $Root -PackageRoot $Package -NonInteractive *>&1 | ForEach-Object { "$_" }) }
    return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Text = ($output -join ' | '); Last = $(if ($output.Count) { $output[-1] } else { '' }) }
}

# The English server scripts of the new package that a folder holds.
$serverNames = @($hisNames | Where-Object { $_.StartsWith('Server\') })
$ourServer = @{}
foreach ($name in $serverNames) { $ourServer[$name] = Get-Hash (Join-Path $repository "overlay\$name") }
function Get-EnglishServer([string]$Root) { return @($serverNames | Where-Object { (Get-Hash (Join-Path $Root $_)) -eq $ourServer[$_] }) }

# What the new package installs; the 11.00 set only on 11.00.
function Get-Ours([string]$Root) {
    return [pscustomobject]@{
        Script = Test-Path -LiteralPath (Join-Path $Root 'App\FGOAC_Launcher.ps1')
        Master = Test-Path -LiteralPath (Join-Path $Root 'Server\tools\fgoac_master_level.py')
        English1151 = Test-Path -LiteralPath (Join-Path $Root 'App\Localization\en-fgoac\language.json')
        English1100 = Test-Path -LiteralPath (Join-Path $Root 'App\zh\text.txt')
        Marker = Test-Path -LiteralPath (Join-Path $Root 'App\fgoac-patch.json')
    }
}

try {
    $newPackage = Join-Path $work 'package-new'
    $oldPackage = Join-Path $work 'package-1.2.0'
    New-NewPackage $newPackage
    New-OldPackage $oldPackage
    # The hashes of the eight scripts as 1.2.0 installs them, for checking that an old copy of ours is gone.
    $old = @{}
    foreach ($name in $hisNames) { $old[$name] = Get-Hash (Join-Path $oldPackage "payload\$name") }

    # S-A: game 11.00 and the new package.
    $root = Join-Path $work 'S-A'
    New-Install $root '1.02'
    $before = Get-HisFiles $root
    $run = Invoke-Patch $newPackage $root
    $ours = Get-Ours $root
    $settings = Get-Settings $root
    $changed = Get-Changed $before (Get-HisFiles $root)
    $english = Get-EnglishServer $root
    Test-Result 'S-A 11.00 + new: both English sets and our files, his untouched but the server scripts' ($run.ExitCode -eq 0 -and $ours.Script -and $ours.Master -and $ours.English1151 -and $ours.English1100 -and $ours.Marker -and ($changed -join ',') -ceq ($serverNames -join ',')) "exit $($run.ExitCode), ours: $($ours | ConvertTo-Json -Compress), his changed: $($changed -join ', ')"
    Test-Result 'S-A the English server scripts installed' ($english.Count -eq $serverNames.Count) "ours: $($english -join ', ')"
    Test-Result 'S-A English on both ways' ($settings.gameLanguage -ceq 'en-fgoac' -and $settings.chineseEnabled -eq $true) "gameLanguage $($settings.gameLanguage), chineseEnabled $($settings.chineseEnabled)"

    # S-B: game 11.51 and the new package.
    $root = Join-Path $work 'S-B'
    New-Install $root '1.10'
    $before = Get-HisFiles $root
    $run = Invoke-Patch $newPackage $root
    $ours = Get-Ours $root
    $settings = Get-Settings $root
    $changed = Get-Changed $before (Get-HisFiles $root)
    Test-Result 'S-B 11.51 + new: the 11.51 set and our files, no App\zh, his untouched' ($run.ExitCode -eq 0 -and $ours.Script -and $ours.Master -and $ours.English1151 -and !(Test-Path -LiteralPath (Join-Path $root 'App\zh')) -and $ours.Marker -and $changed.Count -eq 0) "exit $($run.ExitCode), ours: $($ours | ConvertTo-Json -Compress), his changed: $($changed -join ', ')"
    Test-Result 'S-B English through gameLanguage only' ($settings.gameLanguage -ceq 'en-fgoac' -and $null -eq $settings.chineseEnabled) "gameLanguage $($settings.gameLanguage), chineseEnabled $($settings.chineseEnabled)"

    # S-C: 1.2.0 on 11.00, then the new package: as S-A, and his scripts that 1.2.0 replaced are his again.
    $root = Join-Path $work 'S-C'
    New-Install $root '1.02'
    $before = Get-HisFiles $root
    $first = Invoke-Patch $oldPackage $root
    Start-Sleep -Seconds 1
    $run = Invoke-Patch $newPackage $root
    $ours = Get-Ours $root
    $changed = Get-Changed $before (Get-HisFiles $root)
    $english = Get-EnglishServer $root
    Test-Result 'S-C 1.2.0 then new on 11.00: as S-A, his launch scripts back, the English server scripts ours' ($first.ExitCode -eq 0 -and $run.ExitCode -eq 0 -and $ours.Script -and $ours.English1151 -and $ours.English1100 -and ($changed -join ',') -ceq ($serverNames -join ',') -and $english.Count -eq $serverNames.Count) "1.2.0 exit $($first.ExitCode), new exit $($run.ExitCode), ours: $($ours | ConvertTo-Json -Compress), not his 1.02 any more: $($changed -join ', '), English server scripts: $($english.Count)"

    # S-D: S-A, then his 1.10 update: English 11.51 on the next start of either launcher, his files his.
    $root = Join-Path $work 'S-D'
    New-Install $root '1.02'
    [void](Invoke-Patch $newPackage $root)
    $ownBefore = Get-Hash (Join-Path $root 'App\FGOAC_Launcher.ps1')
    Invoke-HisUpdate $root
    $hisAfterUpdate = Get-HisFiles $root
    $run = Invoke-Patch $newPackage $root
    $settings = Get-Settings $root
    $changed = Get-Changed $hisAfterUpdate (Get-HisFiles $root)
    Test-Result 'S-D his update keeps ours and the language' ($ownBefore -eq (Get-Hash (Join-Path $root 'App\FGOAC_Launcher.ps1')) -and (Test-Path -LiteralPath (Join-Path $root 'App\Localization\en-fgoac\language.json')) -and $settings.gameLanguage -ceq 'en-fgoac') "own launch script kept: $($ownBefore -eq (Get-Hash (Join-Path $root 'App\FGOAC_Launcher.ps1'))), gameLanguage $($settings.gameLanguage)"
    Test-Result 'S-D the new package after his update writes nothing of his' ($run.ExitCode -eq 0 -and $changed.Count -eq 0 -and !(Test-Path -LiteralPath (Join-Path $root 'App\zh'))) "exit $($run.ExitCode), his changed: $($changed -join ', '), App\zh: $(Test-Path -LiteralPath (Join-Path $root 'App\zh'))"

    # S-E: 1.2.0 on 11.00, his 1.10 update, 1.2.0 again (it had no build check), then the new package repairs.
    $root = Join-Path $work 'S-E'
    New-Install $root '1.02'
    [void](Invoke-Patch $oldPackage $root)
    Start-Sleep -Seconds 1
    Invoke-HisUpdate $root
    $his110 = Get-HisFiles $root
    Start-Sleep -Seconds 1
    $again = Invoke-Patch $oldPackage $root
    $overwritten = @($hisNames | Where-Object { (Get-Hash (Join-Path $root $_)) -eq $old[$_] })
    Start-Sleep -Seconds 1
    $run = Invoke-Patch $newPackage $root
    $changed = Get-Changed $his110 (Get-HisFiles $root)
    $stale = Test-Path -LiteralPath (Join-Path $root 'App\zh')
    $kept = @(Get-ChildItem -LiteralPath (Join-Path $root '_en-patch-backup') -Recurse -Filter 'text.txt' -ErrorAction SilentlyContinue | Where-Object { $_.FullName -like '*\App\zh\text.txt' }).Count
    Test-Result 'S-E the old 1.2.0 overwrote his 1.10 scripts' ($again.ExitCode -eq 0 -and $overwritten.Count -eq $hisNames.Count) "exit $($again.ExitCode), ours over his: $($overwritten.Count) of $($hisNames.Count)"
    Test-Result 'S-E the new package puts his 1.10 scripts back and sets the stale set aside' ($run.ExitCode -eq 0 -and $changed.Count -eq 0 -and !$stale -and $kept -ge 1 -and (Get-Ours $root).English1151) "exit $($run.ExitCode), not his 1.10: $($changed -join ', '), App\zh left: $stale, App\zh in a backup: $kept; $($run.Text)"

    # The English server scripts of this package, left on a folder that moved to his build without his updater replacing
    # them: recognised as ours, and named, since no backup of his taken after his update exists.
    $root = Join-Path $work 'S-E2'
    New-Install $root '1.02'
    [void](Invoke-Patch $newPackage $root)
    Write-FakeExe (Join-Path $root 'App\ago.exe') 0x65C5AF1E 0x2039000
    Start-Sleep -Seconds 1
    Write-Text (Join-Path $root 'update-1.10.json') '{"version":"1.10"}'
    $run = Invoke-Patch $newPackage $root
    $named = @($serverNames | Where-Object { $run.Text.Contains($_) })
    Test-Result 'S-E2 the 11.00 server scripts of this package are known as ours on his build' ($run.ExitCode -eq 0 -and $named.Count -eq $serverNames.Count -and $run.Text -match 'Run his updater again') "exit $($run.ExitCode), named: $($named -join ', ')"

    # S-F: his updater again over the new package: ours untouched, our settings kept.
    $root = Join-Path $work 'S-F'
    New-Install $root '1.10'
    [void](Invoke-Patch $newPackage $root)
    $ourFiles = @('App\FGOAC_Launcher.ps1', 'App\FGOAC_EnvironmentCheck.ps1', 'App\FGOAC_StartupChecks.ps1', 'Server\tools\fgoac_master_level.py', 'App\Localization\en-fgoac\language.json', 'App\fgoacaddon.dll', 'App\fgoac-patch.json')
    $ourBefore = @($ourFiles | ForEach-Object { Get-Hash (Join-Path $root $_) })
    Invoke-HisUpdate $root
    $ourAfter = @($ourFiles | ForEach-Object { Get-Hash (Join-Path $root $_) })
    $settings = Get-Settings $root
    Test-Result 'S-F his updater again: ours untouched, language kept' (($ourBefore -join ',') -eq ($ourAfter -join ',') -and $settings.gameLanguage -ceq 'en-fgoac') "ours unchanged: $(($ourBefore -join ',') -eq ($ourAfter -join ',')), gameLanguage $($settings.gameLanguage)"

    # S-H: -Rollback on 11.51: Japanese, our English set gone, his files untouched.
    $root = Join-Path $work 'S-H'
    New-Install $root '1.10'
    [void](Invoke-Patch $newPackage $root)
    $before = Get-HisFiles $root
    $run = Invoke-Patch $newPackage $root -Rollback
    $settings = Get-Settings $root
    $changed = Get-Changed $before (Get-HisFiles $root)
    Test-Result 'S-H rollback on 11.51' ($run.ExitCode -eq 0 -and $settings.gameLanguage -ceq 'ja' -and !(Test-Path -LiteralPath (Join-Path $root 'App\Localization\en-fgoac')) -and $changed.Count -eq 0) "exit $($run.ExitCode), gameLanguage $($settings.gameLanguage), en-fgoac left: $(Test-Path -LiteralPath (Join-Path $root 'App\Localization\en-fgoac')), his changed: $($changed -join ', '); $($run.Last)"
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

$sink = Get-DialogSink
Test-Result 'no window shown' ($sink.Shown -eq 0) $sink.Detail
if ($script:failed -gt 0) { Write-Host "$($script:failed) check(s) failed."; exit 1 }
Write-Host 'Every check passed.'
exit 0
