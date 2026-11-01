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
using DTC.Core.Markdown;

namespace ReviewG33k.Services;

/// <summary>
/// Runs ReviewG33k from command-line arguments without starting the Avalonia UI.
/// </summary>
/// <remarks>
/// Intended for Codex and other post-coding automation that needs a terminal-friendly review
/// result and a useful process exit code.
/// </remarks>
internal sealed class CommandLineReviewService
{
    internal const int SuccessExitCode = 0;
    internal const int FindingsExitCode = 1;
    internal const int FailureExitCode = 2;

    private readonly MainWindowReviewWorkflowService m_reviewWorkflowService;
    private readonly CommandLineReviewMarkdownFormatter m_formatter = new();
    private readonly CommandLineReviewJsonFormatter m_jsonFormatter = new();
    private readonly ConsoleMarkdown m_consoleMarkdown = new();

    internal CommandLineReviewService(MainWindowReviewWorkflowService reviewWorkflowService)
    {
        m_reviewWorkflowService = reviewWorkflowService ?? throw new ArgumentNullException(nameof(reviewWorkflowService));
    }

    internal async Task<int> RunAsync(string[] args)
    {
        var options = CommandLineReviewOptions.Parse(args);
        if (options.ShowHelp)
        {
            m_consoleMarkdown.Write(m_formatter.FormatHelp());
            return SuccessExitCode;
        }

        if (!string.IsNullOrWhiteSpace(options.Error))
        {
            WriteError(options, options.Error);
            return FailureExitCode;
        }

        var logLines = new List<string>();
        try
        {
            var preparationResult = await m_reviewWorkflowService.PrepareReviewByModeAsync(
                isPullRequestReviewMode: false,
                isLocalCommittedReviewMode: options.Mode == CommandLineReviewMode.Committed,
                isLocalRepositoryReviewMode: options.Mode == CommandLineReviewMode.Tree,
                repositoryRootPath: null,
                pullRequestUrl: null,
                localRepositoryPath: options.RepositoryPath,
                localBaseBranch: options.BaseBranch,
                includeFullModifiedFiles: options.IncludeFullModifiedFiles,
                appendLog: logLines.Add,
                updateBusyProgress: (_, _, message) =>
                {
                    if (!string.IsNullOrWhiteSpace(message))
                        logLines.Add(message);
                },
                CancellationToken.None);

            if (!preparationResult.IsSuccess)
            {
                var error = preparationResult.Error?.DialogMessage ??
                            preparationResult.Error?.StatusMessage ??
                            "Review preparation failed.";
                WriteError(options, error);
                return FailureExitCode;
            }

            var applyResult = m_reviewWorkflowService.BuildApplyResult(preparationResult, options.RepositoryPath);
            WriteResult(options, applyResult);
            return applyResult.Report?.Findings.Count > 0
                ? FindingsExitCode
                : SuccessExitCode;
        }
        catch (OperationCanceledException)
        {
            WriteError(options, "Review was cancelled.");
            return FailureExitCode;
        }
        catch (Exception ex)
        {
            WriteError(options, ex.Message);
            return FailureExitCode;
        }
    }

    private void WriteResult(CommandLineReviewOptions options, MainWindowReviewWorkflowApplyResult result)
    {
        if (options.OutputFormat == CommandLineOutputFormat.Json)
            Console.WriteLine(m_jsonFormatter.FormatResult(options, result));
        else
            m_consoleMarkdown.Write(m_formatter.FormatResult(options, result));
    }

    private void WriteError(CommandLineReviewOptions options, string message)
    {
        if (options.OutputFormat == CommandLineOutputFormat.Json)
            Console.Error.WriteLine(m_jsonFormatter.FormatError(message));
        else
            m_consoleMarkdown.Write(m_formatter.FormatError(message));
    }
}
