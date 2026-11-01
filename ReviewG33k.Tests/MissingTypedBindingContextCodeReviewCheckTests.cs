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
public sealed class MissingTypedBindingContextCodeReviewCheckTests
{
    [Test]
    public void AnalyzeWhenAddedMarkupHasBindingAndNoTypedContextReportsHint()
    {
        const string source = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <TextBlock Text="{Binding Title}" />
            </UserControl>
            """;

        var report = Analyze("A", "Views/SampleView.axaml", source, Enumerable.Range(1, 4));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
        Assert.That(report.Findings[0].Message, Does.Contain("x:DataType"));
    }

    [Test]
    public void AnalyzeWhenMarkupHasBindingAndTypedContextDoesNotReport()
    {
        const string source = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                         x:DataType="vm:SampleViewModel">
                <TextBlock Text="{Binding Title}" />
            </UserControl>
            """;

        var report = Analyze("A", "Views/SampleView.axaml", source, Enumerable.Range(1, 5));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenMarkupHasRelativeSourceBindingWithoutTypedContextDoesNotReport()
    {
        const string source = """
            <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Viewbox x:Key="Add">
                    <Path Fill="{Binding Path=(TextElement.Foreground), RelativeSource={RelativeSource AncestorType=FrameworkElement}}" />
                </Viewbox>
            </ResourceDictionary>
            """;

        var report = Analyze("A", "Resources/Icons.xaml", source, Enumerable.Range(1, 6));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenMarkupHasElementNameBindingWithoutTypedContextDoesNotReport()
    {
        const string source = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <TextBlock x:Name="SourceText" Text="Value" />
                <TextBlock Text="{Binding Text, ElementName=SourceText}" />
            </UserControl>
            """;

        var report = Analyze("A", "Views/SampleView.axaml", source, Enumerable.Range(1, 5));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenMarkupHasMixedBindingsWithoutTypedContextReportsHint()
    {
        const string source = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <TextBlock Text="{Binding Text, ElementName=SourceText}" />
                <TextBlock Text="{Binding Title}" />
            </UserControl>
            """;

        var report = Analyze("A", "Views/SampleView.axaml", source, Enumerable.Range(1, 5));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
    }

    [Test]
    public void AnalyzeWhenModifiedMarkupHasNoAddedBindingLinesDoesNotReport()
    {
        const string source = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <TextBlock Text="{Binding Title}" />
            </UserControl>
            """;

        var report = Analyze("M", "Views/SampleView.axaml", source, [1]);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenModifiedMarkupAddsBindingLineReportsHint()
    {
        const string source = """
            <UserControl xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <TextBlock Text="{Binding Existing}" />
                <TextBlock Text="{Binding Added}" />
            </UserControl>
            """;

        var report = Analyze("M", "Views/SampleView.axaml", source, [4]);

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].LineNumber, Is.EqualTo(4));
    }

    private static CodeSmellReport Analyze(string status, string path, string source, IEnumerable<int> addedLines)
    {
        var normalizedSource = (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalizedSource.Split('\n');
        var changedFile = new CodeReviewChangedFile(
            status,
            path,
            path,
            normalizedSource,
            lines,
            new HashSet<int>(addedLines ?? []));
        var context = new CodeReviewAnalysisContext(
            [changedFile],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var report = new CodeSmellReport();
        var check = new MissingTypedBindingContextCodeReviewCheck();
        check.Analyze(context, report);
        return report;
    }
}
