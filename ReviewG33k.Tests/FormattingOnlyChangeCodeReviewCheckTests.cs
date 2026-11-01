// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Text;
using ReviewG33k.Services;
using ReviewG33k.Services.Checks;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class FormattingOnlyChangeCodeReviewCheckTests
{
    [Test]
    public void AnalyzeWhenOnlyCommentChangesInProductionFileDoesNotReport()
    {
        const string baselineText = """
            public sealed class Sample
            {
                // Old comment.
                public int Count => 1;
            }
            """;
        const string currentText = """
            public sealed class Sample
            {
                // New comment.
                public int Count => 1;
            }
            """;

        var report = Analyze("Services/Sample.cs", currentText, baselineText);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenOnlyFormattingChangesInProductionFileReportsHint()
    {
        const string baselineText = """
            public sealed class Sample { public int Count => 1; }
            """;
        const string currentText = """
            public sealed class Sample
            {
                    public int Count => 1;
            }
            """;

        var report = Analyze("Services/Sample.cs", currentText, baselineText);

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.FormattingOnlyChange));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
    }

    [Test]
    public void AnalyzeWhenCodeChangesDoesNotReport()
    {
        const string baselineText = """
            public sealed class Sample
            {
                public int Count => 1;
            }
            """;
        const string currentText = """
            public sealed class Sample
            {
                public int Count => 2;
            }
            """;

        var report = Analyze("Services/Sample.cs", currentText, baselineText);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenTestFileOnlyCommentChangesDoesNotReport()
    {
        const string baselineText = """
            public sealed class SampleTests
            {
                // Old comment.
            }
            """;
        const string currentText = """
            public sealed class SampleTests
            {
                // New comment.
            }
            """;

        var report = Analyze("ReviewG33k.Tests/SampleTests.cs", currentText, baselineText);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenOnlyTrailingWhitespaceChangesDoesNotDuplicateExistingRule()
    {
        const string baselineText = "public sealed class Sample\n{\n    public int Count => 1;\n}\n";
        const string currentText = "public sealed class Sample   \n{\n    public int Count => 1;\n}\n";

        var report = Analyze("Services/Sample.cs", currentText, baselineText);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenOnlyNewlineStyleChangesDoesNotDuplicateExistingRule()
    {
        const string baselineText = "public sealed class Sample\n{\n    public int Count => 1;\n}\n";
        const string currentText = "public sealed class Sample\r\n{\r\n    public int Count => 1;\r\n}\r\n";

        var report = Analyze("Services/Sample.cs", currentText, baselineText);

        Assert.That(report.Findings, Is.Empty);
    }

    private static CodeSmellReport Analyze(string path, string currentText, string baselineText)
    {
        var normalizedCurrentText = (currentText ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalizedCurrentText.Split('\n');
        var changedFile = new CodeReviewChangedFile(
            "M",
            path,
            path,
            currentText,
            lines,
            new HashSet<int>(Enumerable.Range(1, lines.Length)),
            baselineText,
            Encoding.UTF8.GetBytes(currentText ?? string.Empty),
            Encoding.UTF8.GetBytes(baselineText ?? string.Empty));
        var context = new CodeReviewAnalysisContext(
            [changedFile],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            allChangedFiles: [changedFile]);

        var report = new CodeSmellReport();
        var check = new FormattingOnlyChangeCodeReviewCheck();
        check.Analyze(context, report);
        return report;
    }
}
