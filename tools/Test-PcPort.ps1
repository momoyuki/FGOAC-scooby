<#
Checks the cabinet patches without starting the launcher or the game.

  pwsh -File tools\Test-PcPort.ps1 [-GameExe D:\games\FGOA\App\ago.exe] [-GameExe1151 <his 1.10 package>\App\ago.exe]

Through the launcher assembly (src\bin\Release, so run build.cmd first) it reads the ago.exe of each build of
the table, 11.00 (-GameExe) and 11.51 (-GameExe1151), read only: the build header of the file equals the table's
header for that build, and every site of the table holds that build's original bytes at the file offset its
address there maps to, with a replacement of the same length. The PC play switches read their defaults
(continue timer and popup timers on, GP purchase, story timer and menu timer off) and read back what
is saved.

A fake image, a PE header and the original bytes of every site, is then patched as plain bytes through the
patcher's read and write functions, with no process: every switch on writes every site but one already
patched, under the 11.00 header and under the 11.51 header with its own original bytes; 11.00 bytes under the
11.51 header refuse the GP purchase switch, whose first site differs; the menu timer site holding other bytes on
11.51, as Cloud23333's high-FPS countdown table may leave it, refuses that switch alone; under a header that is
not a build of the table every site is found by its signature and written, and a second run finds each already
patched, while a signature missing or found twice turns off its switch alone in one line; for each switch in
turn, its last site holding other bytes leaves every site of that switch unwritten while the other switches are
written; a write that does not hold puts back the sites of its switch written before it, and a put-back that
does not hold is logged.

Each build's ago.exe, laid out as its image, is patched the same way: under its own header every site is
written at the table's address; under a header the table does not know every signature matches exactly once,
at the table's address of that build; for each switch, a byte of its last site's signature changed or the
signature copied elsewhere turns that switch off alone; the menu timer site rewritten refuses that switch alone.

It then starts a child pwsh that holds a fake image: a PE header and the original bytes of every site, in
execute-read memory it allocates, and runs the patcher against that process with the table moved into it.
Every switch off writes nothing; under a header that is not a build of the table the sites are found by their
signatures in the child's executable section and written, and with one signature missing its switch alone is
off; a site already patched
is kept, a site with other bytes refuses its whole switch and the other switches are written; a second run
writes nothing; a switch turned on later writes only its own sites; the page protection is back to
execute-read after each run. The child is stopped at the end.

Exit codes: 0 every check passed, 1 a check failed, 2 a prerequisite is missing.
#>
[CmdletBinding()]
param(
    [string]$GameExe = 'D:\FGOA\App\ago.exe',
    [string]$GameExe1151 = "$env:FGOAC_CLOUD_UPDATE\payload\payload\App\ago.exe"
)
$ErrorActionPreference = 'Stop'

$repository = Split-Path -Parent $PSScriptRoot
$assembly = Join-Path $repository 'src\bin\Release\net6.0-windows\FGOLocalPlatform.dll'
if ($PSVersionTable.PSVersion.Major -lt 7) { Write-Host 'Run this with pwsh 7: the launcher assembly targets .NET 6.'; exit 2 }
if (!(Test-Path -LiteralPath $assembly)) { Write-Host "Missing $assembly. Run build.cmd first."; exit 2 }
# Every message box, dialog and window of the launcher goes to the sink, never to the screen.
. (Join-Path $PSScriptRoot 'DialogSink.ps1'); Enable-DialogSink $assembly
foreach ($exe in @($GameExe, $GameExe1151)) { if (!(Test-Path -LiteralPath $exe)) { Write-Host "Missing $exe."; exit 2 } }

$script:failed = 0
function Test-Result([string]$Name, [bool]$Passed, [string]$Detail) {
    Write-Host ("{0} {1}: {2}" -f $(if ($Passed) { 'PASS' } else { 'FAIL' }), $Name, $Detail)
    if (!$Passed) { $script:failed++ }
}

function Format-Bytes([byte[]]$Bytes) { return (($Bytes | ForEach-Object { $_.ToString('x2') }) -join ' ') }

function Read-FileBytes([IO.FileStream]$Stream, [long]$Offset, [int]$Count) {
    $buffer = [byte[]]::new($Count)
    [void]$Stream.Seek($Offset, 'Begin')
    $read = 0
    while ($read -lt $Count) {
        $n = $Stream.Read($buffer, $read, $Count - $read)
        if ($n -le 0) { throw "end of file at $Offset" }
        $read += $n
    }
    return , $buffer
}

$child = $null
$childScript = Join-Path ([IO.Path]::GetTempPath()) ('fgoac-pcport-target-' + [guid]::NewGuid().ToString('N') + '.ps1')
try {
    [void][Reflection.Assembly]::LoadFrom($assembly)
    $patches = [FGOLocalPlatform.CabinetPatches]
    $patcher = [FGOLocalPlatform.CabinetPatcher]
    $switchType = [FGOLocalPlatform.PcPortSwitches]
    $table = $patches::Table
    $builds = @($table.Builds)
    $sites = @($table.Sites)
    $headers = ($builds | ForEach-Object { '{0} 0x{1:X} 0x{2:X}' -f $_.Name, $_.TimeDateStamp, $_.SizeOfImage }) -join ', '
    Test-Result 'table builds' ($headers -ceq '11.00 0x643F4ECD 0x2034000, 11.51 0x65C5AF1E 0x2039000' -and $table.ImageBase -eq 0x140000000) "$headers, image base 0x$($table.ImageBase.ToString('X'))"
    $columns = @($sites | Where-Object { $_.Address.Count -ne $builds.Count -or $_.Original.Count -ne $builds.Count })
    Test-Result 'table columns' ($columns.Count -eq 0) "$($columns.Count) of $($sites.Count) sites without an address and original bytes for each build"

    # The table against each build's game file on disk.
    $exes = @($GameExe, $GameExe1151)
    for ($b = 0; $b -lt $builds.Count; $b++) {
        $build = $builds[$b]
        $stream = [IO.FileStream]::new($exes[$b], 'Open', 'Read', 'ReadWrite, Delete')
        try {
            $dos = Read-FileBytes $stream 0 0x40
            $peOffset = [BitConverter]::ToInt32($dos, 0x3C)
            $pe = Read-FileBytes $stream $peOffset 0x108
            $timeDateStamp = [BitConverter]::ToUInt32($pe, 8)
            $sectionCount = [BitConverter]::ToUInt16($pe, 6)
            $optionalSize = [BitConverter]::ToUInt16($pe, 20)
            $imageBase = [BitConverter]::ToInt64($pe, 24 + 24)
            $sizeOfImage = [BitConverter]::ToUInt32($pe, 24 + 56)
            Test-Result "build header $($build.Name)" ($timeDateStamp -eq $build.TimeDateStamp -and $sizeOfImage -eq $build.SizeOfImage -and $imageBase -eq $table.ImageBase) ("{0}: TimeDateStamp 0x{1:X}, SizeOfImage 0x{2:X}, ImageBase 0x{3:X}; table: 0x{4:X}, 0x{5:X}, 0x{6:X}" -f $exes[$b], $timeDateStamp, $sizeOfImage, $imageBase, $build.TimeDateStamp, $build.SizeOfImage, $table.ImageBase)
            $sectionTable = Read-FileBytes $stream ($peOffset + 24 + $optionalSize) (40 * $sectionCount)
            $sections = for ($i = 0; $i -lt $sectionCount; $i++) {
                [pscustomobject]@{
                    Name = [Text.Encoding]::ASCII.GetString($sectionTable, 40 * $i, 8).TrimEnd([char]0)
                    VirtualSize = [BitConverter]::ToUInt32($sectionTable, 40 * $i + 8)
                    VirtualAddress = [BitConverter]::ToUInt32($sectionTable, 40 * $i + 12)
                    RawSize = [BitConverter]::ToUInt32($sectionTable, 40 * $i + 16)
                    RawPointer = [BitConverter]::ToUInt32($sectionTable, 40 * $i + 20)
                }
            }
            foreach ($site in $sites) {
                $address = $site.Address[$b]
                $original = [byte[]]$site.Original[$b]
                $rva = $address - $table.ImageBase
                $section = $sections | Where-Object { $rva -ge $_.VirtualAddress -and $rva + $original.Length -le $_.VirtualAddress + [Math]::Min($_.VirtualSize, $_.RawSize) } | Select-Object -First 1
                if ($null -eq $section) { Test-Result "site on disk $($build.Name)" $false ("{0} 0x{1:X}: not inside a section's file bytes" -f $site.Key, $address); continue }
                $offset = $rva - $section.VirtualAddress + $section.RawPointer
                $onDisk = Read-FileBytes $stream $offset $original.Length
                $same = [Linq.Enumerable]::SequenceEqual([byte[]]$onDisk, $original)
                $shape = $site.Replacement.Length -eq $original.Length -and ![Linq.Enumerable]::SequenceEqual([byte[]]$site.Replacement, $original)
                Test-Result "site on disk $($build.Name)" ($same -and $shape -and $section.Name -eq '.text') ("{0} row {1} 0x{2:X} at offset 0x{3:X} in {4}: file {5}, original {6}, replacement {7}" -f $site.Key, $site.Row, $address, $offset, $section.Name, (Format-Bytes $onDisk), (Format-Bytes $original), (Format-Bytes $site.Replacement))
            }
        }
        finally {
            $stream.Dispose()
        }
    }
    $counts = ($sites | Group-Object Key | Sort-Object Name | ForEach-Object { "$($_.Name)=$($_.Count)" }) -join ','
    Test-Result 'table keys' ($counts -eq 'noContinueTimer=2,noGpPurchase=4,noMenuTimer=1,noPopupTimers=12') $counts
    $rowOf = ($sites | ForEach-Object { "$($_.Key):$($_.Row)" } | Sort-Object -Unique) -join ','
    Test-Result 'table rows' ($rowOf -eq 'noContinueTimer:4,noGpPurchase:3a,noMenuTimer:9,noPopupTimers:5') $rowOf
    function Format-Sites([string]$Key, [int]$Build) { return @($sites | Where-Object { $_.Key -eq $Key } | ForEach-Object { '0x{0:X}: {1} -> {2}' -f $_.Address[$Build], (Format-Bytes $_.Original[$Build]), (Format-Bytes $_.Replacement) }) -join ', ' }
    $gpSites = Format-Sites 'noGpPurchase' 0
    Test-Result 'gp purchase sites 11.00' ($gpSites -ceq '0x1408878EB: 74 40 -> 66 90, 0x140889EAC: 74 09 -> eb 09, 0x14088A101: 75 1a -> 66 90, 0x14088A005: 75 30 -> 66 90') $gpSites
    $gpSites = Format-Sites 'noGpPurchase' 1
    Test-Result 'gp purchase sites 11.51' ($gpSites -ceq '0x14088B910: 74 3f -> 66 90, 0x14088D9C4: 74 09 -> eb 09, 0x14088DC23: 75 1a -> 66 90, 0x14088DB28: 75 30 -> 66 90') $gpSites
    $menuSites = Format-Sites 'noMenuTimer' 0
    Test-Result 'menu timer site 11.00' ($menuSites -ceq '0x1408864D2: ff 8f 84 00 00 00 -> 66 0f 1f 44 00 00') $menuSites
    $menuSites = Format-Sites 'noMenuTimer' 1
    Test-Result 'menu timer site 11.51' ($menuSites -ceq '0x14088A502: ff 8f 84 00 00 00 -> 66 0f 1f 44 00 00') $menuSites
    for ($b = 0; $b -lt $builds.Count; $b++) {
        $ordered = @($sites | Sort-Object { $_.Address[$b] })
        $overlaps = @(for ($i = 1; $i -lt $ordered.Count; $i++) { if ($ordered[$i - 1].Address[$b] + $ordered[$i - 1].Original[$b].Length -gt $ordered[$i].Address[$b]) { '0x{0:X}' -f $ordered[$i].Address[$b] } })
        Test-Result "no overlap $($builds[$b].Name)" ($overlaps.Count -eq 0 -and @($sites | ForEach-Object { $_.Address[$b] } | Select-Object -Unique).Count -eq 19) "$($sites.Count) sites, overlapping: $($overlaps -join ', ')"
    }

    # The PC play switches: defaults, and what is saved.
    $defaults = $switchType::Defaults
    Test-Result 'switch defaults' ($defaults.NoContinueTimer -and $defaults.NoPopupTimers -and !$defaults.NoGpPurchase -and !$defaults.NoStoryTimer -and !$defaults.NoMenuTimer) "$defaults"
    $read = $switchType::Read([Text.Json.Nodes.JsonNode]::Parse('{"displayMode":"windowed"}'))
    Test-Result 'switches not saved' ($read -eq $defaults) "$read"
    $read = $switchType::Read([Text.Json.Nodes.JsonNode]::Parse('{"pcport":{"noContinueTimer":false,"noGpPurchase":true,"noPrintWait":true,"noMenuTimer":true}}'))
    Test-Result 'switches saved in part' (!$read.NoContinueTimer -and $read.NoPopupTimers -and $read.NoGpPurchase -and !$read.NoStoryTimer -and $read.NoMenuTimer -and $read.ToString() -notmatch 'PrintWait') "$read"
    $settings = [Text.Json.Nodes.JsonNode]::Parse('{"displayMode":"windowed"}')
    $saved = $switchType::new($false, $true, $true, $true, $true)
    $saved.Write($settings)
    $json = $settings.ToJsonString()
    Test-Result 'switches written' ($json -ceq '{"displayMode":"windowed","pcport":{"noContinueTimer":false,"noPopupTimers":true,"noGpPurchase":true,"noStoryTimer":true,"noMenuTimer":true}}' -and $switchType::Read($settings) -eq $saved) $json
    $keysOn = @('noContinueTimer', 'noPopupTimers', 'noGpPurchase', 'noStoryTimer', 'noPrintWait', 'noMenuTimer', 'other' | Where-Object { $saved.IsOn($_) }) -join ','
    Test-Result 'switch keys' ($keysOn -eq 'noPopupTimers,noGpPurchase,noStoryTimer,noMenuTimer') $keysOn
    $missing = Join-Path ([IO.Path]::GetTempPath()) ('fgoac-pcport-missing-' + [guid]::NewGuid().ToString('N') + '.json')
    Test-Result 'switches, no settings file' ($switchType::Load($missing) -eq $defaults) 'Load of a missing file gives the defaults'

    # A fake image of a build: header at 0 with one executable section at 0x1000, site i at 0x1000 + 0x10 * i holding
    # that build's original bytes and then its marker f1 i, which the moved table's signatures find.
    $original = @(for ($b = 0; $b -lt $builds.Count; $b++) { , [byte[][]]@($sites | ForEach-Object { , [byte[]]$_.Original[$b] }) })
    function Set-Header([byte[]]$Bytes, [uint32]$TimeDateStamp, [uint32]$SizeOfImage) {
        $copy = [byte[]]$Bytes.Clone()
        [BitConverter]::GetBytes($TimeDateStamp).CopyTo($copy, 0x80 + 8)
        [BitConverter]::GetBytes($SizeOfImage).CopyTo($copy, 0x80 + 24 + 56)
        return , $copy
    }
    function New-Image([int]$Build) {
        $bytes = [byte[]]::new(0x2000)
        $bytes[0] = 0x4D; $bytes[1] = 0x5A
        [BitConverter]::GetBytes([int]0x80).CopyTo($bytes, 0x3C)
        $bytes[0x80] = 0x50; $bytes[0x81] = 0x45
        $bytes[0x86] = 1
        [BitConverter]::GetBytes([uint16]0xF0).CopyTo($bytes, 0x80 + 20)
        [BitConverter]::GetBytes([uint32]0x1000).CopyTo($bytes, 0x188 + 8)
        [BitConverter]::GetBytes([uint32]0x1000).CopyTo($bytes, 0x188 + 12)
        [BitConverter]::GetBytes([uint32]0x60000020).CopyTo($bytes, 0x188 + 36)
        for ($i = 0; $i -lt $sites.Count; $i++) {
            $original[$Build][$i].CopyTo($bytes, 0x1000 + 0x10 * $i)
            $bytes[0x1000 + 0x10 * $i + $original[$Build][$i].Length] = 0xF1
            $bytes[0x1000 + 0x10 * $i + $original[$Build][$i].Length + 1] = $i
        }
        return , (Set-Header $bytes $builds[$Build].TimeDateStamp $builds[$Build].SizeOfImage)
    }
    # The table with every site of every build moved to the fake image's site at base.
    function Move-Table([long]$Base) {
        $moved = for ($i = 0; $i -lt $sites.Count; $i++) {
            $signature = ((@('..') * $original[0][$i].Length) + 'f1' + $i.ToString('x2')) -join ' '
            [FGOLocalPlatform.CabinetSite]::new($sites[$i].Row, $sites[$i].Key, [long[]]@(foreach ($b in $builds) { $Base + 0x1000 + 0x10 * $i }), $sites[$i].Original, $sites[$i].Replacement, $signature, 0)
        }
        return [FGOLocalPlatform.CabinetTable]::new($Base, $table.Builds, [FGOLocalPlatform.CabinetSite[]]@($moved))
    }
    $image = New-Image 0
    $image1151 = New-Image 1
    function Get-Site([byte[]]$Bytes, [int]$Index) { return , [byte[]]$Bytes[(0x1000 + 0x10 * $Index)..(0x1000 + 0x10 * $Index + $original[0][$Index].Length - 1)] }
    function Get-Outcomes($Run) { return ($Run.Sites | ForEach-Object { $_.Outcome.ToString() }) -join ',' }
    $continueIndex = [Array]::FindIndex($sites, [Predicate[object]] { param($s) $s.Key -eq 'noContinueTimer' })

    # The image as plain bytes at 0x10000, read and written through the patcher's functions: no process.
    $memoryBase = 0x10000
    $memory = $null
    $read = [Func[long, int, byte[]]] { param($address, $count) return , [byte[]]$memory[($address - $memoryBase)..($address - $memoryBase + $count - 1)] }
    $write = [Func[long, byte[], string]] { param($address, $bytes) $bytes.CopyTo($memory, $address - $memoryBase); return [NullString]::Value }
    $inMemory = Move-Table $memoryBase
    $at = @(for ($i = 0; $i -lt $sites.Count; $i++) { $memoryBase + 0x1000 + 0x10 * $i })
    $allOn = $switchType::new($true, $true, $true, $true, $true)
    $keys = @($sites | Select-Object -ExpandProperty Key -Unique)
    function Get-Unwritten([byte[]]$Bytes, [int[]]$Except) { return @(for ($i = 0; $i -lt $sites.Count; $i++) { if ($Except -notcontains $i -and ![Linq.Enumerable]::SequenceEqual([byte[]](Get-Site $Bytes $i), [byte[]]$sites[$i].Replacement)) { '0x{0:X}' -f $at[$i] } }) }
    function Get-Summary([string]$Except) { return @($keys | Where-Object { $_ -ne $Except } | ForEach-Object { $key = $_; $count = @($sites | Where-Object { $_.Key -eq $key }).Count; '{0} (row {1}): {2} patched, 0 already patched, 0 refused.' -f $key, ($sites | Where-Object { $_.Key -eq $key } | Select-Object -First 1).Row, $count }) }

    # Every switch on, one site already patched: every other site written.
    $memory = [byte[]]$image.Clone()
    $sites[$continueIndex].Replacement.CopyTo($memory, 0x1000 + 0x10 * $continueIndex)
    $run = $patcher::Apply($inMemory, $allOn, $read, $write)
    $expected = @(for ($i = 0; $i -lt $sites.Count; $i++) { if ($i -eq $continueIndex) { 'AlreadyPatched' } else { 'Patched' } }) -join ','
    $unwritten = Get-Unwritten $memory @()
    $said = @($keys | ForEach-Object { $key = $_; $count = @($sites | Where-Object { $_.Key -eq $key }).Count; $already = [int]($key -eq 'noContinueTimer'); '{0} (row {1}): {2} patched, {3} already patched, 0 refused.' -f $key, ($sites | Where-Object { $_.Key -eq $key } | Select-Object -First 1).Row, ($count - $already), $already })
    Test-Result 'functions, every switch on' ((Get-Outcomes $run) -eq $expected -and $unwritten.Count -eq 0 -and ($run.Lines -join '|') -ceq ((@('ago.exe is build 11.00 (0x643F4ECD, image size 0x2034000): writing the cabinet patches the PC play switches turn on.') + $said) -join '|')) ("not written: $($unwritten -join ', '); " + ($run.Lines -join ' | '))

    # The 11.51 header with its own original bytes: every site written.
    $memory = [byte[]]$image1151.Clone()
    $run = $patcher::Apply($inMemory, $allOn, $read, $write)
    $unwritten = Get-Unwritten $memory @()
    Test-Result 'functions, 11.51 every switch on' ((Get-Outcomes $run) -eq ((@('Patched') * $sites.Count) -join ',') -and $unwritten.Count -eq 0 -and ($run.Lines -join '|') -ceq ((@('ago.exe is build 11.51 (0x65C5AF1E, image size 0x2039000): writing the cabinet patches the PC play switches turn on.') + (Get-Summary '')) -join '|')) ("not written: $($unwritten -join ', '); " + ($run.Lines -join ' | '))

    # 11.00 bytes under the 11.51 header: the first GP purchase site holds 74 40, not 74 3f; that switch alone is refused.
    $gpIndexes = @(for ($i = 0; $i -lt $sites.Count; $i++) { if ($sites[$i].Key -eq 'noGpPurchase') { $i } })
    $memory = Set-Header $image $builds[1].TimeDateStamp $builds[1].SizeOfImage
    $run = $patcher::Apply($inMemory, $allOn, $read, $write)
    $expected = @(for ($i = 0; $i -lt $sites.Count; $i++) { if ($i -eq $gpIndexes[0]) { 'Refused' } elseif ($gpIndexes -contains $i) { 'Skipped' } else { 'Patched' } }) -join ','
    $kept = @($gpIndexes | Where-Object { ![Linq.Enumerable]::SequenceEqual([byte[]](Get-Site $memory $_), [byte[]](Get-Site $image $_)) })
    $gpLines = @($run.Lines | Where-Object { $_ -clike 'noGpPurchase *' })
    Test-Result 'functions, 11.00 bytes under the 11.51 header' ((Get-Outcomes $run) -eq $expected -and (Get-Unwritten $memory $gpIndexes).Count -eq 0 -and $kept.Count -eq 0 -and ($gpLines -join '|') -ceq (('noGpPurchase (row 3a): 0x{0:x} holds 74 40, not 74 3f; left as it is.' -f $at[$gpIndexes[0]]), 'noGpPurchase (row 3a): 1 of 4 refused, none written.' -join '|')) ($run.Lines -join ' | ')

    # 11.51 with the menu timer site rewritten, as Cloud23333's high-FPS countdown table may leave it: that switch alone
    # is skipped and its site left as it is.
    $menuIndex = [Array]::FindIndex($sites, [Predicate[object]] { param($s) $s.Key -eq 'noMenuTimer' })
    $memory = [byte[]]$image1151.Clone()
    $his = [byte[]](0xe9, 0x11, 0x22, 0x33, 0x44, 0x90)
    $his.CopyTo($memory, 0x1000 + 0x10 * $menuIndex)
    $run = $patcher::Apply($inMemory, $allOn, $read, $write)
    $expected = @(for ($i = 0; $i -lt $sites.Count; $i++) { if ($i -eq $menuIndex) { 'Refused' } else { 'Patched' } }) -join ','
    $menuLines = @($run.Lines | Where-Object { $_ -clike 'noMenuTimer *' })
    Test-Result 'functions, 11.51 menu timer site changed by the high-FPS table' ((Get-Outcomes $run) -eq $expected -and (Get-Unwritten $memory @($menuIndex)).Count -eq 0 -and (Format-Bytes (Get-Site $memory $menuIndex)) -eq (Format-Bytes $his) -and ($menuLines -join '|') -ceq (('noMenuTimer (row 9): 0x{0:x} holds e9 11 22 33 44 90, not ff 8f 84 00 00 00; left as it is.' -f $at[$menuIndex]), 'noMenuTimer (row 9): 1 of 1 refused, none written.' -join '|')) ($run.Lines -join ' | ')

    # A header that is not a build of the table: every site found by its signature and written.
    $memory = Set-Header $image ($builds[0].TimeDateStamp + 1) $builds[0].SizeOfImage
    $run = $patcher::Apply($inMemory, $allOn, $read, $write)
    $found = @(for ($i = 0; $i -lt $sites.Count; $i++) { if ($run.Sites[$i].Address -ne $at[$i]) { '0x{0:X} at 0x{1:X}' -f $at[$i], $run.Sites[$i].Address } })
    Test-Result 'functions, unknown build' ((Get-Outcomes $run) -eq ((@('Patched') * $sites.Count) -join ',') -and $found.Count -eq 0 -and (Get-Unwritten $memory @()).Count -eq 0 -and ($run.Lines -join '|') -ceq ((@('ago.exe is not a build the launcher knows (its header says 0x643F4ECE, 0x2034000): finding the cabinet patch sites by their signatures.') + (Get-Summary '')) -join '|')) ("found elsewhere: $($found -join ', '); " + ($run.Lines -join ' | '))
    $run = $patcher::Apply($inMemory, $allOn, $read, $write)
    Test-Result 'functions, unknown build again' ((Get-Outcomes $run) -eq ((@('AlreadyPatched') * $sites.Count) -join ',')) (Get-Outcomes $run)

    # The same header with the marker of the menu timer site gone, then with it twice: that switch off, one sentence,
    # the others written.
    $marker = 0x1000 + 0x10 * $menuIndex + $original[0][$menuIndex].Length
    foreach ($twice in @($false, $true)) {
        $memory = Set-Header $image ($builds[0].TimeDateStamp + 1) $builds[0].SizeOfImage
        if ($twice) { $memory[0x1E00] = 0xF1; $memory[0x1E01] = $menuIndex } else { $memory[$marker] = 0 }
        $before = [byte[]]$memory.Clone()
        $run = $patcher::Apply($inMemory, $allOn, $read, $write)
        $expected = @(for ($i = 0; $i -lt $sites.Count; $i++) { if ($i -eq $menuIndex) { 'Refused' } else { 'Patched' } }) -join ','
        $menuLines = @($run.Lines | Where-Object { $_ -clike 'noMenuTimer *' })
        Test-Result "functions, unknown build, menu timer signature $(if ($twice) { 'twice' } else { 'missing' })" ((Get-Outcomes $run) -eq $expected -and $run.Sites[$menuIndex].Address -eq 0 -and (Get-Unwritten $memory @($menuIndex)).Count -eq 0 -and [Linq.Enumerable]::SequenceEqual([byte[]](Get-Site $memory $menuIndex), [byte[]](Get-Site $before $menuIndex)) -and ($menuLines -join '|') -ceq 'noMenuTimer (row 9): off, 1 of 1 sites not found exactly once in this ago.exe; nothing written.') ($run.Lines -join ' | ')
    }

    # For each switch, its last site holding other bytes: none of that switch's sites written, every other switch's are.
    foreach ($key in $keys) {
        $indexes = @(for ($i = 0; $i -lt $sites.Count; $i++) { if ($sites[$i].Key -eq $key) { $i } })
        $last = $indexes[-1]
        $memory = [byte[]]$image.Clone()
        for ($b = 0; $b -lt $original[0][$last].Length; $b++) { $memory[0x1000 + 0x10 * $last + $b] = 0xCC }
        $foreign = [byte[]](Get-Site $memory $last)
        $run = $patcher::Apply($inMemory, $allOn, $read, $write)
        $expected = @(for ($i = 0; $i -lt $sites.Count; $i++) { if ($i -eq $last) { 'Refused' } elseif ($sites[$i].Key -eq $key) { 'Skipped' } else { 'Patched' } }) -join ','
        $wrong = @(for ($i = 0; $i -lt $sites.Count; $i++) {
                $want = if ($i -eq $last) { $foreign } elseif ($sites[$i].Key -eq $key) { $original[0][$i] } else { $sites[$i].Replacement }
                if (![Linq.Enumerable]::SequenceEqual([byte[]](Get-Site $memory $i), [byte[]]$want)) { '0x{0:X}' -f $at[$i] }
            })
        $row = $sites[$last].Row
        $said = @($run.Lines | Where-Object { $_ -ceq ('{0} (row {1}): 0x{2:x} holds {3}, not {4}; left as it is.' -f $key, $row, $at[$last], (Format-Bytes $foreign), (Format-Bytes $original[0][$last])) -or $_ -ceq ('{0} (row {1}): 1 of {2} refused, none written.' -f $key, $row, $indexes.Count) })
        Test-Result "functions, switch refused whole: $key" ((Get-Outcomes $run) -eq $expected -and $wrong.Count -eq 0 -and $said.Count -eq 2) ("wrong bytes: $($wrong -join ', '); " + ($run.Lines -join ' | '))
    }

    # The last popup site's write does not hold: the popup sites written before it are put back; then the first one's
    # put-back does not hold either and it stays written.
    $popups = @(for ($i = 0; $i -lt $sites.Count; $i++) { if ($sites[$i].Key -eq 'noPopupTimers') { $i } })
    $failAt = $at[$popups[-1]]
    $stuckAt = 0
    $failing = [Func[long, byte[], string]] {
        param($address, $bytes)
        if ($address -eq $failAt -or ($address -eq $stuckAt -and [Linq.Enumerable]::SequenceEqual($bytes, $original[0][$popups[0]]))) { return 'refused by the test' }
        $bytes.CopyTo($memory, $address - $memoryBase)
        return [NullString]::Value
    }
    foreach ($stuck in @($false, $true)) {
        $stuckAt = if ($stuck) { $at[$popups[0]] } else { 0 }
        $memory = [byte[]]$image.Clone()
        $run = $patcher::Apply($inMemory, $allOn, $read, $failing)
        $expected = @(for ($i = 0; $i -lt $sites.Count; $i++) { if ($i -eq $popups[-1]) { 'Refused' } elseif ($stuck -and $i -eq $popups[0]) { 'Patched' } elseif ($sites[$i].Key -eq 'noPopupTimers') { 'Skipped' } else { 'Patched' } }) -join ','
        $wrong = @(for ($i = 0; $i -lt $sites.Count; $i++) {
                $want = if ($sites[$i].Key -ne 'noPopupTimers' -or ($stuck -and $i -eq $popups[0])) { $sites[$i].Replacement } else { $original[0][$i] }
                if (![Linq.Enumerable]::SequenceEqual([byte[]](Get-Site $memory $i), [byte[]]$want)) { '0x{0:X}' -f $at[$i] }
            })
        $popupLines = @($run.Lines | Where-Object { $_ -clike 'noPopupTimers *' })
        $said = @('noPopupTimers (row 5): 0x{0:x} could not be written (refused by the test); it now holds {1}.' -f $failAt, (Format-Bytes $original[0][$popups[-1]]))
        $said += if ($stuck) { 'noPopupTimers (row 5): 0x{0:x} could not be put back (refused by the test); it now holds {1}.' -f $at[$popups[0]], (Format-Bytes $sites[$popups[0]].Replacement); 'noPopupTimers (row 5): 1 patched, 0 already patched, 1 refused.' } else { 'noPopupTimers (row 5): 1 of 12 refused, none written.' }
        Test-Result "functions, a write that does not hold$(if ($stuck) { ', its put-back neither' })" ((Get-Outcomes $run) -eq $expected -and $wrong.Count -eq 0 -and ($popupLines -join '|') -ceq ($said -join '|')) ("wrong bytes: $($wrong -join ', '); " + ($popupLines -join ' | '))
    }

    # Each build's ago.exe laid out as its image, read and written through the patcher's functions. Under its own
    # header every site is written at the table's address; under a header the table does not know every site is
    # found there by its signature and written, and a second run finds every site already patched; a signature
    # broken or repeated turns off its switch alone; the menu timer site rewritten refuses that switch alone.
    function Read-Image([string]$Path) {
        $raw = [IO.File]::ReadAllBytes($Path)
        $pe = [BitConverter]::ToInt32($raw, 0x3C)
        $bytes = [byte[]]::new([BitConverter]::ToUInt32($raw, $pe + 24 + 56))
        [Array]::Copy($raw, 0, $bytes, 0, [BitConverter]::ToUInt32($raw, $pe + 24 + 60))
        $sectionTable = $pe + 24 + [BitConverter]::ToUInt16($raw, $pe + 20)
        for ($i = 0; $i -lt [BitConverter]::ToUInt16($raw, $pe + 6); $i++) {
            $entry = $sectionTable + 40 * $i
            $length = [Math]::Min([BitConverter]::ToUInt32($raw, $entry + 8), [BitConverter]::ToUInt32($raw, $entry + 16))
            [Array]::Copy($raw, [BitConverter]::ToUInt32($raw, $entry + 20), $bytes, [BitConverter]::ToUInt32($raw, $entry + 12), $length)
        }
        return , $bytes
    }
    $real = $null
    $realRead = [Func[long, int, byte[]]] {
        param($address, $count)
        $from = $address - $table.ImageBase
        if ($from -lt 0 -or $from + $count -gt $real.Length) { return $null }
        $buffer = [byte[]]::new($count)
        [Array]::Copy($real, $from, $buffer, 0, $count)
        return , $buffer
    }
    $realWrite = [Func[long, byte[], string]] { param($address, $bytes) [Array]::Copy($bytes, 0, $real, $address - $table.ImageBase, $bytes.Length); return [NullString]::Value }
    function Get-RealWrong([int]$Build, [int[]]$Except) {
        return @(for ($i = 0; $i -lt $sites.Count; $i++) {
                $address = $sites[$i].Address[$Build]
                $now = [byte[]]::new($sites[$i].Replacement.Length)
                [Array]::Copy($real, $address - $table.ImageBase, $now, 0, $now.Length)
                $want = if ($Except -contains $i) { $original[$Build][$i] } else { $sites[$i].Replacement }
                if ($run.Sites[$i].Address -ne $(if ($Except -contains $i) { 0 } else { $address }) -or ![Linq.Enumerable]::SequenceEqual($now, [byte[]]$want)) { '{0} 0x{1:X}: found 0x{2:X}, holds {3}' -f $sites[$i].Key, $address, $run.Sites[$i].Address, (Format-Bytes $now) }
            })
    }
    $pristine = @((Read-Image $GameExe), (Read-Image $GameExe1151))
    for ($b = 0; $b -lt $builds.Count; $b++) {
        $name = $builds[$b].Name
        $peAt = [BitConverter]::ToInt32($pristine[$b], 0x3C)
        $real = [byte[]]$pristine[$b].Clone()
        $run = $patcher::Apply($table, $allOn, $realRead, $realWrite)
        $wrong = Get-RealWrong $b @()
        Test-Result "ago.exe $name, its header" ((Get-Outcomes $run) -eq ((@('Patched') * $sites.Count) -join ',') -and $wrong.Count -eq 0 -and $run.Lines[0] -clike "ago.exe is build $name *") ("wrong: $($wrong -join ', '); " + ($run.Lines -join ' | '))

        $unknown = [byte[]]$pristine[$b].Clone()
        [BitConverter]::GetBytes([uint32]0x7FFFFFFF).CopyTo($unknown, $peAt + 8)
        $real = [byte[]]$unknown.Clone()
        $run = $patcher::Apply($table, $allOn, $realRead, $realWrite)
        $wrong = Get-RealWrong $b @()
        Test-Result "ago.exe $name, unknown header" ((Get-Outcomes $run) -eq ((@('Patched') * $sites.Count) -join ',') -and $wrong.Count -eq 0 -and ($run.Lines -join '|') -ceq ((@(('ago.exe is not a build the launcher knows (its header says 0x7FFFFFFF, 0x{0:X}): finding the cabinet patch sites by their signatures.' -f $builds[$b].SizeOfImage)) + (Get-Summary '')) -join '|')) ("wrong: $($wrong -join ', '); " + ($run.Lines -join ' | '))
        $run = $patcher::Apply($table, $allOn, $realRead, $realWrite)
        Test-Result "ago.exe $name, unknown header again" ((Get-Outcomes $run) -eq ((@('AlreadyPatched') * $sites.Count) -join ',')) (Get-Outcomes $run)

        # Each switch in turn: the first known byte of its last site's signature changed, then that signature's
        # bytes copied to the start of .text.
        foreach ($key in $keys) {
            $last = @(for ($i = 0; $i -lt $sites.Count; $i++) { if ($sites[$i].Key -eq $key) { $i } })[-1]
            $tokens = $sites[$last].Signature -split ' '
            $start = $sites[$last].Address[$b] - $table.ImageBase - $sites[$last].At
            $known = [Array]::FindIndex($tokens, [Predicate[string]] { param($t) $t -ne '..' })
            foreach ($how in @('broken', 'repeated')) {
                $real = [byte[]]$unknown.Clone()
                if ($how -eq 'broken') { $real[$start + $known] = $real[$start + $known] -bxor 0xFF } else { [Array]::Copy($real, $start, $real, 0x1000, $tokens.Count) }
                $run = $patcher::Apply($table, $allOn, $realRead, $realWrite)
                $indexes = @(for ($i = 0; $i -lt $sites.Count; $i++) { if ($sites[$i].Key -eq $key) { $i } })
                $expected = @(for ($i = 0; $i -lt $sites.Count; $i++) { if ($i -eq $last) { 'Refused' } elseif ($indexes -contains $i) { 'Skipped' } else { 'Patched' } }) -join ','
                $wrong = @(for ($i = 0; $i -lt $sites.Count; $i++) {
                        $address = $sites[$i].Address[$b]
                        $now = [byte[]]::new($sites[$i].Replacement.Length)
                        [Array]::Copy($real, $address - $table.ImageBase, $now, 0, $now.Length)
                        $want = if ($indexes -contains $i) { $original[$b][$i] } else { $sites[$i].Replacement }
                        if (![Linq.Enumerable]::SequenceEqual($now, [byte[]]$want)) { '0x{0:X}' -f $address }
                    })
                $said = '{0} (row {1}): off, 1 of {2} sites not found exactly once in this ago.exe; nothing written.' -f $key, $sites[$last].Row, $indexes.Count
                Test-Result "ago.exe $name, unknown header, $key signature $how" ((Get-Outcomes $run) -eq $expected -and $run.Sites[$last].Address -eq 0 -and $wrong.Count -eq 0 -and @($run.Lines | Where-Object { $_ -clike "$key *" }).Count -eq 1 -and $run.Lines -ccontains $said) ("wrong bytes: $($wrong -join ', '); " + ($run.Lines -join ' | '))
            }
        }

        # The menu timer site rewritten, as Cloud23333's high-FPS countdown table may leave it: found, refused alone.
        $menuAddress = $sites[$menuIndex].Address[$b]
        $real = [byte[]]$unknown.Clone()
        $his.CopyTo($real, $menuAddress - $table.ImageBase)
        $run = $patcher::Apply($table, $allOn, $realRead, $realWrite)
        $expected = @(for ($i = 0; $i -lt $sites.Count; $i++) { if ($i -eq $menuIndex) { 'Refused' } else { 'Patched' } }) -join ','
        $menuLines = @($run.Lines | Where-Object { $_ -clike 'noMenuTimer *' })
        Test-Result "ago.exe $name, unknown header, menu timer site changed" ((Get-Outcomes $run) -eq $expected -and $run.Sites[$menuIndex].Address -eq $menuAddress -and ($menuLines -join '|') -ceq (('noMenuTimer (row 9): 0x{0:x} holds e9 11 22 33 44 90, not ff 8f 84 00 00 00; left as it is.' -f $menuAddress), 'noMenuTimer (row 9): 1 of 1 refused, none written.' -join '|')) ($run.Lines -join ' | ')
    }
    $real = $null; $pristine = $null; $unknown = $null

    # A fake image in a child process: the same bytes in execute-read memory the child allocates.
    Set-Content -LiteralPath $childScript -Encoding UTF8 -Value @'
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class FakeImage
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Region { public IntPtr BaseAddress; public IntPtr AllocationBase; public uint AllocationProtect; public ushort PartitionId; public UIntPtr RegionSize; public uint State; public uint Protect; public uint Type; }
    [DllImport("kernel32.dll", SetLastError = true)] public static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint type, uint protect);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint protect, out uint old);
    [DllImport("kernel32.dll")] public static extern UIntPtr VirtualQuery(IntPtr address, out Region region, UIntPtr size);
    public static uint Protection(IntPtr address) { Region region; VirtualQuery(address, out region, (UIntPtr)Marshal.SizeOf(typeof(Region))); return region.Protect; }
}
"@
$size = [UIntPtr]::new(0x2000)
$base = [FakeImage]::VirtualAlloc([IntPtr]::Zero, $size, 0x3000, 0x04)
function Set-Image([string]$Hex) {
    $old = 0
    [void][FakeImage]::VirtualProtect($base, $size, 0x04, [ref]$old)
    $bytes = [Convert]::FromHexString($Hex)
    [Runtime.InteropServices.Marshal]::Copy($bytes, 0, $base, $bytes.Length)
    [void][FakeImage]::VirtualProtect($base, $size, 0x20, [ref]$old)
}
Set-Image ([Console]::In.ReadLine())
[Console]::Out.WriteLine($base.ToInt64().ToString())
[Console]::Out.Flush()
while ($null -ne ($line = [Console]::In.ReadLine())) {
    if ($line -eq 'dump') {
        $bytes = [byte[]]::new(0x2000)
        [Runtime.InteropServices.Marshal]::Copy($base, $bytes, 0, 0x2000)
        $protect = @(0, 0x1000) | ForEach-Object { '0x{0:X}' -f [FakeImage]::Protection([IntPtr]::Add($base, $_)) }
        [Console]::Out.WriteLine(($protect -join ',') + ' ' + [Convert]::ToHexString($bytes))
    }
    elseif ($line.StartsWith('set ')) { Set-Image $line.Substring(4); [Console]::Out.WriteLine('ok') }
    else { break }
    [Console]::Out.Flush()
}
'@
    $start = [Diagnostics.ProcessStartInfo]::new((Get-Process -Id $PID).Path)
    foreach ($argument in @('-NoLogo', '-NoProfile', '-NonInteractive', '-File', $childScript)) { $start.ArgumentList.Add($argument) }
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.UseShellExecute = $false
    $child = [Diagnostics.Process]::Start($start)
    $child.StandardInput.WriteLine([Convert]::ToHexString($image))
    $baseAddress = [long]$child.StandardOutput.ReadLine()
    Test-Result 'fake image' ($baseAddress -gt 0) ("process {0}, image at 0x{1:X}" -f $child.Id, $baseAddress)

    function Get-Image {
        $child.StandardInput.WriteLine('dump')
        $parts = $child.StandardOutput.ReadLine() -split ' '
        return [pscustomobject]@{ Protect = $parts[0]; Bytes = [Convert]::FromHexString($parts[1]) }
    }
    function Set-Image([byte[]]$Bytes) {
        $child.StandardInput.WriteLine('set ' + [Convert]::ToHexString($Bytes))
        [void]$child.StandardOutput.ReadLine()
    }

    $moved = Move-Table $baseAddress
    $movedAt = @(for ($i = 0; $i -lt $sites.Count; $i++) { $baseAddress + 0x1000 + 0x10 * $i })
    $foreignIndex = [Array]::FindLastIndex($sites, [Predicate[object]] { param($s) $s.Key -eq 'noPopupTimers' -and $s.Original[0].Length -eq 2 })

    # Every switch off: nothing is read or written.
    $run = $patcher::Apply($child.Id, $moved, $switchType::new($false, $false, $false, $true, $false))
    $after = Get-Image
    Test-Result 'all off' ((Get-Outcomes $run) -eq ((@('Off') * $sites.Count) -join ',') -and [Linq.Enumerable]::SequenceEqual($after.Bytes, $image)) ($run.Lines -join ' | ')

    # A header that is not a build of the table: the sites found by their signatures in the child's section and
    # written; with the menu timer marker gone, that switch alone off.
    foreach ($missing in @($false, $true)) {
        $other = Set-Header $image $builds[0].TimeDateStamp ($builds[0].SizeOfImage + 0x1000)
        if ($missing) { $other[$marker] = 0 }
        Set-Image $other
        $run = $patcher::Apply($child.Id, $moved, $switchType::new($true, $true, $true, $false, $true))
        $after = Get-Image
        $skip = if ($missing) { @($menuIndex) } else { @() }
        $expected = @(for ($i = 0; $i -lt $sites.Count; $i++) { if ($skip -contains $i) { 'Refused' } else { 'Patched' } }) -join ','
        $wrong = @(for ($i = 0; $i -lt $sites.Count; $i++) { if ($run.Sites[$i].Address -ne $(if ($skip -contains $i) { 0 } else { $movedAt[$i] })) { $i } })
        Test-Result "unknown build$(if ($missing) { ', menu timer signature missing' })" ((Get-Outcomes $run) -eq $expected -and $wrong.Count -eq 0 -and (Get-Unwritten $after.Bytes $skip).Count -eq 0 -and $after.Protect -eq '0x20,0x20' -and $run.Lines[0] -ceq 'ago.exe is not a build the launcher knows (its header says 0x643F4ECD, 0x2035000): finding the cabinet patch sites by their signatures.') ($run.Lines -join ' | ')
    }

    # One continue site already patched, one popup site holding other bytes, GP purchase and print wait off: the
    # continue switch is written, the popup switch refused whole.
    $prepared = [byte[]]$image.Clone()
    $sites[$continueIndex].Replacement.CopyTo($prepared, 0x1000 + 0x10 * $continueIndex)
    $prepared[0x1000 + 0x10 * $foreignIndex] = 0xCC
    $prepared[0x1000 + 0x10 * $foreignIndex + 1] = 0xCC
    Set-Image $prepared
    $run = $patcher::Apply($child.Id, $moved, $switchType::Defaults)
    $after = Get-Image
    $expected = @(for ($i = 0; $i -lt $sites.Count; $i++) { if ($i -eq $continueIndex) { 'AlreadyPatched' } elseif ($i -eq $foreignIndex) { 'Refused' } elseif (!$switchType::Defaults.IsOn($sites[$i].Key)) { 'Off' } elseif ($sites[$i].Key -eq 'noPopupTimers') { 'Skipped' } else { 'Patched' } }) -join ','
    $written = @(for ($i = 0; $i -lt $sites.Count; $i++) {
            $now = Get-Site $after.Bytes $i
            $want = if ($i -eq $foreignIndex) { [byte[]](0xCC, 0xCC) } elseif (!$switchType::Defaults.IsOn($sites[$i].Key) -or $sites[$i].Key -eq 'noPopupTimers') { $original[0][$i] } else { $sites[$i].Replacement }
            if (![Linq.Enumerable]::SequenceEqual([byte[]]$now, [byte[]]$want)) { '0x{0:X}: {1}' -f $movedAt[$i], (Format-Bytes $now) }
        })
    Test-Result 'patched, skipped, refused' ((Get-Outcomes $run) -eq $expected -and $written.Count -eq 0) ("outcomes $(Get-Outcomes $run); wrong bytes: $($written -join ', ')")
    Test-Result 'protection restored' ($after.Protect -eq '0x20,0x20') "pages $($after.Protect) after the writes"
    $refusal = @($run.Lines | Where-Object { $_ -like '*cc cc*' })
    Test-Result 'refusal logged' ($refusal.Count -eq 1 -and $refusal[0] -like ('noPopupTimers (row 5): 0x{0:x} holds cc cc, not {1}; left as it is.' -f $movedAt[$foreignIndex], (Format-Bytes $original[0][$foreignIndex]))) ($refusal -join ' | ')
    $summary = @($run.Lines | Where-Object { $_ -notlike '*cc cc*' })
    Test-Result 'run logged' (($summary -join '|') -ceq 'ago.exe is build 11.00 (0x643F4ECD, image size 0x2034000): writing the cabinet patches the PC play switches turn on.|noContinueTimer (row 4): 1 patched, 1 already patched, 0 refused.|noPopupTimers (row 5): 1 of 12 refused, none written.|noGpPurchase (row 3a): off, nothing written.|noMenuTimer (row 9): off, nothing written.') ($summary -join ' | ')

    # The same run again writes nothing.
    $run = $patcher::Apply($child.Id, $moved, $switchType::Defaults)
    $again = Get-Image
    Test-Result 'second run' (@($run.Sites | Where-Object { $_.Outcome -eq 'Patched' }).Count -eq 0 -and @($run.Sites | Where-Object { $_.Outcome -eq 'Refused' }).Count -eq 1 -and [Linq.Enumerable]::SequenceEqual($again.Bytes, $after.Bytes)) (Get-Outcomes $run)

    # GP purchase turned on later: its four sites, nothing else.
    $run = $patcher::Apply($child.Id, $moved, $switchType::new($false, $false, $true, $false, $false))
    $gp = Get-Image
    $changed = @(for ($i = 0; $i -lt 0x2000; $i++) { if ($gp.Bytes[$i] -ne $again.Bytes[$i]) { $i } })
    $outside = @($changed | Where-Object { $byte = $_; @($gpIndexes | Where-Object { $byte -ge 0x1000 + 0x10 * $_ -and $byte -lt 0x1000 + 0x10 * $_ + $original[0][$_].Length }).Count -eq 0 })
    $gpWritten = @($gpIndexes | Where-Object { [Linq.Enumerable]::SequenceEqual([byte[]](Get-Site $gp.Bytes $_), [byte[]]$sites[$_].Replacement) })
    Test-Result 'one switch on' ($gpIndexes.Count -eq 4 -and @($gpIndexes | Where-Object { $run.Sites[$_].Outcome -ne 'Patched' }).Count -eq 0 -and @($run.Sites | Where-Object { $_.Outcome -ne 'Off' }).Count -eq 4 -and $gpWritten.Count -eq 4 -and $outside.Count -eq 0 -and $gp.Protect -eq '0x20,0x20' -and @($run.Lines | Where-Object { $_ -ceq 'noGpPurchase (row 3a): 4 patched, 0 already patched, 0 refused.' }).Count -eq 1) ($run.Lines -join ' | ')

    # The process gone: nothing written, one sentence.
    $child.StandardInput.WriteLine('quit')
    [void]$child.WaitForExit(10000)
    $run = $patcher::Apply($child.Id, $moved, $switchType::Defaults)
    Test-Result 'no process' ((Get-Outcomes $run) -eq ((@('Skipped') * $sites.Count) -join ',') -and $run.Lines.Count -eq 1 -and $run.Lines[0] -like '*nothing was written*') ($run.Lines -join ' | ')
}
catch {
    Test-Result 'run' $false $_.Exception.Message
}
finally {
    if ($null -ne $child) {
        if (!$child.HasExited) { $child.Kill() }
        $child.Dispose()
    }
    Remove-Item -LiteralPath $childScript -ErrorAction SilentlyContinue
}

$sink = Get-DialogSink
Test-Result 'no window shown' ($sink.Shown -eq 0) $sink.Detail
if ($script:failed -gt 0) { Write-Host "$($script:failed) check(s) failed."; exit 1 }
Write-Host 'Every check passed.'
exit 0
