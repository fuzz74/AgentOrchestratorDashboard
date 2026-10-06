namespace OrchDash.Pages.GitView.Format;

// One row of the Git page's file list (25.3): Text is a markup line (escaped), Path the plain file path and Committed
// whether the file comes from GitTask.Committed (true) or from GitTask.UncommittedFiles (false).
public sealed record GitFileRow(string Text, string Path, bool Committed);
