<#
Checks that the test suites can never put a launcher window on the screen.

  pwsh -File tools\Test-DialogSink.ps1

  - the source: no message box, dialog, window or shell open outside DialogSink.cs and ThemedMessageBox.Show, which
    goes through it, so the one switch every suite sets covers them all;
  - through the launcher assembly (src\bin\Release, so run build.cmd first), with the sink on: a message box is
    captured as "caption: text" and answers as a box closed without a button, without a Window being created; a
    dialog is captured by its title and answers nothing; a Window's Loaded event counts as a shown window.

Exit codes: 0 every check passed, 1 a check failed, 2 a prerequisite is missing.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'

$repository = Split-Path -Parent $PSScriptRoot
$assembly = Join-Path $repository 'src\bin\Release\net6.0-windows\FGOLocalPlatform.dll'
if ($PSVersionTable.PSVersion.Major -lt 7) { Write-Host 'Run this with pwsh 7: the launcher assembly targets .NET 6.'; exit 2 }
if (!(Test-Path -LiteralPath $assembly)) { Write-Host "Missing $assembly. Run build.cmd first."; exit 2 }

# WPF windows need an STA thread.
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    & (Join-Path $PSHOME 'pwsh.exe') -STA -NoProfile -File $PSCommandPath
    exit $LASTEXITCODE
}

$script:failed = 0
function Test-Result([string]$Name, [bool]$Passed, [string]$Detail) {
    Write-Host ("{0} {1}: {2}" -f $(if ($Passed) { 'PASS' } else { 'FAIL' }), $Name, $Detail)
    if (!$Passed) { $script:failed++ }
}

# A window reaches the screen through ShowDialog, Show, MessageBox.Show or a shell open; only the sink may call them.
# The updater's file swap is a hidden batch file, not a window.
$direct = foreach ($file in Get-ChildItem -LiteralPath (Join-Path $repository 'src') -Recurse -Filter '*.cs') {
    if ($file.Name -ceq 'DialogSink.cs' -or $file.FullName -match '\\(bin|obj)\\') { continue }
    $number = 0
    foreach ($line in [IO.File]::ReadAllLines($file.FullName)) {
        $number++
        $shell = $line.Contains('UseShellExecute = true') -and $file.Name -cne 'Updater.cs'
        if ($line -match '(?<!DialogSink)\.ShowDialog\(|\.Show\(\)|(?<!Themed)MessageBox\.Show\(' -or $shell) {
            "$($file.Name):$number $($line.Trim())"
        }
    }
}
Test-Result 'every window goes through the sink' (@($direct).Count -eq 0) "$(@($direct).Count) direct: $(@($direct) -join ' | ')"
# A direct call would put its window on the screen in the checks below.
if ($script:failed -gt 0) { Write-Host "$script:failed check(s) failed."; exit 1 }

$work = Join-Path ([IO.Path]::GetTempPath()) ('fgoac-dialogsink-' + [guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    [AppContext]::SetData('APP_CONTEXT_BASE_DIRECTORY', $work + '\')
    Add-Type -AssemblyName PresentationFramework
    $launcher = [Reflection.Assembly]::LoadFrom($assembly)
    $application = [Activator]::CreateInstance($launcher.GetType('FGOLocalPlatform.App'))
    $application.InitializeComponent()
    . (Join-Path $PSScriptRoot 'DialogSink.ps1')
    Enable-DialogSink $assembly

    # Building the App queues its start on the dispatcher; the first pump would run it and open the launcher.
    $before = $application.Windows.Count
    [void](Get-DialogSink)
    $sink = Get-DialogSink
    Test-Result 'the App built for its resources does not start the launcher' ($application.Windows.Count -eq 0 -and $sink.Captured.Count -eq 0 -and $sink.Shown -eq 0) "windows $before -> $($application.Windows.Count); $($sink.Detail)"

    $box = $launcher.GetType('FGOLocalPlatform.ThemedMessageBox')
    $show = $box.GetMethod('Show', [Type[]]@([string], [string], [Windows.MessageBoxButton], [Windows.MessageBoxImage], [Windows.MessageBoxResult], [bool]))
    $before = $application.Windows.Count
    $ok = $show.Invoke($null, [object[]]@('Something failed.', 'FGOAC scooby - Error', [Windows.MessageBoxButton]::OK, [Windows.MessageBoxImage]::Hand, [Windows.MessageBoxResult]::None, $false))
    $question = $show.Invoke($null, [object[]]@('Delete it?', 'Mods', [Windows.MessageBoxButton]::YesNo, [Windows.MessageBoxImage]::Question, [Windows.MessageBoxResult]::Yes, $false))
    $sink = Get-DialogSink
    $seen = "answers $ok, $question; windows $before -> $($application.Windows.Count); $($sink.Detail)"
    Test-Result 'a message box is captured, creating no window' ($sink.Captured[0] -ceq 'FGOAC scooby - Error: Something failed.' -and $sink.Captured[1] -ceq 'Mods: Delete it?' -and $ok -eq [Windows.MessageBoxResult]::OK -and $question -eq [Windows.MessageBoxResult]::No -and $application.Windows.Count -eq $before -and $sink.Shown -eq 0) $seen

    $dialog = $launcher.GetType('FGOLocalPlatform.WhatsNewDialog').GetConstructors()[0].Invoke([object[]]@($null, '1.3.0', 'Notes'))
    $dialog.Title = 'Updated'
    $answer = $launcher.GetType('FGOLocalPlatform.DialogSink').GetMethod('ShowDialog', [Type[]]@([Windows.Window])).Invoke($null, [object[]]@($dialog))
    $sink = Get-DialogSink
    Test-Result 'a dialog is captured by its title and answers nothing' ($null -eq $answer -and $sink.Captured[-1] -ceq 'dialog: Updated' -and !$dialog.IsVisible -and $sink.Shown -eq 0) "answer '$answer', visible $($dialog.IsVisible); $($sink.Detail)"

    # Loaded is what a window raises once shown; raised by hand here, so nothing is shown.
    $dialog.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.FrameworkElement]::LoadedEvent))
    Test-Result 'a shown window is counted' ((Get-DialogSink).Shown -eq 1) (Get-DialogSink).Detail
}
catch {
    Test-Result 'run' $false ($_ | Out-String)
}
finally {
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}

if ($script:failed -gt 0) { Write-Host "$script:failed check(s) failed."; exit 1 }
Write-Host 'Every check passed.'
exit 0
