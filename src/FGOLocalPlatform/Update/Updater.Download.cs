using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FGOLocalPlatform;

internal static partial class Updater
{
	/// <summary>
	/// Full control for SYSTEM, Administrators and this process's default owner, which is
	/// Administrators when elevated, inherited by update.zip: a program running as the same user
	/// without elevation can neither write into the staging folder nor plant a link inside it.
	/// </summary>
	private static DirectorySecurity StagingSecurity()
	{
		using WindowsIdentity self = WindowsIdentity.GetCurrent();
		DirectorySecurity security = new DirectorySecurity();
		security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
		foreach (IdentityReference identity in new IdentityReference[3]
		{
			new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
			new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
			self.Owner
		})
		{
			security.AddAccessRule(new FileSystemAccessRule(identity, FileSystemRights.FullControl, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
		}
		return security;
	}

	/// <summary>
	/// Writes the body at <paramref name="url" /> into <paramref name="file" />. It stops when nothing
	/// arrives for 15 seconds; the download as a whole has no limit.
	/// </summary>
	private static async Task DownloadAsync(string url, Stream file, CancellationToken cancellation)
	{
		using CancellationTokenSource idle = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
		idle.CancelAfter(ReadTimeout);
		try
		{
			using HttpResponseMessage response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, idle.Token);
			response.EnsureSuccessStatusCode();
			using Stream body = await response.Content.ReadAsStreamAsync(idle.Token);
			byte[] buffer = new byte[81920];
			int read;
			while ((read = await body.ReadAsync(buffer, idle.Token)) > 0)
			{
				idle.CancelAfter(ReadTimeout);
				await file.WriteAsync(buffer.AsMemory(0, read), cancellation);
			}
		}
		catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
		{
			throw new IOException("The download stopped: nothing arrived for 15 seconds");
		}
	}

	/// <summary>The .sig text, or null when it is missing, over 4 KB or not all there within 15 seconds.</summary>
	private static async Task<string> ReadSignatureAsync(string url, CancellationToken cancellation)
	{
		using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
		timeout.CancelAfter(ReadTimeout);
		try
		{
			using HttpResponseMessage response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
			if (response.StatusCode == HttpStatusCode.NotFound)
			{
				Log("The signature file is not there (HTTP 404).");
				return null;
			}
			response.EnsureSuccessStatusCode();
			using Stream body = await response.Content.ReadAsStreamAsync(timeout.Token);
			byte[] buffer = new byte[4097];
			int length = 0;
			int read;
			while (length < buffer.Length && (read = await body.ReadAsync(buffer, length, buffer.Length - length, timeout.Token)) > 0)
			{
				length += read;
			}
			if (length > 4096)
			{
				Log("The signature file is over 4 KB.");
				return null;
			}
			return Encoding.ASCII.GetString(buffer, 0, length);
		}
		catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
		{
			Log("The signature file did not arrive within 15 seconds.");
			return null;
		}
	}
}
