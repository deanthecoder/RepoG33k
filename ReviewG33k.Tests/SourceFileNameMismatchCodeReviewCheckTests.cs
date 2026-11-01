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
public sealed class SourceFileNameMismatchCodeReviewCheckTests
{
    [Test]
    public void AnalyzeWhenAddedSingleClassFileNameDiffersReportsHint()
    {
        const string source = """
            public sealed class Customer
            {
            }
            """;

        var report = Analyze("A", "Services/Order.cs", source, Enumerable.Range(1, 3));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.SourceFileNameMismatch));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
        Assert.That(report.Findings[0].Message, Does.Contain("`Order.cs`"));
        Assert.That(report.Findings[0].Message, Does.Contain("`Customer`"));
    }

    [Test]
    public void AnalyzeWhenAddedSingleEnumFileNameDiffersReportsHint()
    {
        const string source = """
            public enum ReviewState
            {
                Pending
            }
            """;

        var report = Analyze("A", "Services/ReviewStatus.cs", source, Enumerable.Range(1, 4));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Message, Does.Contain("`ReviewState.cs`"));
    }

    [Test]
    public void AnalyzeWhenFileNameMatchesSingleTypeDoesNotReport()
    {
        const string source = """
            public readonly struct Customer
            {
            }
            """;

        var report = Analyze("A", "Services/Customer.cs", source, Enumerable.Range(1, 3));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenFileHasMultipleTopLevelTypesDoesNotReport()
    {
        const string source = """
            public sealed class Customer
            {
            }

            public sealed class Order
            {
            }
            """;

        var report = Analyze("A", "Services/Customer.cs", source, Enumerable.Range(1, 7));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenSingleTypeIsPartialDoesNotReport()
    {
        const string source = """
            public sealed partial class Customer
            {
            }
            """;

        var report = Analyze("A", "Services/Customer.Generated.cs", source, Enumerable.Range(1, 3));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenTypeLineWasNotAddedDoesNotReport()
    {
        const string source = """
            public sealed class Customer
            {
                public void Run()
                {
                }
            }
            """;

        var report = Analyze("M", "Services/Order.cs", source, [4]);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenTypeIsInsideNamespaceStillReportsHint()
    {
        const string source = """
            namespace ReviewG33k.Services;

            public interface ICustomer
            {
            }
            """;

        var report = Analyze("A", "Services/CustomerService.cs", source, Enumerable.Range(1, 5));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Message, Does.Contain("`ICustomer.cs`"));
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
        var check = new SourceFileNameMismatchCodeReviewCheck();
        check.Analyze(context, report);
        return report;
    }
}
