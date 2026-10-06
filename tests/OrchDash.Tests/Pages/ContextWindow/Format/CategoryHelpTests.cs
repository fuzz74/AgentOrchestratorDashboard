using OrchDash.Pages.ContextWindow.Format;
using Xunit;

namespace OrchDash.Tests.Pages.ContextWindow.Format;

public sealed class CategoryHelpTests
{
    [Theory]
    [InlineData(PartCategory.SystemPrompt, "System prompt explained")]
    [InlineData(PartCategory.ToolDefinitions, "Tool definitions explained")]
    [InlineData(PartCategory.Injected, "Injected explained")]
    [InlineData(PartCategory.Prompt, "Prompt explained")]
    [InlineData(PartCategory.Conversation, "Conversation explained")]
    [InlineData(PartCategory.Other, "Other explained")]
    public void Every_category_is_explained_in_the_same_four_sections(PartCategory category, string title)
    {
        var popup = CategoryHelp.Popup(category);

        Assert.Equal(title, popup.Title);
        Assert.Equal(
            [CategoryHelp.WhatItIs, CategoryHelp.HowItGetsIn, CategoryHelp.WhyItMatters, CategoryHelp.OnThisPage],
            popup.Sections.Select(s => s.Heading));
        Assert.All(popup.Sections, s => Assert.True(s.Text.Length > 100, $"{title}: {s.Heading} is too short"));
    }

    [Fact]
    public void The_page_section_names_the_key_that_opens_the_text()
    {
        Assert.Contains("Key s opens", CategoryHelp.Popup(PartCategory.SystemPrompt).Sections[^1].Text, StringComparison.Ordinal);
        Assert.Contains("Key t opens", CategoryHelp.Popup(PartCategory.ToolDefinitions).Sections[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unknown_category_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CategoryHelp.Popup((PartCategory)99));
    }
}
