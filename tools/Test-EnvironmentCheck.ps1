<#
Checks overlay\App\FGOAC_EnvironmentCheck.ps1 without the game: it runs on throwaway install folders under %TEMP%,
deleted when the check ends, once under Windows PowerShell 5.1 and once under PowerShell 7, as the launcher may pick
either.

  pwsh -File tools\Test-EnvironmentCheck.ps1 [-CloudApp <his 1.10 package>\App]

On a folder with Cloud23333's App\gpu-runtime, the check lists this PC's graphics cards through his probe, as his own
environment check does; on a folder without it, it has no graphics card line. His touch key check passes while
[touch] clickKey is bound to nothing else and names each [keyboard], [io4] or [aime] key that shares it; his database
check passes for a Server\data\mariadb\ibdata1 that opens for writing, fails with READONLY for one marked
read-only, and is left out with -CommunityServer, which needs no local server.

Exit codes: 0 every check passed, 1 a check failed, 2 a prerequisite is missing.
#>
[CmdletBinding()]
param(
    [string]$CloudApp = "$env:FGOAC_CLOUD_UPDATE\payload\payload\App"
)
$ErrorActionPreference = 'Stop'

$repository = Split-Path -Parent $PSScriptRoot
$check = Join-Path $repository 'overlay\App\FGOAC_EnvironmentCheck.ps1'
foreach ($path in @($check, (Join-Path $CloudApp 'FGO_GpuRouting.ps1'), (Join-Path $CloudApp 'FGO_GpuProbe.cs'), (Join-Path $CloudApp 'gpu-runtime\FgoGpuProbe.dll'))) {
    if (!(Test-Path -LiteralPath $path)) { Write-Host "Missing $path."; exit 2 }
}
$hosts = @((Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'), (Join-Path $PSHOME 'pwsh.exe'))

$script:failed = 0
function Test-Result([string]$Name, [bool]$Passed, [string]$Detail) {
    Write-Host ("{0} {1}: {2}" -f $(if ($Passed) { 'PASS' } else { 'FAIL' }), $Name, $Detail)
    if (!$Passed) { $script:failed++ }
}

# The checks the script reports for the install at $Root, run by $Executable, as objects with Name, Passed and Detail.
function Invoke-Check([string]$Executable, [string]$Root, [switch]$CommunityServer) {
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', (Join-Path $Root 'App\FGOAC_EnvironmentCheck.ps1'), '-AsJson')
    if ($CommunityServer) { $arguments += '-CommunityServer' }
    $output = & $Executable @arguments 2>&1 | ForEach-Object { "$_" }
    return @(($output -join "`n") | ConvertFrom-Json)
}

$work = Join-Path ([IO.Path]::GetTempPath()) ('fgoac-environment-check-' + [guid]::NewGuid().ToString('N'))
try {
    $his = Join-Path $work 'his'
    $old = Join-Path $work 'old'
    foreach ($root in @($his, $old)) {
        New-Item -ItemType Directory -Path (Join-Path $root 'App') -Force | Out-Null
        Copy-Item -LiteralPath $check -Destination (Join-Path $root 'App')
    }
    Copy-Item -LiteralPath (Join-Path $CloudApp 'FGO_GpuRouting.ps1'), (Join-Path $CloudApp 'FGO_GpuProbe.cs') -Destination (Join-Path $his 'App')
    Copy-Item -LiteralPath (Join-Path $CloudApp 'gpu-runtime') -Destination (Join-Path $his 'App\gpu-runtime') -Recurse
    # His build: touch on the left mouse button, bound to nothing else; a database file that opens for writing.
    [IO.File]::WriteAllText((Join-Path $his 'App\segatools.ini'), "[keyboard]`r`nattack=0x2`r`nskill1=0x31`r`n[io4]`r`ntest=0x70`r`n")
    # 11.00: touch on the right mouse button, which attack and the card reader share; a read-only database file.
    [IO.File]::WriteAllText((Join-Path $old 'App\segatools.ini'), "[touch]`r`nclickKey=2`r`n[keyboard]`r`nattack=0x2`r`n[aime]`r`nscan=2`r`n")
    foreach ($root in @($his, $old)) {
        New-Item -ItemType Directory -Path (Join-Path $root 'Server\data\mariadb') -Force | Out-Null
        [IO.File]::WriteAllBytes((Join-Path $root 'Server\data\mariadb\ibdata1'), [byte[]](1..64))
    }
    [IO.File]::SetAttributes((Join-Path $old 'Server\data\mariadb\ibdata1'), [IO.FileAttributes]::ReadOnly)

    foreach ($executable in $hosts) {
        $name = [IO.Path]::GetFileNameWithoutExtension($executable)
        $hisChecks = Invoke-Check $executable $his
        $cards = @($hisChecks | Where-Object { $_.Name -ceq 'Graphics Cards' })
        Test-Result "[$name] graphics cards listed on his build" ($cards.Count -eq 1 -and $cards[0].Passed -and $cards[0].Detail -match '^Found [1-9][0-9]* physical graphics card') $(if ($cards.Count) { $cards[0].Detail } else { "checks: $(@($hisChecks | ForEach-Object Name) -join ', ')" })
        $checks = Invoke-Check $executable $old
        $cards = @($checks | Where-Object { $_.Name -ceq 'Graphics Cards' })
        Test-Result "[$name] no graphics card line without his probe" ($checks.Count -gt 5 -and $cards.Count -eq 0) "$($checks.Count) checks"
        $touch = @($checks | Where-Object { $_.Name -ceq 'Touch Key Conflict' })
        $database = @($checks | Where-Object { $_.Name -ceq 'Database Files' })
        Test-Result "[$name] touch key shared with attack and the card reader" ($touch.Count -eq 1 -and !$touch[0].Passed -and $touch[0].Detail -match '^Right mouse button is also bound to keyboard\.attack, aime\.scan;') $(if ($touch.Count) { $touch[0].Detail } else { 'no touch key line' })
        Test-Result "[$name] read-only database file" ($database.Count -eq 1 -and !$database[0].Passed -and $database[0].Detail -like '`[READONLY`] *') $(if ($database.Count) { $database[0].Detail } else { 'no database line' })
        $touch = @($hisChecks | Where-Object { $_.Name -ceq 'Touch Key Conflict' })
        $database = @($hisChecks | Where-Object { $_.Name -ceq 'Database Files' })
        Test-Result "[$name] touch key bound to nothing else" ($touch.Count -eq 1 -and $touch[0].Passed -and $touch[0].Detail -match '^Touch click: Left mouse button;') $(if ($touch.Count) { $touch[0].Detail } else { 'no touch key line' })
        Test-Result "[$name] database file opens for writing" ($database.Count -eq 1 -and $database[0].Passed -and $database[0].Detail -like '`[OK`] *') $(if ($database.Count) { $database[0].Detail } else { 'no database line' })
        $checks = Invoke-Check $executable $his -CommunityServer
        $database = @($checks | Where-Object { $_.Name -ceq 'Database Files' })
        Test-Result "[$name] no database line for the community server" ($checks.Count -gt 5 -and $database.Count -eq 0) "$($checks.Count) checks"
    }
}
catch {
    Test-Result 'run' $false ($_ | Out-String)
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($script:failed -gt 0) { Write-Host "$($script:failed) check(s) failed."; exit 1 }
Write-Host 'Every check passed.'
exit 0
