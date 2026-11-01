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

internal sealed class GitSingleCommitChangedFileSource : ICodeReviewChangedFileSource
{
    private readonly GitCommandRunner m_gitCommandRunner;
    private readonly string m_repositoryPath;
    private readonly string m_commitHash;

    internal GitSingleCommitChangedFileSource(
        GitCommandRunner gitCommandRunner,
        string repositoryPath,
        string commitHash)
    {
        m_gitCommandRunner = gitCommandRunner ?? throw new ArgumentNullException(nameof(gitCommandRunner));
        m_repositoryPath = repositoryPath ?? string.Empty;
        m_commitHash = commitHash?.Trim() ?? string.Empty;
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

        if (string.IsNullOrWhiteSpace(m_commitHash))
        {
            info.Add("Code review scan skipped: Merge commit hash unavailable.");
            return new CodeReviewChangedFileSourceResult([], info);
        }

        var parentCommitResult = await m_gitCommandRunner.RunAsync(
            m_repositoryPath,
            "rev-parse",
            "--verify",
            $"{m_commitHash}^");
        if (!parentCommitResult.IsSuccess)
        {
            info.Add("Code review scan skipped: Merge commit parent could not be resolved.");
            return new CodeReviewChangedFileSourceResult([], info);
        }

        var parentCommitHash = NormalizeCommitHash(parentCommitResult.StandardOutput);
        if (string.IsNullOrWhiteSpace(parentCommitHash))
        {
            info.Add("Code review scan skipped: Merge commit parent could not be resolved.");
            return new CodeReviewChangedFileSourceResult([], info);
        }

        var diffRange = $"{parentCommitHash}..{m_commitHash}";
        progressLogger?.Invoke("Code review scan: Enumerating files changed in merge commit...");
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
            info.Add("Code review scan skipped: Unable to enumerate merge-commit file status.");
            return new CodeReviewChangedFileSourceResult([], info);
        }

        var changedFileEntries = ParseNameStatusOutput(nameStatusResult.StandardOutput)
            .Where(entry => Support.CodeReviewFileClassification.IsAnalyzableChangedPath(entry.Path))
            .ToArray();
        if (changedFileEntries.Length == 0)
        {
            info.Add("Code review scan: No merge-commit analyzable files detected.");
            return new CodeReviewChangedFileSourceResult([], info);
        }

        progressLogger?.Invoke($"Code review scan: Loading {changedFileEntries.Length} merge-commit file(s)...");
        var changedFiles = new List<CodeReviewChangedFile>(changedFileEntries.Length);
        for (var index = 0; index < changedFileEntries.Length; index++)
        {
            var entry = changedFileEntries[index];
            if (entry.Status.Equals("D", StringComparison.OrdinalIgnoreCase))
                continue;

            var fullPath = repositoryPathInfo.GetFile(entry.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!fullPath.Exists())
                continue;

            var baselineRevision = entry.Status.Equals("A", StringComparison.OrdinalIgnoreCase) ? null : parentCommitHash;
            var content = await GitChangedFileContent.LoadAsync(
                m_gitCommandRunner,
                m_repositoryPath,
                fullPath,
                baselineRevision,
                entry.Path,
                m_commitHash);
            var lines = SplitLines(content.Text);
            var addedLineNumbers = entry.Status.Equals("A", StringComparison.OrdinalIgnoreCase)
                ? new HashSet<int>(Enumerable.Range(1, lines.Count))
                : await GetAddedLineNumbersAsync(diffRange, entry.Path);

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
                    $"Code review scan: Loaded {filesProcessed}/{changedFileEntries.Length} merge-commit file(s)...");
            }
        }

        if (changedFiles.Count == 0)
        {
            info.Add("Code review scan: No analyzable merge-commit files found.");
            return new CodeReviewChangedFileSourceResult([], info);
        }

        info.Add($"Code review scan: Analyzing {changedFiles.Count} merge-commit analyzable file(s).");
        return new CodeReviewChangedFileSourceResult(changedFiles, info);
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

    private static string NormalizeCommitHash(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var firstLine = value
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;
        return firstLine.Trim();
    }

    private static IReadOnlyList<string> SplitLines(string text) =>
        (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private static bool ShouldLogFileProgress(int filesProcessed, int totalFiles) =>
        filesProcessed == 1 || filesProcessed == totalFiles || filesProcessed % 25 == 0;
}
