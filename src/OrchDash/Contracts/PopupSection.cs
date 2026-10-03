namespace OrchDash.Contracts;

public sealed record PopupSection(string Heading, string Text, TextKind Kind = TextKind.Plain);
