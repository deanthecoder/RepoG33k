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
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

/// <summary>
/// Detects newly added code blocks that exactly match an existing block elsewhere in the repository.
/// </summary>
/// <remarks>
/// This helps catch copy-pasted implementation chunks during change-based reviews without turning whole-repository scans
/// into a full clone-detection pass.
/// </remarks>
public sealed class DuplicateCodeBlockCodeReviewCheck : CodeReviewCheckBase
{
    private const int WindowSize = 6;
    private readonly GitCommandRunner m_gitCommandRunner;

    public DuplicateCodeBlockCodeReviewCheck(GitCommandRunner gitCommandRunner = null)
    {
        m_gitCommandRunner = gitCommandRunner;
    }

    public override string RuleId => CodeReviewRuleIds.DuplicateCodeBlock;

    public override string DisplayName => "Duplicated code blocks";

    public override CodeReviewCheckScope Scope => CodeReviewCheckScope.AddedLinesOnly;

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        if (context.IsEntireRepositoryScan)
            return;

        var candidateFiles = context.Files
            .Where(file => file != null)
            .Where(file => file.AddedLineNumbers.Count > 0)
            .Where(file => CodeReviewFileClassification.IsDuplicateCodeCheckPath(file.Path))
            .Where(file => !CodeReviewFileClassification.IsTestFilePath(file.Path))
            .ToArray();
        if (candidateFiles.Length == 0)
            return;

        foreach (var repositoryGroup in candidateFiles.GroupBy(GetRepositoryRootPath, StringComparer.OrdinalIgnoreCase))
        {
            var repositoryRootPath = repositoryGroup.Key;
            if (string.IsNullOrWhiteSpace(repositoryRootPath))
                continue;

            var repositoryWindowsByHash = BuildRepositoryWindowIndex(repositoryRootPath);
            if (repositoryWindowsByHash.Count == 0)
                continue;

            foreach (var changedFile in repositoryGroup)
                AnalyzeFile(changedFile, repositoryWindowsByHash, report);
        }
    }

    private static string GetRepositoryRootPath(CodeReviewChangedFile file) =>
        DuplicateCodeBlockUtilities.TryGetRepositoryRootPath(file, out var repositoryRootPath)
            ? repositoryRootPath
            : null;

    private Dictionary<ulong, List<CodeBlockWindow>> BuildRepositoryWindowIndex(string repositoryRootPath)
    {
        var repositoryFiles = DuplicateCodeBlockUtilities.EnumerateDuplicateCheckFiles(repositoryRootPath, m_gitCommandRunner);
        var windowsByHash = new Dictionary<ulong, List<CodeBlockWindow>>();
        foreach (var repositoryFile in repositoryFiles)
        {
            var relativePath = RepositoryUtilities.NormalizeRepoPath(Path.GetRelativePath(repositoryRootPath, repositoryFile.FullName));
            var normalizedFile = DuplicateCodeBlockUtilities.NormalizeCodeFile(repositoryFile, relativePath);
            foreach (var window in DuplicateCodeBlockUtilities.CreateWindows(normalizedFile, WindowSize))
            {
                if (!windowsByHash.TryGetValue(window.Hash, out var windows))
                {
                    windows = [];
                    windowsByHash[window.Hash] = windows;
                }

                windows.Add(window);
            }
        }

        return windowsByHash;
    }

    private void AnalyzeFile(
        CodeReviewChangedFile changedFile,
        IReadOnlyDictionary<ulong, List<CodeBlockWindow>> repositoryWindowsByHash,
        CodeSmellReport report)
    {
        var normalizedFile = DuplicateCodeBlockUtilities.NormalizeCodeFile(changedFile);
        var sourceWindows = DuplicateCodeBlockUtilities.CreateWindows(normalizedFile, WindowSize, changedFile.AddedLineNumbers);
        if (sourceWindows.Count == 0)
            return;

        var consumedStartIndexes = new HashSet<int>();
        for (var sourceIndex = 0; sourceIndex < sourceWindows.Count; sourceIndex++)
        {
            var sourceWindow = sourceWindows[sourceIndex];
            if (consumedStartIndexes.Contains(sourceWindow.StartNormalizedIndex))
                continue;
            if (!repositoryWindowsByHash.TryGetValue(sourceWindow.Hash, out var candidateWindows))
                continue;

            var match = candidateWindows.FirstOrDefault(candidate =>
                string.Equals(candidate.NormalizedText, sourceWindow.NormalizedText, StringComparison.Ordinal) &&
                !IsSelfWindow(changedFile, sourceWindow, candidate) &&
                !IsEntirelyAddedSameFileWindow(changedFile, candidate, normalizedFile));
            if (match == null)
                continue;

            var duplicateLineCount = ExtendDuplicateRun(
                sourceWindows,
                sourceIndex,
                match,
                repositoryWindowsByHash,
                consumedStartIndexes);
            AddFinding(
                report,
                CodeReviewFindingSeverity.Suggestion,
                changedFile.Path,
                sourceWindow.StartLineNumber,
                $"Added code block duplicates {duplicateLineCount} line(s) already present in `{match.RelativePath}:{match.StartLineNumber}`.");
        }
    }

    private static int ExtendDuplicateRun(
        IReadOnlyList<CodeBlockWindow> sourceWindows,
        int startSourceIndex,
        CodeBlockWindow targetWindow,
        IReadOnlyDictionary<ulong, List<CodeBlockWindow>> repositoryWindowsByHash,
        ISet<int> consumedStartIndexes)
    {
        var matchedWindowCount = 1;
        var finalWindow = sourceWindows[startSourceIndex];
        consumedStartIndexes.Add(finalWindow.StartNormalizedIndex);

        for (var sourceIndex = startSourceIndex + 1; sourceIndex < sourceWindows.Count; sourceIndex++)
        {
            var nextSourceWindow = sourceWindows[sourceIndex];
            if (nextSourceWindow.StartNormalizedIndex != finalWindow.StartNormalizedIndex + 1)
                break;

            if (!repositoryWindowsByHash.TryGetValue(nextSourceWindow.Hash, out var nextCandidateWindows))
                break;

            var matchingNextWindow = nextCandidateWindows.FirstOrDefault(candidate =>
                string.Equals(candidate.RelativePath, targetWindow.RelativePath, StringComparison.OrdinalIgnoreCase) &&
                candidate.StartNormalizedIndex == targetWindow.StartNormalizedIndex + (sourceIndex - startSourceIndex) &&
                candidate.Hash == nextSourceWindow.Hash &&
                string.Equals(candidate.NormalizedText, nextSourceWindow.NormalizedText, StringComparison.Ordinal));
            if (matchingNextWindow == null)
                break;

            consumedStartIndexes.Add(nextSourceWindow.StartNormalizedIndex);
            finalWindow = nextSourceWindow;
            matchedWindowCount++;
        }

        return WindowSize + matchedWindowCount - 1;
    }

    private static bool IsSelfWindow(CodeReviewChangedFile changedFile, CodeBlockWindow sourceWindow, CodeBlockWindow candidateWindow) =>
        string.Equals(candidateWindow.RelativePath, changedFile.Path, StringComparison.OrdinalIgnoreCase) &&
        candidateWindow.StartLineNumber == sourceWindow.StartLineNumber;

    private static bool IsEntirelyAddedSameFileWindow(
        CodeReviewChangedFile changedFile,
        CodeBlockWindow candidateWindow,
        NormalizedCodeFile normalizedFile)
    {
        if (!string.Equals(candidateWindow.RelativePath, changedFile.Path, StringComparison.OrdinalIgnoreCase))
            return false;

        var candidateLines = normalizedFile.Lines
            .Skip(candidateWindow.StartNormalizedIndex)
            .Take(WindowSize)
            .Select(line => line.OriginalLineNumber);
        return candidateLines.All(lineNumber => changedFile.AddedLineNumbers.Contains(lineNumber));
    }
}
