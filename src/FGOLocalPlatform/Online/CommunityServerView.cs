using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace FGOLocalPlatform;

/// <summary>The community server's address block on the Online page: the boxes, Save, and Enter to save.</summary>
public partial class CommunityServerView : UserControl, IComponentConnector
{
	private bool loaded;

	/// <summary>Raised after a save, with the settings now in the Settings file.</summary>
	public event EventHandler? Saved;

	public CommunityServerView()
	{
		InitializeComponent();
	}

	private void Fill(CommunityServer.Settings settings)
	{
		CommunityHostBox.Text = settings.Host;
		CommunityTlsPortBox.Text = settings.TlsPort.ToString();
		CommunityPinBox.Text = settings.CertificatePin;
		CommunityUdpPingBox.Text = settings.UdpPingPort.ToString();
		CommunityUdpDistBox.Text = settings.UdpDistPort.ToString();
		CommunityHttpBox.Text = settings.Http.ToString();
		CommunityBillingBox.Text = settings.Billing.ToString();
		CommunityAimeBox.Text = settings.Aime.ToString();
	}

	/// <summary>The boxes as settings with the given flag; an empty address clears the saved one.</summary>
	private CommunityServer.Settings SettingsFromBoxes(bool enabled)
	{
		string host = CommunityHostBox.Text.Trim();
		if (host.Length > 0)
		{
			host = CommunityServer.NormalizeHost(host);
		}
		int[] ports = new string[6] { CommunityTlsPortBox.Text, CommunityUdpPingBox.Text, CommunityUdpDistBox.Text, CommunityHttpBox.Text, CommunityBillingBox.Text, CommunityAimeBox.Text }.Select(delegate(string value)
		{
			if (!int.TryParse(value, out var result) || result < 1 || result > 65535)
			{
				throw new ArgumentException("Each port must be a whole number from 1 to 65535.");
			}
			return result;
		}).ToArray();
		if (ports.Skip(3).Concat(new int[3] { CommunityServer.MsPort, CommunityServer.PingPort, CommunityServer.DistPort }).Distinct().Count() != 6)
		{
			throw new ArgumentException("The title, billing and card reader ports cannot share a number, and cannot be 30110, 30111 or 30112, which the game uses for matching.");
		}
		string pin = CommunityServer.NormalizePin(CommunityPinBox.Text);
		return new CommunityServer.Settings(enabled, host, ports[0], pin, ports[1], ports[2], ports[3], ports[4], ports[5], CommunityServer.MsPort, CommunityServer.PingPort, CommunityServer.DistPort);
	}

	private void Save()
	{
		if (Process.GetProcessesByName("ago").Length != 0)
		{
			ShowNote("Quit the game before changing the address.", problem: true);
			return;
		}
		try
		{
			CommunityServer.Settings settings = SettingsFromBoxes(CommunityServer.Load(GamePaths.GameRoot).Enabled);
			CommunityServer.Save(GamePaths.GameRoot, settings);
			Fill(settings);
			ShowNote("Saved.", problem: false);
			Saved?.Invoke(this, EventArgs.Empty);
		}
		catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException || ex is JsonException || ex is InvalidDataException)
		{
			ShowNote(ex.Message, problem: true);
		}
	}

	private void ShowNote(string text, bool problem)
	{
		SaveNote.Text = text;
		SaveNote.Foreground = (Brush)FindResource(problem ? "WarnBrush" : "TextSoftBrush");
	}

	private void Save_OnClick(object sender, RoutedEventArgs e)
	{
		Save();
	}

	private void View_OnPreviewKeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Enter && e.OriginalSource is TextBox)
		{
			e.Handled = true;
			Save();
		}
	}

	private void View_OnLoaded(object sender, RoutedEventArgs e)
	{
		if (!loaded)
		{
			loaded = true;
			Fill(CommunityServer.Load(GamePaths.GameRoot));
		}
	}
}
