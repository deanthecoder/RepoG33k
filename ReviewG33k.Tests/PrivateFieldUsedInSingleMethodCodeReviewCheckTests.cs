// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Core.UnitTesting;
using ReviewG33k.Services;
using ReviewG33k.Services.Checks;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class PrivateFieldUsedInSingleMethodCodeReviewCheckTests : TestsBase
{
    [Test]
    public void AnalyzeWhenNonReadonlyFieldIsOnlyUsedInOneMethodDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                private int m_count;

                public void Run()
                {
                    m_count = 42;
                    System.Console.WriteLine(m_count);
                }
            }
            """;

        var report = AnalyzeSource(source, Enumerable.Range(1, 11));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenReadonlyFieldIsOnlyUsedInConstructorReportsHint()
    {
        const string source = """
            using System.Windows.Input;

            public sealed class Sample
            {
                private readonly ICommand m_commandImpl;

                public ICommand Command { get; }

                public Sample()
                {
                    m_commandImpl = null;
                    Command = m_commandImpl;
                }
            }
            """;

        var report = AnalyzeSource(source, Enumerable.Range(1, 14));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.PrivateFieldUsedInSingleMethod));
        Assert.That(report.Findings[0].Message, Does.Contain("constructor"));
    }

    [Test]
    public void AnalyzeWhenReadonlyFieldIsReadOutsideConstructorDoesNotReport()
    {
        const string source = """
            using System.Windows.Input;

            public sealed class Sample
            {
                private readonly ICommand m_commandImpl;

                public ICommand Command => m_commandImpl;

                public Sample()
                {
                    m_commandImpl = null;
                }
            }
            """;

        var report = AnalyzeSource(source, Enumerable.Range(1, 13));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenReadonlyFieldIsUsedInAnotherMethodWithUnresolvedTypeDoesNotReport()
    {
        const string source = """
            using System;

            public sealed class Sample
            {
                private readonly Func<MissingType> m_getStatusFunc;
                private MissingType m_statusData;

                public Sample(Func<MissingType> getStatusFunc)
                {
                    m_getStatusFunc = getStatusFunc;
                }

                public void Refresh()
                {
                    m_statusData = m_getStatusFunc();
                }
            }
            """;

        var report = AnalyzeSource(source, Enumerable.Range(1, 17));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenReadonlyFieldHasInitializerDoesNotReport()
    {
        const string source = """
            using System.Windows.Input;

            public sealed class Sample
            {
                private readonly ICommand m_commandImpl = null;

                public ICommand Command { get; }

                public Sample()
                {
                    Command = m_commandImpl;
                }
            }
            """;

        var report = AnalyzeSource(source, Enumerable.Range(1, 12));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenConstructorReadsFieldBeforeWritingDoesNotReport()
    {
        const string source = """
            using System.Windows.Input;

            public sealed class Sample
            {
                private readonly ICommand m_commandImpl;

                public ICommand Command { get; }

                public Sample()
                {
                    Command = m_commandImpl;
                    m_commandImpl = null;
                }
            }
            """;

        var report = AnalyzeSource(source, Enumerable.Range(1, 13));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenConstructorUpdatesFieldFromPreviousValueDoesNotReport()
    {
        const string source = """
            using System.Windows.Input;

            public sealed class Sample
            {
                private readonly ICommand m_commandImpl;

                public ICommand Command { get; }

                public Sample(ICommand existing)
                {
                    m_commandImpl = m_commandImpl ?? existing;
                    Command = m_commandImpl;
                }
            }
            """;

        var report = AnalyzeSource(source, Enumerable.Range(1, 13));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenReadonlyFieldIsAssignedInMultipleConstructorsDoesNotReport()
    {
        const string source = """
            using System.Windows.Input;

            public sealed class Sample
            {
                private readonly ICommand m_commandImpl;

                public Sample()
                {
                    m_commandImpl = null;
                }

                public Sample(ICommand command)
                {
                    m_commandImpl = command;
                }
            }
            """;

        var report = AnalyzeSource(source, Enumerable.Range(1, 15));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenConstructorOnlyUsageIsOutsideAddedLinesDoesNotReport()
    {
        const string source = """
            using System.Windows.Input;

            public sealed class Sample
            {
                private readonly ICommand m_commandImpl;

                public ICommand Command { get; }

                public Sample()
                {
                    m_commandImpl = null;
                    Command = m_commandImpl;
                }
            }
            """;

        var report = AnalyzeSource(source, [1, 2], status: "M");

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenRealReviewResultsWindowFileIsScannedDoesNotReportStaleOpenTargetMenuCommandField()
    {
        var fullPath = Path.Combine(ProjectDir.Parent.FullName, "Views", "ReviewResultsWindow.axaml.cs");
        var source = File.ReadAllText(fullPath);
        var normalizedSource = source.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalizedSource.Split('\n');

        var changedFile = new CodeReviewChangedFile(
            "A",
            "Views/ReviewResultsWindow.axaml.cs",
            fullPath,
            normalizedSource,
            lines,
            new HashSet<int>(Enumerable.Range(1, lines.Length)));
        var context = new CodeReviewAnalysisContext(
            [changedFile],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var report = new CodeSmellReport();
        var check = new PrivateFieldUsedInSingleMethodCodeReviewCheck();
        check.Analyze(context, report);

        Assert.That(
            report.Findings.Any(finding =>
                finding.RuleId == CodeReviewRuleIds.PrivateFieldUsedInSingleMethod &&
                finding.Message.Contains("m_openTargetMenuCommandImpl", StringComparison.Ordinal)),
            Is.False);
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
        var check = new PrivateFieldUsedInSingleMethodCodeReviewCheck();
        check.Analyze(context, report);
        return report;
    }
}
