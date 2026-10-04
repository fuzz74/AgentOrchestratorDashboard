namespace OrchDash.Core.Model;

public interface IRunFolderReader
{
    RunFolderData Read(string runDir, DateTimeOffset now);   // runDir = <repo>/.orchestrator; never throws
}
