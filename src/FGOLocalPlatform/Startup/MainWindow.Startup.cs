using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	private bool firstRunning;

	private bool startupChecking;

	/// <summary>The install check's sentence when it failed; Play stays off while it is set.</summary>
	private string? installProblem;

	/// <summary>The release notes the Status offers with What's new after an update, until they are read or Play is pressed.</summary>
	private string? whatsNewBody;

	private async Task<bool> RunFirstRunAsync()
	{
		bool freshInstall = false;
		firstRunning = true;
		accountToolRunning = true;
		SetAccountControlsEnabled(enabled: false);
		try
		{
			FirstRun firstRun = new FirstRun(this, async (string[] arguments) =>
			{
				AccountToolResult accountToolResult = await RunAccountToolAsync(arguments);
				return new FirstRun.ToolResult(accountToolResult.Ok, string.IsNullOrWhiteSpace(accountToolResult.Message) ? accountToolResult.Error : accountToolResult.Message, accountToolResult.Root);
			}, delegate(string message)
			{
				RuntimeStatusText.Text = message;
			}, AppendAccountLog);
			if (await firstRun.RunAsync())
			{
				LoadLauncherSettings();
				await RefreshAccountsAsync(showErrors: false);
			}
			freshInstall = firstRun.FreshInstall;
		}
		catch (Exception ex)
		{
			AppendAccountLog("First-run setup error: " + ex.Message);
			ThemedMessageBox.Show(this, "The first-run setup did not finish:\n" + ex.Message + "\n\nYou can still set everything up by hand from the Account and Settings pages.", "FGOAC scooby - First Run", MessageBoxButton.OK, MessageBoxImage.Exclamation, MessageBoxResult.OK, foreground: true);
		}
		finally
		{
			firstRunning = false;
			accountToolRunning = false;
			SetAccountControlsEnabled(enabled: true);
			await RefreshRuntimeStatusAsync();
		}
		return freshInstall;
	}

	/// <summary>
	/// The current version's own section of the CHANGELOG.md that ships beside the launcher, as
	/// plain text: headings without their marks, wrapped source lines joined into paragraphs and
	/// bullets so the window wraps them itself. Empty when the file or the section is missing.
	/// </summary>
	private static string ReadReleaseNotes(string version)
	{
		string path = Path.Combine(GamePaths.GameRoot, "..", "CHANGELOG.md");
		if (!File.Exists(path))
		{
			return "";
		}
		List<string> lines = new List<string>();
		bool inside = false;
		foreach (string raw in File.ReadLines(path))
		{
			string line = raw.TrimEnd();
			if (line.StartsWith("## [", StringComparison.Ordinal))
			{
				if (inside)
				{
					break;
				}
				inside = line.StartsWith("## [" + version + "]", StringComparison.Ordinal);
				continue;
			}
			if (!inside)
			{
				continue;
			}
			if (line.Length == 0)
			{
				if (lines.Count > 0 && lines[lines.Count - 1].Length > 0)
				{
					lines.Add("");
				}
			}
			else if (line.StartsWith("### ", StringComparison.Ordinal))
			{
				if (lines.Count > 0 && lines[lines.Count - 1].Length > 0)
				{
					lines.Add("");
				}
				lines.Add(line.Substring(4));
			}
			else if (line.StartsWith("- ", StringComparison.Ordinal))
			{
				lines.Add(line);
			}
			else if (lines.Count > 0)
			{
				lines[lines.Count - 1] = lines[lines.Count - 1] + " " + line.Trim();
			}
			else
			{
				lines.Add(line.Trim());
			}
		}
		return string.Join("\n", lines).Trim();
	}

	/// <summary>
	/// After an update, once: the recorded launcher version differs from the running one and this
	/// is not a fresh install, which the first-run box already covers. The Status then says
	/// "Updated to" with What's new as its Next step. Records the running version either way.
	/// </summary>
	private void OfferWhatsNewIfUpdated(bool freshInstall)
	{
		string path = Path.Combine(GamePaths.GameRoot, "fgo-launcher.json");
		try
		{
			JsonObject settings = ((File.Exists(path) && JsonNode.Parse(File.ReadAllText(path)) is JsonObject existing) ? existing : new JsonObject());
			string previous = settings["launcherVersion"]?.GetValue<string>() ?? "";
			if (previous == UpdateSettings.Version)
			{
				return;
			}
			settings["launcherVersion"] = UpdateSettings.Version;
			AtomicFile.WriteAllText(path, settings.ToJsonString(new JsonSerializerOptions
			{
				WriteIndented = true
			}) + Environment.NewLine);
			if (freshInstall)
			{
				return;
			}
			string body = ReadReleaseNotes(UpdateSettings.Version);
			if (body.Length == 0)
			{
				body = "The launcher is now version " + UpdateSettings.Version + ". Your accounts, decks and settings are as they were.";
			}
			whatsNewBody = body;
		}
		catch (Exception ex)
		{
			AppendAccountLog("Could not show the release notes: " + ex.Message);
		}
	}

	private void ShowWhatsNew()
	{
		string? body = whatsNewBody;
		whatsNewBody = null;
		if (body != null)
		{
			DialogSink.ShowDialog(new WhatsNewDialog(this, UpdateSettings.Version, body));
		}
		_ = RefreshRuntimeStatusAsync();
	}

	/// <summary>Runs the install checks after the window is up; a failure keeps Play off and takes the status line.</summary>
	public async Task CheckInstallAsync()
	{
		startupChecking = true;
		await RefreshRuntimeStatusAsync();
		await Task.Run(() => LauncherLog.Rotate(GamePaths.LogsRoot, DateTime.Now));
		try
		{
			await Task.Run(StartupDiagnostics.CheckLayout);
		}
		catch (Exception ex)
		{
			installProblem = ex.Message.Split("\n\n")[0].Trim();
			AppendServerControlLog("Startup check: " + ex.Message);
			QueueLauncherOutput("[launcher] Startup check: " + ex.Message, standardError: true);
			FlushLauncherOutput();
		}
		finally
		{
			startupChecking = false;
		}
		await RefreshRuntimeStatusAsync();
		if (SectionTabs.SelectedItem == PlayTab && StartGameButton.IsEnabled && !StartGameButton.IsKeyboardFocusWithin)
		{
			StartGameButton.Focus();
		}
		else if (SectionTabs.SelectedItem == OnlineTab)
		{
			if (PlayOnlineButton.IsEnabled && !PlayOnlineButton.IsKeyboardFocusWithin)
			{
				PlayOnlineButton.Focus();
			}
			await ProbeCommunityServerAsync();
		}
	}
}
