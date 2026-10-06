namespace OrchDash.Core.Model;

public sealed record DiffFile(string Path, int? Added, int? Removed);          // nulls for a binary file
