using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace FGOLocalPlatform;

/// <summary>The PC play card: the five PC port switches. A change asks the window to save the Settings file.</summary>
public partial class PcPlaySettingsView : UserControl, IComponentConnector
{
	private bool showing;

	public event EventHandler? SaveRequested;

	public PcPlaySettingsView()
	{
		InitializeComponent();
		Switches = PcPortSwitches.Defaults;
	}

	public PcPortSwitches Switches
	{
		get
		{
			return new PcPortSwitches(NoContinueTimerSwitch.IsChecked == true, NoPopupTimersSwitch.IsChecked == true, NoGpPurchaseSwitch.IsChecked == true, NoStoryTimerSwitch.IsChecked == true, NoMenuTimerSwitch.IsChecked == true);
		}
		set
		{
			showing = true;
			NoContinueTimerSwitch.IsChecked = value.NoContinueTimer;
			NoPopupTimersSwitch.IsChecked = value.NoPopupTimers;
			NoGpPurchaseSwitch.IsChecked = value.NoGpPurchase;
			NoStoryTimerSwitch.IsChecked = value.NoStoryTimer;
			NoMenuTimerSwitch.IsChecked = value.NoMenuTimer;
			showing = false;
		}
	}

	private void Switch_OnChanged(object sender, RoutedEventArgs e)
	{
		if (!showing)
		{
			this.SaveRequested?.Invoke(this, EventArgs.Empty);
		}
	}
}
