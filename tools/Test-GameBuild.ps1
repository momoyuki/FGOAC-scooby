<#
Checks that the English patch follows the game build, without starting the launcher or the game. Every file is
written under a throwaway folder in %TEMP%, deleted when the check ends.

  pwsh -File tools\Test-GameBuild.ps1

The fake install holds an App\ago.exe that is only a PE header, carrying either the 11.00 build's TimeDateStamp and
SizeOfImage or the 11.51 build's, with or without the update-1.10.json that Cloud23333's 1.10 updater leaves at the
root. Through the launcher assembly (src\bin\Release, so run build.cmd first): the first run hands the patch to
Apply-EN-Patch.ps1 on every build while a file of the package is missing, and on 11.51 needs no App\zh for the patch
to count as installed. The lines that follow the build: the frame rate line (held at 60 on 11.00, the pick on his
update while his highFpsV2 is true), the English line, exit code 16 (a missing card in our launch script, his
graphics card pick in his), exit 13 and ERROR 4102 without a version number, and [xinput] cardNavigation read as on
when missing on his update only. Then patch\Apply-EN-Patch.ps1 itself against a package of one 11.00 English file and one
file of the launcher's own: both copied on 11.00, only the launcher's own on 11.51 or after the 1.10 update, and
-Rollback refused on 11.51.

Exit codes: 0 every check passed, 1 a check failed, 2 a prerequisite is missing.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'

$repository = Split-Path -Parent $PSScriptRoot
$assembly = Join-Path $repository 'src\bin\Release\net6.0-windows\FGOLocalPlatform.dll'
$patchScript = Join-Path $repository 'patch\Apply-EN-Patch.ps1'
if ($PSVersionTable.PSVersion.Major -lt 7) { Write-Host 'Run this with pwsh 7: the launcher assembly targets .NET 6.'; exit 2 }
if (!(Test-Path -LiteralPath $assembly)) { Write-Host "Missing $assembly. Run build.cmd first."; exit 2 }
# Every message box, dialog and window of the launcher goes to the sink, never to the screen.
. (Join-Path $PSScriptRoot 'DialogSink.ps1'); Enable-DialogSink $assembly

$script:failed = 0
function Test-Result([string]$Name, [bool]$Passed, [string]$Detail) {
    Write-Host ("{0} {1}: {2}" -f $(if ($Passed) { 'PASS' } else { 'FAIL' }), $Name, $Detail)
    if (!$Passed) { $script:failed++ }
}

# A PE header with the two fields the build check reads; the rest of the file is zeros.
function Write-FakeExe([string]$Path, [uint32]$TimeDateStamp, [uint32]$SizeOfImage) {
    $bytes = [byte[]]::new(0x400)
    $bytes[0] = 0x4D; $bytes[1] = 0x5A
    [BitConverter]::GetBytes([int32]0x130).CopyTo($bytes, 0x3C)
    [BitConverter]::GetBytes([uint32]0x00004550).CopyTo($bytes, 0x130)
    [BitConverter]::GetBytes([uint16]0x8664).CopyTo($bytes, 0x134)
    [BitConverter]::GetBytes($TimeDateStamp).CopyTo($bytes, 0x138)
    [BitConverter]::GetBytes([uint16]0xF0).CopyTo($bytes, 0x144)
    [BitConverter]::GetBytes([uint16]0x20B).CopyTo($bytes, 0x148)
    [BitConverter]::GetBytes($SizeOfImage).CopyTo($bytes, 0x148 + 56)
    [IO.File]::WriteAllBytes($Path, $bytes)
}

$work = Join-Path ([IO.Path]::GetTempPath()) ('fgoac-gamebuild-' + [guid]::NewGuid().ToString('N'))
$root = Join-Path $work 'install'
$app = Join-Path $root 'App'
$exe = Join-Path $app 'ago.exe'
$updateMarker = Join-Path $root 'update-1.10.json'
$settingsPath = Join-Path $app 'fgo-launcher.json'
$ran = Join-Path $root 'patch-ran.txt'

function Set-Build([string]$Build) {
    switch ($Build) {
        '1100' { Write-FakeExe $exe 0x643F4ECD 0x2034000; Remove-Item -LiteralPath $updateMarker -ErrorAction SilentlyContinue }
        '1151' { Write-FakeExe $exe 0x65C5AF1E 0x2039000; Remove-Item -LiteralPath $updateMarker -ErrorAction SilentlyContinue }
        '1100+update' { Write-FakeExe $exe 0x643F4ECD 0x2034000; [IO.File]::WriteAllText($updateMarker, '{"version":"1.10"}') }
        'garbage' { [IO.File]::WriteAllBytes($exe, [byte[]]::new(16)); Remove-Item -LiteralPath $updateMarker -ErrorAction SilentlyContinue }
    }
}

function Set-Settings([string]$Json) { [IO.File]::WriteAllText($settingsPath, $Json) }
function Get-Settings { return Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json }

try {
    New-Item -ItemType Directory -Path $app -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $root 'Server') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $app 'FGO_Runtime.dll'), 'stand-in')
    Set-Build '1100'

    # GamePaths.GameRoot is read once from the base directory, so it has to point at the fake install first.
    [AppContext]::SetData('APP_CONTEXT_BASE_DIRECTORY', $root + '\')
    $launcher = [Reflection.Assembly]::LoadFrom($assembly)
    $gameRoot = [FGOLocalPlatform.GamePaths]::GameRoot
    if ($gameRoot -ne $app) { Write-Host "GamePaths.GameRoot is $gameRoot, not $app."; exit 2 }

    $nonPublic = [Reflection.BindingFlags]'NonPublic, Public, Instance, Static'
    $firstRunType = $launcher.GetType('FGOLocalPlatform.FirstRun', $true)
    $applyPatch = $firstRunType.GetMethod('ApplyPatchAsync', $nonPublic)
    $applyDefaults = $firstRunType.GetMethod('ApplyMissingDefaults', $nonPublic)
    $summaryField = $firstRunType.GetField('summary', $nonPublic)

    function New-FirstRun {
        $script:reports = [Collections.Concurrent.ConcurrentQueue[string]]::new()
        $script:logs = [Collections.Concurrent.ConcurrentQueue[string]]::new()
        $constructor = $firstRunType.GetConstructors($nonPublic)[0]
        return $constructor.Invoke(@($null, $null, [Action[string]]$script:reports.Enqueue, [Action[string]]$script:logs.Enqueue))
    }

    # The first run's patch step: a stand-in Apply-EN-Patch.ps1 at the root records that it was started. The manifest
    # lists one file of the 11.00 English set and one of the launcher's own files.
    [IO.File]::WriteAllText((Join-Path $root 'Apply-EN-Patch.ps1'), "Set-Content -LiteralPath '$ran' -Value ran`nexit 0")
    $ownText = 'own launch script'
    $ownHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($ownText))).ToLowerInvariant()
    $zhHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes('english'))).ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $root 'manifest.json'), ('{"version":"9.9.9","manifestHash":"abc","files":{"App\\zh\\english.txt":"' + $zhHash + '","App\\FGOAC_Launcher.ps1":"' + $ownHash + '"}}'))
    $ownPath = Join-Path $app 'FGOAC_Launcher.ps1'
    $markerPath = Join-Path $app 'fgoac-patch.json'

    foreach ($case in @(
            @{ Build = '1100'; Own = $false; Runs = $true }, @{ Build = '1151'; Own = $false; Runs = $true },
            @{ Build = 'garbage'; Own = $false; Runs = $true }, @{ Build = '1151'; Own = $true; Runs = $false },
            @{ Build = '1100+update'; Own = $true; Runs = $false }, @{ Build = '1100'; Own = $true; Runs = $true },
            @{ Build = '1151'; Own = $true; Stale = $true; Runs = $true })) {
        Set-Build $case.Build
        Set-Settings '{"chineseEnabled":true}'
        Remove-Item -LiteralPath $ran, $ownPath, $markerPath -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath (Join-Path $app 'zh') -Recurse -ErrorAction SilentlyContinue
        if ($case.Own) {
            [IO.File]::WriteAllText($ownPath, $ownText)
            [IO.File]::WriteAllText($markerPath, '{"version":"9.9.9","manifestHash":"abc"}')
        }
        if ($case.Stale) { New-Item -ItemType File -Path (Join-Path $app 'zh\english.txt') -Force | Out-Null }
        $firstRun = New-FirstRun
        $patched = $applyPatch.Invoke($firstRun, @()).GetAwaiter().GetResult()
        $started = Test-Path -LiteralPath $ran
        $what = if ($case.Stale) { 'with its own files in place and a stale App\zh' } elseif ($case.Own) { 'with its own files in place and no App\zh' } else { 'with nothing installed' }
        Test-Result "first run on $($case.Build) $what $(if ($case.Runs) { 'runs' } else { 'skips' }) the patch" ($started -eq $case.Runs -and $patched -eq $case.Runs) "started: $started, result: $patched, log: $(@($script:logs) -join ' | ')"
    }

    # The game language: gameLanguage "en-fgoac" for English on every build, "ja" without; chineseEnabled follows only on 11.00.
    $language = $launcher.GetType('FGOLocalPlatform.GameLanguage')
    $write = if ($language) { $language.GetMethod('Write', $nonPublic) }
    $isEnglish = if ($language) { $language.GetMethod('IsEnglish', $nonPublic) }
    if (!$write -or !$isEnglish) {
        Test-Result 'GameLanguage' $false 'FGOLocalPlatform.GameLanguage.Write or IsEnglish is missing'
    } else {
        foreach ($case in @(
                @{ Build = '1100'; Before = '{"chineseEnabled":false}'; English = $true; After = '{"chineseEnabled":true,"gameLanguage":"en-fgoac"}' },
                @{ Build = '1100'; Before = '{"chineseEnabled":true,"gameLanguage":"en-fgoac"}'; English = $false; After = '{"chineseEnabled":false,"gameLanguage":"ja"}' },
                @{ Build = '1151'; Before = '{"chineseEnabled":false}'; English = $true; After = '{"chineseEnabled":false,"gameLanguage":"en-fgoac"}' },
                @{ Build = '1151'; Before = '{"chineseEnabled":true,"gameLanguage":"en-fgoac"}'; English = $false; After = '{"chineseEnabled":true,"gameLanguage":"ja"}' })) {
            Set-Build $case.Build
            $node = [Text.Json.Nodes.JsonNode]::Parse($case.Before)
            [void]$write.Invoke($null, @($node, $case.English, [IO.Path]::GetFullPath((Join-Path $app '..'))))
            Test-Result "save on $($case.Build), English $($case.English)" ($node.ToJsonString() -ceq $case.After) "$($case.Before) -> $($node.ToJsonString())"
        }
        $reads = foreach ($json in '{"gameLanguage":"en-fgoac"}', '{"gameLanguage":"en"}', '{"gameLanguage":"ja"}', '{"gameLanguage":"zh"}', '{"chineseEnabled":true}', '{"chineseEnabled":false}', '{}') {
            "$($isEnglish.Invoke($null, [object[]]@(,[Text.Json.Nodes.JsonNode]::Parse($json))))"
        }
        Test-Result 'the switch reads English' (($reads -join ',') -ceq 'True,True,False,False,True,False,False') ($reads -join ',')
    }

    # The first run gives a settings file from before gameLanguage the language its chineseEnabled meant, once.
    foreach ($case in @(@{ Settings = '{"chineseEnabled":true}'; Language = 'en-fgoac' }, @{ Settings = '{"chineseEnabled":false}'; Language = 'ja' }, @{ Settings = '{"gameLanguage":"en"}'; Language = 'en' })) {
        Set-Build '1151'
        Set-Settings ('{"displayMode":"windowed","resolutionWidth":1280,"resolutionHeight":720,"inputMode":"keyboard","monitorDevice":"\\\\.\\DISPLAY1","gpuCompat":false,' + $case.Settings.TrimStart('{'))
        $firstRun = New-FirstRun
        [void]$applyDefaults.Invoke($firstRun, @($false, $false))
        $settings = Get-Settings
        Test-Result "first run with $($case.Settings)" ($settings.gameLanguage -ceq $case.Language) "gameLanguage $($settings.gameLanguage)"
    }

    # The input mode: a missing one follows the controller; on the launcher's first start in a folder, controller mode
    # with no controller connected becomes keyboard and mouse; any other start leaves the saved mode as it is.
    foreach ($case in @(
            @{ Input = $null; First = $false; Pad = $false; After = 'keyboard' }, @{ Input = $null; First = $false; Pad = $true; After = 'xinput' },
            @{ Input = 'xinput'; First = $true; Pad = $false; After = 'keyboard'; Said = $true }, @{ Input = 'xinput'; First = $true; Pad = $true; After = 'xinput' },
            @{ Input = 'xinput'; First = $false; Pad = $false; After = 'xinput' }, @{ Input = 'keyboard'; First = $true; Pad = $true; After = 'keyboard' })) {
        $inputJson = if ($case.Input) { '"inputMode":"' + $case.Input + '",' } else { '' }
        Set-Settings ('{' + $inputJson + '"displayMode":"windowed","resolutionWidth":1280,"resolutionHeight":720,"monitorDevice":"\\\\.\\DISPLAY1","gameLanguage":"en-fgoac"}')
        $firstRun = New-FirstRun
        [void]$applyDefaults.Invoke($firstRun, @($case.First, $case.Pad))
        $said = (@($summaryField.GetValue($firstRun)) -join ' ') -match 'keyboard and mouse'
        Test-Result "input $(if ($case.Input) { $case.Input } else { 'missing' }), first start $($case.First), controller $($case.Pad) -> $($case.After)" ((Get-Settings).inputMode -ceq $case.After -and (!$case.Said -or $said)) "inputMode $((Get-Settings).inputMode), summary: $(@($summaryField.GetValue($firstRun)) -join ' | ')"
    }

    # The first start: neither the patch marker nor the marker launchers up to 1.2.0 kept in App\zh.
    $firstStart = $firstRunType.GetMethod('OurFirstStart', $nonPublic)
    if (!$firstStart) {
        Test-Result 'first start' $false 'FirstRun.OurFirstStart is missing'
    } else {
        $legacyMarker = Join-Path $app 'zh\en-patch.json'
        $starts = foreach ($case in 'none', 'marker', 'legacy') {
            Remove-Item -LiteralPath $markerPath, $legacyMarker -ErrorAction SilentlyContinue
            if ($case -eq 'marker') { [IO.File]::WriteAllText($markerPath, '{}') }
            if ($case -eq 'legacy') { New-Item -ItemType File -Path $legacyMarker -Force | Out-Null }
            "$case=$($firstStart.Invoke($null, @([string]$app)))"
        }
        Remove-Item -LiteralPath $markerPath, $legacyMarker -ErrorAction SilentlyContinue
        Test-Result 'first start' (($starts -join ',') -ceq 'none=True,marker=False,legacy=False') ($starts -join ',')
    }

    # Cards for things his 1.10 update removes: the Banners patch of his 1.02 server files, the Easter egg's files.
    $installRoot = [IO.Path]::GetFullPath((Join-Path $app '..'))
    $banners = $launcher.GetType('FGOLocalPlatform.BannerSettingsView').GetMethod('Offered', $nonPublic)
    $easter = $launcher.GetType('FGOLocalPlatform.EasterEggSettingsView').GetMethod('Offered', $nonPublic)
    if (!$banners -or !$easter) {
        Test-Result 'cards his update retires' $false 'BannerSettingsView.Offered or EasterEggSettingsView.Offered is missing'
    } else {
        $shown = foreach ($case in @(@{ Build = '1100'; Egg = $true }, @{ Build = '1100'; Egg = $false }, @{ Build = '1151'; Egg = $true }, @{ Build = '1151'; Egg = $false })) {
            Set-Build $case.Build
            if ($case.Egg) { New-Item -ItemType Directory -Path (Join-Path $app 'EasterEgg') -Force | Out-Null } else { Remove-Item -LiteralPath (Join-Path $app 'EasterEgg') -Recurse -Force -ErrorAction SilentlyContinue }
            "$($case.Build):$($banners.Invoke($null, @($installRoot))):$($easter.Invoke($null, @([string]$app)))"
        }
        Test-Result 'cards his update retires' (($shown -join ',') -ceq '1100:True:True,1100:True:False,1151:False:True,1151:False:False') ($shown -join ',')
    }

    # The frame rate line: held at 60 on 11.00; on his update the pick applies while his updater's highFpsV2 is true.
    $frameRate = $launcher.GetType('FGOLocalPlatform.MainWindow').GetMethod('FrameRateHelp', $nonPublic)
    if (!$frameRate) {
        Test-Result 'frame rate line' $false 'MainWindow.FrameRateHelp is missing'
    } else {
        $rates = foreach ($case in @(@($true, '{"highFpsV2":true}'), @($false, '{"highFpsV2":true}'), @($false, '{"highFpsV2":false}'), @($false, '{}'))) {
            [string]$frameRate.Invoke($null, @($case[0], [Text.Json.Nodes.JsonNode]::Parse($case[1])))
        }
        Test-Result 'frame rate line' ($rates[0] -ceq 'On this game version the game runs at 60 FPS whatever is picked: higher rates break the menus and touch.' -and $rates[1] -ceq 'The game runs at the rate picked.' -and $rates[2] -ceq 'The game runs at 60 FPS whatever is picked while High frame rate timing below is off.' -and $rates[3] -ceq $rates[2] -and ($rates -join ' ') -notmatch '11\.00') ($rates -join ' | ')
    }

    # The English line: on his update it says a game started from his launcher shows Japanese text.
    $languageHelp = if ($language) { $language.GetMethod('Help', $nonPublic) }
    if (!$languageHelp) {
        Test-Result 'English line' $false 'GameLanguage.Help is missing'
    } else {
        $own = [string]$languageHelp.Invoke($null, @($true)); $his = [string]$languageHelp.Invoke($null, @($false))
        Test-Result 'English line' ($own -ceq 'Off leaves the game in Japanese. The switch takes effect the next time you press Play.' -and $his.StartsWith($own) -and $his.EndsWith(" Start the game from FGOAC scooby for this English: started from Cloud23333's launcher, the game's text stays Japanese.")) "$own | $his"
    }

    # The exit codes follow the launch script of the build: 16 is a missing card in ours, his graphics card pick in his.
    $diagnostics = $launcher.GetType('FGOLocalPlatform.StartupDiagnostics')
    $explain = $diagnostics.GetMethod('Explain', $nonPublic, $null, [type[]]@([int]), $null)
    $codes = foreach ($build in '1100', '1151') { Set-Build $build; [string]$explain.Invoke($null, @(16)) }
    Test-Result 'exit 16 follows the build' ($codes[0] -match 'Master menu on Play' -and $codes[1] -match '^The graphics card picked for the game could not be used' -and $codes[1] -match 'Save Display Settings') ($codes -join ' | ')
    $thirteen = [string]$explain.Invoke($null, @(13))
    $errors = [string]$diagnostics.GetField('GameErrors').GetValue($null)
    Test-Result 'exit 13 and ERROR 4102 for every build' ($thirteen -notmatch '11\.00' -and $thirteen -match 'gameVersion' -and $errors -match 'NamedPipe\.cpp' -and $errors -notmatch '11\.00') "$thirteen | $($errors.Substring(0, 400))"

    # His 1.10 hook takes a missing [xinput] cardNavigation as on, so RB is Card Right; on 11.00 nothing has it.
    [IO.File]::WriteAllText((Join-Path $app 'segatools.ini'), "[xinput]`r`ncardRight=0x200`r`n")
    $rb = foreach ($build in '1100', '1151') { Set-Build $build; "${build}:$([FGOLocalPlatform.StockControls]::Read((Join-Path $app 'segatools.ini')).PadAction([FGOLocalPlatform.Chord]::PadCode + 9))" }
    Test-Result 'card navigation on unless his ini turns it off' (($rb -join ',') -ceq '1100:,1151:Card Right') ($rb -join ',')

    # Apply-EN-Patch.ps1 against a two-file package, the way the updater and a player run it.
    $package = Join-Path $work 'package'
    $payloadZh = Join-Path $package 'payload\App\zh\english.txt'
    $payloadOwn = Join-Path $package 'payload\App\FGOAC_Launcher.ps1'
    New-Item -ItemType Directory -Path (Split-Path -Parent $payloadZh) -Force | Out-Null
    [IO.File]::WriteAllText($payloadZh, 'english')
    [IO.File]::WriteAllText($payloadOwn, $ownText)
    Copy-Item -LiteralPath (Join-Path $root 'manifest.json') -Destination (Join-Path $package 'manifest.json')
    Copy-Item -LiteralPath $patchScript -Destination (Join-Path $package 'Apply-EN-Patch.ps1')
    $installed = Join-Path $app 'zh\english.txt'

    foreach ($case in @(@{ Build = '1151'; English = $false }, @{ Build = '1100+update'; English = $false }, @{ Build = '1100'; English = $true })) {
        Set-Build $case.Build
        Set-Settings '{"chineseEnabled":false}'
        Remove-Item -LiteralPath (Join-Path $app 'zh') -Recurse -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $ownPath, $markerPath -ErrorAction SilentlyContinue
        $output = @(& (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File (Join-Path $package 'Apply-EN-Patch.ps1') -InstallRoot $root -PackageRoot $package -NonInteractive *>&1 | ForEach-Object { "$_" })
        $exitCode = $LASTEXITCODE
        $zh = Test-Path -LiteralPath (Join-Path $app 'zh')
        $own = Test-Path -LiteralPath $ownPath
        $markerWritten = Test-Path -LiteralPath $markerPath
        $settings = Get-Settings
        if ($case.English) {
            Test-Result "Apply-EN-Patch.ps1 on $($case.Build) copies the 11.00 English set and its own files" ($exitCode -eq 0 -and (Test-Path -LiteralPath $installed) -and $own -and $markerWritten) "exit $exitCode, App\zh: $zh, own: $own, marker: $markerWritten, last line: $($output[-1])"
            Test-Result "Apply-EN-Patch.ps1 first install on $($case.Build) turns English on both ways" ($settings.gameLanguage -ceq 'en-fgoac' -and $settings.chineseEnabled -eq $true) "gameLanguage $($settings.gameLanguage), chineseEnabled $($settings.chineseEnabled)"
        } else {
            Test-Result "Apply-EN-Patch.ps1 on $($case.Build) copies its own files only" ($exitCode -eq 0 -and !$zh -and $own -and $markerWritten) "exit $exitCode, App\zh: $zh, own: $own, marker: $markerWritten, last line: $($output[-1])"
            Test-Result "Apply-EN-Patch.ps1 first install on $($case.Build) sets gameLanguage only" ($settings.gameLanguage -ceq 'en-fgoac' -and $settings.chineseEnabled -eq $false) "gameLanguage $($settings.gameLanguage), chineseEnabled $($settings.chineseEnabled)"
        }
    }

    # An update keeps the player's language; a settings file from before gameLanguage gets it from chineseEnabled.
    foreach ($case in @(@{ Settings = '{"chineseEnabled":true}'; Language = 'en-fgoac' }, @{ Settings = '{"chineseEnabled":false}'; Language = 'ja' }, @{ Settings = '{"chineseEnabled":true,"gameLanguage":"ja"}'; Language = 'ja' })) {
        Set-Build '1151'
        Set-Settings $case.Settings
        Remove-Item -LiteralPath $ownPath -ErrorAction SilentlyContinue
        $output = @(& (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File (Join-Path $package 'Apply-EN-Patch.ps1') -InstallRoot $root -PackageRoot $package -NonInteractive *>&1 | ForEach-Object { "$_" })
        $settings = Get-Settings
        Test-Result "Apply-EN-Patch.ps1 update with $($case.Settings)" ($LASTEXITCODE -eq 0 -and $settings.gameLanguage -ceq $case.Language) "gameLanguage $($settings.gameLanguage), last line: $($output[-1])"
    }

    # The 11.00 case above left a backup; a rollback on the new build must not put it back.
    Set-Build '1151'
    $output = @(& (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File (Join-Path $package 'Apply-EN-Patch.ps1') -InstallRoot $root -PackageRoot $package -NonInteractive -Rollback *>&1 | ForEach-Object { "$_" })
    $exitCode = $LASTEXITCODE
    $kept = Test-Path -LiteralPath $ownPath
    $settings = Get-Settings
    Test-Result 'Apply-EN-Patch.ps1 -Rollback on 1151 turns English off and puts back no 11.00 file' ($exitCode -eq 0 -and $kept -and !(Test-Path -LiteralPath $markerPath) -and $settings.gameLanguage -ceq 'ja' -and ($output -join ' ') -match 'latest update') "exit $exitCode, files kept: $kept, gameLanguage $($settings.gameLanguage), last line: $($output[-1])"
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

$sink = Get-DialogSink
Test-Result 'no window shown' ($sink.Shown -eq 0) $sink.Detail
if ($script:failed -gt 0) { Write-Host "$($script:failed) check(s) failed."; exit 1 }
Write-Host 'Every check passed.'
exit 0
