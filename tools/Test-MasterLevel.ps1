<#
Checks Max Master Level without the game, the local server or Cloud23333's account tool. Every file is written
under a throwaway folder in %TEMP%, deleted when the check ends.

  pwsh -File tools\Test-MasterLevel.ps1 [-Python D:\games\FGOA\Server\python\python.exe]

overlay\Server\tools\fgoac_master_level.py runs beside a stand-in fgo_account.py that keeps profiles in memory and
records each save, with a master EXP table of three levels: the Master's EXP goes to the top once and a second run
changes nothing; a missing table, a running server and an unknown account each refuse with their error and save
nothing. Through the launcher assembly (src\bin\Release, so run build.cmd first) the Max Master Level button runs
this script, not an action of the account tool.

Exit codes: 0 every check passed, 1 a check failed, 2 a prerequisite is missing.
#>
[CmdletBinding()]
param(
    [string]$Python = 'D:\FGOA\Server\python\python.exe'
)
$ErrorActionPreference = 'Stop'

$repository = Split-Path -Parent $PSScriptRoot
$assembly = Join-Path $repository 'src\bin\Release\net6.0-windows\FGOLocalPlatform.dll'
$masterScript = Join-Path $repository 'overlay\Server\tools\fgoac_master_level.py'
if ($PSVersionTable.PSVersion.Major -lt 7) { Write-Host 'Run this with pwsh 7: the launcher assembly targets .NET 6.'; exit 2 }
if (!(Test-Path -LiteralPath $assembly)) { Write-Host "Missing $assembly. Run build.cmd first."; exit 2 }
# Every message box, dialog and window of the launcher goes to the sink, never to the screen.
. (Join-Path $PSScriptRoot 'DialogSink.ps1'); Enable-DialogSink $assembly
if (!(Test-Path -LiteralPath $Python)) { Write-Host "Missing $Python."; exit 2 }

$script:failed = 0
function Test-Result([string]$Name, [bool]$Passed, [string]$Detail) {
    Write-Host ("{0} {1}: {2}" -f $(if ($Passed) { 'PASS' } else { 'FAIL' }), $Name, $Detail)
    if (!$Passed) { $script:failed++ }
}

$stub = @'
import json, os
from pathlib import Path
TOOLS_DIR = Path(__file__).resolve().parent
PROFILES_PATH = TOOLS_DIR.parent / "state" / "fgo-players.json"
def server_running():
    return os.environ.get("STUB_SERVER") == "1"
def backup_file(path):
    return str(path) + ".bak-stub"
def find_profile_by_aime_id(profiles, aime_id):
    key = f"aime:{aime_id}"
    return (key, profiles[key]) if key in profiles else (None, None)
class Servlet:
    def __init__(self):
        self._profiles = json.loads(PROFILES_PATH.read_text(encoding="utf-8"))
    def _save_profiles(self, profile):
        self._profiles["aime:1"] = profile
        PROFILES_PATH.write_text(json.dumps(self._profiles), encoding="utf-8")
def build_servlet():
    return Servlet()
'@

$work = Join-Path ([IO.Path]::GetTempPath()) ('fgoac-masterlevel-' + [guid]::NewGuid().ToString('N'))
$tools = Join-Path $work 'Server\tools'
$profiles = Join-Path $work 'Server\state\fgo-players.json'
$table = Join-Path $work 'Server\data\fgo-master\master_exp_table\arms_mst_master_exp_table.bin'

function Invoke-MasterLevel([int]$AimeId, [string]$Server = '0') {
    $env:STUB_SERVER = $Server
    try {
        $out = & $Python (Join-Path $tools 'fgoac_master_level.py') --aime-id $AimeId --json 2>&1 | ForEach-Object { "$_" }
        $code = $LASTEXITCODE
    } finally { Remove-Item Env:STUB_SERVER -ErrorAction SilentlyContinue }
    $json = try { ($out | Select-Object -Last 1) | ConvertFrom-Json } catch { $null }
    return [pscustomobject]@{ Code = $code; Json = $json; Text = ($out -join ' ') }
}
function Get-Exp { return (Get-Content -LiteralPath $profiles -Raw | ConvertFrom-Json).'aime:1'.mstr_exp }

try {
    foreach ($folder in @($tools, (Split-Path -Parent $profiles), (Split-Path -Parent $table))) { New-Item -ItemType Directory -Path $folder -Force | Out-Null }
    if (Test-Path -LiteralPath $masterScript) { Copy-Item -LiteralPath $masterScript -Destination $tools }
    [IO.File]::WriteAllText((Join-Path $tools 'fgo_account.py'), $stub)
    [IO.File]::WriteAllText($profiles, '{"aime:1": {"mstr_exp": 5, "master_name": "Master"}}')
    [IO.File]::WriteAllText($table, "master_exp_table.0.lv=1`nmaster_exp_table.0.exp=10`nmaster_exp_table.1.lv=2`nmaster_exp_table.1.exp=30`nmaster_exp_table.2.lv=3`nmaster_exp_table.2.exp=60`n")

    $run = Invoke-MasterLevel 1
    Test-Result 'raises the Master to the top' ($run.Code -eq 0 -and $run.Json.ok -and $run.Json.changed -eq 1 -and (Get-Exp) -eq 60) "exit $($run.Code), exp now $(Get-Exp): $($run.Text)"
    $run = Invoke-MasterLevel 1
    Test-Result 'a second run changes nothing' ($run.Code -eq 0 -and $run.Json.ok -and $run.Json.changed -eq 0 -and (Get-Exp) -eq 60) "exit $($run.Code): $($run.Text)"

    [IO.File]::WriteAllText($profiles, '{"aime:1": {"mstr_exp": 5}}')
    $run = Invoke-MasterLevel 1 '1'
    Test-Result 'server running refuses' ($run.Code -eq 1 -and $run.Json.error -eq 'server_running' -and (Get-Exp) -eq 5) "exit $($run.Code): $($run.Text)"
    $run = Invoke-MasterLevel 7
    Test-Result 'unknown account refuses' ($run.Code -eq 1 -and $run.Json.error -eq 'account_not_found' -and (Get-Exp) -eq 5) "exit $($run.Code): $($run.Text)"
    Remove-Item -LiteralPath $table
    $run = Invoke-MasterLevel 1
    Test-Result 'missing table refuses' ($run.Code -eq 1 -and $run.Json.error -eq 'master_table_missing' -and (Get-Exp) -eq 5) "exit $($run.Code): $($run.Text)"

    $launcher = [Reflection.Assembly]::LoadFrom($assembly)
    $window = $launcher.GetType('FGOLocalPlatform.MainWindow', $true)
    $property = $window.GetProperty('MasterLevelScriptPath', [Reflection.BindingFlags]'NonPublic, Static')
    $path = if ($property) { [string]$property.GetValue($null) } else { 'missing' }
    Test-Result 'the button runs this script' ($path -like '*\Server\tools\fgoac_master_level.py') "MasterLevelScriptPath: $path"
} finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

$sink = Get-DialogSink
Test-Result 'no window shown' ($sink.Shown -eq 0) $sink.Detail
if ($script:failed -gt 0) { Write-Host "$($script:failed) check(s) failed."; exit 1 }
Write-Host 'Every check passed.'
exit 0
