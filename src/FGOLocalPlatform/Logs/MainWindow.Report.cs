using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	/// <summary>The Report the Status names, with Open folder as its Next step, until that is pressed or the next session.</summary>
	private string? savedReport;

	private async void SaveReportItem_OnClick(object sender, RoutedEventArgs e)
	{
		SaveFileDialog dialog = new SaveFileDialog
		{
			Title = "Save a report",
			InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
			FileName = Report.FileName(DateTime.Now),
			Filter = "Zip file (*.zip)|*.zip",
			DefaultExt = ".zip",
			AddExtension = true
		};
		if (DialogSink.ShowDialog(dialog, this) != true)
		{
			return;
		}
		string path = dialog.FileName;
		// The mode and the Status of the page whose Play started the last session.
		bool online = CommunityServer.OnlineSession(GamePaths.GameRoot);
		Report.Facts facts = new Report.Facts(UpdateSettings.Version, online ? "Online" : "Local", (online ? OnlineStatusText : RuntimeStatusText).Text, InjectionLogTextBox.Text, lastSessionExit);
		try
		{
			await Task.Run(() => Report.Save(path, GamePaths.GameRoot, Path.GetTempPath(), facts));
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
		{
			HoldStatus("The report could not be saved: " + ex.Message.TrimEnd('.') + ". Choose another folder, then try again.", problem: true);
			return;
		}
		savedReport = path;
		AppendServerControlLog("Report saved: " + path);
		statusHoldUntil = DateTime.MinValue;
		await RefreshRuntimeStatusAsync();
	}

	private void OpenReportFolder()
	{
		string? path = savedReport;
		savedReport = null;
		if (path != null)
		{
			DialogSink.Open("explorer.exe", "/select,\"" + path + "\"");
		}
		_ = RefreshRuntimeStatusAsync();
	}
}
