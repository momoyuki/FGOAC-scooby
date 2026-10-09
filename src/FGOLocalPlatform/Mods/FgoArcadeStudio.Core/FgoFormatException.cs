using System;

namespace FgoArcadeStudio.Core;

public sealed class FgoFormatException : Exception
{
	public FgoFormatException(string message)
		: base(message)
	{
	}

	public FgoFormatException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
