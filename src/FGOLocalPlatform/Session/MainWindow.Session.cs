using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	private bool launcherProcessRunning;

	private bool playStarting;

	private CapturedProcess? runningLauncher;

	private CommunityTunnel? communityTunnel;

	private InputLayer? inputLayer;

	/// <summary>True while the game's connections run through this launcher's community tunnel.</summary>
	public bool CommunityTunnelRunning => communityTunnel is { Running: true };

	private bool launcherCancellationRequested;

	/// <summary>Why the last Play did not reach the game, shown until the next Play.</summary>
	private string? playProblem;

	/// <summary>True when the session last started or starting is Play Online's.</summary>
	private bool sessionOnline;

	/// <summary>
	/// Reading MainModule opens the process and walks its module list, which is far too much to do
	/// every two seconds, so the answer is remembered per process id. In the normal case there is no
	/// ago.exe at all and the check costs one process enumeration.
	/// </summary>
	private static readonly Dictionary<int, bool> GameProcessCache = new Dictionary<int, bool>();

	private static bool IsThisGameRunning()
	{
		string b = Path.Combine(GamePaths.GameRoot, "ago.exe");
		Process[] processesByName = Process.GetProcessesByName("ago");
		if (processesByName.Length == 0)
		{
			GameProcessCache.Clear();
			return false;
		}
		bool found = false;
		HashSet<int> alive = new HashSet<int>();
		foreach (Process process in processesByName)
		{
			using (process)
			{
				alive.Add(process.Id);
				if (!GameProcessCache.TryGetValue(process.Id, out var isOurs))
				{
					isOurs = false;
					try
					{
						isOurs = string.Equals(process.MainModule?.FileName, b, StringComparison.OrdinalIgnoreCase);
						GameProcessCache[process.Id] = isOurs;
					}
					catch (Exception ex) when (((ex is Win32Exception || ex is InvalidOperationException) ? 1 : 0) != 0)
					{
						// The module list is not readable while the game is still starting; ask again next tick.
					}
				}
				found |= isOurs;
			}
		}
		foreach (int gone in GameProcessCache.Keys.Where((int id) => !alive.Contains(id)).ToList())
		{
			GameProcessCache.Remove(gone);
		}
		return found;
	}

	private async void StartGameButton_OnClick(object sender, RoutedEventArgs e)
	{
		await StartSessionAsync(online: false);
	}

	/// <summary>Play on the local server, or Play Online through the tunnel after the probe; refused while a session starts or runs.</summary>
	private async Task StartSessionAsync(bool online)
	{
		if (playStarting || launcherProcessRunning || serverConfiguring || stoppingServer || windowClosing)
		{
			return;
		}
		// A Recover Account request rotates the code the tunnel would start with.
		if (online && communityBusy)
		{
			HoldOnlineStatus("An account request is still running. Press Play Online again when it has finished.");
			return;
		}
		sessionOnline = online;
		if (online)
		{
			onlineProblem = null;
		}
		else
		{
			playProblem = null;
		}
		whatsNewBody = null;
		PublishDeck();
		if (!SaveLauncherSettings(out string displayMode, out int width, out int height, out string inputMode, out int targetFps) || (cardCollection.SelectedCards.Count == 0 && ThemedMessageBox.Show("The deck is empty. Start the game anyway?", "FGOAC scooby", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes))
		{
			return;
		}
		string? gpuPreferenceProblem = null;
		try
		{
			ApplyGpuPreference();
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException)
		{
			gpuPreferenceProblem = ex.Message;
		}
		// The stock hook rereads battle-debug.ini about once a second while the game runs.
		string? battleDebugProblem = null;
		DebugPanel.OnlineSession = online;
		try
		{
			BattleDebug.WriteForSession(GamePaths.GameRoot, online);
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
		{
			if (online)
			{
				ShowPlayProblem("Play Online turns every Debug switch off, and App\\battle-debug.ini could not be written: " + ex.Message.TrimEnd('.') + ". Close any program that has it open, then press Play Online again.");
				return;
			}
			battleDebugProblem = ex.Message;
		}
		LaunchScript.Choice launch = LaunchScript.Prepare(GamePaths.GameRoot);
		string launcher = launch.Script;
		CommunityTunnel? tunnel;
		// Held across the awaits below, where the status tick can enable Play again before the launch starts.
		playStarting = true;
		try
		{
			if (online && !await EnsureCommunityAccountAsync())
			{
				return;
			}
			if (!File.Exists(launcher))
			{
				ShowPlayProblem(launch.Own ? "The launch script App\\FGOAC_Launcher.ps1 is missing. Unzip the FGOAC scooby package into the game folder again, then press Play." : "Cloud23333's launch script App\\FGO_Launcher.ps1 is missing. Run his updater again, then press Play.");
				return;
			}
			try
			{
				// Off the UI thread: Start looks the server's address up.
				tunnel = online ? await Task.Run(StartCommunityTunnel) : null;
			}
			catch (Exception ex) when (ex is IOException || ex is ArgumentException || ex is UnauthorizedAccessException)
			{
				ShowPlayProblem(ex.Message);
				return;
			}
			if (tunnel != null)
			{
				string? refusal = await ProbeSessionTunnelAsync(tunnel);
				if (refusal != null)
				{
					tunnel.Dispose();
					ShowPlayProblem(refusal);
					return;
				}
			}
		}
		finally
		{
			playStarting = false;
		}
		communityTunnel = tunnel;
		launcherCancellationRequested = false;
		launcherProcessRunning = true;
		StopGameButton.IsEnabled = true;
		OnlineStopGameButton.IsEnabled = true;
		StartGameButton.IsEnabled = false;
		PlayOnlineButton.IsEnabled = false;
		SaveLastPlay(online);
		ShowSessionStatus($"Starting the game, {displayMode} {width} x {height} at {targetFps} FPS. The title screen appears in about a minute." + ((launch.Note == null) ? "" : (" " + launch.Note)), "busy");
		BeginLauncherOutputCapture(launcher);
		if (launch.Note != null)
		{
			QueueLauncherOutput("[launcher] " + launch.Note, standardError: true);
		}
		if (gpuPreferenceProblem != null)
		{
			QueueLauncherOutput("[launcher] The GPU preference could not be set: " + gpuPreferenceProblem, standardError: true);
		}
		if (battleDebugProblem != null)
		{
			QueueLauncherOutput("[launcher] The Debug switches could not be written to App\\battle-debug.ini: " + battleDebugProblem, standardError: true);
		}
		if (tunnel != null)
		{
			CommunityServer.Settings community = tunnel.Settings;
			QueueLauncherOutput($"[tunnel] Carrying the game's connections from 127.0.0.1 ports {community.Http}, {community.Billing}, {community.Aime} and {community.Ms} to {community.Host}:{community.TlsPort} over TLS, and UDP {community.Ping} and {community.Dist} to UDP {community.UdpPingPort} and {community.UdpDistPort}.", standardError: false);
		}
		CapturedProcess launcherProcess;
		try
		{
			QueueLauncherOutput("[launcher] Checking the PowerShell runtime...", standardError: false);
			await Task.Run(() => PowerShellHost.Executable).WaitAsync(TimeSpan.FromSeconds(15.0));
			if (launcherCancellationRequested || windowClosing)
			{
				tunnel?.Dispose();
				launcherProcessRunning = false;
				CompleteLauncherOutputCapture(0);
				await RefreshRuntimeStatusAsync();
				return;
			}
			QueueLauncherOutput("[launcher] Runtime ready, starting the game script...", standardError: false);
			List<string> arguments = new List<string> { "-DisplayMode", displayMode, "-ResolutionWidth", width.ToString(), "-ResolutionHeight", height.ToString(), "-InputMode", inputMode, "-TargetFps", targetFps.ToString() };
			// The script plays Online exactly when this is passed; one from before the switch fails on it, and so does his.
			if (tunnel != null && launch.Own)
			{
				arguments.Add("-CommunityServer");
			}
			launcherProcess = StartCapturedPowerShellScript(launcher, QueueLauncherOutput, arguments.ToArray());
		}
		catch (Exception ex)
		{
			tunnel?.Dispose();
			launcherProcessRunning = false;
			QueueLauncherOutput("[launcher] Could not start PowerShell: " + ex.Message, standardError: true);
			CompleteLauncherOutputCapture(-1);
			ShowPlayProblem("The game script could not start: " + ex.Message.TrimEnd('.') + ". Open Logs for the full output, then press Play again.");
			return;
		}
		runningLauncher = launcherProcess;
		inputLayer = InputLayer.StartForSession(GamePaths.GameRoot, ReportBindings);
		// Watches the game script until it exits; nothing here waits for that.
		_ = MonitorLauncherAsync(launcherProcess, tunnel);
	}

	/// <summary>Why the session's Play did not reach the game: its page's Status keeps saying it until that Play is pressed again.</summary>
	private void ShowPlayProblem(string text)
	{
		if (sessionOnline)
		{
			onlineProblem = text;
		}
		else
		{
			playProblem = text;
		}
		ShowSessionStatus(text, "problem");
	}

	/// <summary>Writes the Status of the page whose Play started the session.</summary>
	private void ShowSessionStatus(string text, string tone)
	{
		if (sessionOnline)
		{
			ShowOnlineStatus(text, tone);
		}
		else
		{
			RuntimeStatusText.Text = text;
			SetStatusTone(tone);
		}
	}

	/// <summary>Keeps which Play started the last session in communityServer.enabled: the page the launcher opens on and the start-up checks follow it.</summary>
	private void SaveLastPlay(bool online)
	{
		CommunityServer.Settings saved = CommunityServer.Load(GamePaths.GameRoot);
		if (saved.Enabled == online)
		{
			return;
		}
		try
		{
			CommunityServer.Save(GamePaths.GameRoot, saved with { Enabled = online });
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException || ex is InvalidDataException)
		{
			AppendServerControlLog("The last Play was not saved: " + ex.Message);
		}
	}

	/// <summary>The running tunnel for Play Online, on the saved server with this PC's card.</summary>
	private CommunityTunnel StartCommunityTunnel()
	{
		CommunityServer.Settings settings = SavedServer();
		string code = CommunityServer.ReadCard(GamePaths.GameRoot, settings.Host) ?? throw new IOException("This PC has no account on " + settings.Host + " yet. Choose New account on the Online page, or Recover account if you have a recovery key.");
		return CommunityTunnel.Start(settings, code, ReportTunnel);
	}

	/// <summary>Tunnel lines reach the Game log in the Logs drawer and logs\server-control.log; a problem also takes the status line.</summary>
	private void ReportTunnel(string message, bool problem)
	{
		QueueLauncherOutput("[tunnel] " + message, problem);
		AppendServerControlLog("[tunnel] " + message);
		if (problem)
		{
			Dispatcher.InvokeAsync(() => HoldOnlineStatus(message));
		}
	}

	/// <summary>Input layer lines reach the Game log and logs\server-control.log.</summary>
	private void ReportBindings(string message)
	{
		QueueLauncherOutput("[bindings] " + message, standardError: false);
		AppendServerControlLog("[bindings] " + message);
	}

	private async Task MonitorLauncherAsync(CapturedProcess launcherProcess, CommunityTunnel? tunnel)
	{
		int exitCode = -1;
		_ = PatchCabinetLeftoversAsync(launcherProcess);
		try
		{
			while (!launcherProcess.Process.HasExited)
			{
				await Task.Delay(200);
			}
			if (await Task.WhenAny(launcherProcess.StreamsCompleted, Task.Delay(TimeSpan.FromSeconds(3.0))) == launcherProcess.StreamsCompleted)
			{
				await launcherProcess.StreamsCompleted;
			}
			else
			{
				QueueLauncherOutput("[launcher] Timed out waiting for stdout / stderr to finish - the last few lines may be incomplete.", standardError: true);
			}
			exitCode = launcherProcess.Process.ExitCode;
		}
		catch (Exception ex)
		{
			QueueLauncherOutput("[launcher] Could not monitor the launcher: " + ex.Message, standardError: true);
		}
		finally
		{
			tunnel?.Dispose();
			inputLayer?.Dispose();
			inputLayer = null;
			if (runningLauncher == launcherProcess)
			{
				runningLauncher = null;
			}
			launcherProcess.Process.Dispose();
			launcherProcessRunning = false;
			CompleteLauncherOutputCapture(exitCode);
		}
		await RefreshRuntimeStatusAsync();
		if (exitCode != 0 && !launcherCancellationRequested && !windowClosing)
		{
			string text = $"The game stopped with code {exitCode}. {StartupDiagnostics.Explain(exitCode)} Open Logs for the full output.";
			ShowPlayProblem(text);
			QueueLauncherOutput(text, standardError: true);
		}
		// Every session ends with a look for a newer release, which the Status then offers.
		if (!windowClosing)
		{
			await CheckForUpdateAsync(announce: false);
		}
	}

	private void CancelPendingLauncher()
	{
		launcherCancellationRequested = true;
		try
		{
			if (runningLauncher != null && !runningLauncher.Process.HasExited)
			{
				runningLauncher.Process.Kill();
			}
		}
		catch (Exception ex) when (((ex is Win32Exception || ex is InvalidOperationException) ? 1 : 0) != 0)
		{
			AppendServerControlLog(ex.Message);
		}
	}

	private async void StopGameButton_OnClick(object sender, RoutedEventArgs e)
	{
		// The game is in front when this is asked, so the question has to come with it.
		if (ThemedMessageBox.Show(this, "Stop the current game session?", "FGOAC scooby", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.Yes, foreground: true) != MessageBoxResult.Yes)
		{
			return;
		}
		CancelPendingLauncher();
		// With the separator, a sibling folder such as AppBackup does not match.
		string gameRoot = Path.GetFullPath(GamePaths.GameRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
		Process[] processesByName = Process.GetProcessesByName("ago");
		foreach (Process process in processesByName)
		{
			try
			{
				ProcessModule? mainModule = process.MainModule;
				if (mainModule != null && mainModule.FileName?.StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase) == true)
				{
					process.CloseMainWindow();
				}
			}
			catch
			{
			}
		}
		await Task.Delay(3000);
		string[] array = new string[3] { "ago", "amdaemon", "inject" };
		for (int i = 0; i < array.Length; i++)
		{
			processesByName = Process.GetProcessesByName(array[i]);
			foreach (Process process2 in processesByName)
			{
				try
				{
					ProcessModule? mainModule2 = process2.MainModule;
					if (mainModule2 != null && mainModule2.FileName?.StartsWith(gameRoot, StringComparison.OrdinalIgnoreCase) == true)
					{
						process2.Kill(entireProcessTree: true);
					}
				}
				catch
				{
				}
			}
		}
		await RefreshRuntimeStatusAsync();
	}
}
