using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace FGOLocalPlatform;

/// <summary>
/// The graphics card pick of Cloud23333's launcher, on a game folder with his App\gpu-runtime: the adapters his
/// FgoGpuProbe.dll lists and his keys nativeGpuLuid, nativeGpuName and nativeGpuPhysicalId in fgo-launcher.json's
/// graphics object, which his launch script routes the game by. Ported from his GpuSettingsView and FgoGpuRouting.Native.
/// </summary>
public sealed class GpuCard
{
	private sealed record Adapter(string Luid, string Vendor, string Name, string PhysicalId);

	public sealed record Option(string Luid, string Label)
	{
		public override string ToString()
		{
			return Label;
		}
	}

	[UnmanagedFunctionPointer(CallingConvention.Winapi)]
	private delegate void Output(IntPtr bytes, uint length, IntPtr context);

	[UnmanagedFunctionPointer(CallingConvention.Winapi)]
	private delegate int Enumerate(Output output, IntPtr context);

	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr LoadLibraryExW(string path, IntPtr file, uint flags);

	[DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
	private static extern IntPtr GetProcAddress(IntPtr module, string name);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool FreeLibrary(IntPtr module);

	private string selectedLuid = "";

	private string selectedName = "";

	private string selectedPhysicalId = "";

	private Dictionary<string, string> adapterAliases = new Dictionary<string, string>();

	private List<Adapter> adapters = new List<Adapter>();

	public string DetectionError { get; private set; } = "";

	public IReadOnlyList<Option> Options { get; private set; } = Array.Empty<Option>();

	public Option Selected { get; private set; } = new Option("", "Automatic");

	/// <summary>True when a card is picked, so his launch script routes the game to it.</summary>
	public bool Routed => selectedLuid.Length > 0 || selectedName.Length > 0;

	public static string RuntimeDirectory(string appRoot)
	{
		return Path.Combine(appRoot, "gpu-runtime");
	}

	public static bool Available(string appRoot)
	{
		return File.Exists(Path.Combine(RuntimeDirectory(appRoot), "FgoGpuProbe.dll"));
	}

	/// <summary>His probe's adapter list: one "LUID\tVENDOR\tNAME\tPHYSICALID" line per adapter.</summary>
	public static string ListAdapters(string runtimeDirectory)
	{
		string fullPath = Path.GetFullPath(Path.Combine(runtimeDirectory, "FgoGpuProbe.dll"));
		IntPtr module = LoadLibraryExW(fullPath, IntPtr.Zero, 2304u);
		if (module == IntPtr.Zero)
		{
			throw new IOException("The graphics card probe could not be loaded: " + fullPath, new Win32Exception(Marshal.GetLastWin32Error()));
		}
		try
		{
			IntPtr procAddress = GetProcAddress(module, "FgoGpuProbeEnumerateAdapters");
			if (procAddress == IntPtr.Zero)
			{
				throw new IOException("The graphics card probe has no FgoGpuProbeEnumerateAdapters.");
			}
			StringBuilder text = new StringBuilder();
			List<Exception> callbackErrors = new List<Exception>();
			Output output = delegate(IntPtr bytes, uint length, IntPtr context)
			{
				try
				{
					if (length > 4096)
					{
						throw new IOException("The graphics card probe returned a line that is too long.");
					}
					byte[] array = new byte[length];
					Marshal.Copy(bytes, array, 0, array.Length);
					text.Append(Encoding.UTF8.GetString(array));
				}
				catch (Exception item)
				{
					callbackErrors.Add(item);
				}
			};
			int status = Marshal.GetDelegateForFunctionPointer<Enumerate>(procAddress)(output, IntPtr.Zero);
			GC.KeepAlive(output);
			if (callbackErrors.Count != 0)
			{
				throw new IOException("The graphics card list could not be read.", callbackErrors[0]);
			}
			if (status != 0)
			{
				throw new IOException("The graphics card list could not be read, status " + status + ".");
			}
			return text.ToString();
		}
		finally
		{
			FreeLibrary(module);
		}
	}

	public void Load(JsonObject? graphics)
	{
		selectedLuid = (graphics?["nativeGpuLuid"] as JsonValue)?.GetValue<string>()?.ToUpperInvariant() ?? "";
		selectedName = (graphics?["nativeGpuName"] as JsonValue)?.GetValue<string>() ?? "";
		selectedPhysicalId = (graphics?["nativeGpuPhysicalId"] as JsonValue)?.GetValue<string>() ?? "";
		Populate();
	}

	/// <summary>Reads the probe's list; a line it cannot read leaves no adapters and sets <see cref="DetectionError" />.</summary>
	public void Detected(string probeOutput)
	{
		try
		{
			List<Adapter> list = new List<Adapter>();
			string[] array = probeOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < array.Length; i++)
			{
				string[] fields = array[i].TrimEnd('\r').Split('\t');
				if (fields.Length != 4 || !Regex.IsMatch(fields[0], "^[0-9A-F]{16}$") || !Regex.IsMatch(fields[1], "^[0-9A-F]{4}$"))
				{
					throw new IOException("The graphics card probe returned a line the launcher cannot read.");
				}
				list.Add(new Adapter(fields[0], fields[1], fields[2], fields[3]));
			}
			List<Adapter> physical = list.Where((Adapter a) => !a.PhysicalId.Contains(":4294967295:")).ToList();
			Dictionary<string, string> aliases = new Dictionary<string, string>();
			foreach (IGrouping<string, Adapter> device in physical.GroupBy((Adapter a) => a.PhysicalId))
			{
				foreach (Adapter adapter in device)
				{
					aliases[adapter.Luid] = device.First().Luid;
				}
			}
			foreach (Adapter logical in list.Except(physical))
			{
				string deviceId = string.Join(':', logical.PhysicalId.Split(':').Take(2)) + ":";
				List<IGrouping<string, Adapter>> matches = (from a in physical
					where a.PhysicalId.StartsWith(deviceId, StringComparison.Ordinal)
					group a by a.PhysicalId).ToList();
				if (matches.Count == 1)
				{
					aliases[logical.Luid] = matches[0].First().Luid;
				}
			}
			adapterAliases = aliases;
			adapters = (from a in physical
				group a by a.PhysicalId into g
				select g.First()).ToList();
			DetectionError = "";
		}
		catch (IOException ex)
		{
			DetectionError = ex.Message;
		}
		Populate();
	}

	/// <summary>The probe could not list the cards: the saved pick stays, with the reason under the box.</summary>
	public void Failed(string message)
	{
		DetectionError = message;
		Populate();
	}

	/// <summary>Picks the option with this LUID; "" is Automatic.</summary>
	public void Select(string luid)
	{
		selectedLuid = luid;
		Adapter? adapter = adapters.FirstOrDefault((Adapter a) => a.Luid == selectedLuid);
		selectedName = ((selectedLuid.Length == 0) ? "" : (adapter?.Name ?? selectedName));
		selectedPhysicalId = ((selectedLuid.Length == 0) ? "" : (adapter?.PhysicalId ?? selectedPhysicalId));
		Selected = Options.FirstOrDefault((Option o) => o.Luid == luid) ?? Selected;
	}

	/// <summary>Writes the pick as his launcher does; Automatic removes his three keys.</summary>
	public void Save(JsonObject graphics)
	{
		if (!Routed)
		{
			graphics.Remove("nativeGpuLuid");
			graphics.Remove("nativeGpuName");
			graphics.Remove("nativeGpuPhysicalId");
			return;
		}
		if (selectedName.Length > 0)
		{
			graphics.Remove("nativeGpuLuid");
		}
		else
		{
			graphics["nativeGpuLuid"] = selectedLuid;
		}
		graphics["nativeGpuName"] = selectedName;
		graphics["nativeGpuPhysicalId"] = selectedPhysicalId;
	}

	/// <summary>The line under the box.</summary>
	public string Status(bool layerOn)
	{
		if (layerOn)
		{
			return "No graphics card can be picked while the AMD and Intel compatibility layer is on.";
		}
		if (DetectionError.Length > 0)
		{
			return DetectionError;
		}
		if (!Routed)
		{
			return "The game starts on the card Windows gives it. A card picked here is used the next time the game starts.";
		}
		return "Saved by the card's name and device and matched again at every start. If the card is gone or cannot start the game, the game does not start, rather than run on another card.";
	}

	private void Populate()
	{
		Option? saved = null;
		if (selectedName.Length > 0)
		{
			List<Adapter> named = adapters.Where((Adapter a) => string.Equals(a.Name, selectedName, StringComparison.OrdinalIgnoreCase) && (selectedPhysicalId.Length == 0 || a.PhysicalId == selectedPhysicalId)).ToList();
			if (named.Count == 1)
			{
				selectedLuid = named[0].Luid;
				selectedPhysicalId = named[0].PhysicalId;
			}
			else
			{
				saved = new Option(selectedLuid, selectedName + ((named.Count > 1) ? " (two cards have this name; pick one again)" : " (not found)"));
			}
		}
		else
		{
			if (adapterAliases.TryGetValue(selectedLuid, out string? alias))
			{
				selectedLuid = alias;
			}
			selectedName = adapters.FirstOrDefault((Adapter a) => a.Luid == selectedLuid)?.Name ?? "";
		}
		List<Option> options = new List<Option>
		{
			new Option("", "Automatic")
		};
		foreach (Adapter adapter in adapters)
		{
			string suffix = (adapters.Count((Adapter a) => a.Name == adapter.Name) > 1) ? (" · " + adapter.Luid.Substring(8)) : "";
			options.Add(new Option(adapter.Luid, adapter.Name + suffix));
		}
		if (saved != null)
		{
			options.Add(saved);
		}
		else if (!options.Any((Option o) => o.Luid == selectedLuid))
		{
			options.Add(new Option(selectedLuid, ((selectedName.Length > 0) ? selectedName : selectedLuid) + " (not found)"));
		}
		Options = options;
		Selected = saved ?? options.First((Option o) => o.Luid == selectedLuid);
	}
}
