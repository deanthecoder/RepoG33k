// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software.

using RepoG33k.Services;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class TemporaryReviewWorkspaceTests
{
    [Test]
    public void SessionReusesItsRootAndDeletesClonesAndWorktreesIncludingReadOnlyObjects()
    {
        using var workspace = new TemporaryReviewWorkspace();
        var root = workspace.GetRepositoryRoot();
        Assert.That(workspace.GetRepositoryRoot(), Is.EqualTo(root));
        var objects = Directory.CreateDirectory(Path.Combine(root, "repo", ".git", "objects"));
        var objectFile = Path.Combine(objects.FullName, "object");
        File.WriteAllText(objectFile, "git object");
        File.SetAttributes(objectFile, File.GetAttributes(objectFile) | FileAttributes.ReadOnly);
        var worktree = Directory.CreateDirectory(Path.Combine(root, "CodeReview", "repo", "PR-123"));
        File.WriteAllText(Path.Combine(worktree.FullName, "source.cs"), "review content");

        workspace.Dispose();

        Assert.That(Directory.Exists(root), Is.False);
        Assert.DoesNotThrow(workspace.Dispose);
    }

    [Test]
    public void CleanupDoesNotDeleteAnotherSessionsWorkspace()
    {
        using var first = new TemporaryReviewWorkspace();
        using var second = new TemporaryReviewWorkspace();
        var firstRoot = first.GetRepositoryRoot();
        var secondRoot = second.GetRepositoryRoot();
        Assert.That(firstRoot, Is.Not.EqualTo(secondRoot));

        first.Dispose();

        Assert.That(Directory.Exists(secondRoot), Is.True);
    }
}
