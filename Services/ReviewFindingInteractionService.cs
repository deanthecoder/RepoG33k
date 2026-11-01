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
using System.Threading.Tasks;
using ReviewG33k.Models;

namespace ReviewG33k.Services;

internal sealed class ReviewFindingInteractionService
{
    private readonly CodeLocationOpener m_codeLocationOpener;
    private readonly ReviewFindingCommentFormatter m_commentFormatter;
    private readonly LogNavigationService m_logNavigationService;
    private readonly BitbucketPullRequestMetadataClient m_pullRequestMetadataClient;

    public ReviewFindingInteractionService(
        CodeLocationOpener codeLocationOpener,
        ReviewFindingCommentFormatter commentFormatter,
        LogNavigationService logNavigationService,
        BitbucketPullRequestMetadataClient pullRequestMetadataClient)
    {
        m_codeLocationOpener = codeLocationOpener ?? throw new ArgumentNullException(nameof(codeLocationOpener));
        m_commentFormatter = commentFormatter ?? throw new ArgumentNullException(nameof(commentFormatter));
        m_logNavigationService = logNavigationService ?? throw new ArgumentNullException(nameof(logNavigationService));
        m_pullRequestMetadataClient = pullRequestMetadataClient ?? throw new ArgumentNullException(nameof(pullRequestMetadataClient));
    }

    public bool IsTargetAvailable(CodeLocationOpenTarget target, bool isClipboardAvailable)
    {
        if (target == CodeLocationOpenTarget.Clipboard)
            return isClipboardAvailable;

        return m_codeLocationOpener.IsTargetAvailable(target);
    }

    public async Task<ReviewFindingOpenResult> OpenFindingAsync(
        CodeSmellFinding finding,
        CodeLocationOpenTarget target,
        string latestReviewWorktreePath,
        bool isClipboardAvailable,
        Func<string, Task> setClipboardTextAsync)
    {
        if (!TryResolveFindingFile(finding, latestReviewWorktreePath, out var resolvedFile, out var lineNumber, out var error))
            return ReviewFindingOpenResult.CreateFailure(error, error);

        if (target == CodeLocationOpenTarget.Clipboard)
        {
            if (!isClipboardAvailable || setClipboardTextAsync == null)
                return ReviewFindingOpenResult.CreateFailure("Clipboard is unavailable.", "Clipboard is unavailable.");

            try
            {
                await setClipboardTextAsync(resolvedFile.FullName);
                return ReviewFindingOpenResult.CreateSuccess($"Copied path to clipboard: {resolvedFile.FullName}");
            }
            catch (Exception exception)
            {
                var clipboardError = $"Failed to copy to clipboard: {exception.Message}";
                return ReviewFindingOpenResult.CreateFailure(clipboardError, clipboardError);
            }
        }

        if (!m_codeLocationOpener.TryOpenAtLocation(target, resolvedFile.FullName, lineNumber, out var launchError))
            return ReviewFindingOpenResult.CreateFailure(launchError, launchError);

        var targetName = m_codeLocationOpener.GetDisplayName(target);
        return ReviewFindingOpenResult.CreateSuccess($"Opened in {targetName}: {resolvedFile.Name}:{lineNumber}");
    }

    public ReviewFindingOpenResult OpenFindingInVsCode(CodeSmellFinding finding, string latestReviewWorktreePath)
    {
        if (!TryResolveFindingFile(finding, latestReviewWorktreePath, out var resolvedFile, out var lineNumber, out var error))
            return ReviewFindingOpenResult.CreateFailure(error, error);

        if (!m_codeLocationOpener.TryOpenAtLocation(CodeLocationOpenTarget.VsCode, resolvedFile.FullName, lineNumber, out var launchError))
            return ReviewFindingOpenResult.CreateFailure(launchError, launchError);

        return ReviewFindingOpenResult.CreateSuccess($"Opened in VS Code: {resolvedFile.Name}:{lineNumber}");
    }

    public string ResolveFindingPath(CodeSmellFinding finding, string latestReviewWorktreePath)
    {
        if (finding == null)
            return null;

        return m_logNavigationService.TryResolveLogFile(finding.FilePath, latestReviewWorktreePath, out var resolvedFile)
            ? resolvedFile.FullName
            : null;
    }

    public async Task<ReviewFindingCommentResult> CommentOnFindingAsync(
        CodeSmellFinding finding,
        BitbucketPullRequestReference pullRequest)
    {
        if (finding == null || string.IsNullOrWhiteSpace(finding.FilePath) || finding.LineNumber < 1)
        {
            return ReviewFindingCommentResult.CreateFailure(
                "Selected finding has no valid file/line for commenting.",
                null);
        }

        if (pullRequest == null)
        {
            return ReviewFindingCommentResult.CreateFailure(
                "Pull request context is unavailable for posting comments.",
                null);
        }

        var commentText = m_commentFormatter.Format(finding);
        var result = await m_pullRequestMetadataClient.TryAddInlineCommentAsync(
            pullRequest,
            finding.FilePath,
            finding.LineNumber,
            commentText);
        if (result.Success)
        {
            return ReviewFindingCommentResult.CreateSuccess(
                "Comment posted to Bitbucket.",
                $"HINT: Posted Bitbucket comment at [{finding.FilePath}:{finding.LineNumber}].");
        }

        var errorMessage = string.IsNullOrWhiteSpace(result.ErrorMessage)
            ? "Failed to post Bitbucket comment."
            : result.ErrorMessage;
        return ReviewFindingCommentResult.CreateFailure(
            "Failed to post comment. See log for details.",
            $"WARNING: Could not post Bitbucket comment at [{finding.FilePath}:{finding.LineNumber}]. {errorMessage}");
    }

    private bool TryResolveFindingFile(
        CodeSmellFinding finding,
        string latestReviewWorktreePath,
        out FileInfo resolvedFile,
        out int lineNumber,
        out string error)
    {
        resolvedFile = null;
        lineNumber = 1;
        error = null;

        if (finding == null)
        {
            error = "No finding selected.";
            return false;
        }

        lineNumber = finding.LineNumber > 0 ? finding.LineNumber : 1;
        if (m_logNavigationService.TryResolveLogFile(finding.FilePath, latestReviewWorktreePath, out resolvedFile))
            return true;

        error = $"Could not resolve file path: {finding.FilePath}";
        return false;
    }
}

internal readonly record struct ReviewFindingOpenResult(
    bool Success,
    string StatusMessage,
    string LogMessage)
{
    public static ReviewFindingOpenResult CreateSuccess(string statusMessage) =>
        new(true, statusMessage, null);

    public static ReviewFindingOpenResult CreateFailure(string statusMessage, string logMessage) =>
        new(false, statusMessage, logMessage);
}

internal readonly record struct ReviewFindingCommentResult(
    bool Success,
    string StatusMessage,
    string LogMessage)
{
    public static ReviewFindingCommentResult CreateSuccess(string statusMessage, string logMessage) =>
        new(true, statusMessage, logMessage);

    public static ReviewFindingCommentResult CreateFailure(string statusMessage, string logMessage) =>
        new(false, statusMessage, logMessage);
}
