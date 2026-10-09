using System;
using System.IO;
using System.Threading.Tasks;

namespace FGOLocalPlatform;

public partial class MainWindow
{
	/// <summary>The last probe: the settings and card it dialled with, when it ended, and its refusal or null.</summary>
	private (CommunityServer.Settings Settings, string Card, DateTime At, string? Refusal)? lastProbe;

	/// <summary>True while the probe of the saved server runs (Online page shown, Check again).</summary>
	private bool savedServerProbing;

	/// <summary>True while the probe before Play Online runs.</summary>
	private bool sessionProbing;

	private bool probing => savedServerProbing || sessionProbing;

	/// <summary>
	/// Probes the saved community server once this PC holds its card: when the Online page is shown and the saved
	/// address or card has no probe yet, and on Check again; never on a timer.
	/// </summary>
	private async Task ProbeCommunityServerAsync(bool again = false)
	{
		CommunityServer.Settings settings = SavedServer();
		if (settings.Host.Length == 0 || probing || playStarting || launcherProcessRunning || windowClosing)
		{
			return;
		}
		string? card = CardOrNull(settings.Host);
		if (card == null || (!again && lastProbe is { } probe && probe.Settings == settings && probe.Card == card))
		{
			return;
		}
		savedServerProbing = true;
		string? refusal;
		try
		{
			await RefreshRuntimeStatusAsync();
			refusal = await CommunityTunnel.ProbeAsync(settings, card, ReportProbe);
		}
		finally
		{
			savedServerProbing = false;
		}
		lastProbe = (settings, card, DateTime.Now, refusal);
		FlushLauncherOutput();
		await RefreshRuntimeStatusAsync();
	}

	/// <summary>The probe on the tunnel Play Online started, with the Online Status saying so meanwhile; returns the refusal, or null to go on to the launch script.</summary>
	private async Task<string?> ProbeSessionTunnelAsync(CommunityTunnel tunnel)
	{
		CommunityServer.Settings settings = tunnel.Settings;
		sessionProbing = true;
		ShowOnlineStatus("Checking the community server " + settings.Host + "...", "busy");
		string? refusal;
		try
		{
			refusal = await tunnel.ProbeAsync();
		}
		finally
		{
			sessionProbing = false;
		}
		string? card = CardOrNull(settings.Host);
		if (card != null)
		{
			lastProbe = (settings, card, DateTime.Now, refusal);
		}
		if (refusal != null)
		{
			FlushLauncherOutput();
		}
		return refusal;
	}

	/// <summary>The saved community server as the probe and the tunnel dial it; the flag of the last Play is no part of it.</summary>
	private static CommunityServer.Settings SavedServer()
	{
		return CommunityServer.Load(GamePaths.GameRoot) with { Enabled = false };
	}

	/// <summary>The Online Status from the last probe of these settings and this card, or null when there is none.</summary>
	private (string Text, string Tone)? ProbeStatus(CommunityServer.Settings settings, string card)
	{
		if (probing)
		{
			return ("Checking the community server " + settings.Host + "...", "busy");
		}
		if (lastProbe is { } probe && probe.Settings == settings && probe.Card == card)
		{
			return (probe.Refusal != null) ? (probe.Refusal, "problem") : ($"Online, {settings.Host}, reachable, checked {probe.At:HH:mm}", "ready");
		}
		return null;
	}

	/// <summary>This PC's card for the host, or null when it has none or it cannot be read.</summary>
	private static string? CardOrNull(string host)
	{
		try
		{
			return CommunityServer.ReadCard(GamePaths.GameRoot, host);
		}
		catch (Exception ex) when (ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
		{
			return null;
		}
	}

	/// <summary>A probe's lines go to the Game log and logs\server-control.log, as the tunnel's do, without holding the Status.</summary>
	private void ReportProbe(string message, bool problem)
	{
		QueueLauncherOutput("[tunnel] " + message, problem);
		AppendServerControlLog("[tunnel] " + message);
	}
}
