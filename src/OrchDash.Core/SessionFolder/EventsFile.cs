using System.Collections.Immutable;
using System.Text.Json;
using OrchDash.Core.Model;

namespace OrchDash.Core.SessionFolder;

// Maps the lines of a session folder's events.jsonl by the session folder table (spec 4.3, 12.1, 12.2).
internal static class EventsFile
{
    private static ReadOnlySpan<byte> Utf8Bom => [0xEF, 0xBB, 0xBF];

    public static StoreData Map(byte[] bytes)
    {
        string? cliVersion = null;
        var systemPrompt = ImmutableArray<string>.Empty;
        int unparsed = 0;

        // Each line ends with '\n'; the bytes after the last '\n' are not complete yet and are not read.
        int consumed = 0;
        int newline;
        while ((newline = bytes.AsSpan(consumed).IndexOf((byte)'\n')) >= 0)
        {
            int start = consumed;
            int length = newline;
            consumed += newline + 1;

            if (start == 0 && bytes.AsSpan(0, length).StartsWith(Utf8Bom))
            {
                start += Utf8Bom.Length;
                length -= Utf8Bom.Length;
            }
            if (length > 0 && bytes[start + length - 1] == (byte)'\r')
                length--;

            try
            {
                using var document = JsonDocument.Parse(bytes.AsMemory(start, length));
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    unparsed++;
                    continue;
                }
                if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object)
                    continue;

                switch (String(root, "type"))
                {
                    case "session.start":
                        cliVersion = String(data, "copilotVersion") ?? cliVersion;
                        break;
                    case "system.message":
                        systemPrompt = SystemPrompt(data);
                        break;
                }
            }
            catch (JsonException)
            {
                unparsed++;
            }
        }

        return StoreData.Empty with { CliVersion = cliVersion, SystemPrompt = systemPrompt, UnparsedLines = unparsed };
    }

    // The content of each entry of data.contentBlocks[], or [data.content] when there are no blocks.
    private static ImmutableArray<string> SystemPrompt(JsonElement data)
    {
        if (data.TryGetProperty("contentBlocks", out var blocks) && blocks.ValueKind == JsonValueKind.Array
            && blocks.GetArrayLength() > 0)
        {
            var texts = ImmutableArray.CreateBuilder<string>();
            foreach (var block in blocks.EnumerateArray())
            {
                if (block.ValueKind == JsonValueKind.Object && String(block, "content") is { } text)
                    texts.Add(text);
            }
            return texts.ToImmutable();
        }

        return String(data, "content") is { } content ? [content] : [];
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
