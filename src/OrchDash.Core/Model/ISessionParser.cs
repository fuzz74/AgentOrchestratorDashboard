namespace OrchDash.Core.Model;

public interface ISessionParser        // one per session, single-threaded
{
    void AddLine(string line);         // one complete events.jsonl line, without the line break
    SessionContent Build();            // everything added so far
}
