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
public sealed class PropertySetterIgnoresValueCodeReviewCheckTests
{
    [Test]
    public void AnalyzeWhenSetterAssignsConstantReportsSuggestion()
    {
        const string source = """
            public sealed class Sample
            {
                private int m_count;

                public int Count
                {
                    get => m_count;
                    set => m_count = 1;
                }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.PropertySetterIgnoresValue));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Suggestion));
        Assert.That(report.Findings[0].Message, Does.Contain("`Count`"));
        Assert.That(report.Findings[0].Message, Does.Contain("`value`"));
    }

    [Test]
    public void AnalyzeWhenBlockSetterDoesNotUseValueReportsSuggestion()
    {
        const string source = """
            public sealed class Sample
            {
                private int m_count;

                public int Count
                {
                    get => m_count;
                    set
                    {
                        m_count = 1;
                    }
                }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 13));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
    }

    [Test]
    public void AnalyzeWhenSetterUsesValueDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                private int m_count;

                public int Count
                {
                    get => m_count;
                    set => m_count = value;
                }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenAutoPropertyDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public int Count { get; set; }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 4));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenSetterThrowsDoesNotReport()
    {
        const string source = """
            using System;

            public sealed class Sample
            {
                public int Count
                {
                    get => 0;
                    set => throw new NotSupportedException();
                }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 11));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenSetterLineWasNotAddedDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                private int m_count;

                public int Count
                {
                    get => m_count;
                    set => m_count = 1;
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
        var check = new PropertySetterIgnoresValueCodeReviewCheck();
        check.Analyze(context, report);
        return report;
    }
}
