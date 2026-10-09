<#
Checks the start of the game on an install that has Cloud23333's 1.10 update, without starting the launcher, the
game or any program of his. Every file is written under a throwaway folder in %TEMP%, deleted when the check ends.

  pwsh -File tools\Test-Game1151.ps1 [-CloudApp <his 1.10 package>\App] [-GameRoot D:\games\FGOA]

The fake install holds his 1.10 scripts copied from -CloudApp, segatools.ini and config.json from -GameRoot (an
install, read only), an App\ago.exe that is only a PE header and placeholder game files; his environment check is
replaced by a stub, since it starts his front end. Through the launcher assembly (src\bin\Release, so run build.cmd
first), the way Play prepares a start:
  - game 11.00: Play runs FGOAC_Launcher.ps1.
  - his build with the add-on: Play runs a generated copy of his FGO_Launcher.ps1 that differs from it only by the
    lines inserted before the line that adds ago.exe: the two that add the add-on and, with the HUD at the screen
    edges, two that set [touch] inputWidth and inputHeight back to the resolution after his reset, so the runtime
    ini takes touch over the whole surface as FGOAC_Launcher.ps1 makes it; that copy passes his own -CheckOnly under
    Windows PowerShell 5.1 with the arguments Play passes. A script of his without the names those two lines use
    gets the add-on lines only and a sentence that touch is off.
  - the [pcport], [widescreen] and [cursor] keys go into App\segatools.ini with the values FGOAC_Launcher.ps1
    writes into the runtime ini for the same settings, and no [camera] block, the camera being his; every other
    line of the file stays.
  - without the add-on, or when his script has that line twice or not at all, Play runs his script as it is, and
    says so when the add-on is off.
  - a later build of his (another exe header, a changed launch script with the line still there once) still gets
    the generated copy, and it still passes -CheckOnly.
  - on his build, Play puts hide_dark_mask in graphics-effects.ini back in line with Hide NP screen after his launcher
    rewrote the file, keeping his other keys.
  - on his build, Play leaves the AMD layer the player chose (compat\amd-shim's files in App) in place, without a note.

Exit codes: 0 every check passed, 1 a check failed, 2 a prerequisite is missing.
#>
[CmdletBinding()]
param(
    [string]$CloudApp = "$env:FGOAC_CLOUD_UPDATE\payload\payload\App",
    [string]$GameRoot = 'D:\FGOA'
)
$ErrorActionPreference = 'Stop'

$repository = Split-Path -Parent $PSScriptRoot
$assembly = Join-Path $repository 'src\bin\Release\net6.0-windows\FGOLocalPlatform.dll'
$windowsPowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
if ($PSVersionTable.PSVersion.Major -lt 7) { Write-Host 'Run this with pwsh 7: the launcher assembly targets .NET 6.'; exit 2 }
if (!(Test-Path -LiteralPath $assembly)) { Write-Host "Missing $assembly. Run build.cmd first."; exit 2 }
# Every message box, dialog and window of the launcher goes to the sink, never to the screen.
. (Join-Path $PSScriptRoot 'DialogSink.ps1'); Enable-DialogSink $assembly
foreach ($path in @((Join-Path $CloudApp 'FGO_Launcher.ps1'), (Join-Path $GameRoot 'App\segatools.ini'), (Join-Path $GameRoot 'App\config.json'))) {
    if (!(Test-Path -LiteralPath $path)) { Write-Host "Missing $path."; exit 2 }
}

$script:failed = 0
function Test-Result([string]$Name, [bool]$Passed, [string]$Detail) {
    Write-Host ("{0} {1}: {2}" -f $(if ($Passed) { 'PASS' } else { 'FAIL' }), $Name, $Detail)
    if (!$Passed) { $script:failed++ }
}

function Write-FakeExe([string]$Path, [uint32]$TimeDateStamp, [uint32]$SizeOfImage) {
    $bytes = [byte[]]::new(0x400)
    $bytes[0] = 0x4D; $bytes[1] = 0x5A
    [BitConverter]::GetBytes([int32]0x130).CopyTo($bytes, 0x3C)
    [BitConverter]::GetBytes([uint32]0x00004550).CopyTo($bytes, 0x130)
    [BitConverter]::GetBytes($TimeDateStamp).CopyTo($bytes, 0x138)
    [BitConverter]::GetBytes($SizeOfImage).CopyTo($bytes, 0x148 + 56)
    [IO.File]::WriteAllBytes($Path, $bytes)
}

# The runtime ini is UTF-16; the base ini UTF-8. Every key of one section, in a sorted key=value list.
function Get-Section([string]$Text, [string]$Section) {
    $lines = @()
    $inside = $false
    foreach ($line in ($Text -split "`r?`n")) {
        if ($line -match '^\[(.+)\]\s*$') { $inside = $Matches[1] -eq $Section; continue }
        if ($inside -and $line -match '^\s*([^=;#]+?)\s*=(.*)$') { $lines += "$($Matches[1])=$($Matches[2])" }
    }
    return (@($lines | Sort-Object) -join ';')
}

$work = Join-Path ([IO.Path]::GetTempPath()) ('fgoac-game1151-' + [guid]::NewGuid().ToString('N'))
$install = Join-Path $work 'install'
$app = Join-Path $install 'App'
$exe = Join-Path $app 'ago.exe'
$settingsPath = Join-Path $app 'fgo-launcher.json'
$baseIni = Join-Path $app 'segatools.ini'
$hisScript = Join-Path $app 'FGO_Launcher.ps1'
$generated = Join-Path $app 'FGOAC_Launcher.generated.ps1'
$arguments = @('-DisplayMode', 'windowed', '-ResolutionWidth', '1280', '-ResolutionHeight', '720', '-InputMode', 'keyboard', '-TargetFps', '60')

try {
    foreach ($folder in @('App\am', 'App\Tools\Locale_Remulator', 'AMFS', 'DEVICE', 'Server')) { New-Item -ItemType Directory -Path (Join-Path $install $folder) -Force | Out-Null }
    Get-ChildItem -LiteralPath $CloudApp -File | Where-Object { $_.Extension -in '.ps1', '.cs' -or $_.Name -eq 'FGO_Runtime.dll' } | Copy-Item -Destination $app
    Set-Content -LiteralPath (Join-Path $app 'FGO_EnvironmentCheck.ps1') -Value "param([switch]`$AsJson)`r`n'[]'"
    foreach ($name in @('FGOAC_Launcher.ps1', 'FGOAC_StartupChecks.ps1')) { Copy-Item -LiteralPath (Join-Path $repository "overlay\App\$name") -Destination $app }
    Set-Content -LiteralPath (Join-Path $app 'FGOAC_EnvironmentCheck.ps1') -Value "param([switch]`$AsJson,[switch]`$CommunityServer)`r`n'[]'"
    Copy-Item -LiteralPath (Join-Path $GameRoot 'App\segatools.ini') -Destination $baseIni
    Copy-Item -LiteralPath (Join-Path $GameRoot 'App\config.json') -Destination $app
    foreach ($file in @('App\inject.exe', 'App\fgohook.dll', 'App\fgozh.dll', 'App\am\amdaemon.exe', 'App\Tools\Locale_Remulator\LRHookx64.dll', 'AMFS\ICF1', 'AMFS\ICF2')) {
        New-Item -ItemType File -Path (Join-Path $install $file) -Force | Out-Null
    }
    [IO.File]::WriteAllText((Join-Path $install 'DEVICE\aime.txt'), "00000000000000000001`r`n")
    [IO.File]::WriteAllText((Join-Path $install 'update-1.10.json'), '{"version":"1.10"}')
    Write-FakeExe $exe 0x65C5AF1E 0x2039000
    $settings = [ordered]@{
        serverHost = 'auto'; serverPorts = [ordered]@{ http = 80; billing = 8443; aime = 22345 }; requiredPorts = @(80, 8443, 22345)
        autoStartLocalServer = $false; displayMode = 'windowed'; resolutionWidth = 1280; resolutionHeight = 720; inputMode = 'keyboard'
        gameLanguage = 'ja'; graphics = [ordered]@{ highFpsV2 = $true }
        pcport = [ordered]@{ noContinueTimer = $false; noGpPurchase = $true; noMenuTimer = 1 }
        widescreen = [ordered]@{ enable = $true; vertPlus = $false; hudAnchor = $true; anchorFix = 'A1' }
        camera = [ordered]@{ enabled = $true; mouseButton = 2; mouseSensitivity = 0.5; freeKey = 70; turnLeft = 0; lockPad = 1.5; invertX = 1 }
        cursor = [ordered]@{ confine = $true }
    }
    [IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
    $baseBefore = [IO.File]::ReadAllText($baseIni)

    [AppContext]::SetData('APP_CONTEXT_BASE_DIRECTORY', $install + '\')
    $launcher = [Reflection.Assembly]::LoadFrom($assembly)
    $launchScript = $launcher.GetType('FGOLocalPlatform.LaunchScript')
    $prepare = if ($launchScript) { $launchScript.GetMethod('Prepare', [Reflection.BindingFlags]'NonPublic, Static') }
    function Invoke-Prepare { if (!$prepare) { return [pscustomobject]@{ Script = 'missing'; Own = $null; Note = 'LaunchScript.Prepare is missing' } } return $prepare.Invoke($null, @([string]$app)) }
    function Invoke-CheckOnly([string]$Script, [string[]]$Extra = @()) {
        $output = @(& $windowsPowerShell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $Script -CheckOnly @arguments @Extra 2>&1 | ForEach-Object { "$_" })
        return [pscustomobject]@{ ExitCode = $LASTEXITCODE; Text = ($output -join ' | ') }
    }

    # What FGOAC_Launcher.ps1 writes into the runtime ini for these settings, from segatools.ini as it was.
    $ownRun = Invoke-CheckOnly (Join-Path $app 'FGOAC_Launcher.ps1') @('-SkipServerCheck')
    $runtimePath = Join-Path $install 'DEVICE\runtime\segatools.runtime.ini'
    $runtime = if (Test-Path -LiteralPath $runtimePath) { [IO.File]::ReadAllText($runtimePath) } else { '' }
    Remove-Item -LiteralPath (Join-Path $install 'DEVICE\runtime') -Recurse -Force -ErrorAction SilentlyContinue

    # His build with the add-on: the generated copy.
    New-Item -ItemType File -Path (Join-Path $app 'fgoacaddon.dll') -Force | Out-Null
    $choice = Invoke-Prepare
    $hisLines = [IO.File]::ReadAllText($hisScript) -split "`n"
    $genLines = if (Test-Path -LiteralPath $generated) { [IO.File]::ReadAllText($generated) -split "`n" } else { @() }
    $anchorAt = [Array]::FindIndex([string[]]$hisLines, [Predicate[string]] { param($l) $l.Trim() -eq '$launchArguments.Add($agoPath)' })
    # Everything but the lines inserted right before the anchor is his, line for line.
    function Get-Inserted([string[]]$Generated) {
        $count = $Generated.Count - $hisLines.Count
        if ($count -lt 1) { return $null }
        $same = ((@($Generated[0..($anchorAt - 1)]) -join "`n") -ceq (@($hisLines[0..($anchorAt - 1)]) -join "`n")) -and ((@($Generated[($anchorAt + $count)..($Generated.Count - 1)]) -join "`n") -ceq (@($hisLines[$anchorAt..($hisLines.Count - 1)]) -join "`n"))
        if (!$same) { return $null }
        return @($Generated[$anchorAt..($anchorAt + $count - 1)] | ForEach-Object { $_.Trim() })
    }
    $inserted = Get-Inserted $genLines
    $addOnLines = '$launchArguments.Add("-k") / $launchArguments.Add(''' + (Join-Path $app 'fgoacaddon.dll') + ''')'
    $bom = if (Test-Path -LiteralPath $generated) { $b = [IO.File]::ReadAllBytes($generated); $b[0] -eq 0xEF -and $b[1] -eq 0xBB -and $b[2] -eq 0xBF } else { $false }
    $resetAt = [Array]::FindIndex([string[]]$hisLines, [Predicate[string]] { param($l) $l -match '-Section "touch" -Key "inputWidth"' })
    Test-Result 'his build, add-on: a generated copy of his script' ($choice.Script -eq $generated -and $choice.Own -eq $false -and $null -eq $choice.Note -and $null -ne $inserted -and $bom -and (@($inserted[-2..-1]) -join ' / ') -ceq $addOnLines) "script $($choice.Script), note '$($choice.Note)', inserted before the anchor: $(if ($null -eq $inserted) { 'not only there' } else { $inserted -join ' / ' }), BOM: $bom"
    Test-Result 'HUD at the edges: the touch keys re-applied after his reset' ($null -ne $inserted -and $resetAt -ge 0 -and $resetAt -lt $anchorAt -and ($inserted -join "`n") -match 'Set-IniValue -Path \$runtimeIni -Section "touch" -Key "inputWidth"' -and ($inserted -join "`n") -match 'Set-IniValue -Path \$runtimeIni -Section "touch" -Key "inputHeight"') "his reset at line $($resetAt + 1), anchor at line $($anchorAt + 1)"
    Remove-Item -LiteralPath (Join-Path $install 'DEVICE\runtime\segatools.runtime.ini') -ErrorAction SilentlyContinue
    $run = Invoke-CheckOnly $generated
    Test-Result 'his build, add-on: the copy passes his -CheckOnly' ($run.ExitCode -eq 0 -and $run.Text -match 'FGO startup checks passed') "exit $($run.ExitCode): $($run.Text.Substring([Math]::Max(0, $run.Text.Length - 300)))"
    $hisRuntime = if (Test-Path -LiteralPath $runtimePath) { [IO.File]::ReadAllText($runtimePath) } else { '' }
    $touch = @((Get-Section $hisRuntime 'touch') -split ';' | Where-Object { $_ -like 'input*' }) -join ';'
    $ownTouch = @((Get-Section $runtime 'touch') -split ';' | Where-Object { $_ -like 'input*' }) -join ';'
    Test-Result 'HUD at the edges: the whole surface takes touch, as on 11.00' ($touch -ceq 'inputHeight=720;inputWidth=1280' -and $touch -ceq $ownTouch) "his script with the copy: $touch; FGOAC_Launcher.ps1: $ownTouch"

    # Our blocks in segatools.ini, against the runtime ini FGOAC_Launcher.ps1 writes for the same settings.
    $baseAfter = [IO.File]::ReadAllText($baseIni)
    foreach ($section in @('pcport', 'widescreen', 'cursor')) {
        $ours = Get-Section $runtime $section
        $base = Get-Section $baseAfter $section
        Test-Result "[$section] in segatools.ini as FGOAC_Launcher.ps1 writes it" ($ownRun.ExitCode -eq 0 -and $ours.Length -gt 0 -and $base -ceq $ours) "segatools.ini: $base | runtime ini: $ours | FGOAC_Launcher.ps1 exit $($ownRun.ExitCode)$(if ($ownRun.ExitCode -ne 0) { ': ' + $ownRun.Text })"
    }
    Test-Result 'no [camera] block: the camera is Cloud23333''s' ($baseAfter -notmatch '(?m)^\[camera\]' -and $runtime -notmatch '(?m)^\[camera\]') "segatools.ini [camera]: $(Get-Section $baseAfter 'camera') | runtime ini [camera]: $(Get-Section $runtime 'camera')"
    $kept = @(($baseBefore -split "`r?`n") | Where-Object { $_ -ne '' -and !($baseAfter -split "`r?`n").Contains($_) })
    Test-Result 'every other line of segatools.ini stays' ($kept.Count -eq 0) "lines lost: $($kept -join ' | ')"
    Test-Result 'anchors template beside the runtime ini' (Test-Path -LiteralPath (Join-Path $install 'DEVICE\runtime\widescreen-anchors.txt')) 'DEVICE\runtime\widescreen-anchors.txt'

    # HUD in the 16:9 box: only the add-on lines, and his 16:9 touch canvas stays.
    $settings.widescreen.hudAnchor = $false
    [IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
    $choice = Invoke-Prepare
    $inserted = Get-Inserted ([IO.File]::ReadAllText($generated) -split "`n")
    $run = Invoke-CheckOnly $generated
    $touch = @((Get-Section ([IO.File]::ReadAllText($runtimePath)) 'touch') -split ';' | Where-Object { $_ -like 'input*' }) -join ';'
    Test-Result 'HUD in the 16:9 box: his touch canvas' ($null -ne $inserted -and ($inserted -join ' / ') -ceq $addOnLines -and $run.ExitCode -eq 0 -and $touch -ceq 'inputHeight=1080;inputWidth=1920') "inserted: $($inserted -join ' / '), exit $($run.ExitCode), touch: $touch"
    $settings.widescreen.hudAnchor = $true
    [IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))

    # A script of his whose resolution variable has another name: the add-on still goes in, the touch keys say they are off.
    $original = [IO.File]::ReadAllText($hisScript)
    [IO.File]::WriteAllText($hisScript, $original.Replace('$effectiveResolutionHeight', '$effectiveRenderHeight'), [Text.UTF8Encoding]::new($true))
    $choice = Invoke-Prepare
    $text = [IO.File]::ReadAllText($generated)
    $run = Invoke-CheckOnly $generated
    Test-Result 'HUD at the edges on a script it does not know: add-on in, touch said off' ($choice.Script -eq $generated -and $text.Contains('fgoacaddon.dll') -and !$text.Contains('-Section "touch" -Key "inputWidth" -Value ([string]$effectiveResolutionWidth)') -and $choice.Note -match 'touch' -and $run.ExitCode -eq 0) "note '$($choice.Note)', exit $($run.ExitCode)"
    [IO.File]::WriteAllText($hisScript, $original, [Text.UTF8Encoding]::new($true))

    # His script as it is: no add-on, or the line not there exactly once.
    Remove-Item -LiteralPath (Join-Path $app 'fgoacaddon.dll')
    $choice = Invoke-Prepare
    Test-Result 'his build, no add-on: his script' ($choice.Script -eq $hisScript -and $null -eq $choice.Note) "script $($choice.Script), note '$($choice.Note)'"
    New-Item -ItemType File -Path (Join-Path $app 'fgoacaddon.dll') -Force | Out-Null
    $original = [IO.File]::ReadAllText($hisScript)
    foreach ($case in @(@{ Name = 'line missing'; Text = $original.Replace('$launchArguments.Add($agoPath)', '$launchArguments.Add([string]$agoPath)') }, @{ Name = 'line twice'; Text = $original.Replace('$launchArguments.Add($agoPath)', "`$launchArguments.Add(`$agoPath)`n`$launchArguments.Add(`$agoPath)") })) {
        [IO.File]::WriteAllText($hisScript, $case.Text, [Text.UTF8Encoding]::new($true))
        $choice = Invoke-Prepare
        Test-Result "his script with the $($case.Name): run as it is, add-on off" ($choice.Script -eq $hisScript -and $choice.Note -match 'add-on is off') "script $($choice.Script), note '$($choice.Note)'"
    }
    [IO.File]::WriteAllText($hisScript, $original, [Text.UTF8Encoding]::new($true))

    # Hide NP screen on, then his launcher rewrites graphics-effects.ini whole with hide_dark_mask=0: Play sets it back.
    $effectsIni = Join-Path $app 'graphics-effects.ini'
    $iniBefore = [IO.File]::ReadAllText($baseIni)
    [FGOLocalPlatform.BattleEffects]::new($true, $false, 100).Write($app)
    [IO.File]::WriteAllText($effectsIni, "[effects]`nhide_dark_mask=0`nopacity=40`nview_distance=300`n")
    $choice = Invoke-Prepare
    $effects = Get-Section ([IO.File]::ReadAllText($effectsIni)) 'effects'
    [FGOLocalPlatform.BattleEffects]::new($false, $false, 100).Write($app)
    [IO.File]::WriteAllText($effectsIni, "[effects]`nhide_dark_mask=1`nopacity=40`n")
    $off = Invoke-Prepare
    $effectsOff = Get-Section ([IO.File]::ReadAllText($effectsIni)) 'effects'
    [IO.File]::WriteAllText($baseIni, $iniBefore)
    Remove-Item -LiteralPath $effectsIni
    Test-Result 'his build: Hide NP screen put back after his launcher' ($effects -ceq 'hide_dark_mask=1;opacity=40;view_distance=300' -and $effectsOff -ceq 'hide_dark_mask=0;opacity=40' -and $null -eq $choice.Note -and $null -eq $off.Note) "on: $effects; off: $effectsOff"

    # The AMD layer the player chose, on his build: Play leaves its three files in App and says nothing.
    $shimFolder = Join-Path $repository 'compat\amd-shim'
    Copy-Item -LiteralPath (Join-Path $shimFolder 'opengl32.dll') -Destination (Join-Path $app 'opengl32.dll')
    Copy-Item -LiteralPath (Join-Path $env:SystemRoot 'System32\opengl32.dll') -Destination (Join-Path $app 'opengl32real.dll')
    New-Item -ItemType Directory -Path (Join-Path $app 'amdcfg') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $shimFolder 'amdcfg\amdOglpSettings.cfg') -Destination (Join-Path $app 'amdcfg\amdOglpSettings.cfg')
    $shimFiles = 'opengl32.dll', 'opengl32real.dll', 'amdcfg\amdOglpSettings.cfg'
    $choice = Invoke-Prepare
    $kept = @($shimFiles | Where-Object { Test-Path -LiteralPath (Join-Path $app $_) })
    Test-Result 'his build: the AMD layer the player chose stays' ($kept.Count -eq 3 -and $null -eq $choice.Note) "kept: $($kept -join ', '); note '$($choice.Note)'"
    Remove-Item -LiteralPath (Join-Path $app 'opengl32.dll'), (Join-Path $app 'opengl32real.dll'), (Join-Path $app 'amdcfg') -Recurse

    # Game 11.00: our own script.
    Write-FakeExe $exe 0x643F4ECD 0x2034000
    Remove-Item -LiteralPath (Join-Path $install 'update-1.10.json')
    $choice = Invoke-Prepare
    Test-Result 'game 11.00: FGOAC_Launcher.ps1' ($choice.Script -eq (Join-Path $app 'FGOAC_Launcher.ps1') -and $choice.Own -eq $true) "script $($choice.Script)"

    # A later build of his: another exe header and a launch script with a new rule; the line is there once.
    Write-FakeExe $exe 0x66A0B000 0x2040000
    [IO.File]::WriteAllText($hisScript, $original.Replace('$launchArguments.Add($agoPath)', "`$env:FGO_FUTURE_RULE = '1'`r`n`$launchArguments.Add(`$agoPath)"), [Text.UTF8Encoding]::new($true))
    $choice = Invoke-Prepare
    $run = Invoke-CheckOnly $choice.Script
    # gameLanguage "en-fgoac": his own script loads the English set from App\Localization\en-fgoac.
    $settings.gameLanguage = 'en-fgoac'
    [IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
    $english = Invoke-CheckOnly $hisScript
    Test-Result 'his script loads en-fgoac' ($english.ExitCode -eq 0 -and $english.Text -match [Regex]::Escape("[localization] Language 'en-fgoac': $app\Localization\en-fgoac")) "exit $($english.ExitCode): $(@($english.Text -split ' \| ' | Where-Object { $_ -like '*localization*' }) -join ' | ')"
    Test-Result 'a later build of his still starts with the add-on' ($choice.Script -eq $generated -and $null -eq $choice.Note -and [IO.File]::ReadAllText($generated).Contains("FGO_FUTURE_RULE") -and $run.ExitCode -eq 0) "script $($choice.Script), exit $($run.ExitCode)"

    # The English set follows the game files it was made from: an override whose Japanese base changed is turned off alone.
    $englishSet = $launcher.GetType('FGOLocalPlatform.EnglishSet')
    $verify = if ($englishSet) { $englishSet.GetMethod('Verify', [Reflection.BindingFlags]'NonPublic, Static') }
    if (!$verify) {
        Test-Result 'EnglishSet.Verify' $false 'FGOLocalPlatform.EnglishSet.Verify is missing'
    } else {
        $set = Join-Path $app 'Localization\en-fgoac'
        $overrides = [ordered]@{}
        foreach ($name in 'a', 'b') {
            New-Item -ItemType Directory -Path (Join-Path $app 'rom'), (Join-Path $set 'rom') -Force | Out-Null
            [IO.File]::WriteAllText((Join-Path $app "rom\$name.farc"), "japanese $name")
            [IO.File]::WriteAllText((Join-Path $set "rom\$name.farc"), "english $name")
            $overrides["rom/$name.farc"] = (Get-FileHash -LiteralPath (Join-Path $app "rom\$name.farc") -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        [IO.File]::WriteAllText((Join-Path $set 'fgoac-overrides.json'), ([ordered]@{ format = 1; files = $overrides } | ConvertTo-Json -Depth 4))
        $first = $verify.Invoke($null, @([string]$app))
        Test-Result 'English set, first check' (!$first.UpdateDetected -and $first.TurnedOff.Count -eq 0) "update detected: $($first.UpdateDetected), turned off: $($first.TurnedOff -join ', ')"
        Write-FakeExe $exe 0x66B0C000 0x2041000
        [IO.File]::WriteAllText((Join-Path $app 'rom\a.farc'), 'japanese a, changed by his update')
        $second = $verify.Invoke($null, @([string]$app))
        $off = (Test-Path -LiteralPath (Join-Path $set 'rom\a.farc.fgoac-off')) -and !(Test-Path -LiteralPath (Join-Path $set 'rom\a.farc')) -and (Test-Path -LiteralPath (Join-Path $set 'rom\b.farc'))
        Test-Result 'his update: that override off, the rest on, said once' ($second.UpdateDetected -and ($second.TurnedOff -join ',') -eq 'rom\a.farc' -and $off) "update detected: $($second.UpdateDetected), turned off: $($second.TurnedOff -join ', '), files: $off"
        $choice = Invoke-Prepare
        $run = Invoke-CheckOnly $choice.Script
        Test-Result 'the game still starts after it' ($run.ExitCode -eq 0) "exit $($run.ExitCode) through $($choice.Script)"
        $third = $verify.Invoke($null, @([string]$app))
        Test-Result 'nothing new: nothing said' (!$third.UpdateDetected -and $third.TurnedOff.Count -eq 0 -and $third.TurnedOn.Count -eq 0) "update detected: $($third.UpdateDetected)"
        [IO.File]::WriteAllText((Join-Path $app 'rom\a.farc'), 'japanese a')
        $fourth = $verify.Invoke($null, @([string]$app))
        Test-Result 'the base back: the override back on' (($fourth.TurnedOn -join ',') -eq 'rom\a.farc' -and (Test-Path -LiteralPath (Join-Path $set 'rom\a.farc'))) "turned on: $($fourth.TurnedOn -join ', ')"
        # On 11.00 the game reads App\zh, not this set, so its 11.51 bases differ by design: nothing is turned off or said,
        # and an override turned off earlier is put back.
        [IO.File]::WriteAllText((Join-Path $app 'rom\b.farc'), 'japanese b, changed by his update')
        $fifth = $verify.Invoke($null, @([string]$app))
        Write-FakeExe $exe 0x643F4ECD 0x2034000
        Get-ChildItem -LiteralPath (Split-Path -Parent $app) -Filter 'update-*.json' | Remove-Item -Force
        [IO.File]::WriteAllText((Join-Path $app 'rom\a.farc'), 'japanese a of 11.00')
        $sixth = $verify.Invoke($null, @([string]$app))
        $allOn = (Test-Path -LiteralPath (Join-Path $set 'rom\a.farc')) -and (Test-Path -LiteralPath (Join-Path $set 'rom\b.farc')) -and !(Test-Path -LiteralPath (Join-Path $set 'rom\b.farc.fgoac-off'))
        Test-Result '11.00: nothing turned off or said, earlier ones back on' (($fifth.TurnedOff -join ',') -eq 'rom\b.farc' -and !$sixth.UpdateDetected -and $sixth.TurnedOff.Count -eq 0 -and $sixth.TurnedOn.Count -eq 0 -and $allOn) "turned off: $($sixth.TurnedOff -join ', '), update: $($sixth.UpdateDetected), all on: $allOn"
    }
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

$sink = Get-DialogSink
Test-Result 'no window shown' ($sink.Shown -eq 0) $sink.Detail
if ($script:failed -gt 0) { Write-Host "$($script:failed) check(s) failed."; exit 1 }
Write-Host 'Every check passed.'
exit 0
