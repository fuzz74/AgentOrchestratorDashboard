using System.Collections.Immutable;

namespace OrchDash.Core.Model;

public sealed record ContextCheckpoint(long? PromptTokens, long? ToolTokens, ImmutableArray<string> ToolNames,
    ImmutableArray<TokenPart> SystemSegments, long? NanoAiu, double? PremiumRequests);
