using System.Collections.Immutable;

namespace OrchDash.Core.Model;

public sealed record SessionInit(string? Cwd, string? PermissionMode, string? CliVersion,
    ImmutableArray<string> Tools, ImmutableArray<string> McpServers);   // server as "name (status)"
