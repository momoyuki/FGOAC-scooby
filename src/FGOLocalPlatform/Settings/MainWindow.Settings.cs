using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	/// <summary>Hides the Settings sections whose title does not contain the filter text.</summary>
	private void SettingsFilterBox_OnTextChanged(object sender, TextChangedEventArgs e)
	{
		string filter = SettingsFilterBox.Text.Trim();
		SettingsFilterHint.Visibility = (SettingsFilterBox.Text.Length == 0) ? Visibility.Visible : Visibility.Collapsed;
		bool any = false;
		foreach (Border section in SettingsSections.Children.OfType<Border>())
		{
			bool match = section.Tag is string title && title.Contains(filter, StringComparison.OrdinalIgnoreCase);
			section.Visibility = (match ? Visibility.Visible : Visibility.Collapsed);
			any |= match;
		}
		SettingsFilterEmptyText.Visibility = (any ? Visibility.Collapsed : Visibility.Visible);
		SettingsScroller.ScrollToTop();
	}

	/// <summary>
	/// The sections of Settings and Controls keep their own scroll viewers, which take the wheel even with nothing to
	/// scroll; the page scrolls unless the viewer under the pointer can still move that way. Drop-downs are left alone.
	/// </summary>
	private void PageScroller_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
	{
		ScrollViewer page = (ScrollViewer)sender;
		DependencyObject? node = e.OriginalSource as DependencyObject;
		while (node != null && node != page)
		{
			if (node is ScrollViewer inner && (e.Delta < 0 ? inner.VerticalOffset < inner.ScrollableHeight : inner.VerticalOffset > 0))
			{
				return;
			}
			node = (node is Visual) ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
		}
		// A drop-down's own tree ends before the page does.
		if (node == null)
		{
			return;
		}
		page.ScrollToVerticalOffset(page.VerticalOffset - e.Delta * 0.4);
		e.Handled = true;
	}
}
