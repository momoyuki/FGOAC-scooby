using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace FGOLocalPlatform;

/// <summary>
/// Cloud23333's start-up rule, ported unchanged from his 1.10 launcher: a window that is not maximized and lies partly
/// off the work area of the monitor nearest to it is moved back onto that work area, and shrunk where it is larger.
/// Maximized, the window fills that work area (KeepMaximizedOnWorkArea).
/// </summary>
internal static class WindowVisibility
{
	internal struct Bounds
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;
	}

	private struct MonitorInfo
	{
		public int Size;

		public Bounds Monitor;

		public Bounds Work;

		public uint Flags;
	}

	[DllImport("user32.dll")]
	private static extern bool GetWindowRect(IntPtr window, out Bounds rect);

	[DllImport("user32.dll")]
	private static extern IntPtr MonitorFromRect(ref Bounds rect, uint flags);

	[DllImport("user32.dll")]
	private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

	[DllImport("user32.dll")]
	private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

	[DllImport("user32.dll")]
	private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

	private const int WmGetMinMaxInfo = 0x0024;

	/// <summary>The work area as WM_GETMINMAXINFO takes the maximized box: measured from the monitor's top left corner.</summary>
	internal static Bounds MaximizedArea(Bounds monitor, Bounds work)
	{
		return new Bounds
		{
			Left = work.Left - monitor.Left,
			Top = work.Top - monitor.Top,
			Right = work.Right - monitor.Left,
			Bottom = work.Bottom - monitor.Top
		};
	}

	/// <summary>
	/// A window with WindowStyle None maximizes over the whole monitor, taskbar included; its maximized box is set to the
	/// work area of the monitor it is on. The minimum size stays WPF's.
	/// </summary>
	internal static void KeepMaximizedOnWorkArea(Window window)
	{
		window.SourceInitialized += delegate
		{
			HwndSource.FromHwnd(new WindowInteropHelper(window).Handle)?.AddHook(delegate(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
			{
				if (msg == WmGetMinMaxInfo)
				{
					MonitorInfo info = new MonitorInfo
					{
						Size = Marshal.SizeOf<MonitorInfo>()
					};
					if (GetMonitorInfo(MonitorFromWindow(hwnd, 2u), ref info))
					{
						// MINMAXINFO: ptMaxSize at bytes 8 and 12, ptMaxPosition at 16 and 20.
						Bounds area = MaximizedArea(info.Monitor, info.Work);
						Marshal.WriteInt32(lParam, 8, area.Right - area.Left);
						Marshal.WriteInt32(lParam, 12, area.Bottom - area.Top);
						Marshal.WriteInt32(lParam, 16, area.Left);
						Marshal.WriteInt32(lParam, 20, area.Top);
					}
				}
				return IntPtr.Zero;
			});
		};
	}

	internal static Bounds Fit(Bounds window, Bounds work)
	{
		int num = Math.Min(window.Right - window.Left, work.Right - work.Left);
		int num2 = Math.Min(window.Bottom - window.Top, work.Bottom - work.Top);
		int num3 = Math.Clamp(window.Left, work.Left, work.Right - num);
		int num4 = Math.Clamp(window.Top, work.Top, work.Bottom - num2);
		return new Bounds
		{
			Left = num3,
			Top = num4,
			Right = num3 + num,
			Bottom = num4 + num2
		};
	}

	internal static void EnsureVisible(Window window)
	{
		if (window.WindowState == WindowState.Maximized)
		{
			return;
		}
		IntPtr handle = new WindowInteropHelper(window).Handle;
		if (!GetWindowRect(handle, out var rect))
		{
			return;
		}
		IntPtr monitor = MonitorFromRect(ref rect, 2u);
		MonitorInfo info = new MonitorInfo
		{
			Size = Marshal.SizeOf<MonitorInfo>()
		};
		if (GetMonitorInfo(monitor, ref info))
		{
			Bounds bounds = Fit(rect, info.Work);
			if (bounds.Left != rect.Left || bounds.Top != rect.Top || bounds.Right != rect.Right || bounds.Bottom != rect.Bottom)
			{
				SetWindowPos(handle, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, 20u);
			}
		}
	}
}
