using System.Collections.Immutable;
using OrchDash.Core.Model;
using Xunit;

namespace OrchDash.Tests.Support;

public sealed class SessionTimesTests
{
    private static readonly DateTimeOffset Start = SampleRun.At(12, 0, 0);

    [Fact]
    public void Items_with_times_keep_their_times()
    {
        ImmutableArray<ConversationItem> items =
        [
            Text(SampleRun.At(12, 0, 10)),
            Text(SampleRun.At(12, 0, 5)),
            Text(SampleRun.At(12, 1, 0)),
        ];

        Assert.Equal([SampleRun.At(12, 0, 10), SampleRun.At(12, 0, 5), SampleRun.At(12, 1, 0)],
            SessionTimes.ItemTimes(items, Start));
    }

    [Fact]
    public void Items_without_times_get_the_start()
    {
        ImmutableArray<ConversationItem> items = [Text(null), Text(null)];

        Assert.Equal([Start, Start], SessionTimes.ItemTimes(items, Start));
    }

    [Fact]
    public void An_untimed_item_gets_the_time_of_the_nearest_earlier_item()
    {
        ImmutableArray<ConversationItem> items =
        [
            Text(null),
            Text(SampleRun.At(12, 0, 20)),
            Text(null),
            Text(null),
            Text(SampleRun.At(12, 2, 0)),
            Text(null),
        ];

        Assert.Equal(
            [Start, SampleRun.At(12, 0, 20), SampleRun.At(12, 0, 20), SampleRun.At(12, 0, 20), SampleRun.At(12, 2, 0), SampleRun.At(12, 2, 0)],
            SessionTimes.ItemTimes(items, Start));
    }

    [Fact]
    public void Empty_and_default_arrays_give_an_empty_result()
    {
        var fromEmpty = SessionTimes.ItemTimes([], Start);
        var fromDefault = SessionTimes.ItemTimes(default, Start);

        Assert.False(fromEmpty.IsDefault);
        Assert.Empty(fromEmpty);
        Assert.False(fromDefault.IsDefault);
        Assert.Empty(fromDefault);
    }

    private static AssistantText Text(DateTimeOffset? time) => new("msg_01", time, "text");
}
