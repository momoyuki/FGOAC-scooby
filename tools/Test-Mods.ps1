<#
Checks the Mods page's MOD system, Cloud23333's code ported into src\FGOLocalPlatform\Mods, against the files his hooks
and his launcher read: a throwaway App\1_MOD under %TEMP%, deleted when the check ends; the game's own files are only
read, from -GameRoot. Nothing is shown: the launcher's message boxes and dialogs go to its sink.

  pwsh -File tools\Test-Mods.ps1 [-GameRoot <install>\App] [-CloudApp <his payload>\App]

-CloudApp is the App folder of his 1.10 payload: his App\1_MOD\Tools\ffmpeg.exe, his App\1_MOD\Dialogue\Catalog and
his FGO_ModRuntime.ps1, the composer his launch script runs.

Through the launcher assembly (src\bin\Release, so run build.cmd first):
  - music: an import is 44.1 kHz stereo Vorbis named 1_MOD/BGM/<32 hex>.ogg with LOOP_BEGIN/LOOP_END in samples, the
    whole track when no loop is given; the automatic loudness measures against the game's own track of the pool; a
    loop edit rewrites only the loop tags, the audio packets unchanged; the loop finder answers with one of its notes;
  - BGM\settings.json and BGM\runtime.ini in his keys and order; the pools and their usage from the game's files;
  - 1_MOD\runtime.ini composed by the launcher equals, byte for byte, the file his FGO_ModRuntime.ps1 writes from the
    same tree, with no package and with a music package chosen;
  - a music package exported and imported again: mod-package.json in his schema, the package under Packages\<id>,
    chosen in packages.json and named in [mod_sources]; his import checks still refuse a backslash name, an entry
    outside the package's folder and a package for another game version;
  - dialogue: a quest trigger with a click-to-continue image built into Dialogue\runtime.ini, its script and its
    1920x1080 24-bit continue.bmp; a portrait a line uses cannot be deleted, an unused one can;
  - Master portraits: an image of the wrong size is refused; one of the right size builds [master] in Master\runtime.ini;
  - a loading image builds [loading] in Loading\runtime.ini;
  - no window was shown.

Exit codes: 0 every check passed, 1 a check failed, 2 a prerequisite is missing.
#>
[CmdletBinding()]
param(
    [string]$GameRoot = 'D:\FGOA\App',
    [string]$CloudApp = "$env:FGOAC_CLOUD_UPDATE\payload\payload\App"
)
$ErrorActionPreference = 'Stop'

$repository = Split-Path -Parent $PSScriptRoot
$assembly = Join-Path $repository 'src\bin\Release\net6.0-windows\FGOLocalPlatform.dll'
if ($PSVersionTable.PSVersion.Major -lt 7) { Write-Host 'Run this with pwsh 7: the launcher assembly targets .NET 6.'; exit 2 }
if (!(Test-Path -LiteralPath $assembly)) { Write-Host "Missing $assembly. Run build.cmd first."; exit 2 }
foreach ($path in (Join-Path $GameRoot 'rom\single'), (Join-Path $CloudApp '1_MOD\Tools\ffmpeg.exe'), (Join-Path $CloudApp '1_MOD\Dialogue\Catalog\portraits.json'), (Join-Path $CloudApp 'FGO_ModRuntime.ps1')) {
    if (!(Test-Path -LiteralPath $path)) { Write-Host "Missing $path."; exit 2 }
}

# WPF imaging and the dispatcher the async ports resume on need an STA thread.
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    & (Join-Path $PSHOME 'pwsh.exe') -STA -NoProfile -File $PSCommandPath -GameRoot $GameRoot -CloudApp $CloudApp
    exit $LASTEXITCODE
}

$script:failed = 0
function Test-Result([string]$Name, [bool]$Passed, [string]$Detail) {
    Write-Host ("{0} {1}: {2}" -f $(if ($Passed) { 'PASS' } else { 'FAIL' }), $Name, $Detail)
    if (!$Passed) { $script:failed++ }
}
function Wait-Task($Task) {
    while (!$Task.IsCompleted) { [Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke([Action] {}, [Windows.Threading.DispatcherPriority]::Background); Start-Sleep -Milliseconds 20 }
    if ($Task.IsFaulted) { throw $Task.Exception.InnerException }
    return $Task.Result
}
# The ffmetadata tags and the audio stream line of a file, as ffmpeg reports them.
function Get-Tags([string]$File) {
    $lines = & $script:ffmpeg -nostdin -hide_banner -i $File -map_metadata 0:s:0 -f ffmetadata - 2>&1 | ForEach-Object { "$_" }
    $tags = @{}
    foreach ($line in $lines) { if ($line -match '^(LOOP_BEGIN|LOOP_END)=(\d+)$') { $tags[$Matches[1]] = $Matches[2] } }
    $tags['stream'] = @($lines | Where-Object { $_ -match 'Audio:' })[0]
    return $tags
}
function Get-AudioHash([string]$File) {
    return (& $script:ffmpeg -nostdin -v error -i $File -map 0:a -c:a copy -f md5 - 2>$null | Select-Object -Last 1)
}
# A 16-bit stereo 44.1 kHz WAV of a tone.
function Write-Wave([string]$Path, [double]$Seconds, [double]$Hertz) {
    $frames = [int](44100 * $Seconds)
    $data = [byte[]]::new($frames * 4)
    for ($i = 0; $i -lt $frames; $i++) {
        $sample = [int16](8000 * [Math]::Sin(2 * [Math]::PI * $Hertz * $i / 44100))
        $bytes = [BitConverter]::GetBytes($sample)
        $data[$i * 4] = $bytes[0]; $data[$i * 4 + 1] = $bytes[1]; $data[$i * 4 + 2] = $bytes[0]; $data[$i * 4 + 3] = $bytes[1]
    }
    $stream = [IO.MemoryStream]::new()
    $writer = [IO.BinaryWriter]::new($stream)
    $writer.Write([Text.Encoding]::ASCII.GetBytes('RIFF')); $writer.Write([int](36 + $data.Length)); $writer.Write([Text.Encoding]::ASCII.GetBytes('WAVEfmt '))
    $writer.Write([int]16); $writer.Write([int16]1); $writer.Write([int16]2); $writer.Write([int]44100); $writer.Write([int](44100 * 4)); $writer.Write([int16]4); $writer.Write([int16]16)
    $writer.Write([Text.Encoding]::ASCII.GetBytes('data')); $writer.Write([int]$data.Length); $writer.Write($data)
    [IO.File]::WriteAllBytes($Path, $stream.ToArray())
}
function Write-Png([string]$Path, [int]$Width, [int]$Height) {
    $pixels = [byte[]]::new($Width * $Height * 4)
    for ($i = 0; $i -lt $pixels.Length; $i += 4) { $pixels[$i] = 200; $pixels[$i + 1] = 60; $pixels[$i + 2] = 120; $pixels[$i + 3] = 255 }
    $bitmap = [Windows.Media.Imaging.BitmapSource]::Create($Width, $Height, 96, 96, [Windows.Media.PixelFormats]::Bgra32, $null, $pixels, $Width * 4)
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $file = [IO.File]::Create($Path)
    try { $encoder.Save($file) } finally { $file.Dispose() }
}
function Copy-ZipWith([string]$Source, [string]$Destination, [scriptblock]$Change) {
    Copy-Item -LiteralPath $Source -Destination $Destination
    $zip = [IO.Compression.ZipFile]::Open($Destination, [IO.Compression.ZipArchiveMode]::Update)
    try { & $Change $zip } finally { $zip.Dispose() }
}

$work = Join-Path ([IO.Path]::GetTempPath()) ('fgoac-mods-' + [guid]::NewGuid().ToString('N'))
$app = Join-Path $work 'App'
$root = Join-Path $app '1_MOD'
try {
    New-Item -ItemType Directory -Path (Join-Path $root 'Tools'), (Join-Path $root 'Dialogue'), (Join-Path $app 'rom\sound\bgm') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $CloudApp '1_MOD\Tools\ffmpeg.exe') -Destination (Join-Path $root 'Tools')
    Copy-Item -LiteralPath (Join-Path $CloudApp '1_MOD\Dialogue\Catalog') -Destination (Join-Path $root 'Dialogue') -Recurse
    Copy-Item -LiteralPath (Join-Path $GameRoot 'rom\sound\bgm\arms_bgm04.ogg') -Destination (Join-Path $app 'rom\sound\bgm')
    $script:ffmpeg = Join-Path $root 'Tools\ffmpeg.exe'
    [AppContext]::SetData('APP_CONTEXT_BASE_DIRECTORY', $app + '\')
    Add-Type -AssemblyName PresentationFramework, System.IO.Compression.FileSystem
    $launcher = [Reflection.Assembly]::LoadFrom($assembly)
    . (Join-Path $PSScriptRoot 'DialogSink.ps1'); Enable-DialogSink $assembly
    [Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new())
    $storage = [FGOLocalPlatform.ModStorage]

    # Music.
    $wave = Join-Path $work 'tone.wav'
    Write-Wave $wave 2 440
    $looped = Wait-Task ($storage::Import($root, $wave, 'battle_all', [Nullable[double]]0.5, [Nullable[double]]1.5, $false, $false))
    $tags = Get-Tags ($storage::TrackPath($root, $looped.File))
    Test-Result 'an import is 44.1 kHz stereo Vorbis with the loop in samples' ($looped.File -cmatch '\A1_MOD/BGM/[0-9a-f]{32}\.ogg\z' -and $tags.LOOP_BEGIN -eq '22050' -and $tags.LOOP_END -eq '66150' -and $tags.stream -match 'vorbis' -and $tags.stream -match '44100 Hz' -and $tags.stream -match 'stereo') "$($looped.File); $($tags.LOOP_BEGIN)..$($tags.LOOP_END); $($tags.stream)"
    $whole = Wait-Task ($storage::Import($root, $wave, 'battle_all', $null, $null, $false, $false))
    $tags = Get-Tags ($storage::TrackPath($root, $whole.File))
    Test-Result 'with no loop the whole track loops' ($tags.LOOP_BEGIN -eq '0' -and [int]$tags.LOOP_END -ge 88000 -and [int]$tags.LOOP_END -le 88200) "$($tags.LOOP_BEGIN)..$($tags.LOOP_END)"
    $loud = Wait-Task ($storage::Import($root, $wave, 'menu', $null, $null, $false, $true))
    Test-Result "the automatic loudness measures against the game's own track" ($loud.AutoLoudness -and [double]::IsFinite($loud.GainDb) -and $loud.GainDb -ne 0) "gain $($loud.GainDb) dB"
    $before = Get-AudioHash ($storage::TrackPath($root, $looped.File))
    $edited = Wait-Task ([FGOLocalPlatform.ModAudioEditing]::UpdateLoop($root, $looped, 0.25, 1.25))
    $tags = Get-Tags ($storage::TrackPath($root, $edited.File))
    $after = Get-AudioHash ($storage::TrackPath($root, $edited.File))
    Test-Result 'a loop edit rewrites the tags and keeps the audio packets' ($tags.LOOP_BEGIN -eq '11025' -and $tags.LOOP_END -eq '55125' -and $before.Length -gt 0 -and $before -ceq $after) "$($tags.LOOP_BEGIN)..$($tags.LOOP_END); audio $before -> $after"
    $analysis = Wait-Task ([FGOLocalPlatform.ModAudioAnalysis]::Loop($script:ffmpeg, $wave))
    Test-Result 'the loop finder answers with one of its notes' ($analysis.Method -cin 'Loop tags of the file', 'No clear repeat found; the whole track loops', 'Silent track; the whole track loops', 'Found by matching the waveform') $analysis.Method

    $settings = [FGOLocalPlatform.ModSettings]::new()
    $settings.BgmEnabled = $true
    $settings.Tracks.Add($edited); $settings.Tracks.Add($loud)
    $storage::SaveComponent($root, $settings, $true)
    $runtime = [IO.File]::ReadAllText((Join-Path $root 'BGM\runtime.ini'))
    $expected = "[bgm]`r`nenabled=1`r`ncount=2`r`n[track0]`r`npool=battle_all`r`nfile=$($edited.File)`r`n[track1]`r`npool=menu`r`nfile=$($loud.File)`r`n"
    $json = Get-Content -LiteralPath (Join-Path $root 'BGM\settings.json') -Raw | ConvertFrom-Json
    $keys = ($json.PSObject.Properties.Name -join ',') + ' / ' + ($json.Tracks[0].PSObject.Properties.Name -join ',')
    Test-Result 'BGM\settings.json and BGM\runtime.ini in his keys and order' ($runtime -ceq $expected -and $keys -ceq 'Title,Author,Version,Description,BgmEnabled,Tracks / Pool,Title,File,LoopBegin,LoopEnd,AutoLoudness,GainDb,LoopAnalysis') $keys

    $pools = $storage::Pools($GameRoot)
    $first = @($pools | Select-Object -First 7 | ForEach-Object Key) -join ','
    $menu = @($pools | Where-Object Key -ceq 'arms_bgm04.ogg')[0]
    Test-Result "the pools and their usage come from the game's files" ($first -ceq 'battle_all,menu,room,menu_room_shared,arms_bgm15.ogg,arms_bgm16.ogg,arms_bgm23.ogg' -and $menu.Label -ceq 'Home · arms_bgm04' -and $menu.Usage.Length -gt 0) "$($pools.Count) pools: $first; $($menu.Label)"

    # Composition, against his launch script.
    function Compare-Composer([string]$Name) {
        $theirs = Join-Path $work ('theirs-' + [guid]::NewGuid().ToString('N'))
        Copy-Item -LiteralPath $root -Destination $theirs -Recurse
        Remove-Item -LiteralPath (Join-Path $theirs 'runtime.ini') -ErrorAction SilentlyContinue
        & { . (Join-Path $CloudApp 'FGO_ModRuntime.ps1'); Update-FgoModRuntime -ModRoot $theirs }
        [FGOLocalPlatform.ModLibrary]::Compose($root)
        $ours = [IO.File]::ReadAllBytes((Join-Path $root 'runtime.ini'))
        $his = [IO.File]::ReadAllBytes((Join-Path $theirs 'runtime.ini'))
        $same = [Linq.Enumerable]::SequenceEqual($ours, $his)
        Test-Result $Name $same "$($ours.Length) bytes, his $($his.Length)"
        return [Text.Encoding]::UTF8.GetString($ours)
    }
    Remove-Item -LiteralPath (Join-Path $root 'runtime.ini') -ErrorAction SilentlyContinue
    [void](Compare-Composer '1_MOD\runtime.ini equals his composer with no package')

    # A music package: exported, imported, chosen.
    $zip = Join-Path $work 'battle.zip'
    $settings.Title = 'Battle set'; $settings.Author = 'Tester'; $settings.Version = '2.1'
    [FGOLocalPlatform.ModPackage]::Export($GameRoot, $root, $settings, $zip, $true)
    $archive = [IO.Compression.ZipFile]::OpenRead($zip)
    try {
        $names = @($archive.Entries | ForEach-Object FullName)
        $reader = [IO.StreamReader]::new($archive.GetEntry('mod-package.json').Open())
        $info = $reader.ReadToEnd() | ConvertFrom-Json; $reader.Dispose()
    } finally { $archive.Dispose() }
    $outside = @($names | Where-Object { $_ -cne 'mod-package.json' -and $_ -cne 'README.txt' -and !$_.StartsWith('App/1_MOD/BGM/', [StringComparison]::Ordinal) })
    Test-Result 'an exported package is his schema with only the music' ($info.schemaVersion -eq 3 -and $info.component -ceq 'audio' -and $info.gameVersion -ceq '11.51' -and $info.Title -ceq 'Battle set' -and $outside.Count -eq 0 -and $names -contains 'App/1_MOD/BGM/settings.json' -and $names -contains 'App/1_MOD/BGM/runtime.ini') "$($names.Count) entries; outside: $($outside -join ', ')"

    $refusals = foreach ($case in @(
            @('a backslash name', { param($z) $e = $z.CreateEntry('App\1_MOD\BGM\extra.txt'); $w = [IO.StreamWriter]::new($e.Open()); $w.Write('x'); $w.Dispose() }),
            @('an entry outside its folder', { param($z) $e = $z.CreateEntry('App/1_MOD/Master/extra.txt'); $w = [IO.StreamWriter]::new($e.Open()); $w.Write('x'); $w.Dispose() }),
            @('another game version', { param($z) $old = $z.GetEntry('mod-package.json'); $r = [IO.StreamReader]::new($old.Open()); $text = $r.ReadToEnd(); $r.Dispose(); $old.Delete(); $e = $z.CreateEntry('mod-package.json'); $w = [IO.StreamWriter]::new($e.Open()); $w.Write($text.Replace('"11.51"', '"11.00"')); $w.Dispose() }))) {
        $bad = Join-Path $work ('bad-' + [guid]::NewGuid().ToString('N') + '.zip')
        Copy-ZipWith $zip $bad $case[1]
        try { [void][FGOLocalPlatform.ModInstall]::Install($root, $bad, $false); "$($case[0]): taken" } catch { "$($case[0]): refused" }
    }
    $leftover = @(Get-ChildItem -LiteralPath (Join-Path $root 'Packages') -Directory -ErrorAction SilentlyContinue).Count
    Test-Result 'his import checks refuse a bad package' (@($refusals | Where-Object { $_ -like '*: taken' }).Count -eq 0 -and $leftover -eq 0) (($refusals -join '; ') + "; packages left $leftover")

    $id = [FGOLocalPlatform.ModInstall]::Install($root, $zip, $true)
    $installed = Get-Content -LiteralPath (Join-Path $root "Packages\$id\BGM\installed-package.json") -Raw | ConvertFrom-Json
    $selection = Get-Content -LiteralPath (Join-Path $root 'packages.json') -Raw | ConvertFrom-Json
    $listed = @([FGOLocalPlatform.ModLibrary]::List($root))
    Test-Result 'an imported package is kept apart and chosen' ($id -cmatch '\A[0-9a-f]{32}\z' -and $installed.Component -ceq 'audio' -and $installed.GameVersion -ceq '11.51' -and $selection.SchemaVersion -eq 1 -and $selection.Selected.audio -ceq $id -and $listed.Count -eq 1 -and (Test-Path -LiteralPath (Join-Path $root "Packages\$id\BGM\runtime.ini"))) "id $id; listed $($listed.Count)"
    $composed = Compare-Composer '1_MOD\runtime.ini equals his composer with a music package chosen'
    Test-Result 'the chosen package is named in [mod_sources]' ($composed.EndsWith("[mod_sources]`r`naudio=Packages/$id`r`n")) ($composed.Substring([Math]::Max(0, $composed.Length - 60)).Replace("`r`n", ' / '))
    [FGOLocalPlatform.ModLibrary]::Select($root, 'audio', [NullString]::Value)

    # Dialogue, the portrait library and the click-to-continue screen.
    $quest = foreach ($file in Get-ChildItem -LiteralPath (Join-Path $GameRoot 'rom\single') -Filter 'quest_prop_*.txt') {
        $text = [IO.File]::ReadAllText($file.FullName)
        if ($text -match '(?m)^quest_id=(\d+)' -and $text -match '(?m)^battle\.1\.wave_id=') { [int]([regex]::Match($text, '(?m)^quest_id=(\d+)').Groups[1].Value); break }
    }
    $native = @([FGOLocalPlatform.ModDialogueCompiler]::NativePortraits($GameRoot, $root))[0]
    Write-Png (Join-Path $work 'face.png') 300 400
    $added = [FGOLocalPlatform.ModPortraits]::ImportBatch($root, [string[]]@((Join-Path $work 'face.png')), $null)
    $custom = $added.Item1[0].Reference
    $dialogue = [FGOLocalPlatform.ModDialogue]::new()
    $dialogue.Quest = $quest; $dialogue.Trigger = 1; $dialogue.ContinueScreen = $true; $dialogue.ContinueCustomImage = $true
    foreach ($portrait in $native, $custom) { $line = [FGOLocalPlatform.ModLine]::new(); $line.Portrait = $portrait; $line.Name = 'Mash'; $line.Text = 'Senpai.'; $dialogue.Lines.Add($line) }
    Write-Png (Join-Path $work 'continue.png') 800 600
    [FGOLocalPlatform.ModContinueImage]::Import($root, $dialogue.Id, (Join-Path $work 'continue.png'))
    $settings = $storage::Load($root)
    $settings.DialogueEnabled = $true
    $settings.Dialogues.Add($dialogue)
    try { [FGOLocalPlatform.ModPortraits]::Remove($root, $custom, $settings.Dialogues, $null); $kept = 'deleted' } catch { $kept = 'refused' }
    [FGOLocalPlatform.ModDialogueCompiler]::Build($GameRoot, $root, $settings)
    $storage::Save($root, $settings)
    $runtime = [IO.File]::ReadAllText((Join-Path $root 'Dialogue\runtime.ini'))
    $resource = [FGOLocalPlatform.ModDialogueCompiler]::EventResource($settings.DialogueFolder, $dialogue.Id)
    $expected = "[dialogue]`r`nenabled=1`r`nfolder=$($settings.DialogueFolder)`r`ncount=1`r`n[dialogue0]`r`nquest=$quest`r`nsequence=1`r`ntype=0`r`nonce=0`r`nid=$($dialogue.Id)`r`ncontinue_screen=1`r`ncontinue_image=1`r`nresource=$resource`r`n"
    $script = [FGOLocalPlatform.ModDialogueCompiler]::ScriptFile($root, $settings, $dialogue)
    $bmp = [IO.File]::ReadAllBytes((Join-Path $root "$resource\continue.bmp"))
    $bmpShape = "$([BitConverter]::ToInt32($bmp, 18))x$([BitConverter]::ToInt32($bmp, 22)) $([BitConverter]::ToInt16($bmp, 28)) bit"
    Test-Result 'a quest trigger builds his runtime, script and continue screen' ($runtime -ceq $expected -and (Test-Path -LiteralPath $script) -and $bmpShape -ceq '1920x1080 24 bit' -and $settings.DialogueFolder -cmatch '\ADialogue/Events/shared-[0-9a-f]{32}\z') "quest $quest; $bmpShape; script $(Split-Path -Leaf $script)"
    $settings.Dialogues[0].Lines.RemoveAt(1)
    try { [FGOLocalPlatform.ModPortraits]::Remove($root, $custom, $settings.Dialogues, $null); $freed = 'deleted' } catch { $freed = "refused: $($_.Exception.InnerException.Message)" }
    Test-Result 'a portrait a line uses cannot be deleted' ($kept -ceq 'refused' -and $freed -ceq 'deleted') "in use: $kept; unused: $freed"

    # Master portraits and loading images.
    $master = [FGOLocalPlatform.ModMaster]::Load($root)
    $slot = [FGOLocalPlatform.ModMaster]::Slots[0]
    Write-Png (Join-Path $work 'wrong.png') 100 100
    Write-Png (Join-Path $work 'icon.png') $slot.Width $slot.Height
    try { [FGOLocalPlatform.ModMaster]::Import($root, $master, $slot, (Join-Path $work 'wrong.png')); $wrong = 'taken' } catch { $wrong = 'refused' }
    [FGOLocalPlatform.ModMaster]::Import($root, $master, $slot, (Join-Path $work 'icon.png'))
    $master.Enabled = $true
    [FGOLocalPlatform.ModMaster]::Build($GameRoot, $root, $master)
    $runtime = [IO.File]::ReadAllText((Join-Path $root 'Master\runtime.ini'))
    Test-Result 'Master portraits: a wrong size is refused, the right one builds [master]' ($wrong -ceq 'refused' -and $runtime.StartsWith("[master]`r`nenabled=1`r`nformat=2`r`nfolder=Master/Generated/build-") -and (Test-Path -LiteralPath (Join-Path $root "$($master.Folder)\Icons\male.png"))) ($runtime.Split("`r`n")[0..4] -join ' / ')

    $loading = [FGOLocalPlatform.ModLoading]::Load($root)
    Write-Png (Join-Path $work 'loading.png') 1280 720
    [FGOLocalPlatform.ModLoading]::Import($root, $quest, (Join-Path $work 'loading.png'))
    $entry = [FGOLocalPlatform.ModLoadingEntry]::new(); $entry.Quest = $quest; $loading.Entries.Add($entry)
    [FGOLocalPlatform.ModLoading]::Build($GameRoot, $root, $loading)
    $runtime = [IO.File]::ReadAllText((Join-Path $root 'Loading\runtime.ini'))
    Test-Result 'a loading image builds [loading]' ($runtime -cmatch "\A\[loading\]\r\ncount=1\r\nfolder=[^\r\n]+\r\n\[loading0\]\r\nquest=$quest\r\nset=\d+\r\n\z") $runtime.Replace("`r`n", ' / ')
}
catch {
    Test-Result 'run' $false ($_ | Out-String)
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

$sink = Get-DialogSink
Test-Result 'no window shown' ($sink.Shown -eq 0) $sink.Detail
if ($script:failed -gt 0) { Write-Host "$script:failed check(s) failed."; exit 1 }
Write-Host 'Every check passed.'
exit 0
