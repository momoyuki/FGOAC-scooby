<#
Checks the deck code without starting the launcher or the game.

  pwsh -File tools\Test-DeckCode.ps1

Through the launcher assembly (src\bin\Release, so run build.cmd first) it checks the deck code
layout: a fixed two-card code written out by hand, a thirty-card deck with copies
and a 48-byte name read back as it was written, the 200-character bound, a one-card deck with no name,
whitespace anywhere and the prefix in any case, the name cut at 48 bytes, and every code the layout
refuses: a newer version, no prefix, a wrong length, a reserved bit, a repeated card and copy, a changed
checksum and a control character in the name. The refused codes other than the checksum carry a correct
checksum, so each is caught by its own rule. It also checks the loadout name an imported code is saved
under.

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

# CRC-32 as in zip, written here from the polynomial so the checks do not lean on the launcher's own.
function Get-Crc32([byte[]]$Bytes) {
    $crc = [uint32]::MaxValue
    foreach ($b in $Bytes) {
        $crc = [uint32]($crc -bxor [uint32]$b)
        for ($i = 0; $i -lt 8; $i++) {
            $crc = if ($crc -band 1) { [uint32](($crc -shr 1) -bxor 0xEDB88320u) } else { [uint32]($crc -shr 1) }
        }
    }
    return [uint32]($crc -bxor [uint32]::MaxValue)
}

function ConvertTo-Base64Url([byte[]]$Bytes) {
    return [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

# A code for raw bytes with a correct checksum appended, big-endian.
function New-RawCode([byte[]]$Body) {
    $crc = Get-Crc32 $Body
    $all = [byte[]]($Body + @([byte](($crc -shr 24) -band 0xFF), [byte](($crc -shr 16) -band 0xFF), [byte](($crc -shr 8) -band 0xFF), [byte]($crc -band 0xFF)))
    return 'FGOAC-' + (ConvertTo-Base64Url $all)
}

function Format-Slots($Slots) {
    return (@($Slots | ForEach-Object { '{0}{1}#{2}' -f $_.CardId, $(if ($_.CraftEssence) { 'C' } else { 'S' }), $_.Copy }) -join ' ')
}

$notWhole = 'That is not a whole deck code. Copy all of it and paste it again.'
$newer = 'A newer launcher made this deck code. Update the launcher to read it.'

try {
    [void][Reflection.Assembly]::LoadFrom($assembly)
    $code = [FGOLocalPlatform.DeckCode]
    function New-Slot([int]$Id, [bool]$Craft, [int]$Copy) { return [FGOLocalPlatform.DeckCode+Slot]::new($Id, $Craft, $Copy) }
    function New-Deck([object[]]$Slots) {
        $list = [Collections.Generic.List[FGOLocalPlatform.DeckCode+Slot]]::new()
        foreach ($s in $Slots) { $list.Add($s) }
        return , $list
    }

    # The fixed vector, bytes by hand from the layout: 01 version, 02 cards, Servant 6 copy 1 = 00 18 00,
    # Craft Essence 1 copy 2 = 00 06 10, name length 01, "A" = 41, then CRC-32 57 02 45 8C.
    $fixed = 'FGOAC-AQIAGAAABhABQVcCRYw'
    $two = New-Deck @((New-Slot 6 $false 1), (New-Slot 1 $true 2))
    $written = $code::Encode($two, 'A')
    Test-Result 'fixed vector' ($written -ceq $fixed) $written
    Test-Result 'fixed vector by hand' ((New-RawCode ([byte[]](1, 2, 0x00, 0x18, 0x00, 0x00, 0x06, 0x10, 1, 0x41))) -ceq $fixed) $fixed
    $read = $code::Decode($fixed)
    Test-Result 'fixed vector read' ($null -eq $read.Refusal -and (Format-Slots $read.Slots) -ceq '6S#1 1C#2' -and $read.Name -ceq 'A') "$(Format-Slots $read.Slots) '$($read.Name)' $($read.Refusal)"

    # Thirty cards in deck order, copies of the same card apart, and a 48-byte UTF-8 name.
    $slots = @()
    for ($i = 0; $i -lt 10; $i++) { $slots += New-Slot (16383 - $i) $true 1 }
    for ($c = 1; $c -le 12; $c++) { $slots += New-Slot 6 $false $c }
    for ($c = 30; $c -ge 23; $c--) { $slots += New-Slot 2821 $true $c }
    $thirty = New-Deck $slots
    $e = [string][char]0xE9
    $name = "Saber $([char]0xD7)3 $([char]0x65E5)$([char]0x672C)$([char]0x8A9E) " + ($e * 13) + 'xy'
    Test-Result 'name is 48 bytes' ([Text.Encoding]::UTF8.GetByteCount($name) -eq 48) "$([Text.Encoding]::UTF8.GetByteCount($name)) bytes"
    $long = $code::Encode($thirty, $name)
    $read = $code::Decode($long)
    Test-Result 'thirty cards round trip' ($null -eq $read.Refusal -and (Format-Slots $read.Slots) -ceq (Format-Slots $thirty) -and $read.Name -ceq $name) "$(Format-Slots $read.Slots) '$($read.Name)' $($read.Refusal)"
    Test-Result 'thirty cards and a full name' ($long.Length -eq 200) "$($long.Length) characters"
    $bytes = [Convert]::FromBase64String($long.Substring(6).Replace('-', '+').Replace('_', '/') + '==')
    Test-Result 'thirty cards and a full name bytes' ($bytes.Length -eq 145) "$($bytes.Length) bytes"

    # One card and no name.
    $one = New-Deck @(New-Slot 1537 $false 1)
    $short = $code::Encode($one, '')
    $read = $code::Decode($short)
    Test-Result 'one card no name' ($null -eq $read.Refusal -and (Format-Slots $read.Slots) -ceq '1537S#1' -and $read.Name -ceq '') "$short $(Format-Slots $read.Slots) '$($read.Name)'"

    # The name is cut at a character boundary and loses its control characters.
    $read = $code::Decode($code::Encode($one, $e * 30))
    Test-Result 'name cut at 48 bytes' ($read.Name -ceq ($e * 24)) $read.Name
    $read = $code::Decode($code::Encode($one, "Lancer`tgo`n"))
    Test-Result 'control characters left out' ($read.Name -ceq 'Lancergo') $read.Name

    # Whitespace anywhere and the prefix in any case.
    $pasted = '  fgoac-' + $fixed.Substring(6, 5) + "`r`n " + $fixed.Substring(11, 7) + "`t" + $fixed.Substring(18) + ' '
    $read = $code::Decode($pasted)
    Test-Result 'whitespace and prefix case ignored' ($null -eq $read.Refusal -and (Format-Slots $read.Slots) -ceq '6S#1 1C#2') "$pasted -> $($read.Refusal)"
    $read = $code::Decode('Fgoac-' + $fixed.Substring(6))
    Test-Result 'mixed case prefix' ($null -eq $read.Refusal) "$($read.Refusal)"
    $read = $code::Decode('FGOAC-' + $fixed.Substring(6).ToLowerInvariant())
    Test-Result 'body case is data' ($null -ne $read.Refusal) "$($read.Refusal)"

    # The base64url alphabet: this code holds both - and _.
    $read = $code::Decode('FGOAC-AQEAGAACZll9-_kf')
    Test-Result 'base64url alphabet' ($null -eq $read.Refusal -and (Format-Slots $read.Slots) -ceq '6S#1' -and $read.Name -ceq 'fY') "$(Format-Slots $read.Slots) '$($read.Name)' $($read.Refusal)"

    # A repeated card with another copy, and one number as Servant and Craft Essence, are separate cards.
    $read = $code::Decode((New-RawCode ([byte[]](1, 3, 0x00, 0x18, 0x00, 0x00, 0x18, 0x10, 0x00, 0x1A, 0x00, 0))))
    Test-Result 'copies and kinds are separate cards' ($null -eq $read.Refusal -and (Format-Slots $read.Slots) -ceq '6S#1 6S#2 6C#1') "$(Format-Slots $read.Slots) $($read.Refusal)"

    # Every refusal. Each code but the checksum one has a correct checksum.
    $flipped = [Convert]::FromBase64String($fixed.Substring(6).Replace('-', '+').Replace('_', '/') + '=')
    $flipped[$flipped.Length - 1] = $flipped[$flipped.Length - 1] -bxor 1
    $refused = [ordered]@{
        'version 2'                   = @((New-RawCode ([byte[]](2, 1, 0x00, 0x18, 0x00, 0))), $newer)
        'version 2 without checksum'  = @('FGOAC-AgEAGAAA', $newer)
        'version 0'                   = @((New-RawCode ([byte[]](0, 1, 0x00, 0x18, 0x00, 0))), $notWhole)
        'no prefix'                   = @($fixed.Substring(6), $notWhole)
        'another prefix'              = @(('FGOAD-' + $fixed.Substring(6)), $notWhole)
        'empty'                       = @('   ', $notWhole)
        'prefix only'                 = @('FGOAC-', $notWhole)
        'not base64url'               = @('FGOAC-*', $notWhole)
        'base64 padding'              = @(($fixed + '='), $notWhole)
        'standard base64 alphabet'    = @('FGOAC-AQEAGAACZll9+/kf', $notWhole)
        'short by an entry'           = @((New-RawCode ([byte[]](1, 2, 0x00, 0x18, 0x00, 0))), $notWhole)
        'a byte too many'             = @((New-RawCode ([byte[]](1, 1, 0x00, 0x18, 0x00, 0, 0x41))), $notWhole)
        'name longer than said'       = @((New-RawCode ([byte[]](1, 1, 0x00, 0x18, 0x00, 1))), $notWhole)
        'no cards'                    = @((New-RawCode ([byte[]](1, 0, 0))), $notWhole)
        'thirty-one cards'            = @((New-RawCode ([byte[]](@(1, 31) + @(for ($i = 1; $i -le 31; $i++) { 0; $i * 4; 0 }) + @(0)))), $notWhole)
        'name of 49 bytes'            = @((New-RawCode ([byte[]](@(1, 1, 0x00, 0x18, 0x00, 49) + @(1..49 | ForEach-Object { 0x41 })))), $notWhole)
        'reserved bit set'            = @((New-RawCode ([byte[]](1, 1, 0x00, 0x18, 0x01, 0))), $notWhole)
        'card 0'                      = @((New-RawCode ([byte[]](1, 1, 0x00, 0x00, 0x00, 0))), $notWhole)
        'copy 31'                     = @((New-RawCode ([byte[]](1, 1, 0x00, 0x19, 0xE0, 0))), $notWhole)
        'repeated card and copy'      = @((New-RawCode ([byte[]](1, 2, 0x00, 0x18, 0x00, 0x00, 0x18, 0x00, 0))), $notWhole)
        'checksum off by one byte'    = @(('FGOAC-' + (ConvertTo-Base64Url $flipped)), $notWhole)
        'control character in name'   = @((New-RawCode ([byte[]](1, 1, 0x00, 0x18, 0x00, 1, 7))), $notWhole)
        'broken UTF-8 in name'        = @((New-RawCode ([byte[]](1, 1, 0x00, 0x18, 0x00, 1, 0xC3))), $notWhole)
    }
    foreach ($case in $refused.GetEnumerator()) {
        $read = $code::Decode($case.Value[0])
        Test-Result "refused: $($case.Key)" ($read.Refusal -ceq $case.Value[1] -and $read.Slots.Count -eq 0) "$($case.Value[0]) -> $($read.Refusal)"
    }

    # Encode takes only a deck the layout can carry.
    $bad = [ordered]@{
        'empty deck'             = (New-Deck @())
        'thirty-one cards'       = (New-Deck @(1..31 | ForEach-Object { New-Slot $_ $false 1 }))
        'card 16384'             = (New-Deck @(New-Slot 16384 $false 1))
        'copy 31'                = (New-Deck @(New-Slot 6 $false 31))
        'repeated card and copy' = (New-Deck @((New-Slot 6 $false 1), (New-Slot 6 $false 1)))
    }
    foreach ($case in $bad.GetEnumerator()) {
        $threw = $false
        try { [void]$code::Encode($case.Value, '') } catch { $threw = $_.Exception.InnerException -is [ArgumentException] -or $_.Exception -is [ArgumentException] }
        Test-Result "not encoded: $($case.Key)" $threw ''
    }

    # The loadout an imported code is saved as: a file name, never over one already there.
    $folderType = [Reflection.Assembly]::LoadFrom($assembly).GetType('FGOLocalPlatform.PresetFolder')
    $work = Join-Path ([IO.Path]::GetTempPath()) ('fgoac-deckcode-check-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work | Out-Null
    try {
        $free = { param($n) $folderType.GetMethod('FreeName').Invoke($null, [object[]]@([string]$work, [string]$n)) }
        Test-Result 'loadout name' ((& $free 'Arena') -ceq 'Arena') (& $free 'Arena')
        Test-Result 'loadout name file characters' ((& $free 'Lancer: go/..\x') -ceq 'Lancer- go-..-x') (& $free 'Lancer: go/..\x')
        Set-Content -LiteralPath (Join-Path $work 'Arena.json') -Value '{}'
        Set-Content -LiteralPath (Join-Path $work 'Arena (2).json') -Value '{}'
        Test-Result 'loadout name taken' ((& $free 'arena') -ceq 'arena (3)') (& $free 'arena')
    }
    finally { Remove-Item -LiteralPath $work -Recurse -Force }
}
catch {
    Test-Result 'run' $false ($_ | Out-String)
}

$sink = Get-DialogSink
Test-Result 'no window shown' ($sink.Shown -eq 0) $sink.Detail
if ($script:failed -gt 0) { Write-Host "$script:failed check(s) failed."; exit 1 }
Write-Host 'Every check passed.'
exit 0
