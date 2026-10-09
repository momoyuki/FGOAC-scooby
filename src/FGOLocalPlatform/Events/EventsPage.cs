using System.IO;
using System.Windows.Controls;

namespace FGOLocalPlatform;

/// <summary>
/// The Events page: which limited events the server on this PC offers. On game 11.00 that is the Banners patch of
/// Cloud23333's 1.02 server files; with his 1.10 server, his own event switches.
/// </summary>
public partial class EventsPage : UserControl
{
	/// <summary>Shows the event switches as the server's settings file holds them now.</summary>
	public void Reload()
	{
		(Switches.Content as EventSwitchesView)?.Reload();
	}

	public EventsPage()
	{
		InitializeComponent();
		string installRoot = Path.GetFullPath(Path.Combine(GamePaths.GameRoot, ".."));
		if (BannerSettingsView.Offered(installRoot))
		{
			Switches.Content = new BannerSettingsView();
		}
		else if (EventSwitches.Offered(Path.Combine(installRoot, "Server")))
		{
			Switches.Content = new EventSwitchesView();
		}
		else
		{
			Switches.Content = new TextBlock
			{
				Style = (System.Windows.Style)FindResource("HelpTextStyle"),
				Margin = new System.Windows.Thickness(28.0, 16.0, 28.0, 0.0),
				Text = "This game folder has no local server with event switches."
			};
		}
	}
}
