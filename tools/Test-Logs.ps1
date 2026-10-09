<#
Checks the launcher's logs and the Report without starting the launcher or the game.

  pwsh -File tools\Test-Logs.ps1

Through the launcher assembly (src\bin\Release, so run build.cmd first) it checks that a card's twenty
digits and a recovery key, dashed or not, come out of Redaction as <redacted> while ports, sizes, dates,
exit codes, a certificate fingerprint and a nineteen-digit number stay as they were. In a throwaway logs
folder under %TEMP% the launcher log writes redacted, timestamped lines to server-control.log, moves a
file last written on an earlier day to server-control-YYYY-MM-DD.log, keeps fourteen days of those and
deletes nothing else. A throwaway install then gets a Report: the zip holds the manifest, the Game log,
today's and the previous launcher log, the other logs, the settings file with the community server's
address and fingerprint removed, the patch transcript, the newest crash dump of each kind at up to 8 MB and
Cloud23333's display snapshot (each monitor's model, connection, refresh rate and scaling, and the game window), his
render logs, and files-on-disk.json with the size and SHA-256 of each graphics and hook file in App\ (missing ones
listed as missing), and no
card file, no DEVICE folder and no twenty-digit run in any text entry. A grant of bonus items the account tool queued
is reported by one Status sentence and one Game log line that names each item.

Exit codes: 0 every check passed, 1 a check failed, 2 a prerequisite is missing.
#>
[CmdletBinding()]
param(
    [switch]$KeepFiles
)
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

$work = Join-Path ([IO.Path]::GetTempPath()) ('fgoac-logs-check-' + [guid]::NewGuid().ToString('N'))
$card = '01234567890123456789'
$key = 'MFRG-GZDF-MZTW-Q2LK-NNWG-23TP-OBYX-E4TT'
try {
    [void][Reflection.Assembly]::LoadFrom($assembly)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $redaction = [FGOLocalPlatform.Redaction]
    $log = [FGOLocalPlatform.LauncherLog]
    $report = [FGOLocalPlatform.Report]

    # Redaction: the card and the key go, ordinary numbers stay.
    $cases = @(
        @("[tunnel] FGOAC-TUNNEL 1 title $card", '[tunnel] FGOAC-TUNNEL 1 title <redacted>'),
        @("card=$card;", 'card=<redacted>;'),
        @("Recovery key: $key", 'Recovery key: <redacted>'),
        @("key $($key.Replace('-', '').ToLowerInvariant()) pasted", 'key <redacted> pasted'),
        @("$card$card", '<redacted>')
    )
    foreach ($case in $cases) {
        $out = $redaction::Apply($case[0])
        Test-Result 'redacted' ($out -ceq $case[1]) $out
    }
    $ordinary = @(
        '[2026-09-26 14:05:09] Port 40080 on 127.0.0.1 answered',
        'wrote 1048576 bytes to fgo.log in 0.25 s',
        'ago.exe crashed after 12.5 seconds (exit -1073741819) 0xC0000005',
        'ticks 1234567890123456789 and build 20260926',
        'pin 3f6a1c0e9b8d7f2a4c5e6b1d0a9f8e7d6c5b4a3928171605f4e3d2c1b0a99887',
        'patch transcript fgoa-scooby-patch-0f8e1c2d3b4a4e6f9a7b5c3d1e2f4a6b.log',
        'amdaemon-20260926-140509-123-tmp.dmp'
    )
    foreach ($line in $ordinary) {
        $out = $redaction::Apply($line)
        Test-Result 'ordinary line kept' ($out -ceq $line) $out
    }

    # The launcher log: redacted lines, a new file per day, fourteen days kept.
    $logs = Join-Path $work 'logs'
    $today = Join-Path $logs 'server-control.log'
    $log::Append($logs, "Startup check: card $card", [datetime]'2026-09-20 10:00:00')
    $text = Get-Content -LiteralPath $today -Raw
    Test-Result 'launcher log line' ($text -ceq "[2026-09-20 10:00:00] Startup check: card <redacted>`n") $text
    [IO.File]::SetLastWriteTime($today, [datetime]'2026-09-20 23:59:00')
    $log::Append($logs, 'next day', [datetime]'2026-09-21 00:01:00')
    $archive = Join-Path $logs 'server-control-2026-09-20.log'
    Test-Result 'earlier day archived' ((Test-Path -LiteralPath $archive) -and (Get-Content -LiteralPath $archive -Raw) -like '*Startup check*') $archive
    Test-Result 'today starts a new file' ((Get-Content -LiteralPath $today -Raw) -ceq "[2026-09-21 00:01:00] next day`n") (Get-Content -LiteralPath $today -Raw)
    [IO.File]::SetLastWriteTime($today, [datetime]'2026-09-21 00:01:00')
    $log::Append($logs, 'same day', [datetime]'2026-09-21 18:00:00')
    Test-Result 'same day appends' (@(Get-Content -LiteralPath $today).Count -eq 2) ((Get-Content -LiteralPath $today) -join ' | ')

    foreach ($day in 1..20) { Set-Content -LiteralPath (Join-Path $logs ('server-control-2026-09-{0:00}.log' -f $day)) -Value "day $day" }
    $others = @('server-control-notes.log', 'server-control-2026-09-01.txt', 'fgo.log', 'server-control-2026-13-01.log')
    foreach ($name in $others) { Set-Content -LiteralPath (Join-Path $logs $name) -Value 'other' }
    [IO.File]::SetLastWriteTime($today, [datetime]'2026-09-21 18:00:00')
    $log::Rotate($logs, [datetime]'2026-09-22 08:00:00')
    $kept = @(Get-ChildItem -LiteralPath $logs -Filter 'server-control-2026-09-*.log' | ForEach-Object Name | Sort-Object)
    $expected = @(9..21 | ForEach-Object { 'server-control-2026-09-{0:00}.log' -f $_ })
    Test-Result 'fourteen days kept' ((($kept -join ',') -ceq ($expected -join ',')) -and !(Test-Path -LiteralPath $today)) ($kept -join ', ')
    Test-Result 'nothing else deleted' (@($others | Where-Object { Test-Path -LiteralPath (Join-Path $logs $_) }).Count -eq $others.Count) ($others -join ', ')
    Test-Result 'previous launcher log' ($log::Previous($logs) -eq (Join-Path $logs 'server-control-2026-09-21.log')) "$($log::Previous($logs))"

    # The Game log's pending output, Cloud23333's LauncherOutputBuffer: stdout and stderr each capped on its own
    # (192 KB and 64 KB of text), the oldest lines dropped first and counted, the rest drained in the order written,
    # at most 64 KB at a time.
    $bufferType = [FGOLocalPlatform.Redaction].Assembly.GetType('FGOLocalPlatform.LauncherOutputBuffer')
    if ($null -ne $bufferType) {
        $buffer = [Activator]::CreateInstance($bufferType, $true)
        $enqueue = $bufferType.GetMethod('Enqueue', [Reflection.BindingFlags]'NonPublic, Public, Instance')
        $drain = $bufferType.GetMethod('Drain', [Reflection.BindingFlags]'NonPublic, Public, Instance')
        $complete = $bufferType.GetMethod('Complete', [Reflection.BindingFlags]'NonPublic, Public, Instance')
        [void]$enqueue.Invoke($buffer, @('error 1', $true))
        $filler = 'x' * 98
        for ($i = 0; $i -lt 4000; $i++) { [void]$enqueue.Invoke($buffer, @("$filler", $false)) }
        [void]$enqueue.Invoke($buffer, @('error 2', $true))
        [void]$enqueue.Invoke($buffer, @('last line', $false))
        [void]$enqueue.Invoke($buffer, @(('y' * 40000), $false))
        $batches = @()
        $finished = $false
        for ($round = 0; $round -lt 20 -and !$finished; $round++) {
            if ($round -eq 5) { [void]$complete.Invoke($buffer, @()) }
            $result = $drain.Invoke($buffer, @())
            $batches += $result.Item1
            $finished = $result.Item2
        }
        $text = $batches -join ''
        $lines = $text -split "`r`n"
        $kept = @($lines | Where-Object { $_ -ceq $filler }).Count
        $longest = ($batches | ForEach-Object { $_.Length } | Measure-Object -Maximum).Maximum
        $order = $text.IndexOf('[stderr] error 2') -lt $text.IndexOf("last line`r`n") -and $text.IndexOf('[stderr] error 1') -lt $text.IndexOf($filler)
        Test-Result 'Game log output capped per stream' ($lines[0] -match '^\[launcher\] Output too fast - the panel dropped the oldest [0-9]+ stdout and 0 stderr lines; the log file still has everything the script wrote\.$' -and $text.Contains('[stderr] error 1') -and $text.Contains('[stderr] error 2') -and $order -and $kept -gt 1500 -and $kept -lt 2000 -and $text.Contains('...single line of output truncated...') -and $longest -le 65536 -and $finished) "first line: $($lines[0]); filler lines kept: $kept; largest batch: $longest; in order: $order; finished: $finished"
    } else {
        Test-Result 'Game log output capped per stream' $false 'FGOLocalPlatform.LauncherOutputBuffer is missing'
    }

    # The Report.
    Test-Result 'report name' ($report::FileName([datetime]'2026-09-26 14:05:59') -ceq 'FGOAC-report-2026-09-26-1405.zip') $report::FileName([datetime]'2026-09-26 14:05:59')
    $install = Join-Path $work 'install'
    $app = Join-Path $install 'App'
    $installLogs = Join-Path $install 'logs'
    $temp = Join-Path $work 'temp'
    $cardFolder = Join-Path $install 'DEVICE\community\play.example.org'
    foreach ($folder in @($app, $installLogs, $temp, $cardFolder)) { New-Item -ItemType Directory -Path $folder -Force | Out-Null }
    Set-Content -LiteralPath (Join-Path $cardFolder 'aime.txt') -Value $card
    @{
        displayMode     = 'borderless'
        resolutionWidth = 1920
        lastCard        = $card
        communityServer = @{ enabled = $true; host = 'play.example.org'; tlsPort = 443; certificatePin = 'ab' * 32; ports = @{ http = 40080 } }
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $app 'fgo-launcher.json')
    Set-Content -LiteralPath (Join-Path $installLogs 'server-control.log') -Value "[2026-09-26 10:00:00] today $card to play.example.org:443"
    Set-Content -LiteralPath (Join-Path $installLogs 'server-control-2026-09-25.log') -Value 'yesterday'
    Set-Content -LiteralPath (Join-Path $installLogs 'server-control-2026-09-20.log') -Value 'last week'
    foreach ($name in @('fgo.log', 'fgozh.log', 'environment-check.txt', 'fgo-last-launch.log', 'update.log', 'artemis-stderr.log', 'amdaemon-20260926-140509-123-amdaemon.exe.log')) {
        Set-Content -LiteralPath (Join-Path $installLogs $name) -Value "$name line $card"
    }
    [IO.File]::WriteAllBytes((Join-Path $installLogs 'ago-crash-old.dmp'), [byte[]](1..200))
    [IO.File]::WriteAllBytes((Join-Path $installLogs 'amdaemon-20260926-140509-123-tmp.dmp'), [byte[]](1..200))
    $big = [IO.File]::Create((Join-Path $installLogs 'ago-crash-big.dmp'))
    $big.SetLength(9MB)
    $big.Dispose()
    [IO.File]::SetLastWriteTime((Join-Path $installLogs 'ago-crash-old.dmp'), [datetime]'2026-09-26 07:00:00')
    [IO.File]::SetLastWriteTime((Join-Path $installLogs 'ago-crash-big.dmp'), [datetime]'2026-09-26 10:00:00')
    Set-Content -LiteralPath (Join-Path $temp 'fgoa-scooby-patch-0f8e1c2d3b4a4e6f9a7b5c3d1e2f4a6b.log') -Value 'patch output'
    $large = Join-Path $installLogs 'fgo-inject-live.log'
    Set-Content -LiteralPath $large -Value (('x' * 99 + "`n") * 20000 + 'last line')
    # Cloud23333's render-diagnostics inputs: his render logs and the graphics and hook files in App\.
    $renderLogs = @('aft-render-20260926-140509-4242.jsonl', 'fgo-present-20260926-140509-4242-0f8e1c2d.csv', 'fgo-present-20260926-140509-4242-0f8e1c2d.json')
    foreach ($name in $renderLogs) { Set-Content -LiteralPath (Join-Path $installLogs $name) -Value "$name frame 1" }
    New-Item -ItemType Directory -Path (Join-Path $app 'gpu-runtime') -Force | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $app 'fgoglcompat.dll'), [byte[]](1..64))
    [IO.File]::WriteAllBytes((Join-Path $app 'gpu-runtime\opengl32.dll'), [byte[]](65..96))
    $compatHash = (Get-FileHash -LiteralPath (Join-Path $app 'fgoglcompat.dll') -Algorithm SHA256).Hash.ToLowerInvariant()

    $facts = [FGOLocalPlatform.Report+Facts]::new('1.3.0', 'Online', 'The game stopped with code 22.', "[launcher] started`n[tunnel] key $key`n", [Nullable[int]]22)
    $zip = Join-Path $work 'FGOAC-report-2026-09-26-1405.zip'
    [void]$report::Save($zip, $app, $temp, $facts)
    $archiveFile = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        $entries = @($archiveFile.Entries | ForEach-Object FullName)
        $texts = @{}
        foreach ($entry in $archiveFile.Entries) {
            if ($entry.FullName -notlike '*.dmp') {
                $reader = [IO.StreamReader]::new($entry.Open())
                $texts[$entry.FullName] = $reader.ReadToEnd()
                $reader.Dispose()
            }
        }
    }
    finally { $archiveFile.Dispose() }
    $wanted = @('manifest.txt', 'game-log.txt', 'fgo-launcher.json', 'logs/server-control.log', 'logs/server-control-2026-09-25.log', 'logs/fgo.log', 'logs/fgozh.log', 'logs/environment-check.txt', 'logs/fgo-last-launch.log', 'logs/fgo-inject-live.log', 'logs/update.log', 'logs/artemis-stderr.log', 'logs/amdaemon-20260926-140509-123-amdaemon.exe.log', 'logs/amdaemon-20260926-140509-123-tmp.dmp', 'temp/fgoa-scooby-patch-0f8e1c2d3b4a4e6f9a7b5c3d1e2f4a6b.log')
    $missing = @($wanted | Where-Object { $entries -notcontains $_ })
    Test-Result 'report entries' ($missing.Count -eq 0) "missing: $($missing -join ', '); has: $($entries -join ', ')"
    $unwanted = @($entries | Where-Object { $_ -match 'aime|DEVICE|server-control-2026-09-20|ago-crash' })
    Test-Result 'report leaves out' ($unwanted.Count -eq 0) ($unwanted -join ', ')
    $leaks = @($texts.Keys | Where-Object { $texts[$_] -match '(?<![0-9])[0-9]{20}(?![0-9])' -or $texts[$_].Contains($key) -or $texts[$_].Contains('play.example.org') })
    Test-Result 'report text redacted' ($leaks.Count -eq 0) ($leaks -join ', ')
    $settings = $texts['fgo-launcher.json'] | ConvertFrom-Json
    Test-Result 'settings redacted' ($settings.communityServer.host -eq '<redacted>' -and $settings.communityServer.certificatePin -eq '<redacted>' -and $settings.lastCard -eq '<redacted>' -and $settings.communityServer.ports.http -eq 40080 -and $settings.resolutionWidth -eq 1920) $texts['fgo-launcher.json']
    $manifest = $texts['manifest.txt']
    Test-Result 'manifest' ($manifest -match 'Launcher version: 1\.3\.0' -and $manifest -match 'Mode: Online' -and $manifest -match 'Last exit: 22 ' -and $manifest -match 'The game stopped with code 22\.' -and $manifest -match 'logs/ago-crash-big\.dmp' -and $manifest -match '1 older crash dump' -and $manifest -match 'Nothing was uploaded') $manifest
    $tail = $texts['logs/fgo-inject-live.log']
    Test-Result 'long log keeps its end' ($tail.Length -le 1MB + 200 -and $tail.TrimEnd().EndsWith('last line')) "$($tail.Length) characters"
    $missingRender = @($renderLogs | Where-Object { $entries -notcontains "logs/$_" })
    Test-Result 'render logs' ($missingRender.Count -eq 0) "missing: $($missingRender -join ', ')"
    $onDisk = if ($texts.ContainsKey('files-on-disk.json')) { @(($texts['files-on-disk.json'] | ConvertFrom-Json).filesOnDisk) } else { @() }
    $listed = @($onDisk | ForEach-Object file)
    $expectedFiles = @('fgoglcompat.dll', 'fgohook.dll', 'gpu-runtime/opengl32.dll', 'FGO_GpuRouting.ps1', 'gpu-runtime/FgoGpuProbe.dll', 'opengl32.dll', 'opengl32real.dll', 'fgoacaddon.dll', 'ago.exe')
    $compat = $onDisk | Where-Object file -ceq 'fgoglcompat.dll'
    $hook = $onDisk | Where-Object file -ceq 'fgohook.dll'
    $runtime = $onDisk | Where-Object file -ceq 'gpu-runtime/opengl32.dll'
    Test-Result 'files on disk' ((($listed -join ',') -ceq ($expectedFiles -join ',')) -and $compat.exists -eq $true -and $compat.bytes -eq 64 -and $compat.sha256 -ceq $compatHash -and $runtime.exists -eq $true -and $runtime.bytes -eq 32 -and $hook.exists -eq $false -and $null -eq $hook.sha256 -and $manifest -match 'files-on-disk\.json') $(if ($texts.ContainsKey('files-on-disk.json')) { $texts['files-on-disk.json'] } else { 'no files-on-disk.json' })
    $display = if ($texts.ContainsKey('display-presentation.json')) { $texts['display-presentation.json'] | ConvertFrom-Json } else { $null }
    $paths = if ($display) { @($display.activePaths) } else { @() }
    Test-Result 'display snapshot' ($null -ne $display -and $display.schema -ceq 'fgo-display-presentation-snapshot-v1' -and $display.displayErrorCode -eq 0 -and $paths.Count -ge 1 -and $null -ne $paths[0].configuredRefreshHz -and $null -ne $paths[0].scaling -and $null -ne $paths[0].connection -and $null -ne $display.gameWindows -and $manifest -match 'display-presentation\.json') $(if ($display) { "$($paths.Count) display path(s), first $($paths[0].monitorModel) at $($paths[0].configuredRefreshHz) Hz over $($paths[0].connection)" } else { 'no display-presentation.json' })

    # A grant of bonus items the account tool queued: the Status sentence and the Game log line that report it.
    $grantResult = [FGOLocalPlatform.MainWindow].GetMethod('GrantResult', [Reflection.BindingFlags]'NonPublic, Static')
    $two = [System.Text.Json.Nodes.JsonNode]::Parse('{"ok": true, "queued": true, "grants": [{"name": "Saint Quartz", "applied": 100}, {"name": "Golden Fruit", "applied": 7}]}')
    $one = [System.Text.Json.Nodes.JsonNode]::Parse('{"ok": true, "queued": true, "grants": [{"name": "Saint Quartz", "applied": 30}]}')
    $granted = $grantResult.Invoke($null, @($two, 'Artoria', 5))
    $grantedOne = $grantResult.Invoke($null, @($one, 'Artoria', 5))
    Test-Result 'grant result' ($granted.Item1 -ceq 'Sent 2 bonus items to the Present Box of Artoria. Open the Present Box in the game to claim them.' -and $granted.Item2 -ceq 'Granted 2 bonus items to account 5: Saint Quartz 100, Golden Fruit 7' -and $grantedOne.Item1 -ceq 'Sent 1 bonus item to the Present Box of Artoria. Open the Present Box in the game to claim it.') "$($granted.Item1) | $($granted.Item2) | $($grantedOne.Item1)"
}
catch {
    Test-Result 'run' $false ($_ | Out-String)
}
finally {
    if ($KeepFiles) { Write-Host "Kept $work" } elseif (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}

$sink = Get-DialogSink
Test-Result 'no window shown' ($sink.Shown -eq 0) $sink.Detail
if ($script:failed -gt 0) { Write-Host "$script:failed check(s) failed."; exit 1 }
Write-Host 'Every check passed.'
exit 0
