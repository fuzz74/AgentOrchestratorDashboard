using System.Collections.Immutable;

namespace OrchDash.Core.Model;

public sealed record CommandLogData(ImmutableArray<CommandLog> Logs, ImmutableArray<string> Problems);
