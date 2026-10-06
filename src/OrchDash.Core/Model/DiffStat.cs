using System.Collections.Immutable;

namespace OrchDash.Core.Model;

public sealed record DiffStat(ImmutableArray<DiffFile> Files)
{
    public int Added => Files.Sum(f => f.Added ?? 0);
    public int Removed => Files.Sum(f => f.Removed ?? 0);
}
