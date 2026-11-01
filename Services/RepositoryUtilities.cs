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
using System.IO;
using System.Linq;
using DTC.Core.Extensions;

namespace ReviewG33k.Services;

public static class RepositoryUtilities
{
    private static readonly HashSet<string> SkippedSolutionSearchDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git",
        ".vs",
        ".idea",
        "bin",
        "obj",
        "dist",
        "node_modules"
    };
    private static readonly ConcurrentDictionary<string, string> TopLevelSolutionFileCache = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsGitRepository(string directory) =>
        !string.IsNullOrWhiteSpace(directory) &&
        (directory.ToDir().GetDir(".git").Exists() || directory.ToDir().GetFile(".git").Exists());

    public static string FindTopLevelSolutionFile(string rootFolder)
    {
        var normalizedRootFolder = NormalizeRepoPath(rootFolder);
        if (string.IsNullOrWhiteSpace(rootFolder) || !rootFolder.ToDir().Exists())
            return null;

        return TopLevelSolutionFileCache.GetOrAdd(normalizedRootFolder, _ => FindTopLevelSolutionFileCore(rootFolder));
    }

    private static string FindTopLevelSolutionFileCore(string rootFolder)
    {
        var directories = new List<DirectoryInfo> { rootFolder.ToDir() };
        while (directories.Count > 0)
        {
            var candidates = new List<string>();
            var nextLevel = new List<DirectoryInfo>();
            foreach (var directory in directories)
            {
                foreach (var entry in directory.TryGetContent())
                {
                    if (entry is FileInfo file &&
                        (file.Extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) ||
                         file.Extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase)))
                    {
                        candidates.Add(file.FullName);
                    }
                    else if (entry is DirectoryInfo child &&
                             !SkippedSolutionSearchDirectories.Contains(child.Name) &&
                             !child.IsSymbolicLink())
                    {
                        nextLevel.Add(child);
                    }
                }
            }

            // Search one depth at a time so a nearby solution avoids a full tree scan.
            if (candidates.Count > 0)
                return candidates.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).First();

            directories = nextLevel;
        }

        return null;
    }

    public static bool AreSameRepoPath(string leftPath, string rightPath)
    {
        if (string.IsNullOrWhiteSpace(leftPath) || string.IsNullOrWhiteSpace(rightPath))
            return false;

        return string.Equals(NormalizeRepoPath(leftPath), NormalizeRepoPath(rightPath), StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeRepoPath(string path) =>
        (path ?? string.Empty).Replace('\\', '/').Trim();
}
