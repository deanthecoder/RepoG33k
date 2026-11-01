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
using DTC.Core.Extensions;

namespace ReviewG33k.Services;

/// <summary>
/// Represents the outcome of startup-time Git availability checks.
/// </summary>
/// <remarks>
/// Useful for passing a complete UI-ready result from service code to the window layer, including
/// status text, log lines, and optional dialog content when Git is missing.
/// </remarks>
internal readonly record struct GitStartupStatus(
    bool IsGitAvailable,
    string StatusMessage,
    IReadOnlyList<string> LogMessages,
    string DialogTitle,
    string DialogMessage);

/// <summary>
/// Coordinates startup workflows used by the main window.
/// </summary>
/// <remarks>
/// Useful for keeping startup orchestration out of code-behind by centralizing Git availability checks,
/// safe cleanup eligibility/execution, and clipboard-based pull request URL prefill in one testable service.
/// </remarks>
internal sealed class MainWindowStartupService
{
    private const string GitMissingActionText = "Install Git for Windows (https://git-scm.com/download/win) and ensure git.exe is on PATH to enable pull request and local repository reviews.";
    private readonly Func<string, Task<GitAvailabilityCheckResult>> m_checkGitAvailabilityAsync;
    private readonly Func<string, Action<string>, CancellationToken, Task> m_clearCodeReviewFolderAsync;

    public MainWindowStartupService(
        GitAvailabilityService gitAvailabilityService,
        CodeReviewOrchestrator orchestrator)
        : this(
            workingDirectory => gitAvailabilityService.CheckAvailabilityAsync(workingDirectory),
            (repositoryRoot, appendLog, cancellationToken) =>
                orchestrator.ClearCodeReviewFolderAsync(repositoryRoot, appendLog, logWhenMissing: false, cancellationToken))
    {
        ArgumentNullException.ThrowIfNull(gitAvailabilityService);
        ArgumentNullException.ThrowIfNull(orchestrator);
    }

    internal MainWindowStartupService(
        Func<string, Task<GitAvailabilityCheckResult>> checkGitAvailabilityAsync,
        Func<string, Action<string>, CancellationToken, Task> clearCodeReviewFolderAsync)
    {
        m_checkGitAvailabilityAsync = checkGitAvailabilityAsync ?? throw new ArgumentNullException(nameof(checkGitAvailabilityAsync));
        m_clearCodeReviewFolderAsync = clearCodeReviewFolderAsync ?? throw new ArgumentNullException(nameof(clearCodeReviewFolderAsync));
    }

    public async Task<GitStartupStatus> CheckGitAvailabilityAsync(string workingDirectory)
    {
        var result = await m_checkGitAvailabilityAsync(workingDirectory);
        if (result.IsAvailable)
            return new GitStartupStatus(true, null, [], null, null);

        var logs = new List<string>
        {
            "ERROR: Git is not available."
        };
        if (!string.IsNullOrWhiteSpace(result.FailureDetails))
            logs.Add($"Details: {result.FailureDetails}");

        var dialogDetail = string.IsNullOrWhiteSpace(result.FailureDetails)
            ? GitMissingActionText
            : $"{GitMissingActionText}{Environment.NewLine}{Environment.NewLine}Details: {result.FailureDetails}";

        return new GitStartupStatus(
            IsGitAvailable: false,
            StatusMessage: "Git is missing. Git-backed review modes are unavailable.",
            LogMessages: logs,
            DialogTitle: "Git not found",
            DialogMessage: dialogDetail);
    }

    public bool CanRunStartupCleanup(string repositoryRoot)
    {
        var normalizedPath = repositoryRoot?.Trim();
        return !string.IsNullOrWhiteSpace(normalizedPath) &&
               normalizedPath.ToDir().Exists();
    }

    public async Task RunStartupCleanupAsync(string repositoryRoot, Action<string> appendLog, CancellationToken cancellationToken)
    {
        if (appendLog == null)
            throw new ArgumentNullException(nameof(appendLog));
        var normalizedPath = repositoryRoot?.Trim();
        if (!CanRunStartupCleanup(normalizedPath))
            return;

        await m_clearCodeReviewFolderAsync(normalizedPath, appendLog, cancellationToken);
    }

    public async Task<bool> TryPrefillPullRequestUrlFromClipboardAsync(
        Func<Task<string>> getClipboardTextAsync,
        Func<string, bool> tryApplyPullRequestUrlFromClipboard)
    {
        if (getClipboardTextAsync == null)
            throw new ArgumentNullException(nameof(getClipboardTextAsync));
        if (tryApplyPullRequestUrlFromClipboard == null)
            throw new ArgumentNullException(nameof(tryApplyPullRequestUrlFromClipboard));

        string clipboardText;
        try
        {
            clipboardText = await getClipboardTextAsync();
        }
        catch
        {
            return false;
        }

        return tryApplyPullRequestUrlFromClipboard(clipboardText);
    }
}
