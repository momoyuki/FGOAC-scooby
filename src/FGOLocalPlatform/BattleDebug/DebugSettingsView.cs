using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media.Imaging;

namespace FGOLocalPlatform;

public partial class DebugSettingsView : UserControl, IComponentConnector
{
	private readonly string appFolder;

	private bool ready;

	private Dictionary<string, CheckBox> options = new Dictionary<string, CheckBox>();

	/// <summary>The PvP maps of Cloud23333's map pick; none on game 11.00, whose hook has no map pick.</summary>
	private readonly IReadOnlyList<PvpStages.Stage> stages = Array.Empty<PvpStages.Stage>();

	/// <summary>True from Play Online until the next Play: a save then keeps battle-debug.ini as that Play wrote it, every switch 0.</summary>
	public bool OnlineSession { get; set; }

	public DebugSettingsView()
		: this(GamePaths.GameRoot)
	{
	}

	public DebugSettingsView(string appFolder)
	{
		this.appFolder = appFolder;
		InitializeComponent();
		StopTimerHookNote.Visibility = StockHook.Reads(appFolder, "stop_timer") ? Visibility.Collapsed : Visibility.Visible;
		options = new Dictionary<string, CheckBox>
		{
			["attack_stun"] = AttackStun,
			["one_hit"] = OneHit,
			["full_np"] = FullNp,
			["instant_noble"] = InstantNoble,
			["invincible"] = Invincible,
			["stop_timer"] = StopTimer
		};
		try
		{
			if (!GameBuild.Is1100(Path.GetFullPath(Path.Combine(appFolder, ".."))))
			{
				HisSwitchesPanel.Visibility = Visibility.Visible;
				options["auto_clear"] = AutoClear;
				options["cost_233"] = Cost233;
				options["pvp_select"] = PvpSelect;
				stages = PvpStages.Read(appFolder);
				PvpStage.ItemsSource = stages;
				int saved = BattleDebug.PvpStage(appFolder);
				PvpStage.SelectedItem = stages.FirstOrDefault((PvpStages.Stage stage) => stage.StageId == saved) ?? stages.FirstOrDefault();
				ShowPvpPreview();
			}
			IReadOnlySet<string> on = BattleDebug.Load(appFolder);
			foreach (KeyValuePair<string, CheckBox> option in options)
			{
				option.Value.IsChecked = on.Contains(option.Key);
			}
			StatusText.Text = "Settings loaded.";
		}
		catch (Exception ex)
		{
			StatusText.Text = "Could not read the presets: " + ex.Message;
		}
		ready = true;
	}

	private void Option_OnChanged(object sender, RoutedEventArgs e)
	{
		if (ready)
		{
			Save();
		}
	}

	public bool Save()
	{
		try
		{
			int pvpStage = (PvpStage.SelectedItem as PvpStages.Stage)?.StageId ?? 0;
			string pvpStages = string.Join(",", stages.Select((PvpStages.Stage stage) => stage.StageId).Distinct());
			BattleDebug.SaveFromCard(appFolder, options.Where((KeyValuePair<string, CheckBox> option) => option.Value.IsChecked == true).Select((KeyValuePair<string, CheckBox> option) => option.Key).ToHashSet(), OnlineSession, pvpStage, pvpStages);
			StatusText.Text = OnlineSession ? "Settings saved for Local play - Play Online keeps every switch off." : "Settings saved. A running game takes them about a second later.";
			return true;
		}
		catch (Exception ex)
		{
			StatusText.Text = "Could not save the presets: " + ex.Message;
			return false;
		}
	}

	private void PvpStage_OnChanged(object sender, SelectionChangedEventArgs e)
	{
		ShowPvpPreview();
		if (ready)
		{
			Save();
		}
	}

	/// <summary>The picked map's preview from the MapPreviews folder his update installs beside App, else from App\1_MOD\MapPreviews.</summary>
	private void ShowPvpPreview()
	{
		PvpPreview.Source = null;
		if (PvpStage.SelectedItem is not PvpStages.Stage stage)
		{
			return;
		}
		string path = Path.GetFullPath(Path.Combine(appFolder, "..", "MapPreviews", stage.StageId + ".png"));
		if (!File.Exists(path))
		{
			path = Path.Combine(appFolder, "1_MOD", "MapPreviews", stage.StageId + ".png");
		}
		if (!File.Exists(path))
		{
			return;
		}
		try
		{
			BitmapImage image = new BitmapImage();
			image.BeginInit();
			image.CacheOption = BitmapCacheOption.OnLoad;
			image.UriSource = new Uri(path);
			image.EndInit();
			PvpPreview.Source = image;
		}
		catch (Exception ex) when (ex is IOException || ex is NotSupportedException || ex is UnauthorizedAccessException)
		{
		}
	}

	private void Save_OnClick(object sender, RoutedEventArgs e)
	{
		Save();
	}

	private void DisableAll_OnClick(object sender, RoutedEventArgs e)
	{
		ready = false;
		foreach (CheckBox value in options.Values)
		{
			value.IsChecked = false;
		}
		ready = true;
		Save();
	}
}
