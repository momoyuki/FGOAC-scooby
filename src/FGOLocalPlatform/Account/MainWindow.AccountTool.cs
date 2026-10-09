using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	private sealed class AccountToolResult
	{
		public bool Ok { get; init; }

		public string Error { get; init; } = "";

		public string Message { get; init; } = "";

		public string Stderr { get; init; } = "";

		public string RawOutput { get; init; } = "";

		public JsonObject? Root { get; init; }
	}

	private bool accountToolRunning;

	private bool lastKnownServerRunning;

	private static string AccountToolPythonPath => Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..", "Server", "python", "python.exe"));

	private static string AccountToolScriptPath => Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..", "Server", "tools", "fgo_account.py"));

	private static string AccountToolWorkingDirectory => Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..", "Server", "artemis"));

	private static AccountToolResult AccountToolFailure(string error, string message)
	{
		return new AccountToolResult
		{
			Error = error,
			Message = message
		};
	}

	/// <summary>Max Master Level, a script of this launcher's beside Cloud23333's account tool, which has no such action.</summary>
	private static string MasterLevelScriptPath => Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..", "Server", "tools", "fgoac_master_level.py"));

	private static Task<AccountToolResult> RunAccountToolAsync(params string[] arguments)
	{
		return RunAccountScriptAsync(AccountToolScriptPath, arguments);
	}

	private static async Task<AccountToolResult> RunAccountScriptAsync(string script, params string[] arguments)
	{
		if (!File.Exists(AccountToolPythonPath))
		{
			return AccountToolFailure("tool_missing", "Python runtime not found: " + AccountToolPythonPath);
		}
		if (!File.Exists(script))
		{
			return AccountToolFailure("tool_missing", "Account tool script not found: " + script);
		}
		ProcessStartInfo processStartInfo = new ProcessStartInfo
		{
			FileName = AccountToolPythonPath,
			WorkingDirectory = AccountToolWorkingDirectory,
			UseShellExecute = false,
			CreateNoWindow = true,
			WindowStyle = ProcessWindowStyle.Hidden,
			ErrorDialog = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
			StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
		};
		processStartInfo.Environment["PYTHONUTF8"] = "1";
		processStartInfo.Environment["PYTHONIOENCODING"] = "utf-8";
		processStartInfo.ArgumentList.Add(script);
		foreach (string item in arguments)
		{
			processStartInfo.ArgumentList.Add(item);
		}
		using Process process = Process.Start(processStartInfo) ?? throw new InvalidOperationException("Could not start the account tool process");
		Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
		Task<string> stderrTask = process.StandardError.ReadToEndAsync();
		using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60.0));
		try
		{
			await process.WaitForExitAsync(timeout.Token);
		}
		catch (OperationCanceledException)
		{
			try
			{
				process.Kill(entireProcessTree: true);
			}
			catch
			{
			}
			return AccountToolFailure("timeout", "The account tool timed out after 60 seconds");
		}
		string stdout = await stdoutTask;
		string stderr = await stderrTask;
		JsonObject jsonObject = null;
		try
		{
			jsonObject = JsonNode.Parse(stdout.Trim()) as JsonObject;
		}
		catch (JsonException)
		{
		}
		if (jsonObject == null)
		{
			return new AccountToolResult
			{
				Error = "bad_output",
				Message = "The account tool did not return valid JSON",
				Stderr = stderr,
				RawOutput = stdout
			};
		}
		return new AccountToolResult
		{
			Ok = (jsonObject["ok"]?.GetValue<bool>() ?? false),
			Error = (jsonObject["error"]?.GetValue<string>() ?? ""),
			Message = (jsonObject["message"]?.GetValue<string>() ?? ""),
			Stderr = stderr,
			RawOutput = stdout,
			Root = jsonObject
		};
	}

	private void AppendAccountLog(string message)
	{
		AppendInjectionLog("[account] " + message + Environment.NewLine);
	}

	private async Task ExecuteAccountActionAsync(Func<Task> action)
	{
		if (accountToolRunning)
		{
			return;
		}
		accountToolRunning = true;
		// The Account page's result line reports only the latest account action.
		AccountStatusText.Visibility = Visibility.Collapsed;
		SetAccountControlsEnabled(enabled: false);
		try
		{
			await action();
		}
		catch (Exception ex)
		{
			AppendAccountLog("Account operation error: " + ex.Message);
			ThemedMessageBox.Show("Account operation failed:\n" + ex.Message, "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
		finally
		{
			accountToolRunning = false;
			SetAccountControlsEnabled(enabled: true);
		}
	}

	private static void ShowServerRunningAccountWarning()
	{
		ThemedMessageBox.Show("The local server is running, so this account operation cannot run.\nStop it from the menu beside the status on Play, then try again.", "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Exclamation);
	}

	/// <summary>True when the game is running; shows the warning that account actions are blocked until it exits.</summary>
	private bool GameBlocksAccountAction()
	{
		if (Process.GetProcessesByName("ago").Length == 0)
		{
			return false;
		}
		lastKnownGameRunning = true;
		SetAccountControlsEnabled(enabled: true);
		ShowGameRunningAccountWarning();
		return true;
	}

	/// <summary>The account tool refuses to run while the local server is up; warns and says whether it is.</summary>
	private async Task<bool> ServerBlocksAccountActionAsync()
	{
		if (await IsPortOpenAsync(ServerSettingsView.ConfiguredPorts()[0]))
		{
			lastKnownServerRunning = true;
			ShowServerRunningAccountWarning();
			return true;
		}
		lastKnownServerRunning = false;
		return false;
	}

	private static void ShowGameRunningAccountWarning()
	{
		ThemedMessageBox.Show("The game is running, so accounts cannot be switched, created, deleted, reset or repaired.\nEnd the current game session first, so the card still matches the save that is logged in.", "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Exclamation);
	}

	private bool ReportAccountToolFailure(AccountToolResult result, string operation)
	{
		if (result.Ok)
		{
			return false;
		}
		string text = ((result.Error.Length > 0 && result.Message.Length > 0) ? (result.Error + ": " + result.Message) : ((result.Message.Length > 0) ? result.Message : result.Error));
		AppendAccountLog(operation + " failed: " + text);
		if (result.Stderr.Trim().Length > 0)
		{
			AppendAccountLog("stderr: " + result.Stderr.Trim());
		}
		if (result.Error == "server_running")
		{
			lastKnownServerRunning = true;
			ShowServerRunningAccountWarning();
		}
		else
		{
			ThemedMessageBox.Show("Account operation (" + operation + ") failed:\n" + text, "FGOAC scooby", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
		return true;
	}
}
