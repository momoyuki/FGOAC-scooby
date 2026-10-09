<#
Checks the graphics settings of Cloud23333's 1.10 update on the Display card without the game. Every file is written
under a throwaway folder in %TEMP%, deleted when the check ends.

  pwsh -File tools\Test-Graphics.ps1 [-CloudApp <his 1.10 package>\App]

Through the launcher assembly (src\bin\Release, so run build.cmd first): fog, reflections, ultra-low quality, draw
distance and the treasure chest size and opacity read with his defaults and ranges from the graphics object of
fgo-launcher.json and are written back under his key names with every other key kept; the draw distance also goes
into [effects] view_distance of graphics-effects.ini key by key. Then the block of his own FGO_Launcher.ps1 that turns
those keys into the game's environment at launch runs on the written file and gives the values picked. High frame
rate timing, VSync and the AMD anti-tearing mode go the same way: his keys highFpsV2, vsync and amdPresentSync, which
his script passes as FGO_HIGH_FPS_V2, FGO_VSYNC and FGO_AMD_PRESENT_SYNC; without high frame rate timing it holds
the game at 60 FPS.

Exit codes: 0 every check passed, 1 a check failed, 2 a prerequisite is missing.
#>
[CmdletBinding()]
param(
    [string]$CloudApp = "$env:FGOAC_CLOUD_UPDATE\payload\payload\App"
)
$ErrorActionPreference = 'Stop'

$repository = Split-Path -Parent $PSScriptRoot
$assembly = Join-Path $repository 'src\bin\Release\net6.0-windows\FGOLocalPlatform.dll'
if ($PSVersionTable.PSVersion.Major -lt 7) { Write-Host 'Run this with pwsh 7: the launcher assembly targets .NET 6.'; exit 2 }
foreach ($path in @($assembly, (Join-Path $CloudApp 'FGO_Launcher.ps1'))) { if (!(Test-Path -LiteralPath $path)) { Write-Host "Missing $path."; exit 2 } }

$script:failed = 0
function Test-Result([string]$Name, [bool]$Passed, [string]$Detail) {
    Write-Host ("{0} {1}: {2}" -f $(if ($Passed) { 'PASS' } else { 'FAIL' }), $Name, $Detail)
    if (!$Passed) { $script:failed++ }
}

$work = Join-Path ([IO.Path]::GetTempPath()) ('fgoac-graphics-' + [guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $work | Out-Null
    $launcher = [Reflection.Assembly]::LoadFrom($assembly)
    # Every message box, dialog and window of the launcher goes to the sink, never to the screen.
    . (Join-Path $PSScriptRoot 'DialogSink.ps1'); Enable-DialogSink $assembly
    $type = $launcher.GetType('FGOLocalPlatform.SceneGraphics')
    if (!$type) { Test-Result 'SceneGraphics' $false 'FGOLocalPlatform.SceneGraphics is missing'; throw 'stop' }
    $flags = [Reflection.BindingFlags]'NonPublic, Public, Static'
    function Read-Graphics([string]$Json) { $node = [Text.Json.Nodes.JsonNode]::Parse($Json); return $type.GetMethod('Read', $flags).Invoke($null, [object[]]@(, $node)) }

    $defaults = Read-Graphics '{}'
    Test-Result 'his defaults' ($defaults.VolumeFog -and $defaults.Reflections -and !$defaults.UltraLow -and $defaults.ViewDistance -eq 100 -and $defaults.TreasureScale -eq 100 -and $defaults.TreasureOpacity -eq 100) "$defaults"
    $odd = Read-Graphics '{"volumeFog": false, "reflections": "no", "ultraLowQuality": true, "viewDistance": 300, "treasureScale": 250, "treasureOpacity": 42.5}'
    Test-Result 'his ranges' (!$odd.VolumeFog -and $odd.Reflections -and $odd.UltraLow -and $odd.ViewDistance -eq 100 -and $odd.TreasureScale -eq 200 -and $odd.TreasureOpacity -eq 42.5) "$odd"

    $graphics = [Text.Json.Nodes.JsonNode]::Parse('{"smaa": 2, "volumeFog": true, "photo": {"keys": [1]}}')
    $picked = $type.GetConstructors()[0].Invoke(@($false, $false, $true, 400, 150.5, 30.0))
    [void]$picked.GetType().GetMethod('Write').Invoke($picked, [object[]]@(, $graphics))
    $json = $graphics.ToJsonString()
    Test-Result 'written under his key names, the rest kept' ($json -ceq '{"smaa":2,"volumeFog":false,"photo":{"keys":[1]},"reflections":false,"ultraLowQuality":true,"viewDistance":400,"treasureScale":150.5,"treasureOpacity":30}') $json

    $ini = Join-Path $work 'graphics-effects.ini'
    [IO.File]::WriteAllText($ini, "[effects]`r`nhide_dark_mask=1`r`nopacity=80`r`n[photo]`r`nbackdrop=2`r`n")
    [void]$picked.GetType().GetMethod('WriteDrawDistance').Invoke($picked, @([string]$work))
    $after = [IO.File]::ReadAllText($ini)
    Test-Result 'view_distance key by key' ($after -match '(?m)^view_distance=400\r?$' -and $after -match '(?m)^hide_dark_mask=1\r?$' -and $after -match '(?m)^opacity=80\r?$' -and $after -match '(?m)^backdrop=2\r?$' -and $after.IndexOf('view_distance') -lt $after.IndexOf('[photo]')) ($after -replace "`r`n", '\n')

    # His launch script's own block that turns the graphics keys into the game's environment.
    $script = [IO.File]::ReadAllLines((Join-Path $CloudApp 'FGO_Launcher.ps1'))
    $start = [Array]::FindIndex($script, [Predicate[string]] { param($l) $l -match '^\s*foreach \(\$damageSetting in @\(' })
    $end = [Array]::FindIndex($script, [Predicate[string]] { param($l) $l -match 'FGO_HIDE_UI' })
    if ($start -lt 0 -or $end -lt $start) { Test-Result 'his launch block' $false "block not found ($start, $end)"; throw 'stop' }
    $block = [scriptblock]::Create(($script[$start..($end - 1)] -join "`n"))
    $settings = Join-Path $work 'fgo-launcher.json'
    [IO.File]::WriteAllText($settings, "{`"graphics`": $json}")
    $environment = & {
        $graphics = (Get-Content -LiteralPath $settings -Raw | ConvertFrom-Json).graphics
        $anisotropy = 16
        $injectStartInfo = [pscustomobject]@{ EnvironmentVariables = @{} }
        & $block
        $injectStartInfo.EnvironmentVariables
    }
    $seen = @('FGO_VOLUME_FOG', 'FGO_REFLECTIONS', 'FGO_ULTRA_LOW', 'FGO_TREASURE_SCALE', 'FGO_TREASURE_OPACITY', 'FGO_SHADOW_RESOLUTION') | ForEach-Object { "$_=$($environment[$_])" }
    Test-Result 'his launch script reads the picks' (($seen -join ',') -ceq 'FGO_VOLUME_FOG=0,FGO_REFLECTIONS=0,FGO_ULTRA_LOW=1,FGO_TREASURE_SCALE=150.5,FGO_TREASURE_OPACITY=30,FGO_SHADOW_RESOLUTION=1024') ($seen -join ', ')

    $offered = $type.GetMethod('Offered', $flags)
    $app = Join-Path $work 'install\App'
    New-Item -ItemType Directory -Path $app -Force | Out-Null
    $bytes = [byte[]]::new(0x400); $bytes[0] = 0x4D; $bytes[1] = 0x5A
    [BitConverter]::GetBytes([int32]0x130).CopyTo($bytes, 0x3C); [BitConverter]::GetBytes([uint32]0x00004550).CopyTo($bytes, 0x130)
    [BitConverter]::GetBytes([uint32]0x643F4ECD).CopyTo($bytes, 0x138); [BitConverter]::GetBytes([uint32]0x2034000).CopyTo($bytes, 0x148 + 56)
    [IO.File]::WriteAllBytes((Join-Path $app 'ago.exe'), $bytes)
    $on1100 = $offered.Invoke($null, @([string](Join-Path $work 'install')))
    [IO.File]::WriteAllText((Join-Path $work 'install\update-1.10.json'), '{}')
    $onHis = $offered.Invoke($null, @([string](Join-Path $work 'install')))
    Test-Result 'shown on his build only' (!$on1100 -and $onHis) "11.00: $on1100, his build: $onHis"

    # His frame rate and sync switches: high frame rate timing, VSync and the AMD anti-tearing mode, passed to his hook
    # by his launch script, which holds the game at 60 FPS unless high frame rate timing is on.
    $sync = $launcher.GetType('FGOLocalPlatform.FrameSync')
    if (!$sync) { Test-Result 'FrameSync' $false 'FGOLocalPlatform.FrameSync is missing'; throw 'stop' }
    function Read-Sync([string]$Json) { $node = [Text.Json.Nodes.JsonNode]::Parse($Json); return $sync.GetMethod('Read', $flags).Invoke($null, [object[]]@(, $node)) }
    $syncDefaults = Read-Sync '{}'
    $syncOdd = Read-Sync '{"highFpsV2": 1, "vsync": "yes", "amdPresentSync": true}'
    Test-Result 'sync switches off unless true' (!$syncDefaults.HighFpsV2 -and !$syncDefaults.VSync -and !$syncDefaults.AmdPresentSync -and !$syncOdd.HighFpsV2 -and !$syncOdd.VSync -and $syncOdd.AmdPresentSync) "$syncDefaults; $syncOdd"
    $syncGraphics = [Text.Json.Nodes.JsonNode]::Parse('{"smaa": 1, "vsync": false}')
    $syncPicked = $sync.GetConstructors()[0].Invoke(@($true, $true, $true))
    [void]$sync.GetMethod('Write').Invoke($syncPicked, [object[]]@(, $syncGraphics))
    $syncJson = $syncGraphics.ToJsonString()
    Test-Result 'sync written under his key names, the rest kept' ($syncJson -ceq '{"smaa":1,"vsync":true,"highFpsV2":true,"amdPresentSync":true}') $syncJson
    $rateStart = [Array]::FindIndex($script, [Predicate[string]] { param($l) $l -match '^\$configuredTargetFps = ' })
    $rateEnd = [Array]::FindIndex($script, [Predicate[string]] { param($l) $l -match '^\$useAudioHook = ' })
    $syncLines = @($script | Where-Object { $_ -match '^\$env:FGO_(HIGH_FPS_V2|VSYNC|AMD_PRESENT_SYNC) = ' })
    if ($rateStart -lt 0 -or $rateEnd -lt $rateStart -or $syncLines.Count -ne 3) { Test-Result 'his frame rate lines' $false "block ($rateStart, $rateEnd), $($syncLines.Count) sync lines"; throw 'stop' }
    $syncBlock = [scriptblock]::Create((@($script[$rateStart..($rateEnd - 1)]) + $syncLines -join "`n"))
    $cases = @(
        @('{"targetFps":240,"graphics":{"highFpsV2":true,"vsync":true,"amdPresentSync":true}}', '240,1,1,1'),
        @('{"targetFps":240,"graphics":{"highFpsV2":false,"vsync":false,"amdPresentSync":false}}', '60,0,0,0'),
        @('{"targetFps":144,"graphics":{"highFpsV2":true,"vsync":false,"amdPresentSync":true}}', '144,1,0,1'))
    foreach ($case in $cases) {
        $passed = & {
            $launcherConfig = $case[0] | ConvertFrom-Json
            $TargetFps = 0
            . $syncBlock 3>$null
            "$effectiveTargetFps,$env:FGO_HIGH_FPS_V2,$env:FGO_VSYNC,$env:FGO_AMD_PRESENT_SYNC"
        }
        Remove-Item Env:FGO_HIGH_FPS_V2, Env:FGO_VSYNC, Env:FGO_AMD_PRESENT_SYNC -ErrorAction SilentlyContinue
        Test-Result "his launch script reads the frame rate and sync switches ($($case[1]))" ($passed -ceq $case[1]) $passed
    }
}
catch {
    if ("$_" -ne 'stop') { Test-Result 'run' $false ($_ | Out-String) }
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

$sink = Get-DialogSink
Test-Result 'no window shown' ($sink.Shown -eq 0) $sink.Detail
if ($script:failed -gt 0) { Write-Host "$($script:failed) check(s) failed."; exit 1 }
Write-Host 'Every check passed.'
exit 0
