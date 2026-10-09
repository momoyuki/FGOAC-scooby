using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace FGOLocalPlatform;

/// <summary>The Mods page: Cloud23333's MOD workbench on App\1_MOD, which only the hooks of his latest update read.</summary>
public partial class ModsPage : UserControl
{
	public ModsPage()
	{
		InitializeComponent();
		if (GameBuild.Is1100(Path.GetFullPath(Path.Combine(GamePaths.GameRoot, ".."))))
		{
			StatusText.Text = "Mods need Cloud23333's latest update, which this game folder does not have yet.";
			return;
		}
		StatusText.Visibility = Visibility.Collapsed;
		Workbench.Content = new ModSettingsView();
	}
}
