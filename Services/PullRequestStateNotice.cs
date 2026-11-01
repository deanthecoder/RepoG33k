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
using ReviewG33k.Models;

namespace ReviewG33k.Services;

/// <summary>
/// Builds user-facing notices for pull requests that are not in the OPEN state.
/// </summary>
/// <remarks>
/// Useful for keeping warning-message rules in one place while callers own deduplication state
/// through the generated notice key.
/// </remarks>
internal static class PullRequestStateNotice
{
    public static bool TryCreateNonOpenPullRequestNotice(
        BitbucketPullRequestReference pullRequest,
        bool? isPullRequestOpen,
        string pullRequestStateDisplay,
        string lastNoticeKey,
        out string noticeKey,
        out string message)
    {
        noticeKey = lastNoticeKey;
        message = null;

        if (pullRequest == null || isPullRequestOpen != false)
            return false;

        var normalizedState = string.IsNullOrWhiteSpace(pullRequestStateDisplay)
            ? "N/A"
            : pullRequestStateDisplay.Trim();
        var candidateNoticeKey = $"{pullRequest.SourceUrl}|{normalizedState}";
        if (string.Equals(candidateNoticeKey, lastNoticeKey, StringComparison.Ordinal))
            return false;

        noticeKey = candidateNoticeKey;
        message = normalizedState.Equals("MERGED", StringComparison.OrdinalIgnoreCase)
            ? $"PR #{pullRequest.PullRequestId} is MERGED. ReviewG33k will attempt merge-commit fallback."
            : $"PR #{pullRequest.PullRequestId} is {normalizedState}. Review checkout requires an OPEN pull request.";
        return true;
    }
}
