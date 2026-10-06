using System.Text;
using OrchDash.Core.Model;

namespace OrchDash.Core.Tests.Store;

// A repo folder under the temp folder with session files the tests write; deleted on Dispose.
public sealed class TempRepo : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "OrchDash.Store." + Guid.NewGuid().ToString("N"));

    public TempRepo() : this("repo")
    {
    }

    private TempRepo(string relativeRepoPath)
    {
        RepoPath = Path.Combine(_root, relativeRepoPath);
        Directory.CreateDirectory(LogsDir);
    }

    // An archived run: the repo path is <temp>\Repo.runs\<stamp>.
    public static TempRepo Archive(string stamp) => new(Path.Combine("Repo.runs", stamp));

    public string Root => _root;
    public string RepoPath { get; }
    public string LogsDir => Path.Combine(RepoPath, ".orchestrator", "logs");

    public SessionFiles Session(string key, string? taskId = null, AgentRole role = AgentRole.Worker,
        bool hasResultFile = false, DateTimeOffset? promptWrittenAt = null)
    {
        var resultPath = Path.Combine(LogsDir, key.Replace('/', Path.DirectorySeparatorChar));
        return new SessionFiles(key, taskId, role, null, 1, 0, false,
            resultPath, resultPath + ".prompt.md", resultPath + ".events.jsonl", resultPath + ".stderr",
            hasResultFile, File.Exists(resultPath + ".events.jsonl"), promptWrittenAt);
    }

    public void Append(SessionFiles session, string text) => AppendBytes(session, Encoding.UTF8.GetBytes(text));

    public void AppendBytes(SessionFiles session, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(session.EventsPath)!);
        using var stream = new FileStream(session.EventsPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        stream.Write(bytes);
    }

    public void Overwrite(SessionFiles session, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(session.EventsPath)!);
        File.WriteAllText(session.EventsPath, text);
    }

    public void WritePrompt(SessionFiles session, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(session.PromptPath)!);
        File.WriteAllText(session.PromptPath, text);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // a file still in use; the temp folder is cleaned up by the OS
        }
    }
}
