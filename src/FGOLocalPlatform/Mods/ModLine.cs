using System.ComponentModel;

namespace FGOLocalPlatform;

public sealed class ModLine : INotifyPropertyChanged
{
	private string portrait = "";

	private string name = "";

	private string text = "";

	public string Portrait
	{
		get
		{
			return portrait;
		}
		set
		{
			portrait = value;
			Changed("Portrait");
		}
	}

	public string Name
	{
		get
		{
			return name;
		}
		set
		{
			name = value;
			Changed("Name");
		}
	}

	public string Text
	{
		get
		{
			return text;
		}
		set
		{
			text = value;
			Changed("Text");
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	private void Changed(string field)
	{
		this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(field));
	}
}
