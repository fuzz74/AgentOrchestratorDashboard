using System.Text;

namespace OrchDash.Core.Tests.CommandLogs;

/// <summary>A <c>.orchestrator</c> folder with a <c>logs</c> folder under the temp folder, deleted on dispose.</summary>
internal sealed class TempLogs : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "OrchDash.CommandLogs." + Guid.NewGuid().ToString("N"));

    public TempLogs()
    {
        RunDir = Path.Combine(_root, ".orchestrator");
        Directory.CreateDirectory(Path.Combine(RunDir, "logs"));
    }

    public string RunDir { get; }

    public string FullPath(string key) => Path.Combine(RunDir, "logs", key.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Writes UTF-8 text without a BOM, or with one when <paramref name="bom"/> is true.</summary>
    public string Write(string key, string text, bool bom = false)
    {
        var path = FullPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: bom));
        return path;
    }

    public void Append(string key, string text)
    {
        using var stream = new FileStream(FullPath(key), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        stream.Write(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>Opens the file, creating it when missing, so that no other handle can open it.</summary>
    public FileStream Hold(string key)
    {
        var path = FullPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
