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
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ReviewG33k.Services;

/// <summary>
/// Resolves and ranks local-review base branch candidates for a Git repository.
/// </summary>
public sealed class LocalBaseBranchService
{
    private readonly GitCommandRunner m_gitCommandRunner;

    /// <summary>
    /// Creates a new service using the provided Git command runner.
    /// </summary>
    public LocalBaseBranchService(GitCommandRunner gitCommandRunner)
    {
        m_gitCommandRunner = gitCommandRunner ?? throw new ArgumentNullException(nameof(gitCommandRunner));
    }

    /// <summary>
    /// Tries to infer likely base-branch options from repository history.
    /// </summary>
    public async Task<List<string>> GetLocalBaseBranchOptionsAsync(string localRepositoryPath)
    {
        var branchOptions = new List<string>();
        var seenBranches = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddBranchOption(string branchName)
        {
            var normalizedBranchName = NormalizeBranchName(branchName);
            if (string.IsNullOrWhiteSpace(normalizedBranchName) || !seenBranches.Add(normalizedBranchName))
                return;

            branchOptions.Add(normalizedBranchName);
        }

        var defaultBranch = await TryGetDefaultRemoteBranchAsync(localRepositoryPath);
        var stopBranchCandidates = new List<string>();
        AddStopBranchCandidate(defaultBranch);
        AddStopBranchCandidate("main");
        AddStopBranchCandidate("master");

        var currentBranchResult = await m_gitCommandRunner.RunAsync(
            localRepositoryPath,
            "symbolic-ref",
            "--quiet",
            "--short",
            "HEAD");
        if (currentBranchResult.IsSuccess)
            AddBranchOption(currentBranchResult.StandardOutput);

        var stopBranchRef = await ResolveFirstExistingBranchReferenceAsync(localRepositoryPath, stopBranchCandidates);
        var stopCommitHash = await TryGetMergeBaseCommitAsync(localRepositoryPath, stopBranchRef);
        var branchTipsByCommit = await LoadBranchTipsByCommitAsync(localRepositoryPath);

        var historyArguments = string.IsNullOrWhiteSpace(stopCommitHash)
            ? new[] { "rev-list", "--first-parent", "HEAD" }
            : new[] { "rev-list", "--first-parent", $"{stopCommitHash}..HEAD" };
        var historyResult = await m_gitCommandRunner.RunAsync(localRepositoryPath, historyArguments);
        if (historyResult.IsSuccess)
        {
            foreach (var commitHash in ParseGitOutputLines(historyResult.StandardOutput))
            {
                if (branchTipsByCommit.TryGetValue(commitHash, out var branchesAtCommit))
                {
                    foreach (var branchAtCommit in branchesAtCommit)
                        AddBranchOption(branchAtCommit);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(stopCommitHash) &&
            branchTipsByCommit.TryGetValue(stopCommitHash, out var stopCommitBranches))
        {
            foreach (var stopCommitBranch in stopCommitBranches)
                AddBranchOption(stopCommitBranch);
        }

        var normalizedStopBranch = NormalizeBranchName(stopBranchRef);
        if (!string.IsNullOrWhiteSpace(normalizedStopBranch))
            AddBranchOption(normalizedStopBranch);
        else if (!string.IsNullOrWhiteSpace(defaultBranch))
            AddBranchOption(defaultBranch);

        return branchOptions;

        void AddStopBranchCandidate(string branchName)
        {
            var normalizedBranchName = NormalizeBranchName(branchName);
            if (string.IsNullOrWhiteSpace(normalizedBranchName))
                return;

            if (!stopBranchCandidates.Any(candidate => candidate.Equals(normalizedBranchName, StringComparison.OrdinalIgnoreCase)))
                stopBranchCandidates.Add(normalizedBranchName);
        }
    }

    /// <summary>
    /// Resolves the effective base branch, optionally logging when the requested branch is replaced.
    /// </summary>
    public async Task<string> ResolveLocalBaseBranchAsync(
        string localRepositoryPath,
        string requestedBaseBranch,
        bool logWhenChanged,
        Action<string> log)
    {
        if (!RepositoryUtilities.IsGitRepository(localRepositoryPath))
            return requestedBaseBranch?.Trim();

        var normalizedRequestedBranch = requestedBaseBranch?.Trim();
        var detectedDefaultBranch = await TryGetDefaultRemoteBranchAsync(localRepositoryPath);
        if (string.IsNullOrWhiteSpace(detectedDefaultBranch))
            return normalizedRequestedBranch;

        if (string.IsNullOrWhiteSpace(normalizedRequestedBranch))
        {
            if (logWhenChanged)
                log?.Invoke($"Detected default base branch: {detectedDefaultBranch}");
            return detectedDefaultBranch;
        }

        var hasRequestedBranch = await HasRemoteTrackingBranchAsync(localRepositoryPath, normalizedRequestedBranch) ||
                                 await HasLocalBranchAsync(localRepositoryPath, normalizedRequestedBranch);
        if (!hasRequestedBranch)
        {
            if (logWhenChanged)
                log?.Invoke($"Detected default base branch: {detectedDefaultBranch} (replacing '{normalizedRequestedBranch}')");
            return detectedDefaultBranch;
        }

        if (string.Equals(normalizedRequestedBranch, "main", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(normalizedRequestedBranch, detectedDefaultBranch, StringComparison.OrdinalIgnoreCase))
        {
            if (logWhenChanged)
                log?.Invoke($"Detected default base branch: {detectedDefaultBranch} (replacing '{normalizedRequestedBranch}')");
            return detectedDefaultBranch;
        }

        return normalizedRequestedBranch;
    }

    /// <summary>
    /// Normalizes refs and remote-qualified branch names to a plain branch identifier.
    /// </summary>
    public static string NormalizeBranchName(string branchName)
    {
        var normalized = branchName?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        while (true)
        {
            var updated = normalized;
            updated = StripPrefix(updated, "refs/heads/");
            updated = StripPrefix(updated, "refs/remotes/");
            updated = StripPrefix(updated, "heads/");
            updated = StripPrefix(updated, "remotes/");
            updated = StripPrefix(updated, "origin/");

            if (updated.Equals(normalized, StringComparison.Ordinal))
                break;

            normalized = updated;
        }

        return normalized.Equals("HEAD", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("origin", StringComparison.OrdinalIgnoreCase)
            ? null
            : normalized;

        static string StripPrefix(string text, string prefix) =>
            text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? text[prefix.Length..]
                : text;
    }

    private async Task<Dictionary<string, List<string>>> LoadBranchTipsByCommitAsync(string localRepositoryPath)
    {
        var branchTipsByCommit = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var refsResult = await m_gitCommandRunner.RunAsync(
            localRepositoryPath,
            "for-each-ref",
            "--format=%(objectname)%09%(refname:short)",
            "refs/heads",
            "refs/remotes/origin");
        if (!refsResult.IsSuccess)
            return branchTipsByCommit;

        foreach (var line in ParseGitOutputLines(refsResult.StandardOutput))
        {
            var separatorIndex = line.IndexOf('\t');
            if (separatorIndex <= 0 || separatorIndex >= line.Length - 1)
                continue;

            var commitHash = line[..separatorIndex].Trim();
            var branchName = NormalizeBranchName(line[(separatorIndex + 1)..]);
            if (string.IsNullOrWhiteSpace(commitHash) || string.IsNullOrWhiteSpace(branchName))
                continue;

            if (!branchTipsByCommit.TryGetValue(commitHash, out var branchesAtCommit))
            {
                branchesAtCommit = [];
                branchTipsByCommit[commitHash] = branchesAtCommit;
            }

            if (!branchesAtCommit.Any(candidate => candidate.Equals(branchName, StringComparison.OrdinalIgnoreCase)))
                branchesAtCommit.Add(branchName);
        }

        return branchTipsByCommit;
    }

    private async Task<string> ResolveFirstExistingBranchReferenceAsync(string localRepositoryPath, IReadOnlyList<string> branchNames)
    {
        foreach (var branchName in branchNames ?? [])
        {
            var branchReference = await ResolveBranchReferenceAsync(localRepositoryPath, branchName);
            if (!string.IsNullOrWhiteSpace(branchReference))
                return branchReference;
        }

        return null;
    }

    private async Task<string> ResolveBranchReferenceAsync(string localRepositoryPath, string branchName)
    {
        var normalizedBranchName = NormalizeBranchName(branchName);
        if (string.IsNullOrWhiteSpace(normalizedBranchName))
            return null;

        if (await HasRemoteTrackingBranchAsync(localRepositoryPath, normalizedBranchName))
            return $"origin/{normalizedBranchName}";
        if (await HasLocalBranchAsync(localRepositoryPath, normalizedBranchName))
            return normalizedBranchName;

        return null;
    }

    private async Task<string> TryGetMergeBaseCommitAsync(string localRepositoryPath, string branchReference)
    {
        if (string.IsNullOrWhiteSpace(branchReference))
            return null;

        var result = await m_gitCommandRunner.RunAsync(
            localRepositoryPath,
            "merge-base",
            "HEAD",
            branchReference);
        if (!result.IsSuccess)
            return null;

        return ParseGitOutputLines(result.StandardOutput).FirstOrDefault();
    }

    private async Task<string> TryGetDefaultRemoteBranchAsync(string localRepositoryPath)
    {
        if (!RepositoryUtilities.IsGitRepository(localRepositoryPath))
            return null;

        var symbolicRefResult = await m_gitCommandRunner.RunAsync(
            localRepositoryPath,
            "symbolic-ref",
            "--quiet",
            "--short",
            "refs/remotes/origin/HEAD");
        if (symbolicRefResult.IsSuccess)
        {
            var branch = ParseRemoteHeadBranch(symbolicRefResult.StandardOutput);
            if (!string.IsNullOrWhiteSpace(branch))
                return branch;
        }

        if (await HasRemoteTrackingBranchAsync(localRepositoryPath, "main"))
            return "main";
        if (await HasRemoteTrackingBranchAsync(localRepositoryPath, "master"))
            return "master";

        return null;
    }

    private async Task<bool> HasRemoteTrackingBranchAsync(string localRepositoryPath, string branchName)
    {
        if (string.IsNullOrWhiteSpace(branchName))
            return false;

        var result = await m_gitCommandRunner.RunAsync(
            localRepositoryPath,
            "show-ref",
            "--verify",
            "--quiet",
            $"refs/remotes/origin/{branchName}");
        return result.IsSuccess;
    }

    private async Task<bool> HasLocalBranchAsync(string localRepositoryPath, string branchName)
    {
        if (string.IsNullOrWhiteSpace(branchName))
            return false;

        var result = await m_gitCommandRunner.RunAsync(
            localRepositoryPath,
            "show-ref",
            "--verify",
            "--quiet",
            $"refs/heads/{branchName}");
        return result.IsSuccess;
    }

    private static List<string> ParseGitOutputLines(string output) =>
        (output ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

    private static string ParseRemoteHeadBranch(string rawOutput)
    {
        var normalized = rawOutput?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return null;

        const string longPrefix = "remotes/origin/";
        if (normalized.StartsWith(longPrefix, StringComparison.OrdinalIgnoreCase))
            return normalized[longPrefix.Length..];

        const string shortPrefix = "origin/";
        return normalized.StartsWith(shortPrefix, StringComparison.OrdinalIgnoreCase)
            ? normalized[shortPrefix.Length..]
            : normalized;
    }
}
