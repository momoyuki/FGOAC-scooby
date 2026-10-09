using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace FGOLocalPlatform;

internal static partial class Updater
{
	/// <summary>
	/// True when a line of <paramref name="signatureText" />, base64 of a 64-byte IEEE P1363 ECDSA
	/// P-256 signature, verifies under one of UpdateSettings.ReleaseKeys for the ASCII statement
	/// "FGOAC-scooby {version} {lower-case hex SHA-256 of the zip}", with no trailing newline.
	/// </summary>
	private static bool IsSigned(string version, string sha256, string signatureText)
	{
		byte[] statement = Encoding.ASCII.GetBytes("FGOAC-scooby " + version + " " + sha256);
		List<ECDsa> keys = new List<ECDsa>();
		try
		{
			for (int i = 0; i < UpdateSettings.ReleaseKeys.Length; i++)
			{
				ECDsa key = ECDsa.Create();
				try
				{
					key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(UpdateSettings.ReleaseKeys[i]), out _);
					keys.Add(key);
				}
				// NotSupportedException covers PlatformNotSupportedException: a well-formed key on a curve this PC lacks.
				catch (Exception ex) when (ex is FormatException || ex is CryptographicException || ex is ArgumentException || ex is NotSupportedException)
				{
					key.Dispose();
					Log($"ReleaseKeys[{i}] is not a usable ECDSA public key, so it verifies nothing: {ex.Message}");
				}
			}
			foreach (string line in (signatureText ?? "").Split(new char[2] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
			{
				byte[] signature;
				try
				{
					signature = Convert.FromBase64String(line.Trim());
				}
				catch (FormatException)
				{
					continue;
				}
				if (signature.Length == 64 && keys.Any((ECDsa key) => key.VerifyData(statement, signature, HashAlgorithmName.SHA256)))
				{
					return true;
				}
			}
			return false;
		}
		finally
		{
			foreach (ECDsa key in keys)
			{
				key.Dispose();
			}
		}
	}

	internal static string HashFile(string path)
	{
		using FileStream stream = File.OpenRead(path);
		return HashStream(stream);
	}

	/// <summary>Lower-case hex SHA-256 of the stream from its position to its end.</summary>
	private static string HashStream(Stream stream)
	{
		using SHA256 sha = SHA256.Create();
		return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
	}
}
