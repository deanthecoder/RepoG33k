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
public sealed class NumericStringCultureForFileWriteCodeReviewCheckTests
{
    [Test]
    public void AnalyzeWhenDoubleToStringIsWrittenViaFileWriteAllTextReports()
    {
        const string source = """
            using System.IO;

            public sealed class Sample
            {
                public void Run(double amount)
                {
                    File.WriteAllText("output.txt", amount.ToString());
                }
            }
            """;

        var report = AnalyzeSource(source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.NumericStringCultureForFileWrite));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Important));
    }

    [Test]
    public void AnalyzeWhenDoubleToStringUsesInvariantCultureDoesNotReport()
    {
        const string source = """
            using System.Globalization;
            using System.IO;

            public sealed class Sample
            {
                public void Run(double amount)
                {
                    File.WriteAllText("output.txt", amount.ToString(CultureInfo.InvariantCulture));
                }
            }
            """;

        var report = AnalyzeSource(source, Enumerable.Range(1, 11));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenFloatToStringIsWrittenViaFileInfoExtensionReports()
    {
        const string source = """
            using DTC.Core.Extensions;
            using System.IO;

            public sealed class Sample
            {
                public void Run(float ratio)
                {
                    var file = new FileInfo("output.txt");
                    file.WriteAllText(ratio.ToString("F3"));
                }
            }
            """;

        var report = AnalyzeSource(source, Enumerable.Range(1, 12));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.NumericStringCultureForFileWrite));
        Assert.That(report.Findings[0].Message, Does.Contain("culture-sensitive formatting"));
    }

    [Test]
    public void AnalyzeWhenNumericToStringIsNotWrittenToFileDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public string Run(decimal amount)
                {
                    return amount.ToString();
                }
            }
            """;

        var report = AnalyzeSource(source, Enumerable.Range(1, 8));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenCultureSensitiveWriteIsOutsideAddedLinesDoesNotReport()
    {
        const string source = """
            using System.IO;

            public sealed class Sample
            {
                public void Run(double amount)
                {
                    File.WriteAllText("output.txt", amount.ToString());
                }
            }
            """;

        var report = AnalyzeSource(source, [1, 2], status: "M");

        Assert.That(report.Findings, Is.Empty);
    }

    private static CodeSmellReport AnalyzeSource(string source, IEnumerable<int> addedLineNumbers, string status = "A")
    {
        var normalizedSource = (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalizedSource.Split('\n');
        var changedFile = new CodeReviewChangedFile(
            status,
            "Services/Sample.cs",
            "Services/Sample.cs",
            normalizedSource,
            lines,
            new HashSet<int>(addedLineNumbers ?? []));
        var context = new CodeReviewAnalysisContext(
            [changedFile],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var report = new CodeSmellReport();
        var check = new NumericStringCultureForFileWriteCodeReviewCheck();
        check.Analyze(context, report);
        return report;
    }
}
