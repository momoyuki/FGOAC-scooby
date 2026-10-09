using System;
using System.Collections.Generic;
using System.IO;

namespace FGOLocalPlatform;

/// <summary>
/// Clears the graphics layers' shader caches in the App folder, ported from Cloud23333's FGO_ClearShaderCache.ps1: his
/// layer's aft-program-link-cache-v1 and shader-cache, and the shader-cache-r10 and shader-cache-r2 of this launcher's
/// layers. Refused while the game runs; only a plain folder inside App is removed, never a link. The next start builds
/// the caches again.
/// </summary>
public static class ShaderCache
{
	public static IReadOnlyList<string> Folders { get; } = new string[4] { "aft-program-link-cache-v1", "shader-cache", "shader-cache-r10", "shader-cache-r2" };

	/// <summary>
	/// Removes each cache folder there is and returns their names; throws InvalidOperationException with a sentence for
	/// the player when it refuses, leaving the folders it had not reached.
	/// </summary>
	public static IReadOnlyList<string> Clear(string appFolder, bool gameRunning)
	{
		if (gameRunning)
		{
			throw new InvalidOperationException("Close the game first, then clear the shader cache.");
		}
		string appRoot = Path.GetFullPath(appFolder).TrimEnd('\\') + "\\";
		if (!File.Exists(Path.Combine(appRoot, "ago.exe")))
		{
			throw new InvalidOperationException("The game's App folder with ago.exe was not found, so nothing was cleared.");
		}
		List<string> removed = new List<string>();
		foreach (string name in Folders)
		{
			string target = Path.GetFullPath(Path.Combine(appRoot, name));
			if (!Directory.Exists(target) && !File.Exists(target))
			{
				continue;
			}
			FileAttributes attributes = File.GetAttributes(target);
			if ((attributes & FileAttributes.Directory) == 0 || (attributes & FileAttributes.ReparsePoint) != 0 || !target.StartsWith(appRoot, StringComparison.OrdinalIgnoreCase))
			{
				throw new InvalidOperationException("App\\" + name + " is not a plain folder in App, so it was left as it is.");
			}
			Directory.Delete(target, recursive: true);
			removed.Add(name);
		}
		return removed;
	}
}
