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
using DTC.Core;
using DTC.Core.Extensions;

namespace ReviewG33k.Tests;

public sealed class MissingTestsForNewPublicPropertiesCodeReviewCheckTests
{
    [Test]
    public void AnalyzeWhenNewPublicPropertyAddedAndNoTestsChangedReportsSuggestion()
    {
        const string source = """
            public sealed class OrderService
            {
                public string CurrentOrderId { get; set; }
            }
            """;

        var report = Analyze(source);

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.MissingTestsForPublicProperties));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Suggestion));
        Assert.That(report.Findings[0].Message, Does.Contain("CurrentOrderId"));
    }

    [Test]
    public void AnalyzeWhenMatchingTestFileChangedDoesNotReport()
    {
        const string source = """
            public sealed class OrderService
            {
                public string CurrentOrderId { get; set; }
            }
            """;

        const string testSource = """
            public sealed class OrderServiceTests
            {
                public void CurrentOrderId_WhenSet_ReturnsValue()
                {
                }
            }
            """;

        var report = Analyze(source, ("Tests/OrderServiceTests.cs", "M", testSource));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenUnrelatedTestFileChangedReportsHint()
    {
        const string source = """
            public sealed class OrderService
            {
                public string CurrentOrderId { get; set; }
            }
            """;

        const string testSource = """
            public sealed class CustomerServiceTests
            {
                public void Lookup_WhenCalled_ReturnsValue()
                {
                }
            }
            """;

        var report = Analyze(source, ("Tests/CustomerServiceTests.cs", "M", testSource));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
        Assert.That(report.Findings[0].Message, Does.Contain("CurrentOrderId"));
        Assert.That(report.Findings[0].Message, Does.Contain("likely matching unit test(s)"));
    }

    [Test]
    public void AnalyzeWhenPropertyIsPrivateDoesNotReport()
    {
        const string source = """
            public sealed class OrderService
            {
                private string CurrentOrderId { get; set; }
            }
            """;

        var report = Analyze(source);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenPropertyIsOnInterfaceDoesNotReport()
    {
        const string source = """
            public interface IOrderService
            {
                string CurrentOrderId { get; }
            }
            """;

        var report = Analyze(source);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenPropertyIsOnProgramDoesNotReport()
    {
        const string source = """
            public static class Program
            {
                public static string ApplicationName => "ReviewG33k";
            }
            """;

        var report = AnalyzeWithProductionPath(source, "Program.cs");

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenFileIsCodeBehindDoesNotReport()
    {
        const string source = """
            public sealed class ReviewResultsWindow
            {
                public string Title { get; set; }
            }
            """;

        var report = AnalyzeWithProductionPath(source, "Views/ReviewResultsWindow.xaml.cs");

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenPropertyIsInTestFixtureTypeDoesNotReport()
    {
        const string source = """
            [TestFixture]
            public sealed class SmartRipOpcDocumentation
            {
                public string OutputPath { get; set; }
            }
            """;

        var report = AnalyzeWithProductionPath(source, "Services/SmartRipOpcDocumentation.cs");

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenExistingRepositoryTestMentionsPropertyDoesNotReport()
    {
        using var tempRoot = new TempDirectory();
        var productionFile = tempRoot.GetFile("SPC/MeteorOpc/MeteorInterface.cs");
        productionFile.Directory!.Create();
        productionFile.WriteAllText(
            """
            public sealed class MeteorInterface
            {
                public string ControllerId { get; set; }
            }
            """);

        var testFile = tempRoot.GetFile("SPC/CSharp.UnitTests/MeteorOpc/MeteorInterfaceTests.cs");
        testFile.Directory!.Create();
        testFile.WriteAllText(
            """
            public sealed class MeteorInterfaceTests
            {
                public void ControllerId_WhenSet_ReturnsValue()
                {
                }
            }
            """);

        var changedFile = CreateChangedFile(
            relativePath: "SPC/MeteorOpc/MeteorInterface.cs",
            fullPath: productionFile.FullName,
            status: "A",
            source: productionFile.ReadAllText());
        var context = new CodeReviewAnalysisContext(
            [changedFile],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var report = new CodeSmellReport();
        var check = new MissingTestsForNewPublicPropertiesCodeReviewCheck();
        check.Analyze(context, report);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenExistingRepositoryTestDoesNotMentionPropertyStillReports()
    {
        using var tempRoot = new TempDirectory();
        var productionFile = tempRoot.GetFile("SPC/MeteorOpc/MeteorInterface.cs");
        productionFile.Directory!.Create();
        productionFile.WriteAllText(
            """
            public sealed class MeteorInterface
            {
                public string ControllerId { get; set; }
            }
            """);

        var testFile = tempRoot.GetFile("SPC/CSharp.UnitTests/MeteorOpc/MeteorInterfaceTests.cs");
        testFile.Directory!.Create();
        testFile.WriteAllText(
            """
            public sealed class MeteorInterfaceTests
            {
                public void GivenSomethingElse()
                {
                }
            }
            """);

        var changedFile = CreateChangedFile(
            relativePath: "SPC/MeteorOpc/MeteorInterface.cs",
            fullPath: productionFile.FullName,
            status: "A",
            source: productionFile.ReadAllText());
        var context = new CodeReviewAnalysisContext(
            [changedFile],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var report = new CodeSmellReport();
        var check = new MissingTestsForNewPublicPropertiesCodeReviewCheck();
        check.Analyze(context, report);

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Message, Does.Contain("ControllerId"));
    }

    [Test]
    public void AnalyzeWhenNestedPublicTypePropertyIsCoveredByOuterTypeTestsDoesNotReport()
    {
        using var tempRoot = new TempDirectory();
        var productionFile = tempRoot.GetFile("SPC/MeteorOpc/MeteorInterface.cs");
        productionFile.Directory!.Create();
        productionFile.WriteAllText(
            """
            public sealed class MeteorInterface
            {
                public struct ControllerConfiguration
                {
                    public string ControllerId { get; set; }
                }
            }
            """);

        var testFile = tempRoot.GetFile("SPC/CSharp.UnitTests/MeteorOpc/MeteorInterfaceTests.cs");
        testFile.Directory!.Create();
        testFile.WriteAllText(
            """
            public sealed class MeteorInterfaceTests
            {
                public void ControllerId_WhenSet_ReturnsValue()
                {
                }
            }
            """);

        var changedFile = CreateChangedFile(
            relativePath: "SPC/MeteorOpc/MeteorInterface.cs",
            fullPath: productionFile.FullName,
            status: "A",
            source: productionFile.ReadAllText());
        var context = new CodeReviewAnalysisContext(
            [changedFile],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var report = new CodeSmellReport();
        var check = new MissingTestsForNewPublicPropertiesCodeReviewCheck();
        check.Analyze(context, report);

        Assert.That(report.Findings, Is.Empty);
    }

    private static CodeSmellReport Analyze(string productionSource, params (string Path, string Status, string Source)[] additionalFiles) =>
        AnalyzeWithProductionPath(productionSource, "Services/OrderService.cs", additionalFiles);

    private static CodeSmellReport AnalyzeWithProductionPath(
        string productionSource,
        string productionPath,
        params (string Path, string Status, string Source)[] additionalFiles)
    {
        var files = new List<CodeReviewChangedFile>
        {
            CreateChangedFile(productionPath, "A", productionSource)
        };

        foreach (var (path, status, source) in additionalFiles ?? [])
            files.Add(CreateChangedFile(path, status, source));

        var addedTestFilesByName = new HashSet<string>(
            files.Where(file => file.IsAdded && file.Path.EndsWith("Tests.cs", StringComparison.OrdinalIgnoreCase))
                .Select(file => Path.GetFileName(file.Path)),
            StringComparer.OrdinalIgnoreCase);

        var context = new CodeReviewAnalysisContext(files, addedTestFilesByName);

        var report = new CodeSmellReport();
        var check = new MissingTestsForNewPublicPropertiesCodeReviewCheck();
        check.Analyze(context, report);
        return report;
    }

    private static CodeReviewChangedFile CreateChangedFile(string path, string status, string source)
    {
        var normalizedSource = (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalizedSource.Split('\n');
        return new CodeReviewChangedFile(
            status,
            path,
            path,
            normalizedSource,
            lines,
            new HashSet<int>(Enumerable.Range(1, lines.Length)));
    }

    private static CodeReviewChangedFile CreateChangedFile(string relativePath, string fullPath, string status, string source)
    {
        var normalizedSource = (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalizedSource.Split('\n');
        return new CodeReviewChangedFile(
            status,
            relativePath,
            fullPath,
            normalizedSource,
            lines,
            new HashSet<int>(Enumerable.Range(1, lines.Length)));
    }
}
