using System;
using System.Collections;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Threading;
using DeckReaderUI;
using DeckReaderUI.Kancolle;

namespace FGOLocalPlatform;

public partial class MainWindow : Window, IComponentConnector, IStyleConnector
{
	private readonly CardCollection cardCollection;

	private readonly ICollectionView cardListView;

	private readonly ICollectionView selectedCardListView;

	private readonly DispatcherTimer statusTimer;

	private bool windowClosing;

	private bool closeReady;

	public MainWindow()
	{
		Config deckConfig = Config.Load(Path.Combine(GamePaths.GameRoot, "deck.json"));
		GamePaths.ResolveMovedCards(deckConfig, GamePaths.GameRoot);
		// The card folder is read while the controls are built; cards hold no WPF objects until a thumbnail is asked for.
		Task<CardCollection> cardScan = Task.Run(() => new CardCollection(new TrcDB("trcdb.json"), deckConfig.CardsPath, deckConfig.SelectedCards, deckConfig.SelectedCardCopies));
		InitializeComponent();
		ConnectAccountsPage();
		ApplyProtectedBranding();
		SidebarVersionText.Text = UpdateSettings.Version;
		AboutVersionText.Text = "Version " + UpdateSettings.Version + ", an English build of the FGO Arcade local platform.";
		// The channel name is hashed from the game folder, the same way the launch script hands it
		// to the hook, so the root has to be right before the channel is opened.
		GameCommunication.GameRoot = GamePaths.GameRoot;
		GameCommunication.Initialize();
		config = deckConfig;
		cardCollection = cardScan.GetAwaiter().GetResult();
		cardListView = CollectionViewSource.GetDefaultView(availableCardStacks);
		selectedCardListView = CollectionViewSource.GetDefaultView(selectedCardStacks);
		cardListItemTemplate = CardList.ItemTemplate;
		cardListItemsPanel = CardList.ItemsPanel;
		cardIconRowTemplate = (DataTemplate)FindResource("CardIconRowTemplate");
		CardList.ItemsSource = (IEnumerable)cardListView;
		SelectedCardList.ItemsSource = (IEnumerable)selectedCardListView;
		CardTypeComboBox.SelectionChanged += CardTypeComboBox_OnSelectionChanged;
		CardList.SizeChanged += CardList_OnSizeChanged;
		CardsPathTextBox.Text = config.CardsPath;
		SearchTextBox.Text = "";
		LoadLauncherSettings();
		LoadLayoutSettings();
		OnlineTab.Visibility = CommunityServer.Offered ? Visibility.Visible : Visibility.Collapsed;
		EasterEggCard.Visibility = EasterEggSettingsView.Offered(GamePaths.GameRoot) ? Visibility.Visible : Visibility.Collapsed;
		// The window opens on the page whose Play started the last session.
		if (CommunityServer.OnlineSession(GamePaths.GameRoot))
		{
			SectionTabs.SelectedItem = OnlineTab;
		}
		try
		{
			PublishDeck();
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is InvalidOperationException)
		{
			// A deck that cannot be published must not keep the window from opening.
			RuntimeStatusText.Text = "The deck could not be saved: " + ex.Message;
		}
		UpdateDeckStatus();
		OwnedCardsOnlyCheckBox.IsChecked = true;
		SetCardViewMode(useIcons: true);
		ApplyOwnedCardFilter();
		launcherOutputTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromMilliseconds(125.0)
		};
		launcherOutputTimer.Tick += delegate
		{
			FlushLauncherOutput();
		};
		statusTimer = new DispatcherTimer
		{
			Interval = TimeSpan.FromSeconds(2.0)
		};
		statusTimer.Tick += async delegate
		{
			// Nothing on screen to update while the window is out of the way.
			if (base.WindowState == WindowState.Minimized)
			{
				return;
			}
			await RefreshRuntimeStatusAsync();
			await RefreshLogPanelsAsync();
			await RefreshAccountsIfChangedAsync();
			ApplyProtectedBranding();
		};
		statusTimer.Start();
		base.Loaded += async delegate
		{
			// Photo mode opens the game's model archives, so it is built when its tab is first
			// shown rather than on every start.
			await RefreshRuntimeStatusAsync();
			await RefreshLogPanelsAsync();
			await RefreshAccountsAsync(showErrors: false);
			bool freshInstall = await RunFirstRunAsync();
			try
			{
				RefreshLoadoutList();
			}
			catch (Exception ex)
			{
				AppendAccountLog("Could not list the deck loadouts: " + ex.Message);
			}
			OfferWhatsNewIfUpdated(freshInstall);
			await RefreshRuntimeStatusAsync();
			await CheckForUpdateAsync(announce: false);
		};
	}

	private async void MainWindow_OnClosing(object? sender, CancelEventArgs e)
	{
		if (closeReady)
		{
			return;
		}
		e.Cancel = true;
		if (windowClosing || (summonSettingsPanel != null && !summonSettingsPanel.DiscardConfirmed()))
		{
			return;
		}
		// The tunnel lives in this process, so closing the window cuts the game off the community server.
		if (communityTunnel is { Running: true } && ThemedMessageBox.Show("The game is connected to the community server " + communityTunnel.Settings.Host + " through this launcher. Closing the launcher now cuts that connection and ends any match in progress.\n\nClose the launcher anyway?", "FGOAC scooby", MessageBoxButton.YesNo, MessageBoxImage.Exclamation, MessageBoxResult.No) != MessageBoxResult.Yes)
		{
			return;
		}
		windowClosing = true;
		statusTimer.Stop();
		launcherOutputTimer.Stop();
		bool gameRunning = IsThisGameRunning();
		string? stopScript = LocalServerStop.CloseScript(GamePaths.GameRoot, gameRunning, AppendServerControlLog);
		RuntimeStatusText.Text = (stopScript == null) ? "Closing the launcher..." : "Closing the launcher and shutting down the local server...";
		try
		{
			SaveLayoutSettings();
			PublishDeck();
			if (!gameRunning)
			{
				CancelPendingLauncher();
				if (stopScript != null)
				{
					await StopLocalServerAsync();
				}
			}
			else if (stopScript != null)
			{
				await Task.Run(() => PowerShellHost.Executable).WaitAsync(TimeSpan.FromSeconds(15.0));
				ProcessStartInfo processStartInfo = PowerShellHost.CreateStartInfo(GamePaths.GameRoot, redirectOutput: false, new string[4] { "-File", stopScript, "-FrontendProcessId", Environment.ProcessId.ToString() });
				using (Process.Start(processStartInfo) ?? throw new IOException("Could not start the server cleanup process"))
				{
				}
			}
		}
		catch (Exception ex)
		{
			AppendServerControlLog("Closing the launcher: " + ex.Message);
			windowClosing = false;
			statusTimer.Start();
			launcherOutputTimer.Start();
			RuntimeStatusText.Text = "The server did not shut down completely and the launcher stayed open: " + ex.Message;
			return;
		}
		closeReady = true;
		// Queued: with nothing awaited above this still runs inside the Closing event, where Close() throws.
		_ = Dispatcher.InvokeAsync(Close);
	}
}
