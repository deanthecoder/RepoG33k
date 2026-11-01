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

public sealed class LocalVariablePrefixCodeReviewCheckTests
{
    [Test]
    public void AnalyzeWhenLocalStartsWithMUnderscoreReportsHint()
    {
        const string source = """
            public sealed class Sample
            {
                public void Run()
                {
                    var m_provider = new object();
                    _ = m_provider;
                }
            }
            """;

        var report = AnalyzeSource(source);

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.LocalVariablePrefix));
        Assert.That(report.Findings[0].Message, Does.Contain("`m_provider`"));
    }

    [Test]
    public void AnalyzeWhenLocalStartsWithUnderscoreReportsHint()
    {
        const string source = """
            public sealed class Sample
            {
                public void Run()
                {
                    var _provider = new object();
                    _ = _provider;
                }
            }
            """;

        var report = AnalyzeSource(source);

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Message, Does.Contain("`_provider`"));
    }

    [Test]
    public void AnalyzeWhenDiscardUnderscoreIsUsedDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public void Run()
                {
                    var value = 42;
                    _ = value;
                }
            }
            """;

        var report = AnalyzeSource(source);

        Assert.That(report.Findings, Is.Empty);
    }

    private static CodeSmellReport AnalyzeSource(string source)
    {
        var normalizedSource = (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalizedSource.Split('\n');
        var changedFile = new CodeReviewChangedFile(
            "A",
            "Services/Sample.cs",
            "Services/Sample.cs",
            normalizedSource,
            lines,
            new HashSet<int>(Enumerable.Range(1, lines.Length)));
        var context = new CodeReviewAnalysisContext([changedFile], new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var report = new CodeSmellReport();
        var check = new LocalVariablePrefixCodeReviewCheck();
        check.Analyze(context, report);
        return report;
    }
}
