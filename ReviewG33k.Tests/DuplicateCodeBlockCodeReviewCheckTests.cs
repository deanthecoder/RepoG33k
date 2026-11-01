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
public sealed class DuplicateCodeBlockCodeReviewCheckTests
{
    [Test]
    public void AnalyzeWhenAddedCodeBlockMatchesExistingFileReportsSuggestion()
    {
        using var tempRoot = new TempDirectory();
        var existingFile = tempRoot.GetFile("Services/ExistingHelper.cs");
        existingFile.Directory!.Create();
        existingFile.WriteAllText(
            """
            using System;

            public sealed class ExistingHelper
            {
                public void Run()
                {
                    // Existing logic
                    DoFirst();
                    DoSecond();
                    DoThird();
                    DoFourth();
                    DoFifth();
                    DoSixth();
                }
            }
            """);

        const string changedSource =
            """
            using System.Linq;

            public sealed class NewHelper
            {
                public void Run()
                {
                    DoFirst();
                    DoSecond();
                    DoThird();
                    DoFourth();
                    DoFifth();
                    DoSixth();
                }
            }
            """;
        var changedFile = CreateChangedFile(tempRoot, "Services/NewHelper.cs", changedSource, "A");

        var report = Analyze([changedFile]);

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.DuplicateCodeBlock));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Suggestion));
        Assert.That(report.Findings[0].Message, Does.Contain("ExistingHelper.cs"));
        Assert.That(report.Findings[0].Message, Does.Contain(":5"));
    }

    [Test]
    public void AnalyzeWhenBlockIsSmallerThanThresholdDoesNotReport()
    {
        using var tempRoot = new TempDirectory();
        var existingFile = tempRoot.GetFile("Services/ExistingHelper.cs");
        existingFile.Directory!.Create();
        existingFile.WriteAllText(
            """
            public sealed class ExistingHelper
            {
                public void Run()
                {
                    DoFirst();
                    DoSecond();
                    DoThird();
                    DoFourth();
                }
            }
            """);

        var changedFile = CreateChangedFile(
            tempRoot,
            "Services/NewHelper.cs",
            """
            public sealed class NewHelper
            {
                public void Run()
                {
                    DoFirst();
                    DoSecond();
                    DoThird();
                    DoFourth();
                }
            }
            """,
            "A");

        var report = Analyze([changedFile]);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenWholeRepositoryScanDoesNotRunCheck()
    {
        using var tempRoot = new TempDirectory();
        var existingFile = tempRoot.GetFile("Services/ExistingHelper.cs");
        existingFile.Directory!.Create();
        existingFile.WriteAllText(
            """
            public sealed class ExistingHelper
            {
                public void Run()
                {
                    DoFirst();
                    DoSecond();
                    DoThird();
                    DoFourth();
                    DoFifth();
                    DoSixth();
                }
            }
            """);

        var changedFile = CreateChangedFile(
            tempRoot,
            "Services/NewHelper.cs",
            """
            public sealed class NewHelper
            {
                public void Run()
                {
                    DoFirst();
                    DoSecond();
                    DoThird();
                    DoFourth();
                    DoFifth();
                    DoSixth();
                }
            }
            """,
            "M");

        var report = Analyze([changedFile], isEntireRepositoryScan: true);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenMarkupOnlyDuplicatesTrivialLayoutBoilerplateDoesNotReport()
    {
        using var tempRoot = new TempDirectory();
        var existingFile = tempRoot.GetFile("Views/ExistingView.axaml");
        existingFile.Directory!.Create();
        existingFile.WriteAllText(
            """
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>
            </Grid>
            """);

        var changedFile = CreateChangedFile(
            tempRoot,
            "Views/NewView.axaml",
            """
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                    <ColumnDefinition Width="*"/>
                </Grid.ColumnDefinitions>
            </Grid>
            """,
            "A");

        var report = Analyze([changedFile]);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenMarkupOnlyDuplicatesWindowAttributeBoilerplateDoesNotReport()
    {
        using var tempRoot = new TempDirectory();
        var existingFile = tempRoot.GetFile("Views/ExistingDialog.xaml");
        existingFile.Directory!.Create();
        existingFile.WriteAllText(
            """
            <Window
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                mc:Ignorable="d"
                TextElement.FontWeight="Regular"
                TextElement.FontSize="13"
                TextOptions.TextFormattingMode="Ideal"
                TextOptions.TextRenderingMode="Auto"
                UseLayoutRounding="True"
                WindowStartupLocation="CenterOwner"
                SizeToContent="Height"
                ResizeMode="NoResize">
            </Window>
            """);

        var changedFile = CreateChangedFile(
            tempRoot,
            "Views/NewDialog.xaml",
            """
            <Window
                xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                mc:Ignorable="d"
                TextElement.FontWeight="Regular"
                TextElement.FontSize="13"
                TextOptions.TextFormattingMode="Ideal"
                TextOptions.TextRenderingMode="Auto"
                UseLayoutRounding="True"
                WindowStartupLocation="CenterOwner"
                SizeToContent="Height"
                ResizeMode="NoResize">
            </Window>
            """,
            "A");

        var report = Analyze([changedFile]);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenMarkupDuplicatesSubstantiveUiBlockReportsSuggestion()
    {
        using var tempRoot = new TempDirectory();
        var existingFile = tempRoot.GetFile("Views/ExistingView.axaml");
        existingFile.Directory!.Create();
        existingFile.WriteAllText(
            """
            <StackPanel>
                <TextBlock Text="{Binding Title}" Classes="HeroTitle"/>
                <TextBlock Text="{Binding Subtitle}" Classes="HeroSubtitle"/>
                <Button Content="Run review" Command="{Binding RunCommand}"/>
                <TextBlock Text="{Binding Status}" Classes="StatusLine"/>
                <ProgressBar IsIndeterminate="{Binding IsBusy}"/>
                <TextBlock Text="{Binding Footer}" Classes="Footer"/>
            </StackPanel>
            """);

        var changedFile = CreateChangedFile(
            tempRoot,
            "Views/NewView.axaml",
            """
            <StackPanel>
                <TextBlock Text="{Binding Title}" Classes="HeroTitle"/>
                <TextBlock Text="{Binding Subtitle}" Classes="HeroSubtitle"/>
                <Button Content="Run review" Command="{Binding RunCommand}"/>
                <TextBlock Text="{Binding Status}" Classes="StatusLine"/>
                <ProgressBar IsIndeterminate="{Binding IsBusy}"/>
                <TextBlock Text="{Binding Footer}" Classes="Footer"/>
            </StackPanel>
            """,
            "A");

        var report = Analyze([changedFile]);

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Message, Does.Contain("ExistingView.axaml"));
    }

    [Test]
    public void AnalyzeWhenProjectFileDuplicatesExistingProjectFileDoesNotReport()
    {
        using var tempRoot = new TempDirectory();
        var existingFile = tempRoot.GetFile("Src/Existing.csproj");
        existingFile.Directory!.Create();
        existingFile.WriteAllText(
            """
            <Project Sdk="Microsoft.NET.Sdk">
                <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <Nullable>disable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <LangVersion>latest</LangVersion>
                    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                    <AssemblyName>Existing</AssemblyName>
                </PropertyGroup>
            </Project>
            """);

        var changedFile = CreateChangedFile(
            tempRoot,
            "Src/New.csproj",
            """
            <Project Sdk="Microsoft.NET.Sdk">
                <PropertyGroup>
                    <TargetFramework>net8.0</TargetFramework>
                    <Nullable>disable</Nullable>
                    <ImplicitUsings>enable</ImplicitUsings>
                    <LangVersion>latest</LangVersion>
                    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
                    <AssemblyName>Existing</AssemblyName>
                </PropertyGroup>
            </Project>
            """,
            "A");

        var report = Analyze([changedFile]);

        Assert.That(report.Findings, Is.Empty);
    }

    private static CodeReviewChangedFile CreateChangedFile(DirectoryInfo root, string relativePath, string source, string status)
    {
        var normalizedSource = (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalizedSource.Split('\n');
        var file = root.GetFile(relativePath);
        file.Directory!.Create();
        file.WriteAllText(normalizedSource);

        return new CodeReviewChangedFile(
            status,
            relativePath.Replace('\\', '/'),
            file.FullName,
            normalizedSource,
            lines,
            new HashSet<int>(Enumerable.Range(1, lines.Length)));
    }

    private static CodeSmellReport Analyze(IReadOnlyList<CodeReviewChangedFile> files, bool isEntireRepositoryScan = false)
    {
        var context = new CodeReviewAnalysisContext(
            files,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            allChangedFiles: files,
            isEntireRepositoryScan: isEntireRepositoryScan);
        var report = new CodeSmellReport();
        var check = new DuplicateCodeBlockCodeReviewCheck();
        check.Analyze(context, report);
        return report;
    }
}
