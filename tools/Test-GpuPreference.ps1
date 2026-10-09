<#
Checks the Display card's GPU preference without starting the launcher or the game, and without touching the real
HKCU\Software\Microsoft\DirectX\UserGpuPreferences: every write and read below goes through a throwaway registry
key under HKCU\Software\FGOAC-scooby-test, deleted when the check ends, and that parent too when nothing else is under it.

  pwsh -File tools\Test-GpuPreference.ps1

Through the launcher assembly (src\bin\Release, so run build.cmd first): the labels, plain and with the adapter's
name; power saving and high performance write GpuPreference=1; and GpuPreference=2; and read back; Let Windows
decide removes the value; other pairs in the value (Windows' own per-app switches) stay through every write; a
value without a GpuPreference pair of 1 or 2 reads as Let Windows decide; the first start writes High performance
once while preferHighPerformanceGpu is not false and no pick is saved, then sets it false, and never replaces a pick,
also on a fresh install without a Settings file, which it then creates;
the adapter DXGI 1.6 ranks first for each preference is read on this PC (a name, or none without DXGI 1.6) and none for
Let Windows decide. On Cloud23333's build: only his card pick (GpuCard.cs, Test-GpuCard.ps1) writes his GPU routing
keys, a pick his launcher saved is read and kept by the first start, and his own FGO_GpuRouting.ps1 (from his 1.10
package) with no routing key takes the system path instead of the card match that stops a start with exit 16.

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

$testParent = 'Software\FGOAC-scooby-test'
$testRoot = $testParent + '\' + [guid]::NewGuid().ToString('N')
$exePath = 'D:\Games\FGOA\App\ago.exe'
try {
    [void][Reflection.Assembly]::LoadFrom($assembly)
    $preference = [FGOLocalPlatform.GpuPreference]

    $labels = @($preference::Label(0, 'NVIDIA GeForce RTX 4090'), $preference::Label(1, $null), $preference::Label(2, ''), $preference::Label(1, 'AMD Radeon(TM) Graphics'), $preference::Label(2, 'NVIDIA GeForce RTX 4090'))
    Test-Result 'labels' (($labels -join '|') -ceq 'Let Windows decide|Power saving|High performance|Power saving (AMD Radeon(TM) Graphics)|High performance (NVIDIA GeForce RTX 4090)') ($labels -join ' | ')

    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($testRoot, $true)

    $preference::Write($key, $exePath, 2)
    Test-Result 'write high performance' ($key.GetValue($exePath) -ceq 'GpuPreference=2;' -and $preference::Read($key, $exePath) -eq 2) "value: $($key.GetValue($exePath))"

    $preference::Write($key, $exePath, 1)
    Test-Result 'write power saving' ($key.GetValue($exePath) -ceq 'GpuPreference=1;' -and $preference::Read($key, $exePath) -eq 1) "value: $($key.GetValue($exePath))"

    $preference::Write($key, $exePath, 0)
    Test-Result 'let windows decide removes the value' ($null -eq $key.GetValue($exePath) -and $preference::Read($key, $exePath) -eq 0) "value: $($key.GetValue($exePath))"

    $preference::Write($key, $exePath, 0)
    Test-Result 'removing a value that is not there is not an error' ($null -eq $key.GetValue($exePath)) 'no exception on the second remove'

    $key.SetValue($exePath, 'VRROptimizeEnable=0;SwapEffectUpgradeEnable=1;')
    $preference::Write($key, $exePath, 2)
    $high = $key.GetValue($exePath)
    $preference::Write($key, $exePath, 1)
    $low = $key.GetValue($exePath)
    $preference::Write($key, $exePath, 0)
    $decide = $key.GetValue($exePath)
    Test-Result 'other pairs kept' ($high -ceq 'VRROptimizeEnable=0;SwapEffectUpgradeEnable=1;GpuPreference=2;' -and $low -ceq 'VRROptimizeEnable=0;SwapEffectUpgradeEnable=1;GpuPreference=1;' -and $decide -ceq 'VRROptimizeEnable=0;SwapEffectUpgradeEnable=1;') "$high / $low / $decide"

    $reads = foreach ($value in 'SwapEffectUpgradeEnable=1;GpuPreference=2;', 'GpuPreference=0;', 'GpuPreference=7;', 'garbage', '') { $key.SetValue($exePath, $value); $preference::Read($key, $exePath) }
    Test-Result 'saved values read' (($reads -join ',') -ceq '2,0,0,0,0') ($reads -join ',')

    $key.DeleteValue($exePath)
    $preference::Write($key, $exePath, 2)
    $preference::Write($key, ($exePath + '2'), 1)
    Test-Result 'two exe paths side by side' ($key.GetValue($exePath) -ceq 'GpuPreference=2;' -and $key.GetValue(($exePath + '2')) -ceq 'GpuPreference=1;') "$($key.GetValue($exePath)) / $($key.GetValue($exePath + '2'))"

    # The first start: High performance once while the Settings file still asks for it, and never over a pick.
    function Invoke-Once([string]$Json) {
        $settings = [Text.Json.Nodes.JsonNode]::Parse($Json)
        $changed = $preference::WriteOnce($key, $exePath, $settings)
        return "$changed $($settings.ToJsonString()) $($key.GetValue($exePath))"
    }
    $key.DeleteValue($exePath)
    $key.SetValue($exePath, 'VRROptimizeEnable=0;')
    $first = Invoke-Once '{"displayMode":"windowed","preferHighPerformanceGpu":true}'
    $second = Invoke-Once '{"preferHighPerformanceGpu":false}'
    Test-Result 'high performance once' ($first -ceq 'True {"displayMode":"windowed","preferHighPerformanceGpu":false} VRROptimizeEnable=0;GpuPreference=2;' -and $second -ceq 'False {"preferHighPerformanceGpu":false} VRROptimizeEnable=0;GpuPreference=2;') "$first / $second"
    $key.DeleteValue($exePath)
    $unset = Invoke-Once '{}'
    $preference::Write($key, $exePath, 0)
    $decide = Invoke-Once '{"preferHighPerformanceGpu":false}'
    $preference::Write($key, $exePath, 1)
    $picked = Invoke-Once '{"preferHighPerformanceGpu":true}'
    Test-Result 'picks kept' ($unset -ceq 'True {"preferHighPerformanceGpu":false} GpuPreference=2;' -and $decide -ceq 'False {"preferHighPerformanceGpu":false} ' -and $picked -ceq 'True {"preferHighPerformanceGpu":false} GpuPreference=1;') "not saved: $unset / Let Windows decide afterwards: $decide / power saving picked: $picked"
    $key.DeleteValue($exePath)

    # The Settings file on disk: a fresh install has none yet, which counts as no pick saved.
    $work = Join-Path ([IO.Path]::GetTempPath()) ('fgoac-gpu-check-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work | Out-Null
    $settingsPath = Join-Path $work 'fgo-launcher.json'
    $preference::WriteOnce($settingsPath, $exePath, $testRoot)
    $created = if (Test-Path -LiteralPath $settingsPath) { ([IO.File]::ReadAllText($settingsPath) | ConvertFrom-Json).preferHighPerformanceGpu } else { 'no file' }
    Test-Result 'fresh install' ($key.GetValue($exePath) -ceq 'GpuPreference=2;' -and $created -eq $false) "value: $($key.GetValue($exePath)); preferHighPerformanceGpu in the new Settings file: $created"
    $preference::Write($key, $exePath, 0)
    $preference::WriteOnce($settingsPath, $exePath, $testRoot)
    Test-Result 'fresh install, then a pick' ($null -eq $key.GetValue($exePath)) "Let Windows decide picked after the first start stays: value $($key.GetValue($exePath))"
    [IO.File]::WriteAllText($settingsPath, '{"displayMode":"windowed"}')
    $preference::WriteOnce($settingsPath, $exePath, $testRoot)
    $kept = [IO.File]::ReadAllText($settingsPath) | ConvertFrom-Json
    Test-Result 'settings file keys kept' ($kept.displayMode -ceq 'windowed' -and $kept.preferHighPerformanceGpu -eq $false -and $key.GetValue($exePath) -ceq 'GpuPreference=2;') "$($kept | ConvertTo-Json -Compress), value $($key.GetValue($exePath))"
    $key.DeleteValue($exePath)
    [IO.File]::WriteAllText($settingsPath, '[]')
    $preference::WriteOnce($settingsPath, $exePath, $testRoot)
    Test-Result 'settings file not an object' ([IO.File]::ReadAllText($settingsPath) -ceq '[]' -and $null -eq $key.GetValue($exePath)) "file $([IO.File]::ReadAllText($settingsPath)), value $($key.GetValue($exePath))"

    # Cloud23333's GPU routing on his build: his launch script stops with exit 16 when a card named in graphics.nativeGpuName,
    # nativeGpuLuid or nativeGpuPhysicalId is not found. Only GpuCard.cs, his card pick on a folder with his
    # gpu-runtime (Test-GpuCard.ps1), writes or removes one of those keys; the Windows preference reads them.
    $writers = @(Get-ChildItem -LiteralPath (Join-Path $repository 'src') -Recurse -Filter '*.cs' | Select-String -Pattern '\["nativeGpu\w*"\]\s*=(?!=)|Remove\("nativeGpu' | ForEach-Object { "$($_.Path | Split-Path -Leaf):$($_.LineNumber)" })
    $readers = @(Get-ChildItem -LiteralPath (Join-Path $repository 'src') -Recurse -Filter '*.cs' | Select-String -Pattern 'nativeGpu' | ForEach-Object { $_.Path | Split-Path -Leaf } | Sort-Object -Unique)
    $writerFiles = @($writers | ForEach-Object { $_.Split(':')[0] } | Sort-Object -Unique)
    Test-Result 'his routing keys written by his card pick only' (($writerFiles -join ',') -ceq 'GpuCard.cs' -and ($readers -join ',') -ceq 'GpuCard.cs,GpuPreference.cs') "writes: $($writers -join ', '); files naming them: $($readers -join ', ')"
    $flags = [Reflection.BindingFlags]'NonPublic, Public, Static'
    $hisPick = $preference.GetMethod('HisPick', $flags)
    if (!$hisPick) {
        Test-Result 'his pick read' $false 'GpuPreference.HisPick is missing'
    } else {
        $picks = foreach ($json in '{}', '{"graphics":{"smaa":1}}', '{"graphics":{"nativeGpuName":"AMD Radeon RX 6600"}}', '{"graphics":{"nativeGpuLuid":"0000000000012345"}}', '{"graphics":{"nativeGpuPhysicalId":"PCI:1:0:0","nativeGpuName":""}}', '{"graphics":{"nativeGpuName":"  "}}') {
            $value = $hisPick.Invoke($null, [object[]]@(, [Text.Json.Nodes.JsonNode]::Parse($json)))
            if ($null -eq $value) { 'none' } elseif ($value -eq '') { 'unnamed' } else { $value }
        }
        Test-Result 'his pick read' (($picks -join ',') -ceq 'none,none,AMD Radeon RX 6600,unnamed,unnamed,none') ($picks -join ',')
    }
    $his = '{"graphics":{"smaa":1,"nativeGpuLuid":"0000000000012345","nativeGpuPhysicalId":"PCI:1:0:0","nativeGpuName":"Old card"}}'
    $settings = [Text.Json.Nodes.JsonNode]::Parse($his.Replace('{"graphics"', '{"preferHighPerformanceGpu":true,"graphics"'))
    [void]$preference::WriteOnce($key, $exePath, $settings)
    $graphics = $settings['graphics'].ToJsonString()
    Test-Result 'his pick left alone by the first start' ($graphics -ceq '{"smaa":1,"nativeGpuLuid":"0000000000012345","nativeGpuPhysicalId":"PCI:1:0:0","nativeGpuName":"Old card"}' -and $key.GetValue($exePath) -ceq 'GpuPreference=2;') "graphics $graphics, value $($key.GetValue($exePath))"
    $key.DeleteValue($exePath)

    # His routing itself, from his 1.10 package: with no routing key, which is all this launcher leaves behind, it takes
    # the system OpenGL path and never reaches the card match that stops a start.
    $routing = "$env:FGOAC_CLOUD_UPDATE\payload\payload\App\FGO_GpuRouting.ps1"
    if (!(Test-Path -LiteralPath $routing)) {
        Test-Result 'his routing with no key' $false "Missing $routing."
    } else {
        $gameRoot = Join-Path $work 'App'
        New-Item -ItemType Directory -Path (Join-Path $gameRoot 'gpu-runtime') -Force | Out-Null
        $state = & {
            . $routing
            try { Enter-FgoGpuRouting -GameRoot $gameRoot -RuntimeDirectory (Join-Path $gameRoot 'gpu-runtime') -Luid '' -Name '' -PhysicalId '' } catch { "threw: $($_.Exception.Message)" }
        }
        $stale = & {
            . $routing
            try { [void](Resolve-FgoGpuSelection -Rows @(, @('0000000000012345', '10DE', 'NVIDIA GeForce RTX 4090', '0:1:0:0')) -Luid '' -Name 'Old card' -PhysicalId ''); 'matched' } catch { 'stopped' }
        }
        Test-Result 'his routing with no key' ($state -isnot [string] -and $state.UseSystem -eq $true -and $stale -eq 'stopped') "state: $(if ($state -is [string]) { $state } else { "UseSystem $($state.UseSystem)" }); a name that matches no card: $stale"
    }

    $power = $preference::AdapterFor(1)
    $performance = $preference::AdapterFor(2)
    Test-Result 'adapters on this PC' ($null -eq $preference::AdapterFor(0) -and ($null -eq $power -or $power.Length -gt 0) -and ($null -eq $performance -or $performance.Length -gt 0)) "power saving: $power; high performance: $performance"
}
catch {
    Test-Result 'run' $false ($_ | Out-String)
}
finally {
    if ($null -ne $work -and (Test-Path -LiteralPath $work)) { Remove-Item -LiteralPath $work -Recurse -Force }
    if ($null -ne $key) { $key.Close() }
    try { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($testRoot, $false) } catch {}
    # DeleteSubKey refuses a key that still has subkeys, so a parent another run is using stays.
    try { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey($testParent, $false) } catch {}
}
$left = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($testParent)
Test-Result 'throwaway key removed' ($null -eq $left -or $left.SubKeyCount -gt 0 -or $left.ValueCount -gt 0) "HKCU\$testParent afterwards: $(if ($null -eq $left) { 'gone' } else { "$($left.SubKeyCount) subkeys, $($left.ValueCount) values" })"
if ($null -ne $left) { $left.Close() }

$sink = Get-DialogSink
Test-Result 'no window shown' ($sink.Shown -eq 0) $sink.Detail
if ($script:failed -gt 0) { Write-Host "$script:failed check(s) failed."; exit 1 }
Write-Host 'Every check passed.'
exit 0
