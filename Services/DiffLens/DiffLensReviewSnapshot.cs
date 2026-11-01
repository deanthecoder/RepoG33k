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

namespace ReviewG33k.Services.DiffLens;

/// <summary>
/// Associates scan findings with their original repository and comparison branch.
/// </summary>
/// <remarks>
/// Normalized relative paths let worktree findings annotate the corresponding working files.
/// Findings remain a scan snapshot and are not revalidated by editing or refreshing the canvas.
/// </remarks>
internal sealed record DiffLensReviewSnapshot(string Root, string BaseBranch, IReadOnlyList<CodeSmellFinding> Findings, CommandLineReviewMode Mode = CommandLineReviewMode.Committed)
{
    public bool Matches(string root, string baseBranch, CommandLineReviewMode mode) =>
        Mode == mode &&
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), Root, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(BaseBranch, baseBranch, StringComparison.Ordinal);

    public IReadOnlyList<CodeSmellFinding> FindingsFor(string root, string baseBranch, CommandLineReviewMode mode, string path) =>
        Matches(root, baseBranch, mode)
            ? Findings.Where(finding => string.Equals(finding.FilePath, path, StringComparison.OrdinalIgnoreCase)).ToArray()
            : [];

    public static DiffLensReviewSnapshot Create(string root, string baseBranch, string scannedRoot, IEnumerable<CodeSmellFinding> findings, CommandLineReviewMode mode = CommandLineReviewMode.Committed)
    {
        var normalized = new List<CodeSmellFinding>();
        foreach (var finding in findings.Where(finding => finding.Severity != CodeReviewFindingSeverity.Ok))
        {
            var path = finding.FilePath;
            if (string.IsNullOrWhiteSpace(path))
                continue;
            var relative = Path.GetRelativePath(scannedRoot, Path.GetFullPath(Path.Combine(scannedRoot, path))).Replace('\\', '/');
            if (relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative))
                continue;
            normalized.Add(new CodeSmellFinding(finding.Severity, finding.RuleId, relative, finding.LineNumber,
                finding.Message, finding.CurrentText, finding.BaselineText));
        }
        return new DiffLensReviewSnapshot(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), baseBranch, normalized, mode);
    }
}
