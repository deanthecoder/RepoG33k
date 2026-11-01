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
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DTC.Core.Extensions;
using ReviewG33k.Models;

namespace ReviewG33k.Services;

public sealed class CodeReviewOrchestrator
{
    private const string CodeReviewFolderName = "CodeReview";
    private const string CodeReviewMarkerFileName = ".reviewg33k";
    private readonly Func<string, CancellationToken, string[], Task<GitCommandResult>> m_runGitAsync;

    public CodeReviewOrchestrator(GitCommandRunner gitCommandRunner)
    {
        ArgumentNullException.ThrowIfNull(gitCommandRunner);
        m_runGitAsync = gitCommandRunner.RunAsync;
    }

    internal CodeReviewOrchestrator(Func<string, CancellationToken, string[], Task<GitCommandResult>> runGitAsync)
    {
        m_runGitAsync = runGitAsync ?? throw new ArgumentNullException(nameof(runGitAsync));
    }

    public async Task<PrepareReviewResult> PrepareReviewAsync(
        string repositoryRoot,
        BitbucketPullRequestReference pullRequest,
        IReadOnlyCollection<string> changedPaths,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(repositoryRoot))
            throw new InvalidOperationException("Repository root folder is required.");

        var repositoryRootDir = repositoryRoot.ToDir();
        if (!repositoryRootDir.Exists())
            throw new DirectoryNotFoundException($"Repository root folder does not exist: {repositoryRoot}");

        var localRepository = await FindOrCloneRepositoryAsync(repositoryRoot, pullRequest, log, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var reviewRef = $"refs/remotes/origin/pr/{pullRequest.PullRequestId}";
        var fetchSpec = $"+refs/pull-requests/{pullRequest.PullRequestId}/from:{reviewRef}";

        log($"Fetching PR #{pullRequest.PullRequestId} from origin...");
        var fetchResult = await m_runGitAsync(localRepository, cancellationToken, ["fetch", "--prune", "origin", fetchSpec]);
        EnsureSuccess(fetchResult, "Failed to fetch pull request from origin.");
        cancellationToken.ThrowIfCancellationRequested();

        EnsureCodeReviewMarker(repositoryRoot);

        var reviewFolderInfo = repositoryRootDir.GetDir(
            $"{CodeReviewFolderName}/{pullRequest.RepoSlug}/PR-{pullRequest.PullRequestId}");
        await EnsureReviewWorktreeReadyAsync(
            localRepository,
            reviewFolderInfo.FullName,
            reviewRef,
            log,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var solutionPath = FindBestSolutionFile(reviewFolderInfo.FullName, changedPaths);

        return new PrepareReviewResult(localRepository, reviewFolderInfo.FullName, solutionPath);
    }

    internal async Task<PrepareReviewResult> PrepareMergedCommitReviewAsync(
        string repositoryRoot,
        BitbucketPullRequestReference pullRequest,
        string targetBranch,
        string mergeCommitHash,
        IReadOnlyCollection<string> changedPaths,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(repositoryRoot))
            throw new InvalidOperationException("Repository root folder is required.");
        if (pullRequest == null)
            throw new InvalidOperationException("Pull request details are required.");
        if (string.IsNullOrWhiteSpace(mergeCommitHash))
            throw new InvalidOperationException("Merge commit hash is required.");

        var repositoryRootDir = repositoryRoot.ToDir();
        if (!repositoryRootDir.Exists())
            throw new DirectoryNotFoundException($"Repository root folder does not exist: {repositoryRoot}");

        var localRepository = await FindOrCloneRepositoryAsync(repositoryRoot, pullRequest, log, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (!string.IsNullOrWhiteSpace(targetBranch))
        {
            log?.Invoke($"Fetching target branch '{targetBranch}' before merge-commit review...");
            _ = await m_runGitAsync(
                localRepository,
                cancellationToken,
                ["fetch", "--prune", "origin", $"+refs/heads/{targetBranch}:refs/remotes/origin/{targetBranch}"]);
        }

        if (!await CommitExistsAsync(localRepository, mergeCommitHash, cancellationToken))
        {
            log?.Invoke("Merge commit not present locally. Fetching latest origin refs...");
            _ = await m_runGitAsync(localRepository, cancellationToken, ["fetch", "--prune", "origin"]);
            if (!await CommitExistsAsync(localRepository, mergeCommitHash, cancellationToken))
            {
                throw new InvalidOperationException(
                    $"Merge commit '{mergeCommitHash}' could not be resolved from local repository or origin.");
            }
        }

        EnsureCodeReviewMarker(repositoryRoot);

        var reviewFolderInfo = repositoryRootDir.GetDir(
            $"{CodeReviewFolderName}/{pullRequest.RepoSlug}/PR-{pullRequest.PullRequestId}-Merged");
        await EnsureReviewWorktreeReadyAsync(
            localRepository,
            reviewFolderInfo.FullName,
            mergeCommitHash,
            log,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var solutionPath = FindBestSolutionFile(reviewFolderInfo.FullName, changedPaths);
        return new PrepareReviewResult(localRepository, reviewFolderInfo.FullName, solutionPath);
    }

    public async Task ClearCodeReviewFolderAsync(
        string repositoryRoot,
        Action<string> log,
        bool logWhenMissing = true,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(repositoryRoot))
            throw new DirectoryNotFoundException("Repository root folder does not exist.");

        var repositoryRootDir = repositoryRoot.ToDir();
        if (!repositoryRootDir.Exists())
            throw new DirectoryNotFoundException("Repository root folder does not exist.");

        var codeReviewRoot = GetCodeReviewRoot(repositoryRoot);
        if (!codeReviewRoot.Exists())
        {
            if (logWhenMissing)
                log("CodeReview folder does not exist, nothing to clear.");
            return;
        }

        if (!HasCodeReviewMarker(codeReviewRoot))
        {
            log($"Safety check: missing '{CodeReviewMarkerFileName}' marker in '{codeReviewRoot.FullName}'. Skipping cleanup.");
            return;
        }

        log("Removing worktrees registered under CodeReview...");
        foreach (var repositoryPath in EnumerateTopLevelGitRepositories(repositoryRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var worktreeList = await m_runGitAsync(repositoryPath, cancellationToken, ["worktree", "list", "--porcelain"]);
            if (!worktreeList.IsSuccess)
                continue;

            foreach (var worktreePath in ParseWorktreePaths(worktreeList.StandardOutput))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsChildPathOf(worktreePath, codeReviewRoot.FullName))
                    continue;

                var removeResult = await m_runGitAsync(repositoryPath, cancellationToken, ["worktree", "remove", "--force", worktreePath]);
                if (!removeResult.IsSuccess)
                    log($"Warning: could not remove registered worktree '{worktreePath}'. {removeResult.GetCombinedOutput()}");
            }

            _ = await m_runGitAsync(repositoryPath, cancellationToken, ["worktree", "prune"]);
        }

        if (codeReviewRoot.Exists())
            codeReviewRoot.TryDelete();

        log("CodeReview folder cleared.");
    }

    private static DirectoryInfo GetCodeReviewRoot(string repositoryRoot) =>
        repositoryRoot.ToDir().GetDir(CodeReviewFolderName);

    private static FileInfo GetCodeReviewMarkerPath(DirectoryInfo codeReviewRoot) =>
        codeReviewRoot.GetFile(CodeReviewMarkerFileName);

    private static bool HasCodeReviewMarker(DirectoryInfo codeReviewRoot) =>
        GetCodeReviewMarkerPath(codeReviewRoot).Exists();

    private static void EnsureCodeReviewMarker(string repositoryRoot)
    {
        var codeReviewRoot = GetCodeReviewRoot(repositoryRoot);
        codeReviewRoot.Create();

        var markerPath = GetCodeReviewMarkerPath(codeReviewRoot);
        if (markerPath.Exists())
            return;

        markerPath.WriteAllText(
            "This folder is managed by ReviewG33k. Cleanup operations are allowed only when this marker exists.");
    }

    private async Task<string> FindOrCloneRepositoryAsync(
        string repositoryRoot,
        BitbucketPullRequestReference pullRequest,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var localRepository = await FindMatchingRepositoryAsync(repositoryRoot, pullRequest, cancellationToken);
        if (localRepository != null)
        {
            log($"Using local repository: {localRepository}");
            return localRepository;
        }

        var targetPath = GetAvailableRepositoryPath(repositoryRoot.ToDir().GetDir(pullRequest.RepoSlug).FullName);

        log($"Local repository not found. Cloning '{pullRequest.CloneUrl}'...");
        var cloneResult = await m_runGitAsync(repositoryRoot, cancellationToken, ["clone", pullRequest.CloneUrl, targetPath]);
        EnsureSuccess(cloneResult, "Failed to clone repository.");

        log($"Clone complete: {targetPath}");
        return targetPath;
    }

    private async Task<string> FindMatchingRepositoryAsync(
        string repositoryRoot,
        BitbucketPullRequestReference pullRequest,
        CancellationToken cancellationToken)
    {
        var expectedRepo = pullRequest.RepoSlug;
        var expectedProject = pullRequest.ProjectKey;
        var expectedHost = pullRequest.Host;

        string nameOnlyMatch = null;

        foreach (var candidate in EnumerateTopLevelGitRepositories(repositoryRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folderName = Path.GetFileName(candidate);
            if (folderName.Equals(expectedRepo, StringComparison.OrdinalIgnoreCase) && nameOnlyMatch == null)
                nameOnlyMatch = candidate;

            var remoteUrlResult = await m_runGitAsync(candidate, cancellationToken, ["config", "--get", "remote.origin.url"]);
            if (!remoteUrlResult.IsSuccess)
                continue;

            var remoteUrl = remoteUrlResult.StandardOutput.Trim();
            if (string.IsNullOrWhiteSpace(remoteUrl))
                continue;

            if (TryParseRemoteIdentity(remoteUrl, out var remoteHost, out var remoteProject, out var remoteRepo) &&
                remoteRepo.Equals(expectedRepo, StringComparison.OrdinalIgnoreCase) &&
                remoteProject.Equals(expectedProject, StringComparison.OrdinalIgnoreCase) &&
                remoteHost.Equals(expectedHost, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return nameOnlyMatch;
    }

    private static bool TryParseRemoteIdentity(string remoteUrl, out string host, out string project, out string repo)
    {
        host = string.Empty;
        project = string.Empty;
        repo = string.Empty;

        if (string.IsNullOrWhiteSpace(remoteUrl))
            return false;

        remoteUrl = remoteUrl.Trim();

        if (Uri.TryCreate(remoteUrl, UriKind.Absolute, out var uri))
        {
            host = uri.Host;
            var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length >= 3 && segments[0].Equals("scm", StringComparison.OrdinalIgnoreCase))
            {
                project = segments[1];
                repo = RemoveGitSuffix(segments[2]);
                return true;
            }

            if (segments.Length >= 4 &&
                segments[0].Equals("projects", StringComparison.OrdinalIgnoreCase) &&
                segments[2].Equals("repos", StringComparison.OrdinalIgnoreCase))
            {
                project = segments[1];
                repo = RemoveGitSuffix(segments[3]);
                return true;
            }

            if (segments.Length >= 2)
            {
                project = segments[^2];
                repo = RemoveGitSuffix(segments[^1]);
                return true;
            }

            return false;
        }

        var sshMatch = Regex.Match(
            remoteUrl,
            "^(?:.+@)?(?<host>[^:]+):(?<project>[^/]+)/(?<repo>.+)$",
            RegexOptions.IgnoreCase);

        if (!sshMatch.Success)
            return false;

        host = sshMatch.Groups["host"].Value;
        project = sshMatch.Groups["project"].Value;
        repo = RemoveGitSuffix(sshMatch.Groups["repo"].Value);

        return !string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(project) && !string.IsNullOrWhiteSpace(repo);
    }

    private static string RemoveGitSuffix(string value)
    {
        var result = value.Trim('/').Trim();
        if (result.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            result = result[..^4];

        return result;
    }

    private async Task EnsureReviewWorktreeReadyAsync(
        string localRepository,
        string reviewFolder,
        string reviewRef,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var reviewFolderInfo = reviewFolder.ToDir();
        if (!reviewFolderInfo.Exists())
        {
            reviewFolderInfo.Parent?.Create();
            log($"Creating detached review worktree at '{reviewFolder}'...");
            var addResult = await m_runGitAsync(localRepository, cancellationToken, ["worktree", "add", "--force", "--detach", reviewFolder, reviewRef]);
            EnsureSuccess(addResult, "Failed to create review worktree.");
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var currentHeadResult = await m_runGitAsync(reviewFolder, cancellationToken, ["rev-parse", "--verify", "HEAD"]);
        var targetHeadResult = await m_runGitAsync(localRepository, cancellationToken, ["rev-parse", "--verify", reviewRef]);
        if (currentHeadResult.IsSuccess &&
            targetHeadResult.IsSuccess &&
            AreSameCommit(currentHeadResult.StandardOutput, targetHeadResult.StandardOutput))
            log($"Reusing existing review worktree at '{reviewFolder}' (already up to date).");
        else
            log($"Refreshing existing review worktree at '{reviewFolder}'...");

        var checkoutResult = await m_runGitAsync(reviewFolder, cancellationToken, ["checkout", "--detach", "--force", reviewRef]);
        EnsureSuccess(
            checkoutResult,
            $"Failed to refresh existing review worktree at '{reviewFolder}'. Close apps using this folder and retry.");

        if (!(currentHeadResult.IsSuccess &&
              targetHeadResult.IsSuccess &&
              AreSameCommit(currentHeadResult.StandardOutput, targetHeadResult.StandardOutput)))
        {
            var cleanResult = await m_runGitAsync(reviewFolder, cancellationToken, ["clean", "-fd"]);
            if (!cleanResult.IsSuccess)
                log($"Warning: could not fully clean review worktree. {cleanResult.GetCombinedOutput()}");
        }
    }

    public async Task EnsureSubmodulesReadyIfNeededAsync(
        string repositoryPath,
        Action<string> log,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(repositoryPath) || !repositoryPath.ToDir().Exists())
            return;
        if (!RepositoryUtilities.IsGitRepository(repositoryPath))
            return;

        var statusResult = await m_runGitAsync(repositoryPath, cancellationToken, ["submodule", "status", "--recursive"]);
        EnsureSuccess(statusResult, $"Failed to inspect submodules in '{repositoryPath}'.");
        if (!RequiresSubmoduleUpdate(statusResult.StandardOutput))
            return;

        log?.Invoke("Syncing submodules for solution open...");
        var syncResult = await m_runGitAsync(repositoryPath, cancellationToken, ["submodule", "sync", "--recursive"]);
        EnsureSuccess(syncResult, $"Failed to sync submodules in '{repositoryPath}'.");

        log?.Invoke("Updating submodules for solution open...");
        var updateResult = await m_runGitAsync(repositoryPath, cancellationToken, ["submodule", "update", "--init", "--recursive"]);
        EnsureSuccess(updateResult, $"Failed to update submodules in '{repositoryPath}'.");
    }

    private async Task<bool> CommitExistsAsync(string localRepository, string commitHash, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(localRepository) || string.IsNullOrWhiteSpace(commitHash))
            return false;

        var commitResult = await m_runGitAsync(
            localRepository,
            cancellationToken,
            ["cat-file", "-e", $"{commitHash}^{{commit}}"]);
        return commitResult.IsSuccess;
    }

    private static bool AreSameCommit(string leftCommit, string rightCommit) =>
        string.Equals(
            NormalizeCommitHash(leftCommit),
            NormalizeCommitHash(rightCommit),
            StringComparison.OrdinalIgnoreCase);

    private static bool RequiresSubmoduleUpdate(string statusOutput)
    {
        var lines = (statusOutput ?? string.Empty)
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
            return false;

        return lines.Any(line =>
            !string.IsNullOrWhiteSpace(line) &&
            line[0] is '-' or '+' or 'U');
    }

    private static string NormalizeCommitHash(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var firstLine = value
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;
        return firstLine.Trim();
    }

    private static IEnumerable<string> ParseWorktreePaths(string output)
    {
        var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            if (line.StartsWith("worktree ", StringComparison.OrdinalIgnoreCase))
                yield return line["worktree ".Length..].Trim();
        }
    }

    private static IEnumerable<string> EnumerateTopLevelGitRepositories(string repositoryRoot)
    {
        foreach (var directory in repositoryRoot.ToDir().TryGetDirs())
        {
            if (directory.Name.Equals(CodeReviewFolderName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!RepositoryUtilities.IsGitRepository(directory.FullName))
                continue;

            yield return directory.FullName;
        }
    }

    private static string GetAvailableRepositoryPath(string preferredPath)
    {
        var preferredDirectory = preferredPath.ToDir();
        if (!preferredDirectory.Exists())
            return preferredPath;

        var parent = preferredDirectory.Parent?.FullName ?? throw new InvalidOperationException("Invalid repository path.");
        var folderName = preferredDirectory.Name;

        for (var index = 2; index < 1000; index++)
        {
            var candidate = parent.ToDir().GetDir($"{folderName}-{index}");
            if (!candidate.Exists())
                return candidate.FullName;
        }

        throw new InvalidOperationException("Could not find an available folder name for clone target.");
    }

    private static string FindBestSolutionFile(string rootFolder, IReadOnlyCollection<string> changedPaths)
    {
        if (changedPaths != null && changedPaths.Count > 0)
        {
            var candidates = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var changedPath in changedPaths)
            {
                var closest = FindClosestSolutionForPath(rootFolder, changedPath);
                if (closest == null)
                    continue;

                candidates.TryGetValue(closest, out var currentScore);
                candidates[closest] = currentScore + 1;
            }

            if (candidates.Count > 0)
            {
                return candidates
                    .OrderByDescending(entry => entry.Value)
                    .ThenByDescending(entry => GetDepth(Path.GetRelativePath(rootFolder, entry.Key)))
                    .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                    .First()
                    .Key;
            }
        }

        return RepositoryUtilities.FindTopLevelSolutionFile(rootFolder);
    }

    private static string FindClosestSolutionForPath(string rootFolder, string changedPath)
    {
        if (string.IsNullOrWhiteSpace(changedPath))
            return null;

        var normalizedRelativePath = NormalizeRelativePath(changedPath);
        if (string.IsNullOrWhiteSpace(normalizedRelativePath))
            return null;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(rootFolder.ToDir().GetFile(normalizedRelativePath).FullName);
        }
        catch
        {
            return null;
        }

        if (!IsChildPathOf(fullPath, rootFolder))
            return null;

        var currentDirectory = fullPath.ToDir().Exists() ? fullPath : fullPath.ToFile().Directory?.FullName;
        currentDirectory = FindNearestExistingDirectory(currentDirectory, rootFolder);
        if (string.IsNullOrWhiteSpace(currentDirectory))
            return null;

        while (!string.IsNullOrWhiteSpace(currentDirectory) && IsChildPathOf(currentDirectory, rootFolder))
        {
            var matches = GetSolutionFiles(currentDirectory);
            if (matches.Length > 0)
                return matches[0];

            if (AreSamePath(currentDirectory, rootFolder))
                break;

            currentDirectory = Path.GetDirectoryName(currentDirectory);
        }

        return null;
    }

    private static string FindNearestExistingDirectory(string directoryPath, string rootFolder)
    {
        var currentDirectory = directoryPath;
        while (!string.IsNullOrWhiteSpace(currentDirectory) && IsChildPathOf(currentDirectory, rootFolder))
        {
            if (currentDirectory.ToDir().Exists())
                return currentDirectory;

            if (AreSamePath(currentDirectory, rootFolder))
                break;

            currentDirectory = Path.GetDirectoryName(currentDirectory);
        }

        return null;
    }

    private static string[] GetSolutionFiles(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || !directoryPath.ToDir().Exists())
            return [];

        try
        {
            return Directory
                .EnumerateFiles(directoryPath, "*.sln", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (DirectoryNotFoundException)
        {
            return [];
        }
    }

    private static string NormalizeRelativePath(string path)
    {
        var normalized = path.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        normalized = normalized.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return normalized.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool AreSamePath(string firstPath, string secondPath)
    {
        static string NormalizeFullPath(string path) =>
            Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(NormalizeFullPath(firstPath), NormalizeFullPath(secondPath), GetPathComparison());
    }

    private static int GetDepth(string relativePath)
    {
        var depth = 0;
        foreach (var character in relativePath)
        {
            if (character == Path.DirectorySeparatorChar || character == Path.AltDirectorySeparatorChar)
                depth++;
        }

        return depth;
    }

    private static bool IsChildPathOf(string childPath, string parentPath)
    {
        var child = AppendDirectorySeparator(Path.GetFullPath(childPath));
        var parent = AppendDirectorySeparator(Path.GetFullPath(parentPath));

        return child.StartsWith(parent, GetPathComparison());
    }

    private static string AppendDirectorySeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;

    private static StringComparison GetPathComparison() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private static void EnsureSuccess(GitCommandResult result, string message)
    {
        if (result.IsSuccess)
            return;

        throw new InvalidOperationException($"{message}{Environment.NewLine}git {result.CommandText}{Environment.NewLine}{result.GetCombinedOutput()}");
    }
}
