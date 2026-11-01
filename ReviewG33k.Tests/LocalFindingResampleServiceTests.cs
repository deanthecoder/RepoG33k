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

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class LocalFindingResampleServiceTests
{
    [Test]
    public void ConstructorWhenGitRunnerIsNullThrows()
    {
        Assert.That(
            () => new LocalFindingResampleService(
                null,
                new CodeSmellReportAnalyzer(new GitCommandRunner())),
            Throws.ArgumentNullException);
    }

    [Test]
    public void ConstructorWhenAnalyzerIsNullThrows()
    {
        Assert.That(
            () => new LocalFindingResampleService(
                new GitCommandRunner(),
                null),
            Throws.ArgumentNullException);
    }

    [Test]
    public async Task ResampleFindingsForFileAsyncWhenFilePathIsBlankReturnsEmpty()
    {
        var service = new LocalFindingResampleService(
            new GitCommandRunner(),
            new CodeSmellReportAnalyzer(new GitCommandRunner()));

        var findings = await service.ResampleFindingsForFileAsync(
            filePath: " ",
            localRepositoryPath: @"C:\repo",
            baseBranch: "main",
            reviewMode: LocalReviewResampleMode.Committed,
            includeFullModifiedFiles: false,
            appendLog: _ => { });

        Assert.That(findings, Is.Empty);
    }

    [Test]
    public async Task ResampleFindingsForFileAsyncWhenLocalRepositoryPathIsBlankReturnsEmpty()
    {
        var service = new LocalFindingResampleService(
            new GitCommandRunner(),
            new CodeSmellReportAnalyzer(new GitCommandRunner()));

        var findings = await service.ResampleFindingsForFileAsync(
            filePath: "src/Foo.cs",
            localRepositoryPath: " ",
            baseBranch: "main",
            reviewMode: LocalReviewResampleMode.Committed,
            includeFullModifiedFiles: false,
            appendLog: _ => { });

        Assert.That(findings, Is.Empty);
    }

    [Test]
    public async Task ResampleFindingsForFileAsyncWhenCommittedModeHasNoBaseBranchReturnsEmpty()
    {
        var service = new LocalFindingResampleService(
            new GitCommandRunner(),
            new CodeSmellReportAnalyzer(new GitCommandRunner()));

        var findings = await service.ResampleFindingsForFileAsync(
            filePath: "src/Foo.cs",
            localRepositoryPath: AppContext.BaseDirectory,
            baseBranch: " ",
            reviewMode: LocalReviewResampleMode.Committed,
            includeFullModifiedFiles: false,
            appendLog: _ => { });

        Assert.That(findings, Is.Empty);
    }
}
