using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	private sealed class CapturedProcess
	{
		public Process Process { get; }

		public Task StreamsCompleted { get; }

		public CapturedProcess(Process process, Task streamsCompleted)
		{
			Process = process;
			StreamsCompleted = streamsCompleted;
		}
	}

	private const int MaxLauncherVisibleCharacters = 524288;

	private const int TrimmedLauncherVisibleCharacters = 393216;

	private readonly DispatcherTimer launcherOutputTimer;

	private readonly LauncherOutputBuffer launcherOutput = new LauncherOutputBuffer();

	private bool launcherLogOwned;

	private string injectionLogSnapshot = "";

	/// <summary>The launch script's exit code from the last session, for the Report.</summary>
	private int? lastSessionExit;

	private static CapturedProcess StartCapturedPowerShellScript(string scriptPath, Action<string, bool> onOutputLine, params string[] arguments)
	{
		Process process = new Process
		{
			StartInfo = PowerShellHost.CreateStartInfo(GamePaths.GameRoot, redirectOutput: true, new string[2] { "-File", scriptPath }.Concat(arguments))
		};
		TaskCompletionSource<object?> outputClosed = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource<object?> errorClosed = new TaskCompletionSource<object>(TaskCreationOptions.RunContinuationsAsynchronously);
		process.OutputDataReceived += delegate(object _, DataReceivedEventArgs e)
		{
			if (e.Data == null)
			{
				outputClosed.TrySetResult(null);
			}
			else
			{
				onOutputLine(e.Data, arg2: false);
			}
		};
		process.ErrorDataReceived += delegate(object _, DataReceivedEventArgs e)
		{
			if (e.Data == null)
			{
				errorClosed.TrySetResult(null);
			}
			else
			{
				onOutputLine(e.Data, arg2: true);
			}
		};
		try
		{
			if (!process.Start())
			{
				throw new InvalidOperationException("Could not start " + scriptPath);
			}
			process.BeginOutputReadLine();
			process.BeginErrorReadLine();
		}
		catch
		{
			try
			{
				if (!process.HasExited)
				{
					process.Kill(entireProcessTree: true);
				}
			}
			catch
			{
			}
			process.Dispose();
			throw;
		}
		return new CapturedProcess(process, Task.WhenAll<object>(outputClosed.Task, errorClosed.Task));
	}

	private void BeginLauncherOutputCapture(string launcherPath)
	{
		launcherOutput.Reset();
		launcherLogOwned = true;
		savedReport = null;
		injectionLogSnapshot = "";
		InjectionLogTextBox.Clear();
		AppendInjectionLog("[launcher] Starting " + Path.GetFileName(launcherPath) + "; receiving stdout / stderr..." + Environment.NewLine);
		launcherOutputTimer.Start();
	}

	private void QueueLauncherOutput(string line, bool standardError)
	{
		launcherOutput.Enqueue(Redaction.Apply(line), standardError);
	}

	private void CompleteLauncherOutputCapture(int exitCode)
	{
		lastSessionExit = exitCode;
		QueueLauncherOutput($"[launcher] PowerShell exited: {exitCode} (0x{(uint)exitCode:X8})", exitCode != 0);
		launcherOutput.Complete();
	}

	private void FlushLauncherOutput()
	{
		(string text, bool finished) = launcherOutput.Drain();
		if (text.Length > 0)
		{
			AppendInjectionLog(text);
		}
		if (finished)
		{
			launcherOutputTimer.Stop();
		}
	}

	private void AppendInjectionLog(string text)
	{
		InjectionLogTextBox.AppendText(Redaction.Apply(text));
		if (InjectionLogTextBox.Text.Length > MaxLauncherVisibleCharacters)
		{
			string text2 = InjectionLogTextBox.Text;
			int startIndex = text2.Length - TrimmedLauncherVisibleCharacters;
			int num = text2.IndexOf('\n', startIndex);
			if (num >= 0 && num + 1 < text2.Length)
			{
				startIndex = num + 1;
			}
			InjectionLogTextBox.Text = "[launcher] ...the panel keeps only the newest output, see logs for the full record..." + Environment.NewLine + text2.Substring(startIndex);
		}
		injectionLogSnapshot = InjectionLogTextBox.Text;
		InjectionLogTextBox.CaretIndex = InjectionLogTextBox.Text.Length;
		InjectionLogTextBox.ScrollToEnd();
	}
}
