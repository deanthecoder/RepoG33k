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

public sealed class PublicMethodArgumentGuardsCodeReviewCheckTests
{
    [Test]
    public void AnalyzeWhenUsingCombinedNullCheckDoesNotReportValueOrParameter()
    {
        const string source = """
            using System;
            using System.Globalization;

            public sealed class EnumToBoolConverter
            {
                public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
                {
                    if (value == null || parameter == null)
                        return false;

                    if (value is not Enum enumValue || parameter is not string enumString)
                        return false;

                    return enumValue.ToString().Equals(enumString, StringComparison.InvariantCultureIgnoreCase);
                }
            }
            """;

        var report = AnalyzeSource(source);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenUsedParameterHasNoGuardReportsHint()
    {
        const string source = """
            public sealed class Sample
            {
                public void Convert(object value, object parameter)
                {
                    if (value == null)
                        return;

                    _ = parameter.ToString();
                }
            }
            """;

        var report = AnalyzeSource(source);

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
        Assert.That(report.Findings[0].Message, Does.Contain("parameter"));
    }

    [Test]
    public void AnalyzeWhenOutParameterIsPresentDoesNotRequireNullGuardForOutParameter()
    {
        const string source = """
            public sealed class Sample
            {
                public void Convert(object value, out string resultMessage)
                {
                    if (value == null)
                    {
                        resultMessage = string.Empty;
                        return;
                    }

                    resultMessage = value.ToString();
                }
            }
            """;

        var report = AnalyzeSource(source);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenStringIsNullOrWhiteSpaceGuardExistsAfterSeveralLinesDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public void Save(string resolvedFilePath)
                {
                    var a = 1;
                    var b = 2;
                    var c = 3;
                    var d = 4;
                    var e = 5;
                    var f = 6;
                    var g = 7;
                    var h = 8;
                    var i = 9;
                    var j = 10;
                    var k = 11;
                    if (string.IsNullOrWhiteSpace(resolvedFilePath))
                        return;

                    _ = resolvedFilePath.Length;
                }
            }
            """;

        var report = AnalyzeSource(source);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenStringIsNullOrEmptyGuardExistsAfterSeveralLinesDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public void Save(string resolvedFilePath)
                {
                    var a = 1;
                    var b = 2;
                    var c = 3;
                    var d = 4;
                    var e = 5;
                    var f = 6;
                    var g = 7;
                    var h = 8;
                    var i = 9;
                    var j = 10;
                    var k = 11;
                    if (string.IsNullOrEmpty(resolvedFilePath))
                        return;

                    _ = resolvedFilePath.Length;
                }
            }
            """;

        var report = AnalyzeSource(source);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenOptionalParametersAreOnlyUsedWithNullConditionalDoesNotReport()
    {
        const string source = """
            using System;

            public sealed class Sample
            {
                public void Run(Action<string> progressLogger = null, Action<int, int, string> progressReporter = null)
                {
                    progressLogger?.Invoke("hello");
                    progressReporter?.Invoke(1, 2, "done");
                }
            }
            """;

        var report = AnalyzeSource(source);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenParameterIsOnlyForwardedDoesNotReport()
    {
        const string source = """
            public sealed class Worker
            {
                public void Save(object value)
                {
                    Store(value);
                }

                private static void Store(object value)
                {
                }
            }
            """;

        var report = AnalyzeSource(source);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenParameterIsOnlyUsedWithNullConditionalAndNullCoalescingDoesNotReport()
    {
        const string source = """
            using System.Linq;

            public sealed class SourceResult
            {
                public string[] InfoMessages { get; set; }

                public string[] Files { get; set; }
            }

            public sealed class Sample
            {
                public int Run(SourceResult sourceResult)
                {
                    var infoCount = sourceResult?.InfoMessages?.Length ?? 0;
                    var fileCount = sourceResult?.Files?.Where(file => file != null).Count() ?? 0;
                    return infoCount + fileCount;
                }
            }
            """;

        var report = AnalyzeSource(source);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenExtensionMethodUsesReceiverDoesNotRequireGuardForReceiver()
    {
        const string source = """
            public sealed class Sample
            {
                public int Value { get; set; }
            }

            public static class SampleExtensions
            {
                public static int ReadValue(this Sample sample, object logger)
                {
                    if (logger == null)
                        return 0;

                    return sample.Value;
                }
            }
            """;

        var report = AnalyzeSource(source);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenMethodIsStaticProgramMainDoesNotRequireArgumentGuard()
    {
        const string source = """
            public static class Program
            {
                public static void Main(string[] args)
                {
                    foreach (var arg in args)
                        _ = arg.Length;
                }
            }
            """;

        var report = AnalyzeSource(source);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenResolvedStructParametersAreUsedDoesNotRequireNullGuards()
    {
        const string source = """
            public struct HeadStatusData
            {
                public int HeadState { get; set; }
            }

            public static class MeteorHeadStatusUtilities
            {
                public static int GetChangedPropertyNames(HeadStatusData previousStatus, HeadStatusData currentStatus, object logger)
                {
                    _ = previousStatus.HeadState;
                    _ = currentStatus.HeadState;
                    _ = logger.ToString();
                    return 0;
                }
            }
            """;

        var report = AnalyzeSource(source);

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Message, Does.Contain("logger"));
        Assert.That(report.Findings[0].Message, Does.Not.Contain("previousStatus"));
        Assert.That(report.Findings[0].Message, Does.Not.Contain("currentStatus"));
    }

    private static CodeSmellReport AnalyzeSource(string source)
    {
        var normalizedSource = (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalizedSource.Split('\n');
        var addedLineNumbers = new HashSet<int>(Enumerable.Range(1, lines.Length));
        var changedFile = new CodeReviewChangedFile(
            "M",
            "Packages/CSharp.Avalonia/Converters/EnumToBoolConverter.cs",
            "Packages/CSharp.Avalonia/Converters/EnumToBoolConverter.cs",
            normalizedSource,
            lines,
            addedLineNumbers);
        var context = new CodeReviewAnalysisContext(
            [changedFile],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var report = new CodeSmellReport();
        var check = new PublicMethodArgumentGuardsCodeReviewCheck();
        check.Analyze(context, report);
        return report;
    }
}
