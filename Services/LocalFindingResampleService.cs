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

namespace ReviewG33k.Services;

internal enum LocalReviewResampleMode
{
    Committed = 1,
    Uncommitted
}

internal sealed class LocalFindingResampleService
{
    private readonly GitCommandRunner m_gitCommandRunner;
    private readonly CodeSmellReportAnalyzer m_codeSmellReportAnalyzer;
    private readonly LocalReviewChangedFilesCache m_changedFilesCache;

    public LocalFindingResampleService(
        GitCommandRunner gitCommandRunner,
        CodeSmellReportAnalyzer codeSmellReportAnalyzer,
        LocalReviewChangedFilesCache changedFilesCache = null)
    {
        m_gitCommandRunner = gitCommandRunner ?? throw new ArgumentNullException(nameof(gitCommandRunner));
        m_codeSmellReportAnalyzer = codeSmellReportAnalyzer ?? throw new ArgumentNullException(nameof(codeSmellReportAnalyzer));
        m_changedFilesCache = changedFilesCache ?? new LocalReviewChangedFilesCache();
    }

    public void SetCachedFiles(
        string localRepositoryPath,
        string baseBranch,
        LocalReviewResampleMode reviewMode,
        IReadOnlyList<CodeReviewChangedFile> files) =>
        m_changedFilesCache.Set(localRepositoryPath, baseBranch, (int)reviewMode, files);

    public void InvalidateCache() =>
        m_changedFilesCache.Invalidate();

    public async Task<IReadOnlyList<CodeSmellFinding>> ResampleFindingsForFileAsync(
        string filePath,
        string localRepositoryPath,
        string baseBranch,
        LocalReviewResampleMode reviewMode,
        bool includeFullModifiedFiles,
        Action<string> appendLog)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return [];

        if (string.IsNullOrWhiteSpace(localRepositoryPath) || !localRepositoryPath.ToDir().Exists())
            return [];

        if (reviewMode == LocalReviewResampleMode.Committed && string.IsNullOrWhiteSpace(baseBranch))
            return [];

        var changedFilesByPath = await GetOrCreateChangedFilesAsync(localRepositoryPath, baseBranch, reviewMode, appendLog);
        if (changedFilesByPath == null || changedFilesByPath.Count == 0)
            return [];

        if (!changedFilesByPath.TryGetValue(RepositoryUtilities.NormalizeRepoPath(filePath), out var targetFile) || targetFile == null)
            return [];

        var refreshedTargetFile = await RefreshChangedFileFromDiskAsync(targetFile);
        if (refreshedTargetFile == null)
            return [];

        changedFilesByPath[RepositoryUtilities.NormalizeRepoPath(refreshedTargetFile.Path)] = refreshedTargetFile;

        var report = m_codeSmellReportAnalyzer.AnalyzeFiles([refreshedTargetFile], includeFullModifiedFiles);
        return report.Findings
            .Where(finding => finding != null)
            .Where(finding => RepositoryUtilities.AreSameRepoPath(finding.FilePath, filePath))
            .OrderBy(finding => finding.LineNumber)
            .ToArray();
    }

    private async Task<Dictionary<string, CodeReviewChangedFile>> GetOrCreateChangedFilesAsync(
        string localRepositoryPath,
        string baseBranch,
        LocalReviewResampleMode reviewMode,
        Action<string> appendLog)
    {
        if (!m_changedFilesCache.TryGet(localRepositoryPath, baseBranch, (int)reviewMode, out var changedFilesByPath))
        {
            ICodeReviewChangedFileSource changedFileSource =
                reviewMode == LocalReviewResampleMode.Committed
                    ? new GitBranchComparisonChangedFileSource(
                        m_gitCommandRunner,
                        localRepositoryPath,
                        baseBranch,
                        fetchTargetBranch: false)
                    : new GitWorkingTreeChangedFileSource(
                        m_gitCommandRunner,
                        localRepositoryPath);

            var sourceResult = await changedFileSource.LoadAsync(appendLog);
            m_changedFilesCache.Set(localRepositoryPath, baseBranch, (int)reviewMode, sourceResult?.Files);
            _ = m_changedFilesCache.TryGet(localRepositoryPath, baseBranch, (int)reviewMode, out changedFilesByPath);
        }

        return changedFilesByPath;
    }

    private static async Task<CodeReviewChangedFile> RefreshChangedFileFromDiskAsync(CodeReviewChangedFile sourceFile)
    {
        if (sourceFile == null || string.IsNullOrWhiteSpace(sourceFile.FullPath))
            return null;

        var fullPath = sourceFile.FullPath.ToFile();
        if (!fullPath.Exists())
            return null;

        var text = await File.ReadAllTextAsync(fullPath.FullName);
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        return new CodeReviewChangedFile(
            sourceFile.Status,
            sourceFile.Path,
            sourceFile.FullPath,
            text,
            lines,
            sourceFile.AddedLineNumbers);
    }
}
