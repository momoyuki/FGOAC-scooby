<#
Checks the graphics layer choice on Display without starting the launcher or the game. Every file is written under a
throwaway folder in %TEMP%, deleted when the check ends.

  pwsh -File tools\Test-GpuCompat.ps1

The fake install has compat\ copied from this repository. Through the launcher assembly (src\bin\Release, so run
build.cmd first): on game 11.00 the choices are Off, Cloud23333's layer, the 1.1.0 layer and fluphus's AMD layer, each
putting in its own files and taking out the others FGOAC scooby put there; our 1.1.0 build is recognised by its SHA-256;
a foreign App\fgoglcompat.dll is reported as another layer and no choice touches it. On the build his update installs
the choices are his layer and fluphus's, his fgoglcompat.dll is never touched, an App\opengl32.dll that is not the shim
stays, and Off and the 1.1.0 layer are refused. An earlier fluphus build the package shipped is upgraded at start.
Clearing the shader caches removes Cloud23333's layer's two cache folders and ours, only while the game is closed, and
never follows a folder that is a link.

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

$work = Join-Path ([IO.Path]::GetTempPath()) ('fgoac-gpucompat-' + [guid]::NewGuid().ToString('N'))
$app = Join-Path $work 'App'
$compat = Join-Path $work 'compat'
$legacy = Join-Path $app 'fgoglcompat.dll'
$shim = Join-Path $app 'opengl32.dll'
$ours = Join-Path $repository 'compat\fgoglcompat.dll'
$foreign = Join-Path $work 'foreign.dll'

function Get-Hash([string]$Path) { if (Test-Path -LiteralPath $Path) { return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash } return 'missing' }

# A PE header with the two fields the build check reads: game 11.00, or the build his 1.10 update installs.
function Write-Header([string]$Path, [uint32]$TimeDateStamp, [uint32]$SizeOfImage) {
    $bytes = [byte[]]::new(0x400)
    $bytes[0] = 0x4D; $bytes[1] = 0x5A
    [BitConverter]::GetBytes([int32]0x130).CopyTo($bytes, 0x3C)
    [BitConverter]::GetBytes([uint32]0x00004550).CopyTo($bytes, 0x130)
    [BitConverter]::GetBytes($TimeDateStamp).CopyTo($bytes, 0x138)
    [BitConverter]::GetBytes($SizeOfImage).CopyTo($bytes, 0x148 + 56)
    [IO.File]::WriteAllBytes($Path, $bytes)
}

function Reset-App {
    Remove-Item -LiteralPath $app -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $compat -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $app -Force | Out-Null
    Write-Header (Join-Path $app 'ago.exe') 0x643F4ECD 0x2034000
    Copy-Item -LiteralPath (Join-Path $repository 'compat') -Destination $compat -Recurse
}

try {
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    $bytes = [byte[]]::new(990720)
    [Random]::new(1151).NextBytes($bytes)
    [IO.File]::WriteAllBytes($foreign, $bytes)
    $foreignHash = Get-Hash $foreign
    Reset-App

    # GamePaths.GameRoot is read once from the base directory, so it has to point at the fake install first.
    [AppContext]::SetData('APP_CONTEXT_BASE_DIRECTORY', $work + '\')
    [void][Reflection.Assembly]::LoadFrom($assembly)
    $gameRoot = [FGOLocalPlatform.GamePaths]::GameRoot
    if ($gameRoot -ne $app) { Write-Host "GamePaths.GameRoot is $gameRoot, not $app."; exit 2 }
    $layer = [FGOLocalPlatform.GamePaths].Assembly.GetType('FGOLocalPlatform.GpuCompat', $true)
    $flags = [Reflection.BindingFlags]'NonPublic, Public, Static'
    function Get-Static([string]$Name) { return $layer.GetProperty($Name, $flags).GetValue($null) }
    function Invoke-Static([string]$Name, [object[]]$Arguments = @()) {
        try { [void]$layer.GetMethod($Name, $flags).Invoke($null, $Arguments); return 'ok' }
        catch { return $_.Exception.InnerException.GetType().Name }
    }

    $choiceType = $layer.GetNestedType('Choice', $flags)
    function Get-Current { $c = $layer.GetProperty('Current', $flags).GetValue($null); if ($null -eq $c) { return 'another' } return "$c" }
    function Set-Choice([string]$Name) { return Invoke-Static 'Apply' @([Enum]::Parse($choiceType, $Name)) }
    $cloudSource = Join-Path $compat 'fgoglcompat.dll'
    $olderSource = Join-Path $compat 'fgoglcompat-1.1.0.dll'
    $shimSource = Join-Path $compat 'amd-shim\opengl32.dll'
    function Get-State {
        $legacyState = if (!(Test-Path -LiteralPath $legacy)) { 'none' } elseif ((Get-Hash $legacy) -eq (Get-Hash $cloudSource)) { 'cloud' } elseif ((Get-Hash $legacy) -eq (Get-Hash $olderSource)) { 'older' } else { 'other' }
        $shimState = if (!(Test-Path -LiteralPath $shim)) { 'none' } elseif ((Get-Hash $shim) -eq (Get-Hash $shimSource)) { 'fluphus' } else { 'other' }
        return "fgoglcompat=$legacyState opengl32=$shimState"
    }

    # The earlier game: four choices, Cloud23333's layer first after Off; each puts in its own files and takes out
    # the others FGOAC scooby put there.
    $offered = @($layer.GetProperty('Offered', $flags).GetValue($null)) -join ','
    Test-Result 'earlier game: the choices offered' ($offered -ceq 'Off,Cloud,Older,Fluphus') $offered
    $runs = foreach ($name in 'Cloud', 'Older', 'Fluphus', 'Cloud', 'Off') { "$name=$(Set-Choice $name)>$(Get-Current) [$(Get-State)]" }
    Test-Result 'earlier game: each choice puts in its layer alone' (($runs -join '; ') -ceq 'Cloud=ok>Cloud [fgoglcompat=cloud opengl32=none]; Older=ok>Older [fgoglcompat=older opengl32=none]; Fluphus=ok>Fluphus [fgoglcompat=none opengl32=fluphus]; Cloud=ok>Cloud [fgoglcompat=cloud opengl32=none]; Off=ok>Off [fgoglcompat=none opengl32=none]') ($runs -join '; ')

    # Our shipped 1.1.0 build with its compat copy gone is still ours, by its hash.
    Copy-Item -LiteralPath $olderSource -Destination $legacy
    Remove-Item -LiteralPath $olderSource
    Test-Result 'earlier game: our 1.1.0 build without a compat copy is Older' ((Get-Current) -ceq 'Older') "Current: $(Get-Current)"

    # A foreign fgoglcompat.dll: shown as another layer and never touched by any choice.
    Reset-App
    Copy-Item -LiteralPath $foreign -Destination $legacy
    $kept = foreach ($name in 'Cloud', 'Older', 'Fluphus', 'Off') { "$name=$(Set-Choice $name) $((Get-Hash $legacy) -eq $foreignHash)" }
    Test-Result 'earlier game: a foreign fgoglcompat.dll is another layer and stays' ((Get-Current) -ceq 'another' -and ($kept -join ',') -ceq 'Cloud=IOException True,Older=IOException True,Fluphus=IOException True,Off=IOException True' -and !(Test-Path -LiteralPath $shim)) "Current: $(Get-Current); $($kept -join ', ')"

    # Another opengl32.dll on the earlier game: shown as another layer; a choice replaces it, as the old switch did.
    Reset-App
    [IO.File]::WriteAllBytes($shim, [byte[]](1..32))
    $before = Get-Current
    $result = Set-Choice 'Cloud'
    Test-Result 'earlier game: another opengl32.dll is replaced by a choice' ($before -ceq 'another' -and $result -eq 'ok' -and (Get-State) -ceq 'fgoglcompat=cloud opengl32=none') "before: $before; Cloud: $result, $(Get-State)"

    # The build his 1.10 update installs: his own fgoglcompat.dll stays; the choices are his layer and fluphus's.
    Reset-App
    Write-Header (Join-Path $app 'ago.exe') 0x65C5AF1E 0x2039000
    Copy-Item -LiteralPath $foreign -Destination $legacy
    $offered = @($layer.GetProperty('Offered', $flags).GetValue($null)) -join ','
    $runs = foreach ($name in 'Fluphus', 'Cloud') { "$name=$(Set-Choice $name)>$(Get-Current) [opengl32 ours: $((Get-Hash $shim) -eq (Get-Hash $shimSource)), his kept: $((Get-Hash $legacy) -eq $foreignHash)]" }
    Test-Result 'his build: his layer or fluphus''s, his file kept' ($offered -ceq 'Cloud,Fluphus' -and ($runs -join '; ') -ceq 'Fluphus=ok>Fluphus [opengl32 ours: True, his kept: True]; Cloud=ok>Cloud [opengl32 ours: False, his kept: True]') "offered: $offered; $($runs -join '; ')"
    [IO.File]::WriteAllBytes($shim, [byte[]](1..32))
    $theirs = Get-Hash $shim
    $kept = "Cloud=$(Set-Choice 'Cloud') $((Get-Hash $shim) -eq $theirs) current $(Get-Current)"
    Test-Result 'his build: an opengl32.dll that is not the shim stays' ($kept -ceq 'Cloud=ok True current Cloud') $kept
    Remove-Item -LiteralPath $shim
    $refused = foreach ($name in 'Off', 'Older') { "$name=$(Set-Choice $name)" }
    Test-Result 'his build: Off and the 1.1.0 layer are refused' (($refused -join ',') -ceq 'Off=IOException,Older=IOException' -and (Get-Hash $legacy) -eq $foreignHash -and !(Test-Path -LiteralPath $shim)) ($refused -join ', ')

    # An older build of fluphus's shim that FGOAC scooby shipped is brought up to the copy under compat\ at start, on
    # both builds; an opengl32.dll FGOAC scooby never shipped stays as it is.
    $shippedField = $layer.GetField('ShippedShimHashes', $flags)
    $upgrade = $layer.GetMethod('UpgradeShipped', $flags)
    if (!$shippedField -or !$upgrade) {
        Test-Result 'an older shim we shipped is upgraded' $false 'GpuCompat.ShippedShimHashes or UpgradeShipped is missing'
    } else {
        $oldShim = Join-Path $work 'old-shim.dll'
        [IO.File]::WriteAllBytes($oldShim, [byte[]](1..64))
        $shippedField.SetValue($null, [string[]](@($shippedField.GetValue($null)) + (Get-Hash $oldShim).ToLowerInvariant()))
        $compatShim = Join-Path $compat 'amd-shim\opengl32.dll'
        $runs = foreach ($build in @(@(0x643F4ECD, 0x2034000, '1100'), @(0x65C5AF1E, 0x2039000, '1151'))) {
            Reset-App
            Write-Header (Join-Path $app 'ago.exe') $build[0] $build[1]
            Copy-Item -LiteralPath $oldShim -Destination $shim
            $done = $upgrade.Invoke($null, @())
            $upgraded = (Get-Hash $shim) -eq (Get-Hash $compatShim) -and (Test-Path -LiteralPath (Join-Path $app 'opengl32real.dll')) -and (Test-Path -LiteralPath (Join-Path $app 'amdcfg\amdOglpSettings.cfg'))
            [IO.File]::WriteAllBytes($shim, [byte[]](65..96))
            $theirs = Get-Hash $shim
            $kept = !$upgrade.Invoke($null, @()) -and (Get-Hash $shim) -eq $theirs
            "$($build[2]): upgraded $($done -and $upgraded), other kept $kept"
        }
        Test-Result 'an older shim we shipped is upgraded, another stays' (($runs -join '; ') -ceq '1100: upgraded True, other kept True; 1151: upgraded True, other kept True') ($runs -join '; ')
    }

    # Clearing the shader caches, Cloud23333's FGO_ClearShaderCache.ps1 rules: refused while the game runs; his
    # layer's two cache folders and ours removed, every other file kept; a folder that is a link is refused, its
    # target kept.
    $cache = [FGOLocalPlatform.GamePaths].Assembly.GetType('FGOLocalPlatform.ShaderCache')
    if ($null -ne $cache) {
        $folders = @('aft-program-link-cache-v1', 'shader-cache', 'shader-cache-r10', 'shader-cache-r2')
        foreach ($name in $folders) {
            New-Item -ItemType Directory -Path (Join-Path $app "$name\sub") -Force | Out-Null
            [IO.File]::WriteAllBytes((Join-Path $app "$name\sub\program.bin"), [byte[]](1..16))
        }
        [IO.File]::WriteAllBytes((Join-Path $app 'shader-cache-r3.bin'), [byte[]](1..16))
        $clear = $cache.GetMethod('Clear', $flags)
        function Invoke-Clear([bool]$Running) {
            try { return 'cleared ' + ($clear.Invoke($null, @([string]$app, $Running)) -join ',') }
            catch { return $_.Exception.InnerException.Message }
        }
        $refused = Invoke-Clear $true
        $kept = @($folders | Where-Object { Test-Path -LiteralPath (Join-Path $app $_) }).Count
        $cleared = Invoke-Clear $false
        $left = @($folders | Where-Object { Test-Path -LiteralPath (Join-Path $app $_) })
        $again = Invoke-Clear $false
        Test-Result 'shader caches cleared only while the game is closed' ($refused -ceq 'Close the game first, then clear the shader cache.' -and $kept -eq 4 -and $cleared -ceq "cleared $($folders -join ',')" -and $left.Count -eq 0 -and (Test-Path -LiteralPath (Join-Path $app 'shader-cache-r3.bin')) -and $again -ceq 'cleared ') "running: $refused; then $cleared, left: $($left -join ',') ; again: $again"
        $outside = Join-Path $work 'outside'
        New-Item -ItemType Directory -Path $outside -Force | Out-Null
        [IO.File]::WriteAllBytes((Join-Path $outside 'keep.bin'), [byte[]](1..16))
        $link = Join-Path $app 'shader-cache-r2'
        New-Item -ItemType Junction -Path $link -Target $outside | Out-Null
        $linked = Invoke-Clear $false
        $targetKept = Test-Path -LiteralPath (Join-Path $outside 'keep.bin')
        [IO.Directory]::Delete($link)
        Test-Result 'a linked cache folder is refused' ($linked -ceq 'App\shader-cache-r2 is not a plain folder in App, so it was left as it is.' -and $targetKept) "$linked; target kept: $targetKept"
    } else {
        Test-Result 'shader caches cleared only while the game is closed' $false 'FGOLocalPlatform.ShaderCache is missing'
    }
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

$sink = Get-DialogSink
Test-Result 'no window shown' ($sink.Shown -eq 0) $sink.Detail
if ($script:failed -gt 0) { Write-Host "$($script:failed) check(s) failed."; exit 1 }
Write-Host 'Every check passed.'
exit 0
