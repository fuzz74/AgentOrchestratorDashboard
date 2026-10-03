using System.Collections.Immutable;

namespace OrchDash.Pages.Conversation.Format;

internal static class TextPreview
{
    // The first `count` non-blank lines of a text, without markup. When lines were left out,
    // the last one ends with "...".
    public static ImmutableArray<string> FirstLines(string? text, int count)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        var lines = text.Split('\n')
            .Select(line => line.TrimEnd('\r'))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();
        if (lines.Count <= count)
            return [.. lines];

        var shown = lines.Take(count).ToArray();
        shown[^1] += "...";
        return [.. shown];
    }
}
