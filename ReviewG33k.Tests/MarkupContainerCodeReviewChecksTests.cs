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
public sealed class MarkupContainerCodeReviewChecksTests
{
    [Test]
    public void FixedSizeLayoutContainerCheckWhenGridHasWidthReportsSuggestion()
    {
        const string source = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Grid Width="640">
                    <TextBlock Text="Title" />
                </Grid>
            </UserControl>
            """;

        var report = Analyze(new FixedSizeLayoutContainerCodeReviewCheck(), "A", source, Enumerable.Range(1, 7));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Suggestion));
        Assert.That(report.Findings[0].Message, Does.Contain("fixed `Width`"));
    }

    [Test]
    public void FixedSizeLayoutContainerCheckWhenStackPanelHasHeightReportsSuggestion()
    {
        const string source = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <StackPanel Height="120">
                    <TextBlock Text="Title" />
                </StackPanel>
            </UserControl>
            """;

        var report = Analyze(new FixedSizeLayoutContainerCodeReviewCheck(), "A", source, Enumerable.Range(1, 7));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Suggestion));
        Assert.That(report.Findings[0].Message, Does.Contain("fixed `Height`"));
    }

    [Test]
    public void FixedSizeLayoutContainerCheckWhenOnlyMinOrMaxSizeIsSetDoesNotReport()
    {
        const string source = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Grid MinWidth="320" MaxHeight="500">
                    <TextBlock Text="Title" />
                </Grid>
            </UserControl>
            """;

        var report = Analyze(new FixedSizeLayoutContainerCodeReviewCheck(), "A", source, Enumerable.Range(1, 7));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void FixedSizeLayoutContainerCheckWhenContainerHasLoadedEventDoesNotReport()
    {
        const string source = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Grid Width="640" Loaded="OnGridLoaded">
                    <TextBlock Text="Title" />
                </Grid>
            </UserControl>
            """;

        var report = Analyze(new FixedSizeLayoutContainerCodeReviewCheck(), "A", source, Enumerable.Range(1, 7));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void FixedSizeLayoutContainerCheckWhenWidthLineIsNotAddedInModifiedFileDoesNotReport()
    {
        const string source = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Grid Width="640">
                    <TextBlock Text="Title" />
                </Grid>
            </UserControl>
            """;

        var report = Analyze(new FixedSizeLayoutContainerCodeReviewCheck(), "M", source, [1, 2]);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void SingleChildWrapperCheckWhenGridHasSingleChildReportsHint()
    {
        const string source = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Grid>
                    <TextBlock Text="Title" />
                </Grid>
            </UserControl>
            """;

        var report = Analyze(new SingleChildWrapperContainerCodeReviewCheck(), "A", source, Enumerable.Range(1, 7));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
        Assert.That(report.Findings[0].Message, Does.Contain("only one child"));
    }

    [Test]
    public void SingleChildWrapperCheckWhenGridHasTwoChildrenDoesNotReport()
    {
        const string source = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Grid>
                    <TextBlock Text="Title" />
                    <TextBlock Text="Subtitle" />
                </Grid>
            </UserControl>
            """;

        var report = Analyze(new SingleChildWrapperContainerCodeReviewCheck(), "A", source, Enumerable.Range(1, 8));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void SingleChildWrapperCheckWhenGridHasXNameDoesNotReport()
    {
        const string source = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Grid x:Name="RootGrid">
                    <TextBlock Text="Title" />
                </Grid>
            </UserControl>
            """;

        var report = Analyze(new SingleChildWrapperContainerCodeReviewCheck(), "A", source, Enumerable.Range(1, 7));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void SingleChildWrapperCheckWhenGridHasLoadedEventDoesNotReport()
    {
        const string source = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Grid Loaded="OnGridLoaded">
                    <TextBlock Text="Title" />
                </Grid>
            </UserControl>
            """;

        var report = Analyze(new SingleChildWrapperContainerCodeReviewCheck(), "A", source, Enumerable.Range(1, 7));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void NestedSamePanelWrapperCheckWhenStackPanelsHaveSameOrientationReportsHint()
    {
        const string source = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <StackPanel>
                    <StackPanel>
                        <TextBlock Text="Title" />
                    </StackPanel>
                </StackPanel>
            </UserControl>
            """;

        var report = Analyze(new NestedSamePanelWrapperCodeReviewCheck(), "A", source, Enumerable.Range(1, 9));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
        Assert.That(report.Findings[0].Message, Does.Contain("Consider flattening"));
    }

    [Test]
    public void NestedSamePanelWrapperCheckWhenOuterHasLoadedEventDoesNotReport()
    {
        const string source = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <StackPanel Loaded="OnLoaded">
                    <StackPanel>
                        <TextBlock Text="Title" />
                    </StackPanel>
                </StackPanel>
            </UserControl>
            """;

        var report = Analyze(new NestedSamePanelWrapperCodeReviewCheck(), "A", source, Enumerable.Range(1, 9));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void NestedSamePanelWrapperCheckWhenOuterStackPanelHasMarginDoesNotReport()
    {
        const string source = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <StackPanel Margin="6">
                    <StackPanel>
                        <TextBlock Text="Title" />
                    </StackPanel>
                </StackPanel>
            </UserControl>
            """;

        var report = Analyze(new NestedSamePanelWrapperCodeReviewCheck(), "A", source, Enumerable.Range(1, 9));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void EmptyContainerCheckWhenGridHasNoChildrenReportsHint()
    {
        const string source = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Grid />
            </UserControl>
            """;

        var report = Analyze(new EmptyContainerCodeReviewCheck(), "A", source, Enumerable.Range(1, 5));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
        Assert.That(report.Findings[0].Message, Does.Contain("no child elements"));
    }

    [Test]
    public void EmptyContainerCheckWhenGridHasXNameDoesNotReport()
    {
        const string source = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Grid x:Name="LayoutRoot" />
            </UserControl>
            """;

        var report = Analyze(new EmptyContainerCodeReviewCheck(), "A", source, Enumerable.Range(1, 5));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void EmptyContainerCheckWhenStackPanelHasChildrenDoesNotReport()
    {
        const string source = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <StackPanel>
                    <TextBlock Text="Title" />
                </StackPanel>
            </UserControl>
            """;

        var report = Analyze(new EmptyContainerCodeReviewCheck(), "A", source, Enumerable.Range(1, 7));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void EmptyContainerCheckWhenContainerLineWasNotAddedInModifiedFileDoesNotReport()
    {
        const string source = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Grid />
            </UserControl>
            """;

        var report = Analyze(new EmptyContainerCodeReviewCheck(), "M", source, [1]);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void SingleChildWrapperCheckWhenBorderHasSingleChildDoesNotReport()
    {
        const string source = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Border>
                    <TextBlock Text="Title" />
                </Border>
            </UserControl>
            """;

        var report = Analyze(new SingleChildWrapperContainerCodeReviewCheck(), "A", source, Enumerable.Range(1, 7));

        Assert.That(report.Findings, Is.Empty);
    }

    private static CodeSmellReport Analyze(ICodeReviewCheck check, string status, string source, IEnumerable<int> addedLines)
    {
        var normalizedSource = (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalizedSource.Split('\n');
        var changedFile = new CodeReviewChangedFile(
            status,
            "Views/SampleView.axaml",
            "Views/SampleView.axaml",
            normalizedSource,
            lines,
            new HashSet<int>(addedLines ?? []));
        var context = new CodeReviewAnalysisContext(
            [changedFile],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var report = new CodeSmellReport();
        check.Analyze(context, report);
        return report;
    }
}
