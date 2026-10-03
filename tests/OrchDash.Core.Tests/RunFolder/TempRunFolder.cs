using System.Text;
using System.Text.Json.Nodes;
using OrchDash.Core.Model;
using OrchDash.Core.RunFolder;

namespace OrchDash.Core.Tests.RunFolder;

/// <summary>A <c>.orchestrator</c> folder under the temp folder, deleted on dispose.</summary>
internal sealed class TempRunFolder : IDisposable
{
    public static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 30, 0, TimeSpan.FromHours(2));

    public const string Started = "2026-10-03 12:00:00  Run started: 5 tasks, max 2 in parallel, integration branch orch/integration\n";
    public const string ClaudeLine = "2026-10-03 12:00:00  Claude: C:\\Users\\me\\.local\\bin\\claude.exe\n";
    public const string Finished = "2026-10-03 12:30:00  Run finished: 5 done, 0 failed, 0 blocked, 1,25 USD\n";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "OrchDash.Tests", Guid.NewGuid().ToString("N"));

    public TempRunFolder()
    {
        RunDir = Path.Combine(_root, ".orchestrator");
        Directory.CreateDirectory(RunDir);
    }

    public string RunDir { get; }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    public RunFolderData Read() => new RunFolderReader().Read(RunDir, Now);

    public string FullPath(string relativePath) => Path.Combine(RunDir, relativePath.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Writes UTF-8 text without a BOM, or with one when <paramref name="bom"/> is true.</summary>
    public string Write(string relativePath, string text, bool bom = false)
    {
        var path = FullPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: bom));
        return path;
    }

    /// <summary>Writes an empty file with the given last write time.</summary>
    public string Touch(string relativePath, DateTime lastWriteTimeUtc)
    {
        var path = Write(relativePath, "");
        File.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
        return path;
    }

    /// <summary>Opens the file, creating it when missing, so that no other handle can open it.</summary>
    public FileStream Hold(string relativePath)
    {
        var path = FullPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public void WriteTasks(params JsonObject[] tasks) =>
        Write("tasks.json", new JsonObject { ["tasks"] = new JsonArray([.. tasks]) }.ToJsonString());

    public void WriteState(params (string Id, string Status)[] states)
    {
        var tasks = new JsonObject();
        foreach (var (id, status) in states)
            tasks[id] = new JsonObject { ["status"] = status };
        Write("state.json", new JsonObject { ["tasks"] = tasks }.ToJsonString());
    }

    public void WriteState(JsonObject tasks) => Write("state.json", new JsonObject { ["tasks"] = tasks }.ToJsonString());

    public static JsonObject Task(string id, string[]? deps = null, string[]? owns = null) => new()
    {
        ["id"] = id,
        ["title"] = "Title of " + id,
        ["deps"] = deps is null ? null : StringArray(deps),
        ["owns"] = owns is null ? null : StringArray(owns),
    };

    private static JsonArray StringArray(string[] items) => new([.. items.Select(item => JsonValue.Create(item))]);
}
