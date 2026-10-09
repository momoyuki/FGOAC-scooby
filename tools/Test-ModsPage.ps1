<#
Checks the Mods page without showing the launcher: the page and its views are built in memory, never shown, once on a
throwaway game 11.00 folder and once on a throwaway folder that has Cloud23333's 1.10 update. Every file is written
under a throwaway folder in %TEMP%, deleted when the check ends.

  pwsh -File tools\Test-ModsPage.ps1

Through the launcher assembly (src\bin\Release, so run build.cmd first):
  - on game 11.00 the page says in one sentence that mods need Cloud23333's latest update, and holds nothing else;
  - on his update it holds his MOD workbench: Packages, Music, Dialogue, Portraits, Master and Export; the notice that
    package import has a known problem stands beside the Import button; the click-to-continue options are shown;
  - without App\1_MOD\Tools\ffmpeg.exe, adding music is off and one sentence says why; with it, it is on;
  - the workbench reads its files without an error, and the loop editor and the portrait picker build from their XAML;
  - no window was shown.

Exit codes: 0 every check passed, 1 a check failed, 2 a prerequisite is missing.
#>
[CmdletBinding()]
param(
    [ValidateSet('', '1100', '1151')][string]$Build = ''
)
$ErrorActionPreference = 'Stop'

$repository = Split-Path -Parent $PSScriptRoot
$assembly = Join-Path $repository 'src\bin\Release\net6.0-windows\FGOLocalPlatform.dll'
if ($PSVersionTable.PSVersion.Major -lt 7) { Write-Host 'Run this with pwsh 7: the launcher assembly targets .NET 6.'; exit 2 }
if (!(Test-Path -LiteralPath $assembly)) { Write-Host "Missing $assembly. Run build.cmd first."; exit 2 }

# The page reads its game folder once per process, so each build runs in a process of its own, on an STA thread.
if ($Build -eq '') {
    $failed = 0
    foreach ($each in '1100', '1151') {
        $lines = @(& (Join-Path $PSHOME 'pwsh.exe') -STA -NoProfile -File $PSCommandPath -Build $each 2>&1 | ForEach-Object { "$_" })
        $lines | ForEach-Object { Write-Host $_ }
        $failed += @($lines | Where-Object { $_ -like 'FAIL *' }).Count
        if ($LASTEXITCODE -ne 0 -and @($lines | Where-Object { $_ -like 'FAIL *' }).Count -eq 0) { Write-Host "FAIL build $each run: exit $LASTEXITCODE"; $failed++ }
    }
    if ($failed -gt 0) { Write-Host "$failed check(s) failed."; exit 1 }
    Write-Host 'Every check passed.'
    exit 0
}

$script:failed = 0
function Test-Result([string]$Name, [bool]$Passed, [string]$Detail) {
    Write-Host ("{0} {1}: {2}" -f $(if ($Passed) { 'PASS' } else { 'FAIL' }), "[$Build] $Name", $Detail)
    if (!$Passed) { $script:failed++ }
}
function Write-FakeExe([string]$Path, [uint32]$TimeDateStamp, [uint32]$SizeOfImage) {
    $bytes = [byte[]]::new(0x400)
    $bytes[0] = 0x4D; $bytes[1] = 0x5A
    [BitConverter]::GetBytes([int32]0x130).CopyTo($bytes, 0x3C)
    [BitConverter]::GetBytes([uint32]0x00004550).CopyTo($bytes, 0x130)
    [BitConverter]::GetBytes($TimeDateStamp).CopyTo($bytes, 0x138)
    [BitConverter]::GetBytes($SizeOfImage).CopyTo($bytes, 0x148 + 56)
    [IO.File]::WriteAllBytes($Path, $bytes)
}
function Get-Field($Object, [string]$Name) {
    $field = $Object.GetType().GetField($Name, [Reflection.BindingFlags]'NonPublic, Public, Instance')
    if ($null -ne $field) { return $field.GetValue($Object) }
    return $null
}
function Invoke-Private($Object, [string]$Name) {
    $method = $Object.GetType().GetMethod($Name, [Reflection.BindingFlags]'NonPublic, Public, Instance')
    if (!$method) { throw "$($Object.GetType().Name).$Name is missing" }
    return $method.Invoke($Object, @())
}
function Find-Logical($Root, [scriptblock]$Match) {
    $found = @()
    foreach ($child in [Windows.LogicalTreeHelper]::GetChildren($Root)) {
        if ($child -is [Windows.DependencyObject]) {
            if (& $Match $child) { $found += $child }
            $found += @(Find-Logical $child $Match)
        }
    }
    return $found
}

$work = Join-Path ([IO.Path]::GetTempPath()) ('fgoac-modspage-' + [guid]::NewGuid().ToString('N'))
$app = Join-Path $work 'App'
$root = Join-Path $app '1_MOD'
try {
    New-Item -ItemType Directory -Path (Join-Path $app 'rom\single'), $root -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $app 'rom\single\quest_prop_000001.txt'), "quest_id=1`r`nquest_name=Fuyuki`r`nbattle.1.wave_id=10`r`n")
    if ($Build -eq '1100') { Write-FakeExe (Join-Path $app 'ago.exe') 0x643F4ECD 0x2034000 }
    else {
        Write-FakeExe (Join-Path $app 'ago.exe') 0x65C5AF1E 0x2039000
        [IO.File]::WriteAllText((Join-Path $work 'update-1.10.json'), '{"version":"1.10"}')
    }
    [AppContext]::SetData('APP_CONTEXT_BASE_DIRECTORY', $app + '\')
    Add-Type -AssemblyName PresentationFramework
    $launcher = [Reflection.Assembly]::LoadFrom($assembly)
    . (Join-Path $PSScriptRoot 'DialogSink.ps1'); Enable-DialogSink $assembly
    $application = [Activator]::CreateInstance($launcher.GetType('FGOLocalPlatform.App'))
    $application.InitializeComponent()

    $page = [Activator]::CreateInstance($launcher.GetType('FGOLocalPlatform.ModsPage'))
    $status = Get-Field $page 'StatusText'
    $bench = (Get-Field $page 'Workbench').Content
    if ($Build -eq '1100') {
        Test-Result 'on game 11.00 the page says it needs his latest update' ($status.Text -ceq "Mods need Cloud23333's latest update, which this game folder does not have yet." -and $status.Visibility -eq [Windows.Visibility]::Visible -and $null -eq $bench) $status.Text
    }
    else {
        $tabs = Get-Field $bench 'Tabs'
        $headers = @($tabs.Items | ForEach-Object { [string]$_.Header }) -join ','
        Test-Result 'on his update the page holds his workbench' ($status.Visibility -eq [Windows.Visibility]::Collapsed -and $bench.GetType().Name -ceq 'ModSettingsView' -and $headers -ceq 'Packages,Music,Dialogue,Portraits,Master,Export') $headers

        $installer = Get-Field $bench 'Installer'
        $notice = Get-Field $installer 'ImportNotice'
        $button = Get-Field $installer 'ImportButton'
        $beside = [object]::ReferenceEquals([Windows.LogicalTreeHelper]::GetParent($notice), [Windows.LogicalTreeHelper]::GetParent($button))
        Test-Result 'his import notice stands beside the Import button' ($notice.Text -ceq 'Package import has a known problem, and Cloud23333 advises against using it.' -and $beside -and $notice.Visibility -eq [Windows.Visibility]::Visible) $notice.Text

        # The quest search box says what it is while it is empty.
        $search = Get-Field $bench 'QuestSearch'; $hint = Get-Field $bench 'QuestSearchHint'
        $emptyShown = $null -ne $hint -and $hint.Visibility -eq [Windows.Visibility]::Visible
        $search.Text = 'Fuyuki'; $typedShown = $null -ne $hint -and $hint.Visibility -eq [Windows.Visibility]::Visible
        $search.Text = ''; $clearedShown = $null -ne $hint -and $hint.Visibility -eq [Windows.Visibility]::Visible
        Test-Result 'the quest search box shows its hint only while empty' ($emptyShown -and -not $typedShown -and $clearedShown -and $hint.Text -ceq 'Search') "empty $emptyShown, typed $typedShown, cleared $clearedShown, '$($hint.Text)'"
        $folder = Get-Field $bench 'LibraryPath'; $folderHint = Get-Field $bench 'LibraryPathHint'
        $folder.Text = ''; $emptyShown = $null -ne $folderHint -and $folderHint.Visibility -eq [Windows.Visibility]::Visible
        $folder.Text = 'D:\portraits'; $typedShown = $null -ne $folderHint -and $folderHint.Visibility -eq [Windows.Visibility]::Visible
        $folder.Text = ''
        Test-Result 'the portrait folder box says an empty box uses App\1_MOD' ($emptyShown -and -not $typedShown -and $folderHint.Text -ceq 'Empty uses App\1_MOD') "empty $emptyShown, typed $typedShown, '$($folderHint.Text)'"

        $expander = @(Find-Logical $bench { param($node) $node -is [Windows.Controls.Expander] })
        $continue = Get-Field $bench 'ContinueScreen'
        Test-Result 'the click-to-continue options are shown' ($expander.Count -eq 1 -and $expander[0].Visibility -eq [Windows.Visibility]::Visible -and $continue.Content -ceq 'Tap to continue') "$($expander.Count) expander(s)"

        [void](Invoke-Private $bench 'Load')
        $note = Get-Field $bench 'ConverterNote'; $add = Get-Field $bench 'ImportMusicButton'
        $missing = "note $($note.Visibility) '$($note.Text)', add $($add.IsEnabled)"
        Test-Result 'without ffmpeg.exe adding music is off and one sentence says why' ($note.Visibility -eq [Windows.Visibility]::Visible -and $note.Text -ceq "Adding music needs App\1_MOD\Tools\ffmpeg.exe, which Cloud23333's latest update installs." -and !$add.IsEnabled) $missing
        New-Item -ItemType Directory -Path (Join-Path $root 'Tools') -Force | Out-Null
        [IO.File]::WriteAllBytes((Join-Path $root 'Tools\ffmpeg.exe'), [byte[]](1..16))
        [void](Invoke-Private $bench 'UpdateConverterNote')
        Test-Result 'with ffmpeg.exe adding music is on' ($note.Visibility -eq [Windows.Visibility]::Collapsed -and $add.IsEnabled) "note $($note.Visibility), add $($add.IsEnabled)"

        $quests = @((Get-Field $bench 'Quest').ItemsSource)
        $pools = @((Get-Field $bench 'Pool').ItemsSource)
        Test-Result 'the workbench reads its files without an error' ($bench.IsEnabled -and $quests.Count -eq 1 -and $quests[0].Name -ceq 'Fuyuki' -and $pools.Count -eq 4 -and (Get-Field $bench 'Status').Text -eq '') "enabled $($bench.IsEnabled); quests $($quests.Count); pools $($pools.Count); status '$((Get-Field $bench 'Status').Text)'"

        $track = [FGOLocalPlatform.ModTrack]::new()
        $editor = $launcher.GetType('FGOLocalPlatform.ModLoopEditor').GetConstructors()[0].Invoke([object[]]@([string]$root, $track, [Func[double, double, Threading.Tasks.Task]] { param($a, $b) [Threading.Tasks.Task]::CompletedTask }))
        $picker = $launcher.GetType('FGOLocalPlatform.ModPortraitPicker').GetConstructors()[0].Invoke([object[]]@([Action[FGOLocalPlatform.ModPortraitReference]] { param($p) }))
        Test-Result 'the loop editor and the portrait picker build from their XAML' ($editor.Title -ceq 'Waveform and loop' -and $picker.Title -ceq 'Portrait of the line' -and !$editor.IsVisible -and !$picker.IsVisible) "$($editor.Title); $($picker.Title)"
    }
}
catch {
    Test-Result 'run' $false ($_ | Out-String)
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

$sink = Get-DialogSink
Test-Result 'no window shown' ($sink.Shown -eq 0) $sink.Detail
if ($script:failed -gt 0) { exit 1 }
exit 0
