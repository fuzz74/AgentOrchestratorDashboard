using OrchDash.Core.Model;

namespace OrchDash.Contracts;

public interface IRunHost      // read on the UI thread each tick; its work runs on its own threads
{
    RunCatalog? Runs { get; }  // the last listing; null before the first completes
    string? Loading { get; }   // the stamp or folder name of the run being switched to; null when idle
    string? Problem { get; }   // why the last switch or listing failed; null when none
    void RefreshRuns();        // returns at once
    void SwitchTo(string repoPath);   // returns at once; ignored while Loading is set or for the current path
}
