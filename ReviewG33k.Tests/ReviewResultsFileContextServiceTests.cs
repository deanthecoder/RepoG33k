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

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class ReviewResultsFileContextServiceTests
{
    [TestCase("reviewed line\nreviewed issue\n", "> 2: reviewed issue")]
    [TestCase("", "(File is empty)")]
    public void SnapshotPreviewUsesReviewedContentWithoutReadingTheWorkingFile(string reviewedText, string expected)
    {
        var finding = new CodeSmellFinding(CodeReviewFindingSeverity.Important, "sample-rule", "src/Sample.cs", 2, "Issue");
        var service = new ReviewResultsFileContextService();
        var success = service.TryBuildPreview(finding,
            _ => throw new InvalidOperationException("Snapshot previews must not read the working file."),
            1, 1, out var preview, out var failure, _ => reviewedText);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(failure, Is.Null);
            Assert.That(preview.PreviewFileName, Is.EqualTo("Sample.cs"));
            Assert.That(preview.Text, Does.Contain(expected));
        });
    }

    [Test]
    public void TryBuildPreviewWhenFindingCanBeResolvedBuildsExpectedText()
    {
        using var tempRoot = new TempDirectory();
        var filePath = tempRoot.GetDir("src");
        filePath.Create();
        var sourceFile = filePath.GetFile("Sample.cs").WriteAllText("line1\nline2\nline3\nline4\nline5\n");
        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Important,
            "sample-rule",
            "src/Sample.cs",
            3,
            "Issue");
        var service = new ReviewResultsFileContextService();

        var success = service.TryBuildPreview(
            finding,
            _ => sourceFile.FullName,
            previewLinesBefore: 1,
            previewLinesAfter: 1,
            out var previewData,
            out var failureReason);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(failureReason, Is.Null);
            Assert.That(previewData.Header, Is.EqualTo("Preview: Sample.cs"));
            Assert.That(previewData.PreviewFileName, Is.EqualTo("Sample.cs"));
            Assert.That(previewData.Text, Does.Contain("  2: line2"));
            Assert.That(previewData.Text, Does.Contain("> 3: line3"));
            Assert.That(previewData.Text, Does.Contain("  4: line4"));
        });
    }

    [Test]
    public void TryBuildPreviewWhenFindingCannotBeResolvedReturnsFailure()
    {
        var service = new ReviewResultsFileContextService();
        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Important,
            "sample-rule",
            "src/Missing.cs",
            1,
            "Issue");

        var success = service.TryBuildPreview(
            finding,
            _ => null,
            previewLinesBefore: 2,
            previewLinesAfter: 2,
            out _,
            out var failureReason);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.False);
            Assert.That(failureReason, Is.EqualTo("Could not resolve file for 'src/Missing.cs'."));
        });
    }

    [Test]
    public void TryBuildCodexPromptWhenRepoCanBeResolvedBuildsPrompt()
    {
        using var tempRoot = new TempDirectory();
        var repoRoot = tempRoot.GetDir("Repo");
        var sourceDir = repoRoot.GetDir("src");
        repoRoot.Create();
        sourceDir.Create();
        repoRoot.GetDir(".git").Create();
        var sourceFile = sourceDir.GetFile("Sample.cs").WriteAllText("alpha\nbeta\ngamma\n");
        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Important,
            "sample-rule",
            "src/Sample.cs",
            2,
            "Fix this.");
        var service = new ReviewResultsFileContextService();

        var success = service.TryBuildCodexPrompt(
            finding,
            _ => sourceFile.FullName,
            promptLinesBefore: 1,
            promptLinesAfter: 1,
            out var promptText,
            out var failureReason);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.True);
            Assert.That(failureReason, Is.Null);
            Assert.That(promptText, Does.Contain($"Repository path: {repoRoot.FullName}"));
            Assert.That(promptText, Does.Contain("File: src/Sample.cs:2"));
            Assert.That(promptText, Does.Contain("Issue: Fix this."));
            Assert.That(promptText, Does.Contain("> 2: beta"));
        });
    }

    [Test]
    public void TryBuildCodexPromptWhenRepoCannotBeResolvedReturnsFailure()
    {
        using var tempRoot = new TempDirectory();
        var sourceDir = tempRoot.GetDir("src");
        sourceDir.Create();
        var sourceFile = sourceDir.GetFile("Sample.cs").WriteAllText("alpha\n");
        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Important,
            "sample-rule",
            "src/Sample.cs",
            1,
            "Issue");
        var service = new ReviewResultsFileContextService();

        var success = service.TryBuildCodexPrompt(
            finding,
            _ => sourceFile.FullName,
            promptLinesBefore: 1,
            promptLinesAfter: 1,
            out _,
            out var failureReason);

        Assert.Multiple(() =>
        {
            Assert.That(success, Is.False);
            Assert.That(failureReason, Is.EqualTo("Could not detect repository root from the selected file."));
        });
    }
}
