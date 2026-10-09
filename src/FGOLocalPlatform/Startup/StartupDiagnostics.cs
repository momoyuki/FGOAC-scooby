using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace FGOLocalPlatform;

internal static class StartupDiagnostics
{
	public const string PlatformUpdateNeeded = "This game folder has not had Cloud23333's V1.01 update yet: App\\FGO_Runtime.dll is missing, and the English launch and server scripts need it. Apply his V1.01 or V1.02 update to the game folder first, then start FGOAC scooby again.";

	public const string PlatformHalfUpdated = "This game folder has Cloud23333's update files, but App\\fgohook.dll is still the original 11.00 file, so the update was not applied completely and the game crashes at start. Unzip his V1.02 update over the game folder again and let it overwrite everything, then start FGOAC scooby again.";

	/// <summary>App\fgohook.dll as shipped in the original 11.00 package, before Cloud23333's V1.01.</summary>
	private const string OriginalHookSha256 = "75cb6a90b6bf36286ad69f177dc4fae39072fea6cdab3e562778768ef6071077";

	public const string GameErrors = "ERROR 4102 - The game stopped. Most often it could not reach the local server, because the server is not running, its ports are already in use, or the server address in Settings is wrong: start the server from the launcher, then click Run Environment Check on this page. If the computer name is the same as the Windows user name, or the game log names NamedPipe.cpp or an invalid named-account owner, the game files are older than Cloud23333's V1.02 update, which fixes that crash: apply his latest update in full. Renaming the Windows account or moving the game to drive C: is not needed.\n\nERROR 8404 - The game's own Startup Mode was saved as Satellite (Sub Unit), so it waits for a main unit that does not exist. Turn on Operator menu keys (F1, F2) on the Controls page and press Play, then on the error screen press F1 to open the Game Test Menu; F2 moves the arrow and F1 confirms. Open Game Settings, set Startup Mode to Main Unit, then choose Exit. The next boot goes to the title screen.\n\nCannot use Aime card, at the title screen - the game's first message to the local server timed out on that boot. Close the game, check that the server shows ready on the Play page, and press Play again.\n\n0x80131515 at Play, or the server failing with a message about FGO_Runtime.dll - Windows marked App\\FGO_Runtime.dll as downloaded from the internet, and PowerShell refuses to load a file with that mark. The launcher clears the mark itself when it starts; if it comes back, right-click the file, open Properties and tick Unblock.\n\nERROR 4104 - The game is installed on drive E: or Y:, and the game's own file hook redirects every E: path to its data mount, so it cannot open its resource files. Move the whole install folder to another drive, such as D:, and start it from there.\n\nERROR 4105 - The game was not started as administrator, so it could not create the registry keys it needs and the server rejected its first play record about 90 seconds after boot. Close the game and start the launcher as administrator.\n\n0xC0000005, the game closing a few seconds after launch - Windows Defender Controlled Folder Access is almost always blocking the game from writing its files. Open Windows Security > Virus & threat protection > Ransomware protection and allow the game's executable, or turn Controlled Folder Access off.\n\nNo sound, or 0x88890004 / 0x88890026 - Windows dropped the game's audio stream, usually because the default output device changed or is muted. Set the headphones or speakers you want as the Windows default output and unmute them in the volume mixer, then start the game again.\n\n0x88890010 - The Windows Audio service is not running. Start the Windows Audio service, then launch the game again.\n\nA black screen or a display driver error - the game is built for NVIDIA cards and needs a current driver. Update your graphics driver, then launch the game again.\n\nERROR 6401 - usually seen with a controller or other USB device plugged in. Turn off USB selective suspend in the Windows power plan, then start the game again.\n\nExit code 22 with 0xC000001D - the CPU lacks the F16C instruction set the game uses (Pentium and Celeron before the 12th generation, Intel Core before the 3rd). The game cannot run on that CPU.\n\nThe game crashes when the first battle loads on an AMD card - the graphics layer earlier FGOAC scooby versions installed fails to build the game's shaders on RX 500, RX 6000 and RX 7600 cards. Cloud23333's latest update brings his own layer for AMD and Intel cards: apply it in full, then press Play.";

	/// <summary>The meaning of a launch script exit code, as the launch script of this game folder uses it.</summary>
	public static string Explain(int code)
	{
		return Explain(code, GameBuild.Is1100(Path.GetFullPath(Path.Combine(GamePaths.GameRoot, ".."))));
	}

	/// <summary>The meaning of a launch script exit code: on <paramref name="game1100"/> FGOAC_Launcher.ps1's, else Cloud23333's FGO_Launcher.ps1's.</summary>
	public static string Explain(int code, bool game1100)
	{
		return code switch
		{
			2 => "Some of the files the game needs are missing. Unzip the FGOAC scooby package into the game folder itself, the one that holds App and Server, so that FGOAC scooby.exe sits beside them; make sure Cloud23333's update has been applied; and check whether your antivirus quarantined anything.",
			3 => "The launcher could not find a network adapter for the cabinet network. Set the server address to auto in Settings and re-install the App folder from the package, so that FGO_Launcher.ps1 and fgohook.dll match; auto mode needs no internet connection.", 
			4 => "A folder the game must write to is locked. Start the launcher as administrator, then check the path shown below for free space, a read-only flag, or a folder block from your antivirus.", 
			5 => "Windows has no audio output device turned on. Enable your speakers or headphones in Windows and set them as the default output, then start the game again.", 
			10 => "The local server could not start, usually because a port is already in use, the database failed, or an older server is still running. Read the error below, then check logs/server-control.log, artemis-stderr.log and mariadb.log.", 
			11 => "The server ports could not be reached. On a single PC set the server address to auto and start the local server; for a remote server check the address and ports you entered; for the community server start the game with Play Online in the launcher, which carries its connections.", 
			12 => "The optional FGOAudio.dll audio hook is missing. The launch script was started with -EnableExperimentalAudio; start the game with Play instead, as the game has its own shared-mode audio and does not need that file.", 
			13 => "A settings file is not valid JSON, or gameVersion in App\\fgo-launcher.json is not a version number. Check App\\fgo-launcher.json and App\\config.json for typos, then press Play again.", 
			14 => "The monitor or the resolution setting is not valid. Choose your monitor and a valid width and height again under Settings > Display, then save.", 
			15 => "The environment check did not pass. Fix the items listed below, then click Run Environment Check under Settings > Diagnostics and Help for the full report.", 
			16 => game1100 ? "This PC has no card for the server Play uses. For the server on this PC, pick an account in the Master menu on Play; for the community server, choose New account on the Online page, or Recover account if you have a recovery key. Then press Play again." : "The graphics card picked for the game could not be used, so the game was stopped rather than run on another card. Under Settings > Display pick another card or Automatic, then press Save Display Settings; with the AMD and Intel compatibility layer on, saving there clears the pick, as no card can be picked beside it.",
			22 => "The game crashed or closed on its own soon after starting. Check the logs folder for the last failure, the exit code and any crash dump. If the game window opened and closed again with 0xC0000005 although the environment check passes, try windowed 1280x720 on the primary monitor, and when reporting it attach logs\\ago-crash-*.dmp with your graphics card and driver version.",
			_ => "The launch script did not finish. Read the error below and logs/fgo-last-launch.log; the exit number on its own does not tell you the cause.", 
		};
	}

	public static void CheckLayout()
	{
		string fullPath = Path.GetFullPath(Path.Combine(GamePaths.GameRoot, ".."));
		string runtime = Path.Combine(GamePaths.GameRoot, "FGO_Runtime.dll");
		if (!File.Exists(runtime))
		{
			throw new FileNotFoundException(PlatformUpdateNeeded, runtime);
		}
		string hook = Path.Combine(GamePaths.GameRoot, "fgohook.dll");
		if (File.Exists(hook) && string.Equals(Updater.HashFile(hook), OriginalHookSha256, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidDataException(PlatformHalfUpdated);
		}
		string text = Path.Combine(GamePaths.GameRoot, "FGOAC_StartupChecks.ps1");
		if (!File.Exists(text))
		{
			throw new FileNotFoundException(Explain(2), text);
		}
		CheckFolders(text, fullPath, CommunityServer.OnlineSession(GamePaths.GameRoot));
	}

	/// <summary>
	/// Runs the folder check of FGOAC_StartupChecks.ps1 on the game folder; a failure throws IOException with the Status
	/// sentence, a blank line, and the check's own output.
	/// </summary>
	public static void CheckFolders(string checkScript, string installRoot, bool online)
	{
		// The check's message alone, unwrapped: the host's error view would fold it and add the script's own path.
		ProcessStartInfo processStartInfo = PowerShellHost.CreateStartInfo(installRoot, redirectOutput: true, new string[2] { "-Command", "[Console]::OutputEncoding=[Text.UTF8Encoding]::new($false); $ErrorActionPreference='Stop'; try { . $env:FGO_CHECK_SCRIPT; Test-FgoWritableLayout -InstallRoot $env:FGO_CHECK_ROOT" + (online ? " -CommunityServer" : "") + " } catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }" });
		processStartInfo.Environment["FGO_CHECK_SCRIPT"] = checkScript;
		processStartInfo.Environment["FGO_CHECK_ROOT"] = installRoot;
		using Process process = Process.Start(processStartInfo) ?? throw new IOException("Could not start the folder check.");
		Task<string> task = process.StandardOutput.ReadToEndAsync();
		Task<string> task2 = process.StandardError.ReadToEndAsync();
		ProcessCompletion.WaitAsync(process, TimeSpan.FromSeconds(20.0)).GetAwaiter().GetResult();
		Task.WhenAll<string>(task, task2).WaitAsync(TimeSpan.FromSeconds(2.0)).GetAwaiter()
			.GetResult();
		if (process.ExitCode != 0)
		{
			string output = (task2.GetAwaiter().GetResult() + task.GetAwaiter().GetResult()).Trim();
			throw new IOException(FolderCheckProblem(output) + "\n\n" + output);
		}
	}

	/// <summary>
	/// The Status sentence for a failed folder check: the first path in its output, as the check names it or as a
	/// quoted or bare path, is the locked folder or file; without one the check itself failed.
	/// </summary>
	public static string FolderCheckProblem(string output)
	{
		Match path = Regex.Match(output, @"Cannot write to (?<path>[A-Za-z]:\\.+?)\. Run the launcher|'(?<path>[A-Za-z]:\\[^']+)'|(?<path>[A-Za-z]:\\[^\s'""]*)");
		return path.Success ? ("A folder or file the game must write to is locked: " + path.Groups["path"].Value.TrimEnd('.') + ". Start the launcher as administrator, then check it for free space, a read-only flag, or a block from your antivirus.") : "The check of the game folder did not finish. Open the logs for the reason, then start the launcher again.";
	}
}
