using OrchDash.Core.Processes;
using Xunit;

namespace OrchDash.Core.Tests.Processes;

// Spec 21.1, 21.2: the real WMI query, once, and the 2 second throttle.
public sealed class WmiProcessListerTests
{
    [Fact]
    public void List_samples_without_throwing_at_most_every_two_seconds()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Skip("Windows only");
        var lister = new WmiProcessLister();
        var now = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

        var first = lister.List(now);

        if (first.Problem is null)
        {
            Assert.Equal(now, first.SampledAt);
            Assert.False(first.Processes.IsDefault);
            Assert.All(first.Processes, process =>
            {
                Assert.Contains(process.Name, ["claude.exe", "copilot.exe"], StringComparer.OrdinalIgnoreCase);
                Assert.NotNull(process.CommandLine);
                Assert.Null(process.CpuShare);   // no previous sample
                Assert.Null(process.TaskId);
                Assert.Null(process.Role);
                Assert.Null(process.SessionId);
            });
        }
        else
        {
            Assert.StartsWith("processes: ", first.Problem, StringComparison.Ordinal);
            Assert.Null(first.SampledAt);
            Assert.Empty(first.Processes);
        }

        Assert.Same(first, lister.List(now.AddSeconds(1)));

        var second = lister.List(now.AddSeconds(2));

        Assert.NotSame(first, second);
        if (first.Problem is null && second.Problem is null)
        {
            Assert.Equal(now.AddSeconds(2), second.SampledAt);
            // A process seen in both samples has a CPU share.
            var again = second.Processes.Where(p => first.Processes.Any(f => f.Pid == p.Pid && f.StartedAt == p.StartedAt));
            Assert.All(again, process => Assert.True(process.CpuShare >= 0));
        }    }
}
