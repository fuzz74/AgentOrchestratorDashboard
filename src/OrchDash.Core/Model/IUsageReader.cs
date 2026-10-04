namespace OrchDash.Core.Model;

public interface IUsageReader        // never throws
{
    UsageRows Read(IReadOnlyCollection<string> sessionIds);
}
