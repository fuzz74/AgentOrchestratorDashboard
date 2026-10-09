using System.Text;

namespace OrchDash.Core.Tests.Transcript;

/// <summary>A Claude Code <c>projects</c> folder under the temp folder, deleted on dispose.</summary>
internal sealed class TempProjects : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "OrchDash.Tests", Guid.NewGuid().ToString("N"));

    public TempProjects()
    {
        ProjectsDir = Path.Combine(_root, "projects");
        Directory.CreateDirectory(ProjectsDir);
    }

    public string ProjectsDir { get; }

    /// <summary>A folder next to <see cref="ProjectsDir"/> that does not exist.</summary>
    public string MissingDir => Path.Combine(_root, "missing");

    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>Writes <c>&lt;ProjectsDir&gt;/&lt;folder&gt;/&lt;sessionId&gt;.jsonl</c> as UTF-8 without a BOM.</summary>
    public string Write(string folder, string sessionId, string text)
    {
        var directory = Path.Combine(ProjectsDir, folder);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, sessionId + ".jsonl");
        File.WriteAllText(path, text, Utf8);
        return path;
    }

    /// <summary>
    /// Writes the sub-agent transcript <c>&lt;ProjectsDir&gt;/&lt;folder&gt;/&lt;sessionId&gt;/subagents/agent-&lt;x&gt;.jsonl</c>
    /// and, unless <paramref name="metaJson"/> is null, its meta file. Gives the transcript's path.
    /// </summary>
    public string WriteSubAgent(string folder, string sessionId, string x, string transcriptText, string? metaJson)
    {
        var directory = Path.Combine(ProjectsDir, folder, sessionId, "subagents");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"agent-{x}.jsonl");
        File.WriteAllText(path, transcriptText, Utf8);
        if (metaJson is not null)
            WriteMeta(path, metaJson);
        return path;
    }

    /// <summary>The <c>agent-&lt;x&gt;.meta.json</c> next to the sub-agent transcript <c>agent-&lt;x&gt;.jsonl</c>.</summary>
    public static string MetaPath(string transcriptPath) => Path.ChangeExtension(transcriptPath, ".meta.json");

    public static void WriteMeta(string transcriptPath, string metaJson) => File.WriteAllText(MetaPath(transcriptPath), metaJson, Utf8);

    public static void Append(string path, string text) => File.AppendAllText(path, text, Utf8);

    /// <summary>Opens the file so that no other handle can open it.</summary>
    public static FileStream Hold(string path) => new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
}
