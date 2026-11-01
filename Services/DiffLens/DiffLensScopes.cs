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
using System.Text;
using System.Threading.Tasks;

namespace ReviewG33k.Services.DiffLens;

/// <summary>
/// Identifies a Git comparison independently of its display label.
/// </summary>
/// <remarks>
/// Commit IDs are immutable; local scopes are reloaded from Git on refresh.
/// </remarks>
internal sealed record DiffLensScope(string Id, string Label, string Commit = null, string Date = null, string ShortHash = null, string BranchTip = null)
{
    public bool IsReadOnly => Id == "staged" || Commit != null || BranchTip != null;
    public override string ToString() => Label;
}

/// <summary>Identifies the immutable trees used for one staged comparison.</summary>
/// <remarks>Pinning both sides keeps file reads consistent if another Git tool changes the index during loading.</remarks>
internal sealed record DiffLensStagedSnapshot(string Baseline, string Index);

internal sealed partial class DiffLensFileLoader
{
    public async Task<(string Target, List<DiffLensScope> Scopes)> GetScopesAsync(string root, string target, DiffLensScope retained = null, int? maxCommits = null)
    {
        Report("Reading Git comparisons…");
        if (string.IsNullOrWhiteSpace(target) || target == "HEAD")
        {
            target = await new LocalBaseBranchService(m_git).ResolveLocalBaseBranchAsync(root, null, false, null);
            if (string.IsNullOrWhiteSpace(target))
            {
                foreach (var candidate in new[] { "main", "master" })
                    if ((await m_git.RunAsync(root, m_cancellation, "rev-parse", "--verify", $"refs/heads/{candidate}")).IsSuccess)
                    {
                        target = candidate;
                        break;
                    }
            }
            target ??= "HEAD";
        }
        var staged = !string.IsNullOrEmpty(await RunAsync(root, "diff", "--cached", "--name-only", "-z"));
        var unstaged = !string.IsNullOrEmpty(await RunAsync(root, "diff", "--name-only", "-z")) ||
            !string.IsNullOrEmpty(await RunAsync(root, "ls-files", "--others", "--exclude-standard", "-z"));
        var scopes = new List<DiffLensScope>();
        if (staged || unstaged)
            scopes.Add(new DiffLensScope("local", "All local changes"));
        if (staged && unstaged)
        {
            scopes.Add(new DiffLensScope("unstaged", "Unstaged changes"));
            scopes.Add(new DiffLensScope("staged", "Staged changes"));
        }
        scopes.Add(new DiffLensScope("branch", $"Entire branch · base: {target}"));
        if (retained != null && scopes.All(s => s.Id != retained.Id) && retained.Commit == null)
            scopes.Insert(0, retained);
        var head = await m_git.RunAsync(root, m_cancellation, "rev-parse", "--verify", "HEAD");
        if (head.IsSuccess)
        {
            var baseline = await ResolveBaselineAsync(root, target);
            // On the base branch itself, show its history rather than an empty commit picker.
            var current = await m_git.RunAsync(root, m_cancellation, "symbolic-ref", "--quiet", "--short", "HEAD");
            var onBaseBranch = current.IsSuccess && current.StandardOutput.Trim() == LocalBaseBranchService.NormalizeBranchName(target);
            var range = baseline == head.StandardOutput.Trim() && onBaseBranch ? "HEAD" : $"{baseline}..HEAD";
            var args = new List<string> { "log", "--first-parent", "--format=%H%x09%h%x09%as%x09%s" };
            if (maxCommits.HasValue)
                args.Add($"--max-count={maxCommits.Value}");
            args.AddRange([range, "--"]);
            var log = await RunAsync(root, args.ToArray());
            foreach (var line in log.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = line.TrimEnd('\r').Split('\t', 4);
                if (fields.Length == 4)
                    scopes.Add(new DiffLensScope(fields[0], fields[3], fields[0], fields[2], fields[1]));
            }
        }
        if (retained?.Commit != null && scopes.All(s => s.Id != retained.Id))
            scopes.Add(retained);
        return (target, scopes);
    }

    public async Task<DiffLensStagedSnapshot> CaptureStagedSnapshotAsync(string root)
    {
        var baseline = await HeadOrEmptyTreeAsync(root);
        var index = (await RunAsync(root, "write-tree")).Trim();
        return new DiffLensStagedSnapshot(baseline, index);
    }

    public async Task<string> GetImmutableCacheKeyAsync(string root, string target, DiffLensScope scope, string path = null, DiffLensStagedSnapshot stagedSnapshot = null)
    {
        if (scope?.Id == "staged")
        {
            var staged = stagedSnapshot ?? await CaptureStagedSnapshotAsync(root);
            return string.Join("\0", Path.GetFullPath(root), scope.Id, staged.Baseline, staged.Index, path ?? string.Empty);
        }
        if (scope?.Commit == null && scope?.BranchTip == null)
            return null;
        var tip = (await RunAsync(root, "rev-parse", "--verify", (scope.Commit ?? scope.BranchTip) + "^{commit}")).Trim();
        var baseline = scope.BranchTip == null ? string.Empty : (await RunAsync(root, "rev-parse", "--verify", target + "^{commit}")).Trim();
        var head = await RunAsync(root, "rev-parse", "HEAD");
        return string.Join("\0", Path.GetFullPath(root), scope.Id, tip, baseline, head.Trim(), path ?? string.Empty);
    }

    public async Task<DiffLensChangeSet> LoadScopeAsync(string repository, string target, DiffLensScope scope, string path = null, DiffLensStagedSnapshot stagedSnapshot = null)
    {
        if (scope.Id == "tree")
            return await LoadReviewSetAsync(repository, "HEAD", CommandLineReviewMode.Tree, path);
        if (scope.Id == "branch")
            return await LoadSetAsync(repository, target, path);
        if (scope.Id == "local")
            return await LoadSetAsync(repository, "HEAD", path);

        var root = Path.GetFullPath((await RunAsync(repository, "rev-parse", "--show-toplevel")).Trim());
        Report("Reading comparison…");
        string baseline;
        string revision;
        string status;
        if (scope.BranchTip != null)
        {
            revision = (await RunAsync(root, "rev-parse", "--verify", scope.BranchTip + "^{commit}")).Trim();
            baseline = (await RunAsync(root, "merge-base", target, revision)).Trim();
            status = await RunAsync(root, "diff", "--name-status", "-z", "--find-renames", baseline, revision, "--");
        }
        else if (scope.Commit != null)
        {
            revision = (await RunAsync(root, "rev-parse", "--verify", scope.Commit + "^{commit}")).Trim();
            var parents = (await RunAsync(root, "rev-list", "--parents", "-n", "1", revision)).Trim().Split(' ');
            baseline = parents.Length > 1 ? parents[1] : null;
            status = baseline == null
                ? await RunAsync(root, "diff-tree", "--root", "--no-commit-id", "-r", "--name-status", "-z", revision, "--")
                : await RunAsync(root, "diff", "--name-status", "-z", "--find-renames", baseline, revision, "--");
        }
        else
        {
            // Capture the index as an immutable tree, without changing the index or working files.
            var index = stagedSnapshot?.Index ?? (await RunAsync(root, "write-tree")).Trim();
            baseline = scope.Id == "unstaged" ? index : stagedSnapshot?.Baseline ?? await HeadOrEmptyTreeAsync(root);
            revision = scope.Id == "staged" ? index : null;
            status = revision == null
                ? await RunAsync(root, "diff", "--name-status", "-z", "--find-renames", baseline, "--")
                : await RunAsync(root, "diff", "--name-status", "-z", "--find-renames", baseline, revision, "--");
        }
        var changes = ParseChanges(status.Split('\0'));
        var paths = changes.Select(c => c.NewPath);
        if (scope.Id == "unstaged")
            paths = paths.Concat((await RunAsync(root, "ls-files", "--others", "--exclude-standard", "-z")).Split('\0', StringSplitOptions.RemoveEmptyEntries));
        var selected = paths.Distinct(StringComparer.Ordinal).Where(p => path == null || p == path).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        if (selected.Length == 0)
            return new DiffLensChangeSet(root, baseline ?? "Empty tree", []);
        // Read each side through one batch reader, rather than launching Git per file.
        // Rename baselines use their original paths; dictionary membership preserves empty blobs.
        var selectedPaths = selected.ToHashSet(StringComparer.Ordinal);
        var oldPaths = changes.Where(c => selectedPaths.Contains(c.NewPath)).ToDictionary(c => c.NewPath, c => c.OldPath, StringComparer.Ordinal);
        var beforeFiles = baseline == null ? new Dictionary<string, byte[]>()
            : await GitBaselineBatchReader.ReadAsync(root, baseline, selected.Select(p => oldPaths.GetValueOrDefault(p, p)), m_cancellation);
        var afterFiles = revision == null ? null : await GitBaselineBatchReader.ReadAsync(root, revision, selected, m_cancellation);
        var entries = new List<DiffLensEntry>();
        foreach (var relative in selected)
        {
            Report("Loading files", entries.Count, selected.Length);
            try
            {
                var oldPath = oldPaths.GetValueOrDefault(relative, relative);
                beforeFiles.TryGetValue(oldPath, out var before);
                if (revision == null)
                    entries.Add(new DiffLensEntry(relative, await LoadFileAsync(root, baseline, changes, relative, before, baselineProvided: true), null));
                else
                {
                    afterFiles.TryGetValue(relative, out var after);
                    if (before == null && after == null)
                        throw new InvalidOperationException("Git entry has no readable file blob (for example, a submodule).");
                    var text = before == null ? string.Empty : GitChangedFileContent.DecodeText(before)
                        ?? throw new InvalidOperationException("Binary file — no text preview.");
                    var file = new DiffLensFile(Path.Combine(root, relative), relative, baseline ?? "Empty tree", text, after, readOnly: true);
                    entries.Add(new DiffLensEntry(relative, file, null));
                }
            }
            catch (DecoderFallbackException)
            {
                entries.Add(new DiffLensEntry(relative, null, "Binary file — no text preview."));
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException or ArgumentException)
            {
                entries.Add(new DiffLensEntry(relative, null, ex.Message));
            }
        }
        Report("Loading files", entries.Count, selected.Length);
        return new DiffLensChangeSet(root, baseline ?? "Empty tree", entries);
    }

}
