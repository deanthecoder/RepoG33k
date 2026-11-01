// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Core;
using DTC.Core.Extensions;
using ReviewG33k.Services;
using ReviewG33k.Services.Checks;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class LambdaCanBeMethodGroupCodeReviewCheckTests
{
    [Test]
    public void AnalyzeWhenParameterlessLambdaOnlyInvokesMethodReportsHint()
    {
        const string source = """
            using System;

            public sealed class Sample
            {
                public void Run()
                {
                    Func<Guid> factory = () => Guid.NewGuid();
                }
            }
            """;

        var report = AnalyzeSource(source);

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.LambdaCanBeMethodGroup));
        Assert.That(report.Findings[0].Message, Does.Contain("Guid.NewGuid"));
    }

    [Test]
    public void AnalyzeWhenLambdaOnlyPassesThroughParameterReportsHint()
    {
        const string source = """
            public sealed class Sample
            {
                public void Run()
                {
                    System.Func<string, string> formatter = value => Normalize(value);
                }

                private static string Normalize(string value) => value?.Trim();
            }
            """;

        var report = AnalyzeSource(source);

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Message, Does.Contain("Normalize"));
    }

    [Test]
    public void AnalyzeWhenInvocationUsesLambdaParameterAsReceiverDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public void Run()
                {
                    System.Func<string, string> formatter = value => value.Trim();
                }
            }
            """;

        var report = AnalyzeSource(source);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenLambdaDiscardsMethodReturnValueDoesNotReport()
    {
        const string source = """
            using System;
            using System.Diagnostics;

            public sealed class Sample
            {
                internal static Action<ProcessStartInfo> ProcessStarter { get; set; } =
                    processStartInfo => Process.Start(processStartInfo);
            }
            """;

        var report = AnalyzeSource(source);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenInvocationIsOutsideAddedLinesDoesNotReport()
    {
        const string source = """
            using System;

            public sealed class Sample
            {
                public void Run()
                {
                    Func<Guid> factory = () => Guid.NewGuid();
                }
            }
            """;

        var report = AnalyzeSource(source, [1, 2], status: "M");

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void TryFixWhenLambdaCanBeMethodGroupRewritesInvocation()
    {
        using var tempFile = new TempFile(".cs");
        const string source = """
            using System;

            public sealed class Sample
            {
                public void Run()
                {
                    Func<Guid> factory = () => Guid.NewGuid();
                }
            }
            """;

        tempFile.WriteAllText(source);

        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Hint,
            CodeReviewRuleIds.LambdaCanBeMethodGroup,
            "Sample.cs",
            7,
            "Lambda can be simplified.");

        var check = new LambdaCanBeMethodGroupCodeReviewCheck();
        var success = check.TryFix(finding, tempFile, out var message);

        Assert.That(success, Is.True);
        Assert.That(message, Does.Contain("Guid.NewGuid"));
        Assert.That(tempFile.ReadAllText(), Does.Contain("Func<Guid> factory = Guid.NewGuid;"));
    }

    private static CodeSmellReport AnalyzeSource(string source, IEnumerable<int> addedLineNumbers = null, string status = "A")
    {
        var normalizedSource = (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalizedSource.Split('\n');
        var changedFile = new CodeReviewChangedFile(
            status,
            "Services/Sample.cs",
            "Services/Sample.cs",
            normalizedSource,
            lines,
            new HashSet<int>(addedLineNumbers ?? Enumerable.Range(1, lines.Length)));
        var context = new CodeReviewAnalysisContext([changedFile], new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var report = new CodeSmellReport();
        var check = new LambdaCanBeMethodGroupCodeReviewCheck();
        check.Analyze(context, report);
        return report;
    }
}
