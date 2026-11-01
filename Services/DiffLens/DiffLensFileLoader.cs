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
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using DTC.Core.Extensions;

namespace ReviewG33k.Services.DiffLens;

/// <summary>
/// Loads current files against HEAD or the merge base with a target branch.
/// </summary>
/// <remarks>
/// Uses HEAD for uncommitted changes, resolves branch refs without fetching, and preserves rename paths.
/// </remarks>
internal sealed partial class DiffLensFileLoader
{
    private readonly GitCommandRunner m_git = new();
    private readonly CancellationToken m_cancellation;
    private readonly IProgress<DiffLensLoadProgress> m_progress;

    public DiffLensFileLoader(CancellationToken cancellationToken = default, IProgress<DiffLensLoadProgress> progress = null)
    {
        m_cancellation = cancellationToken;
        m_progress = progress;
    }

    private void Report(string stage, int completed = 0, int total = 0)
    {
        m_cancellation.ThrowIfCancellationRequested();
        m_progress?.Report(new DiffLensLoadProgress(stage, completed, total));
        m_cancellation.ThrowIfCancellationRequested();
    }

    public async Task<DiffLensFile> LoadAsync(string repository, string target, string path)
    {
        Report("Finding changed files…");
        var snapshot = await ResolveAsync(repository, target);
        path = string.IsNullOrWhiteSpace(path) ? snapshot.Paths.FirstOrDefault() : path;
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("No changes found since the branch base.");
        return await LoadFileAsync(snapshot.Root, snapshot.MergeBase, snapshot.Changes, path);
    }

    public async Task<DiffLensChangeSet> LoadSetAsync(string repository, string target, string path = null)
    {
        Report("Finding changed files…");
        var snapshot = await ResolveAsync(repository, target);
        var paths = string.IsNullOrWhiteSpace(path) ? snapshot.Paths : [path];
        var oldPaths = snapshot.Changes.ToDictionary(c => c.NewPath, c => c.OldPath, StringComparer.Ordinal);
        var baselines = paths.Length == 0 ? new Dictionary<string, byte[]>()
            : await GitBaselineBatchReader.ReadAsync(snapshot.Root, snapshot.MergeBase, paths.Select(p => oldPaths.GetValueOrDefault(p, p)), m_cancellation);
        var entries = new List<DiffLensEntry>();
        Report("Loading files", 0, paths.Length);
        foreach (var relative in paths)
        {
            Report("Loading files", entries.Count, paths.Length);
            try
            {
                baselines.TryGetValue(oldPaths.GetValueOrDefault(relative, relative), out var baseline);
                var file = await LoadFileAsync(snapshot.Root, snapshot.MergeBase, snapshot.Changes, relative, baseline, baselineProvided: true);
                entries.Add(new DiffLensEntry(file.RelativePath, file, null));
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
            {
                // Preserve unsupported files in the map instead of silently omitting changes.
                entries.Add(new DiffLensEntry(relative, null, ex.Message));
            }
        }
        Report("Loading files", entries.Count, paths.Length);
        return new DiffLensChangeSet(snapshot.Root, snapshot.MergeBase, entries);
    }

    public async Task<DiffLensChangeSet> LoadReviewSetAsync(
        string repository, string target, CommandLineReviewMode mode, string path = null)
    {
        Report("Finding files…");
        if (!RepositoryUtilities.IsGitRepository(repository))
            throw new InvalidOperationException("Choose a Git repository for DiffLens.");
        if (mode == CommandLineReviewMode.Tree)
            return await LoadTreeSetAsync(repository, path);
        if (mode == CommandLineReviewMode.Committed && !string.IsNullOrWhiteSpace(target) &&
            target != "HEAD" && !target.StartsWith("refs/", StringComparison.Ordinal))
        {
            // Refresh the requested branch when possible; offline repositories still use local refs.
            await m_git.RunAsync(repository, m_cancellation, "fetch", "--prune", "origin", $"+refs/heads/{target}:refs/remotes/origin/{target}");
        }
        return await LoadSetAsync(repository, mode == CommandLineReviewMode.Committed ? target : "HEAD", path);
    }

    private async Task<DiffLensChangeSet> LoadTreeSetAsync(string repository, string path)
    {
        var root = Path.GetFullPath(repository);
        // Git supplies the scope, including tracked files normally excluded by scanner rules.
        var paths = (await RunAsync(root, "ls-files", "--cached", "--others", "--exclude-standard", "-z"))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var files = paths.Distinct(StringComparer.Ordinal)
            .Where(relative => File.Exists(Path.Combine(root, relative)))
            .Where(relative => string.IsNullOrWhiteSpace(path) || relative == path)
            .OrderBy(relative => relative, StringComparer.Ordinal).ToArray();
        var snapshot = await ResolveAsync(root, "HEAD");
        root = snapshot.Root;
        var revision = snapshot.MergeBase;
        var changes = snapshot.Changes.ToDictionary(change => change.NewPath, change => change.OldPath, StringComparer.Ordinal);
        Report("Reading baseline files…");
        var baselines = await GitBaselineBatchReader.ReadAsync(root, revision, files.Select(relative => changes.GetValueOrDefault(relative, relative)), m_cancellation);
        var entries = new List<DiffLensEntry>();
        Report("Loading files", 0, files.Length);
        foreach (var relative in files)
        {
            Report("Loading files", entries.Count, files.Length);
            try
            {
                var baseline = baselines.GetValueOrDefault(changes.GetValueOrDefault(relative, relative));
                var file = await LoadFileAsync(root, revision, [], relative, baseline, baselineProvided: true);
                entries.Add(new DiffLensEntry(relative, file, null));
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
            {
                entries.Add(new DiffLensEntry(relative, null, ex.Message));
            }
        }
        Report("Loading files", entries.Count, files.Length);
        return new DiffLensChangeSet(root, revision, entries);
    }

    private async Task<(string Root, string MergeBase, (string OldPath, string NewPath)[] Changes, string[] Paths)> ResolveAsync(string repository, string target)
    {
        var root = Path.GetFullPath((await RunAsync(repository, "rev-parse", "--show-toplevel")).Trim());
        var mergeBase = await ResolveBaselineAsync(root, target);
        var statuses = (await RunAsync(root, "diff", "--name-status", "-z", "--find-renames", mergeBase, "--")).Split('\0');
        var changes = ParseChanges(statuses);
        var untracked = (await RunAsync(root, "ls-files", "--others", "--exclude-standard", "-z")).Split('\0', StringSplitOptions.RemoveEmptyEntries);
        return (root, mergeBase, changes, changes.Select(c => c.NewPath).Concat(untracked).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToArray());
    }

    private async Task<string> ResolveBaselineAsync(string root, string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            throw new InvalidOperationException("Choose a target branch, such as main.");
        string mergeBase;
        if (target == "HEAD" || target == "HEAD^")
        {
            // Comparing the index and working tree to HEAD excludes already committed branch changes.
            mergeBase = target == "HEAD" ? await HeadOrEmptyTreeAsync(root) : (await RunAsync(root, "rev-parse", target)).Trim();
        }
        else
        {
            var reference = target.StartsWith("refs/", StringComparison.Ordinal) ? target : $"refs/remotes/origin/{target}";
            if (!(await m_git.RunAsync(root, m_cancellation, "show-ref", "--verify", "--quiet", reference)).IsSuccess)
                reference = $"refs/heads/{target}";
            mergeBase = (await RunAsync(root, "merge-base", "HEAD", reference)).Trim();
        }
        return mergeBase;
    }

    private async Task<string> HeadOrEmptyTreeAsync(string root)
    {
        var head = await m_git.RunAsync(root, m_cancellation, "rev-parse", "--verify", "HEAD");
        if (head.IsSuccess)
            return head.StandardOutput.Trim();
        // Let Git select its object format, including SHA-256 repositories.
        var empty = Path.GetTempFileName();
        try { return (await RunAsync(root, "hash-object", "-w", "-t", "tree", "--", empty)).Trim(); }
        finally { File.Delete(empty); }
    }

    private async Task<DiffLensFile> LoadFileAsync(string root, string mergeBase, (string OldPath, string NewPath)[] changes, string path,
        byte[] baselineBytes = null, bool baselineProvided = false)
    {
        var fullPath = ResolveEditablePath(root, path);
        var relative = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        var change = changes.FirstOrDefault(c => c.NewPath == relative);
        var oldPath = change.OldPath ?? relative;
        var hasBaseline = baselineBytes != null;
        if (!baselineProvided)
        {
            var exists = await m_git.RunAsync(root, m_cancellation, "ls-tree", "-z", mergeBase, "--", oldPath);
            if (!exists.IsSuccess)
                throw new IOException(exists.StandardError);
            hasBaseline = !string.IsNullOrEmpty(exists.StandardOutput);
            if (hasBaseline)
            {
                var result = await m_git.RunBytesAsync(root, m_cancellation, "show", $"{mergeBase}:{oldPath}");
                if (!result.IsSuccess)
                    throw new IOException(result.StandardError);
                baselineBytes = result.StandardOutput;
            }
        }
        var baseline = baselineBytes == null ? string.Empty : GitChangedFileContent.DecodeText(baselineBytes)
            ?? throw new InvalidOperationException("The baseline is not supported text.");
        var bytes = File.Exists(fullPath) ? await ReadWorkingBytesAsync(fullPath) : null;
        if (bytes == null && !hasBaseline)
            throw new FileNotFoundException("File is absent from both the branch base and working tree.", fullPath);
        return new DiffLensFile(fullPath, relative, mergeBase, baseline, bytes);
    }

    private async Task<byte[]> ReadWorkingBytesAsync(string fullPath)
    {
        var file = new FileInfo(fullPath);
        var looksLikeText = file.Length == 0 || file.IsTextFile();
        var bytes = await File.ReadAllBytesAsync(fullPath, m_cancellation);
        // DTC.Core samples ASCII and known extensions. Keep valid Unicode and tiny text files too.
        if (!looksLikeText && GitChangedFileContent.DecodeText(bytes) == null)
            throw new InvalidOperationException("Binary file — no text preview.");
        return bytes;
    }

    private static string ResolveEditablePath(string root, string path)
    {
        var fullPath = Path.GetFullPath(Path.Combine(root, path));
        var relative = Path.GetRelativePath(root, fullPath).Replace('\\', '/');
        if (relative == ".." || relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new InvalidOperationException("Choose a file inside this repository.");
        // Refuse symlink traversal when opening an editable document.
        for (var item = fullPath; !string.Equals(item, root, StringComparison.OrdinalIgnoreCase); item = Path.GetDirectoryName(item))
        {
            if (string.IsNullOrEmpty(item))
                throw new InvalidOperationException("Unable to resolve the repository path.");
            if ((File.Exists(item) || Directory.Exists(item)) && File.GetAttributes(item).HasFlag(FileAttributes.ReparsePoint))
                throw new InvalidOperationException("Symlink files and directories are not editable in this prototype.");
        }
        return fullPath;
    }

    private async Task<string> RunAsync(string root, params string[] args)
    {
        var result = await m_git.RunAsync(root, m_cancellation, args);
        if (!result.IsSuccess)
            throw new InvalidOperationException(result.StandardError.Trim());
        return result.StandardOutput;
    }

    private static (string OldPath, string NewPath)[] ParseChanges(string[] fields)
    {
        var result = new System.Collections.Generic.List<(string, string)>();
        for (var i = 0; i < fields.Length && !string.IsNullOrEmpty(fields[i]);)
        {
            var status = fields[i++];
            var oldPath = fields[i++];
            var newPath = status.StartsWith('R') || status.StartsWith('C') ? fields[i++] : oldPath;
            result.Add((oldPath, newPath));
        }
        return result.ToArray();
    }
}

/// <summary>
/// Reports the current stage and optional file count while loading a DiffLens snapshot.
/// </summary>
/// <remarks>
/// A zero total represents a Git operation whose duration is not yet known.
/// </remarks>
internal sealed record DiffLensLoadProgress(string Stage, int Completed, int Total);
