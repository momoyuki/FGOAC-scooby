<#
Checks the Display card's graphics card pick on a game folder with Cloud23333's App\gpu-runtime, without the game and
without loading his probe: the adapter list is parsed from fixed probe output in his format, one line per adapter,
"LUID<tab>VENDOR<tab>NAME<tab>PHYSICALID".

  pwsh -File tools\Test-GpuCard.ps1 [-Routing <his payload>\App\FGO_GpuRouting.ps1]

Through the launcher assembly (src\bin\Release, so run build.cmd first), against his GpuSettingsView:
  - the list leaves out software adapters (":4294967295:"), shows one entry per physical device, and tells apart two
    cards of the same name by the end of their LUID; a line it cannot read is reported and lists nothing;
  - a card picked is saved as his launcher saves it in fgo-launcher.json's graphics object: nativeGpuName and
    nativeGpuPhysicalId, nativeGpuLuid removed; an older LUID-only pick, or an alias of the card's LUID, comes back
    as that card, saved by its name as his launcher saves it; a name two cards share, or a card that is gone, stays listed as saved and says so;
  - Automatic removes the three keys; other keys of the graphics object stay;
  - his own Resolve-FgoGpuSelection, from his FGO_GpuRouting.ps1, picks the card the launcher saved;
  - App\gpu-runtime\FgoGpuProbe.dll is what makes the pick available; with the AMD and Intel layer on, one sentence
    says no card can be picked.

Exit codes: 0 every check passed, 1 a check failed, 2 a prerequisite is missing.
#>
[CmdletBinding()]
param(
    [string]$Routing = "$env:FGOAC_CLOUD_UPDATE\payload\payload\App\FGO_GpuRouting.ps1"
)
$ErrorActionPreference = 'Stop'

$repository = Split-Path -Parent $PSScriptRoot
$assembly = Join-Path $repository 'src\bin\Release\net6.0-windows\FGOLocalPlatform.dll'
if ($PSVersionTable.PSVersion.Major -lt 7) { Write-Host 'Run this with pwsh 7: the launcher assembly targets .NET 6.'; exit 2 }
if (!(Test-Path -LiteralPath $assembly)) { Write-Host "Missing $assembly. Run build.cmd first."; exit 2 }
if (!(Test-Path -LiteralPath $Routing)) { Write-Host "Missing $Routing."; exit 2 }
# Every message box, dialog and window of the launcher goes to the sink, never to the screen.
. (Join-Path $PSScriptRoot 'DialogSink.ps1'); Enable-DialogSink $assembly

$script:failed = 0
function Test-Result([string]$Name, [bool]$Passed, [string]$Detail) {
    Write-Host ("{0} {1}: {2}" -f $(if ($Passed) { 'PASS' } else { 'FAIL' }), $Name, $Detail)
    if (!$Passed) { $script:failed++ }
}

$probe = @(
    "0000000000012345`t10DE`tNVIDIA GeForce RTX 4090`t0:1:0:0",
    "0000000000012399`t10DE`tNVIDIA GeForce RTX 4090`t0:1:0:0",
    "0000000000012388`t10DE`tNVIDIA GeForce RTX 4090`t0:1:4294967295:0",
    "0000000000020000`t1002`tAMD Radeon RX 6600`t0:3:0:0",
    "0000000000020001`t1002`tAMD Radeon RX 6600`t0:4:0:0",
    "0000000000030000`t1414`tMicrosoft Basic Render Driver`t0:4294967295:0:0"
) -join "`r`n"
$rows = @($probe -split "`n" | ForEach-Object { , ($_.TrimEnd("`r") -split "`t") })

function New-Card([string]$Graphics) {
    $card = [FGOLocalPlatform.GpuCard]::new()
    $card.Load([Text.Json.Nodes.JsonNode]::Parse($Graphics))
    $card.Detected($probe)
    return $card
}
function Get-Labels($Card) { return (@($Card.Options | ForEach-Object Label) -join ' | ') }
function Save-Card($Card, [string]$Graphics = '{"smaa":1}') {
    $node = [Text.Json.Nodes.JsonNode]::Parse($Graphics)
    $Card.Save($node)
    return $node.ToJsonString()
}
function Resolve-His([string]$Graphics) {
    $saved = $Graphics | ConvertFrom-Json
    $routing = $Routing
    return & {
        . $routing
        try { (Resolve-FgoGpuSelection -Rows $rows -Luid ([string]$saved.nativeGpuLuid) -Name ([string]$saved.nativeGpuName) -PhysicalId ([string]$saved.nativeGpuPhysicalId))[0] } catch { 'stopped' }
    }
}

try {
    [void][Reflection.Assembly]::LoadFrom($assembly)

    $card = New-Card '{}'
    $labels = Get-Labels $card
    Test-Result 'one entry per physical card, software adapters left out, same names told apart' ($labels -ceq 'Automatic | NVIDIA GeForce RTX 4090 | AMD Radeon RX 6600 · 00020000 | AMD Radeon RX 6600 · 00020001' -and $card.Selected.Luid -ceq '' -and $card.DetectionError -ceq '') $labels

    $card.Select('0000000000020001')
    $saved = Save-Card $card
    Test-Result 'a pick is saved as his launcher saves it' ($saved -ceq '{"smaa":1,"nativeGpuName":"AMD Radeon RX 6600","nativeGpuPhysicalId":"0:4:0:0"}') $saved
    Test-Result 'his routing resolves the saved pick to that card' ((Resolve-His $saved) -ceq '0000000000020001') (Resolve-His $saved)

    $card = New-Card '{"nativeGpuLuid":"0000000000012399"}'
    $saved = Save-Card $card
    Test-Result 'an older LUID pick comes back as its card' ($card.Selected.Luid -ceq '0000000000012345' -and $saved -ceq '{"smaa":1,"nativeGpuName":"NVIDIA GeForce RTX 4090","nativeGpuPhysicalId":""}' -and (Resolve-His $saved) -ceq '0000000000012345') "$($card.Selected.Label); $saved"

    $card = New-Card '{"nativeGpuName":"AMD Radeon RX 6600","nativeGpuPhysicalId":""}'
    $shared = "$($card.Selected.Label) / $(Save-Card $card)"
    $card = New-Card '{"nativeGpuName":"Old card","nativeGpuPhysicalId":"0:9:0:0"}'
    $gone = "$($card.Selected.Label) / $(Save-Card $card)"
    Test-Result 'a shared name or a missing card stays as saved and says so' ($shared -ceq 'AMD Radeon RX 6600 (two cards have this name; pick one again) / {"smaa":1,"nativeGpuName":"AMD Radeon RX 6600","nativeGpuPhysicalId":""}' -and $gone -ceq 'Old card (not found) / {"smaa":1,"nativeGpuName":"Old card","nativeGpuPhysicalId":"0:9:0:0"}') "$shared; $gone"

    $card = New-Card '{"nativeGpuName":"AMD Radeon RX 6600","nativeGpuPhysicalId":"0:3:0:0"}'
    $card.Select('')
    $saved = Save-Card $card '{"smaa":1,"nativeGpuLuid":"","nativeGpuName":"AMD Radeon RX 6600","nativeGpuPhysicalId":"0:3:0:0"}'
    Test-Result 'Automatic removes his three keys' ($saved -ceq '{"smaa":1}') $saved

    $card = [FGOLocalPlatform.GpuCard]::new()
    $card.Load([Text.Json.Nodes.JsonNode]::Parse('{"nativeGpuName":"AMD Radeon RX 6600","nativeGpuPhysicalId":"0:3:0:0"}'))
    $card.Detected("not a probe line`r`n")
    Test-Result 'a line the probe list cannot hold is reported' ($card.DetectionError.Length -gt 0 -and (Get-Labels $card) -ceq 'Automatic | AMD Radeon RX 6600 (not found)') "$($card.DetectionError); $(Get-Labels $card)"

    $work = Join-Path ([IO.Path]::GetTempPath()) ('fgoac-gpucard-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path (Join-Path $work 'gpu-runtime') -Force | Out-Null
    $before = [FGOLocalPlatform.GpuCard]::Available($work)
    [IO.File]::WriteAllBytes((Join-Path $work 'gpu-runtime\FgoGpuProbe.dll'), [byte[]](1..16))
    $after = [FGOLocalPlatform.GpuCard]::Available($work)
    Remove-Item -LiteralPath $work -Recurse -Force
    $card = New-Card '{}'
    Test-Result 'his probe makes the pick available; the layer turns his routing off' (!$before -and $after -and $card.Status($true) -ceq "No graphics card can be picked while the AMD and Intel compatibility layer is on." -and $card.Status($false).Length -gt 0) "$before -> $after; $($card.Status($true))"
}
catch {
    Test-Result 'run' $false ($_ | Out-String)
}

$sink = Get-DialogSink
Test-Result 'no window shown' ($sink.Shown -eq 0) $sink.Detail
if ($script:failed -gt 0) { Write-Host "$script:failed check(s) failed."; exit 1 }
Write-Host 'Every check passed.'
exit 0
