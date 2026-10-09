<#
Checks the input layer without starting the launcher or the game.

  pwsh -File tools\Test-Input.ps1

Through the launcher assembly (src\bin\Release, so run build.cmd first) it reads chords in the bindings
file's words and back; parses a good bindings file, a garbage one and one that uses the stock controls,
which are refused with one sentence each; writes the file twice and finds the first copy kept as .bak.
It checks what the Input card's key and pad pickers ask Bindings before accepting a key or pad button, each button
of a two-button pick on its own, with Shift, Ctrl and Alt matching either side.
It turns Canvas points into client points for 1280x720, 1920x1080, 2560x1440 and 3440x1440 surfaces and
back through the stock hook's own gate (MAP section 2, written out below), within 1 px; a point outside
the 16:9 area of a wider surface has no client point. It plays the chord rule on key sequences. Then it
starts the layer against a hidden window of this process with key, pad and foreground state set by the
test: each bound press posts one move, one touch button down and one touch button up at the expected
client point, and nothing while the window is not in front. The touch button is [touch] clickKey as the
stock hook reads it (0 to 254, else the left button); taps use that mouse button's messages, and a key
that is not a mouse button taps nothing. The Cursor card's Tap with writes clickKey only away from the left
button, which the stock controls then read, and refuses a button Controls or Bindings use.
Cloud23333's 1.10 hook takes a missing [xinput] cardNavigation as on. While his keyboard battle mode ([battleKeyboard]
enabled) is on, the bindings tap nothing.

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

# The stock hook's gate from a client point to a Canvas point (fgohook +0x25840 viewport, +0x259F0 inverse).
function Get-Viewport([int]$SW, [int]$SH, [int]$ClientH, [bool]$Preserve) {
    [long]$vw = $SW; [long]$vh = $SH
    if ($Preserve) {
        $a = [long]$SW * 1080; $b = [long]$SH * 1920
        if ($a -lt $b) { $vh = [Math]::Truncate($a / 1920) } elseif ($a -gt $b) { $vw = [Math]::Truncate($b / 1080) }
    }
    $vw = $vw -band -2; $vh = $vh -band -2
    $left = [Math]::Truncate(($SW - $vw) / 2)
    $top = $ClientH - [Math]::Truncate(($SH - $vh) / 2) - $vh
    return @{ Left = [long]$left; Top = [long]$top; Width = $vw; Height = $vh }
}
function ConvertTo-CanvasPoint([int]$X, [int]$Y, [int]$SW, [int]$SH, [int]$ClientH, [bool]$Preserve) {
    $v = Get-Viewport $SW $SH $ClientH $Preserve
    $x = [Math]::Min([Math]::Max([long]$X, $v.Left), $v.Left + $v.Width - 1)
    $y = [Math]::Min([Math]::Max([long]$Y, $v.Top), $v.Top + $v.Height - 1)
    $tx = [Math]::Floor(($x * 100 * 1920 + $SW * 50) / ($SW * 100))
    $ty = [Math]::Floor((($SH - $ClientH + $y) * 100 * 1080 + $SH * 50) / ($SH * 100))
    return @([Math]::Min([Math]::Max($tx, 0), 1919), [Math]::Min([Math]::Max($ty, 0), 1079))
}

$work = Join-Path ([IO.Path]::GetTempPath()) ('fgoac-input-check-' + [guid]::NewGuid().ToString('N'))
$layers = @()
$window = $null
try {
    [void][Reflection.Assembly]::LoadFrom($assembly)
    $chord = [FGOLocalPlatform.Chord]
    $bindingsType = [FGOLocalPlatform.Bindings]
    $stockType = [FGOLocalPlatform.StockControls]

    # Chords in the file's words.
    $keys = $chord::ParseKeys('Ctrl+Space')
    Test-Result 'key chord' ((($keys.Codes | ForEach-Object { '0x{0:X}' -f $_ }) -join ',') -eq '0x11,0x20' -and $keys.Format() -ceq 'Ctrl+Space') "$($keys.Format()): $($keys.Codes -join ',')"
    $hex = $chord::ParseKeys('0x70 + k')
    Test-Result 'key chord, number and lower case' ($hex.Format() -ceq 'K+F1') $hex.Format()
    $pad = $chord::ParsePad('LB+A')
    Test-Result 'pad chord' ($pad.PadMask -eq 0x1100 -and $pad.Format() -ceq 'LB+A') "$($pad.Format()) mask 0x$('{0:X}' -f $pad.PadMask)"
    Test-Result 'pad chord, mask' ($chord::ParsePad('0x3000').Format() -ceq 'A+B') $chord::ParsePad('0x3000').Format()
    $garbage = @('', '+', 'Ctrl+', 'Nope', '0x1FF', 'Ctrl+Ctrl') | Where-Object { $null -ne $chord::ParseKeys($_) }
    Test-Result 'key chord garbage' ($garbage.Count -eq 0) "parsed: $($garbage -join ' | ')"
    $garbage = @('', 'A+', 'Z', '0x0', '0x100000', 'A+A') | Where-Object { $null -ne $chord::ParsePad($_) }
    Test-Result 'pad chord garbage' ($garbage.Count -eq 0) "parsed: $($garbage -join ' | ')"

    # A good bindings file.
    $stock = $stockType::Parse('')
    $refused = [Collections.Generic.List[string]]::new()
    $good = @"
[profile]
name=pad

[buttons]
title=960,540
confirm=1500,900
myButton=100,200

[keys]
title=F5
closePopup=Ctrl+P
aimeYes=P

[pad]
closePopup=Back
confirm=RT+Up

[cursor]
sensitivity=1500
deadZone=9000
click=R3
"@
    $b = $bindingsType::Parse($good, $stock, [Action[string]]{ param($line) $refused.Add($line) })
    $title = $b.Buttons | Where-Object Name -eq 'title'
    $confirm = $b.Buttons | Where-Object Name -eq 'confirm'
    $retry = $b.Buttons | Where-Object Name -eq 'retry'
    $mine = $b.Buttons | Where-Object Name -eq 'myButton'
    Test-Result 'good file' ($refused.Count -eq 0 -and $b.Profile -ceq 'pad' -and $b.Key('title').Format() -ceq 'F5' -and $b.Key('closePopup').Format() -ceq 'Ctrl+P' -and $b.Pad('confirm').Format() -ceq 'Up+RT' -and $b.Format() -notmatch '\[cursor\]') "refused: $($refused -join ' | ')"
    Test-Result 'good file, places' ($title.X -eq 960 -and $title.Y -eq 540 -and $confirm.X -eq 1500 -and !$retry.Placed -and $mine.X -eq 100 -and $mine.Label -ceq 'myButton') "title $($title.X),$($title.Y); confirm $($confirm.X),$($confirm.Y); retry placed $($retry.Placed); myButton $($mine.X),$($mine.Y)"
    $known = @('title', 'aimeYes', 'gpCard', 'closePopup', 'terminalHome', 'grailWar', 'nationwideMatch', 'start', 'confirm', 'back', 'retry', 'nextQuest')
    $defaults = $bindingsType::Parse('', $stock, [Action[string]]{ param($line) $refused.Add($line) })
    $placed = @($defaults.Buttons | Where-Object Placed | ForEach-Object { "$($_.Name)=$($_.X),$($_.Y)" })
    Test-Result 'default buttons' ((@($defaults.Buttons | ForEach-Object Name) -join ',') -ceq ($known -join ',') -and ($placed -join ' ') -ceq 'title=960,540 aimeYes=1160,690 gpCard=360,585 closePopup=960,850 grailWar=1462,520 nationwideMatch=660,510 start=1344,827' -and $defaults.Profile -ceq 'keyboard+mouse') ($placed -join ' ')

    # Written and read back, through AtomicFile.
    $path = Join-Path $work 'DEVICE\runtime\bindings.ini'
    $b.Save($path)
    $b.Save($path)
    $again = $bindingsType::Load($path, $stock, [Action[string]]{ param($line) $refused.Add($line) })
    Test-Result 'saved and read back' ($again.Format() -ceq $b.Format() -and (Test-Path -LiteralPath "$path.bak") -and $refused.Count -eq 0) ([IO.File]::ReadAllText($path) -replace "`r?`n", ' | ')
    Test-Result 'unset stays unset' ([IO.File]::ReadAllText($path) -match '(?m)^retry=unset\r?$') 'retry=unset'
    Test-Result 'path in the game folder' ($bindingsType::PathIn('D:\Games\FGOA\App') -ceq 'D:\Games\FGOA\DEVICE\runtime\bindings.ini') $bindingsType::PathIn('D:\Games\FGOA\App')
    $missing = $bindingsType::Load((Join-Path $work 'none.ini'), $stock, [Action[string]]{ param($line) $refused.Add($line) })
    Test-Result 'missing file' ($missing.Format() -ceq $defaults.Format() -and $refused.Count -eq 0) 'defaults, nothing logged'
    # A check at edit time reads the file quietly: one it cannot open has no bindings.
    $lockedPath = Join-Path $work 'locked-bindings.ini'
    [IO.File]::WriteAllText($lockedPath, "[keys]`r`ntitle=F5`r`n")
    $readable = $bindingsType::LoadQuietly($lockedPath, $stock).Key('title')
    $lock = [IO.File]::Open($lockedPath, 'Open', 'ReadWrite', 'None')
    try { $locked = $bindingsType::LoadQuietly($lockedPath, $stock) } finally { $lock.Dispose() }
    Test-Result 'read quietly' ($readable.Format() -ceq 'F5' -and $locked.Format() -ceq $defaults.Format()) "readable: title=$($readable.Format()); locked: $($locked.Format() -replace "`r?`n", ' | ')"

    # A garbage file: defaults for what cannot be read, one line each.
    $refused.Clear()
    $bad = @"
this is not an ini
[profile]
name=joystick
[buttons]
title=9999,5
aimeYes=abc
[keys]
title=Nope
nobody=F6
[pad]
closePopup=Z
[cursor]
sensitivity=-4
deadZone=40000
click=Q
"@
    $g = $bindingsType::Parse($bad, $stock, [Action[string]]{ param($line) $refused.Add($line) })
    $gTitle = $g.Buttons | Where-Object Name -eq 'title'
    Test-Result 'garbage file' ($g.Profile -ceq 'keyboard+mouse' -and $gTitle.X -eq 960 -and $null -eq $g.Key('title') -and $null -eq $g.Pad('closePopup') -and $refused.Count -eq 7) "$($refused.Count) lines: $($refused -join ' | ')"

    # The stock controls: every key and pad button the stock hook reads is refused.
    $refused.Clear()
    $collide = @"
[keys]
title=Space
aimeYes=LShift+U
gpCard=Shift+U
closePopup=RShift+U
grailWar=Esc
start=1
[pad]
title=A
aimeYes=RB+X
gpCard=R3
closePopup=RB
nationwideMatch=Back
start=Back
"@
    $c = $bindingsType::Parse($collide, $stock, [Action[string]]{ param($line) $refused.Add($line) })
    $expected = @(
        'Space is Attack in Controls, so it cannot also tap Title screen: touch to start.',
        'Left Shift is Dash in Controls, so it cannot also tap Card check: Yes.',
        'Shift is Dash in Controls, so it cannot also tap GP purchase: 300 GP.',
        'Esc is Skill Target Cancel in Controls, so it cannot also tap Terminal: Grail War.',
        '1 is Servant Skill 1 in Controls, so it cannot also tap Grail War: Start.',
        'A is Attack in Controls, so it cannot also tap Title screen: touch to start.',
        'X is Noble Phantasm in Controls, so it cannot also tap Card check: Yes.',
        'Back already taps Grail War: Nationwide Match.'
    )
    $missed = @($expected | Where-Object { $refused -notcontains $_ })
    Test-Result 'stock controls refused' ($missed.Count -eq 0 -and $refused.Count -eq $expected.Count -and $c.Key('closePopup').Format() -ceq 'RShift+U' -and $c.Pad('gpCard').Format() -ceq 'R3') "missed: $($missed -join ' | '); logged: $($refused -join ' | ')"
    $custom = $stockType::Parse("[keyboard]`r`nattack=0x2`r`nnp=0x74`r`n[xinput]`r`nmovement=2`r`nnp=0`r`n")
    $check = @(
        $c.Bind('title', $chord::ParseKeys('F5'), $custom),
        $c.Bind('title', $chord::ParseKeys('Space'), $custom),
        $c.Bind('aimeYes', $chord::ParsePad('Up'), $custom),
        $c.Bind('aimeYes', $chord::ParsePad('X'), $custom)
    )
    $touch = @('', "[touch]`r`nclickKey=2", "[touch]`r`nclickKey=0x6", "[touch]`r`nclickKey=255", "[touch]`r`nclickKey=-3") | ForEach-Object { $stockType::Parse($_).TouchKey }
    Test-Result 'touch key' (($touch -join ',') -ceq '1,2,6,1,1' -and $stockType::Parse("[touch]`r`nclickKey=2").KeyAction(2) -ceq 'Touch' -and $null -eq $stockType::Parse("[touch]`r`nclickKey=2").KeyAction(1)) ($touch -join ',')
    Test-Result 'stock controls as saved' ($check[0] -ceq 'F5 is Noble Phantasm in Controls, so it cannot also tap Title screen: touch to start.' -and $null -eq $check[1] -and $check[2] -ceq 'D-Pad Up is Move in Controls, so it cannot also tap Card check: Yes.' -and $null -eq $check[3]) ($check -join ' | ')

    # Cloud23333's 1.10 pad keys, where his update has put them in the ini: card navigation (LB/RB/A/B by default), the
    # pad mouse toggle (LT) and, with his modern camera on, the camera lock (R3).
    $his = $stockType::Parse("[xinput]`r`ncardNavigation=1`r`ncardLeft=0x100`r`ncardRight=0x200`r`ncardConfirm=0x1000`r`ncardUndo=0x2000`r`nmouseToggle=0x10000`r`ncameraLock=0x80`r`nmodernCamera=1`r`n")
    $off = $stockType::Parse("[xinput]`r`ncardNavigation=0`r`ncardRight=0x200`r`ncameraLock=0x80`r`nmodernCamera=0`r`n")
    $padCode = [FGOLocalPlatform.Chord]::PadCode
    $hisActions = @($his.PadAction($padCode + 9), $his.PadAction($padCode + 7), $his.PadAction($padCode + 16), $off.PadAction($padCode + 9), $off.PadAction($padCode + 7), $stock.PadAction($padCode + 9))
    Test-Result 'his pad keys' ($hisActions[0] -ceq 'Card Right' -and $hisActions[1] -ceq 'Camera Lock' -and $hisActions[2] -ceq 'Dash' -and $null -eq $hisActions[3] -and $null -eq $hisActions[4] -and $null -eq $hisActions[5]) ($hisActions -join ' | ')
    Test-Result 'his card navigation refuses a binding' ($c.Bind('closePopup', $chord::ParsePad('RB'), $his) -ceq 'RB is Card Right in Controls, so it cannot also tap Popup: Close.') $c.Bind('closePopup', $chord::ParsePad('RB'), $his)

    $json = [System.Text.Json.Nodes.JsonNode]
    $plainStock = $stockType::Parse("[keyboard]`r`nattack=0x2`r`n")
    $plainBindings = $bindingsType::Parse("[keys]`r`ntitle=F5`r`n[pad]`r`ntitle=Back`r`n", $plainStock, [Action[string]]{ param($line) throw "bindings: $line" })

    # His 1.10 hook takes a missing [xinput] cardNavigation as on; an older hook has no card navigation.
    $padCode = [FGOLocalPlatform.Chord]::PadCode
    $navigation = @($stockType::Parse("[xinput]`r`ncardRight=0x200`r`n", $true).PadAction($padCode + 9), $stockType::Parse("[xinput]`r`ncardRight=0x200`r`n", $false).PadAction($padCode + 9), $stockType::Parse("[xinput]`r`ncardNavigation=0`r`n", $true).PadAction($padCode + 9))
    Test-Result 'his hook: card navigation on unless turned off' ($navigation[0] -ceq 'Card Right' -and $null -eq $navigation[1] -and $null -eq $navigation[2]) ($navigation -join ' | ')

    # His keyboard battle mode, while it is on, reads the mouse buttons as battle actions, so the bindings tap nothing.
    $freeOn = $stockType::Parse("[battleKeyboard]`r`nenabled=1`r`n")
    $freeOff = $stockType::Parse("[battleKeyboard]`r`nenabled=0`r`nlock=0x5`r`n")
    Test-Result 'his keyboard battle mode turns the taps off' ($freeOn.TapsOff -ceq 'Cloud23333''s keyboard battle mode is on and reads the mouse buttons as battle actions, so the bindings tap nothing.' -and $null -eq $freeOff.TapsOff -and $freeOn.KeyboardFreeCamera -and !$freeOff.KeyboardFreeCamera) "$($freeOn.TapsOff) | $($freeOff.TapsOff)"

    # The Input card's key and pad pickers: a key or pad button Bindings uses is refused with its sentence, each
    # button of a two-button pick on its own.
    $controlsType = [FGOLocalPlatform.ControlSettingsView]
    $controlsStock = $stockType::Parse("[xinput]`r`nattack=0x1000`r`nskillTargetCancel=0`r`n")
    $controlsBindings = $bindingsType::Parse("[keys]`r`ntitle=F5`r`nclosePopup=Ctrl+P`r`n[pad]`r`ntitle=Back`r`naimeYes=B`r`n", $controlsStock, [Action[string]]{ param($line) throw "bindings: $line" })
    $controls = @(
        $controlsType::Refusal($chord::ParseKeys('F5'), 'Attack', $controlsBindings),
        $controlsType::Refusal($chord::ParseKeys('P'), 'Noble Phantasm', $controlsBindings),
        $controlsType::Refusal($chord::ParseKeys('MiddleMouse'), 'Insert Coin', $controlsBindings),
        $controlsType::Refusal($chord::ParsePad('RB'), 'Dash', $controlsBindings),
        $controlsType::Refusal($chord::ParsePad('Back'), 'Attack', $controlsBindings),
        $controlsType::Refusal($chord::FromPadMask(0x3000), 'Attack', $controlsBindings),
        $controlsType::Refusal($chord::ParsePad('Back'), 'Attack', $controlsBindings, $true),
        $controlsType::Refusal($chord::ParseKeys('G'), 'Attack', $controlsBindings),
        $controlsType::Refusal($chord::FromPadMask(0xC000), 'Noble Phantasm', $controlsBindings)
    )
    $expectedControls = @(
        'F5 taps Title screen: touch to start in Bindings, so it cannot be Attack.',
        'P taps Popup: Close in Bindings, so it cannot be Noble Phantasm.',
        $null,
        $null,
        'Back taps Title screen: touch to start in Bindings, so it cannot be Attack.',
        'B taps Card check: Yes in Bindings, so it cannot be Attack.',
        'Create taps Title screen: touch to start in Bindings, so it cannot be Attack.',
        $null,
        $null
    )
    $mismatch = @(0..($controls.Count - 1) | Where-Object { $controls[$_] -cne $expectedControls[$_] })
    Test-Result 'controls refused' ($mismatch.Count -eq 0) "got: $($controls -join ' | ')"
    # Shift, Ctrl and Alt in Bindings match either side, as the stock controls read them when the file is loaded.
    $sidesStock = $stockType::Parse("[keyboard]`r`ndash=0x51`r`n")
    $sidesBindings = $bindingsType::Parse("[keys]`r`ngrailWar=Shift+G`r`nstart=LCtrl+H`r`n", $sidesStock, [Action[string]]{ param($line) throw "bindings: $line" })
    $sides = @(
        $controlsType::Refusal($chord::ParseKeys('LShift'), 'Dash', $sidesBindings),
        $controlsType::Refusal($chord::ParseKeys('Ctrl'), 'Dash', $sidesBindings),
        $controlsType::Refusal($chord::ParseKeys('RCtrl'), 'Dash', $sidesBindings)
    )
    $expectedSides = @(
        'Left Shift taps Terminal: Grail War in Bindings, so it cannot be Dash.',
        'Ctrl taps Grail War: Start in Bindings, so it cannot be Dash.',
        $null
    )
    $mismatch = @(0..($sides.Count - 1) | Where-Object { $sides[$_] -cne $expectedSides[$_] })
    Test-Result 'controls refused, either side of a modifier' ($mismatch.Count -eq 0) "got: $($sides -join ' | ')"

    # Tap with on the Cursor card: [touch] clickKey, written only away from the left button, seen by the stock
    # controls, and refused when Controls or Bindings already use the button.
    $cursorType = [FGOLocalPlatform.CursorSettingsView]
    Test-Result 'tap buttons' (($cursorType::TapButtons -join ',') -ceq '1,2,4,5,6') ($cursorType::TapButtons -join ',')
    $mouseBindings = $bindingsType::Parse("[keys]`r`ntitle=0x06`r`n", $plainStock, [Action[string]]{ param($line) throw "bindings: $line" })
    $taps = @(
        $cursorType::TapRefusal(2, $plainStock, $mouseBindings),
        $cursorType::TapRefusal(6, $plainStock, $mouseBindings),
        $cursorType::TapRefusal(4, $plainStock, $mouseBindings),
        $cursorType::TapRefusal(1, $plainStock, $mouseBindings)
    )
    $expectedTaps = @(
        'Right Mouse Button is Attack in Controls, so it cannot be the touch button.',
        'Mouse Button 5 taps Title screen: touch to start in Bindings, so it cannot be the touch button.',
        $null,
        $null
    )
    $mismatch = @(0..($taps.Count - 1) | Where-Object { $taps[$_] -cne $expectedTaps[$_] })
    Test-Result 'tap refused' ($mismatch.Count -eq 0) "got: $($taps -join ' | ')"
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    $tapIni = Join-Path $work 'segatools-tap.ini'
    $tapBefore = "[touch]`r`ncursorStyle=1`r`n[keyboard]`r`nattack=0x20`r`n"
    [IO.File]::WriteAllText($tapIni, $tapBefore)
    $cursorType::WriteTapButton($tapIni, 1)
    $untouched = [IO.File]::ReadAllText($tapIni) -ceq $tapBefore
    $cursorType::WriteTapButton($tapIni, 6)
    $written = $stockType::Read($tapIni)
    $writtenLines = [IO.File]::ReadAllLines($tapIni)
    $cursorType::WriteTapButton($tapIni, 1)
    Test-Result 'tap written' ($untouched -and ($writtenLines -join '|') -ceq '[touch]|cursorStyle=1|clickKey=6|[keyboard]|attack=0x20' -and $written.TouchKey -eq 6 -and $written.KeyAction(6) -ceq 'Touch' -and [IO.File]::ReadAllText($tapIni) -notmatch 'clickKey' -and $stockType::Read($tapIni).TouchKey -eq 1) "left leaves the file as it was: $untouched; then $($writtenLines -join '|')"

    # The Canvas: client point and back through the stock hook's gate, within 1 px.
    foreach ($size in @(@(1280, 720), @(1920, 1080), @(2560, 1440), @(3440, 1440))) {
        $sw, $sh = $size
        $canvas = [FGOLocalPlatform.TouchGate]::new($sw, $sh, 1920, 1080, $true)
        $worst = 0; $outside = 0; $inside = 0; $wrong = @()
        for ($ty = 0; $ty -lt 1080; $ty += 27) {
            for ($tx = 0; $tx -lt 1920; $tx += 31) {
                $client = $canvas.ToClient($tx, $ty, $sw, $sh)
                $v = Get-Viewport $sw $sh $sh $true
                $x0 = [Math]::Round($tx * $sw / 1920)
                $reachable = $x0 -ge $v.Left + 1 -and $x0 -lt $v.Left + $v.Width - 1
                if ($null -eq $client) { if ($reachable) { $wrong += "$tx,$ty" } else { $outside++ }; continue }
                $inside++
                $back = ConvertTo-CanvasPoint $client.X $client.Y $sw $sh $sh $true
                $worst = [Math]::Max($worst, [Math]::Max([Math]::Abs($back[0] - $tx), [Math]::Abs($back[1] - $ty)))
            }
        }
        $wide = $sw * 9 -gt $sh * 16
        Test-Result "canvas ${sw}x$sh" ($worst -le 1 -and $wrong.Count -eq 0 -and $inside -gt 0 -and (($outside -gt 0) -eq $wide)) "$inside points round trip within $worst px, $outside outside the 16:9 area; wrongly refused: $($wrong -join ' ')"
    }
    $canvas = [FGOLocalPlatform.TouchGate]::new(1920, 1080, 1920, 1080, $true)
    $clipped = $canvas.ToClient(960, 540, 1920, 1040)
    $back = ConvertTo-CanvasPoint $clipped.X $clipped.Y 1920 1080 1040 $true
    Test-Result 'canvas, clipped client' ($clipped.X -eq 960 -and $clipped.Y -eq 500 -and $back[0] -eq 960 -and $back[1] -eq 540) "client $($clipped.X),$($clipped.Y) -> canvas $($back -join ',')"
    $wide = [FGOLocalPlatform.TouchGate]::new(3440, 1440, 1920, 1080, $true)
    Test-Result 'canvas, pillarbox' ($null -eq $wide.ToClient(100, 540, 3440, 1440) -and $wide.ToClient(960, 540, 3440, 1440).X -eq 1720) "canvas 100,540 has no client point; 960,540 -> $($wide.ToClient(960, 540, 3440, 1440).X)"
    $read = [FGOLocalPlatform.TouchGate]::Parse("[gfx]`r`nwidth=2560`r`nheight=1440`r`n[touch]`r`ninputWidth=1920`r`ninputHeight=1080`r`n")
    Test-Result 'canvas from the runtime ini' ($read.SurfaceWidth -eq 2560 -and $read.SurfaceHeight -eq 1440 -and $read.PreserveAspect) "$($read.SurfaceWidth)x$($read.SurfaceHeight) preserve $($read.PreserveAspect)"

    # The chord rule.
    $down = [Collections.Generic.HashSet[int]]::new()
    $isDown = [Func[int, bool]]{ param($code) $down.Contains($code) }
    $matcher = [FGOLocalPlatform.ChordMatcher]::new([FGOLocalPlatform.Chord[]]@($chord::ParseKeys('P'), $chord::ParseKeys('Ctrl+P'), $chord::ParseKeys('F5')))
    function Step([int[]]$Press, [int[]]$Release) {
        foreach ($k in $Press) { [void]$down.Add($k) }
        foreach ($k in $Release) { [void]$down.Remove($k) }
        return (@($matcher.Poll($isDown)) -join ',')
    }
    $steps = @(
        (Step -Press 0x11), (Step -Press 0x50), (Step), (Step -Release 0x50, 0x11),
        (Step -Press 0x50), (Step -Release 0x50),
        (Step -Press 0x50), (Step -Press 0x11), (Step -Release 0x50, 0x11),
        (Step -Press 0x74), (Step), (Step -Release 0x74)
    )
    Test-Result 'chord rule' (($steps -join '|') -ceq '|1||||0||1||2||') "fired per step: $($steps -join '|') (0 P, 1 Ctrl+P, 2 F5)"
    [void]$down.Add(0x74); [void]$down.Add(0x50)
    $matcher.Reset($isDown)
    $held = (Step), (Step -Release 0x74, 0x50)
    Test-Result 'nothing fires after a reset' (($held -join '|') -ceq '|') "fired per step: $($held -join '|')"

    # The layer against a hidden window of this process.
    # CS1701: the launcher assembly names .NET 6, which this pwsh's newer runtime stands in for.
    Add-Type -ReferencedAssemblies $assembly, 'System.Collections', 'System.Runtime.InteropServices', 'System.Threading', 'System.Threading.Thread' -IgnoreWarnings -WarningAction SilentlyContinue -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using FGOLocalPlatform;

public sealed class FakeInput : IInputDevice
{
    private readonly object gate = new object();
    private readonly HashSet<int> keys = new HashSet<int>();
    private IntPtr foreground;
    private PadState pad;
    private bool padConnected;
    public readonly List<string> Lines = new List<string>();
    public Action<string> Log { get { return line => { lock (gate) { Lines.Add(line); } }; } }
    public void Press(int vk) { lock (gate) { keys.Add(vk); } }
    public void Release(int vk) { lock (gate) { keys.Remove(vk); } }
    public void SetForeground(IntPtr window) { lock (gate) { foreground = window; } }
    public void SetPad(uint buttons) { lock (gate) { pad = new PadState(buttons); padConnected = true; } }
    public IntPtr ForegroundWindow() { lock (gate) { return foreground; } }
    public bool KeyDown(int virtualKey) { lock (gate) { return keys.Contains(virtualKey); } }
    public bool ReadPad(out PadState state) { lock (gate) { state = pad; return padConnected; } }
}

public sealed class FakeGameWindow : IDisposable
{
    private delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass { public uint Size, Style; public WindowProcedure Procedure; public int ClassExtra, WindowExtra; public IntPtr Instance, Icon, Cursor, Background; public string Menu, Name; public IntPtr SmallIcon; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Message { public IntPtr Window; public uint Id; public IntPtr WParam, LParam; public uint Time; public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern ushort RegisterClassExW(ref WindowClass windowClass);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWindowExW(uint exStyle, string className, string title, uint style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr DefWindowProcW(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool PeekMessageW(out Message message, IntPtr window, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessageW(ref Message message);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandleW(string name);
    private static readonly WindowProcedure procedure = Procedure;
    private static readonly List<string> messages = new List<string>();
    private static bool registered;
    public readonly IntPtr Handle;

    public FakeGameWindow(int x, int y, int width, int height)
    {
        if (!registered)
        {
            WindowClass windowClass = new WindowClass { Size = (uint)Marshal.SizeOf(typeof(WindowClass)), Procedure = procedure, Instance = GetModuleHandleW(null), Name = "FGOACInputCheck" };
            if (RegisterClassExW(ref windowClass) == 0) throw new InvalidOperationException("RegisterClassEx failed");
            registered = true;
        }
        Handle = CreateWindowExW(0, "FGOACInputCheck", "", 0x80000000u, x, y, width, height, IntPtr.Zero, IntPtr.Zero, GetModuleHandleW(null), IntPtr.Zero);
        if (Handle == IntPtr.Zero) throw new InvalidOperationException("CreateWindowEx failed");
    }

    public void ClientOrigin(out int x, out int y) { Point point = new Point(); ClientToScreen(Handle, ref point); x = point.X; y = point.Y; }

    public string[] Take() { lock (messages) { string[] taken = messages.ToArray(); messages.Clear(); return taken; } }

    public void Pump(int milliseconds)
    {
        DateTime end = DateTime.UtcNow.AddMilliseconds(milliseconds);
        do
        {
            Message message;
            while (PeekMessageW(out message, IntPtr.Zero, 0, 0, 1)) DispatchMessageW(ref message);
            Thread.Sleep(5);
        }
        while (DateTime.UtcNow < end);
    }

    private static IntPtr Procedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        string name = message == 0x200 ? "move" : message == 0x201 ? "down" : message == 0x202 ? "up" : message == 0x204 ? "rdown" : message == 0x205 ? "rup" : message == 0x20B ? "xdown" : message == 0x20C ? "xup" : null;
        if (name != null)
        {
            long value = lParam.ToInt64();
            lock (messages) { messages.Add(string.Format("{0} {1},{2} {3}", name, (short)(value & 0xFFFF), (short)((value >> 16) & 0xFFFF), wParam.ToInt64())); }
            return IntPtr.Zero;
        }
        return DefWindowProcW(window, message, wParam, lParam);
    }

    public void Dispose() { DestroyWindow(Handle); }
}
'@
    $window = [FakeGameWindow]::new(100, 100, 1280, 720)
    $input = [FakeInput]::new()
    $refused.Clear()
    $keyboard = $bindingsType::Parse("[keys]`r`ntitle=F5`r`nclosePopup=Ctrl+P`r`naimeYes=P`r`n", $stock, [Action[string]]{ param($line) $refused.Add($line) })
    $canvas = [FGOLocalPlatform.TouchGate]::new(1280, 720, 1920, 1080, $true)
    $layer = [FGOLocalPlatform.InputLayer]::Start($keyboard, $canvas, 1, $PID, $input, $input.Log)
    $layers += $layer

    $input.SetForeground($window.Handle)
    $window.Pump(100); [void]$window.Take()
    $input.Press(0x74); $window.Pump(60); $input.Release(0x74); $window.Pump(300)
    $got = $window.Take()
    Test-Result 'tap' (($got -join ' | ') -ceq 'move 640,360 0 | down 640,360 1 | up 640,360 0') "F5 (Title screen, canvas 960,540): $($got -join ' | ')"
    $input.Press(0x11); $window.Pump(40); $input.Press(0x50); $window.Pump(60); $input.Release(0x50); $input.Release(0x11); $window.Pump(300)
    $got = $window.Take()
    Test-Result 'tap, chord' (($got -join ' | ') -ceq 'move 640,567 0 | down 640,567 1 | up 640,567 0') "Ctrl+P (Popup: Close, canvas 960,850) and P bound alone: $($got -join ' | ')"
    $input.SetForeground([IntPtr]::Zero)
    $input.Press(0x74); $window.Pump(60); $input.Release(0x74); $window.Pump(300)
    $got = $window.Take()
    Test-Result 'nothing in the background' ($got.Count -eq 0) "F5 with another window in front: $($got -join ' | ')"
    $input.Press(0x74); $window.Pump(60); $input.SetForeground($window.Handle); $window.Pump(100); $input.Release(0x74); $window.Pump(300)
    $got = $window.Take()
    Test-Result 'held key on return' ($got.Count -eq 0) "F5 held while the window came to the front: $($got -join ' | ')"
    $layer.Dispose()
    $input.Press(0x74); $window.Pump(60); $input.Release(0x74); $window.Pump(200)
    Test-Result 'stopped' ($window.Take().Count -eq 0) 'nothing after Dispose'
    # A window in front that is not the game's: the layer runs for a process id that is not this one.
    $other = [FGOLocalPlatform.InputLayer]::Start($keyboard, $canvas, 1, [int]::MaxValue - 1, $input, $input.Log)
    $layers += $other
    $window.Pump(100)
    $input.Press(0x74); $window.Pump(60); $input.Release(0x74); $window.Pump(300)
    $other.Dispose()
    $got = $window.Take()
    Test-Result 'another process in front' ($got.Count -eq 0) "F5 with a window of this process in front and the layer on another: $($got -join ' | ')"

    # A pad binding taps its button.
    $padBindings = $bindingsType::Parse("[profile]`r`nname=pad`r`n[pad]`r`ntitle=Back`r`n", $stock, [Action[string]]{ param($line) $refused.Add($line) })
    $input.SetPad(0)
    $layer = [FGOLocalPlatform.InputLayer]::Start($padBindings, $canvas, 1, $PID, $input, $input.Log)
    $layers += $layer
    $window.Pump(100); [void]$window.Take()
    $input.SetPad(0x20); $window.Pump(60); $input.SetPad(0); $window.Pump(300)
    $got = $window.Take()
    Test-Result 'pad tap' (($got -join ' | ') -ceq 'move 640,360 0 | down 640,360 1 | up 640,360 0') "Back (Title screen): $($got -join ' | ')"
    $layer.Dispose()

    # Another touch button: taps use its messages; a key taps nothing.
    $layer = [FGOLocalPlatform.InputLayer]::Start($keyboard, $canvas, 2, $PID, $input, $input.Log)
    $layers += $layer
    $window.Pump(100); [void]$window.Take()
    $input.Press(0x74); $window.Pump(60); $input.Release(0x74); $window.Pump(300)
    $layer.Dispose()
    $got = $window.Take()
    Test-Result 'tap, right button' (($got -join ' | ') -ceq 'move 640,360 0 | rdown 640,360 2 | rup 640,360 0') "F5 with clickKey=2: $($got -join ' | ')"
    $layer = [FGOLocalPlatform.InputLayer]::Start($padBindings, $canvas, 6, $PID, $input, $input.Log)
    $layers += $layer
    $window.Pump(100); [void]$window.Take()
    $input.SetPad(0x20); $window.Pump(60); $input.SetPad(0); $window.Pump(300)
    $layer.Dispose()
    $got = $window.Take()
    Test-Result 'pad tap, mouse button 5' (($got -join ' | ') -ceq 'move 640,360 0 | xdown 640,360 131136 | xup 640,360 131072') "Back with clickKey=6: $($got -join ' | ')"
    $layerLines = @($input.Lines)
    $layer = [FGOLocalPlatform.InputLayer]::Start($keyboard, $canvas, 0x20, $PID, $input, $input.Log)
    $layers += $layer
    $window.Pump(100)
    $input.Press(0x74); $window.Pump(60); $input.Release(0x74); $window.Pump(300)
    $layer.Dispose()
    $got = $window.Take()
    $told = @($input.Lines | Select-Object -Skip $layerLines.Count)
    Test-Result 'a key taps nothing' ($got.Count -eq 0 -and ($told -join ' | ') -ceq 'Space is the touch button, and a binding can only tap with a mouse button, so nothing is tapped this session.') "posted: $($got -join ' | '); logged: $($told -join ' | ')"
    Test-Result 'layer lines' ($layerLines.Count -eq 0 -and $refused.Count -eq 0) "logged: $(@($layerLines) + @($refused) -join ' | ')"
}
catch {
    Test-Result 'run' $false ($_ | Out-String)
}
finally {
    foreach ($layer in $layers) { $layer.Dispose() }
    if ($null -ne $window) { $window.Dispose() }
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}

$sink = Get-DialogSink
Test-Result 'no window shown' ($sink.Shown -eq 0) $sink.Detail
if ($script:failed -gt 0) { Write-Host "$script:failed check(s) failed."; exit 1 }
Write-Host 'Every check passed.'
exit 0
