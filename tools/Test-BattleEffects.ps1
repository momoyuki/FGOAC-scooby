<#
Checks the Battle effects switches without starting the launcher or the game.

  pwsh -File tools\Test-BattleEffects.ps1

Through the launcher assembly (src\bin\Release, so run build.cmd first), in a throwaway App folder under %TEMP%:
nothing saved reads off with opacity 100; ticking both creates graphics-effects.ini with hide_dark_mask and
segatools.ini with hideNpScreen and hideWhiteFlash under [effects], the first tick writing hide_dark_mask and
hideNpScreen alike; the keys are written into files that hold other keys and sections, which come back line for
line with only those keys added; unticking rewrites the keys in place to 0; the first tick reads hideNpScreen
alone, whatever hide_dark_mask holds; a saved 1 reads on and a value that is not a number reads off. The opacity
key: at 100 (the stock hook's own default) it is left unwritten, or removed if a saved file already had it, so
the file stays byte-identical; away from 100 it is written and read back, clamped to 0-100 on write; a number
saved by hand outside 0-100 reads as 100, the hook's own clamp; one that is not a number reads as 0, what
GetPrivateProfileIntW itself returns for that case (the same call the hook makes). The keys four settings need from
the stock hook (skillTarget1, opacity, stop_timer, clickKey) are found in App\fgohook.dll only as whole strings, ASCII or
UTF-16: in a file shaped like the newer build each is found, in one shaped like the older build none is, and with no
file every key counts as read, so no note shows.

Exit codes: 0 every check passed, 1 a check failed, 2 a prerequisite is missing.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'

$repository = Split-Path -Parent $PSScriptRoot
$assembly = Join-Path $repository 'src\bin\Release\net6.0-windows\FGOLocalPlatform.dll'
if ($PSVersionTable.PSVersion.Major -lt 7) { Write-Host 'Run this with pwsh 7: the launcher assembly targets .NET 6.'; exit 2 }
if (!(Test-Path -LiteralPath $assembly)) { Write-Host "Missing $assembly. Run build.cmd first."; exit 2 }
# Every message box, dialog and window of the launcher goes to the sink, never to the screen.
. (Join-Path $PSScriptRoot 'DialogSink.ps1'); Enable-DialogSink $assembly

$script:failed = 0
function Test-Result([string]$Name, [bool]$Passed, [string]$Detail) {
    Write-Host ("{0} {1}: {2}" -f $(if ($Passed) { 'PASS' } else { 'FAIL' }), $Name, $Detail)
    if (!$Passed) { $script:failed++ }
}

$work = Join-Path ([IO.Path]::GetTempPath()) ('fgoac-battle-effects-check-' + [guid]::NewGuid().ToString('N'))
try {
    [void][Reflection.Assembly]::LoadFrom($assembly)
    $effects = [FGOLocalPlatform.BattleEffects]
    New-Item -ItemType Directory -Path $work | Out-Null
    $graphics = Join-Path $work 'graphics-effects.ini'
    $segatools = Join-Path $work 'segatools.ini'
    function Get-Lines([string]$Path) { if (Test-Path -LiteralPath $Path) { @([IO.File]::ReadAllLines($Path) | Where-Object { $_ -ne '' }) -join '|' } else { '(none)' } }

    $read = $effects::Read($work)
    Test-Result 'nothing saved' (!$read.HideNpScreen -and !$read.HideWhiteFlash -and $read.Opacity -eq 100) "$read"

    $effects::new($true, $true, 100).Write($work)
    $read = $effects::Read($work)
    Test-Result 'files created, opacity at default stays unwritten' ((Get-Lines $graphics) -ceq '[effects]|hide_dark_mask=1' -and (Get-Lines $segatools) -ceq '[effects]|hideNpScreen=1|hideWhiteFlash=1' -and $read.HideNpScreen -and $read.HideWhiteFlash -and $read.Opacity -eq 100) "graphics-effects.ini $(Get-Lines $graphics); segatools.ini $(Get-Lines $segatools); read $read"

    $graphicsBefore = "[effects]`r`nunrelated=7`r`n[other]`r`nkeep=1`r`n"
    $segatoolsBefore = "[camera]`r`nenabled=1`r`n[effects]`r`nfoo=2`r`n[io4]`r`ntest=0x70`r`n"
    [IO.File]::WriteAllText($graphics, $graphicsBefore)
    [IO.File]::WriteAllText($segatools, $segatoolsBefore)
    $effects::new($true, $false, 40).Write($work)
    $read = $effects::Read($work)
    Test-Result 'other keys kept, opacity written' ((Get-Lines $graphics) -ceq '[effects]|unrelated=7|hide_dark_mask=1|opacity=40|[other]|keep=1' -and (Get-Lines $segatools) -ceq '[camera]|enabled=1|[effects]|foo=2|hideNpScreen=1|hideWhiteFlash=0|[io4]|test=0x70' -and $read.HideNpScreen -and !$read.HideWhiteFlash -and $read.Opacity -eq 40) "graphics-effects.ini $(Get-Lines $graphics); segatools.ini $(Get-Lines $segatools); read $read"

    $effects::new($false, $true, 40).Write($work)
    $read = $effects::Read($work)
    Test-Result 'rewritten in place' ((Get-Lines $graphics) -ceq '[effects]|unrelated=7|hide_dark_mask=0|opacity=40|[other]|keep=1' -and (Get-Lines $segatools) -ceq '[camera]|enabled=1|[effects]|foo=2|hideNpScreen=0|hideWhiteFlash=1|[io4]|test=0x70' -and !$read.HideNpScreen -and $read.HideWhiteFlash -and $read.Opacity -eq 40) "graphics-effects.ini $(Get-Lines $graphics); segatools.ini $(Get-Lines $segatools); read $read"

    $effects::new($false, $false, 40).Write($work)
    $read = $effects::Read($work)
    Test-Result 'both unticked' ((Get-Lines $graphics) -ceq '[effects]|unrelated=7|hide_dark_mask=0|opacity=40|[other]|keep=1' -and (Get-Lines $segatools) -ceq '[camera]|enabled=1|[effects]|foo=2|hideNpScreen=0|hideWhiteFlash=0|[io4]|test=0x70' -and !$read.HideNpScreen -and !$read.HideWhiteFlash -and $read.Opacity -eq 40) "graphics-effects.ini $(Get-Lines $graphics); segatools.ini $(Get-Lines $segatools); read $read"

    [IO.File]::WriteAllText($graphics, "[effects]`r`nhide_dark_mask=1`r`n")
    [IO.File]::WriteAllText($segatools, "[effects]`r`nhideNpScreen=0`r`n")
    $offRead = $effects::Read($work)
    [IO.File]::WriteAllText($graphics, "[effects]`r`nhide_dark_mask=0`r`n")
    [IO.File]::WriteAllText($segatools, "[effects]`r`nhideNpScreen=1`r`n")
    $onRead = $effects::Read($work)
    Test-Result 'read from hideNpScreen' (!$offRead.HideNpScreen -and $onRead.HideNpScreen) "hide_dark_mask=1 with hideNpScreen=0 read $offRead; hide_dark_mask=0 with hideNpScreen=1 read $onRead"

    [IO.File]::WriteAllText($segatools, "[effects]`r`nhideNpScreen=on`r`nhideWhiteFlash=1`r`n")
    $read = $effects::Read($work)
    Test-Result 'saved values read' (!$read.HideNpScreen -and $read.HideWhiteFlash) "hideNpScreen=on and hideWhiteFlash=1 read $read"

    # Opacity at the stock hook's own default (100) stays out of the file, or is removed if it was there.
    [IO.File]::WriteAllText($graphics, "[effects]`r`nopacity=55`r`n[other]`r`nkeep=1`r`n")
    $effects::new($false, $false, 100).Write($work)
    $read = $effects::Read($work)
    Test-Result 'opacity at default is removed' ((Get-Lines $graphics) -ceq '[effects]|hide_dark_mask=0|[other]|keep=1' -and $read.Opacity -eq 100) "graphics-effects.ini $(Get-Lines $graphics); read $read"

    # Away from the default, it is written and read back.
    $effects::new($false, $false, 55).Write($work)
    $read = $effects::Read($work)
    Test-Result 'opacity away from default is written' ((Get-Lines $graphics) -ceq '[effects]|hide_dark_mask=0|opacity=55|[other]|keep=1' -and $read.Opacity -eq 55) "graphics-effects.ini $(Get-Lines $graphics); read $read"

    # Write clamps a value the slider itself never produces (0-100 only) to that same range: high down to 100
    # (the default, so unwritten), low up to 0 (still written, since 0 is away from the default).
    $effects::new($false, $false, 250).Write($work)
    $highRead = $effects::Read($work)
    Test-Result 'opacity above 100 clamps to 100 on write' ((Get-Lines $graphics) -ceq '[effects]|hide_dark_mask=0|[other]|keep=1' -and $highRead.Opacity -eq 100) "graphics-effects.ini $(Get-Lines $graphics); read $highRead"
    $effects::new($false, $false, 55).Write($work)
    $effects::new($false, $false, -10).Write($work)
    $lowRead = $effects::Read($work)
    Test-Result 'opacity below 0 clamps to 0 on write' ((Get-Lines $graphics) -ceq '[effects]|hide_dark_mask=0|opacity=0|[other]|keep=1' -and $lowRead.Opacity -eq 0) "graphics-effects.ini $(Get-Lines $graphics); read $lowRead"

    # A number saved by hand outside 0-100 reads as the hook's own clamp, 100; one that does not start with a
    # number reads as 0, GetPrivateProfileIntW's own result for that case (0 is in range, so it is used as is,
    # matching what the hook itself would draw).
    [IO.File]::WriteAllText($graphics, "[effects]`r`nopacity=150`r`n")
    Test-Result 'opacity above 100 in the file reads as 100' ($effects::Read($work).Opacity -eq 100) "$($effects::Read($work))"
    [IO.File]::WriteAllText($graphics, "[effects]`r`nopacity=-5`r`n")
    Test-Result 'opacity below 0 in the file reads as 100' ($effects::Read($work).Opacity -eq 100) "$($effects::Read($work))"
    [IO.File]::WriteAllText($graphics, "[effects]`r`nopacity=abc`r`n")
    Test-Result 'opacity not a number reads as 0' ($effects::Read($work).Opacity -eq 0) "$($effects::Read($work))"

    # Cloud23333's launcher writes opacity into graphics-effects.ini from graphics.effectOpacity of fgo-launcher.json
    # at each Play: the same value goes there too, every other key kept; no settings file is created.
    $effects::new($false, $false, 40).Write($work)
    Test-Result 'no settings file: none created' (!(Test-Path -LiteralPath (Join-Path $work 'fgo-launcher.json'))) 'fgo-launcher.json created'
    [IO.File]::WriteAllText((Join-Path $work 'fgo-launcher.json'), '{ "inputMode": "keyboard", "graphics": { "smaa": 2, "effectOpacity": 100 } }')
    $effects::new($false, $false, 40).Write($work)
    $settings = Get-Content -LiteralPath (Join-Path $work 'fgo-launcher.json') -Raw | ConvertFrom-Json
    Test-Result 'his effectOpacity follows, other keys kept' ($settings.graphics.effectOpacity -eq 40 -and $settings.graphics.smaa -eq 2 -and $settings.inputMode -eq 'keyboard') ($settings | ConvertTo-Json -Compress)
    [IO.File]::WriteAllText((Join-Path $work 'fgo-launcher.json'), '{ "inputMode": "keyboard" }')
    $effects::new($false, $false, 100).Write($work)
    $settings = Get-Content -LiteralPath (Join-Path $work 'fgo-launcher.json') -Raw | ConvertFrom-Json
    Test-Result 'his effectOpacity added where missing' ($settings.graphics.effectOpacity -eq 100 -and $settings.inputMode -eq 'keyboard') ($settings | ConvertTo-Json -Compress)
    Remove-Item -LiteralPath (Join-Path $work 'fgo-launcher.json')

    # The keys four settings need from the stock hook, found in fgohook.dll as whole strings, the way the two builds
    # hold them: the newer one names each key in UTF-16; the older one has none of them, only opacity inside longer text.
    $hook = [FGOLocalPlatform.StockHook]
    $hookKeys = 'skillTarget1', 'opacity', 'stop_timer', 'clickKey'
    function Write-Hook([object[]]$Parts) {
        $bytes = [Collections.Generic.List[byte]]::new([byte[]]::new(64))
        foreach ($part in $Parts) { $bytes.AddRange([byte[]]$(if ($part -is [byte[]]) { $part } else { [Text.Encoding]::Unicode.GetBytes("$part`0") })) }
        [IO.File]::WriteAllBytes((Join-Path $work 'fgohook.dll'), $bytes.ToArray())
    }
    function Get-Reads { @($hookKeys | ForEach-Object { "$_=$($hook::Reads($work, $_))" }) -join ',' }
    Test-Result 'no stock hook' ((Get-Reads) -ceq 'skillTarget1=True,opacity=True,stop_timer=True,clickKey=True') "$(Get-Reads): without App\fgohook.dll no row shows a note"
    Write-Hook @('skillTarget1', 'skillTargetCancel', [byte[]](0x0A, 0x00), 'stop_timer', 'opacity', 'fgo_effect_opacity', 'clickKey')
    Test-Result 'newer stock hook' ((Get-Reads) -ceq 'skillTarget1=True,opacity=True,stop_timer=True,clickKey=True') (Get-Reads)
    Write-Hook @([Text.Encoding]::ASCII.GetBytes("Damage UI: (scale/opacity).`n`0"), 'FGO_DAMAGE_NUMBER_OPACITY', 'one_hit', 'fgo_opacity', 'xstop_timer', 'skillTarget12')
    Test-Result 'older stock hook' ((Get-Reads) -ceq 'skillTarget1=False,opacity=False,stop_timer=False,clickKey=False') (Get-Reads)
    Write-Hook (, [Text.Encoding]::ASCII.GetBytes("`0clickKey`0opacity`0"))
    Test-Result 'ASCII keys' ((Get-Reads) -ceq 'skillTarget1=False,opacity=True,stop_timer=False,clickKey=True') (Get-Reads)
}
catch {
    Test-Result 'run' $false ($_ | Out-String)
}
finally {
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}

$sink = Get-DialogSink
Test-Result 'no window shown' ($sink.Shown -eq 0) $sink.Detail
if ($script:failed -gt 0) { Write-Host "$script:failed check(s) failed."; exit 1 }
Write-Host 'Every check passed.'
exit 0
