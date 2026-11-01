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
public sealed class ConsecutiveBooleanArgumentsCodeReviewCheckTests
{
    [Test]
    public void AnalyzeWhenInvocationHasConsecutiveUnnamedBooleanLiteralsReportsHint()
    {
        const string source = """
            public sealed class Sample
            {
                private static void DoFoo(object item, string text, bool first, bool second, bool third)
                {
                }

                public void Run()
                {
                    DoFoo(this, "valid string", true, true, false);
                }
            }
            """;

        var report = AnalyzeSource(source, Enumerable.Range(1, 12));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.ConsecutiveBooleanArguments));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
        Assert.That(report.Findings[0].Message, Does.Contain("Consecutive boolean literal arguments"));
    }

    [Test]
    public void AnalyzeWhenBooleanArgumentsAreNamedDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                private static void DoFoo(object item, string text, bool first, bool second, bool third)
                {
                }

                public void Run()
                {
                    DoFoo(this, "valid string", first: true, second: true, third: false);
                }
            }
            """;

        var report = AnalyzeSource(source, Enumerable.Range(1, 12));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenBooleanArgumentsAreNotConsecutiveDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                private static void DoFoo(bool first, int count, bool second)
                {
                }

                public void Run()
                {
                    DoFoo(true, 1, false);
                }
            }
            """;

        var report = AnalyzeSource(source, Enumerable.Range(1, 11));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenConsecutiveUnnamedBooleanVariablesAreUsedDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                private static void DoFoo(bool first, bool second)
                {
                }

                public void Run()
                {
                    var first = true;
                    var second = false;
                    DoFoo(first, second);
                }
            }
            """;

        var report = AnalyzeSource(source, Enumerable.Range(1, 13));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenConsecutiveUnnamedBooleanInvocationIsOutsideAddedLinesDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                private static void DoFoo(object item, string text, bool first, bool second, bool third)
                {
                }

                public void Run()
                {
                    DoFoo(this, "valid string", true, true, false);
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
        var check = new ConsecutiveBooleanArgumentsCodeReviewCheck();
        check.Analyze(context, report);
        return report;
    }
}
