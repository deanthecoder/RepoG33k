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
public sealed class EagerMaterializedEnumerableReturnCodeReviewCheckTests
{
    [Test]
    public void AnalyzeWhenIEnumerableMethodReturnsToListReportsHint()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;

            public sealed class Sample
            {
                public IEnumerable<int> GetValues(IEnumerable<int> values)
                {
                    return values.Where(value => value > 0).ToList();
                }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.EagerMaterializedEnumerableReturn));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
        Assert.That(report.Findings[0].Message, Does.Contain("`GetValues`"));
        Assert.That(report.Findings[0].Message, Does.Contain("`ToList()`"));
    }

    [Test]
    public void AnalyzeWhenIEnumerableMethodReturnsToArrayReportsHint()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;

            public sealed class Sample
            {
                public IEnumerable<int> GetValues(IEnumerable<int> values)
                {
                    return values.ToArray();
                }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Message, Does.Contain("`ToArray()`"));
    }

    [Test]
    public void AnalyzeWhenIEnumerableMethodReturnsSequenceDoesNotReport()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;

            public sealed class Sample
            {
                public IEnumerable<int> GetValues(IEnumerable<int> values)
                {
                    return values.Where(value => value > 0);
                }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenListMethodReturnsToListDoesNotReport()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;

            public sealed class Sample
            {
                public List<int> GetValues(IEnumerable<int> values)
                {
                    return values.ToList();
                }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenReturnLineWasNotAddedDoesNotReport()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;

            public sealed class Sample
            {
                public IEnumerable<int> GetValues(IEnumerable<int> values)
                {
                    return values.ToList();
                }
            }
            """;

        var report = Analyze("M", source, [6]);

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
        var check = new EagerMaterializedEnumerableReturnCodeReviewCheck();
        check.Analyze(context, report);
        return report;
    }
}
