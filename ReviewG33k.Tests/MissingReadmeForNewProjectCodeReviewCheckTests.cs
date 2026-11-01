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

public sealed class MissingReadmeForNewProjectCodeReviewCheckTests
{
    [Test]
    public void AnalyzeWhenAddedProjectHasNoReadmeReportsHint()
    {
        using var tempRoot = new TempDirectory();
        var projectDirectory = tempRoot.GetDir("ProjectA");
        projectDirectory.Create();
        var projectFile = projectDirectory.GetFile("ProjectA.csproj").WriteAllText("<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var report = AnalyzeSingleFile("A", "ProjectA/ProjectA.csproj", projectFile);

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.MissingReadmeForNewProject));
        Assert.That(report.Findings[0].Message, Does.Contain("README.md"));
    }

    [Test]
    public void AnalyzeWhenAddedProjectFolderAlreadyHasReadmeDoesNotReport()
    {
        using var tempRoot = new TempDirectory();
        var projectDirectory = tempRoot.GetDir("ProjectB");
        projectDirectory.Create();
        var projectFile = projectDirectory.GetFile("ProjectB.csproj").WriteAllText("<Project Sdk=\"Microsoft.NET.Sdk\" />");
        projectDirectory.GetFile("README.md").WriteAllText("# Project B");

        var report = AnalyzeSingleFile("A", "ProjectB/ProjectB.csproj", projectFile);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenProjectFileIsNotNewDoesNotReport()
    {
        using var tempRoot = new TempDirectory();
        var projectDirectory = tempRoot.GetDir("ProjectC");
        projectDirectory.Create();
        var projectFile = projectDirectory.GetFile("ProjectC.csproj").WriteAllText("<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var report = AnalyzeSingleFile("M", "ProjectC/ProjectC.csproj", projectFile);

        Assert.That(report.Findings, Is.Empty);
    }

    private static CodeSmellReport AnalyzeSingleFile(string status, string path, FileInfo fullPath)
    {
        var source = fullPath.ReadAllText();
        var normalizedSource = source.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalizedSource.Split('\n');

        var changedFile = new CodeReviewChangedFile(
            status,
            path,
            fullPath.FullName,
            normalizedSource,
            lines,
            new HashSet<int>(Enumerable.Range(1, lines.Length)));

        var context = new CodeReviewAnalysisContext(
            [changedFile],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var report = new CodeSmellReport();
        var check = new MissingReadmeForNewProjectCodeReviewCheck();
        check.Analyze(context, report);
        return report;
    }
}
