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
using System.Linq;
using System.Text;

namespace ReviewG33k.Services;

/// <summary>
/// Formats command-line review output as Markdown for DTC.Core's console renderer.
/// </summary>
/// <remarks>
/// Provides concise terminal output for Codex and other automation while preserving the same
/// finding details users see in the ReviewG33k UI.
/// </remarks>
internal sealed class CommandLineReviewMarkdownFormatter
{
    internal string FormatHelp() =>
        """
        # ReviewG33k command line

        Run ReviewG33k without opening the UI.

        ```text
        ReviewG33k --cli --repo <path> [--mode uncommitted|committed|tree] [--base <branch>] [--json]
        ReviewG33k /cli /repo <path> [/mode uncommitted|committed|tree] [/base <branch>]
        ```

        Options:

        - `--help`, `/help`, `/?` shows this usage.
        - `--cli`, `/cli` runs without Avalonia UI.
        - `--repo`, `/repo` sets the repository path. Defaults to the current directory.
        - `--mode`, `/mode` accepts `uncommitted`, `committed`, or `tree`. Defaults to `uncommitted`.
        - `--base`, `/base` sets the base branch for committed mode. Defaults to `main`.
        - `--full`, `/full` includes full modified file contents in checks that support it.
        - `--format`, `/format` accepts `console` or `json`. Defaults to `console`.
        - `--json`, `/json` is shorthand for `--format json`.

        Exit code `0` means no findings, `1` means findings were reported, and `2` means the run failed.
        """;

    internal string FormatError(string message) =>
        $"""
        # ReviewG33k failed

        **Error:** {EscapeCell(message)}

        Run `ReviewG33k --cli --help` for usage.
        """;

    internal string FormatResult(
        CommandLineReviewOptions options,
        MainWindowReviewWorkflowApplyResult result)
    {
        var report = result?.Report;
        if (report == null)
            return FormatError("No report was produced.");

        var findings = report.Findings
            .Where(finding => finding.Severity != CodeReviewFindingSeverity.Ok)
            .OrderByDescending(finding => finding.Severity)
            .ThenBy(finding => finding.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(finding => finding.LineNumber)
            .ToList();

        var builder = new StringBuilder();
        builder.AppendLine("# ReviewG33k");
        builder.AppendLine();
        builder.AppendLine(findings.Count == 0
            ? "**No findings.** Nice and quiet."
            : $"**{findings.Count} finding(s) reported.**");
        builder.AppendLine();
        builder.AppendLine("| Setting | Value |");
        builder.AppendLine("| --- | --- |");
        builder.AppendLine($"| Repository | `{EscapeCell(options.RepositoryPath)}` |");
        builder.AppendLine($"| Mode | `{options.Mode.ToString().ToLowerInvariant()}` |");
        if (options.Mode == CommandLineReviewMode.Committed)
            builder.AppendLine($"| Base branch | `{EscapeCell(result.ResolvedLocalBaseBranch ?? options.BaseBranch)}` |");
        if (!string.IsNullOrWhiteSpace(result.SolutionPath))
            builder.AppendLine($"| Solution | `{EscapeCell(result.SolutionPath)}` |");
        builder.AppendLine();

        if (findings.Count > 0)
        {
            builder.AppendLine("## Findings");
            builder.AppendLine();
            builder.AppendLine("| Location | Description |");
            builder.AppendLine("| --- | :--- |");
            foreach (var finding in findings)
            {
                var location = string.IsNullOrWhiteSpace(finding.FilePath)
                    ? "(unknown)"
                    : finding.LineNumber > 0
                        ? $"{finding.FilePath}:{finding.LineNumber}"
                        : finding.FilePath;

                builder.AppendLine(
                    $"| `{EscapeCell(location)}` | {EscapeCell(finding.Message)} |");
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string EscapeCell(string value) =>
        (value ?? string.Empty)
        .Replace("|", "\\|", StringComparison.Ordinal)
        .Replace("\r", " ", StringComparison.Ordinal)
        .Replace("\n", " ", StringComparison.Ordinal)
        .Trim();
}
