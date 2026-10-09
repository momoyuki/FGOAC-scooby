using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace FGOLocalPlatform;

/// <summary>
/// Cloud23333's display snapshot, ported unchanged from his 1.10 launcher: each active display path's monitor,
/// connection, refresh rate, rotation and scaling, and the client size, DPI and monitor of the game's window, as they
/// are when the report is saved. Win32 only; nothing is read from the game's memory or the driver settings.
/// </summary>
internal static class DisplayPresentationDiagnostics
{
#pragma warning disable CS0649 // QueryDisplayConfig fills these structures in.
	private struct Luid
	{
		public uint Low;

		public int High;

		public override string ToString()
		{
			return $"{(uint)High:X8}{Low:X8}";
		}
	}

	private struct Rational
	{
		public uint Numerator;

		public uint Denominator;
	}

	private struct Source
	{
		public Luid Adapter;

		public uint Id;

		public uint Mode;

		public uint Status;
	}

	private struct Target
	{
		public Luid Adapter;

		public uint Id;

		public uint Mode;

		public uint Technology;

		public uint Rotation;

		public uint Scaling;

		public Rational Refresh;

		public uint Scanline;

		public uint Available;

		public uint Status;
	}

	private struct DisplayPath
	{
		public Source Source;

		public Target Target;

		public uint Flags;
	}

#pragma warning restore CS0649

	private struct Header
	{
		public uint Type;

		public uint Size;

		public Luid Adapter;

		public uint Id;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct TargetName
	{
		public Header Header;

		public uint Flags;

		public uint Technology;

		public ushort Manufacturer;

		public ushort Product;

		public uint Connector;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
		public string Friendly;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string DevicePath;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct SourceName
	{
		public Header Header;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string Device;
	}

	private struct Rect
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct MonitorInfo
	{
		public uint Size;

		public Rect Monitor;

		public Rect Work;

		public uint Flags;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string Device;
	}

	[DllImport("user32.dll")]
	private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);

	[DllImport("user32.dll")]
	private static extern int QueryDisplayConfig(uint flags, ref uint paths, [Out] DisplayPath[] pathInfo, ref uint modes, IntPtr modeInfo, IntPtr topology);

	[DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
	private static extern int TargetInfo(ref TargetName name);

	[DllImport("user32.dll", EntryPoint = "DisplayConfigGetDeviceInfo")]
	private static extern int SourceInfo(ref SourceName name);

	[DllImport("user32.dll")]
	private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

	[DllImport("user32.dll", CharSet = CharSet.Unicode)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetClientRect(IntPtr window, out Rect rect);

	[DllImport("user32.dll")]
	private static extern uint GetDpiForWindow(IntPtr window);

	internal static JsonObject Capture(string gameRoot)
	{
		JsonObject jsonObject = new JsonObject
		{
			["schema"] = "fgo-display-presentation-snapshot-v1",
			["capturedUtc"] = DateTime.UtcNow,
			["scope"] = "Export-time active Windows display paths and matching ago.exe window; not historical frame or scanout evidence.",
			["actualPresentMode"] = "not_measured",
			["vrrActuallyActive"] = "not_measured",
			["driverVsyncOverride"] = "not_measured",
			["wglSwapInterval"] = "not_measured",
			["limitations"] = "Configured refresh is not instantaneous VRR refresh. Output adapter LUID is not necessarily the rendering GPU. DWM enabled/windowed style does not prove composed presentation. No process memory or driver settings are read."
		};
		if (!OperatingSystem.IsWindows())
		{
			jsonObject["error"] = "Windows display APIs unavailable";
			return jsonObject;
		}
		try
		{
			CapturePaths(jsonObject);
		}
		catch (Exception ex) when (((ex is Win32Exception || ex is DllNotFoundException || ex is EntryPointNotFoundException || ex is InvalidOperationException) ? 1 : 0) != 0)
		{
			jsonObject["displayError"] = ex.GetType().Name + ": " + ex.Message;
		}
		jsonObject["gameWindows"] = CaptureWindows(gameRoot);
		return jsonObject;
	}

	internal static bool NativeLayoutValid()
	{
		if (Marshal.SizeOf<DisplayPath>() == 72 && Marshal.SizeOf<TargetName>() == 420 && Marshal.SizeOf<SourceName>() == 84)
		{
			return Marshal.SizeOf<Header>() == 20;
		}
		return false;
	}

	private static void CapturePaths(JsonObject output)
	{
		if (!NativeLayoutValid())
		{
			throw new InvalidOperationException("Unexpected native display structure layout");
		}
		for (int i = 0; i < 3; i++)
		{
			uint paths;
			uint modes;
			int num = GetDisplayConfigBufferSizes(2u, out paths, out modes);
			if (num != 0)
			{
				output["displayErrorCode"] = num;
				return;
			}
			if (paths > 256 || modes > 1024)
			{
				throw new InvalidOperationException("Display topology exceeds snapshot bound");
			}
			DisplayPath[] array = new DisplayPath[Math.Max(1, (int)paths)];
			IntPtr intPtr = Marshal.AllocHGlobal(Math.Max(1, (int)modes) * 64);
			try
			{
				num = QueryDisplayConfig(2u, ref paths, array, ref modes, intPtr, IntPtr.Zero);
			}
			finally
			{
				Marshal.FreeHGlobal(intPtr);
			}
			switch (num)
			{
			default:
				output["displayErrorCode"] = num;
				return;
			case 0:
			{
				JsonArray jsonArray = new JsonArray();
				for (int j = 0; j < paths; j++)
				{
					DisplayPath displayPath = array[j];
					SourceName name = new SourceName
					{
						Header = new Header
						{
							Type = 1u,
							Size = 84u,
							Adapter = displayPath.Source.Adapter,
							Id = displayPath.Source.Id
						},
						Device = ""
					};
					TargetName name2 = new TargetName
					{
						Header = new Header
						{
							Type = 2u,
							Size = 420u,
							Adapter = displayPath.Target.Adapter,
							Id = displayPath.Target.Id
						},
						Friendly = "",
						DevicePath = ""
					};
					int num2 = SourceInfo(ref name);
					int num3 = TargetInfo(ref name2);
					JsonObject value = new JsonObject
					{
						["sourceAdapterLuid"] = displayPath.Source.Adapter.ToString(),
						["outputAdapterLuid"] = displayPath.Target.Adapter.ToString(),
						["sourceId"] = displayPath.Source.Id,
						["targetId"] = displayPath.Target.Id,
						["pathFlags"] = displayPath.Flags,
						["sourceNameResult"] = num2,
						["targetNameResult"] = num3,
						["gdiDevice"] = ((num2 == 0) ? name.Device : null),
						["monitorModel"] = ((num3 == 0) ? name2.Friendly : null),
						["connection"] = Connection(displayPath.Target.Technology),
						["connectionCode"] = displayPath.Target.Technology,
						["rotation"] = displayPath.Target.Rotation,
						["scaling"] = displayPath.Target.Scaling,
						["scanlineOrdering"] = displayPath.Target.Scanline,
						["available"] = displayPath.Target.Available != 0,
						["refreshNumerator"] = displayPath.Target.Refresh.Numerator,
						["refreshDenominator"] = displayPath.Target.Refresh.Denominator,
						["configuredRefreshHz"] = Rate(displayPath.Target.Refresh.Numerator, displayPath.Target.Refresh.Denominator)
					};
					jsonArray.Add(value);
				}
				output["activePaths"] = jsonArray;
				output["displayErrorCode"] = 0;
				return;
			}
			case 122:
				break;
			}
		}
		output["displayErrorCode"] = 122;
	}

	internal static double? Rate(uint numerator, uint denominator)
	{
		if (denominator != 0)
		{
			return (double)numerator / (double)denominator;
		}
		return null;
	}

	internal static string Connection(uint technology)
	{
		return technology switch
		{
			0u => "VGA/HD15", 
			4u => "DVI", 
			5u => "HDMI", 
			6u => "LVDS", 
			10u => "DisplayPort external", 
			11u => "DisplayPort embedded", 
			15u => "Miracast", 
			16u => "Indirect wired", 
			17u => "Indirect virtual", 
			2147483648u => "Internal", 
			_ => "Other/unknown", 
		};
	}

	private static JsonArray CaptureWindows(string gameRoot)
	{
		JsonArray jsonArray = new JsonArray();
		string fullPath = Path.GetFullPath(Path.Combine(gameRoot, "ago.exe"));
		Process[] processesByName = Process.GetProcessesByName("ago");
		try
		{
			foreach (Process item in processesByName.Take(8))
			{
				try
				{
					if (!string.Equals(item.MainModule?.FileName, fullPath, StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
					IntPtr mainWindowHandle = item.MainWindowHandle;
					JsonObject jsonObject = new JsonObject
					{
						["pid"] = item.Id,
						["windowFound"] = mainWindowHandle != IntPtr.Zero,
						["coordinateScope"] = "Win32 window coordinates under caller DPI awareness; not internal framebuffer dimensions."
					};
					jsonArray.Add(jsonObject);
					if (!(mainWindowHandle == IntPtr.Zero))
					{
						jsonObject["dpi"] = GetDpiForWindow(mainWindowHandle);
						if (GetClientRect(mainWindowHandle, out var rect))
						{
							jsonObject["clientWidth"] = rect.Right - rect.Left;
							jsonObject["clientHeight"] = rect.Bottom - rect.Top;
						}
						MonitorInfo info = new MonitorInfo
						{
							Size = (uint)Marshal.SizeOf<MonitorInfo>(),
							Device = ""
						};
						if (GetMonitorInfo(MonitorFromWindow(mainWindowHandle, 2u), ref info))
						{
							jsonObject["nearestGdiDevice"] = info.Device;
							jsonObject["monitorWidth"] = info.Monitor.Right - info.Monitor.Left;
							jsonObject["monitorHeight"] = info.Monitor.Bottom - info.Monitor.Top;
						}
					}
				}
				catch (Exception ex) when (((ex is Win32Exception || ex is InvalidOperationException || ex is EntryPointNotFoundException) ? 1 : 0) != 0)
				{
					jsonArray.Add(new JsonObject
					{
						["pid"] = item.Id,
						["error"] = ex.GetType().Name
					});
				}
			}
			return jsonArray;
		}
		finally
		{
			Process[] array = processesByName;
			for (int i = 0; i < array.Length; i++)
			{
				array[i].Dispose();
			}
		}
	}
}
