using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace FGOLocalPlatform;

/// <summary>
/// The OpenGL compatibility layer for AMD and Intel graphics. The game asks for NVIDIA-only extensions, and a layer in
/// App\ translates them. The package ships three under compat\: Cloud23333's layer from his 1.10 update
/// (fgoglcompat.dll), the layer FGOAC scooby shipped with 1.1.0 (fgoglcompat-1.1.0.dll), both installed as
/// App\fgoglcompat.dll, and fluphus's AMD layer under amd-shim\, installed as App\opengl32.dll beside a copy of the
/// system DLL and a driver profile. Cloud23333's layer is the default: on his build his updater installs it and his
/// launch script loads it, and on game 11.00 a fresh install without an NVIDIA card gets it on the first run. A layer
/// file FGOAC scooby did not ship is left as it is.
/// </summary>
internal static class GpuCompat
{
	internal enum Choice
	{
		Off,
		Cloud,
		Older,
		Fluphus
	}

	private const string LegacyFileName = "fgoglcompat.dll";

	/// <summary>SHA-256 of Cloud23333's fgoglcompat.dll from his 1.10 update.</summary>
	private const string CloudLegacyHash = "23f1e11cd820f43cd7d47a86090da4c3f5215f5c1bd0294a381561abe6882ab6";

	/// <summary>SHA-256 of the fgoglcompat.dll FGOAC scooby shipped with 1.1.0.</summary>
	private const string OlderLegacyHash = "4932eebf73d715949b04c74b7cf1ee9dbabcc6887f666f3524850d374156f66c";

	/// <summary>SHA-256 of every earlier build of fluphus's opengl32.dll released under compat\amd-shim.</summary>
	internal static string[] ShippedShimHashes = new string[1] { "a85042d91ea60a3108f28dfad4673265f2e1f2a402b252436e5d5757c368a0f0" };

	private const string DisplayClassKey = "SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e968-e325-11ce-bfc1-08002be10318}";

	/// <summary>True on the build Cloud23333's 1.10 update installs, whose App\fgoglcompat.dll is his.</summary>
	public static bool HisBuild => !GameBuild.Is1100(Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..")));

	private static string CompatRoot => Path.GetFullPath(Path.Combine(GamePaths.GameRoot, "..", "compat"));

	private static string CloudSourcePath => Path.Combine(CompatRoot, LegacyFileName);

	private static string OlderSourcePath => Path.Combine(CompatRoot, "fgoglcompat-1.1.0.dll");

	public static string ShimSourcePath => Path.Combine(CompatRoot, "amd-shim", "opengl32.dll");

	private static string ShimConfigSourcePath => Path.Combine(CompatRoot, "amd-shim", "amdcfg", "amdOglpSettings.cfg");

	private static string LegacyTargetPath => Path.Combine(GamePaths.GameRoot, LegacyFileName);

	private static string ShimTargetPath => Path.Combine(GamePaths.GameRoot, "opengl32.dll");

	private static string ShimRealTargetPath => Path.Combine(GamePaths.GameRoot, "opengl32real.dll");

	private static string ShimConfigTargetPath => Path.Combine(GamePaths.GameRoot, "amdcfg", "amdOglpSettings.cfg");

	public static bool ShimSourceAvailable => File.Exists(ShimSourcePath) && File.Exists(ShimConfigSourcePath);

	/// <summary>The choices this game folder offers, in the order the Display page lists them.</summary>
	public static IReadOnlyList<Choice> Offered
	{
		get
		{
			List<Choice> list = new List<Choice>();
			if (HisBuild)
			{
				list.Add(Choice.Cloud);
			}
			else
			{
				list.Add(Choice.Off);
				if (File.Exists(CloudSourcePath))
				{
					list.Add(Choice.Cloud);
				}
				if (File.Exists(OlderSourcePath))
				{
					list.Add(Choice.Older);
				}
			}
			if (ShimSourceAvailable)
			{
				list.Add(Choice.Fluphus);
			}
			return list;
		}
	}

	/// <summary>The layer in App\; null when App\ holds a layer file FGOAC scooby did not ship.</summary>
	public static Choice? Current
	{
		get
		{
			if (HisBuild)
			{
				return OurShimPresent ? Choice.Fluphus : Choice.Cloud;
			}
			if (File.Exists(ShimTargetPath))
			{
				return OurShimPresent ? Choice.Fluphus : null;
			}
			if (!File.Exists(LegacyTargetPath))
			{
				return Choice.Off;
			}
			return LegacyBuild();
		}
	}

	/// <summary>True when App\opengl32.dll is fluphus's layer as FGOAC scooby ships it now or shipped it before.</summary>
	private static bool OurShimPresent => File.Exists(ShimTargetPath) && (SameFile(ShimTargetPath, ShimSourcePath) || Array.IndexOf(ShippedShimHashes, Updater.HashFile(ShimTargetPath)) >= 0);

	/// <summary>Which shipped build App\fgoglcompat.dll is, by its SHA-256 or the copy under compat\; null for any other.</summary>
	private static Choice? LegacyBuild()
	{
		string hash = Updater.HashFile(LegacyTargetPath);
		if (hash == CloudLegacyHash || SameFile(LegacyTargetPath, CloudSourcePath))
		{
			return Choice.Cloud;
		}
		if (hash == OlderLegacyHash || SameFile(LegacyTargetPath, OlderSourcePath))
		{
			return Choice.Older;
		}
		return null;
	}

	/// <summary>True when both files exist and hold the same bytes.</summary>
	private static bool SameFile(string a, string b)
	{
		return File.Exists(a) && File.Exists(b) && new FileInfo(a).Length == new FileInfo(b).Length && string.Equals(Updater.HashFile(a), Updater.HashFile(b), StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>
	/// Puts in the layer chosen and takes out the others FGOAC scooby put there. On his build only his layer and
	/// fluphus's apply, and his App\fgoglcompat.dll is never touched. Throws IOException when App\ holds a layer file
	/// the choice would have to replace and FGOAC scooby did not ship it.
	/// </summary>
	public static void Apply(Choice choice)
	{
		if (HisBuild)
		{
			ApplyOnHisBuild(choice);
			return;
		}
		if (File.Exists(LegacyTargetPath) && LegacyBuild() == null)
		{
			throw new IOException("App\\fgoglcompat.dll is a layer FGOAC scooby did not ship, so it was left as it is.");
		}
		switch (choice)
		{
		case Choice.Off:
			RemoveShim();
			DeleteLegacy();
			break;
		case Choice.Cloud:
			RemoveShim();
			File.Copy(CloudSourcePath, LegacyTargetPath, overwrite: true);
			break;
		case Choice.Older:
			RemoveShim();
			File.Copy(OlderSourcePath, LegacyTargetPath, overwrite: true);
			break;
		case Choice.Fluphus:
			DeleteLegacy();
			InstallShim();
			break;
		}
	}

	private static void ApplyOnHisBuild(Choice choice)
	{
		switch (choice)
		{
		case Choice.Cloud:
			if (OurShimPresent)
			{
				RemoveShim();
			}
			return;
		case Choice.Fluphus:
			if (File.Exists(ShimTargetPath) && !OurShimPresent)
			{
				throw new IOException("App\\opengl32.dll is a layer FGOAC scooby did not ship, so it was left as it is.");
			}
			if (!SameFile(ShimTargetPath, ShimSourcePath))
			{
				InstallShim();
			}
			return;
		default:
			throw new IOException("This game folder has Cloud23333's latest update: his layer runs, and fluphus's AMD layer is the other choice.");
		}
	}

	/// <summary>
	/// Replaces an App\opengl32.dll that is an earlier build FGOAC scooby shipped with the copy under compat\amd-shim.
	/// Any other opengl32.dll is left as it is. True when the layer was replaced; throws IOException on failure.
	/// </summary>
	internal static bool UpgradeShipped()
	{
		if (!File.Exists(ShimTargetPath) || !ShimSourceAvailable || SameFile(ShimTargetPath, ShimSourcePath))
		{
			return false;
		}
		if (Array.IndexOf(ShippedShimHashes, Updater.HashFile(ShimTargetPath)) < 0)
		{
			return false;
		}
		InstallShim();
		return true;
	}

	private static void DeleteLegacy()
	{
		if (File.Exists(LegacyTargetPath))
		{
			File.Delete(LegacyTargetPath);
		}
	}

	/// <summary>
	/// The three files fluphus's installer writes: the system OpenGL DLL under the name the shim
	/// forwards to, the shim itself, and the driver profile.
	/// </summary>
	private static void InstallShim()
	{
		File.Copy(Path.Combine(Environment.SystemDirectory, "opengl32.dll"), ShimRealTargetPath, overwrite: true);
		File.Copy(ShimSourcePath, ShimTargetPath, overwrite: true);
		Directory.CreateDirectory(Path.GetDirectoryName(ShimConfigTargetPath));
		File.Copy(ShimConfigSourcePath, ShimConfigTargetPath, overwrite: true);
	}

	private static void RemoveShim()
	{
		foreach (string path in new string[3] { ShimTargetPath, ShimRealTargetPath, ShimConfigTargetPath })
		{
			if (File.Exists(path))
			{
				File.Delete(path);
			}
		}
		string configFolder = Path.GetDirectoryName(ShimConfigTargetPath);
		if (Directory.Exists(configFolder) && Directory.GetFileSystemEntries(configFolder).Length == 0)
		{
			Directory.Delete(configFolder);
		}
	}

	/// <summary>
	/// True when any display adapter is NVIDIA. A laptop with NVIDIA beside an Intel chip counts as
	/// NVIDIA, since the hook already asks Windows for the NVIDIA card. Unreadable registry counts as
	/// NVIDIA too, so nothing is installed by guesswork.
	/// </summary>
	public static bool HasNvidiaAdapter()
	{
		try
		{
			using RegistryKey? displayClass = Registry.LocalMachine.OpenSubKey(DisplayClassKey);
			if (displayClass == null)
			{
				return true;
			}
			bool sawAdapter = false;
			foreach (string name in displayClass.GetSubKeyNames())
			{
				if (name.Length != 4 || !int.TryParse(name, out _))
				{
					continue;
				}
				using RegistryKey? adapter = displayClass.OpenSubKey(name);
				if (adapter == null)
				{
					continue;
				}
				string provider = adapter.GetValue("ProviderName") as string ?? "";
				string description = adapter.GetValue("DriverDesc") as string ?? "";
				if (provider.Length == 0 && description.Length == 0)
				{
					continue;
				}
				sawAdapter = true;
				if (provider.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) || description.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
			return !sawAdapter;
		}
		catch (Exception)
		{
			return true;
		}
	}
}
