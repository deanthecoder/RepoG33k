// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace ReviewG33k.Models;

public sealed class BitbucketPullRequestReference
{
    public BitbucketPullRequestReference(string host, string projectKey, string repoSlug, int pullRequestId, string sourceUrl)
    {
        Host = host;
        ProjectKey = projectKey;
        RepoSlug = repoSlug;
        PullRequestId = pullRequestId;
        SourceUrl = sourceUrl;
    }

    public string Host { get; }

    public string ProjectKey { get; }

    public string RepoSlug { get; }

    public int PullRequestId { get; }

    public string SourceUrl { get; }

    public string CloneUrl => $"https://{Host}/scm/{ProjectKey.ToLowerInvariant()}/{RepoSlug}.git";
}
