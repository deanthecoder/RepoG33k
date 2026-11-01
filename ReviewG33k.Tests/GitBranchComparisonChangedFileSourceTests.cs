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

public sealed class GitBranchComparisonChangedFileSourceTests
{
    [Test]
    public async Task LoadAsyncWhenLocalBranchShadowsRemoteTrackingBranchUsesRemoteTrackingBranch()
    {
        using var tempRoot = new TempDirectory();
        var remoteRepository = tempRoot.GetDir("remote.git");
        var localRepository = tempRoot.GetDir("local");
        remoteRepository.Create();
        localRepository.Create();

        var git = new GitCommandRunner();
        Assert.That((await git.RunAsync(remoteRepository.FullName, "init", "--bare")).IsSuccess, Is.True);
        Assert.That((await git.RunAsync(localRepository.FullName, "init")).IsSuccess, Is.True);
        Assert.That((await git.RunAsync(localRepository.FullName, "checkout", "-b", "main")).IsSuccess, Is.True);
        Assert.That((await git.RunAsync(localRepository.FullName, "config", "user.email", "reviewg33k@example.com")).IsSuccess, Is.True);
        Assert.That((await git.RunAsync(localRepository.FullName, "config", "user.name", "ReviewG33k Tests")).IsSuccess, Is.True);

        localRepository.GetFile("Initial.cs").WriteAllText("public sealed class Initial { }\n");
        Assert.That((await git.RunAsync(localRepository.FullName, "add", ".")).IsSuccess, Is.True);
        Assert.That((await git.RunAsync(localRepository.FullName, "commit", "-m", "Initial")).IsSuccess, Is.True);
        var initialCommit = (await git.RunAsync(localRepository.FullName, "rev-parse", "HEAD")).StandardOutput.Trim();
        Assert.That((await git.RunAsync(localRepository.FullName, "remote", "add", "origin", remoteRepository.FullName)).IsSuccess, Is.True);
        Assert.That((await git.RunAsync(localRepository.FullName, "push", "origin", "main")).IsSuccess, Is.True);
        Assert.That((await git.RunAsync(localRepository.FullName, "checkout", "-b", "feature")).IsSuccess, Is.True);

        Assert.That((await git.RunAsync(localRepository.FullName, "checkout", "main")).IsSuccess, Is.True);
        localRepository.GetFile("PageShooter.cs").WriteAllText("public sealed class PageShooter { }\n");
        Assert.That((await git.RunAsync(localRepository.FullName, "add", ".")).IsSuccess, Is.True);
        Assert.That((await git.RunAsync(localRepository.FullName, "commit", "-m", "Add PageShooter")).IsSuccess, Is.True);
        Assert.That((await git.RunAsync(localRepository.FullName, "push", "origin", "main")).IsSuccess, Is.True);

        Assert.That((await git.RunAsync(localRepository.FullName, "checkout", "feature")).IsSuccess, Is.True);
        Assert.That((await git.RunAsync(localRepository.FullName, "merge", "origin/main")).IsSuccess, Is.True);
        localRepository.GetFile("Feature.cs").WriteAllText("public sealed class Feature { }\n");
        Assert.That((await git.RunAsync(localRepository.FullName, "add", ".")).IsSuccess, Is.True);
        Assert.That((await git.RunAsync(localRepository.FullName, "commit", "-m", "Feature change")).IsSuccess, Is.True);
        Assert.That((await git.RunAsync(localRepository.FullName, "update-ref", "refs/heads/origin/main", initialCommit)).IsSuccess, Is.True);

        var source = new GitBranchComparisonChangedFileSource(git, localRepository.FullName, "main", fetchTargetBranch: false);
        var result = await source.LoadAsync();

        Assert.That(result.Files.Select(file => file.Path), Is.EquivalentTo(["Feature.cs"]));
    }

    [Test]
    public async Task LoadAsyncWhenFetchingTargetBranchUpdatesRemoteTrackingRefWithoutCreatingLocalBranch()
    {
        using var tempRoot = new TempDirectory();
        var remoteRepository = tempRoot.GetDir("remote.git");
        var localRepository = tempRoot.GetDir("local");
        remoteRepository.Create();
        localRepository.Create();

        var git = new GitCommandRunner();
        Assert.That((await git.RunAsync(remoteRepository.FullName, "init", "--bare")).IsSuccess, Is.True);
        Assert.That((await git.RunAsync(localRepository.FullName, "init")).IsSuccess, Is.True);
        Assert.That((await git.RunAsync(localRepository.FullName, "checkout", "-b", "main")).IsSuccess, Is.True);
        Assert.That((await git.RunAsync(localRepository.FullName, "config", "user.email", "reviewg33k@example.com")).IsSuccess, Is.True);
        Assert.That((await git.RunAsync(localRepository.FullName, "config", "user.name", "ReviewG33k Tests")).IsSuccess, Is.True);

        localRepository.GetFile("Worker.cs").WriteAllText("public sealed class Worker { }\n");
        Assert.That((await git.RunAsync(localRepository.FullName, "add", ".")).IsSuccess, Is.True);
        Assert.That((await git.RunAsync(localRepository.FullName, "commit", "-m", "Initial")).IsSuccess, Is.True);
        Assert.That((await git.RunAsync(localRepository.FullName, "remote", "add", "origin", remoteRepository.FullName)).IsSuccess, Is.True);
        Assert.That((await git.RunAsync(localRepository.FullName, "push", "origin", "main")).IsSuccess, Is.True);
        Assert.That((await git.RunAsync(localRepository.FullName, "update-ref", "-d", "refs/remotes/origin/main")).IsSuccess, Is.True);

        var source = new GitBranchComparisonChangedFileSource(
            git,
            localRepository.FullName,
            "main",
            fetchTargetBranch: true);

        await source.LoadAsync();

        var remoteTrackingRef = await git.RunAsync(
            localRepository.FullName,
            "show-ref",
            "--verify",
            "--quiet",
            "refs/remotes/origin/main");
        var incorrectlyCreatedLocalBranch = await git.RunAsync(
            localRepository.FullName,
            "show-ref",
            "--verify",
            "--quiet",
            "refs/heads/origin/main");

        Assert.Multiple(() =>
        {
            Assert.That(remoteTrackingRef.IsSuccess, Is.True);
            Assert.That(incorrectlyCreatedLocalBranch.IsSuccess, Is.False);
        });
    }
}
