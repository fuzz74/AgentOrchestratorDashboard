using OrchDash.Core.Processes;
using Xunit;

namespace OrchDash.Core.Tests.Processes;

// Spec 21.1: the CPU share between two samples of the same process.
public sealed class CpuSampleTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void One_second_of_cpu_in_two_seconds_on_four_cores_is_an_eighth()
    {
        var share = CpuSample.Share(5_000_000, T0, 15_000_000, T0.AddSeconds(2), processorCount: 4);

        Assert.Equal(0.125, share);
    }

    [Fact]
    public void No_growth_is_zero()
    {
        Assert.Equal(0.0, CpuSample.Share(42, T0, 42, T0.AddSeconds(3), processorCount: 8));
    }

    [Fact]
    public void Negative_growth_is_clamped_to_zero()
    {
        Assert.Equal(0.0, CpuSample.Share(20_000_000, T0, 10_000_000, T0.AddSeconds(2), processorCount: 4));
    }

    [Fact]
    public void Samples_at_the_same_time_give_null()
    {
        Assert.Null(CpuSample.Share(0, T0, 10_000_000, T0, processorCount: 4));
    }

    [Fact]
    public void Sample_earlier_than_the_previous_gives_null()
    {
        Assert.Null(CpuSample.Share(0, T0, 10_000_000, T0.AddSeconds(-2), processorCount: 4));
    }
}
