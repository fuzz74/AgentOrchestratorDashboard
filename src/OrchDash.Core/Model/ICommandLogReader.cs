namespace OrchDash.Core.Model;

public interface ICommandLogReader   // never throws; Command null, Outcome Unknown, ExitCode null
{
    CommandLogData Read(string runDir);
}
