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

public sealed class StringConcatenationToSameTargetCodeReviewCheckTests
{
    [Test]
    public void AnalyzeWhenStringIsConcatenatedFourTimesInSameBlockReportsSuggestion()
    {
        const string source = """
            public sealed class Sample
            {
                public string Build()
                {
                    var text = string.Empty;
                    text += "A";
                    text += "B";
                    text += "C";
                    text += "D";
                    return text;
                }
            }
            """;

        var report = AnalyzeSource(source, Enumerable.Range(1, 12));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Suggestion));
        Assert.That(report.Findings[0].Message, Does.Contain("StringBuilder"));
        Assert.That(report.Findings[0].Message, Does.Contain("text"));
    }

    [Test]
    public void AnalyzeWhenStringIsConcatenatedOnlyThreeTimesDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public string Build()
                {
                    var text = string.Empty;
                    text += "A";
                    text += "B";
                    text += "C";
                    return text;
                }
            }
            """;

        var report = AnalyzeSource(source, Enumerable.Range(1, 11));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenOnlyAddedConcatCountIsBelowThresholdDoesNotReportForModifiedFile()
    {
        const string source = """
            public sealed class Sample
            {
                public string Build()
                {
                    var text = string.Empty;
                    text += "A";
                    text += "B";
                    text += "C";
                    text += "D";
                    return text;
                }
            }
            """;

        var report = AnalyzeSource(source, [8, 9, 10], "M");

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenSimpleAssignmentConcatPatternIsUsedReportsSuggestion()
    {
        const string source = """
            public sealed class Sample
            {
                public string Build()
                {
                    var text = string.Empty;
                    text = text + "A";
                    text = text + "B";
                    text = text + "C";
                    text = text + "D";
                    return text;
                }
            }
            """;

        var report = AnalyzeSource(source, Enumerable.Range(1, 12));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Message, Does.Contain("text"));
    }

    private static CodeSmellReport AnalyzeSource(string source, IEnumerable<int> addedLineNumbers, string status = "A")
    {
        var normalizedSource = (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalizedSource.Split('\n');
        var changedFile = new CodeReviewChangedFile(
            status,
            "Packages/CSharp.Core/Sample.cs",
            "Packages/CSharp.Core/Sample.cs",
            normalizedSource,
            lines,
            new HashSet<int>(addedLineNumbers ?? []));
        var context = new CodeReviewAnalysisContext(
            [changedFile],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var report = new CodeSmellReport();
        var check = new StringConcatenationToSameTargetCodeReviewCheck();
        check.Analyze(context, report);
        return report;
    }
}
