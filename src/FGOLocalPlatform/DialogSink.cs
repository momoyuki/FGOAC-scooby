using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;

namespace FGOLocalPlatform;

/// <summary>
/// Every message box, dialog, window and shell open of the launcher goes through here. After <see cref="Capture" />
/// (the test suites call it) none of them reaches the screen: each is recorded in <see cref="Captured" /> instead.
/// </summary>
internal static class DialogSink
{
	private static int shown;

	/// <summary>"caption: text" per message box, "dialog: title" per dialog or window, "open: target" per shell open.</summary>
	public static List<string>? Captured { get; private set; }

	public static void Capture()
	{
		if (Captured != null)
		{
			return;
		}
		Captured = new List<string>();
		EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(delegate
		{
			shown++;
		}));
	}

	/// <summary>Windows shown since <see cref="Capture" />, after this thread's queued work has run.</summary>
	public static int ShownWindows()
	{
		Dispatcher.CurrentDispatcher.Invoke(delegate
		{
		}, DispatcherPriority.Background);
		return shown;
	}

	/// <summary>True when the sink took the message box, which then shows nothing.</summary>
	public static bool Take(string caption, string text)
	{
		Captured?.Add(caption + ": " + text);
		return Captured != null;
	}

	public static bool? ShowDialog(Window dialog)
	{
		if (Take("dialog", dialog.Title))
		{
			return null;
		}
		return dialog.ShowDialog();
	}

	public static bool? ShowDialog(CommonDialog dialog, Window? owner)
	{
		if (Take("dialog", (dialog as FileDialog)?.Title ?? dialog.GetType().Name))
		{
			return null;
		}
		return (owner == null) ? dialog.ShowDialog() : dialog.ShowDialog(owner);
	}

	public static void Show(Window window)
	{
		if (!Take("dialog", window.Title))
		{
			window.Show();
		}
	}

	/// <summary>The plain system message box, for a failure before the launcher's resources exist.</summary>
	public static void ShowSystemBox(string text, string caption)
	{
		if (!Take(caption, text))
		{
			MessageBox.Show(text, caption, MessageBoxButton.OK, MessageBoxImage.Hand);
		}
	}

	/// <summary>Opens a folder, file or address in its shell program.</summary>
	public static void Open(string target, string arguments = "")
	{
		if (!Take("open", (target + " " + arguments).Trim()))
		{
			Process.Start(new ProcessStartInfo(target, arguments)
			{
				UseShellExecute = true
			})?.Dispose();
		}
	}
}
