<#
Checks the Events page's switches without the game or the local server. Every file is written under a throwaway
folder in %TEMP%, deleted when the check ends.

  pwsh -File tools\Test-EventSwitches.ps1 [-Python D:\games\FGOA\Server\python\python.exe] [-CloudFgo <his 1.10 package>\Server\artemis\titles\fgo]

Through the launcher assembly (src\bin\Release, so run build.cmd first), against Cloud23333's own event catalogue
activity_display_catalog.json: the catalogue is read in release order and a broken one is refused; the events fall
into his three editions and seven kinds in his order; each is named in English where the launcher's English text has
its title, as Part 1 or Part 2 of a split event and as Run N of a title that comes back; a move stays inside the
event's own kind; and Server\state\activity-display.json is written the way his launcher writes it: the events
switched off in hidden_ids, an order of the player's own in ordered_ids or none for release order, and every id and
key the page does not know kept. His own server code, activity_display.py, then reads the file back to the same
switches and order.

Exit codes: 0 every check passed, 1 a check failed, 2 a prerequisite is missing.
#>
[CmdletBinding()]
param(
    [string]$Python = 'D:\FGOA\Server\python\python.exe',
    [string]$CloudFgo = "$env:FGOAC_CLOUD_UPDATE\payload\payload\Server\artemis\titles\fgo"
)
$ErrorActionPreference = 'Stop'

$repository = Split-Path -Parent $PSScriptRoot
$assembly = Join-Path $repository 'src\bin\Release\net6.0-windows\FGOLocalPlatform.dll'
if ($PSVersionTable.PSVersion.Major -lt 7) { Write-Host 'Run this with pwsh 7: the launcher assembly targets .NET 6.'; exit 2 }
foreach ($path in @($assembly, $Python, (Join-Path $CloudFgo 'activity_display_catalog.json'), (Join-Path $CloudFgo 'activity_display.py'))) {
    if (!(Test-Path -LiteralPath $path)) { Write-Host "Missing $path."; exit 2 }
}

$script:failed = 0
function Test-Result([string]$Name, [bool]$Passed, [string]$Detail) {
    Write-Host ("{0} {1}: {2}" -f $(if ($Passed) { 'PASS' } else { 'FAIL' }), $Name, $Detail)
    if (!$Passed) { $script:failed++ }
}

$work = Join-Path ([IO.Path]::GetTempPath()) ('fgoac-events-' + [guid]::NewGuid().ToString('N'))
$server = Join-Path $work 'Server'
$fgo = Join-Path $server 'artemis\titles\fgo'
$settingsPath = Join-Path $server 'state\activity-display.json'
try {
    New-Item -ItemType Directory -Path $fgo, (Join-Path $server 'state') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $CloudFgo 'activity_display_catalog.json'), (Join-Path $CloudFgo 'activity_display.py') -Destination $fgo
    $launcher = [Reflection.Assembly]::LoadFrom($assembly)
    # Every message box, dialog and window of the launcher goes to the sink, never to the screen.
    . (Join-Path $PSScriptRoot 'DialogSink.ps1'); Enable-DialogSink $assembly
    $switches = $launcher.GetType('FGOLocalPlatform.EventSwitches')
    if (!$switches) { Test-Result 'EventSwitches' $false 'FGOLocalPlatform.EventSwitches is missing'; throw 'stop' }
    $flags = [Reflection.BindingFlags]'NonPublic, Public, Static'
    function Call([string]$Method, [object[]]$Arguments) {
        $values = [object[]]::new($Arguments.Count)
        for ($i = 0; $i -lt $Arguments.Count; $i++) { if ($Arguments[$i] -is [psobject]) { $values[$i] = $Arguments[$i].psobject.BaseObject } else { $values[$i] = $Arguments[$i] } }
        $info = $switches.GetMethod($Method, $flags)
        if (!$info) { throw "EventSwitches.$Method is missing" }
        return , $info.Invoke($null, $values)
    }

    $offered = Call 'Offered' @($server)
    $catalogPath = [string](Call 'CatalogPath' @($server))
    $entries = Call 'ReadCatalog' @($catalogPath)
    $first = @($entries | Select-Object -First 3 | ForEach-Object { "$($_.Id):$($_.ReleaseOrder)" })
    Test-Result 'his catalogue in release order' ($offered -and $entries.Count -eq 73 -and ($first -join ',') -ceq '72:0,73:1,1:10') "$($entries.Count) events; first: $($first -join ', ')"
    $broken = Join-Path $work 'broken.json'
    [IO.File]::WriteAllText($broken, '{"schema_version": 1, "events": [{"id": 1, "name": "a", "edition": "original", "category": "none"}]}')
    $refused = try { [void](Call 'ReadCatalog' @($broken)); 'read' } catch { 'refused' }
    Test-Result 'a broken catalogue is refused' ($refused -eq 'refused') $refused

    $groupList = Call 'Groups' @(, $entries)
    $groups = @($groupList | ForEach-Object { "$($_.Title)=$($_.Count)" })
    Test-Result 'his editions and kinds in his order' ($groups[0] -ceq 'Original events=41' -and $groups[1] -ceq 'Story and quests=13' -and $groups.Contains('Reruns=29') -and $groups[-2] -ceq 'Permanent extra episodes=3' -and $groups[-1] -ceq 'Story and quests=3' -and $groups.Count -eq 16) ($groups -join ', ')

    $byId = @{}
    foreach ($entry in $entries) { $byId[$entry.Id] = $entry }
    $labels = foreach ($id in 72, 73, 2, 63) { [string](Call 'Label' @($byId[$id], $entries)) }
    $runs = @($entries | ForEach-Object { [string](Call 'Label' @($_, $entries)) } | Where-Object { $_ -match ' - Run \d+$' })
    Test-Result 'English names, parts and runs' ($labels[0] -ceq 'Demonic Wails, Drunken Dream Capital: Rashomon - Part 1' -and $labels[1].EndsWith(' - Part 2') -and $labels[2] -ceq 'Gudaguda Honnoji - Run 1' -and $labels[3] -ceq $byId[63].Name -and $runs.Count -gt 0) "$($labels -join ' | '); runs: $($runs.Count)"

    $sorted = Call 'Sort' @($entries, [int[]]@())
    $story = @($sorted | Where-Object { $_.Edition -eq 'original' -and $_.Category -eq 'story' })
    $grouped = Call 'Grouped' @(, $sorted)
    $moved = Call 'Move' @($sorted, $story[0].Id, 'up')
    $top = Call 'Move' @($sorted, $story[-1].Id, 'top')
    $storyAfter = @($top | Where-Object { $_.Edition -eq 'original' -and $_.Category -eq 'story' })
    $outside = @(Compare-Object @($grouped | Where-Object { !($_.Edition -eq 'original' -and $_.Category -eq 'story') } | ForEach-Object Id) @($top | Where-Object { !($_.Edition -eq 'original' -and $_.Category -eq 'story') } | ForEach-Object Id) -SyncWindow 0)
    Test-Result 'a move stays inside its kind' ((@($moved | ForEach-Object Id) -join ',') -ceq (@($grouped | ForEach-Object Id) -join ',') -and $storyAfter[0].Id -eq $story[-1].Id -and $outside.Count -eq 0) "first story event up: unchanged; last to top: $($storyAfter[0].Id)"

    [IO.File]::WriteAllText($settingsPath, '{"schema_version": 1, "hidden_ids": [999], "ordered_ids": [998], "note": "his"}')
    $doc = Call 'Read' @($settingsPath)
    $visible = [Collections.Generic.HashSet[int]]::new()
    foreach ($entry in $entries) { if ($entry.Id -notin 2, 72) { [void]$visible.Add($entry.Id) } }
    $order = [int[]]@($top | ForEach-Object Id)
    [void](Call 'Save' @($settingsPath, $top, $visible, $order))
    $saved = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    Test-Result 'switches saved as his launcher saves them' (($saved.hidden_ids -join ',') -ceq '2,72,999' -and $saved.ordered_ids.Count -eq 74 -and $saved.ordered_ids[0] -eq $order[0] -and $saved.ordered_ids[-1] -eq 998 -and $saved.note -ceq 'his' -and $saved.schema_version -eq 1) "hidden $($saved.hidden_ids -join ','), $($saved.ordered_ids.Count) in order, note $($saved.note)"

    $reader = @'
import json, sys, importlib.util, logging
spec = importlib.util.spec_from_file_location("activity_display", sys.argv[1])
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
class Server: pass
server = Server()
server._install_root = "."
server._activity_display_path = sys.argv[2]
server.logger = logging.getLogger("stub")
hidden, stamp, sequence = module.preferences(server)
print(json.dumps({"hidden": sorted(hidden), "order": list(sequence)}))
'@
    $read = & $Python -c $reader (Join-Path $fgo 'activity_display.py') $settingsPath | ConvertFrom-Json
    Test-Result 'his server reads the same switches and order' (($read.hidden -join ',') -ceq '2,72,999' -and (($read.order | Select-Object -First 73) -join ',') -ceq ($order -join ',')) "hidden $($read.hidden -join ','), order starts $(@($read.order | Select-Object -First 3) -join ',')"

    [void](Call 'Save' @($settingsPath, $sorted, $visible, $null))
    $saved = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    Test-Result 'release order saves no order' ($null -eq $saved.PSObject.Properties['ordered_ids'] -and ($saved.hidden_ids -join ',') -ceq '2,72,999') "keys: $($saved.PSObject.Properties.Name -join ', ')"

    [IO.File]::WriteAllText($settingsPath, '{"schema_version": 2, "hidden_ids": []}')
    $refused = try { [void](Call 'Read' @($settingsPath)); 'read' } catch { 'refused' }
    Remove-Item -LiteralPath $settingsPath
    $fresh = Call 'Read' @($settingsPath)
    Test-Result 'a settings file of another version is refused; none reads as all on' ($refused -eq 'refused' -and $fresh['hidden_ids'].Count -eq 0) "$refused; fresh: $($fresh.ToJsonString())"

    Remove-Item -LiteralPath (Join-Path $fgo 'activity_display_catalog.json')
    Test-Result 'offered only with his catalogue' (!(Call 'Offered' @($server))) 'catalogue removed'
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
