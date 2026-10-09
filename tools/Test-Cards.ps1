<#
Checks the card data and card folders of Cloud23333's 1.10 update without starting the launcher or the game. Every
file is written under a throwaway folder in %TEMP%, deleted when the check ends.

  pwsh -File tools\Test-Cards.ps1 -CloudTables <his 1.10 launcher's JSON resources> -CloudTables1100 <his 1.02
    launcher's JSON resources> -EnglishSet <an 11.51 install>\App\Localization\en-fgoac

Through the launcher assembly (src\bin\Release, so run build.cmd first):
  - a deck that names FGO11_AllServants or FGO11_51_AllServants, here or in another install, lands on the
    FGO11_51_AllServants folder of an install that has it, as his GamePaths.cs maps it; on game 11.00 a deck whose
    FGO11_AllServants folder has moved lands on the install's own, and one that is still there is kept;
  - the Draw Rates page, built in memory and never shown, lists summon_candidates-11.51.json when his update put it
    there, as his SummonSettingsWindow does, and summon_candidates.json without it;
  - every Servant and Craft Essence of his card-name table (his 1.10 launcher, 11.51) has an English name, and every
    Craft Essence of his effect table has its normal and Max Limit Break effects in English;
  - each card his 1.10 table adds to his 1.02 one (11.00), and each 英霊彩像 card, has the name the game's English set
    gives it;
  - every effect's Japanese is his 1.10 (the 11.51 game's), and each Craft Essence new in 11.51 or whose effect
    changed since 11.00 has the English set's skill details for its normal and Max Limit Break skills.

Exit codes: 0 every check passed, 1 a check failed, 2 a prerequisite is missing.
#>
[CmdletBinding()]
param(
    [string]$CloudTables,
    [string]$CloudTables1100,
    [string]$EnglishSet
)
$ErrorActionPreference = 'Stop'

$repository = Split-Path -Parent $PSScriptRoot
$assembly = Join-Path $repository 'src\bin\Release\net6.0-windows\FGOLocalPlatform.dll'
if ($PSVersionTable.PSVersion.Major -lt 7) { Write-Host 'Run this with pwsh 7: the launcher assembly targets .NET 6.'; exit 2 }
if (!(Test-Path -LiteralPath $assembly)) { Write-Host "Missing $assembly. Run build.cmd first."; exit 2 }
foreach ($name in 'CloudTables', 'CloudTables1100', 'EnglishSet') {
    if (!(Get-Variable $name -ValueOnly)) { Write-Host "Pass -$name."; exit 2 }
}
$hisNames = Join-Path $CloudTables 'FGOLocalPlatform.CardNames.json'
if (!(Test-Path -LiteralPath $hisNames)) { Write-Host "Missing $hisNames."; exit 2 }
$hisCrafts = Join-Path $CloudTables 'FGOLocalPlatform.CraftEffects.json'
if (!(Test-Path -LiteralPath $hisCrafts)) { Write-Host "Missing $hisCrafts."; exit 2 }
$oldNames = Join-Path $CloudTables1100 'FGOLocalPlatform.CardNames.json'
$oldCrafts = Join-Path $CloudTables1100 'FGOLocalPlatform.CraftEffects.json'
$englishCrafts = Join-Path $EnglishSet 'rom\mst_data\arms_mst_craft_essence.farc'
$englishSkills = Join-Path $EnglishSet 'rom\mst_data\arms_mst_skill.farc'
foreach ($path in @($oldNames, $oldCrafts, $englishCrafts, $englishSkills)) {
    if (!(Test-Path -LiteralPath $path)) { Write-Host "Missing $path."; exit 2 }
}
if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne 'STA') { Write-Host 'Run this with pwsh -STA: the Draw Rates page is a WPF control.'; exit 2 }
# Every message box, dialog and window of the launcher goes to the sink, never to the screen.
. (Join-Path $PSScriptRoot 'DialogSink.ps1'); Enable-DialogSink $assembly

$script:failed = 0
function Test-Result([string]$Name, [bool]$Passed, [string]$Detail) {
    Write-Host ("{0} {1}: {2}" -f $(if ($Passed) { 'PASS' } else { 'FAIL' }), $Name, $Detail)
    if (!$Passed) { $script:failed++ }
}

$work = Join-Path ([IO.Path]::GetTempPath()) ('fgoac-cards-' + [guid]::NewGuid().ToString('N'))
try {
    $launcher = [Reflection.Assembly]::LoadFrom($assembly)
    $card = '00001_CE00001_A00_NORMAL.bmp'

    # A deck config as deck.json holds it, resolved against the install's App folder.
    function Resolve-Deck([string]$App, [string]$CardsPath, [string]$Selected) {
        $selection = [Collections.Generic.List[string]]::new()
        $selection.Add($Selected)
        $config = [DeckReaderUI.Config]::new($selection, $CardsPath)
        [FGOLocalPlatform.GamePaths]::ResolveMovedCards($config, $App)
        return $config
    }

    $install = Join-Path $work 'install-1151'
    $app = Join-Path $install 'App'
    $latest = Join-Path $install 'DEVICE\print\FGO11_51_AllServants'
    New-Item -ItemType Directory -Path $app, $latest -Force | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $latest $card), [byte[]](1..4))
    $want = '..\DEVICE\print\FGO11_51_AllServants'
    $cases = [ordered]@{
        'the 11.00 folder name'             = '..\DEVICE\print\FGO11_AllServants'
        'the 11.51 folder of another install' = 'D:\elsewhere\DEVICE\print\FGO11_51_AllServants'
        'the 11.00 folder of another install' = 'D:\elsewhere\DEVICE\print\FGO11_AllServants\'
    }
    foreach ($case in $cases.GetEnumerator()) {
        $config = Resolve-Deck $app $case.Value (Join-Path $case.Value.TrimEnd('\') $card)
        Test-Result "11.51 install, deck naming $($case.Key)" ($config.CardsPath -ceq $want -and $config.SelectedCards[0] -ceq "$want\$card") "$($config.CardsPath) | $($config.SelectedCards[0])"
    }
    # His update leaves the 11.00 folder in place; a deck naming it still moves to the 11.51 one.
    $old = Join-Path $install 'DEVICE\print\FGO11_AllServants'
    New-Item -ItemType Directory -Path $old -Force | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $old $card), [byte[]](1..4))
    $config = Resolve-Deck $app '..\DEVICE\print\FGO11_AllServants' "..\DEVICE\print\FGO11_AllServants\$card"
    Test-Result '11.51 install with the 11.00 folder still there' ($config.CardsPath -ceq $want -and $config.SelectedCards[0] -ceq "$want\$card") "$($config.CardsPath) | $($config.SelectedCards[0])"
    $config = Resolve-Deck $app '..\DEVICE\print\FGO11_51_AllServants' "..\DEVICE\print\FGO11_51_AllServants\00002_CE00002_A00_NORMAL.bmp"
    Test-Result '11.51 install, a card the folder lacks is kept' ($config.CardsPath -ceq $want -and $config.SelectedCards[0] -ceq "$want\00002_CE00002_A00_NORMAL.bmp") "$($config.CardsPath) | $($config.SelectedCards[0])"

    $install = Join-Path $work 'install-1100'
    $app = Join-Path $install 'App'
    $own = Join-Path $install 'DEVICE\print\FGO11_AllServants'
    New-Item -ItemType Directory -Path $app, $own -Force | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $own $card), [byte[]](1..4))
    $moved = 'D:\gone\DEVICE\print\FGO11_AllServants'
    $config = Resolve-Deck $app $moved "$moved\$card"
    Test-Result '11.00 install, a moved 11.00 folder lands on its own' ($config.CardsPath -ceq '..\DEVICE\print\FGO11_AllServants' -and $config.SelectedCards[0] -ceq "..\DEVICE\print\FGO11_AllServants\$card") "$($config.CardsPath) | $($config.SelectedCards[0])"
    $kept = Join-Path $work 'other\DEVICE\print\FGO11_AllServants'
    New-Item -ItemType Directory -Path $kept -Force | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $kept $card), [byte[]](1..4))
    $config = Resolve-Deck $app $kept "$kept\$card"
    Test-Result '11.00 install, a 11.00 folder still there is kept' ($config.CardsPath -ceq $kept -and $config.SelectedCards[0] -ceq "$kept\$card") "$($config.CardsPath) | $($config.SelectedCards[0])"

    # The Draw Rates page reads the 11.51 card list when his update put it there, else the 11.00 one.
    $install = Join-Path $work 'install-draw'
    $app = Join-Path $install 'App'
    $data = Join-Path $install 'Server\artemis\titles\fgo\data'
    New-Item -ItemType Directory -Path $app, $data -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $app 'deck.json') -Value '{"CardsPath": "..\\DEVICE\\print\\FGO11_51_AllServants", "SelectedCards": []}'
    function Write-Candidates([string]$Name, [int]$Count) {
        $cards = @(1..$Count | ForEach-Object { @{ tc_id = $_; type = 2; entity_id = $_; name = "$Name $_"; rarity = 5; category = 'regular'; holo_type = 0; acquisition_notes = @(); file_name = "card$_.bmp" } })
        Set-Content -LiteralPath (Join-Path $data $Name) -Value (@{ cards = $cards } | ConvertTo-Json -Depth 4)
    }
    function Get-DrawCards {
        $page = [FGOLocalPlatform.SummonSettingsWindow]::new($app, (Join-Path $install 'config\fgo_summon_weights.json'))
        $field = $page.GetType().GetField('cards', [Reflection.BindingFlags]'NonPublic, Instance')
        return @($field.GetValue($page) | ForEach-Object { $_.Name })
    }
    $application = [Activator]::CreateInstance($launcher.GetType('FGOLocalPlatform.App'))
    $application.InitializeComponent()
    Write-Candidates 'summon_candidates.json' 2
    Write-Candidates 'summon_candidates-11.51.json' 3
    $names = Get-DrawCards
    Test-Result 'Draw Rates reads summon_candidates-11.51.json first' ($names.Count -eq 3 -and @($names | Where-Object { $_ -like 'summon_candidates-11.51.json *' }).Count -eq 3) ($names -join ', ')
    Remove-Item -LiteralPath (Join-Path $data 'summon_candidates-11.51.json')
    $names = Get-DrawCards
    Test-Result 'Draw Rates reads summon_candidates.json without it' ($names.Count -eq 2 -and @($names | Where-Object { $_ -like 'summon_candidates.json *' }).Count -eq 2) ($names -join ', ')

    # Japanese script in a text meant to be English.
    $japanese = '[\p{IsHiragana}\p{IsKatakana}\p{IsCJKUnifiedIdeographs}]'
    $cardNames = $launcher.GetType('FGOLocalPlatform.CardNames', $true)
    $ids = @((Get-Content -LiteralPath $hisNames -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable).Keys)
    $unnamed = @($ids | Where-Object {
        $english = $cardNames.GetMethod('English').Invoke($null, [object[]]@($(if ($_ -like 'SVT*') { 1 } else { 2 }), [int]($_ -replace '^\D+', '')))
        [string]::IsNullOrWhiteSpace($english) -or $english -match $japanese
    } | Sort-Object)
    Test-Result 'every card of the 11.51 table has an English name' ($ids.Count -gt 1384 -and $unnamed.Count -eq 0) "$($ids.Count) ids; without one $($unnamed.Count): $($unnamed -join ' ')"

    # One table of a game mst archive, read through the launcher's own FARC reader: row index -> field -> value.
    function Read-GameTable([string]$Farc, [string]$Table) {
        $archive = [Hanhua.Archive]::new([IO.File]::ReadAllBytes($Farc))
        $entry = @($archive.Entries | Where-Object { $_.Name -ceq "arms_mst_$Table.bin" })[0]
        $rows = @{}
        foreach ($line in ([Text.Encoding]::UTF8.GetString($archive.Read($entry)) -split "`r?`n")) {
            if ($line -match "^$Table\.(\d+)\.([^=]+)=(.*)$") {
                if (!$rows.ContainsKey($Matches[1])) { $rows[$Matches[1]] = @{} }
                $rows[$Matches[1]][$Matches[2]] = $Matches[3]
            }
        }
        return $rows
    }
    $englishCraft = @{}
    foreach ($row in (Read-GameTable $englishCrafts 'craft_essence').Values) { $englishCraft['CE{0:D5}' -f [int]$row['id']] = $row }
    $before = Get-Content -LiteralPath $oldNames -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable
    $added = @($ids | Where-Object { !$before.ContainsKey($_) })
    $otherName = @($added | Where-Object {
        $cardNames.GetMethod('English').Invoke($null, [object[]]@(2, [int]($_ -replace '^\D+', ''))) -cne $englishCraft[$_]['name']
    } | Sort-Object)
    Test-Result 'each card new in 11.51 has the English set name' ($added.Count -eq 198 -and $otherName.Count -eq 0) "$($added.Count) new; another name $($otherName.Count): $($otherName -join ' ')"
    $hisNameRows = Get-Content -LiteralPath $hisNames -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable
    $painted = @($ids | Where-Object { $hisNameRows[$_]['Japanese'].StartsWith('英霊彩像') })
    $otherPainted = @($painted | Where-Object {
        $cardNames.GetMethod('English').Invoke($null, [object[]]@(2, [int]($_ -replace '^\D+', ''))) -cne $englishCraft[$_]['name']
    } | Sort-Object)
    Test-Result 'each 英霊彩像 card has the English set name' ($painted.Count -eq 31 -and $otherPainted.Count -eq 0) "$($painted.Count) cards; another name $($otherPainted.Count): $($otherPainted -join ' ')"

    # Each effect his table has a Japanese text for has an English one here.
    $craftEffects = $launcher.GetType('FGOLocalPlatform.CraftEffects', $true)
    $hisEffects = Get-Content -LiteralPath $hisCrafts -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable
    $untranslated = @($hisEffects.Keys | Where-Object {
        $his = $hisEffects[$_]
        $effect = $craftEffects.GetMethod('Get').Invoke($null, [object[]]@([string]$_))
        $null -eq $effect -or
            ($his.NormalJapanese -ne '' -and [string]::IsNullOrWhiteSpace($effect.Normal)) -or ($his.MaximumJapanese -ne '' -and [string]::IsNullOrWhiteSpace($effect.Maximum)) -or
            $effect.Normal -match $japanese -or $effect.Maximum -match $japanese
    } | Sort-Object)
    Test-Result 'every Craft Essence of the 11.51 table has its effects in English' ($hisEffects.Count -gt 1264 -and $untranslated.Count -eq 0) "$($hisEffects.Count) ids; without them $($untranslated.Count): $($untranslated -join ' ')"

    # His 1.10 Japanese is the 11.51 game's; a Craft Essence new in 11.51, or whose Japanese changed since his 1.02 (11.00),
    # has the game's English set skill details, each [Max Limit Break] written as the table's [MAX].
    $oldEffects = Get-Content -LiteralPath $oldCrafts -Raw -Encoding utf8 | ConvertFrom-Json -AsHashtable
    $englishDetail = @{}
    foreach ($row in (Read-GameTable $englishSkills 'skill_detail').Values) { $englishDetail[$row['id']] = [string]$row['detail'] }
    $otherJapanese = @()
    $otherEnglish = @()
    $renewed = 0
    foreach ($key in $hisEffects.Keys) {
        $his = $hisEffects[$key]
        $effect = $craftEffects.GetMethod('Get').Invoke($null, [object[]]@([string]$key))
        if ($null -eq $effect -or $effect.NormalJapanese -cne $his.NormalJapanese -or $effect.MaximumJapanese -cne $his.MaximumJapanese) { $otherJapanese += $key }
        $old = $oldEffects[$key]
        if ($null -ne $old -and $old.NormalJapanese -ceq $his.NormalJapanese -and $old.MaximumJapanese -ceq $his.MaximumJapanese) { continue }
        $renewed++
        $normal = $englishDetail[$englishCraft[$key]['normal_skill_id']]
        $maximum = ($englishDetail[$englishCraft[$key]['strong_skill_id']].TrimEnd() -replace '\s*\[Max Limit Break\]$', ' [MAX]') -replace '\[Max Limit Break\]', '[MAX]'
        if ($null -eq $effect -or $effect.Normal -cne $normal -or $effect.Maximum -cne $maximum) { $otherEnglish += $key }
    }
    Test-Result 'every effect has the 11.51 Japanese' ($otherJapanese.Count -eq 0) "another $($otherJapanese.Count): $(@($otherJapanese | Sort-Object) -join ' ')"
    Test-Result 'each effect new or changed in 11.51 is the English set text' ($renewed -eq 217 -and $otherEnglish.Count -eq 0) "$renewed new or changed; another text $($otherEnglish.Count): $(@($otherEnglish | Sort-Object) -join ' ')"
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
