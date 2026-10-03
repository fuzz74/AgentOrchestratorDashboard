using OrchDash.Contracts;
using OrchDash.Shell;
using Xunit;

namespace OrchDash.Tests.Shell;

public sealed class PopupTextTests
{
    [Fact]
    public void Each_section_is_its_bold_heading_and_its_text_separated_by_a_blank_line()
    {
        var markup = PopupText.Build([new PopupSection("One", "first\nsecond"), new PopupSection("Two", "third")]);

        Assert.Equal("[bold]One[/]\nfirst\nsecond\n\n[bold]Two[/]\nthird", markup);
    }

    [Fact]
    public void Diff_lines_are_coloured_by_their_prefix()
    {
        var markup = PopupText.Build([new PopupSection("Diff", "@@ -1,2 +1,2 @@\n-old\n+new\n same\n", TextKind.Diff)]);

        Assert.Equal("[bold]Diff[/]\n[accent]@@ -1,2 +1,2 @@[/]\n[error]-old[/]\n[success]+new[/]\n same\n", markup);
    }

    [Fact]
    public void Json_that_parses_is_indented_by_two_spaces()
    {
        var markup = PopupText.Build([new PopupSection("Input", """{"file_path":"src/a.cs","n":[1,2],"text":"ä + <b>"}""", TextKind.Json)]);

        // The array brackets are escaped as markup.
        var expected = """
            [bold]Input[/]
            {
              "file_path": "src/a.cs",
              "n": [[
                1,
                2
              ]],
              "text": "ä + <b>"
            }
            """.ReplaceLineEndings("\n");
        Assert.Equal(expected, markup);
    }

    [Fact]
    public void Json_that_does_not_parse_is_unchanged()
    {
        var markup = PopupText.Build([new PopupSection("Input", "{not json", TextKind.Json), new PopupSection("Empty", "", TextKind.Json)]);

        Assert.Equal("[bold]Input[/]\n{not json\n\n[bold]Empty[/]\n", markup);
    }

    [Fact]
    public void Markup_in_text_and_headings_stays_literal_in_every_kind()
    {
        var markup = PopupText.Build(
        [
            new PopupSection("[red]Head[/]", "[bold]x[/]"),
            new PopupSection("Json", "\"[bold]x[/]\"", TextKind.Json),
            new PopupSection("Diff", "+[bold]x[/]\n[bold]y[/]", TextKind.Diff),
        ]);

        Assert.Equal(
            "[bold][[red]]Head[[/]][/]\n[[bold]]x[[/]]\n\n" +
            "[bold]Json[/]\n\"[[bold]]x[[/]]\"\n\n" +
            "[bold]Diff[/]\n[success]+[[bold]]x[[/]][/]\n[[bold]]y[[/]]",
            markup);
    }

    [Fact]
    public void Windows_line_breaks_become_plain_line_breaks()
    {
        var markup = PopupText.Build([new PopupSection("Text", "a\r\nb"), new PopupSection("Diff", "+a\r\n-b", TextKind.Diff)]);

        Assert.Equal("[bold]Text[/]\na\nb\n\n[bold]Diff[/]\n[success]+a[/]\n[error]-b[/]", markup);
    }
}
