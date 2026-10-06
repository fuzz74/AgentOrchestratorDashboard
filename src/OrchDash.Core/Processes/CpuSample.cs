namespace OrchDash.Core.Processes;

/// <summary>The CPU share of a process between two samples (spec 21.1).</summary>
public static class CpuSample
{
    /// <summary>
    /// The growth of user plus kernel time (100 ns units) between two samples, divided by the time between them and by
    /// <paramref name="processorCount"/>: 0.12 is 12 % of all cores. Null when no time has passed (or without a processor);
    /// a negative growth gives 0.
    /// </summary>
    public static double? Share(long previousCpu100ns, DateTimeOffset previousAt, long cpu100ns, DateTimeOffset at, int processorCount)
    {
        var seconds = (at - previousAt).TotalSeconds;
        if (seconds <= 0 || processorCount <= 0)
            return null;
        var cpuSeconds = Math.Max(0, cpu100ns - previousCpu100ns) / 1e7;
        return cpuSeconds / seconds / processorCount;
    }
}
