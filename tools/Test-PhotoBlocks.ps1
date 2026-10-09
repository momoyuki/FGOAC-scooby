<#
Checks the Photo Mode panels against the shared-memory blocks of Cloud23333's photo hook, without the game and without
showing a window: this process stands in for the game, creates each block the way his hook does (his name with this
process id, his magic, version and size), builds the panels in memory on an STA thread with the launcher's resources,
changes their controls and reads the written bytes back at his offsets. Every file is written under a throwaway folder
in %TEMP%, deleted when the check ends.

  pwsh -File tools\Test-PhotoBlocks.ps1

Through the launcher assembly (src\bin\Release, so run build.cmd first):
  - the numeric fields: a typed value outside the slider clamps to it, or widens it on a field that expands; a drag
    moves by the slider's step and snaps to its ticks; position sliders run -100..100 and widen at either edge;
  - the Face panel reads a character's expressions off the UI thread and publishes nothing until they are in, and
    names the character by its Servant's English card name;
  - the Model block, version 12 (his 1.10 hook): position, eye gaze, iris, eyelid, mouth, scale and a face shape at
    his offsets, the new controls shown; version 6 (his 1.02 hook, game 11.00): the old fields written, nothing past
    its 856 bytes, the new controls hidden and no restart asked;
  - the Body block, version 3 (his 1.10 hook): the model's resource name read; version 2 (his 1.02 hook) taken
    without one; a motion of another form left out;
  - the Backdrop ('FPGB' version 1) and Style ('FGPS' version 2) blocks the panel creates for his graphics layer, the
    backdrop's colors saved in the photo settings and read back; both made on Model block version 12 only;
  - the character panels' refresh throttle, the camera speed written on the first connect and the overlay's arrow cursor.

Exit codes: 0 every check passed, 1 a check failed, 2 a prerequisite is missing.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'

$repository = Split-Path -Parent $PSScriptRoot
$assembly = Join-Path $repository 'src\bin\Release\net6.0-windows\FGOLocalPlatform.dll'
if ($PSVersionTable.PSVersion.Major -lt 7) { Write-Host 'Run this with pwsh 7: the launcher assembly targets .NET 6.'; exit 2 }
if (!(Test-Path -LiteralPath $assembly)) { Write-Host "Missing $assembly. Run build.cmd first."; exit 2 }

# WPF controls need an STA thread.
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') {
    & (Join-Path $PSHOME 'pwsh.exe') -STA -NoProfile -File $PSCommandPath
    exit $LASTEXITCODE
}

$script:failed = 0
function Test-Result([string]$Name, [bool]$Passed, [string]$Detail) {
    Write-Host ("{0} {1}: {2}" -f $(if ($Passed) { 'PASS' } else { 'FAIL' }), $Name, $Detail)
    if (!$Passed) { $script:failed++ }
}
function Get-Field($Object, [string]$Name) {
    $field = $Object.GetType().GetField($Name, [Reflection.BindingFlags]'NonPublic, Public, Instance')
    if ($null -ne $field) { return $field.GetValue($Object) }
    $property = $Object.GetType().GetProperty($Name, [Reflection.BindingFlags]'NonPublic, Public, Instance')
    if ($null -ne $property) { return $property.GetValue($Object) }
    return $null
}
function Invoke-Static($Type, [string]$Name, [object[]]$Arguments) {
    $method = $Type.GetMethod($Name, [Reflection.BindingFlags]'NonPublic, Public, Static')
    if ($null -eq $method) { throw "$($Type.Name).$Name is missing" }
    return $method.Invoke($null, $Arguments)
}
function Send-Typed($Box, [string]$Text) {
    $Box.Text = $Text
    $lost = [Windows.Input.KeyboardFocusChangedEventArgs]::new([Windows.Input.Keyboard]::PrimaryDevice, 0, $Box, $null)
    $lost.RoutedEvent = [Windows.UIElement]::LostKeyboardFocusEvent
    $Box.RaiseEvent($lost)
}
function New-Slider([double]$Minimum, [double]$Maximum, [double]$SmallChange) {
    $slider = [Windows.Controls.Slider]::new()
    $slider.Minimum = $Minimum
    $slider.Maximum = $Maximum
    $slider.SmallChange = $SmallChange
    return $slider
}
# A block as his hook creates it: Local\<name>_<game pid>, this process standing in for the game.
$script:blocks = @{}
function New-Block([string]$Name, [long]$Size, [int]$Magic, [int]$Version) {
    $map = [IO.MemoryMappedFiles.MemoryMappedFile]::CreateNew("Local\$($Name)_$PID", $Size)
    $view = $map.CreateViewAccessor()
    $view.Write([long]0, [int]$Magic)
    $view.Write([long]4, [int]$Version)
    $script:blocks[$Name] = @($view, $map)
    return $view
}
function Close-Block([string]$Name) {
    if (!$script:blocks.ContainsKey($Name)) { return }
    foreach ($each in $script:blocks[$Name]) { $each.Dispose() }
    $script:blocks.Remove($Name)
}
function Test-Mapping([string]$Name) {
    try { [IO.MemoryMappedFiles.MemoryMappedFile]::OpenExisting("Local\$($Name)_$PID").Dispose(); return $true }
    catch { return $false }
}
function Write-Text($View, [long]$Offset, [string]$Text) {
    $bytes = [Text.Encoding]::ASCII.GetBytes($Text)
    for ($i = 0; $i -lt $bytes.Length; $i++) { $View.Write($Offset + $i, [byte]$bytes[$i]) }
}
# Runs the dispatcher's queued work, the panels' awaits included, until the condition holds or two seconds pass.
function Wait-Dispatcher([scriptblock]$Until) {
    $noop = [Action][Threading.Thread]::MemoryBarrier
    for ($i = 0; $i -lt 100 -and !(& $Until); $i++) {
        [Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke($noop, [Windows.Threading.DispatcherPriority]::Background)
        Start-Sleep -Milliseconds 20
    }
}
# Hidden when the element or a parent below the panel is not Visible.
function Test-Hidden($Element, $Panel) {
    $node = $Element
    while ($null -ne $node -and -not [object]::ReferenceEquals($node, $Panel)) {
        if ($node -is [Windows.UIElement] -and $node.Visibility -ne [Windows.Visibility]::Visible) { return $true }
        $node = [Windows.LogicalTreeHelper]::GetParent($node)
    }
    return $false
}
# The face block with one character on stage, which the Face panel selects on its own.
$actorId = [uint64]0x0000002A00000101
function New-FaceBlock([uint32]$Model = 1, [string]$Token = 'SVT_0001') {
    $face = New-Block 'FGOLocalPhotoFace' 5416 1162037062 1
    $face.Write([long]12, [int]1)
    $face.Write([long]296, $actorId)
    $face.Write([long]304, $Model)
    Write-Text $face 312 $Token
    return $face
}

$work = Join-Path ([IO.Path]::GetTempPath()) ('fgoac-photo-' + [guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    Add-Type -AssemblyName PresentationFramework
    [AppContext]::SetData('APP_CONTEXT_BASE_DIRECTORY', $work + '\')
    $launcher = [Reflection.Assembly]::LoadFrom($assembly)
    . (Join-Path $PSScriptRoot 'DialogSink.ps1'); Enable-DialogSink $assembly
    $application = [Activator]::CreateInstance($launcher.GetType('FGOLocalPlatform.App'))
    $application.InitializeComponent()
    # The panels' awaits resume on this thread's dispatcher, as they do in the launcher.
    [Threading.SynchronizationContext]::SetSynchronizationContext([Windows.Threading.DispatcherSynchronizationContext]::new())
    $numberInput = $launcher.GetType('FGOLocalPlatform.PhotoNumberInput')

    # Typed values: an expanding field widens its slider, the others clamp to it.
    try {
        $plain = New-Slider -1 1 0.01
        Send-Typed (Invoke-Static $numberInput 'Create' @($plain, 1.0, $null, $false, $true)) '5'
        $wide = New-Slider -100 100 0.01
        $wideBox = Invoke-Static $numberInput 'Create' @($wide, 1.0, $null, $true, $true)
        Send-Typed $wideBox '250'
        $up = "$($wide.Value) in $($wide.Minimum)..$($wide.Maximum)"
        Send-Typed $wideBox '-300'
        $down = "$($wide.Value) in $($wide.Minimum)..$($wide.Maximum)"
        $floor = New-Slider 0 4 0.01
        $floorBox = Invoke-Static $numberInput 'Create' @($floor, 1.0, $null, $true, $false)
        Send-Typed $floorBox '-2'
        $low = "$($floor.Value) in $($floor.Minimum)..$($floor.Maximum)"
        Send-Typed $floorBox '9'
        $high = "$($floor.Value) in $($floor.Minimum)..$($floor.Maximum)"
        $seen = "plain $($plain.Value) in $($plain.Minimum)..$($plain.Maximum); wide $up, $down; floor $low, $high"
        Test-Result 'typed values clamp, or widen an expanding field' ($seen -ceq 'plain 1 in -1..1; wide 250 in -100..250, -300 in -300..250; floor 0 in 0..4, 9 in 0..9') $seen
    }
    catch { Test-Result 'typed values clamp, or widen an expanding field' $false $_.Exception.Message }

    # A drag moves by a tenth of the small change, a hundredth with Shift, or by whole ticks on a snapping slider.
    try {
        $fine = New-Slider 0 4 0.01
        $moved = Invoke-Static $numberInput 'DragValue' @($fine, 1.0, 50.0, $false)
        $shifted = Invoke-Static $numberInput 'DragValue' @($fine, 1.0, 50.0, $true)
        $past = Invoke-Static $numberInput 'Apply' @($fine, 6.0, $true, $false)
        $ticks = New-Slider 0 255 1
        $ticks.TickFrequency = 1
        $ticks.IsSnapToTickEnabled = $true
        $snapped = Invoke-Static $numberInput 'Apply' @($ticks, (Invoke-Static $numberInput 'DragValue' @($ticks, 0.0, 10.4, $false)), $false, $true)
        $clamped = Invoke-Static $numberInput 'Apply' @($ticks, 300.0, $false, $true)
        $seen = '{0:0.###} {1:0.####} {2} max {3} {4} {5} max {6}' -f $moved, $shifted, $past, $fine.Maximum, $snapped, $clamped, $ticks.Maximum
        Test-Result 'a drag steps by the slider and snaps to ticks' ($seen -ceq '1.05 1.005 6 max 6 10 255 max 255') $seen
    }
    catch { Test-Result 'a drag steps by the slider and snaps to ticks' $false $_.Exception.Message }

    # Position sliders: -100..100, widened by the larger bound whenever the value reaches an edge, a restored value too.
    try {
        $position = Invoke-Static $numberInput 'CreatePositionSlider' @()
        $start = "$($position.Minimum)..$($position.Maximum)"
        $position.Value = 100
        $right = "$($position.Minimum)..$($position.Maximum)"
        $position.Value = -100
        $left = "$($position.Minimum)..$($position.Maximum)"
        Invoke-Static $numberInput 'SetExpandedValue' @($position, 750.0) | Out-Null
        $restored = "$($position.Value) in $($position.Minimum)..$($position.Maximum)"
        $seen = "$start; $right; $left; $restored"
        Test-Result 'position sliders widen at the edge' ($seen -ceq '-100..100; -100..200; -300..200; 750 in -300..1500') $seen
    }
    catch { Test-Result 'position sliders widen at the edge' $false $_.Exception.Message }

    $faceType = $launcher.GetType('FGOLocalPlatform.PhotoFaceView')
    $modelType = $launcher.GetType('FGOLocalPlatform.PhotoModelView')
    $newer = @('modelScale', 'mouthEnabled', 'morphGroupChoice', 'Backdrop', 'styleView')
    function Get-Newer($Model) { @($newer | ForEach-Object { Get-Field $Model $_ }) + @((Get-Field $Model 'eyeFollow')[0]) }
    # The panels hold their block open until disposed.
    function Close-Views { foreach ($name in 'model', 'face') { $view = Get-Variable -Name $name -Scope 1 -ValueOnly -ErrorAction SilentlyContinue; if ($null -ne $view) { $view.Dispose(); Set-Variable -Name $name -Scope 1 -Value $null } } }

    $faceBlock = New-FaceBlock

    # Expressions: the character's list is read off the UI thread; the panel says so and publishes nothing meanwhile.
    try {
        $face = $faceType::new($work)
        $face.Refresh([Nullable[int]]$PID, $true)
        $during = "loading=$(Get-Field $face 'loading') requested=$($faceBlock.ReadInt32(16))"
        Wait-Dispatcher { -not (Get-Field $face 'loading') }
        $face.Refresh([Nullable[int]]$PID, $true)
        $after = "loading=$(Get-Field $face 'loading') list=$($null -ne (Get-Field $face 'expressions').ItemsSource) requested=$($faceBlock.ReadInt32(16))"
        Test-Result 'expressions load off the UI thread' ($during -ceq 'loading=True requested=0' -and $after -ceq 'loading=False list=True requested=1') "$during; then $after"
        $label = "$(@((Get-Field $face 'actors').Items)[0])"
        Test-Result 'a character is named by its English card name' ($label -ceq 'Altria Pendragon · Model 1 · SVT_0001') $label
    }
    catch { Test-Result 'expressions load off the UI thread' $false ($_ | Out-String) }
    finally { Close-Views }

    # Model block version 12, his 1.10 hook: the old fields and every new one at his offsets.
    try {
        $block = New-Block 'FGOLocalPhotoModel' 158824 1297106758 12
        $block.Write([long]17236, [int]1)
        $block.Write([long]17240, $actorId)
        $block.Write([long]17248, [uint64]1)
        Write-Text $block 17256 'face'
        $block.Write([long](17256 + 68), [int]1)
        Write-Text $block (17256 + 72) 'smile'
        $block.Write([long](17256 + 72 + 64), [int]5)
        $block.Write([long]968, [int](0x1F -bor (1 -shl 12)))
        $face = $faceType::new($work)
        $model = $modelType::new($face)
        $face.Refresh([Nullable[int]]$PID, $true)
        $model.Refresh([Nullable[int]]$PID, $true)
        $first = $block.ReadInt32(8)

        (Get-Field $model 'offsets')[0].Value = 150
        (Get-Field $model 'eyeFollow')[0].IsChecked = $true
        $eyes = $modelType.GetField('eyeValues', [Reflection.BindingFlags]'NonPublic, Instance').GetValue($model)
        $eyes.GetValue(0, 0).Value = 30
        $eyes.GetValue(1, 2).Value = 2.5
        $eyes.GetValue(0, 3).Value = 1.5
        (Get-Field $model 'eyeBlink')[1].IsChecked = $true
        (Get-Field $model 'eyeClosure')[1].Value = 0.5
        (Get-Field $model 'mouthEnabled').IsChecked = $true
        (Get-Field $model 'mouthWeights')[0].Value = 0.7
        (Get-Field $model 'modelScale').Value = 2.5
        (Get-Field $model 'morphAdd').RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Primitives.ButtonBase]::ClickEvent))
        (Get-Field $model 'morphWeights')[0].Value = 0.4
        $block.Write([long]8, [int]0)
        $model.Refresh([Nullable[int]]$PID, $true)

        $read = [ordered]@{
            'pending' = $block.ReadInt32(8); 'actor' = ($block.ReadUInt64(16) -eq $actorId); 'x' = $block.ReadSingle(24)
            'follow' = $block.ReadInt32(852); 'horizontal' = $block.ReadSingle(860); 'strength' = $block.ReadSingle(888); 'iris' = $block.ReadSingle(904)
            'lid' = $block.ReadInt32(920); 'closure' = $block.ReadSingle(924); 'scale' = $block.ReadSingle(900)
            'mouth' = $block.ReadInt32(936); 'preset' = $block.ReadInt32(940); 'a' = $block.ReadSingle(944)
            'morph' = $block.ReadInt32(976); 'target' = $block.ReadInt32(980); 'weight' = $block.ReadSingle(984)
        }
        $seen = ($read.Keys | ForEach-Object { "$_=$($read[$_])" }) -join ' '
        $want = 'pending=1 actor=True x=150 follow=1 horizontal=30 strength=2.5 iris=1.5 lid=1 closure=0.5 scale=2.5 mouth=1 preset=-1 a=0.7 morph=1 target=5 weight=0.4'
        Test-Result 'Model block 12: old and new fields at his offsets' ($first -eq 1 -and $seen -ceq $want) "first publish: $first; $seen"
        $hidden = @(Get-Newer $model | Where-Object { Test-Hidden $_ $model }).Count
        $status = [string](Get-Field $model 'status').Text
        $made = "$(Test-Mapping 'FGOLocalPhotoBackdrop1')/$(Test-Mapping 'FGOLocalPhotoStyle2')"
        Test-Result 'Model block 12: the new controls are shown' ($hidden -eq 0 -and $made -ceq 'True/True' -and $status -notmatch 'Restart|newer') "hidden: $hidden; backdrop/style blocks made: $made; status: $status"
    }
    catch { Test-Result 'Model block 12: old and new fields at his offsets' $false ($_ | Out-String) }
    finally { Close-Views; Close-Block 'FGOLocalPhotoModel' }

    # Model block version 6, his 1.02 hook on game 11.00: the old fields only, nothing past its 856 bytes.
    try {
        $block = New-Block 'FGOLocalPhotoModel' 856 1297106758 6
        $face = $faceType::new($work)
        $model = $modelType::new($face)
        $face.Refresh([Nullable[int]]$PID, $true)
        $model.Refresh([Nullable[int]]$PID, $true)
        (Get-Field $model 'offsets')[0].Value = 12
        (Get-Field $model 'rotations')[0].Value = 45
        (Get-Field $model 'head').IsChecked = $true
        (Get-Field $model 'eyeFollow')[0].IsChecked = $true
        (Get-Field $model 'modelScale').Value = 3
        $block.Write([long]8, [int]0)
        $model.Refresh([Nullable[int]]$PID, $true)
        $page = [byte[]]::new($block.Capacity)
        [void]$block.ReadArray([long]0, $page, 0, $page.Length)
        $past = @($page[856..($page.Length - 1)] | Where-Object { $_ -ne 0 }).Count
        $seen = "pending=$($block.ReadInt32(8)) actor=$($block.ReadUInt64(16) -eq $actorId) x=$($block.ReadSingle(24)) pitch=$($block.ReadSingle(824)) head=$($block.ReadInt32(40)); $past bytes set past 856 of $($page.Length)"
        Test-Result 'Model block 6: old fields only' ($seen -ceq "pending=1 actor=True x=12 pitch=45 head=1; 0 bytes set past 856 of $($page.Length)") $seen
        $hidden = @(Get-Newer $model | Where-Object { Test-Hidden $_ $model }).Count
        $status = [string](Get-Field $model 'status').Text
        $made = "$(Test-Mapping 'FGOLocalPhotoBackdrop1')/$(Test-Mapping 'FGOLocalPhotoStyle2')"
        Test-Result 'Model block 6: the new controls are hidden, no restart asked' ($hidden -eq (Get-Newer $model).Count -and $made -ceq 'False/False' -and $status -notmatch 'Restart|restart|newer') "hidden: $hidden of $((Get-Newer $model).Count); backdrop/style blocks made: $made; status: $status"
    }
    catch { Test-Result 'Model block 6: old fields only' $false ($_ | Out-String) }
    finally { Close-Views; Close-Block 'FGOLocalPhotoModel' }

    # Body block: version 3 (his 1.10 hook) carries the model's 64-byte resource name at 273576; version 2 (his 1.02
    # hook, game 11.00) has none and is taken as it is.
    $bodyType = $launcher.GetType('FGOLocalPlatform.PhotoBodyView')
    foreach ($row in @(@(3, 273640, 'SVT_0001_S02_MDL'), @(2, 273576, ''))) {
        $version, $size, $resource = $row
        $name = "Body block $($version): $(if ($resource) { 'the model resource name is read' } else { 'taken without a name' })"
        try {
            $block = New-Block 'FGOLocalPhotoBody' $size 1111705414 $version
            $block.Write([long]12, [int]1)
            $block.Write([long]40, $actorId)
            $block.Write([long]48, [uint64]7)
            $block.Write([long]68, [int]-1)
            Write-Text $block 72 'root'
            if ($resource) { Write-Text $block 273576 $resource }
            $face = $faceType::new($work)
            $body = $bodyType::new($work, $face)
            $face.Refresh([Nullable[int]]$PID, $true)
            $body.Refresh([Nullable[int]]$PID, $true)
            $seen = "connected=$($null -ne (Get-Field $body 'ipc')) loaded=$(Get-Field $body 'loaded') resource='$(Get-Field $body 'modelResource')' requested=$($block.ReadInt32(24)); status: $((Get-Field $body 'status').Text)"
            Test-Result $name ($seen -clike "connected=True loaded=True resource='$resource' requested=1; status: *" -and $seen -notmatch 'restart|does not match') $seen
        }
        catch { Test-Result $name $false ($_ | Out-String) }
        finally { if ($body) { $body.Dispose(); $body = $null }; Close-Views; Close-Block 'FGOLocalPhotoBody' }
    }

    # Motions by form: an _Snn motion plays on the model of that form, an _S01 one stands in where the form has none.
    try {
        $catalog = $launcher.GetType('FGOLocalPlatform.PhotoAssets.PhotoBodyMotionCatalog')
        $names = [string[]]@('mot_a_S01_idle.mot', 'mot_a_S02_idle.mot', 'mot_a_S01_walk.mot')
        $cases = @(
            @('mot_a_S02_idle.mot', 'SVT_0001_S02_MDL'), @('mot_a_S01_idle.mot', 'SVT_0001_S02_MDL'), @('mot_a_S01_walk.mot', 'SVT_0001_S02_MDL'),
            @('mot_a_S03_idle.mot', 'SVT_0001_S02_MDL'), @('mot_a_S03_idle.mot', ''), @('mot_a_idle.mot', 'SVT_0001_S02_MDL'))
        $seen = ($cases | ForEach-Object { Invoke-Static $catalog 'MatchesModel' @($_[0], $_[1], $names) }) -join ','
        Test-Result 'motions are kept to the model''s form' ($seen -ceq 'True,False,True,False,True,True') $seen
    }
    catch { Test-Result 'motions are kept to the model''s form' $false ($_ | Out-String) }

    # Backdrop block: created by the panel for his graphics layer, 'FPGB' version 1, 36 bytes, RGB as 0..1.
    try {
        $backdropType = $launcher.GetType('FGOLocalPlatform.PhotoBackdropView')
        $backdrop = $backdropType::new()
        $backdrop.Refresh([Nullable[int]]$PID, $true)
        (Get-Field $backdrop 'enabled').IsChecked = $true
        $rgb = Get-Field $backdrop 'rgb'
        $rgb[0].Value = 51
        $rgb[1].Value = 102
        $rgb[2].Value = 255
        $map = [IO.MemoryMappedFiles.MemoryMappedFile]::OpenExisting("Local\FGOLocalPhotoBackdrop1_$PID")
        $view = $map.CreateViewAccessor(0, 36)
        $sequence = $view.ReadInt32(8)
        $seen = "magic=$($view.ReadInt32(0)) version=$($view.ReadInt32(4)) on=$($view.ReadInt32(12)) rgb=$($view.ReadSingle(16)),$($view.ReadSingle(20)),$($view.ReadSingle(24)) sequence even=$($sequence -gt 0 -and $sequence % 2 -eq 0)"
        $saved = [Text.Json.Nodes.JsonObject]::new()
        $backdrop.Save($saved)
        $backdrop.Refresh([Nullable[int]]$PID, $false)
        $off = $view.ReadInt32(12)
        $view.Dispose(); $map.Dispose(); $backdrop.Dispose()
        $loaded = $backdropType::new()
        $loaded.Load([Text.Json.Nodes.JsonNode]::Parse('{"backdropRgb":[1,2,300]}'))
        $back = (@(Get-Field $loaded 'rgb') | ForEach-Object { $_.Value }) -join ','
        $seen += "; saved $($saved.ToJsonString()); off when inactive=$off; loaded $back"
        Test-Result 'Backdrop block written as his panel writes it' ($seen -ceq 'magic=1111969862 version=1 on=1 rgb=0.2,0.4,1 sequence even=True; saved {"backdropRgb":[51,102,255]}; off when inactive=0; loaded 1,2,255') $seen
    }
    catch { Test-Result 'Backdrop block written as his panel writes it' $false ($_ | Out-String) }

    # Style block: created by the panel for his graphics layer, 'FGPS' version 2, 72 bytes, nine values from 16.
    try {
        $styleType = $launcher.GetType('FGOLocalPlatform.PhotoStyleView')
        $style = $styleType::new()
        $style.Refresh([Nullable[int]]$PID, $true)
        (Get-Field $style 'shading').IsChecked = $true
        $values = Get-Field $style 'values'
        $values[0].Value = 0.5
        $values[4].Value = 2
        $values[6].Value = 0.25
        (Get-Field $style 'lines').IsChecked = $true
        $map = [IO.MemoryMappedFiles.MemoryMappedFile]::OpenExisting("Local\FGOLocalPhotoStyle2_$PID")
        $view = $map.CreateViewAccessor(0, 72)
        $sequence = $view.ReadInt32(8)
        $nine = (0..8 | ForEach-Object { $view.ReadSingle(16 + $_ * 4) }) -join ','
        $seen = "magic=$($view.ReadInt32(0)) version=$($view.ReadInt32(4)) shading=$($view.ReadInt32(12)) values=$nine lines=$($view.ReadInt32(52)) sequence even=$($sequence -gt 0 -and $sequence % 2 -eq 0) hosts backdrop=$($null -ne $style.Backdrop -and $style.Children.Contains($style.Backdrop))"
        $style.Refresh([Nullable[int]]$PID, $false)
        $seen += "; inactive shading=$($view.ReadInt32(12)) lines=$($view.ReadInt32(52))"
        $view.Dispose(); $map.Dispose(); $style.Dispose()
        Test-Result 'Style block written as his panel writes it' ($seen -ceq 'magic=1397770054 version=2 shading=1 values=0.5,0,1,1,2,1,0.25,0,0 lines=1 sequence even=True hosts backdrop=True; inactive shading=0 lines=0') $seen
    }
    catch { Test-Result 'Style block written as his panel writes it' $false ($_ | Out-String) }

    # Refresh throttle: the character panels refresh while active, on the tick after, and on a new game process.
    try {
        $state = [Activator]::CreateInstance($launcher.GetType('FGOLocalPlatform.PhotoRefreshState'), $true)
        $steps = @(@(1, $false), @(1, $false), @(1, $true), @(1, $false), @(1, $false), @(2, $false), @($null, $false)) |
            ForEach-Object { $state.GetType().GetMethod('ShouldRefresh', [Reflection.BindingFlags]'NonPublic, Instance').Invoke($state, [object[]]@($_[0], $_[1])) }
        $seen = ($steps | ForEach-Object { [int]$_ }) -join ''
        Test-Result 'Character refresh skipped while inactive on the same game' ($seen -ceq '1011011') $seen
    }
    catch { Test-Result 'Character refresh skipped while inactive on the same game' $false ($_ | Out-String) }

    # Camera speed reaches the game block on the first connect; the overlay's cursor is the arrow.
    try {
        $block = New-Block 'FGOLocalPhoto' 80 1347372870 2
        $owner = [Windows.Window]::new()
        [void][Windows.Interop.WindowInteropHelper]::new($owner).EnsureHandle()
        $photo = $launcher.GetType('FGOLocalPlatform.PhotoWindow').GetConstructors()[0].Invoke([object[]]@($owner, [string](Join-Path $work 'photo.json'), [object]$PID))
        $speed = $block.ReadSingle(44)
        $create = $photo.GetType().GetMethod('CreateOverlay', [Reflection.BindingFlags]'NonPublic, Instance')
        [void]$create.Invoke($photo, @([IntPtr]::Zero))
        $cursor = (Get-Field $photo 'overlay').Cursor
        Test-Result 'Camera speed written on the first connect' ($speed -eq 1.0) "speed $speed"
        Test-Result 'Overlay cursor is the arrow' ([object]::ReferenceEquals($cursor, [Windows.Input.Cursors]::Arrow)) "$cursor"
        $owner.Close()
    }
    catch { Test-Result 'Camera speed written on the first connect' $false ($_ | Out-String) }
}
catch {
    Test-Result 'run' $false ($_ | Out-String)
}
finally {
    foreach ($name in @($script:blocks.Keys)) { Close-Block $name }
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
$sink = Get-DialogSink
Test-Result 'no window shown' ($sink.Shown -eq 0) $sink.Detail
if ($script:failed -gt 0) { Write-Host "$($script:failed) check(s) failed."; exit 1 }
Write-Host 'Every check passed.'
exit 0
