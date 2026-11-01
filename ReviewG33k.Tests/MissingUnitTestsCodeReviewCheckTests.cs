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

public sealed class MissingUnitTestsCodeReviewCheckTests
{
    [Test]
    public void AnalyzeWhenFileLooksLikeUiCodeDoesNotReportMissingUnitTests()
    {
        const string uiSource = """
            using Avalonia.Controls;
            using Avalonia.VisualTree;

            public sealed class DialogHostCloseHelper
            {
                public bool Close(DialogHost dialogHost) => dialogHost != null;
            }
            """;

        var report = AnalyzeAddedFile(
            path: "Packages/CSharp.Avalonia/DialogHostCloseHelper.cs",
            source: uiSource,
            hasAnyAddedTestFiles: false);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenNonUiTypeHasNoTestsReportsSuggestion()
    {
        const string nonUiSource = """
            public sealed class FooService
            {
                public int Add(int a, int b) => a + b;
            }
            """;

        var report = AnalyzeAddedFile(
            path: "Packages/CSharp.Core/FooService.cs",
            source: nonUiSource,
            hasAnyAddedTestFiles: false);

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Suggestion));
    }

    [Test]
    public void AnalyzeWhenAddedProgramTypeDoesNotReportMissingUnitTests()
    {
        const string source = """
            public static class Program
            {
            }
            """;

        var report = AnalyzeAddedFile(
            path: "Program.cs",
            source: source,
            hasAnyAddedTestFiles: false);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenFileIsCodeBehindDoesNotReportMissingUnitTests()
    {
        const string source = """
            public sealed class ReviewResultsWindow
            {
                public void Refresh()
                {
                }
            }
            """;

        var report = AnalyzeAddedFile(
            path: "Views/ReviewResultsWindow.axaml.cs",
            source: source,
            hasAnyAddedTestFiles: false);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenFileHasTextFixtureAttributeDoesNotReportMissingUnitTests()
    {
        const string source = """
            [TextFixture]
            public class SmartRipOpcDocumentation
            {
            }
            """;

        var report = AnalyzeAddedFile(
            path: "Packages/CSharp.Core/SmartRipOpcDocumentation.cs",
            source: source,
            hasAnyAddedTestFiles: false);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenMatchingExistingTestFileWasModifiedDoesNotReportMissingUnitTests()
    {
        const string source = """
            public sealed class PrinterInterfaceLock
            {
            }
            """;

        var report = AnalyzeAddedFile(
            path: "SPC/MeteorOpc/PrinterInterfaceLock.cs",
            source: source,
            addedTestFileNames: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            changedTestFileNames: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PrinterInterfaceLockTests.cs" });

        Assert.That(report.Findings, Is.Empty);
    }

    private static CodeSmellReport AnalyzeAddedFile(
        string path,
        string source,
        bool hasAnyAddedTestFiles)
    {
        var addedTestFileNames = hasAnyAddedTestFiles
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "UnrelatedTests.cs" }
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return AnalyzeAddedFile(path, source, addedTestFileNames, addedTestFileNames);
    }

    private static CodeSmellReport AnalyzeAddedFile(
        string path,
        string source,
        IReadOnlySet<string> addedTestFileNames,
        IReadOnlySet<string> changedTestFileNames)
    {
        var normalizedSource = (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalizedSource.Split('\n');
        var changedFile = new CodeReviewChangedFile(
            "A",
            path,
            path,
            normalizedSource,
            lines,
            new HashSet<int>(Enumerable.Range(1, lines.Length)));
        var context = new CodeReviewAnalysisContext([changedFile], addedTestFileNames, changedTestFileNames);

        var report = new CodeSmellReport();
        var check = new MissingUnitTestsCodeReviewCheck();
        check.Analyze(context, report);
        return report;
    }
}
