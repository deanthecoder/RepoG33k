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
using System.Threading;
using DTC.Core.Extensions;

namespace ReviewG33k.Services;

/// <summary>
/// Finds local Git repositories without starting Git processes.
/// </summary>
/// <remarks>
/// Searches nested repositories as well as their parents, skipping generated folders and directory links.
/// Enumeration is intended for a background worker and yields repositories as they are found.
/// </remarks>
public static class LocalRepositoryDiscoveryService
{
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", ".idea", "bin", "obj", "dist", "node_modules", ".venv", "venv", "__pycache__"
    };

    public static IEnumerable<string> Discover(string rootPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            yield break;

        var pending = new Queue<DirectoryInfo>();
        pending.Enqueue(rootPath.ToDir());
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            RecurseSubdirectories = false
        };
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Dequeue();
            if (RepositoryUtilities.IsGitRepository(directory.FullName))
                yield return directory.FullName;

            try
            {
                foreach (var child in directory.EnumerateDirectories("*", options))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!SkippedDirectories.Contains(child.Name))
                        pending.Enqueue(child);
                }
            }
            catch (IOException)
            {
                // A directory may disappear during the scan.
            }
            catch (UnauthorizedAccessException)
            {
                // Permissions may change after the directory was queued.
            }
        }
    }
}
