// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ReviewG33k.Models;

namespace ReviewG33k.Services;

/// <summary>
/// Posts a short PR-level review stamp comment once a pull-request review has completed.
/// </summary>
/// <remarks>
/// Useful for giving reviewers a quick manual way to mark a PR as reviewed in Bitbucket while keeping
/// the wording consistent with ReviewG33k's current findings and any inline comments already posted.
/// </remarks>
internal sealed class PullRequestReviewStampService
{
    private readonly Func<string, CancellationToken, Task<string>> m_resolveReviewerNameAsync;
    private readonly Func<BitbucketPullRequestReference, string, CancellationToken, Task<(bool Success, string ErrorMessage)>> m_tryPostCommentAsync;

    public PullRequestReviewStampService(
        GitCommandRunner gitCommandRunner,
        BitbucketPullRequestMetadataClient pullRequestMetadataClient)
        : this(
            (workingDirectory, cancellationToken) => ResolveReviewerNameCoreAsync(gitCommandRunner, workingDirectory, cancellationToken),
            pullRequestMetadataClient.TryAddPullRequestCommentAsync)
    {
    }

    internal PullRequestReviewStampService(
        Func<string, CancellationToken, Task<string>> resolveReviewerNameAsync,
        Func<BitbucketPullRequestReference, string, CancellationToken, Task<(bool Success, string ErrorMessage)>> tryPostCommentAsync)
    {
        m_resolveReviewerNameAsync = resolveReviewerNameAsync ?? throw new ArgumentNullException(nameof(resolveReviewerNameAsync));
        m_tryPostCommentAsync = tryPostCommentAsync ?? throw new ArgumentNullException(nameof(tryPostCommentAsync));
    }

    public async Task<PullRequestReviewStampResult> PostReviewStampAsync(
        BitbucketPullRequestReference pullRequest,
        string workingDirectory,
        int findingCount,
        int importantFindingCount,
        int postedInlineCommentCount,
        CancellationToken cancellationToken = default)
    {
        if (pullRequest == null)
        {
            return PullRequestReviewStampResult.CreateFailure(
                "Pull request context is unavailable for posting a review stamp.",
                null);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var reviewerName = await m_resolveReviewerNameAsync(workingDirectory, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var stampText = BuildStampText(
            reviewerName,
            findingCount,
            importantFindingCount,
            postedInlineCommentCount);
        var result = await m_tryPostCommentAsync(pullRequest, stampText, cancellationToken);
        if (result.Success)
        {
            return PullRequestReviewStampResult.CreateSuccess(
                "Posted PR review stamp.",
                $"HINT: Posted PR review stamp to [{pullRequest.SourceUrl}].");
        }

        var errorMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
            ? "Failed to post PR review stamp."
            : result.ErrorMessage;
        return PullRequestReviewStampResult.CreateFailure(
            "Failed to post PR review stamp. See log for details.",
            $"WARNING: Could not post PR review stamp to [{pullRequest.SourceUrl}]. {errorMessage}");
    }

    internal static string BuildStampText(
        string reviewerName,
        int findingCount,
        int importantFindingCount,
        int postedInlineCommentCount)
    {
        var normalizedName = string.IsNullOrWhiteSpace(reviewerName)
            ? "Someone"
            : reviewerName.Trim();

        if (findingCount <= 0)
            return $"{normalizedName} reviewed with ReviewG33k. No suggestions were found.";
        if (postedInlineCommentCount > 0)
            return $"{normalizedName} reviewed with ReviewG33k. Please see inline comments for specific suggestions.";
        if (importantFindingCount <= 0)
            return $"{normalizedName} reviewed with ReviewG33k. Suggestions found, but none require action.";

        return $"{normalizedName} reviewed with ReviewG33k. Suggestions were found and may require action.";
    }

    private static async Task<string> ResolveReviewerNameCoreAsync(
        GitCommandRunner gitCommandRunner,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        if (gitCommandRunner != null)
        {
            var candidateWorkingDirectory = !string.IsNullOrWhiteSpace(workingDirectory) && Directory.Exists(workingDirectory)
                ? workingDirectory
                : AppContext.BaseDirectory;

            try
            {
                var result = await gitCommandRunner.RunAsync(candidateWorkingDirectory, cancellationToken, "config", "user.name");
                var configuredName = result.IsSuccess
                    ? result.StandardOutput?.Trim()
                    : null;
                if (!string.IsNullOrWhiteSpace(configuredName))
                    return configuredName;
            }
            catch
            {
                // Fall back to the local user name when git config lookup is unavailable.
            }
        }

        return string.IsNullOrWhiteSpace(Environment.UserName)
            ? "Someone"
            : Environment.UserName.Trim();
    }
}

internal readonly record struct PullRequestReviewStampResult(
    bool Success,
    string StatusMessage,
    string LogMessage)
{
    public static PullRequestReviewStampResult CreateSuccess(string statusMessage, string logMessage) =>
        new(true, statusMessage, logMessage);

    public static PullRequestReviewStampResult CreateFailure(string statusMessage, string logMessage) =>
        new(false, statusMessage, logMessage);
}
