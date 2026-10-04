namespace OrchDash.Core.Model;

public interface ISessionIdFinder    // never throws
{
    string? Find(string name, string workDir, DateTimeOffset startedAt, IReadOnlySet<string> knownIds);
}
