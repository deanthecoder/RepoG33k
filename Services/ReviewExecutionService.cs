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
using System.Threading;
using System.Threading.Tasks;
using ReviewG33k.Models;

namespace ReviewG33k.Services;

internal sealed class ReviewExecutionService
{
    private readonly GitCommandRunner m_gitCommandRunner;
    private readonly CodeReviewOrchestrator m_orchestrator;
    private readonly CodeSmellReportAnalyzer m_codeSmellReportAnalyzer;
    private readonly BitbucketPullRequestMetadataClient m_pullRequestMetadataClient;

    public ReviewExecutionService(
        GitCommandRunner gitCommandRunner,
        CodeReviewOrchestrator orchestrator,
        CodeSmellReportAnalyzer codeSmellReportAnalyzer,
        BitbucketPullRequestMetadataClient pullRequestMetadataClient)
    {
        m_gitCommandRunner = gitCommandRunner ?? throw new ArgumentNullException(nameof(gitCommandRunner));
        m_orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        m_codeSmellReportAnalyzer = codeSmellReportAnalyzer ?? throw new ArgumentNullException(nameof(codeSmellReportAnalyzer));
        m_pullRequestMetadataClient = pullRequestMetadataClient ?? throw new ArgumentNullException(nameof(pullRequestMetadataClient));
    }

    public async Task<PullRequestReviewExecutionResult> ExecutePullRequestReviewAsync(
        string repositoryRoot,
        BitbucketPullRequestReference pullRequest,
        bool includeFullModifiedFiles,
        Action<string> appendLog,
        Action<int, int, string> updateBusyProgress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var metadata = await m_pullRequestMetadataClient.TryGetMetadataAsync(pullRequest, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        bool? isPullRequestOpen = string.IsNullOrWhiteSpace(metadata?.State)
            ? null
            : string.Equals(metadata.State.Trim(), "OPEN", StringComparison.OrdinalIgnoreCase);
        if (isPullRequestOpen == false)
        {
            var mergedFallbackResult = await TryExecuteMergedPullRequestReviewAsync(
                repositoryRoot,
                pullRequest,
                metadata,
                includeFullModifiedFiles,
                appendLog,
                updateBusyProgress,
                cancellationToken);
            if (mergedFallbackResult != null)
                return mergedFallbackResult;

            return new PullRequestReviewExecutionResult(
                metadata,
                isPullRequestOpen,
                null,
                null);
        }

        var changedPaths = await m_pullRequestMetadataClient.TryGetChangedPathsAsync(pullRequest, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        appendLog?.Invoke($"PR detected: {pullRequest.SourceUrl}");
        appendLog?.Invoke($"PR title: {FormatMetadataText(metadata?.Title)}");
        appendLog?.Invoke($"PR author: {FormatMetadataText(metadata?.Author)}");
        appendLog?.Invoke($"PR modified files: {(changedPaths.Count > 0 ? changedPaths.Count.ToString() : "N/A")}");

        var prepareResult = await m_orchestrator.PrepareReviewAsync(repositoryRoot, pullRequest, changedPaths, appendLog, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        appendLog?.Invoke($"Review worktree ready: {prepareResult.ReviewWorktreePath}");
        if (!string.IsNullOrWhiteSpace(prepareResult.SolutionPath))
            appendLog?.Invoke($"Solution selected: {prepareResult.SolutionPath}");
        else
            appendLog?.Invoke("No .sln file found in review checkout.");

        var report = await RunCodeSmellScanAsync(
            prepareResult.ReviewWorktreePath,
            metadata?.TargetBranch,
            includeFullModifiedFiles,
            appendLog,
            updateBusyProgress,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return new PullRequestReviewExecutionResult(
            metadata,
            isPullRequestOpen,
            prepareResult,
            report);
    }

    public async Task<LocalReviewExecutionResult> ExecuteLocalCommittedReviewAsync(
        string localRepositoryPath,
        string baseBranch,
        bool includeFullModifiedFiles,
        Action<string> appendLog,
        Action<int, int, string> updateBusyProgress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var solutionPath = RepositoryUtilities.FindTopLevelSolutionFile(localRepositoryPath);
        appendLog?.Invoke($"Local review repository: {localRepositoryPath}");
        appendLog?.Invoke($"Comparing current branch changes against: origin/{baseBranch}");
        appendLog?.Invoke(string.IsNullOrWhiteSpace(solutionPath)
            ? "No .sln file found in local repository."
            : $"Solution selected: {solutionPath}");

        var changedFileSource = new GitBranchComparisonChangedFileSource(
            m_gitCommandRunner,
            localRepositoryPath,
            baseBranch,
            fetchTargetBranch: true);

        var sourceResult = await changedFileSource.LoadAsync(appendLog);
        cancellationToken.ThrowIfCancellationRequested();

        var report = await RunCodeSmellScanAsync(
            sourceResult,
            includeFullModifiedFiles,
            appendLog,
            updateBusyProgress,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return new LocalReviewExecutionResult(
            localRepositoryPath,
            solutionPath,
            sourceResult,
            report);
    }

    public async Task<LocalReviewExecutionResult> ExecuteLocalUncommittedReviewAsync(
        string localRepositoryPath,
        bool includeFullModifiedFiles,
        Action<string> appendLog,
        Action<int, int, string> updateBusyProgress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var solutionPath = RepositoryUtilities.FindTopLevelSolutionFile(localRepositoryPath);
        appendLog?.Invoke($"Local review repository: {localRepositoryPath}");
        appendLog?.Invoke("Comparing local uncommitted/untracked changes against: HEAD");
        appendLog?.Invoke(string.IsNullOrWhiteSpace(solutionPath)
            ? "No .sln file found in local repository."
            : $"Solution selected: {solutionPath}");

        var changedFileSource = new GitWorkingTreeChangedFileSource(m_gitCommandRunner, localRepositoryPath);
        var sourceResult = await changedFileSource.LoadAsync(appendLog);
        cancellationToken.ThrowIfCancellationRequested();

        var report = await RunCodeSmellScanAsync(
            sourceResult,
            includeFullModifiedFiles,
            appendLog,
            updateBusyProgress,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return new LocalReviewExecutionResult(
            localRepositoryPath,
            solutionPath,
            sourceResult,
            report);
    }

    public async Task<LocalReviewExecutionResult> ExecuteLocalRepositoryReviewAsync(
        string localRepositoryPath,
        bool includeFullModifiedFiles,
        Action<string> appendLog,
        Action<int, int, string> updateBusyProgress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var solutionPath = RepositoryUtilities.FindTopLevelSolutionFile(localRepositoryPath);
        appendLog?.Invoke($"Local review repository: {localRepositoryPath}");
        appendLog?.Invoke("Reviewing all analyzable files in the local repository.");
        appendLog?.Invoke(string.IsNullOrWhiteSpace(solutionPath)
            ? "No .sln file found in local repository."
            : $"Solution selected: {solutionPath}");

        var changedFileSource = new LocalRepositoryChangedFileSource(localRepositoryPath, m_gitCommandRunner);
        var sourceResult = await changedFileSource.LoadAsync(appendLog);
        cancellationToken.ThrowIfCancellationRequested();

        var report = await RunCodeSmellScanAsync(
            sourceResult,
            includeFullModifiedFiles,
            appendLog,
            updateBusyProgress,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return new LocalReviewExecutionResult(
            localRepositoryPath,
            solutionPath,
            sourceResult,
            report);
    }

    private async Task<CodeSmellReport> RunCodeSmellScanAsync(
        string reviewWorktreePath,
        string targetBranch,
        bool includeFullModifiedFiles,
        Action<string> appendLog,
        Action<int, int, string> updateBusyProgress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        appendLog?.Invoke("Code review scan starting...");
        var report = await m_codeSmellReportAnalyzer.AnalyzeAsync(
            reviewWorktreePath,
            targetBranch,
            appendLog,
            updateBusyProgress,
            includeFullModifiedFiles,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return CodeSmellReportLogService.ProcessReport(report, m_codeSmellReportAnalyzer.Checks, appendLog);
    }

    private async Task<CodeSmellReport> RunCodeSmellScanAsync(
        CodeReviewChangedFileSourceResult sourceResult,
        bool includeFullModifiedFiles,
        Action<string> appendLog,
        Action<int, int, string> updateBusyProgress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        appendLog?.Invoke("Code review scan starting...");
        var report = await m_codeSmellReportAnalyzer.AnalyzeLoadedFilesAsync(
            sourceResult,
            appendLog,
            updateBusyProgress,
            includeFullModifiedFiles,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return CodeSmellReportLogService.ProcessReport(report, m_codeSmellReportAnalyzer.Checks, appendLog);
    }

    private static string FormatMetadataText(string value) =>
        string.IsNullOrWhiteSpace(value) ? "(none)" : value.Trim();

    private async Task<PullRequestReviewExecutionResult> TryExecuteMergedPullRequestReviewAsync(
        string repositoryRoot,
        BitbucketPullRequestReference pullRequest,
        BitbucketPullRequestMetadata metadata,
        bool includeFullModifiedFiles,
        Action<string> appendLog,
        Action<int, int, string> updateBusyProgress,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(metadata?.State?.Trim(), "MERGED", StringComparison.OrdinalIgnoreCase))
            return null;

        appendLog?.Invoke("Pull request is MERGED. Attempting merge-commit fallback review...");
        var changedPaths = await m_pullRequestMetadataClient.TryGetChangedPathsAsync(pullRequest, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var mergeCommitHash = await m_pullRequestMetadataClient.TryGetMergeCommitHashAsync(pullRequest, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(mergeCommitHash))
        {
            appendLog?.Invoke("WARNING: Could not resolve merge commit hash from Bitbucket metadata/activities.");
            return null;
        }

        appendLog?.Invoke($"PR detected: {pullRequest.SourceUrl}");
        appendLog?.Invoke($"PR title: {FormatMetadataText(metadata?.Title)}");
        appendLog?.Invoke($"PR author: {FormatMetadataText(metadata?.Author)}");
        appendLog?.Invoke($"PR state: {FormatMetadataText(metadata?.State)}");
        appendLog?.Invoke($"PR merge commit: {mergeCommitHash}");
        appendLog?.Invoke($"PR modified files: {(changedPaths.Count > 0 ? changedPaths.Count.ToString() : "N/A")}");

        var prepareResult = await m_orchestrator.PrepareMergedCommitReviewAsync(
            repositoryRoot,
            pullRequest,
            metadata?.TargetBranch,
            mergeCommitHash,
            changedPaths,
            appendLog,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        appendLog?.Invoke($"Review worktree ready: {prepareResult.ReviewWorktreePath}");
        if (!string.IsNullOrWhiteSpace(prepareResult.SolutionPath))
            appendLog?.Invoke($"Solution selected: {prepareResult.SolutionPath}");
        else
            appendLog?.Invoke("No .sln file found in review checkout.");

        var changedFileSource = new GitSingleCommitChangedFileSource(
            m_gitCommandRunner,
            prepareResult.ReviewWorktreePath,
            mergeCommitHash);
        var sourceResult = await changedFileSource.LoadAsync(appendLog);
        cancellationToken.ThrowIfCancellationRequested();
        var report = await RunCodeSmellScanAsync(
            sourceResult,
            includeFullModifiedFiles,
            appendLog,
            updateBusyProgress,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return new PullRequestReviewExecutionResult(
            metadata,
            isPullRequestOpen: false,
            prepareResult,
            report);
    }
}
