using System.IO;
using System.Windows;
using System.Windows.Markup;

namespace FGOLocalPlatform;

public partial class NamePromptDialog : Window, IComponentConnector
{
	/// <summary>The name the player typed, or null when the dialog was cancelled.</summary>
	public string? Result { get; private set; }

	public NamePromptDialog(Window owner, string title, string label, string initial)
	{
		InitializeComponent();
		WindowTheme.Apply(this);
		base.Owner = owner;
		base.Title = title;
		LabelText.Text = label;
		NameTextBox.Text = initial;
		NameTextBox.SelectAll();
		NameTextBox.Focus();
	}

	private void SaveButton_OnClick(object sender, RoutedEventArgs e)
	{
		// The name becomes a file name, so the characters a file name cannot hold become dashes.
		string name = NameTextBox.Text.Trim();
		foreach (char c in Path.GetInvalidFileNameChars())
		{
			name = name.Replace(c, '-');
		}
		if (name.Length == 0)
		{
			return;
		}
		Result = name;
		base.DialogResult = true;
	}
}
