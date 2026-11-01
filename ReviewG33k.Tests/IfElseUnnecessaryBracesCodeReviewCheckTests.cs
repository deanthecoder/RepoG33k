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
public sealed class IfElseUnnecessaryBracesCodeReviewCheckTests
{
    [Test]
    public void AnalyzeWhenSingleStatementBranchContainsCommentDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public void Run(bool encrypt, FileInfo inputFile, FileInfo outputFile)
                {
                    if (encrypt)
                    {
                        // Encrypt the file if asked.
                        inputFile.CopyTo(outputFile);
                    }
                    else
                    {
                        inputFile.CopyTo(outputFile);
                    }
                }
            }
            """;

        var report = AnalyzeSource(source, Enumerable.Range(1, 15));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void CanFixWhenFindingMatchesRuleAndLineIsPositiveReturnsTrue()
    {
        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Hint,
            CodeReviewRuleIds.IfElseUnnecessaryBraces,
            "Sample.cs",
            8,
            "Unnecessary braces.");
        var check = new IfElseUnnecessaryBracesCodeReviewCheck();

        var canFix = check.CanFix(finding);

        Assert.That(canFix, Is.True);
    }

    [Test]
    public void TryFixWhenFindingIsNullThrowsArgumentNullException()
    {
        var check = new IfElseUnnecessaryBracesCodeReviewCheck();

        Assert.Throws<ArgumentNullException>(() => check.TryFix(null, new FileInfo("Sample.cs"), out _));
    }

    [Test]
    public void TryFixWhenFilePathIsBlankReturnsFalse()
    {
        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Hint,
            CodeReviewRuleIds.IfElseUnnecessaryBraces,
            "Sample.cs",
            8,
            "Unnecessary braces.");
        var check = new IfElseUnnecessaryBracesCodeReviewCheck();

        var success = check.TryFix(finding, null, out var message);

        Assert.That(success, Is.False);
        Assert.That(message, Is.EqualTo("A valid file path is required."));
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
        var check = new IfElseUnnecessaryBracesCodeReviewCheck();
        check.Analyze(context, report);
        return report;
    }
}
