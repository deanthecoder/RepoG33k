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
using DTC.Core.Extensions;

namespace ReviewG33k.Services;

internal sealed class MainWindowActionStateService
{
    private readonly MainWindowInputValidationService m_inputValidationService;

    public MainWindowActionStateService(MainWindowInputValidationService inputValidationService)
    {
        m_inputValidationService = inputValidationService ?? throw new ArgumentNullException(nameof(inputValidationService));
    }

    public MainWindowActionStateSnapshot BuildSnapshot(
        string repositoryRootPath,
        string localRepositoryPath,
        string localBaseBranch,
        string pullRequestUrl,
        bool isAnyLocalReviewMode,
        bool requiresLocalBaseBranch,
        bool? previewPullRequestIsOpen,
        string previewPullRequestState,
        string latestSolutionPath,
        string latestReviewWorktreePath,
        bool canCancelCurrentOperation,
        bool isCancellationRequested)
    {
        var resolvedSolutionPath = ResolveAvailableSolutionPath(
            latestSolutionPath,
            latestReviewWorktreePath,
            localRepositoryPath,
            repositoryRootPath);

        var hasValidPullRequestInput = BitbucketPrUrlParser.TryParse(pullRequestUrl?.Trim(), out _, out _);
        var hasValidPullRequestPrepareInputs =
            m_inputValidationService.ValidatePullRequestPrepareInputs(repositoryRootPath?.Trim(), pullRequestUrl?.Trim()).IsValid &&
            hasValidPullRequestInput;
        var hasValidLocalPrepareInputs = IsValidLocalPrepareInputs(
            localRepositoryPath,
            localBaseBranch,
            requiresLocalBaseBranch);
        var hasAvailableSolution = !string.IsNullOrWhiteSpace(resolvedSolutionPath) && resolvedSolutionPath.ToFile().Exists();
        var canReviewCurrentPullRequest = previewPullRequestIsOpen != false || IsMergedState(previewPullRequestState);

        return new MainWindowActionStateSnapshot(
            resolvedSolutionPath,
            hasValidPullRequestInput,
            hasValidPullRequestPrepareInputs,
            hasValidLocalPrepareInputs,
            hasAvailableSolution,
            canReviewCurrentPullRequest,
            isAnyLocalReviewMode,
            canCancelCurrentOperation,
            isCancellationRequested);
    }

    private bool IsValidLocalPrepareInputs(
        string localRepositoryPath,
        string localBaseBranch,
        bool requiresLocalBaseBranch)
    {
        var validation = m_inputValidationService.ValidateLocalRepositoryInput(localRepositoryPath?.Trim());
        if (!validation.IsValid)
            return false;

        return !requiresLocalBaseBranch || !string.IsNullOrWhiteSpace(localBaseBranch);
    }

    private static bool IsMergedState(string pullRequestState) =>
        string.Equals(pullRequestState?.Trim(), "MERGED", StringComparison.OrdinalIgnoreCase);

    private static string ResolveAvailableSolutionPath(
        string latestSolutionPath,
        string latestReviewWorktreePath,
        string localRepositoryPath,
        string repositoryRootPath)
    {
        if (!string.IsNullOrWhiteSpace(latestSolutionPath) && latestSolutionPath.ToFile().Exists())
            return latestSolutionPath;

        if (!string.IsNullOrWhiteSpace(latestReviewWorktreePath) && latestReviewWorktreePath.ToDir().Exists())
        {
            var worktreeSolution = RepositoryUtilities.FindTopLevelSolutionFile(latestReviewWorktreePath);
            if (!string.IsNullOrWhiteSpace(worktreeSolution) && worktreeSolution.ToFile().Exists())
                return worktreeSolution;
        }

        if (!string.IsNullOrWhiteSpace(localRepositoryPath) && localRepositoryPath.ToDir().Exists())
        {
            var localSolution = RepositoryUtilities.FindTopLevelSolutionFile(localRepositoryPath);
            if (!string.IsNullOrWhiteSpace(localSolution) && localSolution.ToFile().Exists())
                return localSolution;
        }

        if (!string.IsNullOrWhiteSpace(repositoryRootPath) && repositoryRootPath.ToDir().Exists())
        {
            var rootSolution = RepositoryUtilities.FindTopLevelSolutionFile(repositoryRootPath);
            if (!string.IsNullOrWhiteSpace(rootSolution) && rootSolution.ToFile().Exists())
                return rootSolution;
        }

        return null;
    }
}

internal readonly record struct MainWindowActionStateSnapshot(
    string ResolvedSolutionPath,
    bool HasValidPullRequestInput,
    bool HasValidPullRequestPrepareInputs,
    bool HasValidLocalPrepareInputs,
    bool HasAvailableSolution,
    bool CanReviewCurrentPullRequest,
    bool IsAnyLocalReviewMode,
    bool CanCancelCurrentOperation,
    bool IsCancellationRequested);
