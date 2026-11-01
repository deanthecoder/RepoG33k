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
public sealed class MainWindowActionStateServiceTests
{
    [Test]
    public void ConstructorWhenValidationServiceIsNullThrows()
    {
        Assert.That(() => new MainWindowActionStateService(null), Throws.ArgumentNullException);
    }

    [Test]
    public void BuildSnapshotWhenInputsAreValidIncludesExpectedActionFlags()
    {
        using var tempRoot = new TempDirectory();
        var repositoryRoot = tempRoot.GetDir("repo-root");
        var localRepository = tempRoot.GetDir("local-repo");
        repositoryRoot.Create();
        localRepository.Create();
        localRepository.GetDir(".git").Create();
        var solutionFile = repositoryRoot.GetFile("App.sln").WriteAllText("Microsoft Visual Studio Solution File");

        var service = new MainWindowActionStateService(new MainWindowInputValidationService());
        var snapshot = service.BuildSnapshot(
                repositoryRootPath: repositoryRoot.FullName,
                localRepositoryPath: localRepository.FullName,
                localBaseBranch: string.Empty,
                pullRequestUrl: "https://bitbucket.example.com/projects/PROJ/repos/repo/pull-requests/19",
                isAnyLocalReviewMode: true,
                requiresLocalBaseBranch: false,
                previewPullRequestIsOpen: true,
                previewPullRequestState: "OPEN",
                latestSolutionPath: null,
                latestReviewWorktreePath: null,
                canCancelCurrentOperation: true,
            isCancellationRequested: false);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.HasValidPullRequestInput, Is.True);
            Assert.That(snapshot.HasValidPullRequestPrepareInputs, Is.True);
            Assert.That(snapshot.HasValidLocalPrepareInputs, Is.True);
            Assert.That(snapshot.HasAvailableSolution, Is.True);
            Assert.That(snapshot.ResolvedSolutionPath, Is.EqualTo(solutionFile.FullName));
            Assert.That(snapshot.CanReviewCurrentPullRequest, Is.True);
            Assert.That(snapshot.CanCancelCurrentOperation, Is.True);
        });
    }

    [Test]
    public void BuildSnapshotWhenCommittedModeHasNoBaseBranchMarksLocalInputsInvalid()
    {
        using var tempRoot = new TempDirectory();
        var localRepository = tempRoot.GetDir("local-repo");
        localRepository.Create();
        localRepository.GetDir(".git").Create();

        var service = new MainWindowActionStateService(new MainWindowInputValidationService());
        var snapshot = service.BuildSnapshot(
            repositoryRootPath: tempRoot.FullName,
            localRepositoryPath: localRepository.FullName,
            localBaseBranch: " ",
            pullRequestUrl: " ",
            isAnyLocalReviewMode: true,
            requiresLocalBaseBranch: true,
            previewPullRequestIsOpen: false,
            previewPullRequestState: "DECLINED",
            latestSolutionPath: null,
            latestReviewWorktreePath: null,
            canCancelCurrentOperation: false,
            isCancellationRequested: false);

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.HasValidLocalPrepareInputs, Is.False);
            Assert.That(snapshot.CanReviewCurrentPullRequest, Is.False);
        });
    }

    [Test]
    public void BuildSnapshotWhenPullRequestIsMergedKeepsReviewEnabledForFallback()
    {
        var service = new MainWindowActionStateService(new MainWindowInputValidationService());
        var snapshot = service.BuildSnapshot(
            repositoryRootPath: "C:/repo",
            localRepositoryPath: "C:/repo",
            localBaseBranch: "main",
            pullRequestUrl: "https://bitbucket.example.com/projects/PROJ/repos/repo/pull-requests/19",
            isAnyLocalReviewMode: false,
            requiresLocalBaseBranch: false,
            previewPullRequestIsOpen: false,
            previewPullRequestState: "MERGED",
            latestSolutionPath: null,
            latestReviewWorktreePath: null,
            canCancelCurrentOperation: false,
            isCancellationRequested: false);

        Assert.That(snapshot.CanReviewCurrentPullRequest, Is.True);
    }

    [Test]
    public void BuildSnapshotRejectsDirectoriesWithoutGit()
    {
        using var tempRoot = new TempDirectory();
        var directory = tempRoot.GetDir("plain-folder");
        directory.Create();

        var service = new MainWindowActionStateService(new MainWindowInputValidationService());
        var snapshot = service.BuildSnapshot(
            repositoryRootPath: null,
            localRepositoryPath: directory.FullName,
            localBaseBranch: null,
            pullRequestUrl: null,
            isAnyLocalReviewMode: true,
            requiresLocalBaseBranch: false,
            previewPullRequestIsOpen: null,
            previewPullRequestState: null,
            latestSolutionPath: null,
            latestReviewWorktreePath: null,
            canCancelCurrentOperation: false,
            isCancellationRequested: false);

        Assert.That(snapshot.HasValidLocalPrepareInputs, Is.False);
    }
}
