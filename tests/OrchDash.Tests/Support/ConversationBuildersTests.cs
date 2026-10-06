using OrchDash.Core.Model;
using OrchDash.Pages.Conversation.Format;
using Xunit;

namespace OrchDash.Tests.Support;

public sealed class ConversationBuildersTests
{
    private readonly Session _session = SampleRun.Create().Sessions[0];

    // The entries of Entries without the header and the call separators: the prompt, one per item, the result.
    private ConversationEntry[] Built() =>
    [
        .. ConversationText.Entries(_session, SampleRun.At(12, 30, 0))
            .Where(e => e.Kind is not (EntryKind.Header or EntryKind.CallSeparator)),
    ];

    [Fact]
    public void The_alpha_worker_has_a_prompt_six_items_and_a_result()
    {
        Assert.Equal(SampleRun.AlphaWorkerKey, _session.Files.Key);
        Assert.Equal(6, _session.Content.Items.Length);
        Assert.NotNull(_session.Content.Result);
        Assert.Equal(1 + 6 + 1, Built().Length);
    }

    [Fact]
    public void PromptEntry_is_the_prompt_entry_of_Entries()
    {
        var built = Built()[0];

        Assert.Equal(EntryKind.Prompt, built.Kind);
        AssertSame(built, ConversationText.PromptEntry(_session.Prompt));
    }

    [Fact]
    public void ItemEntry_is_the_entry_of_Entries_for_each_item()
    {
        var built = Built()[1..^1];
        var items = _session.Content.Items;

        Assert.Equal(items.Length, built.Length);
        for (var i = 0; i < items.Length; i++)
            AssertSame(built[i], ConversationText.ItemEntry(items[i]));
    }

    [Fact]
    public void ResultEntry_is_the_result_entry_of_Entries()
    {
        var built = Built()[^1];

        Assert.Equal(EntryKind.Result, built.Kind);
        AssertSame(built, ConversationText.ResultEntry(_session.Content.Result!));
    }

    private static void AssertSame(ConversationEntry expected, ConversationEntry actual)
    {
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.Lines, actual.Lines);
        Assert.Equal(expected.PopupTitle, actual.PopupTitle);
        Assert.Equal(expected.Popup, actual.Popup);
    }
}
