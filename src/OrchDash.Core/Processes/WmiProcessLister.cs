using System.Collections.Immutable;
using System.Management;
using OrchDash.Core.Model;

namespace OrchDash.Core.Processes;

/// <summary>
/// Lists the agent processes through the one WMI query of the process table (spec 21.1, 21.2, 21.4, 4.3), at most every
/// 2 seconds. Never throws; <c>TaskId</c>, <c>Role</c> and <c>SessionId</c> are null on what it returns.
/// </summary>
public sealed class WmiProcessLister : IProcessLister
{
    // The only WMI operation OrchDash runs (N.9, 21.4).
    private const string Query =
        "SELECT ProcessId, Name, CommandLine, CreationDate, WorkingSetSize, UserModeTime, KernelModeTime " +
        "FROM Win32_Process WHERE Name = 'claude.exe' OR Name = 'copilot.exe'";

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly Lock _gate = new();
    private ProcessInfo _current = ProcessInfo.Empty;
    private DateTimeOffset? _lastAttempt;
    private Dictionary<int, CpuReading> _lastReadings = [];

    public ProcessInfo List(DateTimeOffset now)
    {
        lock (_gate)
        {
            // 21.1 speaks of the last sample's SampledAt; the last attempt is used instead so that a failing query is
            // also retried at most every 2 seconds. After a successful sample both are the same time.
            if (_lastAttempt is { } last && now - last < Interval)
                return _current;
            _lastAttempt = now;

            try
            {
                _current = Sample(now);
            }
            catch (Exception e)
            {
                // 21.2: the processes and SampledAt of the last sample, with the problem.
                _current = _current with { Problem = "processes: " + e.Message };
            }
            return _current;
        }
    }

    private ProcessInfo Sample(DateTimeOffset now)
    {
        var rows = ImmutableArray.CreateBuilder<AgentProcess>();
        var readings = new Dictionary<int, CpuReading>();
        var processorCount = Environment.ProcessorCount;

        using (var searcher = new ManagementObjectSearcher(Query))
        using (var results = searcher.Get())
        {
            foreach (var row in results)
            {
                using (row)
                {
                    var pid = checked((int)(uint)row["ProcessId"]);
                    var startedAt = row["CreationDate"] is string cim
                        ? new DateTimeOffset(ManagementDateTimeConverter.ToDateTime(cim))
                        : (DateTimeOffset?)null;
                    var cpu = checked((long)((ulong)row["UserModeTime"] + (ulong)row["KernelModeTime"]));

                    // A previous reading of a reused PID (another start time) is not the same process.
                    double? share = null;
                    if (_lastReadings.TryGetValue(pid, out var previous) && previous.StartedAt == startedAt
                        && _current.SampledAt is { } previousAt)
                        share = CpuSample.Share(previous.Cpu100ns, previousAt, cpu, now, processorCount);

                    readings[pid] = new CpuReading(startedAt, cpu);
                    rows.Add(new AgentProcess(
                        Pid: pid,
                        Name: (string)row["Name"],
                        CommandLine: row["CommandLine"] as string ?? "",
                        StartedAt: startedAt,
                        WorkingSetBytes: checked((long)(ulong)row["WorkingSetSize"]),
                        CpuShare: share,
                        TaskId: null, Role: null, SessionId: null));
                }
            }
        }

        _lastReadings = readings;
        return new ProcessInfo(now, rows.ToImmutable(), null);
    }

    private readonly record struct CpuReading(DateTimeOffset? StartedAt, long Cpu100ns);
}
