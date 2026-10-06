namespace OrchDash.Core.Model;

public sealed record GitWorktree(string Path, string? Branch, string? Head);   // Branch without refs/heads/; null when detached
