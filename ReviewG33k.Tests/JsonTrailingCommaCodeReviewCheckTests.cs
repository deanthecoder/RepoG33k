// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using ReviewG33k.Services;
using ReviewG33k.Services.Checks;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class JsonTrailingCommaCodeReviewCheckTests
{
    [Test]
    public void AnalyzeWhenObjectHasTrailingCommaReportsWarning()
    {
        const string source = """
            {
              "foo": 1,
              "bar": 2,
            }
            """;

        var report = AnalyzeSource(source, "config/settings.json", addedLineNumbers: [4]);

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.JsonTrailingComma));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Important));
        Assert.That(report.Findings[0].LineNumber, Is.EqualTo(3));
    }

    [Test]
    public void AnalyzeWhenArrayHasTrailingCommaReportsWarning()
    {
        const string source = """
            [
              "foo",
            ]
            """;

        var report = AnalyzeSource(source, "config/settings.json");

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].LineNumber, Is.EqualTo(2));
    }

    [Test]
    public void AnalyzeWhenJsonIsValidDoesNotReport()
    {
        const string source = """
            {
              "foo": 1,
              "bar": 2
            }
            """;

        var report = AnalyzeSource(source, "config/settings.json");

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenCommaBeforeBraceIsInsideStringDoesNotReport()
    {
        const string source = """
            {
              "foo": ",}"
            }
            """;

        var report = AnalyzeSource(source, "config/settings.json");

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenFileIsNotJsonDoesNotReport()
    {
        const string source = """
            {
              "foo": 1,
            }
            """;

        var report = AnalyzeSource(source, "Services/Sample.cs");

        Assert.That(report.Findings, Is.Empty);
    }

    private static CodeSmellReport AnalyzeSource(string source, string path, IEnumerable<int> addedLineNumbers = null)
    {
        var normalizedSource = (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var changedFile = new CodeReviewChangedFile(
            "M",
            path,
            path,
            normalizedSource,
            normalizedSource.Split('\n'),
            new HashSet<int>(addedLineNumbers ?? [1]));
        var context = new CodeReviewAnalysisContext(
            [changedFile],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            allChangedFiles: [changedFile]);

        var report = new CodeSmellReport();
        var check = new JsonTrailingCommaCodeReviewCheck();
        check.Analyze(context, report);
        return report;
    }
}
