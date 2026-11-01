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

namespace ReviewG33k.Services;

internal sealed class PullRequestReviewExecutionResult
{
    public PullRequestReviewExecutionResult(
        BitbucketPullRequestMetadata metadata,
        bool? isPullRequestOpen,
        PrepareReviewResult prepareResult,
        CodeSmellReport report)
    {
        Metadata = metadata;
        IsPullRequestOpen = isPullRequestOpen;
        PrepareResult = prepareResult;
        Report = report;
    }

    public BitbucketPullRequestMetadata Metadata { get; }

    public bool? IsPullRequestOpen { get; }

    public PrepareReviewResult PrepareResult { get; }

    public CodeSmellReport Report { get; }
}
