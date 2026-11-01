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
using System.Text;
using DTC.Core.Extensions;

namespace ReviewG33k.Services;

/// <summary>
/// Builds preview/prompt text from review findings and resolved files.
/// </summary>
/// <remarks>
/// Useful for keeping file-resolution, preview rendering, and Codex prompt generation logic out
/// of `ReviewResultsWindow` so the window can focus on UI interaction concerns.
/// </remarks>
internal sealed class ReviewResultsFileContextService
{
    public bool TryResolveFindingFile(
        CodeSmellFinding finding,
        Func<CodeSmellFinding, string> resolveFindingPath,
        out FileInfo resolvedFile)
    {
        resolvedFile = null;
        if (finding == null || resolveFindingPath == null)
            return false;

        var resolvedPath = resolveFindingPath(finding);
        if (string.IsNullOrWhiteSpace(resolvedPath))
            return false;

        resolvedFile = resolvedPath.ToFile();
        return resolvedFile.Exists();
    }

    public bool TryBuildPreview(
        CodeSmellFinding finding,
        Func<CodeSmellFinding, string> resolveFindingPath,
        int previewLinesBefore,
        int previewLinesAfter,
        out ReviewResultsPreviewData previewData,
        out string failureReason,
        Func<CodeSmellFinding, string> readSnapshotText = null)
    {
        previewData = default;
        failureReason = null;

        if (finding == null)
        {
            failureReason = "No finding selected.";
            return false;
        }

        string[] lines;
        string fileName;
        try
        {
            if (readSnapshotText != null)
            {
                var text = readSnapshotText(finding);
                if (text == null)
                {
                    failureReason = $"No reviewed content for '{finding.FilePath}'.";
                    return false;
                }
                var snapshotLines = new System.Collections.Generic.List<string>();
                using var reader = new StringReader(text);
                while (reader.ReadLine() is { } line)
                    snapshotLines.Add(line);
                lines = snapshotLines.ToArray();
                fileName = Path.GetFileName(finding.FilePath);
            }
            else
            {
                if (!TryResolveFindingFile(finding, resolveFindingPath, out var resolvedFile))
                {
                    failureReason = $"Could not resolve file for '{finding.FilePath}'.";
                    return false;
                }
                lines = resolvedFile.ReadAllLines() ?? [];
                fileName = resolvedFile.Name;
            }
        }
        catch (Exception exception)
        {
            failureReason = $"Could not read file: {exception.Message}";
            return false;
        }

        var header = $"Preview: {fileName}";
        if (lines.Length == 0)
        {
            previewData = new ReviewResultsPreviewData(header, "(File is empty)", fileName);
            return true;
        }

        var lineNumber = finding.LineNumber > 0 ? finding.LineNumber : 1;
        var boundedLineNumber = Math.Clamp(lineNumber, 1, lines.Length);
        var startLine = Math.Max(1, boundedLineNumber - Math.Max(0, previewLinesBefore));
        var endLine = Math.Min(lines.Length, boundedLineNumber + Math.Max(0, previewLinesAfter));
        var lineNumberWidth = endLine.ToString().Length;

        var builder = new StringBuilder();
        for (var line = startLine; line <= endLine; line++)
        {
            var marker = line == boundedLineNumber ? ">" : " ";
            builder.Append(marker)
                .Append(' ')
                .Append(line.ToString().PadLeft(lineNumberWidth))
                .Append(": ")
                .AppendLine(lines[line - 1]);
        }

        previewData = new ReviewResultsPreviewData(
            header,
            builder.ToString().TrimEnd('\r', '\n'),
            fileName);
        return true;
    }

    public bool TryBuildCodexPrompt(
        CodeSmellFinding finding,
        Func<CodeSmellFinding, string> resolveFindingPath,
        int promptLinesBefore,
        int promptLinesAfter,
        out string promptText,
        out string failureReason)
    {
        promptText = string.Empty;
        failureReason = null;

        if (finding == null)
        {
            failureReason = "No finding selected.";
            return false;
        }

        if (!TryResolveFindingFile(finding, resolveFindingPath, out var resolvedFile))
        {
            failureReason = $"Could not resolve file for '{finding.FilePath}'.";
            return false;
        }

        if (!TryFindRepositoryRoot(resolvedFile, out var repositoryPath))
        {
            failureReason = "Could not detect repository root from the selected file.";
            return false;
        }

        var issueLine = finding.LineNumber > 0 ? finding.LineNumber : 1;
        var issuePath = GetPromptRelativePath(repositoryPath.FullName, resolvedFile.FullName, finding.FilePath);
        var issueMessage = string.IsNullOrWhiteSpace(finding.Message)
            ? "(no message)"
            : finding.Message.Trim();
        var codeContext = BuildPromptCodeContext(
            resolvedFile,
            issueLine,
            promptLinesBefore,
            promptLinesAfter);

        var builder = new StringBuilder();
        builder.AppendLine("You are fixing one local code review issue.");
        builder.AppendLine();
        builder.Append("Repository path: ").AppendLine(repositoryPath.FullName);
        builder.Append("File: ").Append(issuePath).Append(':').Append(issueLine).AppendLine();
        builder.Append("Issue: ").AppendLine(issueMessage);
        builder.AppendLine();
        builder.AppendLine("Code context:");
        builder.AppendLine("```");
        builder.AppendLine(codeContext);
        builder.AppendLine("```");
        builder.AppendLine();
        builder.AppendLine("Task:");
        builder.AppendLine("- Implement a concise fix for this issue in this repository.");
        builder.AppendLine("- Keep behavior unchanged except for resolving this issue.");
        builder.AppendLine("- Run relevant build/tests if practical.");
        builder.AppendLine();
        builder.AppendLine("Return:");
        builder.AppendLine("* Summary of the fix");
        builder.AppendLine("* Test/build commands run (or why none were run)");
        builder.AppendLine("* Risks or follow-up checks or code improvement suggestions");
        builder.AppendLine("* Any other relevant information");

        promptText = builder.ToString().TrimEnd('\r', '\n');
        return true;
    }

    private static string BuildPromptCodeContext(
        FileInfo resolvedFile,
        int lineNumber,
        int linesBefore,
        int linesAfter)
    {
        string[] lines;
        try
        {
            lines = resolvedFile.ReadAllLines() ?? [];
        }
        catch
        {
            return "(Unable to read file contents.)";
        }

        if (lines.Length == 0)
            return "(File is empty.)";

        var boundedLineNumber = Math.Clamp(lineNumber, 1, lines.Length);
        var startLine = Math.Max(1, boundedLineNumber - Math.Max(0, linesBefore));
        var endLine = Math.Min(lines.Length, boundedLineNumber + Math.Max(0, linesAfter));
        var lineNumberWidth = endLine.ToString().Length;

        var builder = new StringBuilder();
        for (var line = startLine; line <= endLine; line++)
        {
            var marker = line == boundedLineNumber ? ">" : " ";
            builder.Append(marker)
                .Append(' ')
                .Append(line.ToString().PadLeft(lineNumberWidth))
                .Append(": ")
                .AppendLine(lines[line - 1]);
        }

        return builder.ToString().TrimEnd('\r', '\n');
    }

    private static bool TryFindRepositoryRoot(FileInfo resolvedFile, out DirectoryInfo repositoryPath)
    {
        repositoryPath = null;
        if (resolvedFile?.Exists() != true)
            return false;

        var current = resolvedFile.Directory;
        while (current != null)
        {
            if (current.GetDir(".git").Exists() || current.GetFile(".git").Exists())
            {
                repositoryPath = current;
                return true;
            }

            current = current.Parent;
        }

        return false;
    }

    private static string GetPromptRelativePath(string repositoryPath, string resolvedPath, string fallbackPath)
    {
        if (string.IsNullOrWhiteSpace(repositoryPath) || string.IsNullOrWhiteSpace(resolvedPath))
            return RepositoryUtilities.NormalizeRepoPath(fallbackPath);

        try
        {
            var relativePath = Path.GetRelativePath(repositoryPath, resolvedPath);
            if (!string.IsNullOrWhiteSpace(relativePath) &&
                !relativePath.StartsWith("..", StringComparison.Ordinal))
            {
                return RepositoryUtilities.NormalizeRepoPath(relativePath);
            }
        }
        catch
        {
            // Fall back to the finding path below.
        }

        return RepositoryUtilities.NormalizeRepoPath(fallbackPath);
    }
}

internal readonly record struct ReviewResultsPreviewData(
    string Header,
    string Text,
    string PreviewFileName);
