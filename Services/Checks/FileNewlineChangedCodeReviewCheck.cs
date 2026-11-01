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
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

/// <summary>
/// Reports changed text files whose newline style changed.
/// </summary>
/// <remarks>
/// Keeps file-wide LF/CRLF churn visible so reviewers can separate formatting noise from meaningful edits.
/// </remarks>
public sealed class FileNewlineChangedCodeReviewCheck : CodeReviewCheckBase, IFixableCodeReviewCheck
{
    public override string RuleId => CodeReviewRuleIds.FileNewlineChanged;

    public override string DisplayName => "File newline style changed";

    public override CodeReviewCheckScope Scope => CodeReviewCheckScope.ChangedFileSet;

    public bool CanFix(CodeSmellFinding finding) =>
        finding != null &&
        string.Equals(finding.RuleId, RuleId, StringComparison.OrdinalIgnoreCase) &&
        finding.LineNumber > 0;

    public bool TryFix(CodeSmellFinding finding, FileInfo resolvedFile, out string resultMessage)
    {
        if (!this.TryPrepareFix(
                finding,
                resolvedFile,
                out var sourceText,
                out _,
                out resultMessage))
        {
            return false;
        }

        var baselineNewline = TextFileChangeUtilities.DetectPreferredNewline(finding?.BaselineText);
        if (string.IsNullOrEmpty(baselineNewline))
        {
            resultMessage = "Could not detect the original line ending style for this file.";
            return false;
        }

        var updatedText = TextFileChangeUtilities.NormalizeLineEndings(sourceText.ToString(), baselineNewline);
        if (string.Equals(updatedText, sourceText.ToString(), StringComparison.Ordinal))
        {
            resultMessage = "File already matches the original line ending style.";
            return false;
        }

        if (!this.TryWriteUpdatedText(resolvedFile, updatedText, out resultMessage))
            return false;

        resultMessage = $"Restored original line ending style ({TextFileChangeUtilities.GetNewlineDisplayName(TextFileChangeUtilities.DetectNewlineKind(finding.BaselineText))}).";
        return true;
    }

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        foreach (var file in context.AllChangedFiles)
        {
            if (!TextFileChangeUtilities.TryGetComparableText(file, out var baselineText, out var currentText))
                continue;

            var baselineNewlines = TextFileChangeUtilities.DetectNewlineKind(baselineText);
            var currentNewlines = TextFileChangeUtilities.DetectNewlineKind(currentText);
            if (baselineNewlines == currentNewlines ||
                baselineNewlines == NewlineKind.None ||
                currentNewlines == NewlineKind.None ||
                !TextFileChangeUtilities.IsOnlyNewlineStyleChange(baselineText, currentText))
            {
                continue;
            }

            AddFinding(
                report,
                CodeReviewFindingSeverity.Hint,
                file.Path,
                1,
                $"File newline style changed from {TextFileChangeUtilities.GetNewlineDisplayName(baselineNewlines)} to {TextFileChangeUtilities.GetNewlineDisplayName(currentNewlines)}.",
                currentText: file.Text,
                baselineText: file.BaselineText);
        }
    }
}
