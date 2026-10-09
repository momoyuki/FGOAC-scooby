using System.Windows;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	private enum NextStep
	{
		None,
		StartServer,
		StopServer,
		Install,
		WhatsNew,
		OpenFolder
	}

	private NextStep nextStep;

	/// <summary>Puts the step the Status sentence names on the button beside it; None hides the button.</summary>
	private void ShowNextStep(NextStep step)
	{
		nextStep = step;
		StatusNextButton.Content = step switch
		{
			NextStep.StartServer => "Start server",
			NextStep.StopServer => "Stop server",
			NextStep.Install => "Install",
			NextStep.WhatsNew => "What's new",
			NextStep.OpenFolder => "Open folder",
			_ => ""
		};
		StatusNextButton.Visibility = (step == NextStep.None) ? Visibility.Collapsed : Visibility.Visible;
	}

	private void StatusNextButton_OnClick(object sender, RoutedEventArgs e)
	{
		switch (nextStep)
		{
		case NextStep.StartServer:
			StartServerButton_OnClick(sender, e);
			break;
		case NextStep.StopServer:
			StopServerButton_OnClick(sender, e);
			break;
		case NextStep.Install:
			InstallUpdate();
			break;
		case NextStep.WhatsNew:
			ShowWhatsNew();
			break;
		case NextStep.OpenFolder:
			OpenReportFolder();
			break;
		}
	}
}
