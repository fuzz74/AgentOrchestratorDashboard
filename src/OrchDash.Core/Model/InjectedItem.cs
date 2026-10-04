namespace OrchDash.Core.Model;

public sealed record InjectedItem(string Kind, string Role, DateTimeOffset? Time, string Text);
