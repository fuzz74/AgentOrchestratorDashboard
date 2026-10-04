namespace OrchDash.Core.Model;

public sealed record ToolDefinition(string Name, string? Description, string? SchemaJson);   // SchemaJson: indented JSON
