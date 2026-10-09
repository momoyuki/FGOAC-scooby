<#
Checks that the updater installs only a release signed with a key in ReleaseKeys, without starting
the launcher or the game.

  pwsh -File tools\Test-Updater.ps1

It makes a primary, a backup and an unlisted test key with Sign-Release.ps1 in a throwaway folder
under %TEMP%, puts the primary and backup public keys into ReleaseKeys of the loaded launcher
assembly (src\bin\Release, so run build.cmd first) in place of the embedded ones, and serves each
case's release assets from a stub HTTP server on 127.0.0.1. For each case the launcher's own Updater
parses a releases/latest answer naming those assets and installs it into a throwaway game folder,
and the check reads back which files are there. The zip's Apply-EN-Patch.ps1 is a stub that leaves
a marker file. The updater's staging folder and log go into the throwaway folder as well: TMP, TEMP
and the assembly's base directory point there for the run.

Cases: signed with the primary key, with the backup key, without a .zip.sha256, with a two-line .sig
whose second line verifies, and by Sign-Release.ps1 itself (run from a copy of the repository layout
whose source lists the test keys) install; no .sig, a malformed .sig, a .sig over 4 KB, a .sig whose
lines all fail, one byte changed in the zip, a signature over another version, a signature by an
unlisted key, an entry outside the game folder and an entry on a protected path are refused with
nothing written; so is an entry in each folder and file of the protected set, the update backup and
the settings file's .bak, written directly or reached through ..\. While a program runs from App\ or
Server\ of the game folder, standing in for the game or the local server, the update is refused
before anything is downloaded. A zip named for another version than the tag, or an asset name carrying ..\, is
not offered and never downloaded. A ReleaseKeys entry on a curve the PC does not support is skipped:
the backup key still installs, and a release only that entry would verify is refused. Every staging
folder is gone afterwards. Sign-Release.ps1 must refuse, and leave no .sig, when no key in the
source's ReleaseKeys verifies, and -Release must fail a release signed with a test key against the
embedded keys. A release carrying the add-on (payload\App\fgoacaddon.dll) installs. package.ps1, run from a
copy of the repository layout with placeholder files, puts the -AddOnDll file in the payload as
App\fgoacaddon.dll and lists it in manifest.json; without -AddOnDll it leaves both out and says so in one
line, and a missing -AddOnDll file stops it with exit code 2.

  pwsh -File tools\Test-Updater.ps1 -Release release\FGOAC-scooby-vX.Y.Z.zip

-Release checks a real release before it is published instead: the zip and its .sig, read as the
launcher reads it (raw bytes, at most 4 KB, ASCII), against the ReleaseKeys built into the launcher
assembly, unchanged, through the updater's own IsSigned, with X.Y.Z taken from the zip name as the
tag will give it.

Exit codes: 0 every check passed, 1 a check failed, 2 a prerequisite is missing.
#>
[CmdletBinding()]
param(
    [string]$Release,
    [switch]$KeepFiles
)
$ErrorActionPreference = 'Stop'

$repository = Split-Path -Parent $PSScriptRoot
$assembly = Join-Path $repository 'src\bin\Release\net6.0-windows\FGOLocalPlatform.dll'
$signScript = Join-Path $PSScriptRoot 'Sign-Release.ps1'
if ($PSVersionTable.PSVersion.Major -lt 7) { Write-Host 'Run this with pwsh 7: the launcher assembly targets .NET 6.'; exit 2 }
if (!(Test-Path -LiteralPath $assembly)) { Write-Host "Missing $assembly. Run build.cmd first."; exit 2 }
# Every message box, dialog and window of the launcher goes to the sink, never to the screen.
. (Join-Path $PSScriptRoot 'DialogSink.ps1'); Enable-DialogSink $assembly

$script:failed = 0
function Test-Result([string]$Name, [bool]$Passed, [string]$Detail) {
    Write-Host ("{0} {1}: {2}" -f $(if ($Passed) { 'PASS' } else { 'FAIL' }), $Name, $Detail)
    if (!$Passed) { $script:failed++ }
}

if ($Release) {
    $name = [IO.Path]::GetFileName($Release)
    if ($name -notmatch '^FGOAC-scooby-v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)\.zip$') { Write-Host "$name is not named FGOAC-scooby-vX.Y.Z.zip with X, Y and Z written without leading zeros."; exit 2 }
    if (!(Test-Path -LiteralPath $Release)) { Write-Host "Missing $Release."; exit 2 }
    $releaseVersion = '{0}.{1}.{2}' -f [int]$Matches[1], [int]$Matches[2], [int]$Matches[3]
    $isSigned = [Reflection.Assembly]::LoadFrom($assembly).GetType('FGOLocalPlatform.Updater', $true).GetMethod('IsSigned', [Reflection.BindingFlags]'NonPublic, Static')
    $hash = (Get-FileHash -LiteralPath $Release -Algorithm SHA256).Hash.ToLowerInvariant()
    # Read as the launcher reads it: raw bytes, nothing over 4 KB, decoded as ASCII.
    [byte[]]$bytes = @()
    if (Test-Path -LiteralPath "$Release.sig") { $bytes = [IO.File]::ReadAllBytes("$Release.sig") }
    $signature = ''
    if ($bytes.Length -le 4096) { $signature = [Text.Encoding]::ASCII.GetString($bytes) } else { Write-Host "$name.sig is over 4 KB, which the launcher refuses." }
    Test-Result 'release signature' ([bool]$isSigned.Invoke($null, @($releaseVersion, $hash, $signature))) "$name.sig for version $releaseVersion and SHA-256 $hash under the ReleaseKeys built into $assembly"
    exit $(if ($script:failed -gt 0) { 1 } else { 0 })
}

Add-Type -AssemblyName System.IO.Compression.ZipFile

# Collects the updater's report lines on the thread-pool threads that raise them.
Add-Type -TypeDefinition @'
public sealed class UpdateReportSink
{
    public readonly System.Collections.Concurrent.ConcurrentQueue<string> Lines = new System.Collections.Concurrent.ConcurrentQueue<string>();
    public void Report(string message) { Lines.Enqueue(message); }
}
'@

$work = Join-Path $env:TEMP ('fgoac-updater-check-' + [Guid]::NewGuid().ToString('N'))
$assets = Join-Path $work 'assets'
$savedTmp = $env:TMP
$savedTemp = $env:TEMP

$version = '9.9.0'
$zipName = "FGOAC-scooby-v$version.zip"
$passphrase = ConvertTo-SecureString 'check-only passphrase' -AsPlainText -Force
$unverified = 'The update could not be verified as signed by FGOAC scooby, so it was not installed. Download it from the releases page, or wait for a signed release.'
$seen = [Collections.ArrayList]::Synchronized([Collections.ArrayList]::new())

# The asset host: GET /<case>/<name> answers the file under assets\<case>, anything else 404.
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$stub = [PowerShell]::Create()
[void]$stub.AddScript({
    param($Listener, $Root, $Seen)
    while ($true) {
        try { $client = $Listener.AcceptTcpClient() } catch { return }
        try {
            $stream = $client.GetStream()
            $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::ASCII, $false, 1024, $true)
            $request = $reader.ReadLine()
            while ($reader.ReadLine()) { }
            $path = [Uri]::UnescapeDataString(($request -split ' ')[1]).TrimStart('/')
            [void]$Seen.Add($path)
            $file = Join-Path $Root $path
            if ([IO.File]::Exists($file)) { $status = '200 OK'; $body = [IO.File]::ReadAllBytes($file) }
            else { $status = '404 Not Found'; $body = [byte[]]::new(0) }
            $head = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 $status`r`nContent-Type: application/octet-stream`r`nContent-Length: $($body.Length)`r`nConnection: close`r`n`r`n")
            $stream.Write($head, 0, $head.Length)
            $stream.Write($body, 0, $body.Length)
            $stream.Flush()
        }
        catch { [void]$Seen.Add('stub error: ' + $_.Exception.Message) }
        finally { $client.Dispose() }
    }
}).AddArgument($listener).AddArgument($assets).AddArgument($seen)

function New-Key([string]$Name) {
    $path = Join-Path $work "keys\$Name.pem"
    $output = @(& $signScript -NewKey $path -Passphrase $passphrase *>&1 | ForEach-Object { "$_" })
    $key = [Security.Cryptography.ECDsa]::Create()
    $key.ImportFromEncryptedPem([char[]][IO.File]::ReadAllText($path), [char[]]'check-only passphrase')
    $public = [Convert]::ToBase64String($key.ExportSubjectPublicKeyInfo())
    Test-Result "new $Name key" ($LASTEXITCODE -eq 0 -and $output[-1] -eq $public) "Sign-Release.ps1 -NewKey exit $LASTEXITCODE, printed public key matches the PEM: $($output[-1] -eq $public)"
    return [pscustomobject]@{ Path = $path; Key = $key; Public = $public }
}

function New-Zip([string]$Path, [Collections.IDictionary]$Entries) {
    $zip = [IO.Compression.ZipFile]::Open($Path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in $Entries.Keys) {
            $writer = [IO.StreamWriter]::new($zip.CreateEntry($name).Open())
            try { $writer.Write($Entries[$name]) } finally { $writer.Dispose() }
        }
    }
    finally { $zip.Dispose() }
}

function Get-Signature($Key, [string]$StatementVersion, [string]$Zip) {
    $hash = (Get-FileHash -LiteralPath $Zip -Algorithm SHA256).Hash.ToLowerInvariant()
    $statement = [Text.Encoding]::ASCII.GetBytes("FGOAC-scooby $StatementVersion $hash")
    return [Convert]::ToBase64String($Key.Key.SignData($statement, [Security.Cryptography.HashAlgorithmName]::SHA256))
}

function Get-Files([string]$Root) {
    return Get-ChildItem -LiteralPath $Root -Recurse -File | ForEach-Object { [IO.Path]::GetRelativePath($Root, $_.FullName) } | Sort-Object | Join-String -Separator ', '
}

$installer = @'
param([string]$InstallRoot, [string]$PackageRoot, [switch]$NonInteractive, [int]$IgnoreProcessId)
Set-Content -LiteralPath (Join-Path $InstallRoot 'installer-ran.txt') -Value 'ran'
'stub installer ran'
exit 0
'@
$package = [ordered]@{ 'Apply-EN-Patch.ps1' = $installer; 'payload/App/new.txt' = 'new'; 'manifest.json' = '{}' }
$installed = 'App\fgo-launcher.json', 'Apply-EN-Patch.ps1', 'installer-ran.txt', 'manifest.json', 'payload\App\new.txt' | Sort-Object | Join-String -Separator ', '
$untouched = 'App\fgo-launcher.json'

# One release per case: its zip, .sig and .sha256 under assets\<case>, a game folder under
# cases\<case> holding only App\fgo-launcher.json, then the updater's parse and install.
function Invoke-Case([string]$Case, [Collections.IDictionary]$Entries, [scriptblock]$Sign, [switch]$NoSig, [switch]$NoSha256, [switch]$Tamper, [string]$AssetName = $zipName, [string]$Running) {
    $folder = Join-Path $assets $Case
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    $zip = Join-Path $folder $zipName
    New-Zip $zip $Entries
    $list = [Collections.Generic.List[object]]::new()
    if (!$NoSig) {
        [IO.File]::WriteAllText("$zip.sig", (& $Sign $zip), [Text.Encoding]::ASCII)
        $list.Add(@{ name = "$AssetName.sig"; browser_download_url = "http://127.0.0.1:$port/$Case/$zipName.sig" })
    }
    if (!$NoSha256) {
        $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
        [IO.File]::WriteAllText("$zip.sha256", "$hash *$zipName")
        $list.Add(@{ name = "$AssetName.sha256"; browser_download_url = "http://127.0.0.1:$port/$Case/$zipName.sha256" })
    }
    $list.Add(@{ name = $AssetName; browser_download_url = "http://127.0.0.1:$port/$Case/$zipName" })
    if ($Tamper) {
        $bytes = [IO.File]::ReadAllBytes($zip)
        $bytes[[int]($bytes.Length / 2)] = $bytes[[int]($bytes.Length / 2)] -bxor 1
        [IO.File]::WriteAllBytes($zip, $bytes)
    }

    $root = Join-Path $work "cases\$Case"
    New-Item -ItemType Directory -Path (Join-Path $root 'App') -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $root 'App\fgo-launcher.json') -Value 'original'
    $json = @{ tag_name = "v$version"; assets = $list } | ConvertTo-Json -Depth 4
    $release = $script:updater::ParseRelease($json)
    $sink = [UpdateReportSink]::new()
    $ok = $false
    $process = $null
    if ($Running) {
        # PING.EXE copied to the path of the game or the local server stands in for it.
        $program = Join-Path $root $Running
        New-Item -ItemType Directory -Path (Split-Path -Parent $program) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $env:SystemRoot 'System32\PING.EXE') -Destination $program
        $process = Start-Process -FilePath $program -ArgumentList '-n', '60', '127.0.0.1' -WindowStyle Hidden -PassThru
    }
    try {
        if ($release) {
            $timeout = [Threading.CancellationTokenSource]::new([TimeSpan]::FromSeconds(60))
            try { $ok = $script:updater::InstallAsync($release, $root, [Delegate]::CreateDelegate([Action[string]], $sink, 'Report'), $timeout.Token).GetAwaiter().GetResult() }
            finally { $timeout.Dispose() }
        }
    }
    finally { if ($process) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue; $process.WaitForExit() } }
    # The running program's copy is part of the case, not of what the update wrote.
    if ($process) { Remove-Item -LiteralPath $program -Force }
    return [pscustomobject]@{
        Offered = [bool]$release
        Installed = $ok
        Files = Get-Files $root
        Lines = @($sink.Lines)
        Settings = (Get-Content -LiteralPath (Join-Path $root 'App\fgo-launcher.json') -Raw).Trim()
        Root = $root
    }
}

function Test-Installed([string]$Name, $Result) {
    Test-Result $Name ($Result.Installed -and $Result.Files -eq $installed) "installed $($Result.Installed); files: $($Result.Files)"
}

function Test-Refused([string]$Name, $Result, [string]$Expected) {
    $said = $Result.Lines -like "*$Expected*"
    Test-Result $Name (!$Result.Installed -and $Result.Files -eq $untouched -and $Result.Settings -eq 'original' -and $said.Count -ge 1) "installed $($Result.Installed); files: $($Result.Files); said: $($Result.Lines[-1])"
}

function Test-NotOffered([string]$Name, [string]$Case, $Result) {
    $asked = @($seen -like "$Case/*")
    Test-Result $Name (!$Result.Offered -and $asked.Count -eq 0 -and $Result.Files -eq $untouched) "offered $($Result.Offered); requests: $($asked.Count); files: $($Result.Files)"
}

try {
    foreach ($folder in @('keys', 'tmp', 'assets', 'cases', 'launcher')) {
        New-Item -ItemType Directory -Path (Join-Path $work $folder) -Force | Out-Null
    }
    $env:TMP = Join-Path $work 'tmp'
    $env:TEMP = $env:TMP
    # GamePaths resolves the game root, and with it logs\update.log, from the base directory.
    [AppDomain]::CurrentDomain.SetData('APP_CONTEXT_BASE_DIRECTORY', (Join-Path $work 'launcher') + '\')
    $listener.Start()
    $port = $listener.LocalEndpoint.Port
    $stubRun = $stub.BeginInvoke()

    $loaded = [Reflection.Assembly]::LoadFrom($assembly)
    $script:updater = $loaded.GetType('FGOLocalPlatform.Updater', $true)
    $settings = $loaded.GetType('FGOLocalPlatform.UpdateSettings', $true)
    $logs = $loaded.GetType('FGOLocalPlatform.GamePaths', $true)::LogsRoot
    if ($logs -ne (Join-Path $work 'logs')) { Write-Host "The updater log would go to $logs, outside the throwaway folder."; exit 2 }
    $updater::Log('card 12345678901234567890 key ABCD-EFGH-IJKL-MNOP-QRST-UVWX-YZ23-4567')
    $logged = Get-Content -LiteralPath (Join-Path $logs 'update.log') -Raw
    Test-Result 'update log redacted at write' ($logged -match 'card <redacted> key <redacted>' -and $logged -notmatch '12345678901234567890|ABCD-EFGH') $logged.Trim()
    Write-Host "Stub asset host on 127.0.0.1:$port; throwaway folder $work"

    $primary = New-Key 'primary'
    $backup = New-Key 'backup'
    $unlisted = New-Key 'unlisted'
    $settings::ReleaseKeys[0] = $primary.Public
    $settings::ReleaseKeys[1] = $backup.Public

    $result = Invoke-Case 'primary' $package { param($Zip) Get-Signature $primary $version $Zip }
    Test-Installed 'signed with the primary key' $result
    $withAddOn = [ordered]@{} + $package; $withAddOn['payload/App/fgoacaddon.dll'] = 'add-on'
    $result = Invoke-Case 'add-on' $withAddOn { param($Zip) Get-Signature $primary $version $Zip }
    $expected = @($installed -split ', ') + 'payload\App\fgoacaddon.dll' | Sort-Object | Join-String -Separator ', '
    Test-Result 'release carrying the add-on' ($result.Installed -and $result.Files -eq $expected) "installed $($result.Installed); files: $($result.Files)"
    $result = Invoke-Case 'backup' $package { param($Zip) Get-Signature $backup $version $Zip }
    Test-Installed 'signed with the backup key' $result
    $result = Invoke-Case 'no-sha256' $package { param($Zip) Get-Signature $primary $version $Zip } -NoSha256
    Test-Installed 'no .zip.sha256' $result
    $result = Invoke-Case 'two-lines' $package { param($Zip) (Get-Signature $unlisted $version $Zip) + "`r`n" + (Get-Signature $backup $version $Zip) }
    Test-Installed 'two .sig lines, the second verifies' $result
    $isSigned = $script:updater.GetMethod('IsSigned', [Reflection.BindingFlags]'NonPublic, Static')
    $zip = Join-Path $assets "backup\$zipName"
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    Test-Result 'IsSigned by reflection, as -Release calls it' ([bool]$isSigned.Invoke($null, @($version, $hash, [IO.File]::ReadAllText("$zip.sig")))) "backup case zip, SHA-256 $hash"

    # Sign-Release.ps1 run from a copy of the repository layout whose source lists the test keys.
    $copy = Join-Path $work 'repo'
    New-Item -ItemType Directory -Path (Join-Path $copy 'tools'), (Join-Path $copy 'src\FGOLocalPlatform') -Force | Out-Null
    Copy-Item -LiteralPath $signScript -Destination (Join-Path $copy 'tools')
    Set-Content -LiteralPath (Join-Path $copy 'src\FGOLocalPlatform\UpdateSettings.cs') -Value "public static readonly string[] ReleaseKeys = { `"$($primary.Public)`", `"$($backup.Public)`" };"
    $script:signed = $null
    $result = Invoke-Case 'sign-script' $package {
        param($Zip)
        $script:signed = @(& (Join-Path $copy 'tools\Sign-Release.ps1') -Zip $Zip -Key $unlisted.Path, $backup.Path -Passphrase $passphrase *>&1 | ForEach-Object { "$_" })
        $script:signedExit = $LASTEXITCODE
        [IO.File]::ReadAllText("$Zip.sig")
    }
    $lines = @(([IO.File]::ReadAllText((Join-Path $assets "sign-script\$zipName.sig"))) -split "`n")
    Test-Result 'sign script, two keys' ($script:signedExit -eq 0 -and $lines.Count -eq 2) "exit $($script:signedExit), .sig lines: $($lines.Count); $($script:signed[-1])"
    Test-Installed 'signed by the sign script' $result

    $result = Invoke-Case 'no-sig' $package $null -NoSig
    Test-Refused 'no .sig' $result $unverified
    $asked = @($seen -like 'no-sig/*')
    Test-Result 'no .sig, no download' ($asked.Count -eq 0 -and $seen -contains "primary/$zipName") "requests for this release: $($asked.Count); the primary case's zip was requested: $($seen -contains "primary/$zipName")"
    $result = Invoke-Case 'malformed' $package { param($Zip) 'not a signature' }
    Test-Refused 'malformed .sig' $result $unverified
    $result = Invoke-Case 'short' $package { param($Zip) [Convert]::ToBase64String([byte[]]::new(32)) }
    Test-Refused 'short .sig' $result $unverified
    $result = Invoke-Case 'tampered' $package { param($Zip) Get-Signature $primary $version $Zip } -Tamper
    Test-Refused 'one byte changed' $result $unverified
    $result = Invoke-Case 'other-version' $package { param($Zip) Get-Signature $primary '9.8.0' $Zip }
    Test-Refused 'signed for another version' $result $unverified
    $result = Invoke-Case 'unlisted' $package { param($Zip) Get-Signature $unlisted $version $Zip }
    Test-Refused 'signed by an unlisted key' $result $unverified
    $result = Invoke-Case 'all-lines-fail' $package { param($Zip) (Get-Signature $unlisted $version $Zip) + "`n" + (Get-Signature $primary '9.8.0' $Zip) + "`nnot a signature" }
    Test-Refused 'every .sig line fails' $result $unverified
    $result = Invoke-Case 'oversized' $package { param($Zip) (Get-Signature $primary $version $Zip) + "`n" + ('A' * 4200) }
    Test-Refused '.sig over 4 KB' $result $unverified
    # The primary key's curve OID 1.2.840.10045.3.1.7 (P-256) changed to the unknown 1.2.840.10045.3.1.99.
    $hex = [Convert]::ToHexString([Convert]::FromBase64String($primary.Public))
    $settings::ReleaseKeys[0] = [Convert]::ToBase64String([Convert]::FromHexString($hex.Replace('06082A8648CE3D030107', '06082A8648CE3D030163')))
    try {
        $result = Invoke-Case 'unknown-curve-backup' $package { param($Zip) Get-Signature $backup $version $Zip }
        Test-Installed 'ReleaseKeys entry on an unknown curve, the backup key still verifies' $result
        $result = Invoke-Case 'unknown-curve-primary' $package { param($Zip) Get-Signature $primary $version $Zip }
        Test-Refused 'ReleaseKeys entry on an unknown curve, nothing else verifies' $result $unverified
    }
    finally { $settings::ReleaseKeys[0] = $primary.Public }

    $result = Invoke-Case 'zip-for-other-version' $package { param($Zip) Get-Signature $primary '9.8.0' $Zip } -AssetName 'FGOAC-scooby-v9.8.0.zip'
    Test-NotOffered 'zip named for another version than the tag' 'zip-for-other-version' $result
    $result = Invoke-Case 'traversal' $package { param($Zip) Get-Signature $primary $version $Zip } -AssetName "FGOAC-scooby-v$version\..\..\..\traversal.zip"
    Test-NotOffered 'asset name carrying ..\' 'traversal' $result
    $stray = @(Get-ChildItem -LiteralPath $work -Recurse -Filter 'traversal.zip')
    Test-Result 'asset name carrying ..\, nothing created' ($stray.Count -eq 0) "traversal.zip files under the throwaway folder: $($stray.Count)"

    $escaping = [ordered]@{} + $package; $escaping['../escaped.txt'] = 'out'
    $result = Invoke-Case 'escaping' $escaping { param($Zip) Get-Signature $primary $version $Zip }
    Test-Refused 'entry outside the game folder' $result 'lists a path outside the game folder'
    Test-Result 'entry outside the game folder, not written' (!(Test-Path -LiteralPath (Join-Path (Split-Path -Parent $result.Root) 'escaped.txt'))) 'no escaped.txt beside the game folder'
    $protected = [ordered]@{} + $package; $protected['App/fgo-launcher.json'] = 'replaced'
    $result = Invoke-Case 'protected' $protected { param($Zip) Get-Signature $primary $version $Zip }
    Test-Refused 'entry on a protected path' $result 'not allowed to write: App/fgo-launcher.json'
    # Every folder and file of the protected set, the update backup, a .bak the launcher keeps, and
    # entries that reach the set through ..\ from a folder a release may write.
    $protectedEntries = 'Server/data/fgo-master/players.ibd', 'Server/state/fgo-players.json', 'DEVICE/aime.txt',
        'DEVICE/community/play.example.org/aime.txt', 'DEVICE/print/players/aime-1/print.png', 'App/deck.json',
        'App/deck-loadouts/Mine.json', 'DEVICE/runtime/segatools.runtime.ini', 'DEVICE/runtime/bindings.ini', 'backup/pre-9.9.0/manifest.json',
        'App/fgo-launcher.json.bak', 'App/zh/../deck.json', 'payload/../Server/state/fgo-players.json', 'App/../DEVICE/aime.txt',
        'App/1_MOD/Packages/0123456789abcdef0123456789abcdef/BGM/runtime.ini', 'App/1_MOD/runtime.ini'
    $index = 0
    foreach ($entry in $protectedEntries) {
        $index++
        $protected = [ordered]@{} + $package; $protected[$entry] = 'replaced'
        $result = Invoke-Case "protected-$index" $protected { param($Zip) Get-Signature $primary $version $Zip }
        Test-Refused "entry on a protected path, $entry" $result "not allowed to write: $entry"
    }

    # A program running from App\ or Server\ of the game folder is the game or the local server.
    $stopFirst = 'Stop the game and the local server, then install the update.'
    foreach ($program in 'App\ago.exe', 'Server\mariadb\bin\mariadbd.exe') {
        $case = 'running-' + [IO.Path]::GetFileNameWithoutExtension($program)
        $result = Invoke-Case $case $package { param($Zip) Get-Signature $primary $version $Zip } -Running $program
        Test-Refused "$program running from the game folder" $result $stopFirst
        $asked = @($seen -like "$case/*")
        Test-Result "$program running from the game folder, no download" ($asked.Count -eq 0) "requests for this release: $($asked.Count)"
    }

    $staged = @(Get-ChildItem -LiteralPath $env:TMP -Directory -Filter 'FGOAC-scooby-update*')
    Test-Result 'staging folders removed' ($staged.Count -eq 0) "left in TMP: $($staged.Count)"

    # package.ps1 from a copy of the repository layout: placeholder launcher, guides, shims, game files and add-on.
    $packageRepo = Join-Path $work 'package-repo'
    $gameFiles = Join-Path $work 'package-game'
    $addOnBuild = Join-Path $work 'addon-build\fgoacaddon.dll'
    foreach ($file in 'package.ps1', 'patch\Build-Manifest.ps1', 'package\README.md', 'overlay') {
        New-Item -ItemType Directory -Path (Split-Path -Parent (Join-Path $packageRepo $file)) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $repository $file) -Destination (Join-Path $packageRepo $file) -Recurse
    }
    $placeholders = 'dist\FGOAC scooby.exe', 'patch\Apply-EN-Patch.ps1', 'docs\GUIDE_EN.md', 'docs\GUIDE_EN.pdf', 'CHANGELOG.md',
        'compat\amd-shim\opengl32.dll', 'compat\amd-shim\amdcfg\amdOglpSettings.cfg', 'compat\amd-shim\LICENSE', 'compat\fgoglcompat.dll', 'compat\fgoglcompat-1.1.0.dll'
    foreach ($file in @($placeholders | ForEach-Object { Join-Path $packageRepo $_ }) + (Join-Path $gameFiles 'App\zh\text.bin') + (Join-Path $gameFiles 'App\Localization\en-fgoac\language.json') + $addOnBuild) {
        New-Item -ItemType Directory -Path (Split-Path -Parent $file) -Force | Out-Null
        Set-Content -LiteralPath $file -Value "placeholder $([IO.Path]::GetFileName($file))"
    }
    $packageScript = Join-Path $packageRepo 'package.ps1'
    $packageRoot = Join-Path $packageRepo "release\FGOAC-scooby-v$version"
    $payloadAddOn = Join-Path $packageRoot 'payload\App\fgoacaddon.dll'
    $addOnHash = (Get-FileHash -LiteralPath $addOnBuild -Algorithm SHA256).Hash.ToLowerInvariant()
    $output = @(& (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File $packageScript -GameRoot $gameFiles -Version $version -SkipZip -AddOnDll $addOnBuild *>&1 | ForEach-Object { "$_" })
    $exit = $LASTEXITCODE
    $listed = (Get-Content -LiteralPath (Join-Path $packageRoot 'manifest.json') -Raw | ConvertFrom-Json).files.'App\fgoacaddon.dll'
    $copied = (Test-Path -LiteralPath $payloadAddOn) -and (Get-FileHash -LiteralPath $payloadAddOn -Algorithm SHA256).Hash.ToLowerInvariant() -eq $addOnHash
    Test-Result 'package with -AddOnDll' ($exit -eq 0 -and $copied -and $listed -eq $addOnHash) "exit $exit; payload\App\fgoacaddon.dll is the build: $copied; manifest lists it as $listed"
    $output = @(& (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File $packageScript -GameRoot $gameFiles -Version $version -SkipZip *>&1 | ForEach-Object { "$_" })
    $exit = $LASTEXITCODE
    $manifest = Get-Content -LiteralPath (Join-Path $packageRoot 'manifest.json') -Raw | ConvertFrom-Json
    $said = @($output -ceq 'The add-on is not included: pass -AddOnDll with the path of fgoacaddon.dll to include it.')
    Test-Result 'package without -AddOnDll' ($exit -eq 0 -and !(Test-Path -LiteralPath $payloadAddOn) -and $null -eq $manifest.files.'App\fgoacaddon.dll' -and $said.Count -eq 1) "exit $exit; payload\App\fgoacaddon.dll left: $(Test-Path -LiteralPath $payloadAddOn); in the manifest: $($null -ne $manifest.files.'App\fgoacaddon.dll'); said once: $($said.Count -eq 1)"
    $output = @(& (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File $packageScript -GameRoot $gameFiles -Version $version -SkipZip -AddOnDll (Join-Path $work 'missing\fgoacaddon.dll') *>&1 | ForEach-Object { "$_" })
    Test-Result 'package with a missing -AddOnDll' ($LASTEXITCODE -eq 2 -and ($output -like 'The add-on is missing: *').Count -eq 1) "exit $LASTEXITCODE; $($output[-1])"
    $sets = @('App\zh\text.bin', 'App\Localization\en-fgoac\language.json')
    $carried = @($sets | Where-Object { (Test-Path -LiteralPath (Join-Path $packageRoot "payload\$_")) -and $null -ne $manifest.files.$_ })
    Test-Result 'package carries both English sets' ($carried.Count -eq 2) "in the payload and the manifest: $($carried -join ', ')"
    Rename-Item -LiteralPath (Join-Path $gameFiles 'App\Localization\en-fgoac') -NewName 'en-fgoac-away'
    $output = @(& (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File $packageScript -GameRoot $gameFiles -Version $version -SkipZip *>&1 | ForEach-Object { "$_" })
    Test-Result 'package without the English set for the new build' ($LASTEXITCODE -eq 2 -and ($output -like 'The English game files for the new build are missing: *').Count -eq 1) "exit $LASTEXITCODE; $($output[-1])"
    Rename-Item -LiteralPath (Join-Path $gameFiles 'App\Localization\en-fgoac-away') -NewName 'en-fgoac'

    # -Release in a fresh process, so the embedded ReleaseKeys are the ones checked.
    $zip = Join-Path $assets "backup\$zipName"
    $output = @(& (Join-Path $PSHOME 'pwsh.exe') -NoProfile -File $PSCommandPath -Release $zip 2>&1 | ForEach-Object { "$_" })
    Test-Result 'pre-publish check, test key against the embedded keys' ($LASTEXITCODE -eq 1 -and ($output -like 'FAIL release signature: *').Count -eq 1) "exit $LASTEXITCODE; $($output -join ' | ')"

    # Sign-Release.ps1 with keys the source does not list: the statement is printed, no .sig is left.
    $zip = Join-Path $assets "primary\$zipName"
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    $output = @(& $signScript -Zip $zip -Key $primary.Path, $backup.Path -Passphrase $passphrase *>&1 | ForEach-Object { "$_" })
    Test-Result 'sign script, keys not in ReleaseKeys' ($LASTEXITCODE -eq 1 -and !(Test-Path -LiteralPath "$zip.sig") -and $output -contains "Statement: FGOAC-scooby $version $hash") "exit $LASTEXITCODE, .sig left: $(Test-Path -LiteralPath "$zip.sig"); $($output -join ' | ')"
    $inside = Join-Path $repository 'check-key.pem'
    $output = @(& $signScript -NewKey $inside -Passphrase $passphrase *>&1 | ForEach-Object { "$_" })
    Test-Result 'sign script, key inside the repository' ($LASTEXITCODE -eq 1 -and !(Test-Path -LiteralPath $inside)) "exit $LASTEXITCODE; $($output -join ' | ')"
}
finally {
    # Also runs when the setup at the top of the try stopped part way.
    $listener.Stop()
    if ($stubRun) { [void]$stub.EndInvoke($stubRun) }
    $stub.Dispose()
    $env:TMP = $savedTmp
    $env:TEMP = $savedTemp
    if ($KeepFiles) { Write-Host "Kept $work" } elseif (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}

$sink = Get-DialogSink
Test-Result 'no window shown' ($sink.Shown -eq 0) $sink.Detail
if ($script:failed -gt 0) { Write-Host "$script:failed check(s) failed."; exit 1 }
Write-Host 'Every check passed.'
exit 0
