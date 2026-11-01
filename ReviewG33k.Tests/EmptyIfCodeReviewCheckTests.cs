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
public sealed class EmptyIfCodeReviewCheckTests
{
    [TestCase("if (ready) { }")]
    [TestCase("if (ready) { } else { Run(); }")]
    [TestCase("if (ready) { /* Intentionally empty. */ }")]
    [TestCase("if (ready) { // TODO\n }")]
    [TestCase("if (ready);")]
    [TestCase("if (TrySave()) { }")]
    [TestCase("if (ready) Run(); else if (ready) { }")]
    public void AnalyzeWhenIfBodyIsEmptyReportsSuggestion(string statement)
    {
        var report = Analyze("class Sample { void Run() { " + statement + " } }");

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.EmptyIf));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Suggestion));
        Assert.That(report.Findings[0].LineNumber, Is.EqualTo(1));
    }

    [Test]
    public void AnalyzeWhenPageShooterPatternHasEmptyIfReportsInnerBranch()
    {
        const string source = """
            class Sample
            {
                bool Open()
                {
                    if (TryOpenPageShooterJob(jobFile))
                    {
                        if (m_pageShooterCompletionTask is { IsCompleted: false })
                        {
                        }
                        else
                        {
                            m_pageShooterCompletionTask = Task.Run(RunPageShooterCompletionWorker);
                        }

                        return true;
                    }

                    return false;
                }
            }
            """;

        var report = Analyze(source);

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].LineNumber, Is.EqualTo(7));
        Assert.That(report.Findings[0].Message, Does.Contain("inverting the condition"));
    }

    [TestCase("if (ready) { Run(); }")]
    [TestCase("if (ready) Run();")]
    [TestCase("if (ready) { Run(); } else { }")]
    [TestCase("var text = \"if (ready) { }\";")]
    [TestCase("// if (ready) { }\n")]
    [TestCase("if (ready) {\n#if FEATURE\nRun();\n#endif\n}")]
    public void AnalyzeWhenBodyIsNotEmptyOrHasConditionalCodeDoesNotReport(string statement)
    {
        Assert.That(Analyze("class Sample { void Run() { " + statement + " } }").Findings, Is.Empty);
    }

    [TestCase(5, 1)]
    [TestCase(6, 1)]
    [TestCase(7, 1)]
    [TestCase(10, 0)]
    [TestCase(1, 0)]
    public void AnalyzeOnlyReportsChangesInIfHeaderOrBody(int addedLine, int expectedCount)
    {
        const string source = """
            class Sample
            {
                void Run()
                {
                    if (ready)
                    {
                    }
                    else
                    {
                        Run();
                    }
                }
            }
            """;

        Assert.That(Analyze(source, "M", [addedLine]).Findings, Has.Count.EqualTo(expectedCount));
    }

    [Test]
    public void AnalyzeWhenFileIsNotCSharpDoesNotReport()
    {
        Assert.That(Analyze("if (ready) { }", path: "Sample.cpp").Findings, Is.Empty);
    }

    [Test]
    public void DefaultAnalyzerIncludesEmptyIfCheck()
    {
        var analyzer = new CodeSmellReportAnalyzer(new GitCommandRunner());
        Assert.That(analyzer.AllChecks.Any(check => check.RuleId == CodeReviewRuleIds.EmptyIf), Is.True);
    }

    private static CodeSmellReport Analyze(
        string source, string status = "A", IEnumerable<int> addedLines = null, string path = "Sample.cs")
    {
        var normalizedSource = source.Replace("\r\n", "\n").Replace('\r', '\n');
        var file = new CodeReviewChangedFile(status, path, path, normalizedSource,
            normalizedSource.Split('\n'), new HashSet<int>(addedLines ?? []));
        var context = new CodeReviewAnalysisContext(
            [file], new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var report = new CodeSmellReport();
        new EmptyIfCodeReviewCheck().Analyze(context, report);
        return report;
    }
}
