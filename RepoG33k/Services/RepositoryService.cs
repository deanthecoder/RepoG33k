// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software.

using ReviewG33k.Services;

namespace RepoG33k.Services;

/// <summary>Describes a changed path without conflating its index and working-copy states.</summary>
/// <remarks>A partially staged file can appear in both change scopes.</remarks>
internal sealed record RepositoryFile(string Path, char Index, char Working, string OriginalPath = null)
{
    public bool IsConflict => Index == 'U' || Working == 'U' || $"{Index}{Working}" is "AA" or "DD";
    public bool IsStaged => Index is not (' ' or '?') && !IsConflict;
    public bool IsUnstaged => Working != ' ' || Index == '?';
}

/// <summary>Captures the Git state used to render one repository workspace.</summary>
/// <remarks>Refs retain full names, so local and remote branches remain distinct.</remarks>
internal sealed record RepositorySnapshot(string Root, string Branch, string Head, string Upstream, int Ahead, int Behind,
    IReadOnlyList<RepositoryFile> Files, IReadOnlyList<RepositoryRef> Branches, bool Rebasing, bool Merging, string UpstreamHead = null, DateTimeOffset? LastFetched = null);

/// <summary>Identifies a local or remote Git branch.</summary>
/// <remarks>Symbolic remote HEAD aliases are omitted from navigation.</remarks>
internal sealed record RepositoryRef(string Name, string FullName, bool Remote);

/// <summary>Runs explicit repository operations using the installed Git executable.</summary>
/// <remarks>Argument lists preserve unusual filenames; rebase keeps a recovery ref and requires a clean checkout.</remarks>
internal sealed class RepositoryService
{
    private readonly GitCommandRunner m_git = new();

    public async Task<string> RunAsync(string root, params string[] args)
    {
        var result = await m_git.RunAsync(root, args);
        if (!result.IsSuccess)
            throw new InvalidOperationException(result.GetCombinedOutput());
        return result.StandardOutput;
    }

    public async Task<RepositorySnapshot> ReadAsync(string path)
    {
        var root = (await RunAsync(path, "rev-parse", "--show-toplevel")).Trim();
        var status = await RunAsync(root, "status", "--porcelain=v1", "-z", "--untracked-files=all");
        var branchResult = await m_git.RunAsync(root, "symbolic-ref", "--quiet", "--short", "HEAD");
        var head = await m_git.RunAsync(root, "rev-parse", "--verify", "HEAD");
        var upstream = await m_git.RunAsync(root, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}");
        var upstreamHead = upstream.IsSuccess ? (await RunAsync(root, "rev-parse", "@{upstream}")).Trim() : null;
        var ahead = 0;
        var behind = 0;
        if (upstream.IsSuccess && head.IsSuccess)
        {
            var counts = (await RunAsync(root, "rev-list", "--left-right", "--count", "HEAD...@{upstream}")).Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            ahead = int.Parse(counts[0]);
            behind = int.Parse(counts[1]);
        }
        var refs = await RunAsync(root, "for-each-ref", "--format=%(refname)%09%(refname:short)%09%(symref)", "refs/heads", "refs/remotes");
        var branches = refs.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => l.TrimEnd('\r').Split('\t'))
            .Where(f => f.Length == 3 && f[2].Length == 0)
            .Select(f => new RepositoryRef(f[0].StartsWith("refs/remotes/", StringComparison.Ordinal) ? f[0][13..] : f[0][11..], f[0], f[0].StartsWith("refs/remotes/", StringComparison.Ordinal))).ToArray();
        async Task<bool> GitPathExists(string name)
        {
            var relative = (await RunAsync(root, "rev-parse", "--git-path", name)).Trim();
            var full = Path.GetFullPath(relative, root);
            return Directory.Exists(full) || File.Exists(full);
        }
        return new RepositorySnapshot(root, branchResult.IsSuccess ? branchResult.StandardOutput.Trim() : "Detached HEAD",
            head.IsSuccess ? head.StandardOutput.Trim() : null, upstream.IsSuccess ? upstream.StandardOutput.Trim() : null,
            ahead, behind, ParseStatus(status), branches, await GitPathExists("rebase-merge") || await GitPathExists("rebase-apply"), await GitPathExists("MERGE_HEAD"), upstreamHead, await ReadLastFetchAsync(root));
    }

    public async Task<RepositoryBranchStatus> ReadBranchStatusAsync(string root, CancellationToken token = default)
    {
        var observed = DateTimeOffset.UtcNow;
        var branch = await m_git.RunAsync(root, token, "symbolic-ref", "--quiet", "--short", "HEAD");
        var upstream = await m_git.RunAsync(root, token, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{upstream}");
        var ahead = 0;
        var behind = 0;
        if (upstream.IsSuccess)
        {
            var counts = await m_git.RunAsync(root, token, "rev-list", "--left-right", "--count", "HEAD...@{upstream}");
            if (!counts.IsSuccess)
                throw new InvalidOperationException(counts.GetCombinedOutput());
            var values = counts.StandardOutput.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            ahead = int.Parse(values[0]);
            behind = int.Parse(values[1]);
        }
        return new RepositoryBranchStatus(branch.IsSuccess ? branch.StandardOutput.Trim() : "Detached HEAD",
            upstream.IsSuccess ? upstream.StandardOutput.Trim() : null, ahead, behind, await ReadLastFetchAsync(root, token), observed);
    }

    private async Task<DateTimeOffset?> ReadLastFetchAsync(string root, CancellationToken token = default)
    {
        var result = await m_git.RunAsync(root, token, "rev-parse", "--git-path", "FETCH_HEAD");
        if (!result.IsSuccess)
            throw new InvalidOperationException(result.GetCombinedOutput());
        var path = Path.GetFullPath(result.StandardOutput.Trim(), root);
        return File.Exists(path) ? new DateTimeOffset(File.GetLastWriteTimeUtc(path)) : null;
    }

    internal static IReadOnlyList<RepositoryFile> ParseStatus(string output)
    {
        var fields = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var files = new List<RepositoryFile>();
        for (var i = 0; i < fields.Length; i++)
        {
            var field = fields[i];
            if (field.Length < 4)
                throw new InvalidOperationException("Git returned an incomplete status entry.");
            var renamed = field[0] is 'R' or 'C' || field[1] is 'R' or 'C';
            var original = renamed && i + 1 < fields.Length ? fields[++i] : null;
            files.Add(new RepositoryFile(field[3..], field[0], field[1], original));
        }
        return files;
    }

    public Task<string> StageAsync(string root, RepositoryFile file)
        => RunAsync(root, new[] { "add", "--" }.Concat(Paths(file)).ToArray());

    public async Task UnstageAsync(string root, RepositoryFile file)
    {
        var head = await m_git.RunAsync(root, "rev-parse", "--verify", "HEAD");
        await RunAsync(root, (head.IsSuccess ? new[] { "reset", "--quiet", "HEAD", "--" } : new[] { "rm", "--cached", "--force", "--quiet", "--" }).Concat(Paths(file)).ToArray());
    }

    private static IEnumerable<string> Paths(RepositoryFile file) => file.OriginalPath == null ? [file.Path] : [file.Path, file.OriginalPath];

    public async Task<string> IndexTreeAsync(string root) => (await RunAsync(root, "write-tree")).Trim();

    public async Task CommitWorkingOrStagedAsync(string root, string summary, string details, bool amend, string expectedIndex)
    {
        if (string.IsNullOrWhiteSpace(summary) || summary.Contains('\n') || summary.Contains('\r'))
            throw new InvalidOperationException("Enter a single-line commit summary.");
        if (await IndexTreeAsync(root) != expectedIndex)
            throw new InvalidOperationException("Staged changes changed outside RepoG33k. Refresh and review them before committing.");
        var snapshot = await ReadAsync(root);
        if (snapshot.Rebasing || snapshot.Merging || snapshot.Files.Any(f => f.IsConflict))
            throw new InvalidOperationException("Finish or abort the current Git operation before creating a commit.");
        if (!snapshot.Files.Any(f => f.IsStaged))
        {
            if (!snapshot.Files.Any(f => f.IsUnstaged))
                throw new InvalidOperationException("There are no changes to commit.");
            await RunAsync(root, "add", "--all");
            expectedIndex = await IndexTreeAsync(root);
        }
        await CommitAsync(root, summary, details, amend, expectedIndex);
    }

    public async Task CommitAsync(string root, string summary, string details, bool amend, string expectedIndex)
    {
        if (string.IsNullOrWhiteSpace(summary) || summary.Contains('\n') || summary.Contains('\r'))
            throw new InvalidOperationException("Enter a single-line commit summary.");
        if (await IndexTreeAsync(root) != expectedIndex)
            throw new InvalidOperationException("Staged changes changed outside RepoG33k. Refresh and review them before committing.");
        var snapshot = await ReadAsync(root);
        if (snapshot.Rebasing || snapshot.Merging || snapshot.Files.Any(f => f.IsConflict))
            throw new InvalidOperationException("Finish or abort the current Git operation before creating a commit.");
        if (!amend && !snapshot.Files.Any(f => f.IsStaged))
            throw new InvalidOperationException("Stage at least one file before committing.");
        var messagePath = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(messagePath, summary.Trim() + (string.IsNullOrWhiteSpace(details) ? "\n" : "\n\n" + details.Trim() + "\n"));
            await RunAsync(root, amend ? ["commit", "--amend", "--file", messagePath] : ["commit", "--file", messagePath]);
        }
        finally { File.Delete(messagePath); }
    }

    public async Task CreateBranchAsync(string root, string name, string startingPoint)
    {
        await RunAsync(root, "check-ref-format", "--branch", name);
        if ((await ReadAsync(root)).Rebasing)
            throw new InvalidOperationException("Finish or abort the rebase before creating a branch.");
        await RunAsync(root, "switch", "-c", name, startingPoint);
    }

    public async Task RebaseAsync(string root, string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            throw new InvalidOperationException("Choose the branch to rebase onto.");
        var state = await ReadAsync(root);
        if (state.Head == null || state.Branch == "Detached HEAD" || state.Files.Count != 0 || state.Rebasing || state.Merging)
            throw new InvalidOperationException("Rebase requires a clean branch with no operation in progress. Commit or stash your changes first.");
        await RunAsync(root, "rev-parse", "--verify", target + "^{commit}");
        var recovery = $"refs/difflens/recovery/{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        await RunAsync(root, "update-ref", recovery, state.Head);
        await RunAsync(root, "rebase", target);
    }

    public async Task<string> DeleteBranchAsync(string root, RepositoryRef branch, string expectedTip, bool allowUnmerged = false)
    {
        var prefix = branch.Remote ? "refs/remotes/" : "refs/heads/";
        if (!branch.FullName.StartsWith(prefix, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(expectedTip))
            throw new InvalidOperationException("Choose a valid branch and refresh before deleting it.");
        var state = await ReadAsync(root);
        if (state.Rebasing || state.Merging)
            throw new InvalidOperationException("Finish or abort the current Git operation before deleting a branch.");
        if (!branch.Remote && branch.FullName == "refs/heads/" + state.Branch)
            throw new InvalidOperationException("Switch to another branch before deleting the checked-out branch.");
        var tip = (await RunAsync(root, "rev-parse", "--verify", branch.FullName + "^{commit}")).Trim();
        if (tip != expectedTip)
            throw new InvalidOperationException("This branch changed. Refresh and inspect it before deleting it.");
        if (branch.Remote)
        {
            var name = branch.FullName[prefix.Length..];
            var remotes = (await RunAsync(root, "remote")).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(r => r.TrimEnd('\r'));
            var remote = remotes.OrderByDescending(r => r.Length).FirstOrDefault(r => name.StartsWith(r + "/", StringComparison.Ordinal));
            if (remote == null)
                throw new InvalidOperationException("The branch's remote is no longer configured.");
            var destination = "refs/heads/" + name[(remote.Length + 1)..];
            await RunAsync(root, "push", $"--force-with-lease={destination}:{expectedTip}", remote, ":" + destination);
            return null;
        }
        string recovery = null;
        if (allowUnmerged)
        {
            recovery = $"refs/difflens/recovery/deleted-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
            await RunAsync(root, "update-ref", recovery, tip);
        }
        // Git protects branches checked out in other worktrees, including with -D.
        await RunAsync(root, "branch", allowUnmerged ? "-D" : "-d", "--", branch.FullName[prefix.Length..]);
        return recovery;
    }

    public async Task PushWithLeaseAsync(string root, string expectedRemoteHead)
    {
        if (string.IsNullOrWhiteSpace(expectedRemoteHead))
            throw new InvalidOperationException("Fetch and review the upstream branch before pushing rewritten commits.");
        var branch = (await RunAsync(root, "symbolic-ref", "--quiet", "--short", "HEAD")).Trim();
        var remote = (await RunAsync(root, "config", "--get", $"branch.{branch}.remote")).Trim();
        var destination = (await RunAsync(root, "config", "--get", $"branch.{branch}.merge")).Trim();
        if (!destination.StartsWith("refs/heads/", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(remote))
            throw new InvalidOperationException("The current branch has no valid upstream destination.");
        await RunAsync(root, "push", $"--force-with-lease={destination}:{expectedRemoteHead}", remote, $"HEAD:{destination}");
    }
}
