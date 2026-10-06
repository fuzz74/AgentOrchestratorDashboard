using System.Globalization;
using System.Text;

namespace OrchDash.Core.Tests.Runs;

// A repo folder and its runs folder under the temp folder; deleted on Dispose.
public sealed class TempRuns : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "OrchDash.Runs." + Guid.NewGuid().ToString("N"));

    public TempRuns()
    {
        RepoPath = Path.Combine(_root, "Repo");
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;
    public string RepoPath { get; }
    public string RunsPath => RepoPath + ".runs";

    // The folder of the archived run <stamp>: <Repo>.runs\<stamp>.
    public string Archive(string stamp) => Path.Combine(RunsPath, stamp);

    // Copies the named files of a fixture's .orchestrator folder into <repoPath>\.orchestrator.
    public void CopyRun(string repoPath, string fixtureRunDir, params string[] fileNames)
    {
        var runDir = RunDir(repoPath);
        foreach (var name in fileNames)
            File.Copy(Path.Combine(fixtureRunDir, name), Path.Combine(runDir, name));
    }

    // Writes a file of <repoPath>\.orchestrator as UTF-8 without a BOM.
    public void Write(string repoPath, string fileName, string text) =>
        File.WriteAllText(Path.Combine(RunDir(repoPath), fileName), text, new UTF8Encoding(false));

    // Names, sizes and write times of everything under the temp folder.
    public string[] Listing() =>
    [
        .. Directory.EnumerateFileSystemEntries(_root, "*", SearchOption.AllDirectories)
            .Select(path =>
            {
                var relative = Path.GetRelativePath(_root, path);
                if (Directory.Exists(path))
                    return "folder " + relative;
                var file = new FileInfo(path);
                return string.Create(CultureInfo.InvariantCulture, $"file {relative} {file.Length} {file.LastWriteTimeUtc:O}");
            })
            .Order(StringComparer.Ordinal),
    ];

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

    private static string RunDir(string repoPath)
    {
        var runDir = Path.Combine(repoPath, ".orchestrator");
        Directory.CreateDirectory(runDir);
        return runDir;
    }
}
