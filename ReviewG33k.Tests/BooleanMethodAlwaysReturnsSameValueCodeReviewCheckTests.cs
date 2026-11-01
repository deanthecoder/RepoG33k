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
public sealed class BooleanMethodAlwaysReturnsSameValueCodeReviewCheckTests
{
    [Test]
    public void AnalyzeWhenNonPrivateBoolMethodAlwaysReturnsTrueReportsHint()
    {
        const string source = """
            public sealed class Sample
            {
                public bool SaveChanges()
                {
                    if (m_currentDescription == NewDescription)
                        return true;

                    FinalDescription = NewDescription;
                    return true;
                }

                private string m_currentDescription;
                public string NewDescription { get; set; }
                public string FinalDescription { get; set; }
            }
            """;

        var report = Analyze(source, new BooleanMethodAlwaysReturnsSameValueCodeReviewCheck());

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.BooleanMethodAlwaysReturnsSameValue));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
        Assert.That(report.Findings[0].Message, Does.Contain("always returns `true`"));
    }

    [Test]
    public void AnalyzeWhenPrivateBoolMethodAlwaysReturnsFalseReportsSuggestion()
    {
        const string source = """
            public sealed class Sample
            {
                private bool TryPersist()
                {
                    if (m_shouldSkip)
                        return false;

                    Save();
                    return false;
                }

                private bool m_shouldSkip;

                private void Save()
                {
                }
            }
            """;

        var report = Analyze(source, new PrivateBooleanMethodAlwaysReturnsSameValueCodeReviewCheck());

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.PrivateBooleanMethodAlwaysReturnsSameValue));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Suggestion));
        Assert.That(report.Findings[0].Message, Does.Contain("always returns `false`"));
    }

    [Test]
    public void AnalyzeWhenMethodReturnValueVariesDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public bool SaveChanges()
                {
                    if (m_currentDescription == NewDescription)
                        return true;

                    FinalDescription = NewDescription;
                    return false;
                }

                private string m_currentDescription;
                public string NewDescription { get; set; }
                public string FinalDescription { get; set; }
            }
            """;

        var publicReport = Analyze(source, new BooleanMethodAlwaysReturnsSameValueCodeReviewCheck());
        var privateReport = Analyze(source, new PrivateBooleanMethodAlwaysReturnsSameValueCodeReviewCheck());

        Assert.That(publicReport.Findings, Is.Empty);
        Assert.That(privateReport.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenMethodHasSingleLiteralReturnAndNoControlFlowDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                private bool IsSupported()
                {
                    return true;
                }
            }
            """;

        var report = Analyze(source, new PrivateBooleanMethodAlwaysReturnsSameValueCodeReviewCheck());

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenMethodImplementsInterfaceDoesNotReport()
    {
        const string source = """
            public interface ISaver
            {
                bool SaveChanges();
            }

            public sealed class Sample : ISaver
            {
                public bool SaveChanges()
                {
                    if (m_currentDescription == NewDescription)
                        return true;

                    FinalDescription = NewDescription;
                    return true;
                }

                private string m_currentDescription;
                public string NewDescription { get; set; }
                public string FinalDescription { get; set; }
            }
            """;

        var report = Analyze(source, new BooleanMethodAlwaysReturnsSameValueCodeReviewCheck());

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenMethodIsOverrideDoesNotReport()
    {
        const string source = """
            public abstract class BaseSaver
            {
                public abstract bool SaveChanges();
            }

            public sealed class Sample : BaseSaver
            {
                public override bool SaveChanges()
                {
                    if (m_currentDescription == NewDescription)
                        return true;

                    FinalDescription = NewDescription;
                    return true;
                }

                private string m_currentDescription;
                public string NewDescription { get; set; }
                public string FinalDescription { get; set; }
            }
            """;

        var report = Analyze(source, new BooleanMethodAlwaysReturnsSameValueCodeReviewCheck());

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenExpressionBodiedMethodReturnsLiteralDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public bool SaveChanges() => true;
            }
            """;

        var report = Analyze(source, new BooleanMethodAlwaysReturnsSameValueCodeReviewCheck());

        Assert.That(report.Findings, Is.Empty);
    }

    private static CodeSmellReport Analyze(string source, ICodeReviewCheck check)
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
        var context = new CodeReviewAnalysisContext(
            [changedFile],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var report = new CodeSmellReport();
        check.Analyze(context, report);
        return report;
    }
}
