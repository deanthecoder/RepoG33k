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

/// <summary>
/// Loads analyzable files from an entire local repository for a whole-repository review pass.
/// </summary>
/// <remarks>
/// Useful for review modes that intentionally scan all supported files in a repository instead of
/// limiting analysis to changed files relative to Git history or the working tree.
/// </remarks>
public sealed class LocalRepositoryChangedFileSource : ICodeReviewChangedFileSource
{
    private static readonly string[] SearchPatterns = ["*.cs", "*.csproj", "*.axaml", "*.xaml", "*.resx"];
    private readonly GitCommandRunner m_gitCommandRunner;
    private readonly string m_repositoryPath;

    public LocalRepositoryChangedFileSource(string repositoryPath, GitCommandRunner gitCommandRunner = null)
    {
        m_repositoryPath = repositoryPath ?? string.Empty;
        m_gitCommandRunner = gitCommandRunner;
    }

    public async Task<CodeReviewChangedFileSourceResult> LoadAsync(Action<string> progressLogger = null)
    {
        var info = new List<string>();
        var repositoryPathInfo = m_repositoryPath.ToDir();

        if (string.IsNullOrWhiteSpace(m_repositoryPath) || !repositoryPathInfo.Exists())
        {
            info.Add("Code review scan skipped: Local repository path not found.");
            return new CodeReviewChangedFileSourceResult([], info, isEntireRepositoryScan: true);
        }

        progressLogger?.Invoke("Code review scan: Enumerating repository files...");
        var analyzableFiles = await EnumerateAnalyzableFilesAsync(repositoryPathInfo);

        if (analyzableFiles.Length == 0)
        {
            info.Add("Code review scan: No analyzable repository files detected.");
            return new CodeReviewChangedFileSourceResult([], info, isEntireRepositoryScan: true);
        }

        progressLogger?.Invoke($"Code review scan: Loading {analyzableFiles.Length} repository file(s)...");
        var changedFiles = new List<CodeReviewChangedFile>(analyzableFiles.Length);
        for (var index = 0; index < analyzableFiles.Length; index++)
        {
            var file = analyzableFiles[index];
            var text = await File.ReadAllTextAsync(file.FullName);
            var lines = SplitLines(text);
            var relativePath = RepositoryUtilities.NormalizeRepoPath(Path.GetRelativePath(repositoryPathInfo.FullName, file.FullName));
            var allLineNumbers = new HashSet<int>(Enumerable.Range(1, lines.Count));
            changedFiles.Add(new CodeReviewChangedFile("M", relativePath, file.FullName, text, lines, allLineNumbers));

            var filesProcessed = index + 1;
            if (ShouldLogFileProgress(filesProcessed, analyzableFiles.Length))
            {
                progressLogger?.Invoke(
                    $"Code review scan: Loaded {filesProcessed}/{analyzableFiles.Length} repository file(s)...");
            }
        }

        info.Add($"Code review scan: Analyzing {changedFiles.Count} analyzable file(s) from the local repository.");
        return new CodeReviewChangedFileSourceResult(changedFiles, info, isEntireRepositoryScan: true);
    }

    private static IReadOnlyList<string> SplitLines(string text) =>
        (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    internal async Task<FileInfo[]> EnumerateAnalyzableFilesAsync(DirectoryInfo repositoryPathInfo)
    {
        if (m_gitCommandRunner != null && RepositoryUtilities.IsGitRepository(repositoryPathInfo.FullName))
        {
            var gitFiles = await TryEnumerateAnalyzableGitFilesAsync(repositoryPathInfo.FullName);
            if (gitFiles != null)
                return gitFiles;
        }

        return await Task.Run(() => EnumerateAnalyzableFiles(repositoryPathInfo));
    }

    private async Task<FileInfo[]> TryEnumerateAnalyzableGitFilesAsync(string repositoryPath)
    {
        var result = await m_gitCommandRunner.RunAsync(
            repositoryPath,
            "ls-files",
            "--cached",
            "--others",
            "--exclude-standard");
        if (!result.IsSuccess)
            return null;

        var repositoryPathInfo = repositoryPath.ToDir();
        return (result.StandardOutput ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(RepositoryUtilities.NormalizeRepoPath)
            .Where(Support.CodeReviewFileClassification.IsAnalyzableChangedPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(relativePath => repositoryPathInfo.GetFile(relativePath.Replace('/', Path.DirectorySeparatorChar)))
            .Where(file => file.Exists())
            .OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static FileInfo[] EnumerateAnalyzableFiles(DirectoryInfo repositoryPathInfo)
    {
        var pendingDirectories = new Stack<DirectoryInfo>();
        pendingDirectories.Push(repositoryPathInfo);

        var files = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
        while (pendingDirectories.Count > 0)
        {
            var directory = pendingDirectories.Pop();
            foreach (var file in EnumerateMatchingFiles(directory))
                files[file.FullName] = file;

            foreach (var childDirectory in EnumerateChildDirectories(repositoryPathInfo, directory))
                pendingDirectories.Push(childDirectory);
        }

        return files.Values
            .OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<FileInfo> EnumerateMatchingFiles(DirectoryInfo directory)
    {
        foreach (var searchPattern in SearchPatterns)
        {
            IEnumerable<FileInfo> matches;
            try
            {
                matches = directory.EnumerateFiles(searchPattern, SearchOption.TopDirectoryOnly);
            }
            catch
            {
                continue;
            }

            foreach (var file in matches)
            {
                if (file.Exists())
                    yield return file;
            }
        }
    }

    private static IEnumerable<DirectoryInfo> EnumerateChildDirectories(DirectoryInfo repositoryRoot, DirectoryInfo directory)
    {
        IEnumerable<DirectoryInfo> children;
        try
        {
            children = directory.EnumerateDirectories("*", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            yield break;
        }

        foreach (var childDirectory in children)
        {
            var relativeDirectoryPath = RepositoryUtilities.NormalizeRepoPath(
                Path.GetRelativePath(repositoryRoot.FullName, childDirectory.FullName));
            if (!Support.CodeReviewFileClassification.IsIgnoredPath(relativeDirectoryPath))
                yield return childDirectory;
        }
    }

    private static bool ShouldLogFileProgress(int filesProcessed, int totalFiles) =>
        filesProcessed == totalFiles ||
        filesProcessed <= 5 ||
        filesProcessed % 25 == 0;
}
