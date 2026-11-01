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
using System.Text.RegularExpressions;
using ReviewG33k.Models;

namespace ReviewG33k.Services;

public static class BitbucketPrUrlParser
{
    private static readonly Regex PrUrlMatch = new(
        @"https?://[^\s""'<>]+/projects/[^/\s]+/repos/[^/\s]+/pull-requests/\d+(?:[^\s""'<>]*)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool TryParse(string text, out BitbucketPullRequestReference pullRequest, out string error)
    {
        pullRequest = null;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Enter a Bitbucket pull request URL first.";
            return false;
        }

        var matchedUrl = ExtractUrl(text.Trim());
        if (matchedUrl == null)
        {
            error = "Could not find a Bitbucket pull request URL in the text provided.";
            return false;
        }

        if (!Uri.TryCreate(matchedUrl, UriKind.Absolute, out var uri) ||
            !(uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
              uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
        {
            error = "The URL is not a valid HTTP/HTTPS URL.";
            return false;
        }

        var segments = uri.AbsolutePath
            .Trim('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Length < 6 ||
            !segments[0].Equals("projects", StringComparison.OrdinalIgnoreCase) ||
            !segments[2].Equals("repos", StringComparison.OrdinalIgnoreCase) ||
            !segments[4].Equals("pull-requests", StringComparison.OrdinalIgnoreCase))
        {
            error = "The URL is not in expected Bitbucket Server pull request format.";
            return false;
        }

        if (!int.TryParse(segments[5], out var pullRequestId))
        {
            error = "Could not parse the pull request id from the URL.";
            return false;
        }

        var projectKey = segments[1];
        var repoSlug = segments[3];
        var canonicalUrl = BuildCanonicalPullRequestUrl(uri, projectKey, repoSlug, pullRequestId);

        pullRequest = new BitbucketPullRequestReference(uri.Host, projectKey, repoSlug, pullRequestId, canonicalUrl);
        return true;
    }

    private static string BuildCanonicalPullRequestUrl(Uri uri, string projectKey, string repoSlug, int pullRequestId) =>
        $"{uri.Scheme}://{uri.Authority}/projects/{projectKey}/repos/{repoSlug}/pull-requests/{pullRequestId}";

    private static string ExtractUrl(string text)
    {
        if (Uri.TryCreate(text, UriKind.Absolute, out _))
            return text;

        var match = PrUrlMatch.Match(text);
        return match.Success ? match.Value : null;
    }
}
