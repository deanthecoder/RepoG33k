// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using ReviewG33k.Models;
using ReviewG33k.Services;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class PullRequestStateNoticeTests
{
    [Test]
    public void TryCreateNonOpenPullRequestNoticeWhenPullRequestIsOpenReturnsFalse()
    {
        var pullRequest = CreatePullRequest(42);

        var shouldNotify = PullRequestStateNotice.TryCreateNonOpenPullRequestNotice(
            pullRequest,
            isPullRequestOpen: true,
            pullRequestStateDisplay: "OPEN",
            lastNoticeKey: null,
            out _,
            out var message);

        Assert.Multiple(() =>
        {
            Assert.That(shouldNotify, Is.False);
            Assert.That(message, Is.Null);
        });
    }

    [Test]
    public void TryCreateNonOpenPullRequestNoticeWhenClosedFirstTimeReturnsTrueAndSecondTimeReturnsFalse()
    {
        var pullRequest = CreatePullRequest(7);
        string lastNoticeKey = null;

        var first = PullRequestStateNotice.TryCreateNonOpenPullRequestNotice(
            pullRequest,
            isPullRequestOpen: false,
            pullRequestStateDisplay: "DECLINED",
            lastNoticeKey,
            out var firstNoticeKey,
            out var firstMessage);
        lastNoticeKey = firstNoticeKey;
        var second = PullRequestStateNotice.TryCreateNonOpenPullRequestNotice(
            pullRequest,
            isPullRequestOpen: false,
            pullRequestStateDisplay: "DECLINED",
            lastNoticeKey,
            out _,
            out var secondMessage);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.True);
            Assert.That(firstMessage, Does.Contain("PR #7 is DECLINED"));
            Assert.That(second, Is.False);
            Assert.That(secondMessage, Is.Null);
        });
    }

    [Test]
    public void TryCreateNonOpenPullRequestNoticeWhenMergedUsesFallbackMessage()
    {
        var pullRequest = CreatePullRequest(99);

        var shouldNotify = PullRequestStateNotice.TryCreateNonOpenPullRequestNotice(
            pullRequest,
            isPullRequestOpen: false,
            pullRequestStateDisplay: "MERGED",
            lastNoticeKey: null,
            out _,
            out var message);

        Assert.Multiple(() =>
        {
            Assert.That(shouldNotify, Is.True);
            Assert.That(message, Does.Contain("merge-commit fallback"));
        });
    }

    private static BitbucketPullRequestReference CreatePullRequest(int id) =>
        new(
            "bitbucket.example.com",
            "PROJ",
            "repo",
            id,
            $"https://bitbucket.example.com/projects/PROJ/repos/repo/pull-requests/{id}");
}
