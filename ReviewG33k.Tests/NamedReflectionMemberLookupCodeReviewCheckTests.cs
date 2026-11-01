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
public sealed class NamedReflectionMemberLookupCodeReviewCheckTests
{
    [Test]
    public void AnalyzeWhenGetMethodUsesStringLiteralReportsSuggestion()
    {
        const string source = """
            using System;

            public sealed class Sample
            {
                public void Run(Type type)
                {
                    _ = type.GetMethod("DoWork");
                }
            }
            """;

        var report = AnalyzeSource(source, Enumerable.Range(1, 8));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.NamedReflectionMemberLookup));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Suggestion));
    }

    [Test]
    public void AnalyzeWhenGetPropertyUsesNameofReportsSuggestion()
    {
        const string source = """
            using System;

            public sealed class Sample
            {
                public string Name { get; set; }

                public void Run(Type type)
                {
                    _ = type.GetProperty(nameof(Name));
                }
            }
            """;

        var report = AnalyzeSource(source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.NamedReflectionMemberLookup));
    }

    [Test]
    public void AnalyzeWhenEnumeratingMethodsDoesNotReport()
    {
        const string source = """
            using System;

            public sealed class Sample
            {
                public void Run(Type type)
                {
                    foreach (var method in type.GetMethods())
                    {
                    }
                }
            }
            """;

        var report = AnalyzeSource(source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenNameComesFromVariableDoesNotReport()
    {
        const string source = """
            using System;

            public sealed class Sample
            {
                public void Run(Type type, string memberName)
                {
                    _ = type.GetField(memberName);
                }
            }
            """;

        var report = AnalyzeSource(source, Enumerable.Range(1, 8));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenLookupIsOutsideAddedLinesDoesNotReport()
    {
        const string source = """
            using System;

            public sealed class Sample
            {
                public void Run(Type type)
                {
                    _ = type.GetMethod("DoWork");
                }
            }
            """;

        var report = AnalyzeSource(source, [1, 2], status: "M");

        Assert.That(report.Findings, Is.Empty);
    }

    private static CodeSmellReport AnalyzeSource(string source, IEnumerable<int> addedLineNumbers, string status = "A")
    {
        var normalizedSource = (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var changedFile = new CodeReviewChangedFile(
            status,
            "Services/Sample.cs",
            "Services/Sample.cs",
            normalizedSource,
            normalizedSource.Split('\n'),
            new HashSet<int>(addedLineNumbers ?? []));
        var context = new CodeReviewAnalysisContext(
            [changedFile],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var report = new CodeSmellReport();
        var check = new NamedReflectionMemberLookupCodeReviewCheck();
        check.Analyze(context, report);
        return report;
    }
}
