namespace OrchDash.Tests.App;

/// <summary>A new folder under <see cref="Path.GetTempPath"/> that is deleted on dispose.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "OrchDash.Tests.App-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>The full path of the folder.</summary>
    public string Path { get; }

    /// <summary>Creates the folder <paramref name="relative"/> below this one and returns its full path.</summary>
    public string Folder(string relative) => Directory.CreateDirectory(System.IO.Path.Combine(Path, relative)).FullName;

    /// <summary>Writes <paramref name="text"/> to the file <paramref name="relative"/>, creating its folder, and returns its full path.</summary>
    public string Write(string relative, string text)
    {
        var file = System.IO.Path.Combine(Path, relative);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text);
        return file;
    }

    /// <summary>Copies the folder <paramref name="from"/> with everything below it to <paramref name="relative"/> below this one and returns its full path.</summary>
    public string Copy(string from, string relative)
    {
        var to = Folder(relative);
        foreach (var folder in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(System.IO.Path.Combine(to, System.IO.Path.GetRelativePath(from, folder)));
        }
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, System.IO.Path.Combine(to, System.IO.Path.GetRelativePath(from, file)));
        }
        return to;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // A file that is still open is left for the system's temp clean-up.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
