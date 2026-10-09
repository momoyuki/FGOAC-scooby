using System.Windows;
using System.Windows.Input;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
	{
		if (e.ClickCount == 2)
		{
			base.WindowState = ((base.WindowState != WindowState.Maximized) ? WindowState.Maximized : WindowState.Normal);
		}
		else if (e.LeftButton == MouseButtonState.Pressed)
		{
			DragMove();
		}
	}

	private void MinimizeButton_OnClick(object sender, RoutedEventArgs e)
	{
		base.WindowState = WindowState.Minimized;
	}

	private void MaximizeButton_OnClick(object sender, RoutedEventArgs e)
	{
		base.WindowState = ((base.WindowState != WindowState.Maximized) ? WindowState.Maximized : WindowState.Normal);
	}

	private void CloseButton_OnClick(object sender, RoutedEventArgs e)
	{
		Close();
	}

	private void ApplyProtectedBranding()
	{
		base.Title = ProtectedBranding.WindowTitle;
		HeaderBrandText.Text = ProtectedBranding.HeaderText;
	}
}
