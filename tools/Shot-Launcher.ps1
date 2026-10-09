<#
Opens a launcher build, waits for its window, walks its pages through UI Automation and saves one PNG per page.

  pwsh -File tools\Shot-Launcher.ps1 -Exe devroot\FGOLocalPlatform.exe -OutDir shots -Width 1280 -Height 800

The build must run without elevation (build.cmd with -p:ShellDev=true). Pages are the TabItem and ListBoxItem
names the window exposes; -Pages limits the walk. The window is resized to -Width x -Height before the first shot.
Prints the time to the first window, the time each page took to become idle, and the idle CPU sample.

Exit codes: 0 done, 1 the window never appeared.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Exe,
    [string]$OutDir = 'shots',
    [int]$Width = 1280,
    [int]$Height = 800,
    [string[]]$Pages,
    [int]$SettleMs = 700
)
$ErrorActionPreference = 'Continue'
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing
New-Item -ItemType Directory -Force $OutDir | Out-Null
Add-Type @'
using System; using System.Runtime.InteropServices;
public static class ShotNative {
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [DllImport("user32.dll")] public static extern bool MoveWindow(IntPtr h, int x, int y, int w, int ht, bool repaint);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern int GetWindowTextLength(IntPtr h);
  [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
  [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint msg, IntPtr w, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
  public static string TitleOf(IntPtr h) { var sb = new System.Text.StringBuilder(256); GetWindowText(h, sb, 256); return sb.ToString(); }
  // The first visible, titled, owned window of the process: a dialog or message box.
  public static IntPtr DialogOf(int pid) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid && IsWindowVisible(h) && GetWindowTextLength(h) > 0 && GetWindow(h, 4) != IntPtr.Zero) { found = h; return false; }
      return true;
    }, IntPtr.Zero);
    return found;
  }
  // The first visible, titled, unowned top-level window of the process.
  public static IntPtr MainWindowOf(int pid) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h, l) => {
      uint p; GetWindowThreadProcessId(h, out p);
      if (p == pid && IsWindowVisible(h) && GetWindowTextLength(h) > 0 && GetWindow(h, 4) == IntPtr.Zero) { found = h; return false; }
      return true;
    }, IntPtr.Zero);
    return found;
  }
}
'@

function Shot([IntPtr]$h, [string]$name) {
    $r = New-Object ShotNative+RECT
    [ShotNative]::GetWindowRect($h, [ref]$r) | Out-Null
    $w = $r.R - $r.L; $ht = $r.B - $r.T
    if ($w -le 0 -or $ht -le 0) { Write-Host "shot $name skipped: empty rect"; return }
    $bmp = New-Object System.Drawing.Bitmap $w, $ht
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $dc = $g.GetHdc()
    $ok = [ShotNative]::PrintWindow($h, $dc, 2)
    $g.ReleaseHdc($dc)
    if (-not $ok) { $g.CopyFromScreen($r.L, $r.T, 0, 0, $bmp.Size) }
    $g.Dispose()
    $path = Join-Path $OutDir "$name.png"
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    Write-Host "shot $name ($w x $ht)"
}
function Items($root) {
    $tab = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::TabItem)
    $item = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.OrCondition($tab, $item)))
}
function Safe([string]$s) { ($s -replace '[^\w-]+', '_').Trim('_') }

$sw = [Diagnostics.Stopwatch]::StartNew()
$proc = Start-Process -FilePath $Exe -WorkingDirectory (Split-Path -Parent $Exe) -PassThru
$h = [IntPtr]::Zero
while ($sw.ElapsedMilliseconds -lt 60000) {
    $proc.Refresh()
    if ($proc.HasExited) { break }
    $h = [ShotNative]::MainWindowOf($proc.Id)
    if ($h -ne [IntPtr]::Zero) { break }
    $d = [ShotNative]::DialogOf($proc.Id)
    if ($d -ne [IntPtr]::Zero) {
        $title = [ShotNative]::TitleOf($d)
        Write-Host ("dialog before the window after {0} ms: '{1}'" -f $sw.ElapsedMilliseconds, $title)
        Start-Sleep -Milliseconds 300
        Shot $d ("00-dialog-" + (Safe $title))
        [ShotNative]::PostMessage($d, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
        Start-Sleep -Milliseconds 500
    }
    Start-Sleep -Milliseconds 25
}
Write-Host ("first window after {0} ms" -f $sw.ElapsedMilliseconds)
if ($h -eq [IntPtr]::Zero) { Write-Host 'no window'; if (-not $proc.HasExited) { $proc | Stop-Process -Force }; exit 1 }
$proc.WaitForInputIdle(30000) | Out-Null
[ShotNative]::ShowWindow($h, 9) | Out-Null
[ShotNative]::MoveWindow($h, 100, 60, $Width, $Height, $true) | Out-Null
Start-Sleep -Milliseconds ($SettleMs * 2)
Shot $h '00-start'

$root = [System.Windows.Automation.AutomationElement]::FromHandle($h)
$seen = @{}
$i = 0
foreach ($e in @(Items $root)) {
    $name = $e.Current.Name
    if (-not $name -or $seen.ContainsKey($name)) { continue }
    if ($Pages -and $Pages -notcontains $name) { continue }
    $seen[$name] = 1
    $p = $null
    if (-not $e.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$p)) { continue }
    $t = [Diagnostics.Stopwatch]::StartNew()
    $p.Select()
    $proc.WaitForInputIdle(5000) | Out-Null
    $ms = $t.ElapsedMilliseconds
    Start-Sleep -Milliseconds $SettleMs
    $i++
    Write-Host ("page {0} '{1}' idle after {2} ms" -f $i, $name, $ms)
    Shot $h ("{0:d2}-{1}" -f $i, (Safe $name))
}
$proc.Refresh()
$cpu0 = $proc.TotalProcessorTime; $t0 = Get-Date
Start-Sleep -Seconds 5
$proc.Refresh()
$pct = [math]::Round(100 * ($proc.TotalProcessorTime - $cpu0).TotalSeconds / ((Get-Date) - $t0).TotalSeconds / [Environment]::ProcessorCount, 2)
Write-Host ("idle 5 s: cpu {0}%, working set {1} MB" -f $pct, [math]::Round($proc.WorkingSet64 / 1MB))
$proc.CloseMainWindow() | Out-Null
if (-not $proc.WaitForExit(15000)) { Write-Host 'did not close in 15 s; killed'; $proc | Stop-Process -Force }
exit 0
