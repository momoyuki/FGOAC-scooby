# Dot-sourced by the Test-*.ps1 suites: routes every message box, dialog, window and shell open of the launcher
# assembly to its in-memory sink (FGOLocalPlatform.DialogSink), so a suite never puts a window on the screen.

function Enable-DialogSink([string]$Assembly) {
    $script:DialogSinkType = [Reflection.Assembly]::LoadFrom($Assembly).GetType('FGOLocalPlatform.DialogSink', $true)
    [void]$script:DialogSinkType.GetMethod('Capture').Invoke($null, @())
}

# Windows shown since Enable-DialogSink, and what the sink took instead of showing.
function Get-DialogSink {
    if ($null -eq $script:DialogSinkType) { return [pscustomobject]@{ Shown = -1; Captured = @(); Detail = 'the sink was never turned on' } }
    $shown = $script:DialogSinkType.GetMethod('ShownWindows').Invoke($null, @())
    $captured = @($script:DialogSinkType.GetProperty('Captured').GetValue($null))
    return [pscustomobject]@{ Shown = $shown; Captured = $captured; Detail = "$shown shown; captured $($captured.Count): $($captured -join ' | ')" }
}
