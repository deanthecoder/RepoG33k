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
using System.Threading;
using System.Threading.Tasks;
using ReviewG33k.Models;

namespace ReviewG33k.Services;

internal sealed class MainWindowReviewWorkflowService
{
    private readonly MainWindowReviewPreparationService m_reviewPreparationService;

    public MainWindowReviewWorkflowService(MainWindowReviewPreparationService reviewPreparationService)
    {
        m_reviewPreparationService = reviewPreparationService ?? throw new ArgumentNullException(nameof(reviewPreparationService));
    }

    public string GetPrepareReviewStatusText(
        bool isPullRequestReviewMode,
        bool isLocalCommittedReviewMode,
        bool isLocalRepositoryReviewMode)
    {
        if (isPullRequestReviewMode)
            return "Reviewing pull request...";
        if (isLocalCommittedReviewMode)
            return "Reviewing local committed changes...";
        if (isLocalRepositoryReviewMode)
            return "Reviewing local repository files...";

        return "Reviewing local uncommitted changes...";
    }

    public Task<MainWindowReviewPreparationResult> PrepareReviewByModeAsync(
        bool isPullRequestReviewMode,
        bool isLocalCommittedReviewMode,
        bool isLocalRepositoryReviewMode,
        string repositoryRootPath,
        string pullRequestUrl,
        string localRepositoryPath,
        string localBaseBranch,
        bool includeFullModifiedFiles,
        Action<string> appendLog,
        Action<int, int, string> updateBusyProgress,
        CancellationToken cancellationToken)
    {
        if (isPullRequestReviewMode)
        {
            return m_reviewPreparationService.PreparePullRequestReviewAsync(
                repositoryRootPath,
                pullRequestUrl,
                includeFullModifiedFiles,
                appendLog,
                updateBusyProgress,
                cancellationToken);
        }

        if (isLocalCommittedReviewMode)
        {
            return m_reviewPreparationService.PrepareLocalCommittedReviewAsync(
                localRepositoryPath,
                localBaseBranch,
                includeFullModifiedFiles,
                appendLog,
                updateBusyProgress,
                cancellationToken);
        }

        if (isLocalRepositoryReviewMode)
        {
            return m_reviewPreparationService.PrepareLocalRepositoryReviewAsync(
                localRepositoryPath,
                includeFullModifiedFiles,
                appendLog,
                updateBusyProgress,
                cancellationToken);
        }

        return m_reviewPreparationService.PrepareLocalUncommittedReviewAsync(
            localRepositoryPath,
            includeFullModifiedFiles,
            appendLog,
            updateBusyProgress,
            cancellationToken);
    }

    public MainWindowReviewWorkflowApplyResult BuildApplyResult(
        MainWindowReviewPreparationResult preparationResult,
        string localRepositoryPath)
    {
        if (preparationResult == null)
            throw new ArgumentNullException(nameof(preparationResult));

        return preparationResult.Mode switch
        {
            MainWindowReviewPreparationMode.PullRequest => BuildPullRequestApplyResult(preparationResult),
            MainWindowReviewPreparationMode.LocalCommitted => BuildLocalCommittedApplyResult(preparationResult, localRepositoryPath),
            MainWindowReviewPreparationMode.LocalUncommitted => BuildLocalUncommittedApplyResult(preparationResult, localRepositoryPath),
            MainWindowReviewPreparationMode.LocalRepository => BuildLocalRepositoryApplyResult(preparationResult),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private static MainWindowReviewWorkflowApplyResult BuildPullRequestApplyResult(
        MainWindowReviewPreparationResult preparationResult)
    {
        var executionResult = preparationResult.PullRequestExecutionResult;
        var report = preparationResult.Report;
        var isPullRequestOpen = executionResult?.IsPullRequestOpen;
        var notifyNonOpenPullRequest = isPullRequestOpen == false && report == null;
        var logMessage = isPullRequestOpen == false && report != null
            ? "HINT: Pull request is not OPEN. ReviewG33k analyzed the merge commit fallback."
            : null;

        return new MainWindowReviewWorkflowApplyResult(
            preparationResult.Mode,
            preparationResult.PullRequest,
            executionResult?.Metadata?.Title,
            executionResult?.Metadata?.State,
            preparationResult.ReviewWorktreePath,
            preparationResult.SolutionPath,
            notifyNonOpenPullRequest ? null : "Review complete.",
            logMessage,
            notifyNonOpenPullRequest,
            report,
            null,
            null,
            isPullRequestOpen == false && report != null ? "HEAD^" : executionResult?.Metadata?.TargetBranch);
    }

    private static MainWindowReviewWorkflowApplyResult BuildLocalCommittedApplyResult(
        MainWindowReviewPreparationResult preparationResult,
        string localRepositoryPath)
    {
        var cacheUpdate = new MainWindowLocalFindingCacheUpdate(
            localRepositoryPath,
            preparationResult.ResolvedBaseBranch,
            LocalReviewResampleMode.Committed,
            preparationResult.LocalExecutionResult?.ChangedFileSourceResult?.Files);
        return new MainWindowReviewWorkflowApplyResult(
            preparationResult.Mode,
            null,
            null,
            null,
            preparationResult.ReviewWorktreePath,
            preparationResult.SolutionPath,
            "Local review complete.",
            null,
            false,
            preparationResult.Report,
            cacheUpdate,
            preparationResult.ResolvedBaseBranch);
    }

    private static MainWindowReviewWorkflowApplyResult BuildLocalUncommittedApplyResult(
        MainWindowReviewPreparationResult preparationResult,
        string localRepositoryPath)
    {
        var cacheUpdate = new MainWindowLocalFindingCacheUpdate(
            localRepositoryPath,
            null,
            LocalReviewResampleMode.Uncommitted,
            preparationResult.LocalExecutionResult?.ChangedFileSourceResult?.Files);
        return new MainWindowReviewWorkflowApplyResult(
            preparationResult.Mode,
            null,
            null,
            null,
            preparationResult.ReviewWorktreePath,
            preparationResult.SolutionPath,
            "Local review complete.",
            null,
            false,
            preparationResult.Report,
            cacheUpdate,
            null);
    }

    private static MainWindowReviewWorkflowApplyResult BuildLocalRepositoryApplyResult(
        MainWindowReviewPreparationResult preparationResult) =>
        new(
            preparationResult.Mode,
            null,
            null,
            null,
            preparationResult.ReviewWorktreePath,
            preparationResult.SolutionPath,
            "Repository review complete.",
            null,
            false,
            preparationResult.Report,
            null,
            null);
}

internal sealed record MainWindowReviewWorkflowApplyResult(
    MainWindowReviewPreparationMode Mode,
    BitbucketPullRequestReference PullRequest,
    string PullRequestTitle,
    string PullRequestState,
    string ReviewWorktreePath,
    string SolutionPath,
    string StatusMessage,
    string LogMessage,
    bool NotifyNonOpenPullRequest,
    CodeSmellReport Report,
    MainWindowLocalFindingCacheUpdate? LocalFindingCacheUpdate,
    string ResolvedLocalBaseBranch,
    string PullRequestBaseBranch = null)
{
    public bool HasReportFindings => Report?.Findings.Count > 0;
}

internal readonly record struct MainWindowLocalFindingCacheUpdate(
    string RepositoryPath,
    string BaseBranch,
    LocalReviewResampleMode Mode,
    IReadOnlyList<CodeReviewChangedFile> Files);
