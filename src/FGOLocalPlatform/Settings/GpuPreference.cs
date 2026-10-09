using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace FGOLocalPlatform;

/// <summary>
/// Windows' per-app GPU preference for the game: a string value named by the full exe path under
/// HKCU\Software\Microsoft\DirectX\UserGpuPreferences whose <c>GpuPreference=N;</c> pair is 1 (power saving) or 2
/// (high performance); without that pair Windows decides. Other pairs in the value are kept.
/// </summary>
public static class GpuPreference
{
	public const string RegistryPath = "Software\\Microsoft\\DirectX\\UserGpuPreferences";

	public const int LetWindowsDecide = 0;

	public const int PowerSaving = 1;

	public const int HighPerformance = 2;

	private const string Name = "GpuPreference";

	private const uint SoftwareAdapter = 2;

	private static readonly Guid Factory6 = new Guid("c1b6694f-ff09-44a9-b03c-77900a0a1d17");

	private static readonly Guid Adapter1 = new Guid("29038f61-3839-4626-91fd-086879011a05");

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct AdapterDesc1
	{
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string Description;

		public uint VendorId;

		public uint DeviceId;

		public uint SubSysId;

		public uint Revision;

		public UIntPtr DedicatedVideoMemory;

		public UIntPtr DedicatedSystemMemory;

		public UIntPtr SharedSystemMemory;

		public long AdapterLuid;

		public uint Flags;
	}

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int EnumAdapterByGpuPreference(IntPtr factory, uint index, int preference, ref Guid riid, out IntPtr adapter);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	private delegate int GetDesc1(IntPtr adapter, out AdapterDesc1 desc);

	[DllImport("dxgi.dll", ExactSpelling = true)]
	private static extern int CreateDXGIFactory1(ref Guid riid, out IntPtr factory);

	/// <summary>"Let Windows decide", "Power saving" or "High performance", with the adapter's name when known.</summary>
	public static string Label(int preference, string? adapter)
	{
		string label = preference switch
		{
			PowerSaving => "Power saving",
			HighPerformance => "High performance",
			_ => "Let Windows decide",
		};
		return (preference == LetWindowsDecide || string.IsNullOrEmpty(adapter)) ? label : (label + " (" + adapter + ")");
	}

	/// <summary>The adapter DXGI 1.6 ranks first for the preference; null for Let Windows decide, without DXGI 1.6 or on any failure.</summary>
	public static string? AdapterFor(int preference)
	{
		if (preference != PowerSaving && preference != HighPerformance)
		{
			return null;
		}
		try
		{
			Guid factoryId = Factory6;
			if (CreateDXGIFactory1(ref factoryId, out IntPtr factory) < 0)
			{
				return null;
			}
			try
			{
				Guid adapterId = Adapter1;
				if (Method<EnumAdapterByGpuPreference>(factory, 29)(factory, 0, preference, ref adapterId, out IntPtr adapter) < 0)
				{
					return null;
				}
				try
				{
					return (Method<GetDesc1>(adapter, 10)(adapter, out AdapterDesc1 desc) >= 0 && (desc.Flags & SoftwareAdapter) == 0) ? desc.Description : null;
				}
				finally
				{
					Marshal.Release(adapter);
				}
			}
			finally
			{
				Marshal.Release(factory);
			}
		}
		catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
		{
			return null;
		}
	}

	/// <summary>The preference saved for exePath under key; Let Windows decide when there is none or it is not 1 or 2.</summary>
	public static int Read(RegistryKey key, string exePath)
	{
		string? value = Pairs(key.GetValue(exePath) as string).FirstOrDefault((string pair) => pair.StartsWith(Name + "=", StringComparison.OrdinalIgnoreCase));
		return (value?.Substring(Name.Length + 1)) switch
		{
			"1" => PowerSaving,
			"2" => HighPerformance,
			_ => LetWindowsDecide,
		};
	}

	/// <summary>Sets exePath's GpuPreference pair; Let Windows decide drops it, and the value goes when no pair is left.</summary>
	public static void Write(RegistryKey key, string exePath, int preference)
	{
		List<string> pairs = Pairs(key.GetValue(exePath) as string).Where((string pair) => !pair.StartsWith(Name + "=", StringComparison.OrdinalIgnoreCase)).ToList();
		if (preference == PowerSaving || preference == HighPerformance)
		{
			pairs.Add($"{Name}={preference}");
		}
		if (pairs.Count == 0)
		{
			key.DeleteValue(exePath, throwOnMissingValue: false);
		}
		else
		{
			key.SetValue(exePath, string.Concat(pairs.Select((string pair) => pair + ";")), RegistryValueKind.String);
		}
	}

	/// <summary>
	/// High performance for exePath once, as hybrid-GPU laptops can otherwise run the game on an adapter without the OpenGL
	/// extensions it needs: while settings has preferHighPerformanceGpu unset or true and no pick is saved. Either way
	/// preferHighPerformanceGpu is then false, so a later pick, Let Windows decide too, stays. True when settings changed.
	/// </summary>
	public static bool WriteOnce(RegistryKey key, string exePath, JsonObject settings)
	{
		if (!WriteOnceDue(settings))
		{
			return false;
		}
		if (Read(key, exePath) == LetWindowsDecide)
		{
			Write(key, exePath, HighPerformance);
		}
		settings["preferHighPerformanceGpu"] = false;
		return true;
	}

	/// <summary>True while settings has preferHighPerformanceGpu unset or true.</summary>
	private static bool WriteOnceDue(JsonObject settings)
	{
		return !(settings["preferHighPerformanceGpu"] is JsonValue value && value.TryGetValue(out bool prefer) && !prefer);
	}

	/// <summary>
	/// WriteOnce for the game under keyPath in the current user's registry, which it opens only while WriteOnce is due, saving
	/// the Settings file at settingsPath when it changed; a missing Settings file is a fresh install's, with no pick saved.
	/// </summary>
	public static void WriteOnce(string settingsPath, string exePath, string keyPath = RegistryPath)
	{
		JsonObject? settings = File.Exists(settingsPath) ? JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject : new JsonObject();
		if (settings == null || !WriteOnceDue(settings))
		{
			return;
		}
		using RegistryKey key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
		if (WriteOnce(key, exePath, settings))
		{
			AtomicFile.WriteAllText(settingsPath, settings.ToJsonString(new JsonSerializerOptions
			{
				WriteIndented = true
			}) + Environment.NewLine);
		}
	}

	/// <summary>
	/// The graphics card Cloud23333's launcher picked, from the keys his 1.10 launch script routes the game by at every
	/// start ahead of this preference: its name, "" for a pick saved without one, null for none. Read only: a card name
	/// this launcher saved could stop his script's start when that card is not found.
	/// </summary>
	public static string? HisPick(JsonObject settings)
	{
		JsonObject? graphics = settings["graphics"] as JsonObject;
		string Saved(string key) => ((graphics?[key] as JsonValue)?.ToString() ?? "").Trim();
		string name = Saved("nativeGpuName");
		if (name.Length > 0)
		{
			return name;
		}
		return (Saved("nativeGpuLuid").Length > 0 || Saved("nativeGpuPhysicalId").Length > 0) ? "" : null;
	}

	/// <summary>The game's preference in the current user's registry.</summary>
	public static int Read(string exePath)
	{
		using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RegistryPath);
		return (key == null) ? LetWindowsDecide : Read(key, exePath);
	}

	/// <summary>Writes the game's preference into the current user's registry.</summary>
	public static void Write(string exePath, int preference)
	{
		using RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true);
		Write(key, exePath, preference);
	}

	private static IEnumerable<string> Pairs(string? value)
	{
		return (value ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
	}

	private static T Method<T>(IntPtr instance, int slot) where T : Delegate
	{
		return Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size));
	}
}
