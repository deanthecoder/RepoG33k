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
public sealed class EmptyXmlDocContentCodeReviewCheckTests
{
    [Test]
    public void AnalyzeWhenSummaryParamAndReturnsAreEmptyReportsHintFindings()
    {
        const string source = """
            public sealed class Sample
            {
                /// <summary>
                ///
                /// </summary>
                /// <param name="lines"></param>
                /// <returns></returns>
                public string Run(string lines)
                {
                    return lines;
                }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 12));

        Assert.That(report.Findings, Has.Count.EqualTo(3));
        Assert.That(report.Findings.All(finding => finding.RuleId == CodeReviewRuleIds.EmptyXmlDocContent), Is.True);
        Assert.That(report.Findings.All(finding => finding.Severity == CodeReviewFindingSeverity.Hint), Is.True);
        Assert.That(report.Findings.Any(finding => finding.Message.Contains("<summary>", StringComparison.Ordinal)), Is.True);
        Assert.That(report.Findings.Any(finding => finding.Message.Contains("<param name=\"lines\">", StringComparison.Ordinal)), Is.True);
        Assert.That(report.Findings.Any(finding => finding.Message.Contains("<returns>", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void AnalyzeWhenParamAndReturnsAreSelfClosingReportsHintFindings()
    {
        const string source = """
            public sealed class Sample
            {
                /// <summary>Gets the result.</summary>
                /// <param name="lines" />
                /// <returns />
                public string Run(string lines)
                {
                    return lines;
                }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Has.Count.EqualTo(2));
        Assert.That(report.Findings.Any(finding => finding.Message.Contains("<param name=\"lines\">", StringComparison.Ordinal)), Is.True);
        Assert.That(report.Findings.Any(finding => finding.Message.Contains("<returns>", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void AnalyzeWhenXmlDocTagsHaveContentDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                /// <summary>Converts input.</summary>
                /// <param name="lines">The input lines.</param>
                /// <returns>The converted string.</returns>
                public string Run(string lines)
                {
                    return lines;
                }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenEmptyXmlDocsAreOutsideAddedLinesInModifiedFileDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                /// <summary></summary>
                /// <param name="lines"></param>
                /// <returns></returns>
                public string Run(string lines)
                {
                    return lines;
                }
            }
            """;

        var report = Analyze("M", source, [7]);

        Assert.That(report.Findings, Is.Empty);
    }

    private static CodeSmellReport Analyze(string status, string source, IEnumerable<int> addedLines)
    {
        var normalizedSource = (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalizedSource.Split('\n');
        var changedFile = new CodeReviewChangedFile(
            status,
            "Services/Sample.cs",
            "Services/Sample.cs",
            normalizedSource,
            lines,
            new HashSet<int>(addedLines ?? []));
        var context = new CodeReviewAnalysisContext(
            [changedFile],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var report = new CodeSmellReport();
        var check = new EmptyXmlDocContentCodeReviewCheck();
        check.Analyze(context, report);
        return report;
    }
}
