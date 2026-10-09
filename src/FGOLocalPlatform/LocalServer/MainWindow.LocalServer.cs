using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	private bool serverConfiguring;

	private bool stoppingServer;

	private CancellationTokenSource? serverCommandCancellation;

	private Task<int>? serverCommandTask;

	private Task? stopServerTask;

	private async void ServerSettings_OnConfigure(object? sender, EventArgs e)
	{
		if (serverConfiguring || stoppingServer || windowClosing)
		{
			return;
		}
		if (Process.GetProcessesByName("ago").Length != 0 || launcherProcessRunning)
		{
			ThemedMessageBox.Show("Quit the game before changing the server settings.", "Server Settings");
			return;
		}
		serverConfiguring = true;
		ServerSettingsPanel.IsEnabled = false;
		StartGameButton.IsEnabled = false;
		try
		{
			_ = 5;
			try
			{
				string[] arguments = ServerSettingsPanel.ApplyArguments();
				string[] obj = (string[])arguments.Clone();
				obj[0] = "validate";
				await ServerSettingsView.RunTool(obj);
				Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..", "Server"));
				if (await RunServerCommandAsync(start: false) != 0)
				{
					throw new IOException("The server did not stop, so the settings were not changed - see logs/server-control.log.");
				}
				if (windowClosing)
				{
					goto end_IL_00b4;
				}
				await ServerSettingsView.RunTool(arguments);
				await ServerSettingsPanel.ReloadAsync();
				await RefreshRuntimeStatusAsync();
				await RefreshAccountsAsync(showErrors: false);
				ThemedMessageBox.Show("Server settings saved. The server starts when you click Start Server or Play.", "Server Settings");
				goto end_IL_0091;
				end_IL_00b4:;
			}
			catch (Exception ex)
			{
				if (!windowClosing)
				{
					ThemedMessageBox.Show(ex.Message, "Server Settings", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				}
				goto end_IL_0091;
			}
			end_IL_0091:;
		}
		finally
		{
			serverConfiguring = false;
			ServerSettingsPanel.IsEnabled = true;
			await RefreshRuntimeStatusAsync();
		}
	}

	private async void StartServerButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (serverConfiguring || stoppingServer || windowClosing)
		{
			return;
		}
		string fullPath = Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..", "Server", "Start-FGOLocalServer.ps1"));
		if (!File.Exists(fullPath))
		{
			ThemedMessageBox.Show("The server start script is missing:\n" + fullPath, "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Hand);
			return;
		}
		serverConfiguring = true;
		RuntimeStatusText.Text = "Starting and checking the local server...";
		try
		{
			if (await RunServerCommandAsync(start: true) != 0 && !windowClosing)
			{
				ThemedMessageBox.Show(StartupDiagnostics.Explain(10) + "\n\n" + ReadTail(Path.Combine(GamePaths.LogsRoot, "server-control.log")), "Server Did Not Start", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception ex2)
		{
			if (!windowClosing)
			{
				ThemedMessageBox.Show(ex2.Message, "Server Did Not Start", MessageBoxButton.OK, MessageBoxImage.Exclamation);
			}
		}
		finally
		{
			serverConfiguring = false;
			await RefreshRuntimeStatusAsync();
			if (!windowClosing)
			{
				await RefreshAccountsAsync(showErrors: false);
			}
		}
	}

	private async Task<int> RunServerCommandAsync(bool start)
	{
		if (serverCommandTask != null)
		{
			throw new InvalidOperationException("The previous server control command has not finished.");
		}
		using CancellationTokenSource cancellation = new CancellationTokenSource();
		serverCommandCancellation = cancellation;
		Task<int> task = (serverCommandTask = ExecuteServerCommandAsync(start, cancellation.Token));
		try
		{
			return await task;
		}
		finally
		{
			serverCommandTask = null;
			serverCommandCancellation = null;
		}
	}

	private static async Task<int> ExecuteServerCommandAsync(bool start, CancellationToken cancellation)
	{
		string script = Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..", "Server", start ? "Start-FGOLocalServer.ps1" : "Stop-FGOLocalServer.ps1"));
		if (!File.Exists(script))
		{
			throw new FileNotFoundException("The server control script is missing.", script);
		}
		await Task.Run(() => PowerShellHost.Executable).WaitAsync(TimeSpan.FromSeconds(15.0), cancellation);
		cancellation.ThrowIfCancellationRequested();
		CapturedProcess captured = StartCapturedPowerShellScript(script, delegate(string line, bool error)
		{
			AppendServerControlLog(line);
		});
		using (captured.Process)
		{
			try
			{
				await ProcessCompletion.WaitAsync(captured.Process, TimeSpan.FromSeconds(start ? 120 : 50), cancellation);
				await Task.WhenAny(captured.StreamsCompleted, Task.Delay(1500));
				return captured.Process.ExitCode;
			}
			catch (Exception ex)
			{
				AppendServerControlLog(ex.Message);
				throw;
			}
		}
	}

	private async Task CancelServerCommandAsync()
	{
		Task<int> task = serverCommandTask;
		if (task != null)
		{
			serverCommandCancellation?.Cancel();
			try
			{
				await task;
			}
			catch (Exception ex)
			{
				AppendServerControlLog("Server control cancelled: " + ex.Message);
			}
			await Task.Yield();
		}
	}

	private async Task StopLocalServerAsync()
	{
		// One stop at a time: a second caller joins the stop already under way. The shared task is
		// released here, after it settles, so a stop that fails at once cannot stay pinned.
		Task pending = stopServerTask;
		if (pending == null)
		{
			pending = StopLocalServerCoreAsync();
			stopServerTask = pending;
		}
		try
		{
			await pending;
		}
		finally
		{
			if (stopServerTask == pending)
			{
				stopServerTask = null;
			}
		}
	}

	private async Task StopLocalServerCoreAsync()
	{
		await CancelServerCommandAsync();
		if (await RunServerCommandAsync(start: false) != 0)
		{
			throw new IOException("The local server did not stop completely - see logs/server-control.log. The database is left running so writes are not cut off.");
		}
	}

	private async void StopServerButton_OnClick(object sender, RoutedEventArgs e)
	{
		if (stoppingServer || windowClosing)
		{
			return;
		}
		if (IsThisGameRunning() || launcherProcessRunning)
		{
			ThemedMessageBox.Show("Stop the game before stopping the local server.", "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Asterisk);
		}
		else
		{
			if (serverConfiguring && serverCommandTask == null)
			{
				return;
			}
			stoppingServer = true;
			RuntimeStatusText.Text = "Stopping the local server...";
			StartGameButton.IsEnabled = false;
			try
			{
				await StopLocalServerAsync();
			}
			catch (Exception ex)
			{
				if (!windowClosing)
				{
					ThemedMessageBox.Show(ex.Message, "Server Did Not Stop", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				}
			}
			finally
			{
				stoppingServer = false;
				await RefreshRuntimeStatusAsync();
				if (!windowClosing)
				{
					await RefreshAccountsAsync(showErrors: false);
				}
			}
		}
	}
}
