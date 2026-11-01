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

public sealed class PullRequestReviewStampServiceTests
{
    [Test]
    public void BuildStampTextWhenNoFindingsUsesNoSuggestionsMessage()
    {
        var text = PullRequestReviewStampService.BuildStampText("Dean", 0, 0, 0);

        Assert.That(text, Is.EqualTo("Dean reviewed with ReviewG33k. No suggestions were found."));
    }

    [Test]
    public void BuildStampTextWhenInlineCommentsWerePostedUsesInlineCommentsMessage()
    {
        var text = PullRequestReviewStampService.BuildStampText("Dean", 3, 1, 2);

        Assert.That(text, Is.EqualTo("Dean reviewed with ReviewG33k. Please see inline comments for specific suggestions."));
    }

    [Test]
    public void BuildStampTextWhenSuggestionsAreNonActionableUsesSoftMessage()
    {
        var text = PullRequestReviewStampService.BuildStampText("Dean", 2, 0, 0);

        Assert.That(text, Is.EqualTo("Dean reviewed with ReviewG33k. Suggestions found, but none require action."));
    }

    [Test]
    public async Task PostReviewStampAsyncWhenGitNameUnavailableFallsBackToEnvironmentUserName()
    {
        var pullRequest = new BitbucketPullRequestReference(
            "bitbucket.example.com",
            "PROJ",
            "repo",
            42,
            "https://bitbucket.example.com/projects/PROJ/repos/repo/pull-requests/42");
        var postedText = string.Empty;
        var service = new PullRequestReviewStampService(
            (_, _) => Task.FromResult<string>(null),
            (_, text, _) =>
            {
                postedText = text;
                return Task.FromResult((true, (string)null));
            });

        var result = await service.PostReviewStampAsync(
            pullRequest,
            string.Empty,
            findingCount: 0,
            importantFindingCount: 0,
            postedInlineCommentCount: 0);

        Assert.Multiple(() =>
        {
            Assert.That(result.Success, Is.True);
            Assert.That(postedText, Does.Contain("reviewed with ReviewG33k"));
        });
    }
}
