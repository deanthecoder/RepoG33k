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
using System.Threading;
using System.Threading.Tasks;
using ReviewG33k.Models;

namespace ReviewG33k.Services;

internal sealed class PullRequestPreviewService
{
    private readonly BitbucketPullRequestMetadataClient m_metadataClient;

    public PullRequestPreviewService(BitbucketPullRequestMetadataClient metadataClient)
    {
        m_metadataClient = metadataClient ?? throw new ArgumentNullException(nameof(metadataClient));
    }

    public async Task<PullRequestPreviewResult> TryBuildPreviewAsync(
        bool isPullRequestReviewMode,
        string pullRequestUrl,
        CancellationToken cancellationToken)
    {
        if (!isPullRequestReviewMode)
            return PullRequestPreviewResult.Empty;

        var url = pullRequestUrl?.Trim();
        if (string.IsNullOrWhiteSpace(url))
            return PullRequestPreviewResult.Empty;

        if (!BitbucketPrUrlParser.TryParse(url, out var pullRequest, out _))
            return PullRequestPreviewResult.Empty;

        var metadata = await m_metadataClient.TryGetMetadataAsync(pullRequest, cancellationToken);
        return new PullRequestPreviewResult(pullRequest, metadata);
    }
}

internal readonly record struct PullRequestPreviewResult(
    BitbucketPullRequestReference PullRequest,
    BitbucketPullRequestMetadata Metadata)
{
    public static PullRequestPreviewResult Empty => new(null, null);
}
