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
using ReviewG33k.Services;

namespace ReviewG33k.Models;

/// <summary>
/// A repository picker label paired with its full working directory path.
/// </summary>
/// <remarks>
/// Repository ancestors provide compact labels for submodules without exposing intermediate source folders.
/// The full path remains available for tooltips and review operations.
/// </remarks>
public sealed record LocalRepositoryOption(string FullPath, string DisplayName)
{
    public string RepositoryName => new DirectoryInfo(FullPath).Name;

    public string AncestorLabel => DisplayName.EndsWith(RepositoryName, StringComparison.Ordinal)
        ? DisplayName[..^RepositoryName.Length].Replace(Path.DirectorySeparatorChar.ToString(), " › ")
        : string.Empty;

    public override string ToString() => DisplayName;

    public static LocalRepositoryOption Create(string path, string rootPath)
    {
        var fullPath = Path.GetFullPath(path);
        if (string.IsNullOrWhiteSpace(rootPath))
            return new LocalRepositoryOption(fullPath, fullPath);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var relative = Path.GetRelativePath(root, fullPath);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            return new LocalRepositoryOption(fullPath, fullPath);

        var directory = new DirectoryInfo(fullPath);
        if (relative == ".")
            return new LocalRepositoryOption(fullPath, directory.Name);
        var names = new List<string> { directory.Name };
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        for (var parent = directory.Parent; parent != null; parent = parent.Parent)
        {
            if (RepositoryUtilities.IsGitRepository(parent.FullName))
                names.Insert(0, parent.Name);
            if (string.Equals(Path.TrimEndingDirectorySeparator(parent.FullName), root, comparison))
                break;
        }
        return new LocalRepositoryOption(fullPath, names.Count > 1 ? string.Join(" › ", names) : relative);
    }
}
