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
using DTC.Core.Extensions;

namespace ReviewG33k.Services.Checks.Support;

/// <summary>
/// Finds repository test sources without traversing generated or linked directory trees.
/// </summary>
/// <remarks>
/// Both missing-test checks use Git's file list when possible, with a pruned folder search
/// for non-Git folders or unavailable Git installations.
/// </remarks>
internal static class RepositoryTestFileSearch
{
    public static IReadOnlyList<FileInfo> Enumerate(string repositoryRootPath)
    {
        var root = repositoryRootPath.ToDir();
        if (root?.Exists() != true)
            return [];

        if (RepositoryUtilities.IsGitRepository(repositoryRootPath))
        {
            try
            {
                var result = new GitCommandRunner().RunAsync(root.FullName,
                    "ls-files", "--cached", "--others", "--exclude-standard", "-z").GetAwaiter().GetResult();
                if (result.IsSuccess)
                {
                    return result.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries)
                        .Where(IsTestSource)
                        .Distinct(StringComparer.Ordinal)
                        .Select(path => root.GetFile(path))
                        .Where(file => file.Exists() && !file.Attributes.HasFlag(FileAttributes.ReparsePoint))
                        .ToArray();
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
            {
                // Fall back to the same pruned folder traversal used outside Git repositories.
            }
        }

        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            RecurseSubdirectories = false
        };
        var pending = new Stack<DirectoryInfo>();
        pending.Push(root);
        var files = new List<FileInfo>();
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            FileSystemInfo[] entries;
            try
            {
                entries = directory.EnumerateFileSystemInfos("*", options).ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                var relative = Path.GetRelativePath(root.FullName, entry.FullName);
                if (CodeReviewFileClassification.IsIgnoredPath(relative))
                    continue;
                if (entry is DirectoryInfo child)
                    pending.Push(child);
                else if (entry is FileInfo file && IsTestSource(relative))
                    files.Add(file);
            }
        }
        return files;
    }

    private static bool IsTestSource(string path) =>
        path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
        !CodeReviewFileClassification.IsIgnoredPath(path) &&
        CodeReviewFileClassification.IsTestFilePath(path);
}
