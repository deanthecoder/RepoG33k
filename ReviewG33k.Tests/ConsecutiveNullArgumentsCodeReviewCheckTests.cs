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
public sealed class ConsecutiveNullArgumentsCodeReviewCheckTests
{
    [Test]
    public void AnalyzeWhenInvocationHasConsecutiveUnnamedNullLiteralsReportsHint()
    {
        const string source = """
            public sealed class Sample
            {
                private static void DoFoo(object first, object second, object third)
                {
                }

                public void Run()
                {
                    DoFoo(null, null, this);
                }
            }
            """;

        var report = AnalyzeSource(source, Enumerable.Range(1, 11));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.ConsecutiveNullArguments));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
        Assert.That(report.Findings[0].Message, Does.Contain("Consecutive null arguments"));
    }

    [Test]
    public void AnalyzeWhenNullArgumentsAreNamedDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                private static void DoFoo(object first, object second, object third)
                {
                }

                public void Run()
                {
                    DoFoo(first: null, second: null, third: this);
                }
            }
            """;

        var report = AnalyzeSource(source, Enumerable.Range(1, 11));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenNullArgumentsAreNotConsecutiveDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                private static void DoFoo(object first, int count, object second)
                {
                }

                public void Run()
                {
                    DoFoo(null, 1, null);
                }
            }
            """;

        var report = AnalyzeSource(source, Enumerable.Range(1, 11));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenConsecutiveUnnamedNullInvocationIsOutsideAddedLinesDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                private static void DoFoo(object first, object second, object third)
                {
                }

                public void Run()
                {
                    DoFoo(null, null, this);
                }
            }
            """;

        var report = AnalyzeSource(source, [1, 2], status: "M");

        Assert.That(report.Findings, Is.Empty);
    }

    private static CodeSmellReport AnalyzeSource(string source, IEnumerable<int> addedLineNumbers, string status = "A")
    {
        var normalizedSource = (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalizedSource.Split('\n');
        var changedFile = new CodeReviewChangedFile(
            status,
            "Services/Sample.cs",
            "Services/Sample.cs",
            normalizedSource,
            lines,
            new HashSet<int>(addedLineNumbers ?? []));
        var context = new CodeReviewAnalysisContext(
            [changedFile],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var report = new CodeSmellReport();
        var check = new ConsecutiveNullArgumentsCodeReviewCheck();
        check.Analyze(context, report);
        return report;
    }
}
