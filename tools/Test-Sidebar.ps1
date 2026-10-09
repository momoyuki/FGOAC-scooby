<#
Checks the sidebar and the pages it opens, without showing the launcher: the main window is built in memory, never
shown, once on a throwaway game 11.00 folder and once on a throwaway folder that has Cloud23333's 1.10 update. Every
file is written under a throwaway folder in %TEMP%, deleted when the check ends.

  pwsh -File tools\Test-Sidebar.ps1 [-TrcDb <install>\App\trcdb.json]

Through the launcher assembly (src\bin\Release, so run build.cmd first):
  - the sidebar shows Play, Cards, Photo Mode, Mods, Events, Accounts, Battle Debug, Controls, Settings, About, in that order;
  - each page that is not Play, Cards, Settings or About is a page control whose XAML is one file in its feature folder;
  - Controls is a page of its own holding the Input, Bindings and Cursor cards;
  - Settings holds only settings: Display, Audio, PC play, Battle effects, Local server, Diagnostics and Help, and its
    filter finds none of the moved cards;
  - Controls has rows for Skill Target 1 to 3 and Cancel, which never share a key or button with each other;
  - Controls shows Cloud23333's keyboard battle mode on his build only, saved under his keys with his conflict rules;
  - Controls holds the cursor lock, saved as cursor.confine;
  - Controls shows his controller card selection, controller camera with lock-on and controller mouse on his build only,
    saved under his [xinput] keys, the mouse toggle and click never one button;
  - a launcher window off its monitor's work area is moved back onto it, and shrunk to fit;
  - a settings file Cloud23333's launcher also writes, changed while the window was in the background, is read again
    when it comes back, and a save keeps the change;
  - Display shows and saves Cloud23333's 240 FPS, high frame rate timing, VSync and AMD anti-tearing on his build only;
  - Battle Debug shows Cloud23333's auto clear, cost 233 and PvP map pick on his build only;
  - Battle Debug holds the switches Play guards for an Online session, Accounts is the page the Master line's Manage
    opens, and Events holds the Banners patch on game 11.00 and his event switches on his build, where a tick off is
    saved at once into Server\state\activity-display.json. -CloudCatalog is his activity_display_catalog.json.
-TrcDb is copied into the throwaway App folder so the card scan in the window's constructor finds its catalogue.

Exit codes: 0 every check passed, 1 a check failed, 2 a prerequisite is missing.
#>
[CmdletBinding()]
param(
    [string]$TrcDb = 'D:\FGOA\App\trcdb.json',
    [string]$CloudCatalog = "$env:FGOAC_CLOUD_UPDATE\payload\payload\Server\artemis\titles\fgo\activity_display_catalog.json",
    [ValidateSet('', '1100', '1151')][string]$Build = ''
)
$ErrorActionPreference = 'Stop'

$repository = Split-Path -Parent $PSScriptRoot
$assembly = Join-Path $repository 'src\bin\Release\net6.0-windows\FGOLocalPlatform.dll'
if ($PSVersionTable.PSVersion.Major -lt 7) { Write-Host 'Run this with pwsh 7: the launcher assembly targets .NET 6.'; exit 2 }
if (!(Test-Path -LiteralPath $assembly)) { Write-Host "Missing $assembly. Run build.cmd first."; exit 2 }
# Every message box, dialog and window of the launcher goes to the sink, never to the screen.
. (Join-Path $PSScriptRoot 'DialogSink.ps1'); Enable-DialogSink $assembly
if (!(Test-Path -LiteralPath $TrcDb)) { Write-Host "Missing $TrcDb."; exit 2 }

# The window's base folder can be set once per process, so each build runs in a process of its own, on an STA thread.
if ($Build -eq '') {
    $failed = 0
    foreach ($each in '1100', '1151') {
        $lines = @(& (Join-Path $PSHOME 'pwsh.exe') -STA -NoProfile -File $PSCommandPath -TrcDb $TrcDb -CloudCatalog $CloudCatalog -Build $each 2>&1 | ForEach-Object { "$_" })
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
    $property = $Object.GetType().GetProperty($Name, [Reflection.BindingFlags]'NonPublic, Public, Instance')
    if ($null -ne $property) { return $property.GetValue($Object) }
    return $null
}
function Test-Inside($Child, $Parent) {
    $node = $Child
    while ($null -ne $node) {
        if ([object]::ReferenceEquals($node, $Parent)) { return $true }
        $node = [Windows.LogicalTreeHelper]::GetParent($node)
    }
    return $false
}

$work = Join-Path ([IO.Path]::GetTempPath()) ('fgoac-sidebar-' + [guid]::NewGuid().ToString('N'))
$app = Join-Path $work 'App'
try {
    New-Item -ItemType Directory -Path $app, (Join-Path $work 'Server') -Force | Out-Null
    Copy-Item -LiteralPath $TrcDb -Destination (Join-Path $app 'trcdb.json')
    [IO.File]::WriteAllText((Join-Path $app 'fgo-launcher.json'), '{ "serverHost": "auto" }')
    if ($Build -eq '1100') { Write-FakeExe (Join-Path $app 'ago.exe') 0x643F4ECD 0x2034000 }
    else {
        Write-FakeExe (Join-Path $app 'ago.exe') 0x65C5AF1E 0x2039000
        [IO.File]::WriteAllText((Join-Path $work 'update-1.10.json'), '{"version":"1.10"}')
        New-Item -ItemType Directory -Path (Join-Path $work 'Server\tools') -Force | Out-Null
        [IO.File]::WriteAllText((Join-Path $work 'Server\tools\fgo_account_transfer.py'), '# his transfer tool')
        New-Item -ItemType Directory -Path (Join-Path $work 'Server\artemis\titles\fgo') -Force | Out-Null
        Copy-Item -LiteralPath $CloudCatalog -Destination (Join-Path $work 'Server\artemis\titles\fgo')
    }
    [AppContext]::SetData('APP_CONTEXT_BASE_DIRECTORY', $app + '\')
    [IO.Directory]::SetCurrentDirectory($app)
    Add-Type -AssemblyName PresentationFramework
    $launcher = [Reflection.Assembly]::LoadFrom($assembly)
    $application = [Activator]::CreateInstance($launcher.GetType('FGOLocalPlatform.App'))
    $application.InitializeComponent()
    $window = [Activator]::CreateInstance($launcher.GetType('FGOLocalPlatform.MainWindow'))

    $tabs = Get-Field $window 'SectionTabs'
    $shown = @($tabs.Items | Where-Object { $_.Visibility -eq [Windows.Visibility]::Visible } | ForEach-Object { [string]$_.Header })
    Test-Result 'sidebar order' (($shown -join ',') -ceq 'Play,Cards,Photo Mode,Mods,Events,Accounts,Battle Debug,Controls,Settings,About') ($shown -join ', ')

    # The Master line says the accounts are being read until the list has been read once.
    $masterLine = $window.GetType().GetMethod('ShowMasterLine', [Reflection.BindingFlags]'NonPublic, Instance')
    $masterText = Get-Field $window 'MasterLineText'
    $readField = $window.GetType().GetField('accountsRead', [Reflection.BindingFlags]'NonPublic, Instance')
    if (!$masterLine -or !$masterText -or !$readField) {
        Test-Result 'Master line before and after the accounts are read' $false 'ShowMasterLine, MasterLineText or accountsRead is missing'
    } else {
        $masterLine.Invoke($window, @())
        $before = [Windows.Documents.TextRange]::new($masterText.ContentStart, $masterText.ContentEnd).Text
        $readField.SetValue($window, $true)
        $masterLine.Invoke($window, @())
        $after = [Windows.Documents.TextRange]::new($masterText.ContentStart, $masterText.ContentEnd).Text
        Test-Result 'Master line before and after the accounts are read' ($before -ceq 'Reading accounts...' -and $after -ceq 'No Master selected') "before '$before', after '$after'"
    }

    $pages = [ordered]@{ 'Photo Mode' = 'PhotoMode\photomodepage.xaml'; 'Mods' = 'Mods\modspage.xaml'; 'Events' = 'Events\eventspage.xaml'; 'Accounts' = 'Account\accountspage.xaml'; 'Battle Debug' = 'BattleDebug\battledebugpage.xaml' }
    foreach ($header in $pages.Keys) {
        $tab = @($tabs.Items | Where-Object { [string]$_.Header -ceq $header })[0]
        $content = if ($tab) { $tab.Content } else { $null }
        $file = Join-Path $repository ('src\FGOLocalPlatform\' + $pages[$header])
        $named = if ($content) { $content.GetType().Name } else { 'none' }
        $xaml = if (Test-Path -LiteralPath $file) { [IO.File]::ReadAllText($file) } else { '' }
        Test-Result "$header is one page file in its folder" ($content -is [Windows.Controls.UserControl] -and $xaml.Contains('x:Class="FGOLocalPlatform.' + $named + '"')) "page $named, file $($pages[$header]): $(Test-Path -LiteralPath $file)"
    }

    $sections = Get-Field $window 'SettingsSections'
    $titles = @($sections.Children | Where-Object { $_ -is [Windows.Controls.Border] } | ForEach-Object { [string]$_.Tag })
    Test-Result 'Settings holds only settings' (($titles -join ',') -ceq 'Display,Audio,PC play,Battle effects,Local server,Diagnostics and Help') ($titles -join ', ')
    $filter = Get-Field $window 'SettingsFilterBox'
    $controlsCards = @((Get-Field $window 'ControlsSections').Children | Where-Object { $_ -is [Windows.Controls.Border] } | ForEach-Object { [string]$_.Tag })
    $controlsTab = @($tabs.Items | Where-Object { [string]$_.Header -ceq 'Controls' })[0]
    Test-Result 'Controls is its own page with the Input, Bindings and Cursor cards' ($null -ne $controlsTab -and ($controlsCards -join ',') -ceq 'Input,Bindings,Cursor' -and (Test-Inside (Get-Field $window 'ControlsPanel') $controlsTab) -and (Test-Inside (Get-Field $window 'CursorPanel') $controlsTab)) ($controlsCards -join ', ')
    # The pointer preview, outline and hotspot act on a custom image only: shown in that mode alone; Size always.
    $cursor = Get-Field $window 'CursorPanel'
    $mode = Get-Field $cursor 'Mode'
    $parts = 'PreviewBorder', 'OutlinePanel', 'HotspotPanel', 'SizeSlider'
    $shownAs = foreach ($index in 0, 2) {
        $mode.SelectedIndex = $index
        "$index=" + (@($parts | Where-Object { $p = Get-Field $cursor $_; $null -ne $p -and $p.Visibility -eq [Windows.Visibility]::Visible }) -join '+')
    }
    $mode.SelectedIndex = 0
    Test-Result 'pointer image controls shown for a custom image only' (($shownAs -join ' ') -ceq '0=SizeSlider 2=PreviewBorder+OutlinePanel+HotspotPanel+SizeSlider') ($shownAs -join ' ')
    # Every drop-down, slider and text box on the pages has a name a screen reader can say.
    $unnamed = [Collections.Generic.List[string]]::new()
    $queue = [Collections.Generic.Queue[object]]::new()
    foreach ($tab in $tabs.Items) { $queue.Enqueue($tab.Content) }
    while ($queue.Count -gt 0) {
        $node = $queue.Dequeue()
        if (($node -is [Windows.Controls.ComboBox] -or $node -is [Windows.Controls.Slider] -or $node -is [Windows.Controls.TextBox]) -and
            [string]::IsNullOrEmpty([Windows.Automation.AutomationProperties]::GetName($node)) -and $null -eq [Windows.Automation.AutomationProperties]::GetLabeledBy($node)) {
            $owner = $node; do { $owner = [Windows.LogicalTreeHelper]::GetParent($owner) } until ($null -eq $owner -or $owner.GetType().Namespace -eq 'FGOLocalPlatform')
            $unnamed.Add("$($node.GetType().Name) $($node.Name) in $(if ($owner) { $owner.GetType().Name })")
        }
        if ($node -is [Windows.DependencyObject]) { foreach ($child in [Windows.LogicalTreeHelper]::GetChildren($node)) { $queue.Enqueue($child) } }
    }
    Test-Result 'every drop-down, slider and text box has a name' ($unnamed.Count -eq 0) ($unnamed -join ', ')
    # Cards > Deck scrolls as a page when the window is too short for the library header, a row of cards and the deck.
    $deckTab = @((Get-Field $window 'CardsTab').Content.Items | Where-Object { [string]$_.Header -ceq 'Deck' })[0]
    $deckScroll = if ($deckTab) { $deckTab.Content } else { $null }
    $deckFrame = if ($deckScroll -is [Windows.Controls.ScrollViewer]) { $deckScroll.Content } else { $null }
    # The frame takes the viewport's height (a binding, never an open height), so the card list stays virtualized.
    $bound = if ($deckFrame -is [Windows.Controls.Border]) { [Windows.Data.BindingOperations]::GetBinding($deckFrame, [Windows.FrameworkElement]::HeightProperty) } else { $null }
    $sizeHandlers = if ($deckFrame) { @($deckFrame.GetType().GetProperty('EventHandlersStore', [Reflection.BindingFlags]'NonPublic, Instance').GetValue($deckFrame).GetRoutedEventHandlers([Windows.FrameworkElement]::SizeChangedEvent)).Count } else { 0 }
    Test-Result 'the Deck tab scrolls instead of squeezing the library' ($deckFrame -is [Windows.Controls.Border] -and $sizeHandlers -gt 0 -and $null -ne $bound -and $bound.Path.Path -ceq 'ViewportHeight' -and $deckFrame.Child -is [Windows.Controls.Grid]) "content $($deckScroll.GetType().Name) > $($deckFrame.GetType().Name), size handlers $sizeHandlers, height bound to $($bound.Path.Path)"
    # Bindings lists only the buttons that have a place on the screen: a row without one cannot be bound.
    $bindingsCard = @((Get-Field $window 'ControlsSections').Children | Where-Object { [string]$_.Tag -ceq 'Bindings' })[0]
    $bindingsView = @([Windows.LogicalTreeHelper]::GetChildren($bindingsCard.Child) | Where-Object { $_.GetType().Name -eq 'BindingsSettingsView' })[0]
    $labels = @((Get-Field $bindingsView 'BindingRows').Children | Where-Object { $_ -is [Windows.Controls.TextBlock] -and [Windows.Controls.Grid]::GetColumn($_) -eq 0 -and [Windows.Controls.Grid]::GetRow($_) -gt 0 } | ForEach-Object { $_.Text })
    Test-Result 'Bindings lists only placed buttons' (($labels -join ',') -ceq 'Title screen: touch to start,Card check: Yes,GP purchase: 300 GP,Popup: Close,Terminal: Grail War,Grail War: Nationwide Match,Grail War: Start') ($labels -join ', ')
    $found = foreach ($word in 'Debug', 'Photo', 'Banners', 'Account', 'Input', 'Bindings', 'Cursor') {
        $filter.Text = $word
        "$word=$(@($sections.Children | Where-Object { $_ -is [Windows.Controls.Border] -and $_.Visibility -eq [Windows.Visibility]::Visible }).Count)"
    }
    $filter.Text = ''
    Test-Result 'the Settings filter finds no moved card' (($found -join ',') -ceq 'Debug=0,Photo=0,Banners=0,Account=0,Input=0,Bindings=0,Cursor=0') ($found -join ', ')

    $audioCard = @($sections.Children | Where-Object { $_ -is [Windows.Controls.Border] -and [string]$_.Tag -ceq 'Audio' })[0]
    $audio = if ($audioCard) { @([Windows.LogicalTreeHelper]::GetChildren($audioCard.Child) | Where-Object { $_.GetType().Name -eq 'AudioSettingsView' })[0] } else { $null }
    $output = if ($audio) { Get-Field $audio 'OutputChannelsRow' } else { $null }
    $outputShown = $null -ne $output -and $output.Visibility -eq [Windows.Visibility]::Visible
    Test-Result 'Audio output channels on his build only' ($null -ne $output -and $outputShown -eq ($Build -eq '1151')) "row shown: $outputShown"
    # The volume sliders save themselves, so Audio has no Apply button.
    $audioButtons = [Collections.Generic.List[string]]::new()
    $queue = [Collections.Generic.Queue[object]]::new(); $queue.Enqueue($audio)
    while ($queue.Count -gt 0) {
        $node = $queue.Dequeue()
        if ($node -is [Windows.Controls.Button]) { $audioButtons.Add([string]$node.Content) }
        if ($node -is [Windows.DependencyObject]) { foreach ($child in [Windows.LogicalTreeHelper]::GetChildren($node)) { $queue.Enqueue($child) } }
    }
    Test-Result 'Audio has no Apply button' (($audioButtons -join ',') -ceq 'Mute All,Restore Defaults') ($audioButtons -join ', ')

    # Cloud23333's scene settings on Display: shown and saved on his build only.
    $scene = Get-Field $window 'SceneGraphicsPanel'
    $sceneShown = $null -ne $scene -and $scene.Visibility -eq [Windows.Visibility]::Visible
    $width = Get-Field $window 'CustomWidthTextBox'
    if ($null -ne $scene -and $width.Text -match '^\d+$') {
        (Get-Field $window 'VolumeFogCheckBox').IsChecked = $false
        $distance = Get-Field $window 'DrawDistanceComboBox'
        $distance.SelectedIndex = 2
        (Get-Field $window 'TreasureScaleSlider').Value = 150
        $save = $window.GetType().GetMethod('SaveLauncherSettings', [Reflection.BindingFlags]'NonPublic, Instance')
        $saved = $save.Invoke($window, [object[]]::new(5))
        $graphics = (Get-Content -LiteralPath (Join-Path $app 'fgo-launcher.json') -Raw | ConvertFrom-Json).graphics
        $effects = Join-Path $app 'graphics-effects.ini'
        $view = if (Test-Path -LiteralPath $effects) { @(Get-Content -LiteralPath $effects | Where-Object { $_ -like 'view_distance=*' }) -join ',' } else { 'none' }
        $written = "volumeFog=$($graphics.volumeFog) viewDistance=$($graphics.viewDistance) treasureScale=$($graphics.treasureScale) ini=$view"
        $want = if ($Build -eq '1151') { 'volumeFog=False viewDistance=400 treasureScale=150 ini=view_distance=400' } else { 'volumeFog= viewDistance= treasureScale= ini=none' }
        Test-Result 'his scene settings shown and saved on his build only' ($saved -and $sceneShown -eq ($Build -eq '1151') -and $written -ceq $want) "shown: $sceneShown; $written"
    } else {
        Test-Result 'his scene settings shown and saved on his build only' $false "panel: $($null -ne $scene), width box: '$($width.Text)'"
    }

    # His 240 FPS, high frame rate timing, VSync and AMD anti-tearing on Display: shown and saved on his build only.
    $syncPanel = Get-Field $window 'FrameSyncPanel'
    if ($null -ne $syncPanel) {
        $syncShown = $syncPanel.Visibility -eq [Windows.Visibility]::Visible
        (Get-Field $window 'VSyncCheckBox').IsChecked = $true
        (Get-Field $window 'AmdPresentSyncCheckBox').IsChecked = $true
        $highFps = Get-Field $window 'HighFpsV2CheckBox'
        if ($null -ne $highFps) { $highFps.IsChecked = $true }
        $rates = Get-Field $window 'FrameRateComboBox'
        $offered = @($rates.Items | ForEach-Object { [string]$_.Tag }) -join ','
        $top = @($rates.Items | Where-Object { [string]$_.Tag -eq '240' })
        $rates.SelectedItem = if ($top.Count -gt 0) { $top[0] } else { $rates.Items[3] }
        $saved = $window.GetType().GetMethod('SaveLauncherSettings', [Reflection.BindingFlags]'NonPublic, Instance').Invoke($window, [object[]]::new(5))
        $settings = Get-Content -LiteralPath (Join-Path $app 'fgo-launcher.json') -Raw | ConvertFrom-Json
        $graphics = $settings.graphics
        $written = "rates=$offered targetFps=$($settings.targetFps) highFpsV2=$($graphics.highFpsV2) vsync=$($graphics.vsync) amdPresentSync=$($graphics.amdPresentSync)"
        $want = if ($Build -eq '1151') { 'rates=60,90,120,144,240 targetFps=240 highFpsV2=True vsync=True amdPresentSync=True' } else { 'rates=60,90,120,144 targetFps=144 highFpsV2= vsync= amdPresentSync=' }
        Test-Result 'his frame rate and sync switches shown and saved on his build only' ($saved -and $syncShown -eq ($Build -eq '1151') -and $written -ceq $want) "shown: $syncShown; $written"
    } else {
        Test-Result 'his frame rate and sync switches shown and saved on his build only' $false 'FrameSyncPanel is missing'
    }

    # Cloud23333's start-up rule: a window off its monitor's work area is moved, and shrunk if larger, back onto it.
    $visibility = $launcher.GetType('FGOLocalPlatform.WindowVisibility')
    $boundsType = $launcher.GetType('FGOLocalPlatform.WindowVisibility+Bounds')
    if ($null -ne $visibility -and $null -ne $boundsType) {
        function New-Bounds([int]$Left, [int]$Top, [int]$Right, [int]$Bottom) {
            $bounds = [Activator]::CreateInstance($boundsType)
            foreach ($pair in @(('Left', $Left), ('Top', $Top), ('Right', $Right), ('Bottom', $Bottom))) { $boundsType.GetField($pair[0], [Reflection.BindingFlags]'NonPublic, Public, Instance').SetValue($bounds, $pair[1]) }
            return $bounds
        }
        function Show-Bounds($Bounds) { return (@('Left', 'Top', 'Right', 'Bottom') | ForEach-Object { $boundsType.GetField($_, [Reflection.BindingFlags]'NonPublic, Public, Instance').GetValue($Bounds) }) -join ',' }
        $fit = $visibility.GetMethod('Fit', [Reflection.BindingFlags]'NonPublic, Public, Static')
        $workArea = New-Bounds 0 0 1920 1040
        $fitted = foreach ($case in @(@(100, 100, 1300, 900), @(1500, 900, 2700, 1700), @(-3000, -50, -1800, 750), @(-10, -10, 2500, 1300))) {
            Show-Bounds ($fit.Invoke($null, @((New-Bounds $case[0] $case[1] $case[2] $case[3]), $workArea)))
        }
        Test-Result 'the window is pulled back onto the work area' (($fitted -join ' | ') -ceq '100,100,1300,900 | 720,240,1920,1040 | 0,0,1200,800 | 0,0,1920,1040') ($fitted -join ' | ')
        # Maximized, the window fills its monitor's work area and leaves the taskbar uncovered: the position is given
        # from the monitor's corner, as WM_GETMINMAXINFO takes it.
        $area = $visibility.GetMethod('MaximizedArea', [Reflection.BindingFlags]'NonPublic, Public, Static')
        $maximized = if ($area) {
            foreach ($case in @(@(@(-1920, 0, 0, 1080), @(-1920, 0, 0, 1032)), @(@(0, 0, 3440, 1440), @(60, 0, 3440, 1440)))) {
                $box = $area.Invoke($null, @((New-Bounds $case[0][0] $case[0][1] $case[0][2] $case[0][3]), (New-Bounds $case[1][0] $case[1][1] $case[1][2] $case[1][3])))
                Show-Bounds $box
            }
        }
        Test-Result 'maximized fills the work area, not the taskbar' (($maximized -join ' | ') -ceq '0,0,1920,1032 | 60,0,3440,1440') ($maximized -join ' | ')
    } else {
        Test-Result 'the window is pulled back onto the work area' $false 'FGOLocalPlatform.WindowVisibility is missing'
    }

    # Controls: rows for the skill target keys both stock hooks read, with Cloud23333's defaults and his rule that the
    # three targets and Cancel never share a key or button.
    $controls = Get-Field $window 'ControlsPanel'
    $mappings = @(Get-Field $controls 'mappings')
    $targets = @($mappings | Where-Object { $_.Key -like 'skillTarget*' -and $_.Section -cne 'battleKeyboard' } | ForEach-Object { "$($_.Section).$($_.Key)=$($_.Default)" }) -join ','
    $rows = @((Get-Field $controls 'KeyboardMappings').Items).Count, @((Get-Field $controls 'ControllerMappings').Items).Count -join '/'
    $padHelp = (Get-Field $controls 'ControllerDeadzoneHelp').Text
    Test-Result 'controller help names the Skill Target buttons' ($padHelp -like '*Bind Skill Target 1, 2 and 3 to pick the Servant in a skill''s target menu*' -and $padHelp -notlike '*use the game''s own touch interface when you need to pick a target*') $padHelp
    Test-Result 'skill target rows on Controls'($targets -ceq 'keyboard.skillTarget1=49,keyboard.skillTarget2=50,keyboard.skillTarget3=51,keyboard.skillTargetCancel=27,xinput.skillTarget1=0,xinput.skillTarget2=0,xinput.skillTarget3=0,xinput.skillTargetCancel=8192' -and $rows -ceq '16/12') "$targets; rows $rows"
    $second = @($mappings | Where-Object { $_.Section -ceq 'keyboard' -and $_.Key -ceq 'skillTarget2' })
    $padCancel = @($mappings | Where-Object { $_.Section -ceq 'xinput' -and $_.Key -ceq 'skillTargetCancel' })
    $padFirst = @($mappings | Where-Object { $_.Section -ceq 'xinput' -and $_.Key -ceq 'skillTarget1' })
    if ($second.Count -eq 1 -and $padCancel.Count -eq 1 -and $padFirst.Count -eq 1) {
        $status = Get-Field $controls 'StatusText'
        $second[0].Value = 0x31
        $sharedKey = $controls.SaveBindings()
        $sharedKeyText = $status.Text
        $second[0].Value = 0x35
        $padFirst[0].Value = 8192
        $sharedPad = $controls.SaveBindings()
        $padFirst[0].Value = 4096
        $saved = $controls.SaveBindings()
        $ini = [IO.File]::ReadAllText((Join-Path $app 'segatools.ini'))
        $refusal = 'The three skill targets and Cancel cannot share a key or button; any of them can share one with a Servant skill, attack or another action.'
        Test-Result 'skill targets saved, never sharing a key' (!$sharedKey -and $sharedKeyText -ceq $refusal -and !$sharedPad -and $saved -and $ini -match '(?m)^skillTarget2=0x35\r?$' -and $ini -match '(?m)^skillTarget1=0x1000\r?$') "shared key saved: $sharedKey ($sharedKeyText); shared pad button saved: $sharedPad; then saved: $saved"
    } else {
        Test-Result 'skill targets saved, never sharing a key' $false 'no skill target rows'
    }

    # Cloud23333's keyboard battle mode on Controls: his [battleKeyboard] keys and defaults on his build only, his
    # conflict rules, and his controlProfile written so his launcher shows the same.
    $battlePanel = Get-Field $controls 'BattleKeyboardPanel'
    $battleRows = @($mappings | Where-Object { $_.Section -ceq 'battleKeyboard' } | ForEach-Object { "$($_.Key)=$($_.Default)" }) -join ','
    $battleShown = $null -ne $battlePanel -and $battlePanel.Visibility -eq [Windows.Visibility]::Visible
    $hisRows = 'touchClick=1,up=87,down=83,left=65,right=68,attack=1,dash=160,np=32,cardLeft=81,cardRight=69,cardConfirm=5,cardUndo=2,cardWheelUp=256,cardWheelDown=512,lock=4,unlock=88,nextTarget=70,toggle=9,skill1=49,skill2=50,skill3=51,skillTarget1=49,skillTarget2=50,skillTarget3=51,skillTargetCancel=27'
    Test-Result 'keyboard battle mode on his build only' ($null -ne $battlePanel -and $battleShown -eq ($Build -eq '1151') -and $battleRows -ceq $(if ($Build -eq '1151') { $hisRows } else { '' })) "shown: $battleShown; rows: $battleRows"
    $save = $window.GetType().GetMethod('SaveLauncherSettings', [Reflection.BindingFlags]'NonPublic, Instance')
    if ($Build -eq '1151' -and $null -ne $battlePanel) {
        $status = Get-Field $controls 'StatusText'
        function Get-Battle([string]$Key) { return @($mappings | Where-Object { $_.Section -ceq 'battleKeyboard' -and $_.Key -ceq $Key })[0] }
        (Get-Field $controls 'BattleKeyboardSwitch').IsChecked = $true
        (Get-Field $controls 'BattleMouseSensitivity').Value = 150
        (Get-Field $controls 'BattleInvertY').IsChecked = $true
        $refusals = @()
        (Get-Battle 'lock').Value = 0x47
        (Get-Battle 'toggle').Value = 0
        $refusals += "$($save.Invoke($window, [object[]]::new(5)))=$($status.Text)"
        (Get-Battle 'toggle').Value = 0x45
        $refusals += "$($save.Invoke($window, [object[]]::new(5)))=$($status.Text)"
        (Get-Battle 'toggle').Value = 9
        (Get-Battle 'cardLeft').Value = 0x45
        $refusals += "$($save.Invoke($window, [object[]]::new(5)))=$($status.Text)"
        (Get-Battle 'cardLeft').Value = 81
        $want = @(
            'False=Pick a key for Step Out / Back, so the touch menus can be used in battle.',
            'False=Step Out / Back cannot share its key with another keyboard battle mode action.',
            'False=Keyboard battle mode keys clash: Card Left, Card Right.')
        $saved = $save.Invoke($window, [object[]]::new(5))
        $ini = [IO.File]::ReadAllText((Join-Path $app 'segatools.ini'))
        $section = @([regex]::Match($ini, '(?ms)^\[battleKeyboard\]\r?\n(.*?)(?=^\[|\z)').Groups[1].Value -split '\r?\n' | Where-Object { $_ -match '^(enabled|invertY|sensitivity|lock|toggle|cardLeft)=' } | Sort-Object) -join ','
        $profile = (Get-Content -LiteralPath (Join-Path $app 'fgo-launcher.json') -Raw | ConvertFrom-Json).controlProfile
        Test-Result 'keyboard battle mode refusals' (($refusals -join ' | ') -ceq ($want -join ' | ')) ($refusals -join ' | ')
        Test-Result 'keyboard battle mode saved under his keys' ($saved -and $section -ceq 'cardLeft=0x51,enabled=1,invertY=1,lock=0x47,sensitivity=150,toggle=0x9' -and $profile -ceq 'keyboard-battle') "saved: $saved; $section; controlProfile=$profile"
        (Get-Field $controls 'BattleKeyboardSwitch').IsChecked = $false
        $saved = $save.Invoke($window, [object[]]::new(5))
        $ini = [IO.File]::ReadAllText((Join-Path $app 'segatools.ini'))
        $profile = (Get-Content -LiteralPath (Join-Path $app 'fgo-launcher.json') -Raw | ConvertFrom-Json).controlProfile
        Test-Result 'keyboard battle mode off' ($saved -and $ini -match '(?m)^enabled=0\r?$' -and $profile -ceq 'keyboard-touch') "saved: $saved; controlProfile=$profile"
    } elseif ($null -ne $battlePanel) {
        $saved = $save.Invoke($window, [object[]]::new(5))
        $ini = [IO.File]::ReadAllText((Join-Path $app 'segatools.ini'))
        $profile = (Get-Content -LiteralPath (Join-Path $app 'fgo-launcher.json') -Raw | ConvertFrom-Json).controlProfile
        Test-Result 'no keyboard battle mode keys on 11.00' ($saved -and $ini -notmatch '(?m)^\[battleKeyboard\]' -and $null -eq $profile) "saved: $saved; controlProfile=$profile"
    }

    # Cloud23333's controller card selection, controller camera with lock-on and controller mouse on Controls: his
    # [xinput] keys and defaults on his build only, and his rule that the mouse toggle and click never share a button.
    $padPanel = Get-Field $controls 'HisPadPanel'
    $hisPad = 'cardLeft', 'cardRight', 'cardConfirm', 'cardUndo', 'cameraLock', 'mouseToggle', 'mouseConfirm', 'mouseStick', 'mouseSpeed', 'mouseScroll', 'mouseScrollSpeed'
    $padRows = @($mappings | Where-Object { $_.Section -ceq 'xinput' -and $hisPad -ccontains $_.Key } | ForEach-Object { "$($_.Key)=$($_.Default)" }) -join ','
    $padShown = $null -ne $padPanel -and $padPanel.Visibility -ne [Windows.Visibility]::Collapsed
    $hisPadRows = 'cardLeft=256,cardRight=512,cardConfirm=4096,cardUndo=8192,cameraLock=128,mouseToggle=65536,mouseConfirm=4096,mouseStick=0,mouseSpeed=100,mouseScroll=1,mouseScrollSpeed=100'
    Test-Result 'his controller card selection, camera and mouse on his build only' ($null -ne $padPanel -and $padShown -eq ($Build -eq '1151') -and $padRows -ceq $(if ($Build -eq '1151') { $hisPadRows } else { '' })) "shown: $padShown; rows: $padRows"
    if ($Build -eq '1151' -and $null -ne $padPanel) {
        $status = Get-Field $controls 'StatusText'
        function Get-Pad([string]$Key) { return @($mappings | Where-Object { $_.Section -ceq 'xinput' -and $_.Key -ceq $Key })[0] }
        (Get-Field $controls 'CardNavigationEnabled').IsChecked = $false
        (Get-Field $controls 'ModernCameraEnabled').IsChecked = $true
        (Get-Field $controls 'CameraSensitivity').Value = 150
        (Get-Field $controls 'CameraInvertY').IsChecked = $true
        (Get-Pad 'mouseToggle').Value = 4096
        $shared = "$($save.Invoke($window, [object[]]::new(5)))=$($status.Text)"
        (Get-Pad 'mouseToggle').Value = 65536
        (Get-Pad 'mouseSpeed').Value = 150
        $saved = $save.Invoke($window, [object[]]::new(5))
        $ini = [IO.File]::ReadAllText((Join-Path $app 'segatools.ini'))
        $section = @([regex]::Match($ini, '(?ms)^\[xinput\]\r?\n(.*?)(?=^\[|\z)').Groups[1].Value -split '\r?\n' | Where-Object { $_ -match '^(cardNavigation|modernCamera|cameraSensitivity|cameraInvertY|cameraLock|mouseToggle|mouseSpeed)=' } | Sort-Object) -join ','
        Test-Result 'controller mouse toggle and click never share a button' ($shared -ceq "False=The controller mouse's toggle and click buttons cannot be the same; pick Disabled for the toggle to turn the controller mouse off.") $shared
        Test-Result 'his controller settings saved under his keys' ($saved -and $section -ceq 'cameraInvertY=1,cameraLock=0x80,cameraSensitivity=150,cardNavigation=0,modernCamera=1,mouseSpeed=0x96,mouseToggle=0x10000') "saved: $saved; $section"
    } elseif ($null -ne $padPanel) {
        $saved = $save.Invoke($window, [object[]]::new(5))
        $ini = [IO.File]::ReadAllText((Join-Path $app 'segatools.ini'))
        Test-Result 'no controller camera or mouse keys on 11.00' ($saved -and $ini -notmatch '(?m)^(modernCamera|cardNavigation|mouseToggle)=') "saved: $saved"
    }

    # Files Cloud23333's launcher also writes, changed while this window was in the background: on its return the pages
    # show the new values, and a save keeps them.
    $flags = [Reflection.BindingFlags]'NonPublic, Instance'
    $deactivated = $window.GetType().GetMethod('Window_OnDeactivated', $flags)
    $activated = $window.GetType().GetMethod('Window_OnActivated', $flags)
    if ($null -ne $deactivated -and $null -ne $activated) {
        $deactivated.Invoke($window, @($window, [EventArgs]::Empty))
        Start-Sleep -Milliseconds 50
        $jsonPath = Join-Path $app 'fgo-launcher.json'
        $outside = Get-Content -LiteralPath $jsonPath -Raw | ConvertFrom-Json
        if ($null -eq $outside.graphics) { $outside | Add-Member -NotePropertyName graphics -NotePropertyValue ([pscustomobject]@{}) }
        $outside.graphics | Add-Member -NotePropertyName smaa -NotePropertyValue 2 -Force
        [IO.File]::WriteAllText($jsonPath, ($outside | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
        $iniPath = Join-Path $app 'segatools.ini'
        [IO.File]::WriteAllText($iniPath, ([IO.File]::ReadAllText($iniPath) -replace '(?m)^stickDeadzone=\d+', 'stickDeadzone=5000'))
        if ([IO.File]::ReadAllText($iniPath) -notmatch '(?m)^stickDeadzone=5000') { [IO.File]::AppendAllText($iniPath, "`r`n[xinput]`r`nstickDeadzone=5000`r`n") }
        [IO.File]::WriteAllText((Join-Path $app 'graphics-effects.ini'), "[effects]`nhide_dark_mask=0`nopacity=40`nview_distance=100`n")
        [IO.File]::WriteAllText((Join-Path $app 'audio-volume.ini'), "voice=100`r`nbgm=30`r`neffects=100`r`n")
        $activated.Invoke($window, @($window, [EventArgs]::Empty))
        $shown = "smaa=$([string](Get-Field $window 'SmaaComboBox').SelectedItem.Tag) deadzone=$((Get-Field $controls 'DeadzoneInput').Text) opacity=$((Get-Field (Get-Field $window 'BattleEffectsPanel') 'OpacitySlider').Value) bgm=$((Get-Field (Get-Field $window 'AudioPanel') 'BgmSlider').Value)"
        $saved = $save.Invoke($window, [object[]]::new(5))
        $kept = "smaa=$((Get-Content -LiteralPath $jsonPath -Raw | ConvertFrom-Json).graphics.smaa) deadzone=$(if ([IO.File]::ReadAllText($iniPath) -match '(?m)^stickDeadzone=(\d+)') { $Matches[1] })"
        Test-Result 'changes made outside, read again on return' ($shown -ceq 'smaa=2 deadzone=5000 opacity=40 bgm=30' -and $saved -and $kept -ceq 'smaa=2 deadzone=5000') "shown: $shown; saved: $saved, kept: $kept"
    } else {
        Test-Result 'changes made outside, read again on return' $false 'Window_OnDeactivated or Window_OnActivated is missing'
    }

    # The game without his update: one line on Controls says what his update adds.
    $missing = Get-Field $controls 'HisPadMissingText'
    $missingShown = $null -ne $missing -and $missing.Visibility -eq [Windows.Visibility]::Visible
    Test-Result 'what his update adds, on the game without it' ($null -ne $missing -and $missingShown -eq ($Build -ne '1151') -and $missing.Text -ceq 'Cloud23333''s update adds a keyboard battle mode with a mouse camera, a controller camera, a controller mouse and controller card selection here.') "Controls line shown: $missingShown"

    # The cursor lock on Controls: saved as cursor.confine in fgo-launcher.json, which the launch writes as [cursor] confine.
    $lock = Get-Field $controls 'CursorLockSwitch'
    if ($null -ne $lock) {
        $lock.IsChecked = $true
        $saved = $save.Invoke($window, [object[]]::new(5))
        $confine = (Get-Content -LiteralPath (Join-Path $app 'fgo-launcher.json') -Raw | ConvertFrom-Json).cursor.confine
        $lock.IsChecked = $false
        $savedOff = $save.Invoke($window, [object[]]::new(5))
        $confineOff = (Get-Content -LiteralPath (Join-Path $app 'fgo-launcher.json') -Raw | ConvertFrom-Json).cursor.confine
        Test-Result 'cursor lock on Controls' ($saved -and $savedOff -and $confine -eq $true -and $confineOff -eq $false) "on: $saved $confine; off: $savedOff $confineOff"
    } else {
        Test-Result 'cursor lock on Controls' $false 'CursorLockSwitch is missing'
    }

    $debug = Get-Field $window 'DebugPanel'
    $debugTab = @($tabs.Items | Where-Object { [string]$_.Header -ceq 'Battle Debug' })[0]
    Test-Result 'Battle Debug holds the switches Play guards' ($null -ne $debug -and $null -ne $debugTab -and (Test-Inside $debug $debugTab)) "DebugPanel: $(if ($debug) { $debug.GetType().Name } else { 'none' })"
    # Cloud23333's auto clear, cost 233 and PvP map pick on Battle Debug: shown on his build only, saved like the others.
    $hisPanel = if ($debug) { Get-Field $debug 'HisSwitchesPanel' } else { $null }
    if ($null -ne $hisPanel) {
        $hisShown = $hisPanel.Visibility -eq [Windows.Visibility]::Visible
        (Get-Field $debug 'AutoClear').IsChecked = $true
        $iniFile = Join-Path $app 'battle-debug.ini'
        $autoClear = if (Test-Path -LiteralPath $iniFile) { @(Get-Content -LiteralPath $iniFile | Where-Object { $_ -like 'auto_clear=*' }) -join ',' } else { 'no file' }
        $savedClear = (Get-Content -LiteralPath (Join-Path $app 'fgo-launcher.json') -Raw | ConvertFrom-Json).battleDebug.auto_clear
        Test-Result 'his Battle Debug switches on his build only' ($hisShown -eq ($Build -eq '1151') -and $autoClear -ceq $(if ($Build -eq '1151') { 'auto_clear=1' } else { 'auto_clear=0' }) -and $savedClear -eq ($Build -eq '1151')) "shown: $hisShown; ini $autoClear; saved $savedClear"
    } else {
        Test-Result 'his Battle Debug switches on his build only' $false 'HisSwitchesPanel is missing'
    }

    $accountsTab = @($tabs.Items | Where-Object { [string]$_.Header -ceq 'Accounts' })[0]
    $manage = $window.GetType().GetMethod('ManageAccountsButton_OnClick', [Reflection.BindingFlags]'NonPublic, Instance')
    $manage.Invoke($window, @($null, [Windows.RoutedEventArgs]::new()))
    $combo = Get-Field $window 'AccountComboBox'
    Test-Result 'Manage opens Accounts' ($null -ne $accountsTab -and [object]::ReferenceEquals($tabs.SelectedItem, $accountsTab) -and $null -ne $combo -and (Test-Inside $combo $accountsTab)) "selected: $($tabs.SelectedItem.Header)"

    # Every button of the Accounts page and its account list reach their handlers.
    function Get-Handlers($Element, $RoutedEvent) {
        $store = $Element.GetType().GetProperty('EventHandlersStore', [Reflection.BindingFlags]'NonPublic, Instance').GetValue($Element)
        if ($null -eq $store) { return 0 }
        return @($store.GetRoutedEventHandlers($RoutedEvent)).Count
    }
    $buttons = [Collections.Generic.List[object]]::new()
    $queue = [Collections.Generic.Queue[object]]::new()
    $queue.Enqueue($accountsTab.Content)
    while ($queue.Count -gt 0) {
        $node = $queue.Dequeue()
        if ($node -is [Windows.Controls.Button]) { $buttons.Add($node) }
        if ($node -is [Windows.DependencyObject]) { foreach ($child in [Windows.LogicalTreeHelper]::GetChildren($node)) { $queue.Enqueue($child) } }
    }
    $transfer = Get-Field $accountsTab.Content 'AccountTransferPanel'
    $transferShown = $null -ne $transfer -and $transfer.Visibility -eq [Windows.Visibility]::Visible
    Test-Result 'Move accounts where his account tool can' ($null -ne $transfer -and $transferShown -eq ($Build -eq '1151')) "shown: $transferShown"
    $unwired = @($buttons | Where-Object { (Get-Handlers $_ ([Windows.Controls.Primitives.ButtonBase]::ClickEvent)) -eq 0 } | ForEach-Object { [string]$_.Content })
    Test-Result 'every Accounts button reaches its handler' ($buttons.Count -ge 17 -and $unwired.Count -eq 0 -and (Get-Handlers $combo ([Windows.Controls.Primitives.Selector]::SelectionChangedEvent)) -gt 0) "$($buttons.Count) buttons; not connected: $($unwired -join ', ')"

    # The card counts say what they count: the latest acquisitions listed, the cards printed, the cards owned.
    $entryType = $launcher.GetType('FGOLocalPlatform.MainWindow+AccountEntry')
    $historyType = $launcher.GetType('FGOLocalPlatform.MainWindow+SummonHistoryEntry')
    $entry = [Activator]::CreateInstance($entryType, $true)
    $history = [Activator]::CreateInstance([Collections.Generic.List`1].MakeGenericType($historyType))
    1..3 | ForEach-Object { $history.Add([Activator]::CreateInstance($historyType, $true)) }
    $entryType.GetProperty('SummonHistory').SetValue($entry, $history)
    $entryType.GetProperty('SummonResultCount').SetValue($entry, 2)
    $entryType.GetProperty('OwnedCardCount').SetValue($entry, 1234)
    $combo.ItemsSource = @($entry)
    $combo.SelectedItem = $entry
    $rowTexts = foreach ($row in (Get-Field $window 'AccountDetailsTable').Items) {
        (@([Windows.LogicalTreeHelper]::GetChildren($row.Child) | Where-Object { $_ -is [Windows.Controls.TextBlock] } | ForEach-Object { $_.Text }) -join ': ')
    }
    $cardRow = @($rowTexts | Where-Object { $_ -like 'Card records*' })[0]
    $summary = (Get-Field $window 'CurrentSummonSummaryText').Text
    Test-Result 'account card counts named for what they count' ($cardRow -ceq 'Card records: 3 latest acquired / 2 printed' -and $summary -like '* - Cards owned 1,234') "row: $cardRow; summary: $summary"
    $combo.ItemsSource = $null

    $eventsTab = @($tabs.Items | Where-Object { [string]$_.Header -ceq 'Events' })[0]
    $views = @{}
    if ($eventsTab -and $eventsTab.Content) {
        $queue = [Collections.Generic.Queue[object]]::new()
        $queue.Enqueue($eventsTab.Content)
        while ($queue.Count -gt 0) {
            $node = $queue.Dequeue()
            $views[$node.GetType().Name] = $node
            if ($node -is [Windows.DependencyObject]) { foreach ($child in [Windows.LogicalTreeHelper]::GetChildren($node)) { $queue.Enqueue($child) } }
        }
    }
    $holds = if ($views.ContainsKey('BannerSettingsView')) { 'Banners' } elseif ($views.ContainsKey('EventSwitchesView')) { 'his switches' } else { 'nothing' }
    Test-Result 'Events holds the Banners patch on 11.00, his switches on his build' ($holds -ceq $(if ($Build -eq '1100') { 'Banners' } else { 'his switches' })) "holds $holds"

    if ($Build -eq '1151' -and $views.ContainsKey('EventSwitchesView')) {
        # One tick off: the event goes into the file his server reads, at once.
        $view = $views['EventSwitchesView']
        [void]$view.GetType().GetMethod('Reload', [Reflection.BindingFlags]'NonPublic, Instance').Invoke($view, @())
        $boxes = $view.GetType().GetField('boxes', [Reflection.BindingFlags]'NonPublic, Instance').GetValue($view)
        $box = $boxes[2]
        $box.IsChecked = $false
        $box.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Primitives.ButtonBase]::ClickEvent))
        $file = Join-Path $work 'Server\state\activity-display.json'
        $hidden = if (Test-Path -LiteralPath $file) { (Get-Content -LiteralPath $file -Raw | ConvertFrom-Json).hidden_ids -join ',' } else { 'no file' }
        Test-Result 'an event ticked off is saved at once' ($boxes.Count -eq 73 -and $hidden -ceq '2') "$($boxes.Count) events; hidden_ids: $hidden"
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
