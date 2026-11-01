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
public sealed class EmptyMethodCodeReviewCheckTests
{
    [Test]
    public void AnalyzeWhenAddedOrdinaryMethodIsEmptyReportsSuggestion()
    {
        const string source = """
            public sealed class Sample
            {
                public void Run()
                {
                }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 6));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.EmptyMethod));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Suggestion));
        Assert.That(report.Findings[0].Message, Does.Contain("`Run`"));
    }

    [Test]
    public void AnalyzeWhenMethodBodyContainsOnlyCommentReportsSuggestion()
    {
        const string source = """
            public sealed class Sample
            {
                private static void Run()
                {
                    // TODO
                }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 7));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
    }

    [Test]
    public void AnalyzeWhenMethodHasStatementDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public void Run()
                {
                    System.Console.WriteLine("Hello");
                }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 7));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenMethodIsOverrideDoesNotReport()
    {
        const string source = """
            public class BaseSample
            {
                public virtual void Run()
                {
                }
            }

            public sealed class Sample : BaseSample
            {
                public override void Run()
                {
                }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 14));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenMethodIsVirtualDoesNotReport()
    {
        const string source = """
            public class Sample
            {
                public virtual void Run()
                {
                }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 6));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenMethodImplementsInterfaceDoesNotReport()
    {
        const string source = """
            public interface ISample
            {
                void Run();
            }

            public sealed class Sample : ISample
            {
                public void Run()
                {
                }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 11));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenMethodCouldBeUnresolvedInterfaceImplementationDoesNotReport()
    {
        const string source = """
            public sealed class Sample : ISample
            {
                public void Run()
                {
                }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 6));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenDisposeIsEmptyDoesNotDuplicateDedicatedDisposeRule()
    {
        const string source = """
            public sealed class Sample
            {
                public void Dispose()
                {
                }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 6));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenMethodLineWasNotAddedDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public void Run()
                {
                }
            }
            """;

        var report = Analyze("M", source, [4]);

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
        var check = new EmptyMethodCodeReviewCheck();
        check.Analyze(context, report);
        return report;
    }
}
