[CmdletBinding()]
param([switch]$AsJson,[switch]$CommunityServer)
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
$root=Split-Path -Parent $PSScriptRoot
$checks=[Collections.Generic.List[object]]::new()
function Add-Check([string]$Name,[bool]$Passed,[string]$Detail) {
    $checks.Add([pscustomobject]@{ Name=$Name; Passed=$Passed; Detail=$Detail })
}
Add-Check 'Operating System' ([Environment]::Is64BitOperatingSystem -and [Environment]::OSVersion.Version.Major -ge 10) ([Environment]::OSVersion.VersionString+'; this update targets Windows 10/11 x64.')
Add-Check 'PowerShell' ([IntPtr]::Size -eq 8 -and $PSVersionTable.PSVersion -ge [version]'5.1' -and $PSVersionTable.PSVersion.Major -ne 6) ("Found $($PSVersionTable.PSVersion), $([IntPtr]::Size*8)-bit. Windows PowerShell 5.1 and PowerShell 7 are both supported.")
Add-Check 'Launcher runtime' $true 'The launcher carries its own runtime, so nothing else has to be installed for it.'
$criticalPaths=@('App\ago.exe','App\fgohook.dll','App\am\amdaemon.exe',
    'App\Tools\Locale_Remulator\LRHookx64.dll','DEVICE\runtime\segatools.runtime.ini',
    'DEVICE\runtime\amdaemon_main.json','Server\mariadb-10.11.16-winx64\bin\mariadbd.exe') |
    ForEach-Object { [IO.Path]::GetFullPath((Join-Path $root $_)) }
$tooLong=@($criticalPaths | Where-Object { $_.Length -ge 260 })
Add-Check 'Startup Path Length' ($tooLong.Count -eq 0) $(if($tooLong.Count){
    'These paths are too long for the native modules: '+($tooLong -join '; ')+'. Move the installation into fewer nested folders; the drive letter can stay as it is.'
}else{'The critical startup paths are short enough for the native modules. Very deep resource subfolders can still hit the native path length limit.'})
try {
    if (!('FgoEnvironmentNative' -as [type])) {
        # A DLL still marked as downloaded cannot be loaded by Windows PowerShell (0x80131515).
        Unblock-File -LiteralPath (Join-Path $PSScriptRoot 'FGO_Runtime.dll') -ErrorAction SilentlyContinue
        [void][Reflection.Assembly]::LoadFrom((Join-Path $PSScriptRoot 'FGO_Runtime.dll'))
    }
    function Test-Library([string]$Folder,[string]$Name) {
        $path=Join-Path $Folder $Name
        if (!(Test-Path -LiteralPath $path -PathType Leaf)) { return $false }
        $module=[FgoEnvironmentNative]::LoadLibraryEx($path,[IntPtr]::Zero,0x900)
        if ($module -eq [IntPtr]::Zero) { return $false }
        [void][FgoEnvironmentNative]::FreeLibrary($module)
        return $true
    }
    foreach ($runtime in @(
        @{Name='Visual C++ 2010 x64'; Files=@('msvcr100.dll','msvcp100.dll')},
        @{Name='Visual C++ 2012 x64'; Files=@('msvcr110.dll','msvcp110.dll')},
        @{Name='Visual C++ v14 x64'; Files=@('vcruntime140.dll','vcruntime140_1.dll','msvcp140.dll')}
    )) {
        $missing=@()
        foreach ($folder in @($PSScriptRoot,(Join-Path $PSScriptRoot 'am'))) {
            foreach ($name in $runtime.Files) {
                $searchFolder=if (Test-Path -LiteralPath (Join-Path $folder $name)) {$folder} else {[Environment]::SystemDirectory}
                if (!(Test-Library $searchFolder $name)) { $missing+="$searchFolder\$name" }
            }
        }
        $detail=if ($missing.Count -eq 0) {'The required DLLs load; this package ships the base runtime libraries.'} else {"Cannot load: $($missing -join '; '). Reapply the full package, or install the matching Microsoft x64 redistributable."}
        Add-Check $runtime.Name ($missing.Count -eq 0) $detail
    }
    $mediaMissing=@(@('mfplat.dll','mfreadwrite.dll') | Where-Object { !(Test-Library ([Environment]::SystemDirectory) $_) })
    Add-Check 'Windows Media Components' ($mediaMissing.Count -eq 0) $(if($mediaMissing.Count){"Cannot load $($mediaMissing -join ', '). Install the Media Feature Pack on Windows N/KN, or restore the media components on a trimmed-down Windows."}else{'The Media Foundation libraries load.'})
} catch { Add-Check 'Runtime Library Check' $false $_.Exception.Message }
# Cloud23333's probe lists the cards where his update installed it.
if (Test-Path -LiteralPath (Join-Path $root 'App\gpu-runtime\FgoGpuProbe.dll') -PathType Leaf) { try {
    . (Join-Path $PSScriptRoot 'FGO_GpuRouting.ps1')
    $gpuOutput=@((Invoke-FgoGpuProbe -RuntimeDirectory (Join-Path $root 'App\gpu-runtime') -Mode list) -split "`n" | Where-Object { $_.Trim() })
    $adapters=@()
    foreach ($line in $gpuOutput) {
        $parts=([string]$line).TrimEnd("`r").Split("`t")
        if ($parts.Count -ne 4 -or $parts[0] -notmatch '^[0-9A-Fa-f]{16}$' -or $parts[1] -notmatch '^[0-9A-Fa-f]{4}$') {
            throw ('The graphics card list has a line that cannot be read: '+[string]$line)
        }
        $adapters += [pscustomobject]@{ Luid=$parts[0]; Vendor=$parts[1].ToUpperInvariant(); Name=$parts[2]; PhysicalId=$parts[3] }
    }
    $physical=@($adapters | Where-Object { $_.PhysicalId -notmatch ':4294967295:' } | Group-Object PhysicalId | ForEach-Object { $_.Group[0] })
    if ($physical.Count -eq 0) { throw 'No physical graphics card was found. Install the full graphics driver, and do not use a remote session that only has the basic display driver.' }
    $names=@($physical | ForEach-Object {
        $adapter=$_
        $vendor=switch ($adapter.Vendor) { '10DE' {'NVIDIA'} '1002' {'AMD'} '8086' {'Intel'} default {"Vendor $($adapter.Vendor)"} }
        if ($adapter.Name.StartsWith($vendor,[StringComparison]::OrdinalIgnoreCase)) { $adapter.Name } else { "$vendor $($adapter.Name)" }
    })
    Add-Check 'Graphics Cards' $true ("Found $($physical.Count) physical graphics card$(if($physical.Count -ne 1){'s'}): $($names -join '; '). This shows the cards can be listed, not that every model and game scene has been tested. The card the game uses is picked under Graphics card on the Display settings.")
} catch { Add-Check 'Graphics Cards' $false $_.Exception.Message } }
function Get-IniNumber([string]$Path,[string]$Section,[string]$Key,[int]$Default) {
    if (!(Test-Path -LiteralPath $Path -PathType Leaf)) { return $Default }
    $current=''
    foreach ($raw in [IO.File]::ReadAllLines($Path)) {
        $line=$raw.Trim()
        if ($line -match '^\[([^\]]+)\]$') { $current=$matches[1]; continue }
        if (![string]::Equals($current,$Section,[StringComparison]::OrdinalIgnoreCase) -or $line.StartsWith(';') -or $line.StartsWith('#')) { continue }
        $pair=$line.Split('=',2)
        if ($pair.Count -ne 2 -or ![string]::Equals($pair[0].Trim(),$Key,[StringComparison]::OrdinalIgnoreCase)) { continue }
        $value=$pair[1].Split(';',2)[0].Trim()
        try { return $(if($value -match '^0[xX]([0-9A-Fa-f]+)$'){[Convert]::ToInt32($matches[1],16)}else{[int]$value}) } catch { return $Default }
    }
    return $Default
}
try {
    $inputConfig=Join-Path $root 'App\segatools.ini'
    $touchKey=Get-IniNumber $inputConfig 'touch' 'clickKey' 1
    $bindingKeys=@('up','down','left','right','attack','dash','target','np','camera','skill1','skill2','skill3','skillTarget1','skillTarget2','skillTarget3','skillTargetCancel')
    $conflicts=@()
    if ($touchKey -ne 0) {
        foreach ($key in $bindingKeys) { if ((Get-IniNumber $inputConfig 'keyboard' $key -1) -eq $touchKey) { $conflicts += "keyboard.$key" } }
        foreach ($entry in @(@('io4','test'),@('io4','service'),@('io4','coin'),@('aime','scan'))) {
            if ((Get-IniNumber $inputConfig ($entry[0]) ($entry[1]) -1) -eq $touchKey) { $conflicts += "$($entry[0]).$($entry[1])" }
        }
    }
    $touchName=switch ($touchKey) { 0 {'Disabled'} 1 {'Left mouse button'} 2 {'Right mouse button'} 4 {'Middle mouse button'} 5 {'Mouse button 4'} 6 {'Mouse button 5'} default {"Key 0x$($touchKey.ToString('X'))"} }
    Add-Check 'Touch Key Conflict' ($conflicts.Count -eq 0) $(if($conflicts.Count){
        "$touchName is also bound to $($conflicts -join ', '); the touch hook competes with those actions, so mouse clicks fail or work only some of the time. Restore the default mapping on the Controls settings, or keep the touch key free of every other keyboard and mouse action; do not edit segatools.ini to get around this check."
    }else{"Touch click: $touchName; no other keyboard or mouse action uses it. Controller buttons are not part of this check."})
} catch { Add-Check 'Touch Key Conflict' $false $_.Exception.Message }
try {
    $service=Get-Service -Name Audiosrv
    Add-Check 'Windows Audio Service' ($service.Status -eq 'Running') ("Current state: $($service.Status). Start the Windows Audio service if it is not running.")
    $registry=[Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::LocalMachine,[Microsoft.Win32.RegistryView]::Registry64)
    $render=$registry.OpenSubKey('SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render')
    $active=0
    try {
        if ($render) { foreach ($name in $render.GetSubKeyNames()) {
            $endpoint=$render.OpenSubKey($name)
            try { if (([long]$endpoint.GetValue('DeviceState',0) -band 1) -ne 0) { $active++ } } finally { $endpoint.Dispose() }
        } }
    } finally { if ($render) {$render.Dispose()}; $registry.Dispose() }
    Add-Check 'Audio Output Device' ($active -gt 0) ("Found $active enabled output endpoints; the game uses shared mode. If there are none, connect or enable speakers or headphones and set a default output device in Windows.")
} catch { Add-Check 'Audio Check' $false $_.Exception.Message }
$required=@('App\ago.exe','App\am\amdaemon.exe','App\fgohook.dll','App\FGO_Runtime.dll','App\inject.exe','App\Tools\Locale_Remulator\LRHookx64.dll','App\config.json','App\segatools.ini','AMFS\ICF1','AMFS\ICF2')
if (!$CommunityServer) { $required+='Server\mariadb-10.11.16-winx64\bin\mariadbd.exe' }
$missing=@($required | Where-Object {!(Test-Path -LiteralPath (Join-Path $root $_) -PathType Leaf)})
Add-Check 'Game and Server Files' ($missing.Count -eq 0) $(if($missing -contains 'App\FGO_Runtime.dll'){'Missing: '+($missing -join ', ')+'. App\FGO_Runtime.dll comes with Cloud23333''s V1.01 update: apply his V1.01 or V1.02 update to the game folder, then run FGOAC scooby again.'}elseif($missing.Count){'Missing: '+($missing -join ', ')+'. Restore the full installation package; an incremental update does not contain the game itself.'}else{'The core program files are present; this check does not verify every ROM resource.'})
if (!$CommunityServer) { try {
    $python=Join-Path $root 'Server\python\python.exe'
    $result=& $python -I -c "import sys,yaml,sqlalchemy,aiomysql,uvicorn,starlette,Crypto; print(sys.version.split()[0])" 2>&1
    if ($LASTEXITCODE -ne 0) {throw ($result -join "`n")}
    Add-Check 'Bundled Python and Server Libraries' $true ("Python $result and the main server libraries load; no system Python or online dependency install is needed.")
} catch { Add-Check 'Bundled Python and Server Libraries' $false ("The bundled environment is incomplete: $($_.Exception.Message). Restore Server/python and Server/venv, and do not overwrite them with another project's venv.") } }
$folders=if ($CommunityServer) {@('App','AMFS','GameData','DEVICE','logs')} else {@('App','AMFS','GameData','DEVICE','Server\state','Server\data\mariadb','logs')}
foreach ($folder in $folders) {
    $probe=Join-Path (Join-Path $root $folder) ('.fgo-env-'+[guid]::NewGuid().ToString('N'))
    try { [IO.File]::WriteAllText($probe,'check'); [IO.File]::Delete($probe); Add-Check ("Folder permissions: $folder") $true 'Temporary files can be created and deleted.' }
    catch { Add-Check ("Folder permissions: $folder") $false ("Cannot write: $($_.Exception.Message). Run the launcher as administrator and make sure the drive is not full, write-protected, or blocked by your security software.") }
}
# Cloud23333's database file check, from his Server\Repair-FGODatabaseFiles.ps1: opens the existing file without changing it.
function Test-FgoInnoDbDataFile {
    param([string]$DataFile, [switch]$AttributesOnly)
    try {
        $attributes = [IO.File]::GetAttributes($DataFile)
        if (($attributes -band [IO.FileAttributes]::ReadOnly) -ne 0) {
            return [pscustomobject]@{ Passed=$false; Code='READONLY'; Detail="ibdata1 has the ReadOnly attribute: $DataFile" }
        }
        if (-not $AttributesOnly) {
            $stream = [IO.File]::Open($DataFile, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::ReadWrite)
            $stream.Dispose()
        }
        $detail = if ($AttributesOnly) { "ReadOnly is clear; the database is running, so the file-open test waits: $DataFile" }
                  else { "The existing file opens for reading and writing without changing its data: $DataFile" }
        return [pscustomobject]@{ Passed=$true; Code='OK'; Detail=$detail }
    } catch {
        $errorObject = $_.Exception
        while ($errorObject.InnerException) { $errorObject = $errorObject.InnerException }
        $nativeCode = $errorObject.HResult -band 0xffff
        $code = switch ($nativeCode) { 2 {'MISSING'} 3 {'MISSING'} 5 {'ACCESS_DENIED'} 32 {'IN_USE'} 33 {'IN_USE'} default {'IO_ERROR'} }
        return [pscustomobject]@{ Passed=$false; Code=$code; Detail="$DataFile : $($errorObject.Message) (Win32=$nativeCode)" }
    }
}
if (!$CommunityServer) { try {
    $databaseExe = Join-Path $root 'Server\mariadb-10.11.16-winx64\bin\mariadbd.exe'
    $databaseRunning = @(Get-Process -Name mariadbd -ErrorAction SilentlyContinue | Where-Object {
        [string]::Equals($_.Path, $databaseExe, [StringComparison]::OrdinalIgnoreCase)
    }).Count -gt 0
    $dataCheck = Test-FgoInnoDbDataFile -DataFile (Join-Path $root 'Server\data\mariadb\ibdata1') -AttributesOnly:$databaseRunning
    Add-Check 'Database Files' $dataCheck.Passed ("[$($dataCheck.Code)] $($dataCheck.Detail)")
} catch { Add-Check 'Database Files' $false $_.Exception.Message } }
Add-Check 'Network Mode' $true 'In auto mode the game reaches the local services through an in-process virtual adapter and 127.0.0.1, so no physical adapter, gateway or internet connection is needed. This check does not start the server.'
if ($AsJson) { ConvertTo-Json -InputObject @($checks.ToArray()) -Depth 4 }
else { foreach ($check in $checks) { $status=if($check.Passed){'PASS'}else{'ACTION NEEDED'}; Write-Output "[$status] $($check.Name): $($check.Detail)`n" } }
