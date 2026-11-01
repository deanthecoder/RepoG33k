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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DTC.Core.Extensions;
using Support = ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services;

public sealed class GitBranchComparisonChangedFileSource : ICodeReviewChangedFileSource
{
    private readonly GitCommandRunner m_gitCommandRunner;
    private readonly string m_repositoryPath;
    private readonly string m_targetBranch;
    private readonly bool m_fetchTargetBranch;

    public GitBranchComparisonChangedFileSource(
        GitCommandRunner gitCommandRunner,
        string repositoryPath,
        string targetBranch,
        bool fetchTargetBranch = true)
    {
        m_gitCommandRunner = gitCommandRunner ?? throw new ArgumentNullException(nameof(gitCommandRunner));
        m_repositoryPath = repositoryPath ?? string.Empty;
        m_targetBranch = targetBranch ?? string.Empty;
        m_fetchTargetBranch = fetchTargetBranch;
    }

    public async Task<CodeReviewChangedFileSourceResult> LoadAsync(Action<string> progressLogger = null)
    {
        var info = new List<string>();
        var repositoryPathInfo = m_repositoryPath.ToDir();

        if (string.IsNullOrWhiteSpace(m_repositoryPath) || !repositoryPathInfo.Exists())
        {
            info.Add("Code review scan skipped: Review worktree path not found.");
            return new CodeReviewChangedFileSourceResult([], info);
        }

        progressLogger?.Invoke("Code review scan: Resolving target branch...");
        var targetBranch = await ResolveTargetBranchAsync(info);
        if (string.IsNullOrWhiteSpace(targetBranch))
        {
            info.Add("Code review scan skipped: Target branch unavailable from PR metadata.");
            return new CodeReviewChangedFileSourceResult([], info);
        }

        var remoteBaseRef = $"origin/{targetBranch}";
        var remoteBaseRefName = $"refs/remotes/{remoteBaseRef}";
        var localBaseRefName = $"refs/heads/{targetBranch}";
        if (m_fetchTargetBranch)
        {
            progressLogger?.Invoke($"Code review scan: Fetching target branch '{targetBranch}'...");
            var fetchTargetResult = await m_gitCommandRunner.RunAsync(
                m_repositoryPath,
                "fetch",
                "--prune",
                "origin",
                $"+refs/heads/{targetBranch}:{remoteBaseRefName}");
            if (!fetchTargetResult.IsSuccess)
            {
                info.Add($"Code review scan: Unable to fetch target branch '{targetBranch}' from origin. Trying local branch.");
            }
        }

        var hasRemoteBaseRef = await RefExistsAsync(remoteBaseRefName);
        var hasLocalBaseRef = await RefExistsAsync(localBaseRefName);
        // Use fully qualified refs for every Git operation. A local branch named, for example,
        // "origin/master" otherwise takes precedence over the remote-tracking ref of the same name.
        var baseRef = hasRemoteBaseRef
            ? remoteBaseRefName
            : hasLocalBaseRef
                ? localBaseRefName
                : null;
        var baseRefDisplayName = hasRemoteBaseRef
            ? remoteBaseRef
            : targetBranch;
        if (baseRef == null)
        {
            info.Add($"Code review scan skipped: Target branch '{targetBranch}' was not found on origin or as a local branch.");
            return new CodeReviewChangedFileSourceResult([], info);
        }

        if (!hasRemoteBaseRef && hasLocalBaseRef)
            info.Add($"Code review scan: Using local target branch '{targetBranch}' because origin/{targetBranch} was not available.");

        var diffRange = $"{baseRef}...HEAD";
        var comparisonBaseRef = await ResolveMergeBaseAsync(baseRef) ?? baseRef;
        progressLogger?.Invoke($"Code review scan: Enumerating files changed since {baseRefDisplayName}...");
        var nameStatusResult = await m_gitCommandRunner.RunAsync(
            m_repositoryPath,
            "diff",
            "--name-status",
            "--ignore-cr-at-eol",
            "--ignore-space-at-eol",
            "--find-renames",
            diffRange);
        if (!nameStatusResult.IsSuccess)
        {
            info.Add("Code review scan skipped: Unable to enumerate diff file status.");
            return new CodeReviewChangedFileSourceResult([], info);
        }

        var baseDiffEntries = ParseNameStatusOutput(nameStatusResult.StandardOutput).ToArray();
        var workingTreeEntries = await LoadWorkingTreeChangedFileEntriesAsync();
        var allChangedFileEntries = MergeChangedFileEntries(baseDiffEntries, workingTreeEntries).ToArray();
        var baseDiffPaths = new HashSet<string>(
            baseDiffEntries.Select(entry => RepositoryUtilities.NormalizeRepoPath(entry.Path)),
            StringComparer.OrdinalIgnoreCase);

        if (allChangedFileEntries.Length == 0)
        {
            info.Add($"Code review scan: No differences between HEAD and {baseRefDisplayName}.");
            return new CodeReviewChangedFileSourceResult([], info);
        }

        var changedFileEntries = allChangedFileEntries
            .Where(entry => Support.CodeReviewFileClassification.IsAnalyzableChangedPath(entry.Path))
            .ToArray();

        if (changedFileEntries.Length == 0)
        {
            info.Add("Code review scan: No changed analyzable files detected.");
            return new CodeReviewChangedFileSourceResult([], info);
        }

        progressLogger?.Invoke($"Code review scan: Loading {changedFileEntries.Length} changed file(s)...");
        var changedFiles = new List<CodeReviewChangedFile>(changedFileEntries.Length);
        for (var index = 0; index < changedFileEntries.Length; index++)
        {
            var entry = changedFileEntries[index];
            var fullPath = repositoryPathInfo.GetFile(entry.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!fullPath.Exists())
                continue;

            var baselineRevision = entry.Status.Equals("A", StringComparison.OrdinalIgnoreCase)
                ? null
                : baseDiffPaths.Contains(RepositoryUtilities.NormalizeRepoPath(entry.Path))
                    ? comparisonBaseRef
                    : "HEAD";
            var currentRevision = baseDiffPaths.Contains(RepositoryUtilities.NormalizeRepoPath(entry.Path))
                ? "HEAD"
                : null;
            var content = await GitChangedFileContent.LoadAsync(
                m_gitCommandRunner,
                m_repositoryPath,
                fullPath,
                baselineRevision,
                entry.Path,
                currentRevision);
            var lines = SplitLines(content.Text);
            HashSet<int> addedLineNumbers;
            if (baseDiffPaths.Contains(RepositoryUtilities.NormalizeRepoPath(entry.Path)))
                addedLineNumbers = await GetAddedLineNumbersAsync(diffRange, entry.Path);
            else if (entry.Status.Equals("A", StringComparison.OrdinalIgnoreCase))
                addedLineNumbers = new HashSet<int>(Enumerable.Range(1, lines.Count));
            else
                addedLineNumbers = await GetAddedLineNumbersAsync("HEAD", entry.Path);

            changedFiles.Add(new CodeReviewChangedFile(
                entry.Status,
                entry.Path,
                fullPath.FullName,
                content.Text,
                lines,
                addedLineNumbers,
                content.BaselineText,
                content.Bytes,
                content.BaselineBytes));

            var filesProcessed = index + 1;
            if (ShouldLogFileProgress(filesProcessed, changedFileEntries.Length))
            {
                progressLogger?.Invoke(
                    $"Code review scan: Loaded {filesProcessed}/{changedFileEntries.Length} changed file(s)...");
            }
        }

        if (changedFiles.Count == 0)
        {
            info.Add("Code review scan: No analyzable changed files found.");
            return new CodeReviewChangedFileSourceResult([], info);
        }

        info.Add($"Code review scan: Analyzing {changedFiles.Count} changed analyzable file(s).");
        return new CodeReviewChangedFileSourceResult(changedFiles, info);
    }

    private async Task<string> ResolveTargetBranchAsync(ICollection<string> info)
    {
        if (!string.IsNullOrWhiteSpace(m_targetBranch))
            return m_targetBranch.Trim();

        var originHeadBranch = await TryGetOriginHeadBranchNameAsync();
        if (!string.IsNullOrWhiteSpace(originHeadBranch))
        {
            info?.Add($"Code review scan: Target branch unavailable from PR metadata. Falling back to origin HEAD '{originHeadBranch}'.");
            return originHeadBranch;
        }

        foreach (var fallbackBranch in new[] { "main", "master" })
        {
            if (await RefExistsAsync($"refs/remotes/origin/{fallbackBranch}") ||
                await RefExistsAsync($"refs/heads/{fallbackBranch}"))
            {
                info?.Add($"Code review scan: Target branch unavailable from PR metadata. Falling back to '{fallbackBranch}'.");
                return fallbackBranch;
            }
        }

        return null;
    }

    private async Task<string> ResolveMergeBaseAsync(string baseRef)
    {
        if (string.IsNullOrWhiteSpace(baseRef))
            return null;

        var result = await m_gitCommandRunner.RunAsync(m_repositoryPath, "merge-base", baseRef, "HEAD");
        if (!result.IsSuccess)
            return null;

        return NormalizeCommitHash(result.StandardOutput);
    }

    private async Task<string> TryGetOriginHeadBranchNameAsync()
    {
        var symbolicRefResult = await m_gitCommandRunner.RunAsync(
            m_repositoryPath,
            "symbolic-ref",
            "--quiet",
            "--short",
            "refs/remotes/origin/HEAD");
        if (symbolicRefResult.IsSuccess)
        {
            var rawRef = (symbolicRefResult.StandardOutput ?? string.Empty).Trim();
            const string originPrefix = "origin/";
            if (rawRef.StartsWith(originPrefix, StringComparison.OrdinalIgnoreCase))
                return rawRef[originPrefix.Length..];
        }

        var remoteShowResult = await m_gitCommandRunner.RunAsync(
            m_repositoryPath,
            "remote",
            "show",
            "origin");
        if (!remoteShowResult.IsSuccess || string.IsNullOrWhiteSpace(remoteShowResult.StandardOutput))
            return null;

        foreach (var line in remoteShowResult.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            const string marker = "HEAD branch:";
            var markerIndex = line.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
                continue;

            var branchName = line[(markerIndex + marker.Length)..].Trim();
            if (!string.IsNullOrWhiteSpace(branchName) &&
                !branchName.Equals("(unknown)", StringComparison.OrdinalIgnoreCase))
            {
                return branchName;
            }
        }

        return null;
    }

    private async Task<HashSet<int>> GetAddedLineNumbersAsync(string diffRange, string relativePath)
    {
        var result = await m_gitCommandRunner.RunAsync(
            m_repositoryPath,
            "diff",
            "--unified=0",
            "--no-color",
            "--ignore-cr-at-eol",
            "--ignore-space-at-eol",
            diffRange,
            "--",
            relativePath);
        if (!result.IsSuccess || string.IsNullOrWhiteSpace(result.StandardOutput))
            return [];

        return ParseAddedLineNumbers(result.StandardOutput);
    }

    private async Task<bool> RefExistsAsync(string referenceName)
    {
        if (string.IsNullOrWhiteSpace(referenceName))
            return false;

        var result = await m_gitCommandRunner.RunAsync(
            m_repositoryPath,
            "show-ref",
            "--verify",
            "--quiet",
            referenceName);
        return result.IsSuccess;
    }

    private static HashSet<int> ParseAddedLineNumbers(string diffText)
    {
        var addedLines = new HashSet<int>();
        var lines = diffText.Replace("\r\n", "\n").Split('\n');

        var currentNewLine = 0;
        foreach (var line in lines)
        {
            if (line.StartsWith("@@ ", StringComparison.Ordinal))
            {
                if (TryParseHunkStart(line, out var newStart))
                    currentNewLine = newStart;
                continue;
            }

            if (line.StartsWith('+') && !line.StartsWith("+++", StringComparison.Ordinal))
            {
                addedLines.Add(currentNewLine);
                currentNewLine++;
                continue;
            }

            if (line.StartsWith('-') && !line.StartsWith("---", StringComparison.Ordinal))
                continue;

            if (line.StartsWith(' '))
                currentNewLine++;
        }

        return addedLines;
    }

    private static bool TryParseHunkStart(string hunkHeader, out int newStart)
    {
        newStart = 0;
        var plusIndex = hunkHeader.IndexOf('+');
        if (plusIndex < 0)
            return false;

        var commaIndex = hunkHeader.IndexOf(',', plusIndex);
        var spaceIndex = hunkHeader.IndexOf(' ', plusIndex);
        var endIndex = commaIndex >= 0 ? commaIndex : spaceIndex;
        if (endIndex < 0)
            return false;

        var numberText = hunkHeader[(plusIndex + 1)..endIndex];
        return int.TryParse(numberText, out newStart);
    }

    private static IReadOnlyList<(string Status, string Path)> ParseNameStatusOutput(string output)
    {
        var entries = new List<(string Status, string Path)>();
        if (string.IsNullOrWhiteSpace(output))
            return entries;

        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = rawLine.Split('\t');
            if (parts.Length < 2)
                continue;

            var status = parts[0].Trim();
            if (string.IsNullOrWhiteSpace(status))
                continue;

            var normalizedStatus = status[..1];
            var path = normalizedStatus.Equals("R", StringComparison.OrdinalIgnoreCase) && parts.Length >= 3
                ? parts[2]
                : parts[1];

            if (!string.IsNullOrWhiteSpace(path))
                entries.Add((normalizedStatus, path));
        }

        return entries;
    }

    private async Task<IReadOnlyList<(string Status, string Path)>> LoadWorkingTreeChangedFileEntriesAsync()
    {
        var entries = new List<(string Status, string Path)>();

        var trackedChangesResult = await m_gitCommandRunner.RunAsync(
            m_repositoryPath,
            "diff",
            "--name-status",
            "--find-renames",
            "HEAD");
        if (trackedChangesResult.IsSuccess)
            entries.AddRange(ParseNameStatusOutput(trackedChangesResult.StandardOutput));

        var untrackedResult = await m_gitCommandRunner.RunAsync(
            m_repositoryPath,
            "ls-files",
            "--others",
            "--exclude-standard");
        if (untrackedResult.IsSuccess)
            entries.AddRange(ParseUntrackedOutput(untrackedResult.StandardOutput));

        return entries;
    }

    private static IReadOnlyList<(string Status, string Path)> ParseUntrackedOutput(string output)
    {
        var entries = new List<(string Status, string Path)>();
        if (string.IsNullOrWhiteSpace(output))
            return entries;

        foreach (var rawLine in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var path = rawLine.Trim();
            if (!string.IsNullOrWhiteSpace(path))
                entries.Add(("A", path));
        }

        return entries;
    }

    private static IReadOnlyList<(string Status, string Path)> MergeChangedFileEntries(
        IReadOnlyList<(string Status, string Path)> baseEntries,
        IReadOnlyList<(string Status, string Path)> additionalEntries)
    {
        var merged = new Dictionary<string, (string Status, string Path)>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in baseEntries ?? [])
        {
            if (string.IsNullOrWhiteSpace(entry.Path))
                continue;

            merged[RepositoryUtilities.NormalizeRepoPath(entry.Path)] = entry;
        }

        foreach (var entry in additionalEntries ?? [])
        {
            if (string.IsNullOrWhiteSpace(entry.Path))
                continue;

            var key = RepositoryUtilities.NormalizeRepoPath(entry.Path);
            if (!merged.ContainsKey(key))
                merged[key] = entry;
        }

        return merged.Values.ToArray();
    }

    private static IReadOnlyList<string> SplitLines(string text) =>
        (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private static string NormalizeCommitHash(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var firstLine = value
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;
        return firstLine.Trim();
    }

    private static bool ShouldLogFileProgress(int filesProcessed, int totalFiles) =>
        filesProcessed == 1 || filesProcessed == totalFiles || filesProcessed % 25 == 0;
}
