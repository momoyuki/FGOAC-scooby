using System.Windows;
using System.Windows.Markup;

namespace FGOLocalPlatform;

public partial class WhatsNewDialog : Window, IComponentConnector
{
	public WhatsNewDialog(Window owner, string version, string body)
	{
		InitializeComponent();
		WindowTheme.Apply(this);
		base.Owner = owner;
		HeadingText.Text = "Updated to " + version;
		BodyText.Text = body;
	}
}
