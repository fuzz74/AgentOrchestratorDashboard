using OrchDash.Core.Model;
using Xunit;

namespace OrchDash.Tests.Support;

public sealed class StructuredOutputTests
{
    [Fact]
    public void Parses_a_worker_report()
    {
        var (worker, review) = StructuredOutput.Parse("""
            {"status":"done","summary":"Added it.\nTested it.","notes_for_dependents":"Use Foo.","blocked_reason":null}
            """);

        Assert.Null(review);
        Assert.Equal(new WorkerReport("done", "Added it.\nTested it.", "Use Foo.", null), worker);
    }

    [Fact]
    public void Worker_report_fields_default_when_missing()
    {
        var (worker, review) = StructuredOutput.Parse("""{"status":"blocked","summary":42}""");

        Assert.Null(review);
        Assert.Equal(new WorkerReport("blocked", "", null, null), worker);
    }

    [Fact]
    public void Parses_a_review_verdict()
    {
        var (worker, review) = StructuredOutput.Parse("""
            {"spec_verdict":"pass","quality_verdict":"fail","summary":"Mostly fine.",
             "issues":[{"severity":"major","file":"src/A.cs","description":"Swallows errors."},
                       {"severity":"minor","description":"No test."}]}
            """);

        Assert.Null(worker);
        Assert.NotNull(review);
        Assert.Equal("pass", review.SpecVerdict);
        Assert.Equal("fail", review.QualityVerdict);
        Assert.Equal("Mostly fine.", review.Summary);
        Assert.Equal(
            [new ReviewIssue("major", "src/A.cs", "Swallows errors."), new ReviewIssue("minor", null, "No test.")],
            review.Issues);
    }

    [Fact]
    public void Review_fields_default_when_missing()
    {
        var (worker, review) = StructuredOutput.Parse("""
            {"quality_verdict":"pass","status":"done","issues":[7,"x",null,{"file":3},{}]}
            """);

        Assert.Null(worker);
        Assert.NotNull(review);
        Assert.Equal("", review.SpecVerdict);
        Assert.Equal("pass", review.QualityVerdict);
        Assert.Equal("", review.Summary);
        Assert.Equal([new ReviewIssue("", null, ""), new ReviewIssue("", null, "")], review.Issues);
    }

    [Theory]
    [InlineData("""{"spec_verdict":"pass"}""")]
    [InlineData("""{"spec_verdict":"pass","issues":"none"}""")]
    [InlineData("""{"spec_verdict":"pass","issues":{"severity":"major"}}""")]
    public void Review_issues_are_empty_when_missing_or_not_an_array(string json)
    {
        var (_, review) = StructuredOutput.Parse(json);

        Assert.NotNull(review);
        Assert.False(review.Issues.IsDefault);
        Assert.Empty(review.Issues);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \r\n")]
    [InlineData("not json")]
    [InlineData("{\"status\":")]
    [InlineData("{\"status\":\"done\"} trailing")]
    [InlineData("[{\"status\":\"done\"}]")]
    [InlineData("\"done\"")]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("""{"status":1,"spec_verdict":true,"quality_verdict":null}""")]
    [InlineData("""{"summary":"no status"}""")]
    public void Returns_nothing_for_other_input(string? json)
    {
        var (worker, review) = StructuredOutput.Parse(json);

        Assert.Null(worker);
        Assert.Null(review);
    }

    [Fact]
    public void Does_not_throw_on_deep_nesting()
    {
        var json = new string('[', 1_000) + new string(']', 1_000);

        var (worker, review) = StructuredOutput.Parse(json);

        Assert.Null(worker);
        Assert.Null(review);
    }
}
