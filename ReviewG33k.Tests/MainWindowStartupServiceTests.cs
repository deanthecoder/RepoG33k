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
using ReviewG33k.Services;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class MainWindowStartupServiceTests
{
    [Test]
    public void ConstructorWhenGitAvailabilityServiceIsNullThrows()
    {
        var orchestrator = new CodeReviewOrchestrator(new GitCommandRunner());
        Assert.That(() => new MainWindowStartupService(null, orchestrator), Throws.ArgumentNullException);
    }

    [Test]
    public void ConstructorWhenOrchestratorIsNullThrows()
    {
        var gitAvailabilityService = new GitAvailabilityService((_, _) => Task.FromResult(new GitCommandResult(0, string.Empty, string.Empty, string.Empty)));
        Assert.That(() => new MainWindowStartupService(gitAvailabilityService, null), Throws.ArgumentNullException);
    }

    [Test]
    public void InternalConstructorWhenCheckDelegateIsNullThrows()
    {
        Assert.That(
            () => new MainWindowStartupService(null, (_, _, _) => Task.CompletedTask),
            Throws.ArgumentNullException);
    }

    [Test]
    public async Task CheckGitAvailabilityAsyncWhenGitIsAvailableReturnsSuccessResult()
    {
        var service = new MainWindowStartupService(
            _ => Task.FromResult(new GitAvailabilityCheckResult(true, null)),
            (_, _, _) => Task.CompletedTask);

        var result = await service.CheckGitAvailabilityAsync(@"C:\repo");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsGitAvailable, Is.True);
            Assert.That(result.StatusMessage, Is.Null);
            Assert.That(result.LogMessages, Is.Empty);
            Assert.That(result.DialogTitle, Is.Null);
            Assert.That(result.DialogMessage, Is.Null);
        });
    }

    [Test]
    public async Task CheckGitAvailabilityAsyncWhenGitMissingWithDetailsReturnsActionableMessages()
    {
        var service = new MainWindowStartupService(
            _ => Task.FromResult(new GitAvailabilityCheckResult(false, "git: command not found")),
            (_, _, _) => Task.CompletedTask);

        var result = await service.CheckGitAvailabilityAsync(@"C:\repo");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsGitAvailable, Is.False);
            Assert.That(result.StatusMessage, Is.EqualTo("Git is missing. Git-backed review modes are unavailable."));
            Assert.That(result.LogMessages, Has.Count.EqualTo(2));
            Assert.That(result.LogMessages[0], Is.EqualTo("ERROR: Git is not available."));
            Assert.That(result.LogMessages[1], Does.Contain("command not found"));
            Assert.That(result.DialogTitle, Is.EqualTo("Git not found"));
            Assert.That(result.DialogMessage, Does.Contain("Install Git for Windows"));
            Assert.That(result.DialogMessage, Does.Contain("enable pull request and local repository reviews"));
            Assert.That(result.DialogMessage, Does.Contain("Details: git: command not found"));
        });
    }

    [Test]
    public void CanRunStartupCleanupWhenDirectoryExistsReturnsTrue()
    {
        var service = new MainWindowStartupService(
            _ => Task.FromResult(new GitAvailabilityCheckResult(true, null)),
            (_, _, _) => Task.CompletedTask);

        using var tempDirectory = new TempDirectory();
        Assert.That(service.CanRunStartupCleanup(tempDirectory.FullName), Is.True);
    }

    [Test]
    public async Task RunStartupCleanupAsyncWhenDirectoryIsMissingDoesNotInvokeCleanupDelegate()
    {
        var wasInvoked = false;
        var service = new MainWindowStartupService(
            _ => Task.FromResult(new GitAvailabilityCheckResult(true, null)),
            (_, _, _) =>
            {
                wasInvoked = true;
                return Task.CompletedTask;
            });

        await service.RunStartupCleanupAsync(@"C:\does-not-exist\reviewg33k", _ => { }, CancellationToken.None);

        Assert.That(wasInvoked, Is.False);
    }

    [Test]
    public async Task TryPrefillPullRequestUrlFromClipboardAsyncWhenClipboardThrowsReturnsFalse()
    {
        var service = new MainWindowStartupService(
            _ => Task.FromResult(new GitAvailabilityCheckResult(true, null)),
            (_, _, _) => Task.CompletedTask);

        var didApply = await service.TryPrefillPullRequestUrlFromClipboardAsync(
            () => throw new InvalidOperationException("clipboard unavailable"),
            _ => true);

        Assert.That(didApply, Is.False);
    }

    [Test]
    public async Task TryPrefillPullRequestUrlFromClipboardAsyncWhenApplyReturnsTrueReturnsTrue()
    {
        var service = new MainWindowStartupService(
            _ => Task.FromResult(new GitAvailabilityCheckResult(true, null)),
            (_, _, _) => Task.CompletedTask);

        string capturedText = null;
        var didApply = await service.TryPrefillPullRequestUrlFromClipboardAsync(
            () => Task.FromResult("https://bitbucket.example.com/projects/PROJ/repos/sample/pull-requests/42"),
            clipboardText =>
            {
                capturedText = clipboardText;
                return true;
            });

        Assert.Multiple(() =>
        {
            Assert.That(didApply, Is.True);
            Assert.That(capturedText, Does.Contain("pull-requests/42"));
        });
    }
}
