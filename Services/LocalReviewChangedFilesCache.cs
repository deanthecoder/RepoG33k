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

namespace ReviewG33k.Services;

internal sealed class LocalReviewChangedFilesCache
{
    private Dictionary<string, CodeReviewChangedFile> m_changedFilesByPath;
    private string m_repositoryPath;
    private string m_baseBranch;
    private int? m_reviewModeIndex;

    public bool IsValid(string localRepositoryPath, string baseBranch, int reviewModeIndex)
    {
        if (m_changedFilesByPath == null)
            return false;

        var normalizedRepositoryPath = RepositoryUtilities.NormalizeRepoPath(localRepositoryPath);
        if (!string.Equals(m_repositoryPath, normalizedRepositoryPath, StringComparison.OrdinalIgnoreCase))
            return false;

        var normalizedBaseBranch = NormalizeBaseBranch(baseBranch);
        if (!string.Equals(m_baseBranch, normalizedBaseBranch, StringComparison.OrdinalIgnoreCase))
            return false;

        return m_reviewModeIndex == reviewModeIndex;
    }

    public bool TryGet(string localRepositoryPath, string baseBranch, int reviewModeIndex, out Dictionary<string, CodeReviewChangedFile> changedFilesByPath)
    {
        if (!IsValid(localRepositoryPath, baseBranch, reviewModeIndex))
        {
            changedFilesByPath = null;
            return false;
        }

        changedFilesByPath = m_changedFilesByPath;
        return true;
    }

    public void Set(string localRepositoryPath, string baseBranch, int reviewModeIndex, IReadOnlyList<CodeReviewChangedFile> files)
    {
        var normalizedRepositoryPath = RepositoryUtilities.NormalizeRepoPath(localRepositoryPath);
        var normalizedBaseBranch = NormalizeBaseBranch(baseBranch);

        m_repositoryPath = normalizedRepositoryPath;
        m_baseBranch = normalizedBaseBranch;
        m_reviewModeIndex = reviewModeIndex;
        m_changedFilesByPath = (files ?? [])
            .Where(file => file != null && !string.IsNullOrWhiteSpace(file.Path))
            .GroupBy(file => RepositoryUtilities.NormalizeRepoPath(file.Path), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToDictionary(file => RepositoryUtilities.NormalizeRepoPath(file.Path), file => file, StringComparer.OrdinalIgnoreCase);
    }

    public void Invalidate()
    {
        m_changedFilesByPath = null;
        m_repositoryPath = null;
        m_baseBranch = null;
        m_reviewModeIndex = null;
    }

    private static string NormalizeBaseBranch(string baseBranch) =>
        string.IsNullOrWhiteSpace(baseBranch) ? null : baseBranch.Trim();
}
