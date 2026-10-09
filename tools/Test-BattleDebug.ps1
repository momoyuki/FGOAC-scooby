<#
Checks the Debug card's battle-debug.ini without starting the launcher or the game.

  pwsh -File tools\Test-BattleDebug.ps1

Through the launcher assembly (src\bin\Release, so run build.cmd first), in a throwaway folder under %TEMP%: the
card's switches include stop_timer; nothing saved reads nothing on; a write with stop_timer on creates the file
with every switch in order, stop_timer=1 and the four enemy switches at 0, and reads back; a write with nothing on
writes stop_timer=0; only 1 reads on, and the last line for a switch wins. The card's switches are kept under
battleDebug in the Settings file beside its other keys; Play writes the ini from them, every switch 0 for Online and
as saved for Local, so a Local Play after an Online session killed midway has the saved switches again; switches saved
only in the ini are kept in the Settings file before an Online Play zeroes it. A card save goes to the Settings file,
and to the ini too except while an Online Play runs, when the ini keeps every switch 0. A write replaces only the
[battle_debug] section, or adds it at the end: other sections such as [event_capture] stay byte for byte. A header
counts as Windows reads it: spaces inside the brackets and text after the first ] are ignored. The switches of
Cloud23333's 1.10 Debug page on the card (auto_clear, cost_233, pvp_select and the PvP map) are kept like the others,
taken from the ini while the Settings file has none of them, written by a Local Play and 0 for an Online Play; his free
exploration (roam_enabled) and every other line of his stay on a Local Play, and an Online Play writes roam_enabled 0.
Each write records what the ini then holds; when the ini differs from that record, Cloud23333's launcher or updater
changed it, and its switches and map are taken in place of the saved ones. The PvP maps are listed as his launcher
lists them, named from this launcher's English set.

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

$work = Join-Path ([IO.Path]::GetTempPath()) ('fgoac-battle-debug-check-' + [guid]::NewGuid().ToString('N'))
try {
    [void][Reflection.Assembly]::LoadFrom($assembly)
    $debug = [FGOLocalPlatform.BattleDebug]
    New-Item -ItemType Directory -Path $work | Out-Null
    $ini = Join-Path $work 'battle-debug.ini'
    function Get-Lines { @([IO.File]::ReadAllLines($ini) | Where-Object { $_ -ne '' }) -join '|' }
    function New-Set([string[]]$Names) { , [Collections.Generic.HashSet[string]]::new([string[]]$Names) }

    Test-Result 'switches' (($debug::Switches -join ',') -ceq 'attack_stun,one_hit,full_np,instant_noble,invincible,stop_timer') ($debug::Switches -join ',')
    Test-Result 'his switches on the card' (($debug::HisCardSwitches -join ',') -ceq 'auto_clear,cost_233,pvp_select') ($debug::HisCardSwitches -join ',')

    Test-Result 'nothing saved' ($debug::Read($ini).Count -eq 0) "$($debug::Read($ini).Count) on"

    $debug::Write($ini, (New-Set @('stop_timer', 'one_hit')))
    $read = @($debug::Read($ini) | Sort-Object)
    Test-Result 'stop timer on' ((Get-Lines) -ceq '[battle_debug]|attack_stun=0|one_hit=1|full_np=0|instant_noble=0|invincible=0|stop_timer=1|freeze_enemy=0|enemy_evade=0|enemy_flee=0|enemy_guard=0|auto_clear=0|cost_233=0|pvp_select=0' -and ($read -join ',') -ceq 'one_hit,stop_timer') "file $(Get-Lines); read $($read -join ',')"

    $debug::Write($ini, (New-Set @()))
    Test-Result 'stop timer off' ((Get-Lines) -ceq '[battle_debug]|attack_stun=0|one_hit=0|full_np=0|instant_noble=0|invincible=0|stop_timer=0|freeze_enemy=0|enemy_evade=0|enemy_flee=0|enemy_guard=0|auto_clear=0|cost_233=0|pvp_select=0' -and $debug::Read($ini).Count -eq 0) "file $(Get-Lines)"

    [IO.File]::WriteAllText($ini, "[battle_debug]`r`nstop_timer=2`r`nfull_np=1`r`nfull_np=0`r`ninvincible=0`r`ninvincible= 1 `r`nfreeze_enemy=1`r`n")
    $read = @($debug::Read($ini) | Sort-Object)
    Test-Result 'saved values read' (($read -join ',') -ceq 'invincible') "read $($read -join ',')"

    # The card's switches live in the Settings file; each Play writes the ini from them.
    $settings = Join-Path $work 'fgo-launcher.json'
    $zeros = '[battle_debug]|attack_stun=0|one_hit=0|full_np=0|instant_noble=0|invincible=0|stop_timer=0|freeze_enemy=0|enemy_evade=0|enemy_flee=0|enemy_guard=0|auto_clear=0|cost_233=0|pvp_select=0|roam_enabled=0'
    function Get-Saved { @($debug::Load($work) | Sort-Object) -join ',' }
    [IO.File]::WriteAllText($settings, '{"displayMode": "windowed"}')
    $debug::Save($work, (New-Set @('stop_timer', 'invincible')))
    $json = [IO.File]::ReadAllText($settings) | ConvertFrom-Json
    Test-Result 'settings file' ($json.displayMode -ceq 'windowed' -and ($json.battleDebug | ConvertTo-Json -Compress) -ceq '{"attack_stun":false,"one_hit":false,"full_np":false,"instant_noble":false,"invincible":true,"stop_timer":true,"auto_clear":false,"cost_233":false,"pvp_select":false}' -and (Get-Saved) -ceq 'invincible,stop_timer') "battleDebug $($json.battleDebug | ConvertTo-Json -Compress); read $(Get-Saved) with the ini holding invincible alone"

    $debug::WriteForSession($work, $true)
    Test-Result 'online play' ((Get-Lines) -ceq $zeros -and (Get-Saved) -ceq 'invincible,stop_timer') "file $(Get-Lines); saved $(Get-Saved)"
    # An Online session killed before it ended leaves that file; the next Local Play writes the saved switches and keeps
    # his switches as the Online Play left them, at 0.
    $debug::WriteForSession($work, $false)
    Test-Result 'local play' ((Get-Lines) -ceq '[battle_debug]|attack_stun=0|one_hit=0|full_np=0|instant_noble=0|invincible=1|stop_timer=1|freeze_enemy=0|enemy_evade=0|enemy_flee=0|enemy_guard=0|auto_clear=0|cost_233=0|pvp_select=0|roam_enabled=0') "file $(Get-Lines)"

    # Switches saved only in the ini, by a launcher before the Settings key, are kept before an Online Play zeroes the file.
    [IO.File]::WriteAllText($settings, '{"displayMode": "windowed"}')
    $debug::Write($ini, (New-Set @('one_hit')))
    $debug::WriteForSession($work, $true)
    $online = Get-Lines
    $debug::WriteForSession($work, $false)
    Test-Result 'switches moved from the ini' ($online -ceq $zeros -and (Get-Saved) -ceq 'one_hit' -and (Get-Lines) -ceq '[battle_debug]|attack_stun=0|one_hit=1|full_np=0|instant_noble=0|invincible=0|stop_timer=0|freeze_enemy=0|enemy_evade=0|enemy_flee=0|enemy_guard=0|auto_clear=0|cost_233=0|pvp_select=0|roam_enabled=0') "Online $online; saved $(Get-Saved); then Local $(Get-Lines)"

    # The card saves while an Online Play runs: the Settings file only, the ini keeps every switch 0.
    $debug::WriteForSession($work, $true)
    $debug::SaveFromCard($work, (New-Set @('full_np')), $true)
    Test-Result 'card during Online play' ((Get-Lines) -ceq $zeros -and (Get-Saved) -ceq 'full_np') "file $(Get-Lines); saved $(Get-Saved)"
    $debug::SaveFromCard($work, (New-Set @('full_np')), $false)
    Test-Result 'card otherwise' ((Get-Lines) -ceq '[battle_debug]|attack_stun=0|one_hit=0|full_np=1|instant_noble=0|invincible=0|stop_timer=0|freeze_enemy=0|enemy_evade=0|enemy_flee=0|enemy_guard=0|auto_clear=0|cost_233=0|pvp_select=0|roam_enabled=0' -and (Get-Saved) -ceq 'full_np') "file $(Get-Lines); saved $(Get-Saved)"

    # Other sections, such as the stock hook's [event_capture], stay byte for byte; only [battle_debug] is rewritten.
    $latin1 = [Text.Encoding]::Latin1
    $block = "[battle_debug]`nattack_stun=0`none_hit=0`nfull_np=0`ninstant_noble=0`ninvincible=0`nstop_timer=0`nfreeze_enemy=0`nenemy_evade=0`nenemy_flee=0`nenemy_guard=0`nauto_clear=0`ncost_233=0`npvp_select=0`n"
    $onlineBlock = $block + "roam_enabled=0`n"
    $before = "; kept`r`n[event_capture]`r`nenabled=1`r`n; caf$([char]0xC3)$([char]0xA9)`r`n`r`n[Battle_Debug]`r`none_hit=1`r`nstop_timer=1`r`n[other]`r`nkey=value"
    [IO.File]::WriteAllBytes($ini, $latin1.GetBytes($before))
    $debug::WriteForSession($work, $true)
    $after = $latin1.GetString([IO.File]::ReadAllBytes($ini))
    $expected = "; kept`r`n[event_capture]`r`nenabled=1`r`n; caf$([char]0xC3)$([char]0xA9)`r`n`r`n$onlineBlock[other]`r`nkey=value"
    Test-Result 'other sections kept' ($after -ceq $expected -and $debug::Read($ini).Count -eq 0) "file $($after -replace "`r", '\r' -replace "`n", '\n')"
    [IO.File]::WriteAllBytes($ini, $latin1.GetBytes("[event_capture]`r`nenabled=1"))
    $debug::Write($ini, (New-Set @()))
    $after = $latin1.GetString([IO.File]::ReadAllBytes($ini))
    Test-Result 'section added' ($after -ceq "[event_capture]`r`nenabled=1`n$block") "file $($after -replace "`r", '\r' -replace "`n", '\n')"

    # Cloud23333's 1.10 switches in the same section. Auto clear, cost 233 and the PvP map pick are on the card: kept in
    # the Settings file like the others, taken from the ini while the Settings file has none of them, written by a Local
    # Play with the map, and 0 for an Online Play, after which a Local Play brings them back. Free exploration and
    # every other line he writes stay as they are on a Local Play; an Online Play writes free exploration 0.
    [IO.File]::WriteAllText($settings, '{"displayMode": "windowed", "battleDebug": {"one_hit": true}}')
    [IO.File]::WriteAllBytes($ini, $latin1.GetBytes("[battle_debug]`r`nauto_clear=1`r`ncost_233=1`r`npvp_select=1`r`npvp_stage=12`r`nroam_enabled=1`r`nroam_scene=STG017`r`none_hit=1`r`n"))
    $migrated = "$(Get-Saved); map $($debug::PvpStage($work))"
    $debug::WriteForSession($work, $false)
    $local = (Get-Lines) -replace "`r", ''
    Test-Result 'his switches taken from the ini' ($migrated -ceq 'auto_clear,cost_233,one_hit,pvp_select; map 12' -and $local -ceq '[battle_debug]|attack_stun=0|one_hit=1|full_np=0|instant_noble=0|invincible=0|stop_timer=0|freeze_enemy=0|enemy_evade=0|enemy_flee=0|enemy_guard=0|auto_clear=1|cost_233=1|pvp_select=1|pvp_stage=12|pvp_random=0|roam_enabled=1|roam_scene=STG017') "saved $migrated; file $local"
    $debug::SaveFromCard($work, (New-Set @('cost_233', 'pvp_select')), $false, 2005, '2001,2005,2030')
    $json = [IO.File]::ReadAllText($settings) | ConvertFrom-Json
    $card = (Get-Lines) -replace "`r", ''
    Test-Result 'his switches and the map saved from the card' (($json.battleDebug | ConvertTo-Json -Compress) -ceq '{"attack_stun":false,"one_hit":false,"full_np":false,"instant_noble":false,"invincible":false,"stop_timer":false,"auto_clear":false,"cost_233":true,"pvp_select":true,"pvp_stage":2005,"written":["cost_233","pvp_select"],"writtenStage":2005}' -and $card -ceq '[battle_debug]|attack_stun=0|one_hit=0|full_np=0|instant_noble=0|invincible=0|stop_timer=0|freeze_enemy=0|enemy_evade=0|enemy_flee=0|enemy_guard=0|auto_clear=0|cost_233=1|pvp_select=1|pvp_stage=2005|pvp_random=0|pvp_stages=2001,2005,2030|roam_enabled=1|roam_scene=STG017') "battleDebug $($json.battleDebug | ConvertTo-Json -Compress); file $card"
    $debug::WriteForSession($work, $true)
    $online = (Get-Lines) -replace "`r", ''
    $debug::WriteForSession($work, $false)
    $back = (Get-Lines) -replace "`r", ''
    Test-Result 'his switches 0 on an Online Play' ($online -ceq ($zeros + '|pvp_stage=2005|pvp_random=0|pvp_stages=2001,2005,2030|roam_scene=STG017')) "file $online"
    Test-Result 'his switches back on the next Local Play' ($back -ceq '[battle_debug]|attack_stun=0|one_hit=0|full_np=0|instant_noble=0|invincible=0|stop_timer=0|freeze_enemy=0|enemy_evade=0|enemy_flee=0|enemy_guard=0|auto_clear=0|cost_233=1|pvp_select=1|pvp_stage=2005|pvp_random=0|roam_enabled=0|pvp_stages=2001,2005,2030|roam_scene=STG017') "file $back"

    # His launcher, or his updater, changes the ini after a Play of this launcher: its switches and map are kept, by the
    # card and by the next Play, and an ini this launcher wrote last still gives the saved switches.
    $debug::SaveFromCard($work, (New-Set @('invincible')), $false, 2005)
    $debug::WriteForSession($work, $true)
    $debug::WriteForSession($work, $false)
    $ours = Get-Saved
    $text = [IO.File]::ReadAllText($ini) -replace 'invincible=1', 'invincible=0' -replace 'one_hit=0', 'one_hit=1' -replace 'pvp_stage=2005', 'pvp_stage=2030'
    [IO.File]::WriteAllText($ini, $text)
    $his = "$(Get-Saved); map $($debug::PvpStage($work))"
    $debug::WriteForSession($work, $false)
    $kept = (Get-Lines) -replace "`r", ''
    $again = "$(Get-Saved); map $($debug::PvpStage($work))"
    Test-Result 'a change his launcher made to the ini is kept' ($ours -ceq 'invincible' -and $his -ceq 'one_hit; map 2030' -and $kept -match '\|one_hit=1\|' -and $kept -match '\|invincible=0\|' -and $kept -match '\|pvp_stage=2030\|' -and $again -ceq 'one_hit; map 2030') "ours $ours; after his change $his; next Play $kept; then $again"

    # The PvP maps, as his launcher lists them: stage ids 2001 to 2030 in the master's stage_asset table whose stage file
    # is in the game, in stage order, named in English from this launcher's English set, else by number.
    $stages = [FGOLocalPlatform.BattleDebug].Assembly.GetType('FGOLocalPlatform.PvpStages')
    if ($null -ne $stages) {
        $install = Join-Path $work 'install'
        $master = Join-Path $install 'Server\data\fgo-master-11.51\stage_asset'
        $stageFolder = Join-Path $install 'App\rom\stage'
        $english = Join-Path $install 'App\Localization\en-fgoac'
        foreach ($folder in @($master, $stageFolder, $english)) { New-Item -ItemType Directory -Path $folder -Force | Out-Null }
        $table = @('data_list.length=5',
            'stage_asset.0.stage_id=1001', 'stage_asset.0.stage_name=STGTUT2', 'stage_asset.0.disp_name=TUTORIAL',
            'stage_asset.1.stage_id=2005', 'stage_asset.1.stage_name=STG009_VD', "stage_asset.1.disp_name=$([char]0x738B)",
            'stage_asset.2.stage_id=2001', 'stage_asset.2.stage_name=STG002_VD', "stage_asset.2.disp_name=$([char]0x57CE)",
            'stage_asset.3.stage_id=2030', 'stage_asset.3.stage_name=STG024_VE', 'stage_asset.3.disp_name=SUNSET',
            'stage_asset.4.stage_id=2003', 'stage_asset.4.stage_name=STG003_VD', 'stage_asset.4.disp_name=NO STAGE FILE')
        [IO.File]::WriteAllLines((Join-Path $master 'arms_mst_stage_asset.bin'), $table)
        foreach ($code in @('stg009_vd', 'STG002_VD', 'STG024_VE', 'STGTUT2')) { [IO.File]::WriteAllText((Join-Path $stageFolder "$code.info"), '') }
        $entries = @(@{ scope = 'server.name'; source = [string][char]0x738B; translation = 'Ballroom of the King''s Return' }, @{ scope = 'server.name'; source = [string][char]0x57CE; translation = 'Orleans Castle Gate' })
        [IO.File]::WriteAllText((Join-Path $english 'server-display-text.json'), (@{ game_version = '11.51'; entries = $entries } | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))
        $listed = @($stages.GetMethod('Read', [Reflection.BindingFlags]'NonPublic, Public, Static').Invoke($null, @([string](Join-Path $install 'App'))) | ForEach-Object { "$($_.StageId)=$($_.Name)" }) -join ' | '
        Test-Result 'PvP maps' ($listed -ceq '2001=Orleans Castle Gate | 2005=Ballroom of the King''s Return | 2030=Stage 2030') $listed
    } else {
        Test-Result 'PvP maps' $false 'FGOLocalPlatform.PvpStages is missing'
    }

    # Headers as Windows reads them: spaces inside the brackets, text after the first ], or no ] at all.
    [IO.File]::WriteAllBytes($ini, $latin1.GetBytes("[ Battle_Debug ] ; x`r`ninvincible=1`r`n[event_capture]`r`nenabled=1`r`n[battle_debug`r`none_hit=1`r`n"))
    $debug::WriteForSession($work, $true)
    $after = $latin1.GetString([IO.File]::ReadAllBytes($ini))
    $headers = @([regex]::Matches($after, '(?im)^\s*\[[^\r\n]*battle_debug')).Count
    Test-Result 'loose headers replaced' ($after -ceq "$onlineBlock[event_capture]`r`nenabled=1`r`n" -and $headers -eq 1 -and $debug::Read($ini).Count -eq 0) "file $($after -replace "`r", '\r' -replace "`n", '\n')"
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
