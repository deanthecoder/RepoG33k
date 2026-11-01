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
public sealed class MultipleClassesPerFileCodeReviewCheckTests
{
    [Test]
    public void AnalyzeWhenAddedFileDefinesMultipleClassesReportsHint()
    {
        const string source = """
            public sealed class FirstClass
            {
            }

            public sealed class SecondClass
            {
            }
            """;

        var report = Analyze("A", "Services/Sample.cs", source, Enumerable.Range(1, 7));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.MultipleClassesPerFile));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
        Assert.That(report.Findings[0].LineNumber, Is.EqualTo(5));
    }

    [Test]
    public void AnalyzeWhenAddedFileHasSingleClassDoesNotReport()
    {
        const string source = """
            public sealed class OnlyClass
            {
            }
            """;

        var report = Analyze("A", "Services/Sample.cs", source, Enumerable.Range(1, 3));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenAddedFileHasNestedPrivateClassDoesNotReport()
    {
        const string source = """
            public sealed class OuterClass
            {
                private sealed class InnerClass
                {
                }
            }
            """;

        var report = Analyze("A", "Services/Sample.cs", source, Enumerable.Range(1, 7));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenModifiedFileHasMultipleClassesWithoutAddedClassLineDoesNotReport()
    {
        const string source = """
            public sealed class FirstClass
            {
                public void Run()
                {
                }
            }

            public sealed class SecondClass
            {
            }
            """;

        var report = Analyze("M", "Services/Sample.cs", source, [4]);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenModifiedFileAddsAnotherClassReportsHint()
    {
        const string source = """
            public sealed class FirstClass
            {
            }

            public sealed class AddedClass
            {
            }
            """;

        var report = Analyze("M", "Services/Sample.cs", source, [5]);

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].LineNumber, Is.EqualTo(5));
    }

    private static CodeSmellReport Analyze(string status, string path, string source, IEnumerable<int> addedLines)
    {
        var normalizedSource = (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalizedSource.Split('\n');
        var changedFile = new CodeReviewChangedFile(
            status,
            path,
            path,
            normalizedSource,
            lines,
            new HashSet<int>(addedLines ?? []));
        var context = new CodeReviewAnalysisContext(
            [changedFile],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var report = new CodeSmellReport();
        var check = new MultipleClassesPerFileCodeReviewCheck();
        check.Analyze(context, report);
        return report;
    }
}
