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
public sealed class LinqCountComparedToZeroCodeReviewCheckTests
{
    [Test]
    public void AnalyzeWhenCountEqualsZeroReportsHint()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;

            public sealed class Sample
            {
                public bool HasNoValues(IEnumerable<int> values)
                {
                    return values.Count() == 0;
                }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.LinqCountComparedToZero));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
        Assert.That(report.Findings[0].Message, Does.Contain("`!Any()`"));
    }

    [Test]
    public void AnalyzeWhenCountGreaterThanZeroReportsHint()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;

            public sealed class Sample
            {
                public bool HasValues(IEnumerable<int> values)
                {
                    return values.Count() > 0;
                }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Message, Does.Contain("`Any()`"));
    }

    [Test]
    public void AnalyzeWhenZeroLessThanLongCountReportsHint()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;

            public sealed class Sample
            {
                public bool HasValues(IEnumerable<int> values)
                {
                    return 0L < values.LongCount();
                }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
    }

    [Test]
    public void AnalyzeWhenCountPropertyComparedToZeroDoesNotReport()
    {
        const string source = """
            using System.Collections.Generic;

            public sealed class Sample
            {
                public bool HasNoValues(List<int> values)
                {
                    return values.Count == 0;
                }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 9));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenCountComparesToNonZeroDoesNotReport()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;

            public sealed class Sample
            {
                public bool HasManyValues(IEnumerable<int> values)
                {
                    return values.Count() > 3;
                }
            }
            """;

        var report = Analyze("A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenLineWasNotAddedDoesNotReport()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;

            public sealed class Sample
            {
                public bool HasValues(IEnumerable<int> values)
                {
                    return values.Count() > 0;
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
        var check = new LinqCountComparedToZeroCodeReviewCheck();
        check.Analyze(context, report);
        return report;
    }
}
