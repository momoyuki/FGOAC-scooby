using System.Text.Json.Nodes;
using System.Windows;

namespace FGOLocalPlatform;

/// <summary>
/// The Display card's 240 FPS, high frame rate timing, VSync and AMD anti-tearing: Cloud23333's keys, shown on his
/// build only. On 11.00 the 240 FPS pick is taken out of the list, since that launch script caps the game at 60.
/// </summary>
public partial class MainWindow
{
	private void ShowFrameSync(JsonObject? graphics)
	{
		bool offered = SceneGraphicsOffered;
		FrameSyncPanel.Visibility = offered ? Visibility.Visible : Visibility.Collapsed;
		if (!offered)
		{
			FrameRateComboBox.Items.Remove(FrameRate240Item);
		}
		FrameSync sync = FrameSync.Read(graphics);
		HighFpsV2CheckBox.IsChecked = sync.HighFpsV2;
		VSyncCheckBox.IsChecked = sync.VSync;
		AmdPresentSyncCheckBox.IsChecked = sync.AmdPresentSync;
	}

	/// <summary>On his build, writes the frame rate and sync switches into <paramref name="graphics"/>.</summary>
	private void WriteFrameSync(JsonObject graphics)
	{
		if (SceneGraphicsOffered)
		{
			new FrameSync(HighFpsV2CheckBox.IsChecked == true, VSyncCheckBox.IsChecked == true, AmdPresentSyncCheckBox.IsChecked == true).Write(graphics);
		}
	}
}
