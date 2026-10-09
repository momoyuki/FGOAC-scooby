<#
Checks the community server path end to end without starting the game.

  pwsh -File tools\Test-CommunityServer.ps1 -GameRoot D:\games\FGOA

It builds a throwaway install under %TEMP% and runs a stub TLS front on 127.0.0.1 with a self-signed
certificate made for the run and pinned in the settings. The front answers the account calls over
HTTPS and the tunnel's first line with OK or ERR auth, banned, busy, version or service, and after OK forwards
to a stub echo server. Through the launcher's own CommunityServer and CommunityTunnel classes
(src\bin\Release, so run build.cmd first) it creates an account with a Master name, kept beside the card, with
and without the machine id, checks the Master name rule before sending and the server's name_taken and
name_invalid refusals,
refuses a redirect, names a ban, recovers an account with a rotated code and key, checks a recovery key's shape as
the Recover Account dialog does and names the server's 400 and 429 answers to a recovery, carries bytes through every
tunnel service with half-closes, keeps warm connections past OK (none before the game's first
connection, title 3 and the others 1 after its OK, one taken by the game's next connection, a closed
one skipped for a new dial, each replaced once old, all closed when the tunnel stops), and checks
that an unpinned or a wrong certificate, one made out to another address (pinned or not), a plain HTTP
server, a closed port and a host that does not resolve are hard errors that never carry the code in
the clear. Without a fingerprint the certificate check is Windows' chain and name check alone; a fingerprint stands in
for Windows trust, never for the name. The server's ports default to 443, UDP 30111 and 30112 when only the address is
saved. After ERR auth, banned or version a later game connection is closed without dialling the
front; after ERR busy it dials again. The probe before Play keeps its OK as the first warm title
connection, which carries the game's first title connection; every refusal comes back as its Status
sentence after that one dial. The probe on the Online page binds nothing, closes its OK unused and turns an
address that does not resolve into a sentence. After the OK both probes ping the server's UDP ping port in the
game's frame, again each second it is unanswered and three times at most: echoed, they pass, also when the first ping
is lost; to a closed port or one that never answers, they come back as a sentence and one problem line. Stub UDP echo servers check the ping and distributor relay:
datagrams both ways, one upstream socket per game socket, a mapping dropped after silence, and a
mapping that keeps its upstream socket through a stopped server port and its ICMP port unreachable,
reports the silence in one line and carries again once the port answers. It then runs
overlay\App\FGOAC_Launcher.ps1 -CheckOnly against that install: without App\fgoacaddon.dll no line names the
add-on; with it (and FGOAudio.dll on) one line names it the last hook before ago.exe and the runtime ini is
the same. It reads back the [aime], [dns] and
[netenv] values it wrote for the game, and the [io4] operator menu keys: unbound unless operatorMenuKeys
is saved on, then as segatools.ini binds them, and the five [pcport] PC play switches, 1,1,0,0,0 unless saved,
then as saved, and [widescreen]: at its defaults the stock 1920x1080 touch canvas and the same ini as
the first local run, the anchors template written once and kept, and with hudAnchor on the native surface as
the touch canvas and no other line changed; [cursor] confine 0 unless saved on, and no [camera] block even
with a camera saved by an older launcher;
[effects] hideNpScreen and hideWhiteFlash as the Battle effects card writes them into segatools.ini;
with the block saved off, the runtime ini must match byte for
byte the one written before the block existed. DEVICE\runtime\bindings.ini, written through the launcher's Bindings
before the first dry run, is the same file after the Local and the Online one, and the runtime ini's attack key is
refused as a binding. The script plays Online exactly when -CommunityServer,
which Play Online passes, is set, whatever the saved block says, and holds no check that
the two agree; the 1.2.0 script (from the v1.2.0 tag) must fail on -CommunityServer under both Windows
PowerShell and pwsh. The script writes the runtime ini, the amdaemon json and the anchors template through a .tmp
file moved over the old one.

Online needs no Server folder: with it removed, the script and the real environment check pass
-CheckOnly -CommunityServer, nothing under Server\ appears and the print folder is named from the
card; LocalServerStop gives the stop script whenever it exists and logs one line for a missing one;
Apply-EN-Patch.ps1 installs into a root that holds only App\ and skips the manifest's Server\ entries
until Server\ exists; a saved block on the local server's ports moves to 40080, 48443 and 42345 once. The block keeps
only the title, billing and card reader ports on this PC; matching, ping and battle read as 30110, 30111 and 30112
whatever the file holds.

-GameRoot is an FGO Arcade install to copy FGO_LocalNetwork.ps1, segatools.ini and FGO_Runtime.dll
from; nothing in it is written. The environment check is replaced by a stub, since the throwaway
install has no game, except in the Online case without Server\, which runs the real one.
PowerShell 7 is needed to load the launcher assembly. The launch script runs under Windows
PowerShell 5.1, the host most players have.

Exit codes: 0 every check passed, 1 a check failed, 2 a prerequisite is missing.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$GameRoot,
    [switch]$KeepFiles
)
$ErrorActionPreference = 'Stop'

$repository = Split-Path -Parent $PSScriptRoot
$assembly = Join-Path $repository 'src\bin\Release\net6.0-windows\FGOLocalPlatform.dll'
$windowsPowerShell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
if ($PSVersionTable.PSVersion.Major -lt 7) { Write-Host 'Run this with pwsh 7: the launcher assembly targets .NET 6.'; exit 2 }
if (!(Test-Path -LiteralPath $assembly)) { Write-Host "Missing $assembly. Run build.cmd first."; exit 2 }
# Every message box, dialog and window of the launcher goes to the sink, never to the screen.
. (Join-Path $PSScriptRoot 'DialogSink.ps1'); Enable-DialogSink $assembly
foreach ($name in @('FGO_LocalNetwork.ps1', 'segatools.ini', 'FGO_Runtime.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $GameRoot "App\$name"))) { Write-Host "Missing $GameRoot\App\$name."; exit 2 }
}
$releasedScript = git -C $repository rev-parse --verify --quiet 'v1.2.0:overlay/App/FGO_Launcher.ps1'
if (!$releasedScript) { Write-Host 'Missing the v1.2.0 tag, which holds the 1.2.0 launch script.'; exit 2 }

$script:failed = 0
function Test-Result([string]$Name, [bool]$Passed, [string]$Detail) {
    Write-Host ("{0} {1}: {2}" -f $(if ($Passed) { 'PASS' } else { 'FAIL' }), $Name, $Detail)
    if (!$Passed) { $script:failed++ }
}

function Get-ErrorMessage([System.Management.Automation.ErrorRecord]$Record) {
    $exception = $Record.Exception
    while ($exception -is [System.Management.Automation.MethodInvocationException]) { $exception = $exception.InnerException }
    return $exception.Message
}

# This PC's machine id in a request body, printed as its length and shape only.
function Hide-MachineId([string]$Text) {
    return $Text -replace '"machine_id":"([^"]*)"', { '"machine_id":"<{0} {1} characters>"' -f $_.Groups[1].Value.Length, $(if ($_.Groups[1].Value -cmatch '^[0-9a-f]*$') { 'lower-case hex' } else { 'other' }) }
}

function Get-FreePort {
    $probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $probe.Start(); $port = $probe.LocalEndpoint.Port; $probe.Stop()
    return $port
}

function Get-FreeUdpPort {
    $probe = [Net.Sockets.UdpClient]::new([Net.IPEndPoint]::new([Net.IPAddress]::Loopback, 0))
    $port = $probe.Client.LocalEndPoint.Port; $probe.Dispose()
    return $port
}

# SIO_UDP_CONNRESET off: an ICMP port unreachable must not end a receive loop.
function Disable-UdpReset([Net.Sockets.UdpClient]$Client) {
    [void]$Client.Client.IOControl([int]-1744830452, [byte[]]::new(4), $null)
}

function New-TestCertificate([string]$Name) {
    $key = [Security.Cryptography.RSA]::Create(2048)
    $request = [Security.Cryptography.X509Certificates.CertificateRequest]::new("CN=$Name", $key, [Security.Cryptography.HashAlgorithmName]::SHA256, [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $names = [Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder]::new()
    $names.AddIpAddress([Net.IPAddress]::Loopback)
    $request.CertificateExtensions.Add($names.Build())
    $made = $request.CreateSelfSigned([DateTimeOffset]::Now.AddMinutes(-5), [DateTimeOffset]::Now.AddHours(2))
    # SChannel serves only a certificate whose key it can open, so the ephemeral key goes through a PFX.
    return [Security.Cryptography.X509Certificates.X509Certificate2]::new($made.Export('Pfx'))
}

function Wait-Until([scriptblock]$Condition) {
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    while (!(& $Condition) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 50 }
    return [bool](& $Condition)
}

# Collects the tunnel's report lines on the thread-pool threads that raise them.
Add-Type -TypeDefinition @'
public sealed class TunnelReportSink
{
    public readonly System.Collections.Concurrent.ConcurrentQueue<string> Lines = new System.Collections.Concurrent.ConcurrentQueue<string>();
    public void Report(string message, bool problem) { Lines.Enqueue((problem ? "problem " : "note ") + message); }
}
'@

# The throwaway install: the real launch scripts, placeholder game files.
$install = Join-Path $env:TEMP ('fgoac-community-check-' + [Guid]::NewGuid().ToString('N'))
$app = Join-Path $install 'App'
foreach ($folder in @('App\am', 'App\Tools\Locale_Remulator', 'AMFS', 'DEVICE')) {
    New-Item -ItemType Directory -Path (Join-Path $install $folder) -Force | Out-Null
}
Copy-Item -LiteralPath (Join-Path $repository 'overlay\App\FGOAC_Launcher.ps1') -Destination $app
Copy-Item -LiteralPath (Join-Path $repository 'overlay\App\FGOAC_StartupChecks.ps1') -Destination $app
$releasedLauncher = Join-Path $app 'FGO_Launcher-1.2.0.ps1'
git -C $repository cat-file blob $releasedScript > $releasedLauncher
Copy-Item -LiteralPath (Join-Path $GameRoot 'App\FGO_LocalNetwork.ps1') -Destination $app
Copy-Item -LiteralPath (Join-Path $GameRoot 'App\segatools.ini') -Destination $app
Copy-Item -LiteralPath (Join-Path $GameRoot 'App\FGO_Runtime.dll') -Destination $app
Set-Content -LiteralPath (Join-Path $app 'FGOAC_EnvironmentCheck.ps1') -Value "param([switch]`$AsJson)`r`n'[]'"
Set-Content -LiteralPath (Join-Path $app 'config.json') -Value '{}'
foreach ($file in @('App\inject.exe', 'App\fgohook.dll', 'App\ago.exe', 'App\am\amdaemon.exe',
        'App\Tools\Locale_Remulator\LRHookx64.dll', 'AMFS\ICF1', 'AMFS\ICF2')) {
    New-Item -ItemType File -Path (Join-Path $install $file) -Force | Out-Null
}
$settingsPath = Join-Path $app 'fgo-launcher.json'
$localSettings = [ordered]@{
    serverHost = 'auto'
    serverPorts = [ordered]@{ http = 80; billing = 8443; aime = 22345; database = 3307 }
    requiredPorts = @(80, 8443, 22345)
    autoStartLocalServer = $false
    displayMode = 'windowed'
    resolutionWidth = 1280
    resolutionHeight = 720
    inputMode = 'keyboard'
    gameVersion = '11.00'
}
[IO.File]::WriteAllText($settingsPath, ($localSettings | ConvertTo-Json -Depth 4), [Text.UTF8Encoding]::new($false))

$accessCode = '12345678901234567890'
$bannedCode = '98765432109876543210'
$busyCode = '22222222222222222222'
$versionCode = '44444444444444444444'
$serviceCode = '33333333333333333333'
$silentCode = '77777777777777777777'
$recoveryKey = 'ABCD-EFGH-IJKL-MNOP-QRST-UVWX-YZ23-4567'
$newCode = '55555555555555555555'
$newKey = 'NEWK-EYAF-TERR-ECOV-ERYR-OTAT-ES22-2222'
$certificate = New-TestCertificate 'fgoac-check-front'
$otherCertificate = New-TestCertificate 'fgoac-check-other'
$pin = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($certificate.RawData))
$otherPin = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($otherCertificate.RawData))
$seen = [Collections.ArrayList]::Synchronized([Collections.ArrayList]::new())
$echoed = [Collections.ArrayList]::Synchronized([Collections.ArrayList]::new())
$plainSeen = [Collections.ArrayList]::Synchronized([Collections.ArrayList]::new())

# The echo server stands in for the game server behind the front: it sends back what it gets,
# records it, and closes its sending half once the other side has closed its own.
$echoListener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$echoListener.Start()
$echo = [PowerShell]::Create()
[void]$echo.AddScript({
    param($Listener, $Echoed)
    while ($true) {
        try { $client = $Listener.AcceptTcpClient() } catch { return }
        try {
            $stream = $client.GetStream()
            $received = [IO.MemoryStream]::new()
            $buffer = [byte[]]::new(4096)
            while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                $received.Write($buffer, 0, $read)
                $stream.Write($buffer, 0, $read)
            }
            [void]$Echoed.Add([Convert]::ToHexString($received.ToArray()))
            $client.Client.Shutdown([Net.Sockets.SocketShutdown]::Send)
        }
        catch { [void]$Echoed.Add('echo error: ' + $_.Exception.Message) }
        finally { $client.Dispose() }
    }
}).AddArgument($echoListener).AddArgument($echoed)
$echoRun = $echo.BeginInvoke()

# The TLS front: after the handshake a tunnel line gets OK, ERR banned, ERR busy, ERR version, ERR service
# or ERR auth by its code, or closes without a reply for one more; anything else is one of the two
# account calls. A new account comes back on every account call but the second, which gets 429 as a
# rate-limited server answers, the third, which is redirected to /community/moved, and the fifth,
# which gets 403 banned as the recovery key BANN-EDKE-... does. The Master names Taken Name and Rejected get 409
# name_taken (as error) and 400 name_invalid (as reason) and are not counted.
# Recovery rotates as the server does: the key brings back NewCode and NewKey, then only NewKey works. As the server,
# it answers 400 for a key that is not 32 of A-Z and 2-7 once spaces and dashes are dropped; MANY-REQU-... gets 429.
# Each connection runs on a runspace of its own, and after OK the echo server is dialled on the
# client's first bytes, as the server's front does: a connection that waits past OK holds up no other.
# Connections are numbered as accepted; one waiting past OK is in $frontIdle by its number until its
# first bytes, and "<number> <first line>" goes to $frontCarried once it carries bytes or to
# $frontUnused when it closes before any.
$frontListener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$frontListener.Start()
$frontPort = $frontListener.LocalEndpoint.Port
$frontState = [hashtable]::Synchronized(@{ AccountCalls = 0; ValidKey = $recoveryKey; Accepted = 0 })
$frontIdle = [hashtable]::Synchronized(@{})
$frontCarried = [Collections.ArrayList]::Synchronized([Collections.ArrayList]::new())
$frontUnused = [Collections.ArrayList]::Synchronized([Collections.ArrayList]::new())
$frontConnection = {
    param($Client, $Number, $Certificate, $EchoPort, $Code, $BannedCode, $BusyCode, $VersionCode, $ServiceCode, $SilentCode, $Key, $NewCode, $NewKey, $Seen, $State, $Idle, $Carried, $Unused)
    function Read-RawLine($Stream) {
        $bytes = [Collections.Generic.List[byte]]::new()
        while (($next = $Stream.ReadByte()) -ge 0) {
            if ($next -eq 10) { return [Text.Encoding]::ASCII.GetString($bytes.ToArray()) }
            $bytes.Add([byte]$next)
        }
        return $null
    }
    function Send-Text($Stream, [string]$Text) {
        $bytes = [Text.Encoding]::ASCII.GetBytes($Text)
        $Stream.Write($bytes, 0, $bytes.Length)
        $Stream.Flush()
    }
    try {
        $tls = [Net.Security.SslStream]::new($Client.GetStream(), $false)
        # Under TLS 1.3 a client refusing the certificate is seen at the first read, not in the handshake.
        try {
            $tls.AuthenticateAsServer($Certificate, $false, [Security.Authentication.SslProtocols]::None, $false)
            $first = Read-RawLine $tls
        }
        catch { $first = $null }
        if ($null -eq $first) { [void]$Seen.Add('closed by the client before any byte'); return }
        if ($first -like 'FGOAC-TUNNEL *') {
            [void]$Seen.Add($first)
            $offered = ($first -split ' ')[3]
            if ($offered -eq $SilentCode) { return }
            $reply = if ($offered -eq $BannedCode) { 'ERR banned' } elseif ($offered -eq $BusyCode) { 'ERR busy' } elseif ($offered -eq $VersionCode) { 'ERR version' } elseif ($offered -eq $ServiceCode) { 'ERR service' } elseif ($offered -ne $Code) { 'ERR auth' } else { 'OK' }
            Send-Text $tls "$reply`n"
            if ($reply -ne 'OK') { return }
            $Idle[$Number] = [pscustomobject]@{ Line = $first; Client = $Client }
            $buffer = [byte[]]::new(65536)
            try { $read = $tls.Read($buffer, 0, $buffer.Length) } catch { $read = 0 }
            $Idle.Remove($Number)
            if ($read -eq 0) { [void]$Unused.Add("$Number $first"); return }
            [void]$Carried.Add("$Number $first")
            $server = [Net.Sockets.TcpClient]::new('127.0.0.1', $EchoPort)
            try {
                $serverStream = $server.GetStream()
                $serverStream.Write($buffer, 0, $read)
                $back = $serverStream.CopyToAsync($tls)
                $tls.CopyTo($serverStream)
                $server.Client.Shutdown([Net.Sockets.SocketShutdown]::Send)
                $back.Wait()
                $tls.ShutdownAsync().Wait()
            }
            finally { $server.Dispose() }
            return
        }
        $length = 0
        while ($line = (Read-RawLine $tls).TrimEnd("`r")) {
            if ($line -match '^Content-Length:\s*(\d+)') { $length = [int]$Matches[1] }
        }
        $body = [Text.Encoding]::UTF8.GetString([byte[]]@(for ($i = 0; $i -lt $length; $i++) { $tls.ReadByte() }))
        $request = $first.TrimEnd("`r")
        [void]$Seen.Add("$request $body")
        $path = ($request -split ' ')[1]
        $headers = ''
        $name = if ($path -eq '/community/account') { ($body | ConvertFrom-Json).master_name } else { $null }
        $recovery = if ($path -eq '/community/account/recover') { [string]($body | ConvertFrom-Json).recovery_key } else { $null }
        if ($path -eq '/community/account' -and $name -cnotin 'Taken Name', 'Rejected') { $State.AccountCalls = $State.AccountCalls + 1 }
        $accountCalls = $State.AccountCalls
        if ($name -ceq 'Taken Name') {
            $status = '409 Conflict'; $answer = '{"error": "name_taken"}'
        }
        elseif ($name -ceq 'Rejected') {
            $status = '400 Bad Request'; $answer = '{"reason": "name_invalid"}'
        }
        elseif ($path -eq '/community/account' -and $accountCalls -eq 2) {
            $status = '429 Too Many Requests'; $answer = '{"error": "rate limited"}'
        }
        elseif ($path -eq '/community/account' -and $accountCalls -eq 3) {
            $status = '307 Temporary Redirect'; $answer = '{}'
            $headers = "Location: https://127.0.0.1:$($Client.Client.LocalEndPoint.Port)/community/moved`r`n"
        }
        elseif ($null -ne $recovery -and ($recovery -replace '[\s-]', '').ToUpperInvariant() -cnotmatch '^[A-Z2-7]{32}$') {
            $status = '400 Bad Request'; $answer = '{"error": "body must be {\"recovery_key\": \"<key>\"}"}'
        }
        elseif ($recovery -eq 'MANY-REQU-ESTS-2222-2222-2222-2222-2222') {
            $status = '429 Too Many Requests'; $answer = '{"error": "too many requests"}'
        }
        elseif (($path -eq '/community/account' -and $accountCalls -eq 5) -or $recovery -eq 'BANN-EDKE-Y222-2222-2222-2222-2222-2222') {
            $status = '403 Forbidden'; $answer = '{"error": "banned"}'
        }
        elseif ($path -in '/community/account', '/community/moved') {
            $status = '200 OK'; $answer = '{"access_code": "' + $Code + '", "recovery_key": "' + $Key + '"}'
        }
        elseif ($null -ne $recovery -and $recovery -eq $State.ValidKey) {
            $status = '200 OK'; $answer = '{"access_code": "' + $NewCode + '", "recovery_key": "' + $NewKey + '"}'
            $State.ValidKey = $NewKey
        }
        else {
            $status = '404 Not Found'; $answer = '{"error": "unknown recovery key"}'
        }
        Send-Text $tls "HTTP/1.1 $status`r`n$($headers)Content-Type: application/json`r`nContent-Length: $($answer.Length)`r`nConnection: close`r`n`r`n$answer"
    }
    catch { [void]$Seen.Add('front error: ' + $_.Exception.Message) }
    finally { $Client.Dispose() }
}.ToString()
$frontPool = [RunspaceFactory]::CreateRunspacePool(1, 32)
$frontPool.Open()
$front = [PowerShell]::Create()
[void]$front.AddScript({
    param($Listener, $Pool, $Connection, $Arguments, $State)
    while ($true) {
        try { $client = $Listener.AcceptTcpClient() } catch { return }
        $State.Accepted = $State.Accepted + 1
        $one = [PowerShell]::Create()
        $one.RunspacePool = $Pool
        [void]$one.AddScript($Connection).AddArgument($client).AddArgument($State.Accepted)
        foreach ($argument in $Arguments) { [void]$one.AddArgument($argument) }
        [void]$one.BeginInvoke()
    }
}).AddArgument($frontListener).AddArgument($frontPool).AddArgument($frontConnection).AddArgument(@($certificate, $echoListener.LocalEndpoint.Port, $accessCode, $bannedCode, $busyCode, $versionCode, $serviceCode, $silentCode, $recoveryKey, $newCode, $newKey, $seen, $frontState, $frontIdle, $frontCarried, $frontUnused)).AddArgument($frontState)
$frontRun = $front.BeginInvoke()

# A plain HTTP server on the secure port: it records what arrives and answers in the clear with an account.
$plainListener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$plainListener.Start()
$plainPort = $plainListener.LocalEndpoint.Port
$plain = [PowerShell]::Create()
[void]$plain.AddScript({
    param($Listener, $Code, $Received)
    while ($true) {
        try { $client = $Listener.AcceptTcpClient() } catch { return }
        try {
            $stream = $client.GetStream()
            $stream.ReadTimeout = 5000
            $buffer = [byte[]]::new(65536)
            $read = $stream.Read($buffer, 0, $buffer.Length)
            [void]$Received.Add([Text.Encoding]::Latin1.GetString($buffer, 0, $read))
            $answer = '{"access_code": "' + $Code + '", "recovery_key": "IN-THE-CLEAR"}'
            $bytes = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 200 OK`r`nContent-Type: application/json`r`nContent-Length: $($answer.Length)`r`nConnection: close`r`n`r`n$answer")
            $stream.Write($bytes, 0, $bytes.Length)
        }
        catch { }
        finally { $client.Dispose() }
    }
}).AddArgument($plainListener).AddArgument($accessCode).AddArgument($plainSeen)
$plainRun = $plain.BeginInvoke()

# UDP echo servers stand in for the server's public ping and distributor ports, and for the ping port the probes
# ping; each records the source port and the datagram (Latin1, one character per byte), and sends the datagram back
# followed by Tail, except the first Drop datagrams, which it leaves unanswered.
function New-UdpEcho([string]$Name, [int]$Port = 0, [byte[]]$Tail = @(), [int]$Drop = 0) {
    $socket = [Net.Sockets.UdpClient]::new([Net.IPEndPoint]::new([Net.IPAddress]::Loopback, $Port))
    Disable-UdpReset $socket
    $record = [Collections.ArrayList]::Synchronized([Collections.ArrayList]::new())
    $runspace = [PowerShell]::Create()
    [void]$runspace.AddScript({
        param($Socket, $Record, $Tail, $Drop)
        $from = [Net.IPEndPoint]::new([Net.IPAddress]::Any, 0)
        while ($true) {
            try { $data = $Socket.Receive([ref]$from) } catch { return }
            [void]$Record.Add("$($from.Port) $([Text.Encoding]::Latin1.GetString($data))")
            if ($Record.Count -le $Drop) { continue }
            $reply = [byte[]]($data + $Tail)
            [void]$Socket.Send($reply, $reply.Length, $from)
        }
    }).AddArgument($socket).AddArgument($record).AddArgument($Tail).AddArgument($Drop)
    [pscustomobject]@{ Name = $name; Socket = $socket; Port = $socket.Client.LocalEndPoint.Port; Record = $record; Runspace = $runspace; Run = $runspace.BeginInvoke() }
}
# The probe echo answers a ping as the server does: the 8 bytes, then a length and a flag byte of 0 and two zero bytes.
$udpEchoes = @((New-UdpEcho 'ping'), (New-UdpEcho 'dist'), (New-UdpEcho 'probe' -Tail ([byte[]]::new(4))))

# The datagrams the probe echo, or Echo, got, as hex.
function Get-Pings($Echo = $udpEchoes[2]) { @($Echo.Record | ForEach-Object { [Convert]::ToHexString([Text.Encoding]::Latin1.GetBytes(($_ -split ' ', 2)[1])) }) }

# The probe's sentence for a ping port that does not answer.
function Get-Unanswered([int]$Port) { "127.0.0.1 did not answer a ping on UDP port $Port, which the game needs for matching. Check the ping port on the Online page; if it is right, a firewall or router may be blocking UDP." }

function Stop-UdpEcho($Echo) {
    $Echo.Socket.Dispose()
    [void]$Echo.Runspace.EndInvoke($Echo.Run)
    $Echo.Runspace.Dispose()
}

function Invoke-DryRun([string[]]$Arguments) {
    $output = & $windowsPowerShell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $app 'FGOAC_Launcher.ps1') -CheckOnly @Arguments 2>&1
    [pscustomobject]@{ ExitCode = $LASTEXITCODE; Code = [string]($output | Where-Object { "$_" -match '^\[FGO-LAUNCHER:' } | Select-Object -Last 1); Output = @($output | ForEach-Object { "$_" }) }
}

function Get-RuntimeValue([string]$Section, [string]$Key, [string]$Path = (Join-Path $install 'DEVICE\runtime\segatools.runtime.ini')) {
    $text = [IO.File]::ReadAllText($Path)
    $body = [Regex]::Match($text, '(?ms)^\[' + [Regex]::Escape($Section) + '\]\s*$(.*?)(?=^\[|\z)').Groups[1].Value
    return [Regex]::Match($body, '(?m)^\s*' + [Regex]::Escape($Key) + '\s*=(.*?)\s*$').Groups[1].Value
}

# Ports: the four TCP ports on this PC. Udp: server ping, server dist, local ping, local dist (free ports by default).
function New-Settings([bool]$Enabled, [string]$HostName, [int]$TlsPort, [string]$Pin, [int[]]$Ports, [int[]]$Udp) {
    if (!$Udp) { $Udp = @((Get-FreeUdpPort), (Get-FreeUdpPort), (Get-FreeUdpPort), (Get-FreeUdpPort)) }
    return [FGOLocalPlatform.CommunityServer+Settings]::new($Enabled, $HostName, $TlsPort, $Pin, $Udp[0], $Udp[1], $Ports[0], $Ports[1], $Ports[2], $Ports[3], $Udp[2], $Udp[3])
}

# One game UDP socket: sends each datagram to the relay and waits for its echo.
function Send-Datagrams([Net.Sockets.UdpClient]$Game, [int]$Port, [string[]]$Datagrams) {
    $answers = foreach ($text in $Datagrams) {
        $bytes = [Text.Encoding]::ASCII.GetBytes($text)
        [void]$Game.Send($bytes, $bytes.Length, [Net.IPEndPoint]::new([Net.IPAddress]::Loopback, $Port))
        $from = [Net.IPEndPoint]::new([Net.IPAddress]::Any, 0)
        try { [Text.Encoding]::ASCII.GetString($Game.Receive([ref]$from)) } catch { 'no answer' }
    }
    return ,@($answers)
}

function New-GameSocket {
    $game = [Net.Sockets.UdpClient]::new([Net.IPEndPoint]::new([Net.IPAddress]::Loopback, 0))
    $game.Client.ReceiveTimeout = 5000
    return $game
}

# One game connection: sends the payload, closes its sending half and reads until the other side closes.
# A connection the tunnel refuses can be reset before the send finishes: that too is closed with nothing.
function Send-ThroughTunnel([int]$Port, [byte[]]$Payload) {
    $client = [Net.Sockets.TcpClient]::new('127.0.0.1', $Port)
    try {
        $client.ReceiveTimeout = 10000
        $stream = $client.GetStream()
        try {
            $stream.Write($Payload, 0, $Payload.Length)
            $client.Client.Shutdown([Net.Sockets.SocketShutdown]::Send)
        }
        catch [Net.Sockets.SocketException], [IO.IOException] { return '' }
        $received = [IO.MemoryStream]::new()
        $buffer = [byte[]]::new(4096)
        try { while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) { $received.Write($buffer, 0, $read) } }
        catch [IO.IOException] { if ($_.Exception.InnerException.SocketErrorCode -eq 'TimedOut') { return 'timed out' } }
        return [Convert]::ToHexString($received.ToArray())
    }
    finally { $client.Dispose() }
}

function Start-Tunnel([int]$TlsPort, [string]$Pin, [string]$Code, [int[]]$ServerUdp = @((Get-FreeUdpPort), (Get-FreeUdpPort)), [double]$Silence = 120, [double]$WarmFor = 240, [string]$HostName = '127.0.0.1') {
    $script:sink = [TunnelReportSink]::new()
    $script:tunnelPorts = @(Get-FreePort; Get-FreePort; Get-FreePort; Get-FreePort)
    $script:relayPorts = @(Get-FreeUdpPort; Get-FreeUdpPort)
    $report = [Delegate]::CreateDelegate([Action[string, bool]], $script:sink, 'Report')
    $settings = New-Settings $true $HostName $TlsPort $Pin $script:tunnelPorts @($ServerUdp[0], $ServerUdp[1], $script:relayPorts[0], $script:relayPorts[1])
    return [FGOLocalPlatform.CommunityTunnel]::Start($settings, $Code, $report, [TimeSpan]::FromSeconds($Silence), [TimeSpan]::FromSeconds($WarmFor))
}

# The connections the stub front holds past OK, counted per service.
function Get-Warm {
    [Threading.Monitor]::Enter($frontIdle.SyncRoot)
    try { $held = @($frontIdle.Values) } finally { [Threading.Monitor]::Exit($frontIdle.SyncRoot) }
    return (@($held | ForEach-Object { ($_.Line -split ' ')[2] } | Group-Object | Sort-Object Name | ForEach-Object { "$($_.Name) $($_.Count)" }) -join ', ')
}
$fullPool = 'aime 1, billing 1, ms 1, title 3'

function Get-WarmClients {
    [Threading.Monitor]::Enter($frontIdle.SyncRoot)
    try { return @($frontIdle.Keys | ForEach-Object { [pscustomobject]@{ Number = $_; Client = $frontIdle[$_].Client } }) }
    finally { [Threading.Monitor]::Exit($frontIdle.SyncRoot) }
}

# The number of the stub front's connection that carried the last game connection's bytes.
function Get-LastCarrier { return [int](([string]$frontCarried[$frontCarried.Count - 1] -split ' ')[0]) }

$script:reportLines = [Collections.Generic.List[string]]::new()
function Stop-Tunnel($Tunnel) {
    $Tunnel.Dispose()
    $script:reportLines.AddRange([string[]]$script:sink.Lines.ToArray())
}

# A refused tunnel: the game's connection closes with nothing carried, and the report says why.
function Test-Refused([string]$Name, [int]$TlsPort, [string]$Pin, [string]$Code, [string]$Expected, [string]$HostName = '127.0.0.1') {
    $tunnel = Start-Tunnel $TlsPort $Pin $Code -HostName $HostName
    try {
        $answer = Send-ThroughTunnel $script:tunnelPorts[0] ([Text.Encoding]::ASCII.GetBytes('GET / HTTP/1.1'))
        $lines = @($script:sink.Lines.ToArray())
        Test-Result $Name ($answer -eq '' -and ($lines -like "problem title: *$Expected*").Count -eq 1) "game connection closed with $(if ($answer -eq '') { 'nothing' } else { $answer }); $($lines -join ' | ')"
    }
    finally { Stop-Tunnel $tunnel }
}

# The probe on the tunnel Play started: the refusal comes back as the Status sentence, the Game log gets the same line,
# and nothing is dialled after the one title dial.
function Test-Probe([string]$Name, [int]$TlsPort, [string]$Pin, [string]$Code, [string]$Expected) {
    $tunnel = Start-Tunnel $TlsPort $Pin $Code
    try {
        $accepted = $frontState.Accepted
        $refusal = $tunnel.ProbeAsync().GetAwaiter().GetResult()
        Start-Sleep -Milliseconds 300
        $dialled = $frontState.Accepted - $accepted
        $lines = @($script:sink.Lines.ToArray())
        $dials = if ($TlsPort -eq $frontPort) { 1 } else { 0 }
        Test-Result $Name ($refusal -ceq $Expected -and ($lines -ceq "problem title: $Expected").Count -eq 1 -and $dialled -eq $dials) "the probe returned '$refusal' after $dialled dial(s) to the front; $($lines -join ' | ')"
    }
    finally { Stop-Tunnel $tunnel }
}

# A second game connection after a refusal, on another service: only ERR busy dials the server again.
function Test-SecondConnection([string]$Name, [string]$Code, [string]$Expected, [bool]$Dials) {
    $tunnel = Start-Tunnel $frontPort $pin $Code
    try {
        [void](Send-ThroughTunnel $script:tunnelPorts[0] ([Text.Encoding]::ASCII.GetBytes('GET / HTTP/1.1')))
        $before = $seen.Count
        $answer = Send-ThroughTunnel $script:tunnelPorts[1] ([Text.Encoding]::ASCII.GetBytes('POST / HTTP/1.1'))
        $dialled = @($seen | Select-Object -Skip $before)
        $lines = @($script:sink.Lines.ToArray())
        $latched = @($lines -ceq 'note title: Further connections are refused on this PC until the game is restarted.')
        $passed = if ($Dials) {
            ($dialled -join ',') -eq "FGOAC-TUNNEL 1 billing $Code" -and ($lines -like "problem billing: *$Expected*").Count -eq 1 -and $latched.Count -eq 0
        }
        else {
            $dialled.Count -eq 0 -and $lines.Count -eq 2 -and ($lines -like "problem title: *$Expected*").Count -eq 1 -and $latched.Count -eq 1
        }
        Test-Result $Name ($answer -eq '' -and $passed) "the second connection got $(if ($answer -eq '') { 'nothing' } else { $answer }); the front saw $($dialled.Count) new connection(s) $($dialled -join ', '); $($lines -join ' | ')"
    }
    finally { Stop-Tunnel $tunnel }
}

try {
    [void][Reflection.Assembly]::LoadFrom($assembly)
    $community = [FGOLocalPlatform.CommunityServer]
    $testHost = 'community.example'
    $frontSettings = New-Settings $true '127.0.0.1' $frontPort $pin @(1, 2, 3, 4) @($udpEchoes[2].Port, $udpEchoes[1].Port, (Get-FreeUdpPort), (Get-FreeUdpPort))
    Write-Host "Stub TLS front on 127.0.0.1:$frontPort (certificate $pin), plain HTTP stub on $plainPort; throwaway install $install"

    Test-Result 'address' ($community::NormalizeHost(' Community.Example. ') -eq $testHost) 'Community.Example. is stored as community.example'
    foreach ($refused in @('127.0.0.1', 'localhost', 'auto', '192.168.100.1', 'fe80::1', 'bad\..\name')) {
        try { [void]$community::NormalizeHost($refused); Test-Result 'address' $false "$refused was accepted" }
        catch { $message = Get-ErrorMessage $_; Test-Result 'address' ($message -like 'Enter the community server''s address: a host name such as play.example.org, or an IPv4 address.*') "$refused refused: $message" }
    }
    $colons = ($pin.ToLowerInvariant() -split '(..)' | Where-Object { $_ }) -join ':'
    Test-Result 'fingerprint' ($community::NormalizePin(" $colons ") -eq $pin -and $community::NormalizePin('') -eq '') 'colons, spaces and case are dropped; empty stays empty'
    try { [void]$community::NormalizePin('ABCD'); Test-Result 'fingerprint' $false 'a 4-digit fingerprint was accepted' }
    catch { $message = Get-ErrorMessage $_; Test-Result 'fingerprint' ($message -like 'The certificate fingerprint is the SHA-256 fingerprint the server''s operator gives: 64 hexadecimal digits*') "a 4-digit fingerprint is refused: $message" }

    # Without a fingerprint the certificate passes on Windows' own chain and name checks alone, as a domain's public
    # certificate does; a saved fingerprint stands in for Windows trust, never for the name.
    $chainErrors = [Net.Security.SslPolicyErrors]::RemoteCertificateChainErrors
    $nameMismatch = [Net.Security.SslPolicyErrors]::RemoteCertificateNameMismatch
    $unpinned = New-Settings $true 'play.example.org' 443 '' @(1, 2, 3, 4)
    $pinned = New-Settings $true 'play.example.org' 443 $pin @(1, 2, 3, 4)
    $trusted = $community::CertificateProblem($unpinned, $certificate, [Net.Security.SslPolicyErrors]::None)
    $untrusted = $community::CertificateProblem($unpinned, $certificate, $chainErrors)
    $renamedTrusted = $community::CertificateProblem($unpinned, $certificate, $nameMismatch)
    Test-Result 'certificate check, no fingerprint' ($null -eq $trusted -and $untrusted -ceq 'it is not trusted by Windows' -and $renamedTrusted -ceq 'it is made out to another address') "trusted: $(if ($null -eq $trusted) { 'passes' } else { $trusted }); untrusted: $untrusted; trusted but made out to another name: $renamedTrusted"
    $selfSigned = $community::CertificateProblem($pinned, $certificate, $chainErrors)
    $renamedPinned = $community::CertificateProblem($pinned, $certificate, $chainErrors -bor $nameMismatch)
    $otherSigned = $community::CertificateProblem($pinned, $otherCertificate, $chainErrors)
    Test-Result 'certificate check, fingerprint' ($null -eq $selfSigned -and $renamedPinned -ceq 'it is made out to another address' -and $otherSigned -ceq 'it does not match the saved fingerprint') "pinned self-signed: $(if ($null -eq $selfSigned) { 'passes' } else { $selfSigned }); pinned but made out to another name: $renamedPinned; another certificate: $otherSigned"

    # The server's public ports: 443, UDP 30111 and 30112 before any block is saved and for a block that holds only the address.
    $settingsBytes = [IO.File]::ReadAllBytes($settingsPath)
    $fresh = $community::Load($app)
    $addressOnly = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    $addressOnly | Add-Member -NotePropertyName communityServer -NotePropertyValue ([pscustomobject]@{ enabled = $true; host = 'play.example.org' })
    [IO.File]::WriteAllText($settingsPath, ($addressOnly | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    $named = $community::Load($app)
    [IO.File]::WriteAllBytes($settingsPath, $settingsBytes)
    $publicPorts = { param($Settings) "$($Settings.TlsPort),$($Settings.UdpPingPort),$($Settings.UdpDistPort)" }
    Test-Result 'public port defaults' ((& $publicPorts $fresh) -eq '443,30111,30112' -and $fresh.Host -eq '' -and (& $publicPorts $named) -eq '443,30111,30112' -and $named.Host -eq 'play.example.org' -and $named.CertificatePin -eq '') "no block: $(& $publicPorts $fresh); only the address: $(& $publicPorts $named), fingerprint '$($named.CertificatePin)'"

    # The account calls, over HTTPS to the pinned front.
    $account = $community::CreateAccountAsync($frontSettings, 'Ritsuka', $true).GetAwaiter().GetResult()
    Test-Result 'new account' ($account.AccessCode -eq $accessCode -and $account.RecoveryKey -eq $recoveryKey) "access code $($account.AccessCode), recovery key $($account.RecoveryKey)"
    $community::WriteCard($app, $testHost, $account.AccessCode)
    $cardPath = $community::CardPath($app, $testHost)
    $cardBytes = [IO.File]::ReadAllBytes($cardPath)
    Test-Result 'card file' ($cardBytes.Length -eq 22 -and $cardBytes[20] -eq 13 -and $cardBytes[21] -eq 10) "$cardPath, $($cardBytes.Length) bytes, 20 digits and CRLF"
    Test-Result 'card read' ($community::ReadCard($app, $testHost) -eq $accessCode) 'ReadCard returns the stored code'
    [void]$community::TakeAccountAsync($app, $testHost, [Threading.Tasks.Task]::FromResult($account)).GetAwaiter().GetResult()
    $namePath = $community::MasterNamePath($app, $testHost)
    Test-Result 'master name kept' ($account.MasterName -ceq 'Ritsuka' -and $community::ReadMasterName($app, $testHost) -ceq 'Ritsuka' -and [IO.File]::ReadAllText($namePath) -ceq "Ritsuka`r`n" -and $community::ReadCard($app, $testHost) -eq $accessCode) "the reply names no Master, so the name sent is kept: $namePath holds '$($community::ReadMasterName($app, $testHost))'"

    try { [void]$community::CreateAccountAsync($frontSettings, 'Ritsuka', $true).GetAwaiter().GetResult(); Test-Result 'rate limit' $false 'a second account was handed out' }
    catch { $message = Get-ErrorMessage $_; Test-Result 'rate limit' ($message -ceq 'The community server 127.0.0.1 has had too many account requests from your address. Wait up to a day, then try again.') $message }
    try { $taken = $community::CreateAccountAsync($frontSettings, 'Ritsuka', $false).GetAwaiter().GetResult(); Test-Result 'redirect' $false "the redirect was followed to an account: $($taken.AccessCode)" }
    catch { $message = Get-ErrorMessage $_; Test-Result 'redirect' ($message -like '*turned the request down (HTTP 307)*' -and @($seen -like 'POST /community/moved *').Count -eq 0) "$message; the front never saw /community/moved" }

    # The machine id box of the New Account dialog: on for the first two calls, off from the third.
    $taken = $community::CreateAccountAsync($frontSettings, 'Ritsuka', $false).GetAwaiter().GetResult()
    $bodies = @($seen -like 'POST /community/account HTTP/1.1 *' -replace '^POST /community/account HTTP/1\.1 ', '')
    $on = @($bodies[0], $bodies[1] | ConvertFrom-Json -AsHashtable)
    Test-Result 'machine id on' ($on[0].Count -eq 2 -and $on[0].master_name -ceq 'Ritsuka' -and $on[0].machine_id -cmatch '^[0-9a-f]{64}$' -and $on[1].machine_id -ceq $on[0].machine_id) "the body is $(Hide-MachineId $bodies[0]), the same on the next call"
    Test-Result 'machine id off' ($taken.AccessCode -eq $accessCode -and $bodies.Count -eq 4 -and $bodies[2] -ceq '{"master_name":"Ritsuka"}' -and $bodies[3] -ceq '{"master_name":"Ritsuka"}') "the body is $(Hide-MachineId $bodies[3]), no machine_id key"
    try { [void]$community::CreateAccountAsync($frontSettings, 'Ritsuka', $false).GetAwaiter().GetResult(); Test-Result 'banned, new account' $false 'a banned address was handed an account' }
    catch { $message = Get-ErrorMessage $_; Test-Result 'banned, new account' ($message -like '*has banned this PC or address from creating accounts. Ask the server''s operator.') $message }

    # The Master name, chosen once: the rule New Account checks before sending, and the server's two name refusals.
    $wrongLetters = 'A Master name uses only the letters A to Z, digits, spaces, _ and -.'
    $nameCases = [ordered]@{
        'A' = $null; 'Saber Alter' = $null; 'x_y-1' = $null; 'Sixteen chars 16' = $null
        '' = 'Enter a Master name.'; 'Seventeen chars 1' = 'A Master name has at most 16 characters.'
        ' Lead' = 'A Master name cannot start or end with a space.'; 'Trail ' = 'A Master name cannot start or end with a space.'
        'Two  spaces' = 'A Master name cannot have two spaces in a row.'; "$([char]0xC9)mile" = $wrongLetters; 'a.b' = $wrongLetters
        "tab`tname" = $wrongLetters; "line`n" = $wrongLetters
    }
    $wrongNames = @(foreach ($case in $nameCases.GetEnumerator()) { $got = $community::MasterNameProblem($case.Key); if ($got -cne $case.Value) { "'$($case.Key)' gave '$got'" } })
    Test-Result 'master name rule' ($wrongNames.Count -eq 0) "$($nameCases.Count) names checked; $($wrongNames -join '; ')"
    $requests = $seen.Count
    try { [void]$community::CreateAccountAsync($frontSettings, 'Two  spaces', $false).GetAwaiter().GetResult(); Test-Result 'master name, refused here' $false 'an account came back' }
    catch { $message = Get-ErrorMessage $_; Test-Result 'master name, refused here' ($message -ceq 'A Master name cannot have two spaces in a row.' -and $seen.Count -eq $requests) "$message; $($seen.Count - $requests) request(s) sent" }
    foreach ($case in @(@('Taken Name', 'master name, taken', 'That name is taken on this server. Try another.'), @('Rejected', 'master name, refused there', 'This server does not accept that name. Try another.'))) {
        try { [void]$community::CreateAccountAsync($frontSettings, $case[0], $false).GetAwaiter().GetResult(); Test-Result $case[1] $false 'an account came back' }
        catch {
            $refusal = $_.Exception
            while ($refusal.InnerException) { $refusal = $refusal.InnerException }
            Test-Result $case[1] ($refusal -is [FGOLocalPlatform.CommunityServer+MasterNameRefusedException] -and $refusal.Message -ceq $case[2]) "$($refusal.GetType().Name): $($refusal.Message)"
        }
    }

    # The Recover Account flow as the Server tab runs it: TakeAccountAsync stores the rotated code over the card that held the first one.
    $taken = $community::TakeAccountAsync($app, $testHost, $community::RecoverAccountAsync($frontSettings, $recoveryKey)).GetAwaiter().GetResult()
    $recovered = $taken.Item1
    $stored = $community::ReadCard($app, $testHost)
    Test-Result 'recover' ($recovered.AccessCode -eq $newCode -and $recovered.RecoveryKey -eq $newKey -and $null -eq $taken.Item2 -and $stored -eq $newCode) "the key brings back new code $($recovered.AccessCode) and new key $($recovered.RecoveryKey); after TakeAccountAsync the card file holds $stored"
    Test-Result 'recover, no master name' ($null -eq $recovered.MasterName -and !(Test-Path -LiteralPath $namePath)) "a reply that names no Master leaves no name beside the card: $(Test-Path -LiteralPath $namePath)"
    $notice = [FGOLocalPlatform.RecoveryKeyDialog]::Notice($true)
    Test-Result 'recovered key dialog' ($notice -like '*the previous key no longer works*' -and [FGOLocalPlatform.RecoveryKeyDialog]::Notice($false) -notlike '*previous key*') $notice
    try { [void]$community::RecoverAccountAsync($frontSettings, $recoveryKey).GetAwaiter().GetResult(); Test-Result 'old key' $false 'the previous key recovered the account again' }
    catch { $message = Get-ErrorMessage $_; Test-Result 'old key' ($message -like '*does not know that recovery key*') $message }
    try { [void]$community::RecoverAccountAsync($frontSettings, 'ZZZZ-ZZZZ-ZZZZ-ZZZZ-ZZZZ-ZZZZ-ZZZZ-ZZZZ').GetAwaiter().GetResult(); Test-Result 'unknown key' $false 'an unknown key recovered an account' }
    catch { $message = Get-ErrorMessage $_; Test-Result 'unknown key' ($message -like '*does not know that recovery key*') $message }
    # A mistyped key: the Recover Account dialog refuses it by the server's rule, and the server's 400 reads the same.
    $notKey = 'That is not a recovery key. Check it against the one you saved: eight groups of four letters and digits.'
    try { [void]$community::RecoverAccountAsync($frontSettings, 'WRONG-KEY').GetAwaiter().GetResult(); Test-Result 'malformed key' $false 'a malformed key recovered an account' }
    catch { $message = Get-ErrorMessage $_; Test-Result 'malformed key' ($message -ceq $notKey) "the server answered 400: $message" }
    try { [void]$community::RecoverAccountAsync($frontSettings, 'MANY-REQU-ESTS-2222-2222-2222-2222-2222').GetAwaiter().GetResult(); Test-Result 'rate limit, recover' $false 'a rate-limited address recovered an account' }
    catch { $message = Get-ErrorMessage $_; Test-Result 'rate limit, recover' ($message -ceq 'The community server 127.0.0.1 has had too many recovery requests from your address. Wait ten minutes, then try again.') $message }
    $keyCases = [ordered]@{
        $recoveryKey = $null; ' abcd efgh ijkl mnop qrst uvwx yz23 4567 ' = $null; 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567' = $null
        '' = $notKey; 'WRONG-KEY' = $notKey; 'ABCD-EFGH-IJKL-MNOP-QRST-UVWX-YZ23-45678' = $notKey; 'ABCD-EFGH-IJKL-MNOP-QRST-UVWX-YZ23-4568' = $notKey
    }
    $wrongKeys = @(foreach ($case in $keyCases.GetEnumerator()) { $got = $community::RecoveryKeyProblem($case.Key); if ($got -cne $case.Value) { "'$($case.Key)' gave '$got'" } })
    Test-Result 'recovery key rule' ($wrongKeys.Count -eq 0) "$($keyCases.Count) keys checked; $($wrongKeys -join '; ')"
    try { [void]$community::RecoverAccountAsync($frontSettings, 'BANN-EDKE-Y222-2222-2222-2222-2222-2222').GetAwaiter().GetResult(); Test-Result 'banned, recover' $false 'a banned address recovered an account' }
    catch { $message = Get-ErrorMessage $_; Test-Result 'banned, recover' ($message -like '*has banned this PC or address from creating accounts. Ask the server''s operator.') $message }

    $requests = $seen.Count
    foreach ($case in @(
            @('account, unpinned', (New-Settings $true '127.0.0.1' $frontPort '' @(1, 2, 3, 4)), '127.0.0.1 failed the certificate check: it is not trusted by Windows. Nothing was sent. Check the fingerprint on the Online page or ask the operator.'),
            @('account, wrong certificate', (New-Settings $true '127.0.0.1' $frontPort $otherPin @(1, 2, 3, 4)), '127.0.0.1 failed the certificate check: it does not match the saved fingerprint. Nothing was sent. Check the fingerprint on the Online page or ask the operator.'),
            @('account, plain HTTP', (New-Settings $true '127.0.0.1' $plainPort $pin @(1, 2, 3, 4)), "Could not make a secure connection to 127.0.0.1 on port $plainPort. Check that this is the secure port the operator gives."))) {
        try { $taken = $community::CreateAccountAsync($case[1], 'Ritsuka', $true).GetAwaiter().GetResult(); Test-Result $case[0] $false "an account came back: $($taken.AccessCode)" }
        catch { $message = Get-ErrorMessage $_; Test-Result $case[0] ($message -like "*$($case[2])*") $message }
    }
    [void](Wait-Until { $seen.Count -ge $requests + 2 })
    $refusedCalls = @($seen | Select-Object -Skip $requests)
    Test-Result 'refused calls' (($refusedCalls -join ',') -eq 'closed by the client before any byte,closed by the client before any byte') "the front saw: $($refusedCalls -join ', ')"

    $closedPort = Get-FreePort
    try { [void]$community::CreateAccountAsync((New-Settings $true '127.0.0.1' $closedPort $pin @(1, 2, 3, 4)), 'Ritsuka', $true).GetAwaiter().GetResult(); Test-Result 'no server' $false 'an account came back from a closed port' }
    catch { $message = Get-ErrorMessage $_; Test-Result 'no server' ($message -like "Could not reach the community server 127.0.0.1 on port $($closedPort):*") $message }

    # The tunnel: every service carried through the front to the echo server and back.
    $tunnel = Start-Tunnel $frontPort $pin $accessCode
    try {
        $runningWhileUp = $tunnel.Running
        $listening = @([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() | Where-Object { $_.Port -in $script:tunnelPorts })
        Test-Result 'tunnel listeners' ($listening.Count -eq 4 -and @($listening | Where-Object { $_.Address.ToString() -ne '127.0.0.1' }).Count -eq 0) ('TCP on ' + (($listening | ForEach-Object { $_.ToString() }) -join ', '))
        $relaying = @([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveUdpListeners() | Where-Object { $_.Port -in $script:relayPorts })
        Test-Result 'relay listeners' ($relaying.Count -eq 2 -and @($relaying | Where-Object { $_.Address.ToString() -ne '127.0.0.1' }).Count -eq 0) ('UDP on ' + (($relaying | ForEach-Object { $_.ToString() }) -join ', '))
        $accepted = $frontState.Accepted
        Start-Sleep -Milliseconds 1000
        Test-Result 'warm, before the game' ($frontState.Accepted -eq $accepted) "the front accepted $($frontState.Accepted - $accepted) connection(s) in the tunnel's first second without a game connection"
        $services = @('title', 'billing', 'aime', 'ms')
        for ($i = 0; $i -lt 4; $i++) {
            $payload = [Text.Encoding]::ASCII.GetBytes("POST /$($services[$i]) HTTP/1.1`r`n`r`n") + [byte[]](0..255) + [byte[]]::new(40000)
            $sent = [Convert]::ToHexString($payload)
            $answer = Send-ThroughTunnel $script:tunnelPorts[$i] $payload
            $line = ([string]$frontCarried[$frontCarried.Count - 1] -split ' ', 2)[1]
            Test-Result "tunnel $($services[$i])" ($answer -eq $sent -and $echoed[$echoed.Count - 1] -eq $sent) "$($payload.Length) bytes to the echo server and back, both halves closed"
            Test-Result "first line $($services[$i])" ($line -ceq "FGOAC-TUNNEL 1 $($services[$i]) $accessCode") "the connection that carried it opened with '$line'"
        }
        $lines = @($script:sink.Lines.ToArray())
        Test-Result 'tunnel report' ($lines.Count -eq 4 -and ($lines -like 'note * carried over TLS to 127.0.0.1:*').Count -eq 4) ($lines -join ' | ')

        # Warm connections: filled after the first OK, taken by the game's next connection and refilled.
        $filled = Wait-Until { (Get-Warm) -eq $fullPool }
        Test-Result 'warm, filled' $filled "the front holds $(Get-Warm) past OK"
        $payload = [Text.Encoding]::ASCII.GetBytes("POST /warm HTTP/1.1`r`n`r`n")
        $accepted = $frontState.Accepted
        $answer = Send-ThroughTunnel $script:tunnelPorts[0] $payload
        $carrier = Get-LastCarrier
        $refilled = Wait-Until { (Get-Warm) -eq $fullPool }
        Test-Result 'warm, used' ($answer -eq [Convert]::ToHexString($payload) -and $carrier -le $accepted -and $refilled) "carried on connection $carrier, accepted before the game connected (the front had accepted $accepted); then the front holds $(Get-Warm)"
        # The front closes every warm connection, as a restart does: the game's next connection skips them for a new one.
        $accepted = $frontState.Accepted
        $held = Get-WarmClients
        foreach ($warm in $held) { $warm.Client.Dispose() }
        Start-Sleep -Milliseconds 200
        $answer = Send-ThroughTunnel $script:tunnelPorts[0] $payload
        $carrier = Get-LastCarrier
        $refilled = Wait-Until { (Get-Warm) -eq $fullPool }
        $problems = @($script:sink.Lines.ToArray() | Where-Object { $_ -like 'problem *' })
        Test-Result 'warm, dead skipped' ($held.Count -eq 6 -and $answer -eq [Convert]::ToHexString($payload) -and $carrier -gt $accepted -and $refilled -and $problems.Count -eq 0) "after the front closed $($held.Count) warm connections the game's connection was carried on connection $carrier, accepted after (the front had accepted $accepted); then the front holds $(Get-Warm); $($problems.Count) problem line(s)"
        $inUse = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Any, (Get-FreePort))
        $inUse.Start()
        try {
            $settings = New-Settings $true '127.0.0.1' $frontPort $pin @((Get-FreePort), (Get-FreePort), $inUse.LocalEndpoint.Port, (Get-FreePort))
            [FGOLocalPlatform.CommunityTunnel]::Start($settings, $accessCode, [Action[string, bool]]$null).Dispose()
            Test-Result 'port in use' $false 'the tunnel started beside a wildcard listener on its port'
        }
        catch { $message = Get-ErrorMessage $_; Test-Result 'port in use' ($message -eq "Port $($inUse.LocalEndpoint.Port) on this PC is used by another program. Close that program, then press Play Online.") $message }
        finally { $inUse.Stop() }
        $udpInUse = [Net.Sockets.UdpClient]::new([Net.IPEndPoint]::new([Net.IPAddress]::Any, 0))
        $udpPort = $udpInUse.Client.LocalEndPoint.Port
        try {
            $settings = New-Settings $true '127.0.0.1' $frontPort $pin @((Get-FreePort), (Get-FreePort), (Get-FreePort), (Get-FreePort)) @(1, 2, (Get-FreeUdpPort), $udpPort)
            [FGOLocalPlatform.CommunityTunnel]::Start($settings, $accessCode, [Action[string, bool]]$null).Dispose()
            Test-Result 'UDP port in use' $false 'the relay started beside a wildcard UDP socket on its port'
        }
        catch { $message = Get-ErrorMessage $_; Test-Result 'UDP port in use' ($message -eq "Port $udpPort on this PC is used by another program. Close that program, then press Play Online.") $message }
        finally { $udpInUse.Dispose() }
    }
    finally { Stop-Tunnel $tunnel }
    $stillOpen = @($script:tunnelPorts | Where-Object { [Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners().Port -contains $_ })
    $stillRelaying = @($script:relayPorts | Where-Object { [Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveUdpListeners().Port -contains $_ })
    Test-Result 'tunnel stopped' ($stillOpen.Count -eq 0 -and $stillRelaying.Count -eq 0) 'no listener is left on the four TCP and two UDP ports'
    $closed = Wait-Until { $frontIdle.Count -eq 0 }
    Test-Result 'warm, stopped' $closed "stopping the tunnel closed every warm connection: the front holds $($frontIdle.Count) past OK, $($frontUnused.Count) closed unused so far"
    $unknownPorts = @((Get-FreePort), (Get-FreePort), (Get-FreePort), (Get-FreePort))
    try {
        [FGOLocalPlatform.CommunityTunnel]::Start((New-Settings $true 'no-such-host.invalid' $frontPort $pin $unknownPorts), $accessCode, [Action[string, bool]]$null).Dispose()
        Test-Result 'unknown host' $false 'the tunnel started for a host that does not resolve'
    }
    catch {
        $message = Get-ErrorMessage $_
        $bound = @([Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() | Where-Object { $_.Port -in $unknownPorts })
        Test-Result 'unknown host' ($message -eq 'Could not find no-such-host.invalid. Check the address on the Online page and this PC''s internet connection, then press Play Online.' -and $bound.Count -eq 0) "Start looks the address up before it binds anything: $message"
    }
    Test-Result 'running flag' ($runningWhileUp -and !$tunnel.Running) "Running was $runningWhileUp while the tunnel was up and is $($tunnel.Running) once it stopped"

    # Warm connections replaced once 2 s old: every one held after the first OK closes unused and the pool is full again.
    $tunnel = Start-Tunnel $frontPort $pin $accessCode -WarmFor 2
    try {
        [void](Send-ThroughTunnel $script:tunnelPorts[0] ([Text.Encoding]::ASCII.GetBytes('GET / HTTP/1.1')))
        [void](Wait-Until { (Get-Warm) -eq $fullPool })
        $old = @((Get-WarmClients).Number)
        $replaced = Wait-Until {
            $unused = @($frontUnused.ToArray() | ForEach-Object { [int]($_ -split ' ')[0] })
            @($old | Where-Object { $_ -notin $unused }).Count -eq 0 -and (Get-Warm) -eq $fullPool
        }
        Test-Result 'warm, refresh' ($old.Count -eq 6 -and $replaced) "the $($old.Count) warm connections held at first were closed unused and replaced; the front holds $(Get-Warm)"
    }
    finally { Stop-Tunnel $tunnel }
    # Dials under way when the tunnel stopped reach the front's records before the next checks count them.
    [void](Wait-Until { $frontIdle.Count -eq 0 })
    Start-Sleep -Milliseconds 500

    # The UDP relay: datagrams both ways, one upstream socket per game socket, mappings dropped after 1 s of silence.
    $tunnel = Start-Tunnel $frontPort $pin $accessCode @($udpEchoes[0].Port, $udpEchoes[1].Port) 1
    $games = @((New-GameSocket), (New-GameSocket), (New-GameSocket))
    try {
        $a = Send-Datagrams $games[0] $script:relayPorts[0] @('ping-a-1', 'ping-a-2', 'ping-a-3')
        $b = Send-Datagrams $games[1] $script:relayPorts[0] @('ping-b-1')
        $c = Send-Datagrams $games[2] $script:relayPorts[1] @('dist-c-1', 'dist-c-2')
        $pingSources = @($udpEchoes[0].Record | ForEach-Object { ($_ -split ' ')[0] })
        $distSources = @($udpEchoes[1].Record | ForEach-Object { ($_ -split ' ')[0] })
        Test-Result 'relay ping' (($a -join ',') -eq 'ping-a-1,ping-a-2,ping-a-3' -and ($b -join ',') -eq 'ping-b-1') "game socket A got back $($a -join ', '), B got back $($b -join ', ')"
        Test-Result 'relay dist' (($c -join ',') -eq 'dist-c-1,dist-c-2' -and ($udpEchoes[1].Record -join ',') -like '* dist-c-1,* dist-c-2') "game socket C got back $($c -join ', ')"
        Test-Result 'relay sockets' ($pingSources.Count -eq 4 -and @($pingSources[0..2] | Select-Object -Unique).Count -eq 1 -and $pingSources[3] -ne $pingSources[0] -and @($distSources | Select-Object -Unique).Count -eq 1) "the ping server saw A from upstream port $($pingSources[0]) three times and B from $($pingSources[3]); dist saw C from $($distSources[0])"
        $quiet = "ping: UDP from 127.0.0.1:$($games[0].Client.LocalEndPoint.Port) relayed 3 datagrams out and 3 back, then went quiet."
        $expired = Wait-Until { @($script:sink.Lines.ToArray()) -contains "note $quiet" }
        $again = Send-Datagrams $games[0] $script:relayPorts[0] @('ping-a-4')
        $newSource = ($udpEchoes[0].Record[$udpEchoes[0].Record.Count - 1] -split ' ')[0]
        Test-Result 'relay expiry' ($expired -and ($again -join ',') -eq 'ping-a-4' -and $newSource -ne $pingSources[0]) "after 1 s of silence: '$quiet'; the next datagram from A came back through a new upstream port $newSource"
        $lines = @($script:sink.Lines.ToArray())
        Test-Result 'relay report' (@($lines | Where-Object { $_ -match 'ping-|dist-' }).Count -eq 0 -and ($lines -like 'note ping: relaying UDP to 127.0.0.1:*').Count -eq 1 -and ($lines -like 'note dist: relaying UDP to 127.0.0.1:*').Count -eq 1) ($lines -join ' | ')
    }
    finally {
        foreach ($game in $games) { $game.Dispose() }
        Stop-Tunnel $tunnel
    }

    # A server restart under the relay: the ping stub stops and the game keeps sending for 6 s, which draws ICMP port
    # unreachable onto the upstream socket; then a new stub on the same port answers through the same mapping.
    $restartPort = Get-FreeUdpPort
    $restartEcho = New-UdpEcho 'restart' $restartPort
    $tunnel = Start-Tunnel $frontPort $pin $accessCode @($restartPort, $udpEchoes[1].Port)
    $game = New-GameSocket
    try {
        $first = Send-Datagrams $game $script:relayPorts[0] @('restart-1')
        $upstreamPort = ($restartEcho.Record[0] -split ' ')[0]
        Stop-UdpEcho $restartEcho
        $game.Client.ReceiveTimeout = 300
        $down = Send-Datagrams $game $script:relayPorts[0] @(1..20 | ForEach-Object { "down-$_" })
        $restartEcho = New-UdpEcho 'restart' $restartPort
        $game.Client.ReceiveTimeout = 5000
        $again = Send-Datagrams $game $script:relayPorts[0] @('restart-2', 'restart-3')
        $sources = @($restartEcho.Record | ForEach-Object { ($_ -split ' ')[0] } | Select-Object -Unique)
        $answered = @($down | Where-Object { $_ -ne 'no answer' })
        Test-Result 'relay, server restarted' (($first -join ',') -eq 'restart-1' -and $answered.Count -eq 0 -and ($again -join ',') -eq 'restart-2,restart-3' -and ($sources -join ',') -eq $upstreamPort) "before the stop the game got back $($first -join ', '); $($down.Count) datagrams to the stopped port got $($answered.Count) answers; after the restart it got back $($again -join ', ') from upstream port $($sources -join ', ') (first $upstreamPort)"
        $lines = @($script:sink.Lines.ToArray())
        $silent = @($lines -like "note ping: UDP from 127.0.0.1:$($game.Client.LocalEndPoint.Port) has had no answer from 127.0.0.1:$restartPort for * s while the game kept sending*")
        Test-Result 'relay, no answer reported' ($silent.Count -eq 1 -and @($lines -like 'problem *').Count -eq 0) ($lines -join ' | ')
    }
    finally {
        $game.Dispose()
        Stop-Tunnel $tunnel
        Stop-UdpEcho $restartEcho
    }

    $firstLines = $seen.Count
    Test-Refused 'tunnel, ERR auth' $frontPort $pin '11111111111111111111' '127.0.0.1 does not know this PC''s account. Choose Recover account on the Online page and paste your recovery key.'
    Test-Refused 'tunnel, ERR banned' $frontPort $pin $bannedCode '127.0.0.1 has banned this account; ask the server''s operator.'
    Test-Refused 'tunnel, ERR busy' $frontPort $pin $busyCode '127.0.0.1 is not taking new players right now. Wait a few minutes, then press Play Online.'
    Test-Refused 'tunnel, ERR version' $frontPort $pin $versionCode '127.0.0.1 needs a newer launcher; install the update from About.'
    Test-Result 'refused first lines' (($seen[$firstLines] -eq 'FGOAC-TUNNEL 1 title 11111111111111111111') -and ($seen[$firstLines + 1] -eq "FGOAC-TUNNEL 1 title $bannedCode")) "the front read '$($seen[$firstLines])' and '$($seen[$firstLines + 1])'"
    Test-SecondConnection 'after ERR auth' '11111111111111111111' 'does not know this PC''s account' $false
    Test-SecondConnection 'after ERR banned' $bannedCode 'has banned this account' $false
    Test-SecondConnection 'after ERR version' $versionCode 'needs a newer launcher' $false
    Test-SecondConnection 'after ERR busy' $busyCode 'Wait a few minutes, then press Play Online.' $true
    $firstLines = $seen.Count
    Test-Refused 'tunnel, wrong certificate' $frontPort $otherPin $accessCode '127.0.0.1 failed the certificate check: it does not match the saved fingerprint. Nothing was sent. Check the fingerprint on the Online page or ask the operator.'
    Test-Refused 'tunnel, unpinned' $frontPort '' $accessCode '127.0.0.1 failed the certificate check: it is not trusted by Windows. Nothing was sent. Check the fingerprint on the Online page or ask the operator.'
    [void](Wait-Until { $seen.Count -ge $firstLines + 2 })
    $refusedTunnels = @($seen | Select-Object -Skip $firstLines)
    Test-Result 'nothing sent' (($refusedTunnels -join ',') -eq 'closed by the client before any byte,closed by the client before any byte') "the front saw: $($refusedTunnels -join ', ')"
    # The name comes before the fingerprint: the front's certificate, made out to 127.0.0.1, is refused as localhost's, pinned or not.
    $renamedLines = $seen.Count
    $renamed = 'localhost failed the certificate check: it is made out to another address. Nothing was sent. Check the fingerprint on the Online page or ask the operator.'
    Test-Refused 'tunnel, another address' $frontPort $pin $accessCode $renamed 'localhost'
    foreach ($case in @(@('account, another address', $pin), @('account, another address, unpinned', ''))) {
        try { $taken = $community::CreateAccountAsync((New-Settings $true 'localhost' $frontPort $case[1] @(1, 2, 3, 4)), 'Ritsuka', $false).GetAwaiter().GetResult(); Test-Result $case[0] $false "an account came back: $($taken.AccessCode)" }
        catch { $message = Get-ErrorMessage $_; Test-Result $case[0] ($message -ceq $renamed) $message }
    }
    [void](Wait-Until { $seen.Count -ge $renamedLines + 3 })
    $renamedCalls = @($seen | Select-Object -Skip $renamedLines)
    Test-Result 'nothing sent, another address' (($renamedCalls -join ',') -eq ((@('closed by the client before any byte') * 3) -join ',')) "the front saw: $($renamedCalls -join ', ')"
    Test-Refused 'tunnel, plain HTTP' $plainPort $pin $accessCode "Could not make a secure connection to 127.0.0.1 on port $plainPort. Check that this is the secure port the operator gives."
    Test-Refused 'tunnel, no server' $closedPort $pin $accessCode '127.0.0.1 did not answer. Check your internet, then press Play Online again; if it keeps happening the server may be down.'
    $clear = @($plainSeen | Where-Object { $_ -match 'FGOAC-TUNNEL|recovery_key|community' -or $_.Contains($accessCode) })
    $firstBytes = @($plainSeen | ForEach-Object { if ($_.Length -gt 0) { '0x{0:X2}' -f [int][char]$_[0] } else { 'none' } })
    Test-Result 'plain HTTP stub' ($plainSeen.Count -eq 2 -and ($firstBytes -join ',') -eq '0x16,0x16' -and $clear.Count -eq 0) "$($plainSeen.Count) connections, first bytes $($firstBytes -join ' and ') (a TLS handshake record); no code, key or tunnel line in the clear"
    $leaks = @($script:reportLines | Where-Object { $_.Contains($accessCode) -or $_.Contains($bannedCode) -or $_.Contains($busyCode) -or $_.Contains($versionCode) -or $_.Contains('11111111111111111111') })
    Test-Result 'reports' ($script:reportLines.Count -gt 0 -and $leaks.Count -eq 0) "$($script:reportLines.Count) report lines, none with an access code"

    # The probe before Play: its OK is the first warm title connection and carries the game's first title connection;
    # then one ping in the game's frame, 17 09 20 15 and a big-endian time, goes to the server's UDP ping port.
    $script:reportLines.Clear()
    $tunnel = Start-Tunnel $frontPort $pin $accessCode @($udpEchoes[2].Port, $udpEchoes[1].Port)
    try {
        $accepted = $frontState.Accepted
        $pings = (Get-Pings).Count
        $refusal = $tunnel.ProbeAsync().GetAwaiter().GetResult()
        $ping = @(Get-Pings | Select-Object -Skip $pings)
        $filled = Wait-Until { (Get-Warm) -eq $fullPool }
        $dialled = $frontState.Accepted - $accepted
        $payload = [Text.Encoding]::ASCII.GetBytes("POST /first HTTP/1.1`r`n`r`n")
        $answer = Send-ThroughTunnel $script:tunnelPorts[0] $payload
        $carrier = Get-LastCarrier
        $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
        $sent = if ($ping.Count -eq 1 -and $ping[0] -cmatch '^17092015([0-9A-F]{8})$') { [Convert]::ToInt64($Matches[1], 16) } else { 0 }
        Test-Result 'probe, OK' ($null -eq $refusal -and $filled -and $dialled -eq 6 -and $answer -eq [Convert]::ToHexString($payload) -and $carrier -eq $accepted + 1 -and [Math]::Abs($now - $sent) -le 5) "the probe returned $(if ($null -eq $refusal) { 'no refusal' } else { $refusal }); the front then held $(Get-Warm) past OK from $dialled dials; the game's first title connection was carried on connection $carrier, the probe's was $($accepted + 1); the ping port got $($ping -join ', ')"
    }
    finally { Stop-Tunnel $tunnel }
    [void](Wait-Until { $frontIdle.Count -eq 0 })
    $silentPing = Get-FreeUdpPort
    $tunnel = Start-Tunnel $frontPort $pin $accessCode @($silentPing, $udpEchoes[1].Port)
    try {
        $refusal = $tunnel.ProbeAsync().GetAwaiter().GetResult()
        $lines = @($script:sink.Lines.ToArray() | Where-Object { $_ -like 'problem *' })
        Test-Result 'probe, ping unanswered' ($refusal -ceq (Get-Unanswered $silentPing) -and ($lines -join '|') -ceq "problem ping: $refusal") "the probe returned '$refusal'; problem lines: $($lines -join ' | ')"
    }
    finally { Stop-Tunnel $tunnel }
    # Dials under way when the tunnel stopped reach the front's records before the next checks count them.
    [void](Wait-Until { $frontIdle.Count -eq 0 })
    Start-Sleep -Milliseconds 500
    Test-Probe 'probe, ERR auth' $frontPort $pin '11111111111111111111' '127.0.0.1 does not know this PC''s account. Choose Recover account on the Online page and paste your recovery key.'
    Test-Probe 'probe, ERR banned' $frontPort $pin $bannedCode '127.0.0.1 has banned this account; ask the server''s operator.'
    Test-Probe 'probe, ERR busy' $frontPort $pin $busyCode '127.0.0.1 is not taking new players right now. Wait a few minutes, then press Play Online.'
    Test-Probe 'probe, ERR version' $frontPort $pin $versionCode '127.0.0.1 needs a newer launcher; install the update from About.'
    Test-Probe 'probe, ERR service' $frontPort $pin $serviceCode '127.0.0.1 does not carry the game''s title connection; ask the operator.'
    Test-Probe 'probe, no answer' $frontPort $pin $silentCode '127.0.0.1 did not answer. Check your internet, then press Play Online again; if it keeps happening the server may be down.'
    Test-Probe 'probe, closed port' $closedPort $pin $accessCode '127.0.0.1 did not answer. Check your internet, then press Play Online again; if it keeps happening the server may be down.'
    Test-Probe 'probe, wrong pin' $frontPort $otherPin $accessCode '127.0.0.1 failed the certificate check: it does not match the saved fingerprint. Nothing was sent. Check the fingerprint on the Online page or ask the operator.'
    Test-Probe 'probe, plain HTTP' $plainPort $pin $accessCode "Could not make a secure connection to 127.0.0.1 on port $plainPort. Check that this is the secure port the operator gives."

    # The probe on the Online page: nothing bound on this PC, one title dial closed unused after OK.
    $probeSink = [TunnelReportSink]::new()
    $probeReport = [Delegate]::CreateDelegate([Action[string, bool]], $probeSink, 'Report')
    $accepted = $frontState.Accepted
    $unused = $frontUnused.Count
    $pings = (Get-Pings).Count
    $refusal = [FGOLocalPlatform.CommunityTunnel]::ProbeAsync($frontSettings, $accessCode, $probeReport).GetAwaiter().GetResult()
    $closedUnused = Wait-Until { $frontUnused.Count -eq $unused + 1 }
    Start-Sleep -Milliseconds 300
    $ping = @(Get-Pings | Select-Object -Skip $pings)
    Test-Result 'probe at start-up, OK' ($null -eq $refusal -and $closedUnused -and $frontState.Accepted -eq $accepted + 1 -and $frontIdle.Count -eq 0 -and $ping.Count -eq 1 -and $ping[0] -cmatch '^17092015[0-9A-F]{8}$') "the probe returned $(if ($null -eq $refusal) { 'no refusal' } else { $refusal }); the front accepted $($frontState.Accepted - $accepted) connection(s), $($frontUnused.Count - $unused) closed unused, $($frontIdle.Count) held past OK; the ping port got $($ping -join ', ')"
    $silentPing = Get-FreeUdpPort
    $refusal = [FGOLocalPlatform.CommunityTunnel]::ProbeAsync((New-Settings $true '127.0.0.1' $frontPort $pin @(1, 2, 3, 4) @($silentPing, $udpEchoes[1].Port, (Get-FreeUdpPort), (Get-FreeUdpPort))), $accessCode, $probeReport).GetAwaiter().GetResult()
    Test-Result 'probe at start-up, ping unanswered' ($refusal -ceq (Get-Unanswered $silentPing) -and @($probeSink.Lines.ToArray() -ceq "problem ping: $refusal").Count -eq 1) "the probe returned '$refusal'"
    # As the game does, the ping goes again each second it is unanswered, three times at most.
    $lossy = New-UdpEcho 'lossy' -Tail ([byte[]]::new(4)) -Drop 1
    $deaf = New-UdpEcho 'deaf' -Drop 3
    try {
        $refusal = [FGOLocalPlatform.CommunityTunnel]::ProbeAsync((New-Settings $true '127.0.0.1' $frontPort $pin @(1, 2, 3, 4) @($lossy.Port, $udpEchoes[1].Port, (Get-FreeUdpPort), (Get-FreeUdpPort))), $accessCode, $probeReport).GetAwaiter().GetResult()
        $ping = Get-Pings $lossy
        Test-Result 'probe at start-up, first ping lost' ($null -eq $refusal -and $ping.Count -eq 2 -and @($ping -cmatch '^17092015[0-9A-F]{8}$').Count -eq 2) "the probe returned $(if ($null -eq $refusal) { 'no refusal' } else { $refusal }); the ping port got $($ping -join ', ')"
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $refusal = [FGOLocalPlatform.CommunityTunnel]::ProbeAsync((New-Settings $true '127.0.0.1' $frontPort $pin @(1, 2, 3, 4) @($deaf.Port, $udpEchoes[1].Port, (Get-FreeUdpPort), (Get-FreeUdpPort))), $accessCode, $probeReport).GetAwaiter().GetResult()
        $waited = [Math]::Round($watch.Elapsed.TotalSeconds, 1)
        $ping = Get-Pings $deaf
        Test-Result 'probe at start-up, ping never answered' ($refusal -ceq (Get-Unanswered $deaf.Port) -and $ping.Count -eq 3 -and $waited -lt 5) "the probe returned '$refusal' after $waited s; the ping port got $($ping -join ', ')"
    }
    finally {
        Stop-UdpEcho $lossy
        Stop-UdpEcho $deaf
    }
    $refusal = [FGOLocalPlatform.CommunityTunnel]::ProbeAsync($frontSettings, '11111111111111111111', $probeReport).GetAwaiter().GetResult()
    Test-Result 'probe at start-up, ERR auth' ($refusal -ceq '127.0.0.1 does not know this PC''s account. Choose Recover account on the Online page and paste your recovery key.') "the probe returned '$refusal'"
    # A card refused with auth, banned or version is not offered again by the probe on the Online page; ERR busy is.
    $accepted = $frontState.Accepted
    $again = [FGOLocalPlatform.CommunityTunnel]::ProbeAsync($frontSettings, '11111111111111111111', $probeReport).GetAwaiter().GetResult()
    Start-Sleep -Milliseconds 300
    Test-Result 'probe at start-up, refused card kept back' ($again -ceq $refusal -and $frontState.Accepted -eq $accepted) "the second probe returned '$again' after $($frontState.Accepted - $accepted) dial(s) to the front"
    $busy = @(1, 2 | ForEach-Object { $before = $frontState.Accepted; $answer = [FGOLocalPlatform.CommunityTunnel]::ProbeAsync($frontSettings, $busyCode, $probeReport).GetAwaiter().GetResult(); Start-Sleep -Milliseconds 300; "$($frontState.Accepted - $before) $answer" })
    Test-Result 'probe at start-up, busy dials again' (@($busy | Where-Object { $_ -like '1 *is not taking new players right now*' }).Count -eq 2) ($busy -join ' | ')
    $refusal = [FGOLocalPlatform.CommunityTunnel]::ProbeAsync((New-Settings $true 'no-such-host.invalid' $frontPort $pin @(1, 2, 3, 4)), $accessCode, $probeReport).GetAwaiter().GetResult()
    Test-Result 'probe at start-up, unknown host' ($refusal -ceq 'Could not find no-such-host.invalid. Check the address on the Online page and this PC''s internet connection, then press Play Online.') "the probe returned '$refusal'"
    $probeLines = @($script:reportLines) + @($probeSink.Lines.ToArray())
    $leaks = @($probeLines | Where-Object { $_.Contains($accessCode) -or $_.Contains($bannedCode) -or $_.Contains($busyCode) -or $_.Contains($versionCode) -or $_.Contains($serviceCode) -or $_.Contains('11111111111111111111') })
    Test-Result 'probe reports' ($probeLines.Count -gt 0 -and $leaks.Count -eq 0) "$($probeLines.Count) report lines, none with an access code"

    Write-Host 'Requests the front received:'
    $seen | ForEach-Object { Write-Host "  $(Hide-MachineId $_)" }

    # Local server: the values the game gets today.
    $localCard = Join-Path $install 'DEVICE\aime.txt'
    [IO.File]::WriteAllText($localCard, "01234567890123456789`r`n")
    # The bindings file the Bindings card writes beside the runtime ini, before the script runs in either mode.
    $bindingsPath = [FGOLocalPlatform.Bindings]::PathIn($app)
    $playerBindings = [FGOLocalPlatform.Bindings]::Parse("[profile]`r`nname=pad`r`n[keys]`r`ntitle=F5`r`n[pad]`r`nstart=Back`r`n", [FGOLocalPlatform.StockControls]::Parse(''), [Action[string]]{ param($line) throw "bindings: $line" })
    $playerBindings.Save($bindingsPath)
    $bindingsHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($bindingsPath)))
    $run = Invoke-DryRun @('-SkipServerCheck')
    Test-Result 'local dry run' ($run.ExitCode -eq 0) "exit $($run.ExitCode)"
    $runtimeIni = Join-Path $install 'DEVICE\runtime\segatools.runtime.ini'
    $localIni = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($runtimeIni)))
    # The add-on: no -k and no line without App\fgoacaddon.dll; with it, one line naming it the last hook before ago.exe.
    $addOnLines = @($run.Output | Where-Object { $_.StartsWith('[add-on]') })
    $addOn = Join-Path $app 'fgoacaddon.dll'
    $audio = Join-Path $app 'FGOAudio.dll'
    foreach ($file in @($addOn, $audio)) { New-Item -ItemType File -Path $file -Force | Out-Null }
    $addOnRun = Invoke-DryRun @('-SkipServerCheck', '-EnableExperimentalAudio')
    $addOnIni = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($runtimeIni)))
    Remove-Item -LiteralPath $addOn, $audio
    $loaded = @($addOnRun.Output | Where-Object { $_.StartsWith('[add-on]') })
    Test-Result 'add-on absent' ($addOnLines.Count -eq 0) "lines naming the add-on without App\fgoacaddon.dll: $($addOnLines.Count)"
    Test-Result 'add-on loaded last' ($addOnRun.ExitCode -eq 0 -and $loaded.Count -eq 1 -and $loaded[0] -ceq '[add-on] Hooks in load order: fgohook.dll, FGOAudio.dll, fgoacaddon.dll, then ago.exe' -and $addOnIni -eq $localIni) "exit $($addOnRun.ExitCode) with FGOAudio.dll on: $($loaded -join ' | '); the same runtime ini: $($addOnIni -eq $localIni)"
    $aimePath = Get-RuntimeValue 'aime' 'aimePath'
    $dns = 'default', 'startupPort', 'billingPort', 'aimedbPort' | ForEach-Object { Get-RuntimeValue 'dns' $_ }
    Test-Result 'local [aime]' ($aimePath -eq $localCard) "aimePath=$aimePath"
    Test-Result 'local [dns]' (($dns -join ',') -eq '192.168.100.1,80,8443,22345') "default=$($dns[0]) startupPort=$($dns[1]) billingPort=$($dns[2]) aimedbPort=$($dns[3])"
    # Operator menu keys: off, the default, writes F1 and F2 unbound; on keeps the keys segatools.ini binds.
    $operatorKeys = 'test', 'service' | ForEach-Object { Get-RuntimeValue 'io4' $_ }
    Test-Result 'operator keys off' (($operatorKeys -join ',') -eq '0x0,0x0') "test=$($operatorKeys[0]) service=$($operatorKeys[1]) with operatorMenuKeys not saved"
    $settingsText = [IO.File]::ReadAllText($settingsPath)
    $settings = $settingsText | ConvertFrom-Json
    $settings | Add-Member -NotePropertyName operatorMenuKeys -NotePropertyValue $true
    [IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    $run = Invoke-DryRun @('-SkipServerCheck')
    $operatorKeys = 'test', 'service' | ForEach-Object { Get-RuntimeValue 'io4' $_ }
    $baseKeys = 'test', 'service' | ForEach-Object { Get-RuntimeValue 'io4' $_ (Join-Path $app 'segatools.ini') }
    Test-Result 'operator keys on' ($run.ExitCode -eq 0 -and $baseKeys[0] -and ($operatorKeys -join ',') -eq ($baseKeys -join ',')) "exit $($run.ExitCode), test=$($operatorKeys[0]) service=$($operatorKeys[1]); segatools.ini binds $($baseKeys -join ', ')"
    [IO.File]::WriteAllText($settingsPath, $settingsText, [Text.UTF8Encoding]::new($false))
    # PC play switches: not saved, the defaults; saved, each as saved, on or off.
    $pcPortKeys = 'noContinueTimer', 'noPopupTimers', 'noGpPurchase', 'noStoryTimer', 'noMenuTimer'
    $pcPort = $pcPortKeys | ForEach-Object { Get-RuntimeValue 'pcport' $_ }
    Test-Result 'pc play defaults' (($pcPort -join ',') -eq '1,1,0,0,0') "[pcport] $($pcPort -join ',') for $($pcPortKeys -join ',') with pcport not saved"
    $settings = $settingsText | ConvertFrom-Json
    $settings | Add-Member -NotePropertyName pcport -NotePropertyValue ([pscustomobject]@{ noContinueTimer = $false; noPopupTimers = $false; noGpPurchase = $true; noStoryTimer = $true; noPrintWait = $true; noMenuTimer = $true })
    [IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    $run = Invoke-DryRun @('-SkipServerCheck')
    $pcPort = $pcPortKeys | ForEach-Object { Get-RuntimeValue 'pcport' $_ }
    $printWait = Get-RuntimeValue 'pcport' 'noPrintWait'
    Test-Result 'pc play saved' ($run.ExitCode -eq 0 -and ($pcPort -join ',') -eq '0,0,1,1,1' -and [string]::IsNullOrEmpty($printWait)) "exit $($run.ExitCode), [pcport] $($pcPort -join ',') with the first two saved off and the last three on; noPrintWait saved by an older launcher written as '$printWait'"
    [IO.File]::WriteAllText($settingsPath, $settingsText, [Text.UTF8Encoding]::new($false))
    # Widescreen, not saved: the section at its defaults, the stock 1920x1080 touch canvas, the ini as the first local run wrote it.
    $run = Invoke-DryRun @('-SkipServerCheck')
    $defaultIniBytes = [IO.File]::ReadAllBytes($runtimeIni)
    $widescreenKeys = 'enable', 'vertPlus', 'hudAnchor', 'anchors', 'anchorFix'
    $section = [Regex]::Match([IO.File]::ReadAllText($runtimeIni), '(?ms)^\[widescreen\]\s*$(.*?)(?=^\[|\z)').Groups[1].Value
    $written = @($section -split "`r?`n" | Where-Object { $_ -match '=' } | ForEach-Object { ($_ -split '=', 2)[0].Trim() } | Sort-Object)
    $widescreen = $widescreenKeys | ForEach-Object { Get-RuntimeValue 'widescreen' $_ }
    $touch = 'inputWidth', 'inputHeight' | ForEach-Object { Get-RuntimeValue 'touch' $_ }
    $sameIni = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($defaultIniBytes)) -eq $localIni
    Test-Result 'widescreen defaults' ($run.ExitCode -eq 0 -and ($written -join ',') -eq 'anchorFix,anchors,enable,hudAnchor,vertPlus' -and ($widescreen -join ',') -eq '1,1,0,widescreen-anchors.txt,' -and ($touch -join ',') -eq '1920,1080' -and (Get-RuntimeValue 'gfx' 'preserveAspect') -eq '1' -and $sameIni) "exit $($run.ExitCode), [widescreen] $($widescreen -join ',') (keys $($written -join ',')), [touch] $($touch -join 'x'), the same ini as the first local run: $sameIni"
    $anchorsPath = Join-Path $install 'DEVICE\runtime\widescreen-anchors.txt'
    $template = if (Test-Path -LiteralPath $anchorsPath) { [IO.File]::ReadAllText($anchorsPath) } else { '' }
    $entries = @($template -split "`r?`n" | Where-Object { $_.Trim() -and !$_.TrimStart().StartsWith('#') })
    [IO.File]::WriteAllText($anchorsPath, "gam_qst_main/info_quest_ui = TR quest`r`n")
    $run = Invoke-DryRun @('-SkipServerCheck')
    $kept = [IO.File]::ReadAllText($anchorsPath)
    # The example is a line of the add-on's built-in battle table.
    $example = @($template -split "`r?`n" | Where-Object { $_ -like '# Example: *' })
    Test-Result 'widescreen anchors file' ($template.Contains('#   SCENE/LAYER = SIDE GROUP [overhang=N]') -and $template.Contains('#   SCENE/LAYER/POINT = SIDE GROUP [overhang=N]') -and ($example -join '|') -ceq '# Example: gam_qst_main/info_quest_ui = TR quest' -and $entries.Count -eq 0 -and $kept -ceq "gam_qst_main/info_quest_ui = TR quest`r`n") "the template has $(@($template -split "`r?`n").Count) lines, $($entries.Count) of them entries, the example '$($example -join '|')'; an edited file is kept by the next run: $($kept.Trim())"
    $settings = $settingsText | ConvertFrom-Json
    $settings | Add-Member -NotePropertyName widescreen -NotePropertyValue ([pscustomobject]@{ enable = $true; vertPlus = $true; hudAnchor = $false; anchors = 'widescreen-anchors.txt'; anchorFix = '' })
    [IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    $run = Invoke-DryRun @('-SkipServerCheck')
    Test-Result 'widescreen defaults saved' ($run.ExitCode -eq 0 -and [Linq.Enumerable]::SequenceEqual([IO.File]::ReadAllBytes($runtimeIni), $defaultIniBytes)) "exit $($run.ExitCode), the ini with the defaults saved is byte for byte the one with nothing saved"
    # Widescreen, HUD anchoring on at 3440x1440: the touch canvas is the native surface; nothing else changes.
    $settings.resolutionWidth = 3440
    $settings.resolutionHeight = 1440
    [IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    $run = Invoke-DryRun @('-SkipServerCheck')
    $offLines = [IO.File]::ReadAllLines($runtimeIni)
    $offTouch = 'inputWidth', 'inputHeight' | ForEach-Object { Get-RuntimeValue 'touch' $_ }
    $settings.widescreen.hudAnchor = $true
    [IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    $anchoredRun = Invoke-DryRun @('-SkipServerCheck')
    $onLines = [IO.File]::ReadAllLines($runtimeIni)
    $touch = 'inputWidth', 'inputHeight' | ForEach-Object { Get-RuntimeValue 'touch' $_ }
    $surface = 'width', 'height' | ForEach-Object { Get-RuntimeValue 'gfx' $_ }
    $changed = @(if ($onLines.Count -eq $offLines.Count) { for ($i = 0; $i -lt $onLines.Count; $i++) { if ($onLines[$i] -cne $offLines[$i]) { $onLines[$i] } } } else { 'line count differs' })
    Test-Result 'widescreen hudAnchor' ($run.ExitCode -eq 0 -and $anchoredRun.ExitCode -eq 0 -and ($offTouch -join ',') -eq '1920,1080' -and ($surface -join ',') -eq '3440,1440' -and ($touch -join ',') -eq '3440,1440' -and (Get-RuntimeValue 'gfx' 'preserveAspect') -eq '1' -and (Get-RuntimeValue 'widescreen' 'hudAnchor') -eq '1' -and ($changed -join '|') -eq 'inputWidth=3440|inputHeight=1440|hudAnchor=1') "exit $($anchoredRun.ExitCode), [touch] $($offTouch -join 'x') off and $($touch -join 'x') on for [gfx] $($surface -join 'x'); lines changed: $($changed -join ', ')"
    [IO.File]::WriteAllText($settingsPath, $settingsText, [Text.UTF8Encoding]::new($false))

    # Cursor lock: [cursor] confine 0 unless saved as a JSON true; a camera saved by an older launcher writes no
    # [camera] block.
    $run = Invoke-DryRun @('-SkipServerCheck')
    $confineDefault = Get-RuntimeValue 'cursor' 'confine'
    $settings = $settingsText | ConvertFrom-Json
    $settings | Add-Member -NotePropertyName camera -NotePropertyValue ([pscustomobject]@{ enabled = $true; mouseButton = 5; freeKey = 70 })
    $settings | Add-Member -NotePropertyName cursor -NotePropertyValue ([pscustomobject]@{ confine = $true })
    [IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    $savedRun = Invoke-DryRun @('-SkipServerCheck')
    $confineSaved = Get-RuntimeValue 'cursor' 'confine'
    $noCamera = [IO.File]::ReadAllText($runtimeIni) -notmatch '(?m)^\[camera\]'
    $settings.cursor = [pscustomobject]@{ confine = 'on' }
    [IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    $badRun = Invoke-DryRun @('-SkipServerCheck')
    $confineBad = Get-RuntimeValue 'cursor' 'confine'
    Test-Result 'cursor lock' ($run.ExitCode -eq 0 -and $savedRun.ExitCode -eq 0 -and $badRun.ExitCode -eq 0 -and $confineDefault -eq '0' -and $confineSaved -eq '1' -and $confineBad -eq '0') "exit $($run.ExitCode)/$($savedRun.ExitCode)/$($badRun.ExitCode), confine $confineDefault not saved, $confineSaved saved, $confineBad for a string"
    Test-Result 'no camera block' ($noCamera) "a saved camera from an older launcher writes [camera]: $(!$noCamera)"
    [IO.File]::WriteAllText($settingsPath, $settingsText, [Text.UTF8Encoding]::new($false))

    # Battle effects: hideNpScreen and hideWhiteFlash, written into App\segatools.ini as the card writes them, reach the runtime ini.
    $baseIni = Join-Path $app 'segatools.ini'
    $baseIniBytes = [IO.File]::ReadAllBytes($baseIni)
    [FGOLocalPlatform.BattleEffects]::new($true, $true, 100).Write($app)
    $run = Invoke-DryRun @('-SkipServerCheck')
    $npScreen = Get-RuntimeValue 'effects' 'hideNpScreen'
    $whiteFlash = Get-RuntimeValue 'effects' 'hideWhiteFlash'
    [IO.File]::WriteAllBytes($baseIni, $baseIniBytes)
    Remove-Item -LiteralPath (Join-Path $app 'graphics-effects.ini')
    Test-Result 'np screen key' ($run.ExitCode -eq 0 -and $npScreen -eq '1') "exit $($run.ExitCode), [effects] hideNpScreen=$npScreen in the runtime ini"
    Test-Result 'white flash key' ($run.ExitCode -eq 0 -and $whiteFlash -eq '1') "exit $($run.ExitCode), [effects] hideWhiteFlash=$whiteFlash in the runtime ini"

    # Community server on, saved the way the Server tab saves it; the game gets the tunnel's ports on this PC.
    $gamePorts = @((Get-FreePort), (Get-FreePort), (Get-FreePort), (Get-FreePort))
    $udpPorts = @(30111, 30112, (Get-FreeUdpPort), (Get-FreeUdpPort))
    $community::Save($app, (New-Settings $true $testHost 443 $pin $gamePorts $udpPorts))
    $saved = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    Test-Result 'settings' ($saved.serverHost -eq 'auto' -and ($saved.requiredPorts -join ',') -eq '80,8443,22345' -and $saved.serverPorts.http -eq 80 -and $saved.communityServer.tlsPort -eq 443 -and $saved.communityServer.certificatePin -eq $pin -and ($saved.communityServer.ports.PSObject.Properties.Name -join ',') -ceq 'http,billing,aime' -and $saved.communityServer.ports.aime -eq $gamePorts[2] -and $saved.communityServer.udpDistPort -eq 30112) ('communityServer=' + ($saved.communityServer | ConvertTo-Json -Compress) + '; serverHost, serverPorts and requiredPorts unchanged')
    # The matching, ping and battle ports on this PC are the ones the server names to the game, whatever the file holds.
    function Get-Loaded([int[]]$Ports) { New-Settings $true $testHost 443 $pin @($Ports[0], $Ports[1], $Ports[2], 30110) @(30111, 30112, 30111, 30112) }
    Test-Result 'settings read back' ($community::Load($app) -eq (Get-Loaded $gamePorts)) 'Load returns what Save wrote, with the matching, ping and battle ports on this PC at 30110, 30111 and 30112'
    $edited = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    $edited.communityServer.ports | Add-Member -NotePropertyName ms -NotePropertyValue 30120 -Force
    $edited.communityServer.ports | Add-Member -NotePropertyName ping -NotePropertyValue 30121 -Force
    $edited.communityServer.ports | Add-Member -NotePropertyName dist -NotePropertyValue 30122 -Force
    [IO.File]::WriteAllText($settingsPath, ($edited | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    $loaded = $community::Load($app)
    Test-Result 'fixed game ports' ($loaded -eq (Get-Loaded $gamePorts)) "ports.ms 30120, ping 30121 and dist 30122 saved by hand are read as $($loaded.Ms), $($loaded.Ping) and $($loaded.Dist)"
    # A hand-edited port out of range: Load falls back to the default and logs it once, however often it runs.
    $edited = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    $edited.communityServer.ports.http = 70000
    [IO.File]::WriteAllText($settingsPath, ($edited | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    $controlLog = Join-Path $install 'logs\server-control.log'
    New-Item -ItemType Directory -Path (Split-Path -Parent $controlLog) -Force | Out-Null
    Add-Content -LiteralPath $controlLog -Value '[yesterday] line'
    $yesterday = (Get-Date).Date.AddDays(-1)
    (Get-Item -LiteralPath $controlLog).LastWriteTime = $yesterday.AddHours(12)
    $loaded = $community::Load($app)
    $archive = Join-Path $install "logs\server-control-$($yesterday.ToString('yyyy-MM-dd')).log"
    Test-Result 'port line through the launcher log' ((Test-Path -LiteralPath $archive) -and (Get-Content -LiteralPath $archive -Raw).Contains('[yesterday] line') -and !(Get-Content -LiteralPath $controlLog -Raw).Contains('[yesterday] line')) "yesterday's file archived as $archive`: $(Test-Path -LiteralPath $archive)"
    [void]$community::Load($app)
    $portLog = @(Get-Content -LiteralPath (Join-Path $install 'logs\server-control.log') -ErrorAction SilentlyContinue | Where-Object { $_ -like '*70000*' })
    Test-Result 'port out of range' ($loaded -eq (Get-Loaded @(40080, $gamePorts[1], $gamePorts[2])) -and $portLog.Count -eq 1) "ports.http 70000 is read as $($loaded.Http), the other ports as saved; logged once: $($portLog -join ' | ')"
    # A block saved on the local server's ports 80, 8443 and 22345 moves to the Online defaults once.
    $community::Save($app, (New-Settings $true $testHost 443 $pin @(80, 8443, 22345, $gamePorts[3]) $udpPorts))
    $moved = $community::Load($app)
    $written = (Get-Item -LiteralPath $settingsPath).LastWriteTimeUtc
    $again = $community::Load($app)
    $savedPorts = (Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json).communityServer.ports
    $moveLog = @(Get-Content -LiteralPath (Join-Path $install 'logs\server-control.log') | Where-Object { $_ -like '*were 80, 8443 and 22345, the local server''s*' })
    $expected = Get-Loaded @(40080, 48443, 42345)
    Test-Result 'ports moved off the local server' ($moved -eq $expected -and $again -eq $expected -and "$($savedPorts.http),$($savedPorts.billing),$($savedPorts.aime)" -eq '40080,48443,42345' -and (Get-Item -LiteralPath $settingsPath).LastWriteTimeUtc -eq $written -and $moveLog.Count -eq 1) "read as $($moved.Http), $($moved.Billing), $($moved.Aime); the file holds $($savedPorts.http), $($savedPorts.billing), $($savedPorts.aime) and the second Load left it as written; logged once: $($moveLog -join ' | ')"
    $community::Save($app, (New-Settings $true $testHost 443 $pin $gamePorts $udpPorts))
    # The Online page is not offered in this release: a saved block with enabled true still opens Local.
    $offered = $community.GetField('Offered', [Reflection.BindingFlags]'NonPublic, Public, Static')
    $onlineSession = $community.GetMethod('OnlineSession', [Reflection.BindingFlags]'NonPublic, Public, Static')
    $session = if ($onlineSession) { $onlineSession.Invoke($null, @([string]$app)) } else { 'missing' }
    Test-Result 'online not offered' ($null -ne $offered -and $offered.GetValue($null) -eq $false -and $session -eq $false -and $community::Load($app).Enabled) "Offered=$(if ($offered) { $offered.GetValue($null) } else { 'missing' }), OnlineSession=$session, saved enabled=$($community::Load($app).Enabled)"
    $run = Invoke-DryRun @('-SkipServerCheck', '-CommunityServer')
    Test-Result 'community dry run' ($run.ExitCode -eq 0) "exit $($run.ExitCode)"
    $aimePath = Get-RuntimeValue 'aime' 'aimePath'
    $dns = 'default', 'startupPort', 'billingPort', 'aimedbPort' | ForEach-Object { Get-RuntimeValue 'dns' $_ }
    Test-Result 'community [aime]' ($aimePath -eq $cardPath) "aimePath=$aimePath"
    Test-Result 'community [dns]' (($dns -join ',') -eq "192.168.100.1,$($gamePorts[0]),$($gamePorts[1]),$($gamePorts[2])") "default=$($dns[0]) startupPort=$($dns[1]) billingPort=$($dns[2]) aimedbPort=$($dns[3])"
    Test-Result 'community [netenv]' ((Get-RuntimeValue 'netenv' 'broadcast') -eq '127.0.0.1') "broadcast=$(Get-RuntimeValue 'netenv' 'broadcast')"
    # Local and Online dry runs both leave the bindings file as written; its stock controls are the runtime ini's.
    $kept = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($bindingsPath)))
    $attack = Get-RuntimeValue 'keyboard' 'attack'
    $attackKey = if (!$attack) { 0x20 } elseif ($attack -match '^0x') { [Convert]::ToInt32($attack.Substring(2), 16) } else { [int]$attack }
    $refusal = $playerBindings.Bind('closePopup', [FGOLocalPlatform.Chord]::FromKeys([int[]]@($attackKey)), [FGOLocalPlatform.StockControls]::Read($runtimeIni))
    Test-Result 'bindings file kept' ($bindingsPath -eq (Join-Path $install 'DEVICE\runtime\bindings.ini') -and $kept -eq $bindingsHash -and $refusal -like '* is Attack in Controls, so it cannot also tap Popup: Close.') "$bindingsPath unchanged after both dry runs: $($kept -eq $bindingsHash); attack=$attack in the runtime ini: $refusal"
    # The block on without the switch: Play passes none, so the script plays Local.
    $run = Invoke-DryRun @('-SkipServerCheck')
    $aimePath = Get-RuntimeValue 'aime' 'aimePath'
    $dns = 'default', 'startupPort', 'billingPort', 'aimedbPort' | ForEach-Object { Get-RuntimeValue 'dns' $_ }
    Test-Result 'community, no -CommunityServer' ($run.ExitCode -eq 0 -and $aimePath -eq $localCard -and ($dns -join ',') -eq '192.168.100.1,80,8443,22345') "exit $($run.ExitCode) with the block saved on, aimePath=$aimePath, default=$($dns[0]) ports=$($dns[1..3] -join '/')"
    $scriptText = [IO.File]::ReadAllText((Join-Path $repository 'overlay\App\FGOAC_Launcher.ps1'))
    Test-Result 'no disagree check' ($scriptText -notmatch 'disagree' -and $scriptText -notmatch 'communityServer\.enabled') 'the launch script takes the mode from -CommunityServer and never reads communityServer.enabled'
    Test-Result 'no GPU preference write' ($scriptText -notmatch 'UserGpuPreferences' -and $scriptText -notmatch 'preferHighPerformanceGpu') 'the launch script leaves the GPU preference to the launcher'
    # The runtime ini, the amdaemon json and the anchors template go through a .tmp file moved over the old one; only the
    # live inject log and the print folder's player.json are written in place.
    $inPlace = @([Regex]::Matches($scriptText, 'WriteAllText\(\s*([^,]+),') | ForEach-Object { $_.Groups[1].Value.Trim() })
    Test-Result 'runtime files written whole' (($inPlace -join '|') -ceq '$temporary|$injectLiveLogPath|(Join-Path $printDirectory ''player.json'')' -and $scriptText -notmatch 'Copy-Item -LiteralPath \$baseIni -Destination \$runtimeIni\s') "WriteAllText targets: $($inPlace -join ', ')"
    # A launcher from before the switch fails on it before any server or game start.
    foreach ($shell in @($windowsPowerShell, (Get-Process -Id $PID).Path)) {
        $output = & $shell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $releasedLauncher -CheckOnly -SkipServerCheck -CommunityServer 2>&1
        $exitCode = $LASTEXITCODE
        $refusal = [string]($output | Where-Object { "$_" -match "parameter name 'CommunityServer'" } | Select-Object -First 1)
        Test-Result "1.2.0 script, $(Split-Path -Leaf $shell)" ($exitCode -ne 0 -and $refusal) "exit ${exitCode}: $refusal"
    }

    $run = Invoke-DryRun @('-CommunityServer')
    Test-Result 'community probe, no tunnel' ($run.ExitCode -eq 11 -and $run.Code -match "tunnel to the community server $testHost is not listening on port\(s\): $($gamePorts[0]), $($gamePorts[1]), $($gamePorts[2])\.") "exit $($run.ExitCode): $($run.Code)"

    # With autoStartLocalServer on, a start attempt would stop with code 10: the community path never starts it.
    $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    $settings.autoStartLocalServer = $true
    [IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    # The saved host does not resolve, so the tunnel listens on the saved ports with the stub front as its server; as on
    # Play, the launch script runs after the probe's OK.
    $tunnel = [FGOLocalPlatform.CommunityTunnel]::Start((New-Settings $true '127.0.0.1' $frontPort $pin $gamePorts @($udpEchoes[2].Port, $udpEchoes[1].Port, $udpPorts[2], $udpPorts[3])), $accessCode, [Delegate]::CreateDelegate([Action[string, bool]], [TunnelReportSink]::new(), 'Report'))
    try {
        $refusal = $tunnel.ProbeAsync().GetAwaiter().GetResult()
        if ($null -eq $refusal) { $run = Invoke-DryRun @('-CommunityServer') }
    }
    finally { $tunnel.Dispose() }
    Test-Result 'community probe, tunnel up' ($null -eq $refusal -and $run.ExitCode -eq 0) "the probe returned $(if ($null -eq $refusal) { 'no refusal' } else { $refusal }); exit $($run.ExitCode), no local server start $($run.Code)"

    # No Server folder: the real environment check passes Online, and nothing under Server\ appears.
    $serverFolder = Join-Path $install 'Server'
    Remove-Item -LiteralPath $serverFolder -Recurse -Force -ErrorAction SilentlyContinue
    $checkScript = Join-Path $app 'FGOAC_EnvironmentCheck.ps1'
    $stubCheck = [IO.File]::ReadAllText($checkScript)
    Copy-Item -LiteralPath (Join-Path $repository 'overlay\App\FGOAC_EnvironmentCheck.ps1') -Destination $checkScript -Force
    $listeners = foreach ($port in $gamePorts[0..2]) { $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $port); $listener.Start(); $listener }
    try { $run = Invoke-DryRun @('-CommunityServer') }
    finally {
        foreach ($listener in $listeners) { $listener.Stop() }
        [IO.File]::WriteAllText($checkScript, $stubCheck)
    }
    $printFolder = Get-RuntimeValue 'printer' 'printerOutPath'
    $cardEnd = $community::ReadCard($app, $testHost).Substring(16)
    Test-Result 'online, no Server folder' ($run.ExitCode -eq 0 -and !(Test-Path -LiteralPath $serverFolder) -and $printFolder -eq (Join-Path $install "DEVICE\print\players\community-$testHost-$cardEnd")) "exit $($run.ExitCode) with the real environment check and autoStartLocalServer on $($run.Code); Server folder afterwards: $(Test-Path -LiteralPath $serverFolder); printerOutPath=$printFolder"

    # The launcher's folder check at start-up: a locked folder is named in the Status sentence, a check that fails
    # for another reason says to open the logs, and the check's own output follows the sentence for the logs.
    $diagnostics = [FGOLocalPlatform.CommunityServer].Assembly.GetType('FGOLocalPlatform.StartupDiagnostics')
    function Invoke-FolderCheck([string]$Script, [string]$Root) {
        try { [void]$diagnostics.GetMethod('CheckFolders').Invoke($null, @($Script, $Root, $true)); return $null }
        catch { $e = $_.Exception; while ($e.InnerException) { $e = $e.InnerException }; return $e.Message }
    }
    $checkRoot = Join-Path $install 'folder check'
    New-Item -ItemType Directory -Path (Join-Path $checkRoot 'App') -Force | Out-Null
    $realCheck = Join-Path $repository 'overlay\App\FGOAC_StartupChecks.ps1'
    Test-Result 'folder check, writable' ($null -eq (Invoke-FolderCheck $realCheck $checkRoot)) 'a folder the check can write to passes'
    $lockedFolder = Join-Path $checkRoot 'logs'
    Remove-Item -LiteralPath $lockedFolder -Recurse -Force
    Set-Content -LiteralPath $lockedFolder -Value 'a file where the folder goes'
    $message = Invoke-FolderCheck $realCheck $checkRoot
    $sentence = ([string]$message -split "`n`n")[0]
    Test-Result 'folder check, locked folder' ($sentence -ceq "A folder or file the game must write to is locked: $lockedFolder. Start the launcher as administrator, then check it for free space, a read-only flag, or a block from your antivirus." -and $message -like "*Cannot write to $lockedFolder.*") "Status: $sentence | the rest: $(([string]$message -split "`n`n", 2)[1])"
    Remove-Item -LiteralPath $lockedFolder -Force
    $staleCheck = Join-Path $checkRoot 'FGO_StartupChecks-old.ps1'
    Set-Content -LiteralPath $staleCheck -Value 'function Test-FgoWritableLayout { param([Parameter(Mandatory=$true)][string]$InstallRoot) }'
    $message = Invoke-FolderCheck $staleCheck $checkRoot
    $sentence = ([string]$message -split "`n`n")[0]
    Test-Result 'folder check, check failed' ($sentence -ceq 'The check of the game folder did not finish. Open the logs for the reason, then start the launcher again.' -and $message -match "parameter name 'CommunityServer'") "Status: $sentence | the rest: $(([string]$message -split "`n`n", 2)[1])"

    # Closing the launcher runs the local stop script whenever it exists, in either mode; a missing one is one log line, never an error.
    $closeLines = [Collections.Generic.List[string]]::new()
    $closeLog = [Action[string]] { param($line) $closeLines.Add($line) }
    $closeApp = Join-Path $install 'close\App'
    $closeServer = Join-Path $install 'close\Server'
    New-Item -ItemType Directory -Path $closeApp, $closeServer -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $closeApp 'fgo-launcher.json'), '{"communityServer": {"enabled": true, "host": "community.example"}}')
    foreach ($name in @('Stop-FGOLocalServer.ps1', 'Stop-FGOLocalServerWhenIdle.ps1')) { New-Item -ItemType File -Path (Join-Path $closeServer $name) -Force | Out-Null }
    $idle = [FGOLocalPlatform.LocalServerStop]::CloseScript($closeApp, $false, $closeLog)
    $running = [FGOLocalPlatform.LocalServerStop]::CloseScript($closeApp, $true, $closeLog)
    Test-Result 'close, script present' ($idle -eq (Join-Path $closeServer 'Stop-FGOLocalServer.ps1') -and $running -eq (Join-Path $closeServer 'Stop-FGOLocalServerWhenIdle.ps1') -and $closeLines.Count -eq 0) "block on; game not running: $idle; game running: $running"
    $missing = [FGOLocalPlatform.LocalServerStop]::CloseScript($app, $false, $closeLog)
    Test-Result 'close, script missing' ($null -eq $missing -and $closeLines.Count -eq 1 -and $closeLines[0] -eq "Closing the launcher: $(Join-Path $serverFolder 'Stop-FGOLocalServer.ps1') is missing, so the local server was not stopped." -and !(Test-Path -LiteralPath $serverFolder)) ($closeLines -join ' | ')
    $missing = [FGOLocalPlatform.LocalServerStop]::CloseScript($app, $true, $closeLog)
    Test-Result 'close, scripts missing, game running' ($null -eq $missing -and $closeLines.Count -eq 2 -and $closeLines[1] -eq "Closing the launcher: $(Join-Path $serverFolder 'Stop-FGOLocalServerWhenIdle.ps1') is missing, so the local server was not stopped.") $closeLines[1]

    # The English patch into a root that holds only App\: the manifest's Server\ entry waits for a Server folder.
    $patchRoot = Join-Path $install 'patch-root'
    $package = Join-Path $install 'patch-package'
    foreach ($folder in @('patch-root\App', 'patch-package\payload\App\zh', 'patch-package\payload\Server\tools')) { New-Item -ItemType Directory -Path (Join-Path $install $folder) -Force | Out-Null }
    New-Item -ItemType File -Path (Join-Path $patchRoot 'App\FGO_Runtime.dll') -Force | Out-Null
    # The installer copies its payload only onto game 11.00: a PE header with that build's TimeDateStamp and SizeOfImage.
    $header = [byte[]]::new(0x400)
    $header[0] = 0x4D; $header[1] = 0x5A
    [BitConverter]::GetBytes([int32]0x130).CopyTo($header, 0x3C)
    [BitConverter]::GetBytes([uint32]0x00004550).CopyTo($header, 0x130)
    [BitConverter]::GetBytes([uint32]0x643F4ECD).CopyTo($header, 0x138)
    [BitConverter]::GetBytes([uint32]0x2034000).CopyTo($header, 0x148 + 56)
    [IO.File]::WriteAllBytes((Join-Path $patchRoot 'App\ago.exe'), $header)
    [IO.File]::WriteAllText((Join-Path $patchRoot 'App\fgo-launcher.json'), '{}')
    [IO.File]::WriteAllText((Join-Path $package 'payload\App\zh\text.txt'), 'english')
    [IO.File]::WriteAllText((Join-Path $package 'payload\Server\tools\tool.py'), 'tool')
    $patchFiles = [ordered]@{}
    foreach ($relative in @('App\zh\text.txt', 'Server\tools\tool.py')) { $patchFiles[$relative] = (Get-FileHash -LiteralPath (Join-Path $package "payload\$relative") -Algorithm SHA256).Hash.ToLowerInvariant() }
    [IO.File]::WriteAllText((Join-Path $package 'manifest.json'), ([ordered]@{ version = '9.9.9'; manifestHash = 'check'; files = $patchFiles } | ConvertTo-Json -Depth 4))
    $patchRuns = foreach ($withServer in @($false, $false, $true)) {
        if ($withServer) { New-Item -ItemType Directory -Path (Join-Path $patchRoot 'Server') -Force | Out-Null }
        $output = & $windowsPowerShell -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File (Join-Path $repository 'patch\Apply-EN-Patch.ps1') -InstallRoot $patchRoot -PackageRoot $package -NonInteractive 2>&1
        [pscustomobject]@{ ExitCode = $LASTEXITCODE; Last = [string]($output | Select-Object -Last 1); Server = (Test-Path -LiteralPath (Join-Path $patchRoot 'Server')); Tool = (Test-Path -LiteralPath (Join-Path $patchRoot 'Server\tools\tool.py')) }
    }
    Test-Result 'patch, App-only root' ($patchRuns[0].ExitCode -eq 0 -and (Test-Path -LiteralPath (Join-Path $patchRoot 'App\zh\text.txt')) -and (Test-Path -LiteralPath (Join-Path $patchRoot 'App\fgoac-patch.json')) -and !$patchRuns[0].Server) "exit $($patchRuns[0].ExitCode): $($patchRuns[0].Last); Server folder: $($patchRuns[0].Server)"
    Test-Result 'patch, App-only root again' ($patchRuns[1].ExitCode -eq 0 -and $patchRuns[1].Last -like '*is already installed*' -and !$patchRuns[1].Server) "exit $($patchRuns[1].ExitCode): $($patchRuns[1].Last)"
    Test-Result 'patch, Server folder added' ($patchRuns[2].ExitCode -eq 0 -and $patchRuns[2].Tool) "exit $($patchRuns[2].ExitCode): $($patchRuns[2].Last); Server\tools\tool.py: $($patchRuns[2].Tool)"
    $settings.autoStartLocalServer = $false
    [IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))

    $community::Save($app, (New-Settings $true 'other.example' 443 $pin $gamePorts $udpPorts))
    $run = Invoke-DryRun @('-SkipServerCheck', '-CommunityServer')
    Test-Result 'community, no card' ($run.ExitCode -eq 16 -and $run.Code -match 'Choose New account on the Online page') "exit $($run.ExitCode): $($run.Code)"

    # Switched back: the local values return untouched.
    $community::Save($app, (New-Settings $false $testHost 443 $pin $gamePorts $udpPorts))
    $run = Invoke-DryRun @('-SkipServerCheck')
    $aimePath = Get-RuntimeValue 'aime' 'aimePath'
    $dns = 'default', 'startupPort', 'billingPort', 'aimedbPort' | ForEach-Object { Get-RuntimeValue 'dns' $_ }
    Test-Result 'switched back' ($run.ExitCode -eq 0 -and $aimePath -eq $localCard -and ($dns -join ',') -eq '192.168.100.1,80,8443,22345') "exit $($run.ExitCode), aimePath=$aimePath, default=$($dns[0]) ports=$($dns[1..3] -join '/')"
    # The same launch as the local dry run above, which ran before any communityServer block existed.
    $switchedIni = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([IO.File]::ReadAllBytes($runtimeIni)))
    Test-Result 'switched back, same ini' ($switchedIni -eq $localIni) "the runtime ini with the block saved off has SHA-256 $switchedIni, the one written with no block $localIni"
    # The block off with the switch: Play Online passes it, so the script plays Online on the block's address, ports and card.
    $run = Invoke-DryRun @('-SkipServerCheck', '-CommunityServer')
    $aimePath = Get-RuntimeValue 'aime' 'aimePath'
    $dns = 'default', 'startupPort', 'billingPort', 'aimedbPort' | ForEach-Object { Get-RuntimeValue 'dns' $_ }
    Test-Result 'switched back, -CommunityServer' ($run.ExitCode -eq 0 -and $aimePath -eq $cardPath -and ($dns -join ',') -eq "192.168.100.1,$($gamePorts[0]),$($gamePorts[1]),$($gamePorts[2])") "exit $($run.ExitCode) with the block saved off, aimePath=$aimePath, default=$($dns[0]) ports=$($dns[1..3] -join '/')"

    # With autoStartLocalServer on, a start attempt would stop with code 10: the card check comes first.
    Remove-Item -LiteralPath $localCard
    $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    $settings.autoStartLocalServer = $true
    [IO.File]::WriteAllText($settingsPath, ($settings | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    $run = Invoke-DryRun @()
    Test-Result 'local, no card' ($run.ExitCode -eq 16 -and $run.Code -match 'Pick a Master in the Master menu on Play') "exit $($run.ExitCode) before any server start: $($run.Code)"
}
finally {
    foreach ($listener in @($frontListener, $echoListener, $plainListener)) { $listener.Stop() }
    foreach ($udpEcho in $udpEchoes) { $udpEcho.Socket.Dispose() }
    foreach ($pair in @(@($front, $frontRun), @($echo, $echoRun), @($plain, $plainRun)) + @($udpEchoes | ForEach-Object { , @($_.Runspace, $_.Run) })) {
        [void]$pair[0].EndInvoke($pair[1])
        $pair[0].Dispose()
    }
    $frontPool.Dispose()
    $certificate.Dispose()
    $otherCertificate.Dispose()
    if ($KeepFiles) { Write-Host "Kept $install" } else { Remove-Item -LiteralPath $install -Recurse -Force }
}

$sink = Get-DialogSink
Test-Result 'no window shown' ($sink.Shown -eq 0) $sink.Detail
if ($script:failed -gt 0) { Write-Host "$script:failed check(s) failed."; exit 1 }
Write-Host 'Every check passed.'
exit 0
