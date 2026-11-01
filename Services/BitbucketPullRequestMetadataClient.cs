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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ReviewG33k.Models;

namespace ReviewG33k.Services;

/// <summary>
/// Reads pull request metadata/changed paths and posts inline comments via Bitbucket Server REST APIs.
/// </summary>
/// <remarks>
/// Authentication uses a two-step approach:
/// <list type="number">
/// <item>
/// <description>Requests first run with <see cref="HttpClientHandler.UseDefaultCredentials"/> (Windows/SSO style auth where available).</description>
/// </item>
/// <item>
/// <description>If Bitbucket returns 401/403, the client asks Git Credential Manager via <c>git credential fill</c> for credentials for the PR host/repo, then retries with a Basic auth header.</description>
/// </item>
/// </list>
/// This class does not store credentials; they are only requested on demand when fallback auth is needed.
/// </remarks>
public sealed class BitbucketPullRequestMetadataClient : IDisposable
{
    private readonly HttpClient m_httpClient;
    private readonly bool m_ownsHttpClient;
    private readonly ConcurrentDictionary<string, BitbucketPullRequestMetadata> m_metadataCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string[]> m_changedPathsCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> m_mergeCommitHashCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex CommitHashRegex = new("^[0-9a-f]{7,40}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public BitbucketPullRequestMetadataClient()
        : this(CreateDefaultHttpClient(), ownsHttpClient: true)
    {
    }

    internal BitbucketPullRequestMetadataClient(HttpClient httpClient, bool ownsHttpClient = false)
    {
        m_httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        m_ownsHttpClient = ownsHttpClient;
    }

    public async Task<BitbucketPullRequestMetadata> TryGetMetadataAsync(BitbucketPullRequestReference pullRequest, CancellationToken cancellationToken = default)
    {
        if (pullRequest == null)
            return null;

        if (m_metadataCache.TryGetValue(pullRequest.SourceUrl, out var cachedInfo))
            return cachedInfo;

        var apiUrl = BuildMetadataApiUrl(pullRequest);
        var json = await TryGetResponseTextAsync(pullRequest, apiUrl, cancellationToken);
        if (string.IsNullOrWhiteSpace(json))
            return null;

        if (!TryParseMetadata(json, out var metadata))
            return null;

        m_metadataCache[pullRequest.SourceUrl] = metadata;
        return metadata;
    }

    public async Task<IReadOnlyList<string>> TryGetChangedPathsAsync(BitbucketPullRequestReference pullRequest, CancellationToken cancellationToken = default)
    {
        if (pullRequest == null)
            return [];

        if (m_changedPathsCache.TryGetValue(pullRequest.SourceUrl, out var cachedPaths))
            return cachedPaths;

        var paths = new List<string>();
        var start = 0;

        while (true)
        {
            var apiUrl = BuildChangesApiUrl(pullRequest, start, 500);
            var json = await TryGetResponseTextAsync(pullRequest, apiUrl, cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
                break;

            if (!TryParseChangesPage(json, paths, out var isLastPage, out var nextPageStart))
                break;

            if (isLastPage || !nextPageStart.HasValue || nextPageStart.Value <= start)
                break;

            start = nextPageStart.Value;
        }

        var uniquePaths = paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        m_changedPathsCache[pullRequest.SourceUrl] = uniquePaths;
        return uniquePaths;
    }

    internal async Task<string> TryGetMergeCommitHashAsync(BitbucketPullRequestReference pullRequest, CancellationToken cancellationToken = default)
    {
        if (pullRequest == null)
            return null;

        if (m_mergeCommitHashCache.TryGetValue(pullRequest.SourceUrl, out var cachedHash))
            return cachedHash;

        var metadataJson = await TryGetResponseTextAsync(pullRequest, BuildMetadataApiUrl(pullRequest), cancellationToken);
        if (TryExtractMergeCommitHashFromMetadata(metadataJson, out var metadataMergeCommitHash))
        {
            m_mergeCommitHashCache[pullRequest.SourceUrl] = metadataMergeCommitHash;
            return metadataMergeCommitHash;
        }

        var activitiesJson = await TryGetResponseTextAsync(pullRequest, BuildActivitiesApiUrl(pullRequest, 0, 200), cancellationToken);
        if (TryExtractMergeCommitHashFromActivities(activitiesJson, out var activitiesMergeCommitHash))
        {
            m_mergeCommitHashCache[pullRequest.SourceUrl] = activitiesMergeCommitHash;
            return activitiesMergeCommitHash;
        }

        return null;
    }

    public async Task<(bool Success, string ErrorMessage)> TryAddInlineCommentAsync(
        BitbucketPullRequestReference pullRequest,
        string path,
        int line,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (pullRequest == null)
            return (false, "Pull request reference is not available.");
        if (line < 1)
            return (false, "Line number must be greater than zero.");
        if (string.IsNullOrWhiteSpace(path))
            return (false, "File path is required.");
        if (string.IsNullOrWhiteSpace(text))
            return (false, "Comment text is required.");

        var apiUrl = BuildCommentsApiUrl(pullRequest);
        var normalizedPath = NormalizePath(path);
        var trimmedText = text.Trim();
        var inlinePayloadJson = BuildInlineAnchorPayloadJson(normalizedPath, line, trimmedText, "ADDED");

        var inlineResult = await TryPostCommentAsync(pullRequest, apiUrl, inlinePayloadJson, cancellationToken);
        if (inlineResult.Success)
            return (true, null);

        var latestInlineError = inlineResult.ErrorMessage;
        if (ShouldRetryInlineAsContext(inlineResult.StatusCode, inlineResult.ErrorMessage))
        {
            var contextInlinePayloadJson = BuildInlineAnchorPayloadJson(normalizedPath, line, trimmedText, "CONTEXT");
            var contextInlineResult = await TryPostCommentAsync(pullRequest, apiUrl, contextInlinePayloadJson, cancellationToken);
            if (contextInlineResult.Success)
                return (true, null);

            latestInlineError = CombineCommentErrors(inlineResult.ErrorMessage, contextInlineResult.ErrorMessage);
            inlineResult = contextInlineResult;
        }

        if (!ShouldFallbackToGeneralComment(inlineResult.StatusCode, inlineResult.ErrorMessage))
            return (false, latestInlineError);

        var fallbackPayloadJson = JsonSerializer.Serialize(new
        {
            text = BuildFallbackCommentText(normalizedPath, line, trimmedText)
        });
        var fallbackResult = await TryPostCommentAsync(pullRequest, apiUrl, fallbackPayloadJson, cancellationToken);
        if (fallbackResult.Success)
            return (true, null);

        return (false, CombineCommentErrors(latestInlineError, fallbackResult.ErrorMessage));
    }

    public async Task<(bool Success, string ErrorMessage)> TryAddPullRequestCommentAsync(
        BitbucketPullRequestReference pullRequest,
        string text,
        CancellationToken cancellationToken = default)
    {
        if (pullRequest == null)
            return (false, "Pull request reference is not available.");
        if (string.IsNullOrWhiteSpace(text))
            return (false, "Comment text is required.");

        var apiUrl = BuildCommentsApiUrl(pullRequest);
        var payloadJson = JsonSerializer.Serialize(new
        {
            text = text.Trim()
        });

        var result = await TryPostCommentAsync(pullRequest, apiUrl, payloadJson, cancellationToken);
        return result.Success
            ? (true, null)
            : (false, string.IsNullOrWhiteSpace(result.ErrorMessage) ? "Failed to post comment." : result.ErrorMessage);
    }

    public void Dispose()
    {
        if (m_ownsHttpClient)
            m_httpClient.Dispose();
    }

    private static HttpClient CreateDefaultHttpClient()
    {
        var handler = new HttpClientHandler
        {
            UseDefaultCredentials = true
        };

        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
    }

    private async Task<(bool Success, HttpStatusCode StatusCode, string ErrorMessage)> TryPostCommentAsync(
        BitbucketPullRequestReference pullRequest,
        string apiUrl,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await SendCommentRequestAsync(apiUrl, payloadJson, cancellationToken);
        }
        catch (Exception exception)
        {
            return (false, default, exception.Message);
        }

        if (IsAuthFailure(response.StatusCode) &&
            await TryGetGitCredentialsAsync(pullRequest, cancellationToken) is { } gitCredentials)
        {
            response.Dispose();

            try
            {
                response = await SendCommentRequestAsync(
                    apiUrl,
                    payloadJson,
                    cancellationToken,
                    BuildBasicAuthHeader(gitCredentials.Username, gitCredentials.Password));
            }
            catch (Exception exception)
            {
                return (false, default, exception.Message);
            }
        }

        var statusCode = response.StatusCode;
        if (response.IsSuccessStatusCode)
        {
            response.Dispose();
            return (true, statusCode, null);
        }

        var error = await TryReadErrorAsync(response, cancellationToken);
        response.Dispose();
        return (false, statusCode, string.IsNullOrWhiteSpace(error) ? $"Bitbucket returned {(int)statusCode}." : error);
    }

    private static bool IsAuthFailure(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden;

    private static bool ShouldRetryInlineAsContext(HttpStatusCode statusCode, string errorMessage) =>
        ShouldFallbackToGeneralComment(statusCode, errorMessage);

    private static bool ShouldFallbackToGeneralComment(HttpStatusCode statusCode, string errorMessage)
    {
        if (statusCode == HttpStatusCode.Conflict || (int)statusCode == 422)
            return true;
        if (statusCode != HttpStatusCode.BadRequest || string.IsNullOrWhiteSpace(errorMessage))
            return false;

        return errorMessage.Contains("anchor", StringComparison.OrdinalIgnoreCase) ||
               errorMessage.Contains("line", StringComparison.OrdinalIgnoreCase) &&
               (errorMessage.Contains("added", StringComparison.OrdinalIgnoreCase) ||
                errorMessage.Contains("diff", StringComparison.OrdinalIgnoreCase) ||
                errorMessage.Contains("hunk", StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildInlineAnchorPayloadJson(string normalizedPath, int line, string trimmedText, string lineType) =>
        JsonSerializer.Serialize(
            new
            {
                text = trimmedText,
                anchor = new
                {
                    line,
                    lineType,
                    fileType = "TO",
                    path = normalizedPath
                }
            });

    private static string BuildFallbackCommentText(string normalizedPath, int line, string text)
    {
        var prefix = $"[{normalizedPath}:{line}]";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return text;

        return $"{prefix} {text}";
    }

    private static string CombineCommentErrors(string inlineError, string fallbackError)
    {
        if (string.IsNullOrWhiteSpace(inlineError))
            return fallbackError ?? "Failed to post comment.";
        if (string.IsNullOrWhiteSpace(fallbackError))
            return inlineError;

        return $"{inlineError} Fallback comment attempt also failed: {fallbackError}";
    }

    private async Task<HttpResponseMessage> SendCommentRequestAsync(
        string apiUrl,
        string payloadJson,
        CancellationToken cancellationToken,
        AuthenticationHeaderValue authHeader = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, apiUrl)
        {
            Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
        };

        if (authHeader != null)
            request.Headers.Authorization = authHeader;

        return await m_httpClient.SendAsync(request, cancellationToken);
    }

    private static async Task<string> TryReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("errors", out var errorsElement) &&
                errorsElement.ValueKind == JsonValueKind.Array)
            {
                var messages = errorsElement
                    .EnumerateArray()
                    .Select(error => error.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
                        ? messageElement.GetString()
                        : null)
                    .Where(message => !string.IsNullOrWhiteSpace(message))
                    .ToArray();

                if (messages.Length > 0)
                    return string.Join(" ", messages);
            }

            return raw.Trim();
        }
        catch
        {
            return null;
        }
    }

    private async Task<string> TryGetResponseTextAsync(BitbucketPullRequestReference pullRequest, string apiUrl, CancellationToken cancellationToken)
    {
        HttpResponseMessage response;
        try
        {
            response = await m_httpClient.GetAsync(apiUrl, cancellationToken);
        }
        catch
        {
            return null;
        }

        if ((response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden) &&
            await TryGetGitCredentialsAsync(pullRequest, cancellationToken) is { } gitCredentials)
        {
            response.Dispose();

            try
            {
                using var authenticatedRequest = new HttpRequestMessage(HttpMethod.Get, apiUrl);
                authenticatedRequest.Headers.Authorization = BuildBasicAuthHeader(gitCredentials.Username, gitCredentials.Password);
                response = await m_httpClient.SendAsync(authenticatedRequest, cancellationToken);
            }
            catch
            {
                return null;
            }
        }

        if (!response.IsSuccessStatusCode)
        {
            response.Dispose();
            return null;
        }

        string json;
        try
        {
            json = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch
        {
            return null;
        }
        finally
        {
            response.Dispose();
        }

        return json;
    }

    private static string BuildMetadataApiUrl(BitbucketPullRequestReference pullRequest) =>
        $"https://{pullRequest.Host}/rest/api/1.0/projects/{Uri.EscapeDataString(pullRequest.ProjectKey)}/repos/{Uri.EscapeDataString(pullRequest.RepoSlug)}/pull-requests/{pullRequest.PullRequestId}";

    private static string BuildChangesApiUrl(BitbucketPullRequestReference pullRequest, int start, int limit) =>
        $"https://{pullRequest.Host}/rest/api/1.0/projects/{Uri.EscapeDataString(pullRequest.ProjectKey)}/repos/{Uri.EscapeDataString(pullRequest.RepoSlug)}/pull-requests/{pullRequest.PullRequestId}/changes?start={start}&limit={limit}";

    private static string BuildActivitiesApiUrl(BitbucketPullRequestReference pullRequest, int start, int limit) =>
        $"https://{pullRequest.Host}/rest/api/1.0/projects/{Uri.EscapeDataString(pullRequest.ProjectKey)}/repos/{Uri.EscapeDataString(pullRequest.RepoSlug)}/pull-requests/{pullRequest.PullRequestId}/activities?start={start}&limit={limit}";

    private static string BuildCommentsApiUrl(BitbucketPullRequestReference pullRequest) =>
        $"https://{pullRequest.Host}/rest/api/1.0/projects/{Uri.EscapeDataString(pullRequest.ProjectKey)}/repos/{Uri.EscapeDataString(pullRequest.RepoSlug)}/pull-requests/{pullRequest.PullRequestId}/comments";

    private static string NormalizePath(string path) => (path ?? string.Empty).Replace('\\', '/');

    private static bool TryParseMetadata(string json, out BitbucketPullRequestMetadata metadata)
    {
        metadata = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var targetBranch = GetBranchName(root, "toRef");
            var title = GetTitle(root);
            var author = GetAuthor(root);
            var state = GetState(root);

            metadata = new BitbucketPullRequestMetadata(targetBranch, title, author, state);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetBranchName(JsonElement root, string refPropertyName)
    {
        if (!root.TryGetProperty(refPropertyName, out var refElement) || refElement.ValueKind != JsonValueKind.Object)
            return null;

        if (refElement.TryGetProperty("displayId", out var displayIdElement) && displayIdElement.ValueKind == JsonValueKind.String)
            return displayIdElement.GetString();

        if (refElement.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String)
            return TrimGitRefPrefix(idElement.GetString());

        return null;
    }

    private static string GetTitle(JsonElement root)
    {
        if (root.TryGetProperty("title", out var titleElement) && titleElement.ValueKind == JsonValueKind.String)
            return titleElement.GetString();

        return string.Empty;
    }

    private static string GetAuthor(JsonElement root)
    {
        if (!root.TryGetProperty("author", out var authorElement) || authorElement.ValueKind != JsonValueKind.Object)
            return string.Empty;

        if (!authorElement.TryGetProperty("user", out var userElement) || userElement.ValueKind != JsonValueKind.Object)
            return string.Empty;

        if (userElement.TryGetProperty("displayName", out var displayNameElement) && displayNameElement.ValueKind == JsonValueKind.String)
            return displayNameElement.GetString();

        if (userElement.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String)
            return nameElement.GetString();

        return string.Empty;
    }

    private static string GetState(JsonElement root)
    {
        if (root.TryGetProperty("state", out var stateElement) && stateElement.ValueKind == JsonValueKind.String)
            return stateElement.GetString();

        return string.Empty;
    }

    private static string TrimGitRefPrefix(string refName)
    {
        if (string.IsNullOrWhiteSpace(refName))
            return refName;

        const string headsPrefix = "refs/heads/";
        return refName.StartsWith(headsPrefix, StringComparison.OrdinalIgnoreCase) ? refName[headsPrefix.Length..] : refName;
    }

    private static bool TryParseChangesPage(string json, List<string> paths, out bool isLastPage, out int? nextPageStart)
    {
        isLastPage = true;
        nextPageStart = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            if (root.TryGetProperty("isLastPage", out var isLastPageElement) &&
                (isLastPageElement.ValueKind == JsonValueKind.True || isLastPageElement.ValueKind == JsonValueKind.False))
            {
                isLastPage = isLastPageElement.GetBoolean();
            }

            if (root.TryGetProperty("nextPageStart", out var nextPageStartElement) &&
                nextPageStartElement.ValueKind == JsonValueKind.Number &&
                nextPageStartElement.TryGetInt32(out var nextStart))
            {
                nextPageStart = nextStart;
            }

            if (!root.TryGetProperty("values", out var valuesElement) || valuesElement.ValueKind != JsonValueKind.Array)
                return true;

            foreach (var changeElement in valuesElement.EnumerateArray())
            {
                if (TryGetChangedPath(changeElement, out var path))
                    paths.Add(path);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryExtractMergeCommitHashFromMetadata(string json, out string mergeCommitHash)
    {
        mergeCommitHash = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("properties", out var propertiesElement) || propertiesElement.ValueKind != JsonValueKind.Object)
                return false;

            if (!propertiesElement.TryGetProperty("mergeCommit", out var mergeCommitElement) || mergeCommitElement.ValueKind != JsonValueKind.Object)
                return false;

            return TryGetCommitHashFromElement(mergeCommitElement, out mergeCommitHash);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryExtractMergeCommitHashFromActivities(string json, out string mergeCommitHash)
    {
        mergeCommitHash = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("values", out var valuesElement) || valuesElement.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var activityElement in valuesElement.EnumerateArray())
            {
                if (!IsMergedActivity(activityElement))
                    continue;

                if (activityElement.TryGetProperty("commit", out var commitElement) &&
                    commitElement.ValueKind == JsonValueKind.Object &&
                    TryGetCommitHashFromElement(commitElement, out mergeCommitHash))
                {
                    return true;
                }

                if (TryGetCommitHashFromElement(activityElement, out mergeCommitHash))
                    return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsMergedActivity(JsonElement activityElement)
    {
        if (!activityElement.TryGetProperty("action", out var actionElement) || actionElement.ValueKind != JsonValueKind.String)
            return false;

        var action = actionElement.GetString();
        return string.Equals(action, "MERGED", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetCommitHashFromElement(JsonElement element, out string commitHash)
    {
        commitHash = null;

        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty("id", out var idElement) &&
                idElement.ValueKind == JsonValueKind.String &&
                IsCommitHashLike(idElement.GetString()))
            {
                commitHash = idElement.GetString();
                return true;
            }

            if (element.TryGetProperty("hash", out var hashElement) &&
                hashElement.ValueKind == JsonValueKind.String &&
                IsCommitHashLike(hashElement.GetString()))
            {
                commitHash = hashElement.GetString();
                return true;
            }

            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object)
                    continue;

                if (TryGetCommitHashFromElement(property.Value, out commitHash))
                    return true;
            }
        }

        return false;
    }

    private static bool IsCommitHashLike(string value) =>
        !string.IsNullOrWhiteSpace(value) && CommitHashRegex.IsMatch(value.Trim());

    private static bool TryGetChangedPath(JsonElement changeElement, out string path)
    {
        path = null;

        if (!changeElement.TryGetProperty("path", out var pathElement) || pathElement.ValueKind != JsonValueKind.Object)
            return false;

        if (pathElement.TryGetProperty("toString", out var toStringElement) && toStringElement.ValueKind == JsonValueKind.String)
        {
            path = toStringElement.GetString();
            return !string.IsNullOrWhiteSpace(path);
        }

        if (!pathElement.TryGetProperty("components", out var componentsElement) || componentsElement.ValueKind != JsonValueKind.Array)
            return false;

        var components = componentsElement
            .EnumerateArray()
            .Where(c => c.ValueKind == JsonValueKind.String)
            .Select(c => c.GetString())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToArray();

        if (components.Length == 0)
            return false;

        path = string.Join("/", components);
        return true;
    }

    private static AuthenticationHeaderValue BuildBasicAuthHeader(string username, string password)
    {
        var combined = $"{username}:{password}";
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(combined));
        return new AuthenticationHeaderValue("Basic", encoded);
    }

    private static async Task<(string Username, string Password)?> TryGetGitCredentialsAsync(BitbucketPullRequestReference pullRequest, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = AppContext.BaseDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("credential");
        startInfo.ArgumentList.Add("fill");

        using var process = new Process { StartInfo = startInfo };
        try
        {
            process.Start();
        }
        catch
        {
            return null;
        }

        var input = new StringBuilder()
            .AppendLine("protocol=https")
            .AppendLine($"host={pullRequest.Host}")
            .AppendLine($"path=scm/{pullRequest.ProjectKey.ToLowerInvariant()}/{pullRequest.RepoSlug}.git")
            .AppendLine()
            .ToString();

        try
        {
            await process.StandardInput.WriteAsync(input);
            process.StandardInput.Close();
        }
        catch
        {
            return null;
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
            return null;

        var output = await outputTask;
        _ = await errorTask;

        if (!TryParseCredentialOutput(output, out var username, out var password))
            return null;

        return (username, password);
    }

    private static bool TryParseCredentialOutput(string output, out string username, out string password)
    {
        username = null;
        password = null;
        if (string.IsNullOrWhiteSpace(output))
            return false;

        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var separatorIndex = rawLine.IndexOf('=');
            if (separatorIndex <= 0)
                continue;

            var key = rawLine[..separatorIndex].Trim();
            var value = rawLine[(separatorIndex + 1)..].Trim();

            if (key.Equals("username", StringComparison.OrdinalIgnoreCase))
                username = value;
            else if (key.Equals("password", StringComparison.OrdinalIgnoreCase))
                password = value;
        }

        return !string.IsNullOrWhiteSpace(username) && !string.IsNullOrWhiteSpace(password);
    }
}
