<#
Checks Export all and Import on the Accounts page without the game, the local server or a database. Every file is
written under a throwaway folder in %TEMP%, deleted when the check ends.

  pwsh -File tools\Test-AccountTransfer.ps1 [-Python D:\games\FGOA\Server\python\python.exe] [-CloudTools <his 1.10 package>\Server\tools]

Cloud23333's own fgo_account_transfer.py, from his 1.10 package, runs beside a stand-in fgo_account.py that gives it
what his account tool does: the profiles file, the running-server check and an Aime database, here an SQLite file
with his two tables. Through the launcher assembly (src\bin\Release, so run build.cmd first), with the arguments and
the reading of the answers the Accounts page uses: an export of two accounts; a preview of that file on another PC
that has one of the same cards, which names the Master whose save replaces that account's; the import with the preview's token, which
keeps the other local account, gives the imported account whose number is taken a new one and backs up first; an
import with a token from before a change, and an export over an existing file, each refused with an English
sentence; and the buttons offered only where his transfer tool is installed.

Exit codes: 0 every check passed, 1 a check failed, 2 a prerequisite is missing.
#>
[CmdletBinding()]
param(
    [string]$Python = 'D:\FGOA\Server\python\python.exe',
    [string]$CloudTools = "$env:FGOAC_CLOUD_UPDATE\payload\payload\Server\tools"
)
$ErrorActionPreference = 'Stop'

$repository = Split-Path -Parent $PSScriptRoot
$assembly = Join-Path $repository 'src\bin\Release\net6.0-windows\FGOLocalPlatform.dll'
if ($PSVersionTable.PSVersion.Major -lt 7) { Write-Host 'Run this with pwsh 7: the launcher assembly targets .NET 6.'; exit 2 }
foreach ($path in @($assembly, $Python, (Join-Path $CloudTools 'fgo_account_transfer.py'))) {
    if (!(Test-Path -LiteralPath $path)) { Write-Host "Missing $path."; exit 2 }
}

$script:failed = 0
function Test-Result([string]$Name, [bool]$Passed, [string]$Detail) {
    Write-Host ("{0} {1}: {2}" -f $(if ($Passed) { 'PASS' } else { 'FAIL' }), $Name, $Detail)
    if (!$Passed) { $script:failed++ }
}

# His account tool as far as the transfer commands use it; MariaDB is an SQLite file with the same two tables.
$stub = @'
import argparse, json, os, sqlite3, sys
from pathlib import Path
TOOLS_DIR = Path(__file__).resolve().parent
SERVER_DIR = TOOLS_DIR.parent
PROFILES_PATH = SERVER_DIR / "state" / "fgo-players.json"
SERVER_RUNNING_MESSAGE = "请先停止本地服务器（前端“停止服务器”或 Stop-FGOLocalServer.ps1）后重试"
def server_running():
    return os.environ.get("STUB_SERVER") == "1"
class Cursor:
    def __init__(self, cursor): self.cursor = cursor
    def __enter__(self): return self
    def __exit__(self, *exc): return False
    def execute(self, sql, params=()): self.cursor.execute(sql.replace(" FOR UPDATE", "").replace("%s", "?"), tuple(params))
    def fetchall(self): return self.cursor.fetchall()
    def fetchone(self): return self.cursor.fetchone()
class Connection:
    def __init__(self, path): self.db = sqlite3.connect(path)
    def cursor(self): return Cursor(self.db.cursor())
    def commit(self): self.db.commit()
    def rollback(self): self.db.rollback()
    def close(self): self.db.close()
def connect_aime_db():
    return Connection(str(SERVER_DIR / "state" / "aime.sqlite"))
def pick_unique_value(cursor, table, column, base, max_len):
    base = base[:max_len].strip() or "master"
    candidate = base
    suffix = 2
    while True:
        cursor.execute(f"SELECT 1 FROM {table} WHERE {column}=%s LIMIT 1", (candidate,))
        if cursor.fetchone() is None:
            return candidate
        tail = f"_{suffix}"
        candidate = f"{base[: max_len - len(tail)]}{tail}"
        suffix += 1
def main():
    parser = argparse.ArgumentParser()
    commands = parser.add_subparsers(dest="command", required=True)
    for name in ("export-accounts", "preview-accounts", "import-accounts"):
        command = commands.add_parser(name)
        command.add_argument("--file", required=True)
        command.add_argument("--confirm-token", default="")
        command.add_argument("--json", action="store_true")
    args = parser.parse_args()
    sys.path.insert(0, str(TOOLS_DIR))
    import fgo_account_transfer
    sys.stdout.write(json.dumps(fgo_account_transfer.command(sys.modules[__name__], args), ensure_ascii=False) + "\n")
    return 0
if __name__ == "__main__":
    sys.modules["fgo_account"] = sys.modules[__name__]
    raise SystemExit(main())
'@

$schema = @'
import sqlite3, sys, json
db = sqlite3.connect(sys.argv[1])
db.execute("CREATE TABLE aime_user (id INTEGER PRIMARY KEY, username TEXT, email TEXT, password TEXT, permissions INTEGER, created_date TEXT, last_login_date TEXT, suspend_expire_time TEXT)")
db.execute("CREATE TABLE aime_card (id INTEGER PRIMARY KEY AUTOINCREMENT, user INTEGER, access_code TEXT, idm TEXT, chip_id TEXT, created_date TEXT, last_login_date TEXT, is_locked INTEGER, is_banned INTEGER, memo TEXT)")
for row in json.loads(sys.argv[2]):
    db.execute("INSERT INTO aime_user (id, username, email, password, permissions) VALUES (?, ?, ?, '', 1)", (row[0], row[1], row[1] + "@fgo.local"))
    db.execute("INSERT INTO aime_card (user, access_code) VALUES (?, ?)", (row[0], row[2]))
db.commit()
'@

$work = Join-Path ([IO.Path]::GetTempPath()) ('fgoac-transfer-' + [guid]::NewGuid().ToString('N'))
function New-Pc([string]$Name, [object[]]$Accounts) {
    $root = Join-Path $work $Name
    $tools = Join-Path $root 'Server\tools'
    $state = Join-Path $root 'Server\state'
    New-Item -ItemType Directory -Path $tools, $state -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $tools 'fgo_account.py'), $stub, [Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath (Join-Path $CloudTools 'fgo_account_transfer.py') -Destination $tools
    $profiles = [ordered]@{}
    foreach ($account in $Accounts) { $profiles["aime:$($account[0])"] = [ordered]@{ aime_id = $account[0]; pd_id = $account[0]; master_name = $account[1]; auth_access_code = $account[2]; mstr_exp = 100 } }
    [IO.File]::WriteAllText((Join-Path $state 'fgo-players.json'), ($profiles | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
    $rows = ConvertTo-Json -Compress -InputObject @($Accounts | ForEach-Object { , @($_[0], $_[1], $_[2]) })
    & $Python -c $schema (Join-Path $state 'aime.sqlite') $rows
    return $root
}
function Invoke-Tool([string]$Root, [string[]]$Arguments) {
    $out = & $Python (Join-Path $Root 'Server\tools\fgo_account.py') @Arguments 2>&1 | ForEach-Object { "$_" }
    $text = ($out -join "`n").Trim()
    # Assigned inside try: a JsonObject written to the pipeline would be enumerated into its pairs.
    $node = $null
    try { $node = [Text.Json.Nodes.JsonNode]::Parse(($out | Select-Object -Last 1)) } catch { }
    return [pscustomobject]@{ Text = $text; Root = $node }
}
function Get-Profiles([string]$Root) { return Get-Content -LiteralPath (Join-Path $Root 'Server\state\fgo-players.json') -Raw -Encoding UTF8 | ConvertFrom-Json }

try {
    $launcher = [Reflection.Assembly]::LoadFrom($assembly)
    # Every message box, dialog and window of the launcher goes to the sink, never to the screen.
    . (Join-Path $PSScriptRoot 'DialogSink.ps1'); Enable-DialogSink $assembly
    $transfer = $launcher.GetType('FGOLocalPlatform.AccountTransfer')
    if (!$transfer) { Test-Result 'AccountTransfer' $false 'FGOLocalPlatform.AccountTransfer is missing'; throw 'stop' }
    $flags = [Reflection.BindingFlags]'NonPublic, Public, Static'
    function Call([string]$Method, [object[]]$Arguments) {
        $values = [object[]]::new($Arguments.Count)
        for ($i = 0; $i -lt $Arguments.Count; $i++) { if ($Arguments[$i] -is [psobject]) { $values[$i] = $Arguments[$i].psobject.BaseObject } else { $values[$i] = $Arguments[$i] } }
        return $transfer.GetMethod($Method, $flags).Invoke($null, $values)
    }

    $here = New-Pc 'here' @(@(1, 'Arthur', '00000000000000000001'), @(2, 'Gudako', '00000000000000000002'))
    $there = New-Pc 'there' @(@(1, 'Ritsuka', '00000000000000000003'), @(5, 'Gudako old', '00000000000000000002'))
    $file = Join-Path $work 'all.fgoaccounts.json'

    $export = Invoke-Tool $here ([string[]](Call 'ExportArguments' @($file)))
    Test-Result 'export all' ($export.Root -and $export.Root['ok'] -and $export.Root['ok'].GetValue[bool]() -and $export.Root['count'].GetValue[int]() -eq 2 -and (Test-Path -LiteralPath $file)) $export.Text

    $previewRun = Invoke-Tool $there ([string[]](Call 'PreviewArguments' @($file)))
    $preview = if ($previewRun.Root -and $previewRun.Root['ok'].GetValue[bool]()) { Call 'ReadPreview' @(, $previewRun.Root) } else { $null }
    $confirm = if ($preview) { [string](Call 'ConfirmText' @(, $preview)) } else { '' }
    Test-Result 'preview names the Master whose card is already here' ($null -ne $preview -and $preview.Count -eq 2 -and ($preview.Overwrites -join ',') -ceq 'Gudako' -and $preview.Token.Length -eq 64 -and $confirm.Contains("save:`nGudako") -and $confirm.Contains('2 accounts') -and $confirm -match 'backed up') "$($previewRun.Text) | $($confirm -replace "`n", ' / ')"

    $import = Invoke-Tool $there ([string[]](Call 'ImportArguments' @($file, $preview.Token)))
    $after = Get-Profiles $there
    $ids = @($after.PSObject.Properties | ForEach-Object { "$($_.Value.aime_id):$($_.Value.master_name)" } | Sort-Object)
    $backup = if ($import.Root -and $import.Root['backup']) { [string]$import.Root['backup'] } else { '' }
    Test-Result 'import keeps the others, renumbers a taken number, backs up' ($import.Root -and $import.Root['ok'].GetValue[bool]() -and ($ids -join ',') -ceq '1:Ritsuka,2:Arthur,5:Gudako' -and $backup.Length -gt 0 -and (Test-Path -LiteralPath (Join-Path $backup 'snapshot.json'))) "$($ids -join ', '); $($import.Text)"
    $doneText = [string](Call 'ImportedText' @(, $import.Root))
    Test-Result 'import result in one sentence' ($doneText.Contains('2 accounts') -and $doneText.Contains($backup)) $doneText

    $stale = Invoke-Tool $there ([string[]](Call 'ImportArguments' @($file, $preview.Token)))
    $staleText = [string](Call 'Explain' @([string]$stale.Root['message']))
    Test-Result 'a token from before a change is refused in English' ($stale.Root -and !$stale.Root['ok'].GetValue[bool]() -and $staleText -match 'changed' -and $staleText -notmatch '[\u4e00-\u9fff]') $staleText

    $again = Invoke-Tool $here ([string[]](Call 'ExportArguments' @($file)))
    $againText = [string](Call 'Explain' @([string]$again.Root['message']))
    Test-Result 'an export over an existing file is refused in English' ($again.Root -and !$again.Root['ok'].GetValue[bool]() -and $againText -match 'already' -and $againText -notmatch '[\u4e00-\u9fff]') $againText

    # His export refuses an account with no database identity or card, and a duplicated Aime save, in Chinese.
    $hisTool = [IO.File]::ReadAllText((Join-Path $here 'Server\tools\fgo_account_transfer.py'))
    foreach ($message in '账号缺少数据库身份或卡码', '重复 Aime 存档') {
        $text = [string](Call 'Explain' @($message))
        Test-Result "his '$message' explained in English" ($hisTool.Contains($message) -and $text -notmatch '[一-鿿]' -and $text.Length -gt 20) $text
    }

    $env:STUB_SERVER = '1'
    try { $busy = Invoke-Tool $here ([string[]](Call 'ExportArguments' @((Join-Path $work 'busy.fgoaccounts.json')))) } finally { Remove-Item Env:STUB_SERVER }
    Test-Result 'his server-running answer is known' ($busy.Root -and !$busy.Root['ok'].GetValue[bool]() -and (Call 'ServerRunning' @(, $busy.Root))) $busy.Text

    $offered = Call 'Offered' @((Join-Path $here 'Server'))
    Remove-Item -LiteralPath (Join-Path $here 'Server\tools\fgo_account_transfer.py')
    $notOffered = Call 'Offered' @((Join-Path $here 'Server'))
    Test-Result 'offered where his transfer tool is installed' ($offered -and !$notOffered) "with it: $offered, without: $notOffered"
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
