using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	/// <summary>
	/// Writes the cabinet patches the PC play switches turn on into the session's game once its window exists, which
	/// is after the stock hook has installed. The switches are read when the session starts, as the launch script
	/// reads them.
	/// </summary>
	private async Task PatchCabinetLeftoversAsync(CapturedProcess launcherProcess)
	{
		try
		{
			PcPortSwitches switches = PcPortSwitches.Load(Path.Combine(GamePaths.GameRoot, "fgo-launcher.json"));
			int? game;
			while ((game = FindGameWindow()) == null)
			{
				if (runningLauncher != launcherProcess)
				{
					return;
				}
				await Task.Delay(1000);
			}
			int processId = game.Value;
			CabinetPatchRun run = await Task.Run(() => CabinetPatcher.Apply(processId, CabinetPatches.Table, switches));
			foreach (string line in run.Lines)
			{
				QueueLauncherOutput("[pcport] " + line, standardError: false);
				AppendServerControlLog("[pcport] " + line);
			}
		}
		catch (Exception ex)
		{
			QueueLauncherOutput("[pcport] The cabinet patches stopped: " + ex.Message, standardError: true);
			AppendServerControlLog("[pcport] The cabinet patches stopped: " + ex.Message);
		}
	}

	/// <summary>The process id of this game folder's ago.exe once it has a window; null before.</summary>
	private static int? FindGameWindow()
	{
		string path = Path.Combine(GamePaths.GameRoot, "ago.exe");
		int? found = null;
		foreach (Process process in Process.GetProcessesByName("ago"))
		{
			using (process)
			{
				try
				{
					if (found == null && process.MainWindowHandle != IntPtr.Zero && string.Equals(process.MainModule?.FileName, path, StringComparison.OrdinalIgnoreCase))
					{
						found = process.Id;
					}
				}
				catch (Exception ex) when (ex is Win32Exception || ex is InvalidOperationException)
				{
					// The process is starting or has gone; ask again next tick.
				}
			}
		}
		return found;
	}
}
