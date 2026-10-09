<#
Signs a release zip for the launcher's updater, or makes a new signing key.

  pwsh -File tools\Sign-Release.ps1 -NewKey <key file outside the repository>
  pwsh -File tools\Sign-Release.ps1 -Zip release\FGOAC-scooby-vX.Y.Z.zip -Key <key file>
  tools\Sign-Release.ps1 -Zip release\FGOAC-scooby-vX.Y.Z.zip -Key <key file>, <key file>   (from a pwsh prompt)

-NewKey writes a new ECDSA P-256 private key as an encrypted PKCS#8 PEM, asking for its passphrase
twice, and prints its public key (base64 SubjectPublicKeyInfo) to paste into ReleaseKeys in
src\FGOLocalPlatform\UpdateSettings.cs. Run it once for the primary key and once for the backup.

-Zip signs the ASCII statement "FGOAC-scooby X.Y.Z <lower-case hex SHA-256 of the zip>", with no
trailing newline, X.Y.Z taken from the zip name, with each -Key file, and prints the statement. The
launcher installs the release when any signature verifies under any key it lists, so a release
signed with an old and a new key reaches launchers that list either. The script checks the
signatures against the ReleaseKeys in the source, as the launcher does, prints for each key file
whether its line verifies, and only then writes
<zip>.sig (one line per key: base64 of the 64-byte IEEE P1363 signature) and <zip>.sha256. When
none verifies, launchers built from this source would refuse the release: nothing is written and an
older <zip>.sig is removed.

A key file is never read from or written into the repository, whatever link, junction or subst drive
the path goes through, and a new key's passphrase has at least 12 characters. Relative paths are
taken from the PowerShell location. -Passphrase skips every prompt.
PowerShell 7 is needed: Windows PowerShell 5.1 cannot read or write an encrypted PEM key.

Exit codes: 0 done, 1 refused or failed.
#>
[CmdletBinding(DefaultParameterSetName = 'Sign')]
param(
    [Parameter(Mandatory, ParameterSetName = 'NewKey')][string]$NewKey,
    [Parameter(Mandatory, ParameterSetName = 'Sign')][string]$Zip,
    [Parameter(Mandatory, ParameterSetName = 'Sign')][string[]]$Key,
    [securestring]$Passphrase
)
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -lt 7) { Write-Host 'Run this with pwsh 7: Windows PowerShell 5.1 cannot read or write an encrypted PEM key.'; exit 1 }

$repository = Split-Path -Parent $PSScriptRoot

# The path Windows finally opens for an existing file or folder: links, subst drives and short names resolved.
Add-Type -Namespace SignRelease -Name FinalPath -MemberDefinition @'
[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
private static extern uint GetFinalPathNameByHandleW(Microsoft.Win32.SafeHandles.SafeFileHandle handle, System.Text.StringBuilder path, uint size, uint flags);
public static string Of(string path)
{
    // FILE_READ_ATTRIBUTES, any sharing, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS so a folder opens too.
    using (Microsoft.Win32.SafeHandles.SafeFileHandle handle = CreateFileW(path, 0x80, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero))
    {
        if (handle.IsInvalid) { throw new System.ComponentModel.Win32Exception(); }
        System.Text.StringBuilder final = new System.Text.StringBuilder(32768);
        uint length = GetFinalPathNameByHandleW(handle, final, (uint)final.Capacity, 0);
        if (length == 0 || length >= final.Capacity) { throw new System.ComponentModel.Win32Exception(); }
        return final.ToString();
    }
}
'@

# True when the full path, followed through links and subst drives, lands inside the repository.
function Test-InRepository([string]$Path) {
    $existing = $Path
    while ($existing -and !(Test-Path -LiteralPath $existing)) { $existing = Split-Path -Parent $existing }
    if (!$existing) { return $false }
    $final = [IO.Path]::Join([SignRelease.FinalPath]::Of($existing), $Path.Substring($existing.Length))
    return ($final.TrimEnd('\') + '\').StartsWith([SignRelease.FinalPath]::Of($repository).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)
}

function ConvertTo-Plain([securestring]$Secret) {
    return [Net.NetworkCredential]::new('', $Secret).Password
}

try {
    # Relative paths are taken from $PWD, as PowerShell takes them, for every check, read and write below.
    $paths = $ExecutionContext.SessionState.Path
    if ($NewKey) { $NewKey = $paths.GetUnresolvedProviderPathFromPSPath($NewKey) }
    if ($Zip) { $Zip = $paths.GetUnresolvedProviderPathFromPSPath($Zip) }
    $Key = @($Key | Where-Object { $_ } | ForEach-Object { $paths.GetUnresolvedProviderPathFromPSPath($_) })
    if ($PSCmdlet.ParameterSetName -eq 'NewKey') {
        if (Test-InRepository $NewKey) { Write-Host "Refused: $NewKey is inside the repository. Keep the key outside it."; exit 1 }
        if (Test-Path -LiteralPath $NewKey) { Write-Host "Refused: $NewKey already exists."; exit 1 }
        if ($Passphrase) { $secret = ConvertTo-Plain $Passphrase }
        else {
            $secret = ConvertTo-Plain (Read-Host -Prompt 'Passphrase for the new key' -AsSecureString)
            if ($secret -cne (ConvertTo-Plain (Read-Host -Prompt 'The same passphrase again' -AsSecureString))) { Write-Host 'The two passphrases differ; no key was written.'; exit 1 }
        }
        if ($secret.Length -lt 12) { Write-Host 'Refused: the passphrase is shorter than 12 characters.'; exit 1 }
        $ecdsa = [Security.Cryptography.ECDsa]::Create([Security.Cryptography.ECCurve+NamedCurves]::nistP256)
        $pbe = [Security.Cryptography.PbeParameters]::new([Security.Cryptography.PbeEncryptionAlgorithm]::Aes256Cbc, [Security.Cryptography.HashAlgorithmName]::SHA256, 600000)
        $pem = $ecdsa.ExportEncryptedPkcs8PrivateKeyPem([char[]]$secret, $pbe)
        $stream = [IO.FileStream]::new($NewKey, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
        try { $bytes = [Text.Encoding]::ASCII.GetBytes($pem); $stream.Write($bytes, 0, $bytes.Length) }
        finally { $stream.Dispose() }
        Write-Host "Wrote $NewKey. Its public key, for ReleaseKeys in src\FGOLocalPlatform\UpdateSettings.cs:"
        Write-Output ([Convert]::ToBase64String($ecdsa.ExportSubjectPublicKeyInfo()))
        exit 0
    }

    foreach ($path in $Key) {
        if (Test-InRepository $path) { Write-Host "Refused: $path is inside the repository. Keep the key outside it."; exit 1 }
    }
    $name = [IO.Path]::GetFileName($Zip)
    # The launcher takes only the zip named exactly as its tag's version prints: no leading zeros.
    if ($name -notmatch '^FGOAC-scooby-v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)\.zip$') { Write-Host "Refused: $name is not named FGOAC-scooby-vX.Y.Z.zip with X, Y and Z written without leading zeros."; exit 1 }
    $version = '{0}.{1}.{2}' -f [int]$Matches[1], [int]$Matches[2], [int]$Matches[3]
    $sigPath = "$Zip.sig"
    if (Test-Path -LiteralPath $sigPath) { Remove-Item -LiteralPath $sigPath }
    $hash = (Get-FileHash -LiteralPath $Zip -Algorithm SHA256).Hash.ToLowerInvariant()
    # The launcher rebuilds these exact bytes from the release tag and the zip it downloaded.
    $statement = [Text.Encoding]::ASCII.GetBytes("FGOAC-scooby $version $hash")
    Write-Host "Statement: FGOAC-scooby $version $hash"

    $signatures = [Collections.Generic.List[byte[]]]::new()
    foreach ($path in $Key) {
        $secret = if ($Passphrase) { ConvertTo-Plain $Passphrase } else { ConvertTo-Plain (Read-Host -Prompt "Passphrase for $path" -AsSecureString) }
        $ecdsa = [Security.Cryptography.ECDsa]::Create()
        $ecdsa.ImportFromEncryptedPem([char[]][IO.File]::ReadAllText($path), [char[]]$secret)
        $signatures.Add($ecdsa.SignData($statement, [Security.Cryptography.HashAlgorithmName]::SHA256))
        $ecdsa.Dispose()
    }

    $source = [IO.File]::ReadAllText((Join-Path $repository 'src\FGOLocalPlatform\UpdateSettings.cs'))
    $block = [regex]::Match($source, 'ReleaseKeys\s*=\s*\{(.*?)\}', 'Singleline').Groups[1].Value
    $verified = [bool[]]::new($signatures.Count)
    foreach ($match in [regex]::Matches($block, '"([A-Za-z0-9+/=]+)"')) {
        $public = [Security.Cryptography.ECDsa]::Create()
        $public.ImportSubjectPublicKeyInfo([Convert]::FromBase64String($match.Groups[1].Value), [ref]$null)
        for ($i = 0; $i -lt $signatures.Count; $i++) {
            if ($public.VerifyData($statement, $signatures[$i], [Security.Cryptography.HashAlgorithmName]::SHA256)) { $verified[$i] = $true }
        }
        $public.Dispose()
    }
    for ($i = 0; $i -lt $signatures.Count; $i++) {
        Write-Host ("Line {0}, signed with {1}: {2}" -f ($i + 1), $Key[$i], $(if ($verified[$i]) { 'verifies against ReleaseKeys' } else { 'verifies against no key in ReleaseKeys' }))
    }
    if ($verified -notcontains $true) {
        Write-Host 'Refused: no key in ReleaseKeys (src\FGOLocalPlatform\UpdateSettings.cs) verifies these signatures, so launchers built from this source would refuse the release. Paste the public key of the key file into ReleaseKeys and rebuild, or sign with a key that is listed.'
        exit 1
    }
    [IO.File]::WriteAllText($sigPath, (($signatures | ForEach-Object { [Convert]::ToBase64String($_) }) -join "`n"), [Text.Encoding]::ASCII)
    [IO.File]::WriteAllText("$Zip.sha256", "$hash *$name" + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    Write-Host "Wrote $sigPath and $Zip.sha256; the signature verifies against ReleaseKeys."
    exit 0
}
catch {
    Write-Host "Stopped: $($_.Exception.Message)"
    exit 1
}
