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

internal sealed class MainWindowReviewPreparationService
{
    private readonly MainWindowInputValidationService m_inputValidationService;
    private readonly LocalBaseBranchService m_localBaseBranchService;
    private readonly ReviewExecutionService m_reviewExecutionService;

    public MainWindowReviewPreparationService(
        MainWindowInputValidationService inputValidationService,
        LocalBaseBranchService localBaseBranchService,
        ReviewExecutionService reviewExecutionService)
    {
        m_inputValidationService = inputValidationService ?? throw new ArgumentNullException(nameof(inputValidationService));
        m_localBaseBranchService = localBaseBranchService ?? throw new ArgumentNullException(nameof(localBaseBranchService));
        m_reviewExecutionService = reviewExecutionService ?? throw new ArgumentNullException(nameof(reviewExecutionService));
    }

    public async Task<MainWindowReviewPreparationResult> PreparePullRequestReviewAsync(
        string repositoryRootPath,
        string pullRequestUrl,
        bool includeFullModifiedFiles,
        Action<string> appendLog,
        Action<int, int, string> updateBusyProgress,
        CancellationToken cancellationToken)
    {
        var validation = m_inputValidationService.ValidatePullRequestPrepareInputs(
            repositoryRootPath?.Trim(),
            pullRequestUrl?.Trim());
        if (!validation.IsValid)
            return MainWindowReviewPreparationResult.Failed(FromValidation(validation));

        if (!BitbucketPrUrlParser.TryParse(pullRequestUrl?.Trim(), out var pullRequest, out var parseError))
        {
            return MainWindowReviewPreparationResult.Failed(
                new MainWindowReviewPreparationError(
                    parseError,
                    "Invalid pull request URL",
                    parseError,
                    $"Input error: {parseError}"));
        }

        var executionResult = await m_reviewExecutionService.ExecutePullRequestReviewAsync(
            repositoryRootPath,
            pullRequest,
            includeFullModifiedFiles,
            appendLog,
            updateBusyProgress,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return MainWindowReviewPreparationResult.Success(
            MainWindowReviewPreparationMode.PullRequest,
            pullRequest,
            resolvedBaseBranch: null,
            pullRequestExecutionResult: executionResult,
            localExecutionResult: null);
    }

    public async Task<MainWindowReviewPreparationResult> PrepareLocalCommittedReviewAsync(
        string localRepositoryPath,
        string requestedBaseBranch,
        bool includeFullModifiedFiles,
        Action<string> appendLog,
        Action<int, int, string> updateBusyProgress,
        CancellationToken cancellationToken)
    {
        var localRepositoryValidation = m_inputValidationService.ValidateLocalRepositoryInput(localRepositoryPath?.Trim());
        if (!localRepositoryValidation.IsValid)
            return MainWindowReviewPreparationResult.Failed(FromValidation(localRepositoryValidation));

        var resolvedBaseBranch = await m_localBaseBranchService.ResolveLocalBaseBranchAsync(
            localRepositoryPath,
            requestedBaseBranch,
            logWhenChanged: true,
            appendLog);
        if (string.IsNullOrWhiteSpace(resolvedBaseBranch))
        {
            return MainWindowReviewPreparationResult.Failed(
                new MainWindowReviewPreparationError(
                    "Enter a base branch (for example: main).",
                    "Base branch required",
                    "Enter the branch to compare against (for example: main or develop).",
                    null));
        }

        var validation = m_inputValidationService.ValidateLocalCommittedReviewInputs(localRepositoryPath?.Trim(), resolvedBaseBranch?.Trim());
        if (!validation.IsValid)
            return MainWindowReviewPreparationResult.Failed(FromValidation(validation));

        var executionResult = await m_reviewExecutionService.ExecuteLocalCommittedReviewAsync(
            localRepositoryPath,
            resolvedBaseBranch,
            includeFullModifiedFiles,
            appendLog,
            updateBusyProgress,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return MainWindowReviewPreparationResult.Success(
            MainWindowReviewPreparationMode.LocalCommitted,
            pullRequest: null,
            resolvedBaseBranch,
            pullRequestExecutionResult: null,
            localExecutionResult: executionResult);
    }

    public async Task<MainWindowReviewPreparationResult> PrepareLocalUncommittedReviewAsync(
        string localRepositoryPath,
        bool includeFullModifiedFiles,
        Action<string> appendLog,
        Action<int, int, string> updateBusyProgress,
        CancellationToken cancellationToken)
    {
        var validation = m_inputValidationService.ValidateLocalRepositoryInput(localRepositoryPath?.Trim());
        if (!validation.IsValid)
            return MainWindowReviewPreparationResult.Failed(FromValidation(validation));

        var executionResult = await m_reviewExecutionService.ExecuteLocalUncommittedReviewAsync(
            localRepositoryPath,
            includeFullModifiedFiles,
            appendLog,
            updateBusyProgress,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return MainWindowReviewPreparationResult.Success(
            MainWindowReviewPreparationMode.LocalUncommitted,
            pullRequest: null,
            resolvedBaseBranch: null,
            pullRequestExecutionResult: null,
            localExecutionResult: executionResult);
    }

    public async Task<MainWindowReviewPreparationResult> PrepareLocalRepositoryReviewAsync(
        string localRepositoryPath,
        bool includeFullModifiedFiles,
        Action<string> appendLog,
        Action<int, int, string> updateBusyProgress,
        CancellationToken cancellationToken)
    {
        var validation = m_inputValidationService.ValidateLocalRepositoryInput(localRepositoryPath?.Trim());
        if (!validation.IsValid)
            return MainWindowReviewPreparationResult.Failed(FromValidation(validation));

        var executionResult = await m_reviewExecutionService.ExecuteLocalRepositoryReviewAsync(
            localRepositoryPath,
            includeFullModifiedFiles,
            appendLog,
            updateBusyProgress,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return MainWindowReviewPreparationResult.Success(
            MainWindowReviewPreparationMode.LocalRepository,
            pullRequest: null,
            resolvedBaseBranch: null,
            pullRequestExecutionResult: null,
            localExecutionResult: executionResult);
    }

    private static MainWindowReviewPreparationError FromValidation(InputValidationResult validation) =>
        new(validation.StatusMessage, validation.DialogTitle, validation.DialogMessage, null);
}

internal enum MainWindowReviewPreparationMode
{
    PullRequest,
    LocalCommitted,
    LocalUncommitted,
    LocalRepository
}

internal readonly record struct MainWindowReviewPreparationError(
    string StatusMessage,
    string DialogTitle,
    string DialogMessage,
    string LogMessage);

internal sealed record MainWindowReviewPreparationResult(
    bool IsSuccess,
    MainWindowReviewPreparationError? Error,
    MainWindowReviewPreparationMode Mode,
    BitbucketPullRequestReference PullRequest,
    string ResolvedBaseBranch,
    PullRequestReviewExecutionResult PullRequestExecutionResult,
    LocalReviewExecutionResult LocalExecutionResult)
{
    public CodeSmellReport Report =>
        PullRequestExecutionResult?.Report ??
        LocalExecutionResult?.Report;

    public string ReviewWorktreePath =>
        PullRequestExecutionResult?.PrepareResult?.ReviewWorktreePath ??
        LocalExecutionResult?.ReviewWorktreePath;

    public string SolutionPath =>
        PullRequestExecutionResult?.PrepareResult?.SolutionPath ??
        LocalExecutionResult?.SolutionPath;

    public static MainWindowReviewPreparationResult Success(
        MainWindowReviewPreparationMode mode,
        BitbucketPullRequestReference pullRequest,
        string resolvedBaseBranch,
        PullRequestReviewExecutionResult pullRequestExecutionResult,
        LocalReviewExecutionResult localExecutionResult) =>
        new(
            true,
            null,
            mode,
            pullRequest,
            resolvedBaseBranch,
            pullRequestExecutionResult,
            localExecutionResult);

    public static MainWindowReviewPreparationResult Failed(MainWindowReviewPreparationError error) =>
        new(
            false,
            error,
            MainWindowReviewPreparationMode.PullRequest,
            null,
            null,
            null,
            null);
}
