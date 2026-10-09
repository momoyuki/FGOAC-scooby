using System;
using System.IO;

namespace FGOLocalPlatform;

/// <summary>What closing the launcher does to the server on this PC.</summary>
public static class LocalServerStop
{
	/// <summary>
	/// The script closing the launcher runs, in either mode: Stop-FGOLocalServerWhenIdle.ps1 while the
	/// game runs, Stop-FGOLocalServer.ps1 otherwise. Null when the script is missing, which goes to
	/// <paramref name="log" /> as one line.
	/// </summary>
	public static string? CloseScript(string gameRoot, bool gameRunning, Action<string> log)
	{
		string script = Path.GetFullPath(Path.Combine(gameRoot, "..", "Server", gameRunning ? "Stop-FGOLocalServerWhenIdle.ps1" : "Stop-FGOLocalServer.ps1"));
		if (!File.Exists(script))
		{
			log("Closing the launcher: " + script + " is missing, so the local server was not stopped.");
			return null;
		}
		return script;
	}
}
