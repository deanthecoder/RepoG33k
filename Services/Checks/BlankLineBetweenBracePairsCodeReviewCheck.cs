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
using System.Linq;
using Microsoft.CodeAnalysis.Text;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

/// <summary>
/// Detects blank spacer lines between consecutive brace-only lines and can remove them automatically.
/// </summary>
/// <remarks>
/// Useful for keeping brace formatting tight and consistent when blank lines are accidentally left between adjacent opening or closing braces.
/// </remarks>
public sealed class BlankLineBetweenBracePairsCodeReviewCheck : CodeReviewCheckBase, IFixableCodeReviewCheck
{
    public override string RuleId => CodeReviewRuleIds.BlankLineBetweenBracePairs;

    public override string DisplayName => "Blank line between brace pairs";

    public override CodeReviewCheckScope Scope => CodeReviewCheckScope.AddedLinesOnly;

    public bool CanFix(CodeSmellFinding finding) =>
        finding != null &&
        string.Equals(finding.RuleId, RuleId, StringComparison.OrdinalIgnoreCase) &&
        finding.LineNumber > 1;

    public bool TryFix(CodeSmellFinding finding, FileInfo resolvedFile, out string resultMessage)
    {
        if (!this.TryPrepareFix(
                finding,
                resolvedFile,
                out var sourceText,
                out var lineIndex,
                out resultMessage))
        {
            return false;
        }

        if (!TryFindBraceGap(sourceText.Lines.Count, index => sourceText.Lines[index].ToString(), lineIndex, out var blankLineStartIndex, out _))
        {
            resultMessage = "Target line does not contain a removable blank line between brace pairs.";
            return false;
        }

        var removalStart = sourceText.Lines[blankLineStartIndex].Start;
        var removalEnd = sourceText.Lines[lineIndex].Start;
        var updatedText = sourceText.WithChanges(new TextChange(new TextSpan(removalStart, removalEnd - removalStart), string.Empty)).ToString();

        if (!this.TryWriteUpdatedText(resolvedFile, updatedText, out resultMessage))
            return false;

        resultMessage = "Removed blank line between consecutive braces.";
        return true;
    }

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        foreach (var file in context.Files)
        {
            var lines = file.Lines;
            for (var endIndex = 1; endIndex < lines.Count; endIndex++)
            {
                if (!TryFindBraceGap(lines.Count, index => lines[index], endIndex, out var blankLineStartIndex, out var braceKind))
                    continue;
                if (!HasAnyAddedLine(file, blankLineStartIndex + 1, endIndex + 1))
                    continue;

                var braceDescription = braceKind == '{' ? "opening" : "closing";
                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Hint,
                    file.Path,
                    endIndex + 1,
                    $"Remove blank line between consecutive {braceDescription} braces.");
            }
        }
    }

    private static bool HasAnyAddedLine(CodeReviewChangedFile file, int startLineNumberInclusive, int endLineNumberInclusive) =>
        file.IsAdded ||
        Enumerable.Range(startLineNumberInclusive, endLineNumberInclusive - startLineNumberInclusive + 1)
            .Any(file.AddedLineNumbers.Contains);

    private static bool TryFindBraceGap(int lineCount, Func<int, string> getLineText, int endIndex, out int blankLineStartIndex, out char braceKind)
    {
        blankLineStartIndex = -1;
        braceKind = default;
        if (getLineText == null || endIndex <= 0 || endIndex >= lineCount)
            return false;
        if (!TryGetBraceKind(getLineText(endIndex), out braceKind))
            return false;

        var scanIndex = endIndex - 1;
        if (!string.IsNullOrWhiteSpace(getLineText(scanIndex)))
            return false;

        while (scanIndex >= 0 && string.IsNullOrWhiteSpace(getLineText(scanIndex)))
            scanIndex--;

        if (scanIndex < 0)
            return false;
        if (!TryGetBraceKind(getLineText(scanIndex), out var startBraceKind) || startBraceKind != braceKind)
            return false;

        blankLineStartIndex = scanIndex + 1;
        return blankLineStartIndex < endIndex;
    }

    private static bool TryGetBraceKind(string line, out char braceKind)
    {
        braceKind = default;
        var trimmed = line?.Trim();
        if (string.Equals(trimmed, "{", StringComparison.Ordinal))
        {
            braceKind = '{';
            return true;
        }

        if (string.Equals(trimmed, "}", StringComparison.Ordinal))
        {
            braceKind = '}';
            return true;
        }

        return false;
    }
}
