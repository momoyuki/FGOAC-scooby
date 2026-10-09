namespace Hanhua;

public sealed record ArchiveEntry(string Name, int Offset, int Stored, int Size, int Flags, int DirectoryOffset);
