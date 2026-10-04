using System.Text;

namespace OrchDash.Core.Tests.SessionFolder;

// A session-state folder under the temp folder with files the tests write; deleted on Dispose.
public sealed class TempSessionState : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "OrchDash.SessionFolder." + Guid.NewGuid().ToString("N"));

    public TempSessionState()
    {
        Dir = Path.Combine(_root, "session-state");
        Directory.CreateDirectory(Dir);
    }

    public string Dir { get; }

    public string EventsPath(string sessionId) => Path.Combine(Dir, sessionId, "events.jsonl");

    public string WorkspacePath(string folder) => Path.Combine(Dir, folder, "workspace.yaml");

    public void WriteEvents(string sessionId, string text)
    {
        Directory.CreateDirectory(Path.Combine(Dir, sessionId));
        File.WriteAllText(EventsPath(sessionId), text);
    }

    public void WriteEventsBytes(string sessionId, byte[] bytes)
    {
        Directory.CreateDirectory(Path.Combine(Dir, sessionId));
        File.WriteAllBytes(EventsPath(sessionId), bytes);
    }

    public void AppendEvents(string sessionId, string text)
    {
        using var stream = new FileStream(EventsPath(sessionId), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        stream.Write(Encoding.UTF8.GetBytes(text));
    }

    public void WriteWorkspace(string folder, string text)
    {
        Directory.CreateDirectory(Path.Combine(Dir, folder));
        File.WriteAllText(WorkspacePath(folder), text);
    }

    // A workspace.yaml as Copilot writes it, with the folder named after the id.
    public void WriteWorkspace(string id, string name, string cwd, string createdAt) =>
        WriteWorkspace(id, $"id: {id}\ncwd: {cwd}\nname: {name}\ncreated_at: {createdAt}\n");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // a file still in use; the temp folder is cleaned up by the OS
        }
    }
}
