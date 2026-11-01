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
public sealed class UnusedMethodParameterCodeReviewCheckTests
{
    [Test]
    public void AnalyzeWhenAddedMethodDoesNotUseParameterReportsHint()
    {
        const string source = """
            public sealed class Sample
            {
                public int Run(int value)
                {
                    return 1;
                }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 7));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.UnusedMethodParameter));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
        Assert.That(report.Findings[0].Message, Does.Contain("`value`"));
        Assert.That(report.Findings[0].Message, Does.Contain("`Run`"));
    }

    [Test]
    public void AnalyzeWhenAddedMethodUsesParameterDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public int Run(int value)
                {
                    return value + 1;
                }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 7));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenParameterIsCapturedByLambdaDoesNotReport()
    {
        const string source = """
            using System;

            public sealed class Sample
            {
                public Func<int> Create(int value)
                {
                    return () => value;
                }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 9));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenParameterNameIsUnderscoreDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public int Run(int _)
                {
                    return 1;
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
                public virtual void Run(int value)
                {
                }
            }

            public sealed class Sample : BaseSample
            {
                public override void Run(int value)
                {
                }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 14));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenMethodImplementsInterfaceDoesNotReport()
    {
        const string source = """
            public interface ISample
            {
                void Run(int value);
            }

            public sealed class Sample : ISample
            {
                public void Run(int value)
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
                public void Run(int value)
                {
                }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 6));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenEventHandlerDoesNotUseSenderDoesNotReport()
    {
        const string source = """
            using System;

            public sealed class Sample
            {
                public void ButtonClicked(object sender, EventArgs e)
                {
                    Handle(e);
                }

                private static void Handle(EventArgs e)
                {
                    _ = e.GetType();
                }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 13));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenEventHandlerUsesNeitherParameterDoesNotReport()
    {
        const string source = """
            using System;

            public sealed class Sample
            {
                public void ButtonClicked(object sender, EventArgs e)
                {
                    Handle();
                }

                private static void Handle()
                {
                }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 13));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenOrdinarySenderParameterIsUnusedStillReportsHint()
    {
        const string source = """
            public sealed class Sample
            {
                public int Run(object sender)
                {
                    return 1;
                }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 7));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Message, Does.Contain("`sender`"));
    }

    [Test]
    public void AnalyzeWhenMethodLineWasNotAddedDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public int Run(int value)
                {
                    return 1;
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
        var check = new UnusedMethodParameterCodeReviewCheck();
        check.Analyze(context, report);
        return report;
    }
}
