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
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

/// <summary>
/// Reports production C# files where only formatting changed.
/// </summary>
/// <remarks>
/// Helps reviewers separate formatting-only source churn from changes that alter code or comments.
/// </remarks>
public sealed class FormattingOnlyChangeCodeReviewCheck : CodeReviewCheckBase
{
    public override string RuleId => CodeReviewRuleIds.FormattingOnlyChange;

    public override string DisplayName => "Only formatting changed";

    public override CodeReviewCheckScope Scope => CodeReviewCheckScope.ChangedFileSet;

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        foreach (var file in context.AllChangedFiles)
        {
            if (!IsEligibleProductionCSharpFile(file))
                continue;
            if (!TextFileChangeUtilities.TryGetComparableText(file, out var baselineText, out var currentText))
                continue;
            if (TextFileChangeUtilities.IsOnlyTrailingWhitespaceChange(baselineText, currentText) ||
                TextFileChangeUtilities.IsOnlyNewlineStyleChange(baselineText, currentText))
            {
                continue;
            }

            if (!HasOnlyFormattingChanges(baselineText, currentText))
                continue;

            var lineNumber = file.AddedLineNumbers?.Where(line => line > 0).DefaultIfEmpty(1).Min() ?? 1;
            AddFinding(
                report,
                CodeReviewFindingSeverity.Hint,
                file.Path,
                lineNumber,
                "Only formatting appears to have changed in this production file.",
                currentText: file.Text,
                baselineText: file.BaselineText);
        }
    }

    private static bool IsEligibleProductionCSharpFile(CodeReviewChangedFile file) =>
        file != null &&
        !string.IsNullOrWhiteSpace(file.Path) &&
        file.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) &&
        !CodeReviewFileClassification.IsGeneratedFilePath(file.Path) &&
        !CodeReviewFileClassification.IsLikelyTestCodeFile(file);

    private static bool HasOnlyFormattingChanges(string baselineText, string currentText)
    {
        if (string.Equals(baselineText, currentText, StringComparison.Ordinal))
            return false;

        var baselineSignature = GetNonFormattingSignature(baselineText);
        var currentSignature = GetNonFormattingSignature(currentText);
        return baselineSignature.SequenceEqual(currentSignature, StringComparer.Ordinal);
    }

    private static string[] GetNonFormattingSignature(string text)
    {
        var root = CSharpSyntaxTree.ParseText(text ?? string.Empty).GetRoot();
        var signature = new List<string>();
        foreach (var token in root.DescendantTokens(descendIntoTrivia: false))
        {
            AddNonFormattingTrivia(signature, token.LeadingTrivia);
            signature.Add($"token:{token.RawKind}:{token.Text}");
            AddNonFormattingTrivia(signature, token.TrailingTrivia);
        }

        return signature.ToArray();
    }

    private static void AddNonFormattingTrivia(ICollection<string> signature, SyntaxTriviaList triviaList)
    {
        foreach (var trivia in triviaList)
        {
            if (trivia.IsKind(SyntaxKind.WhitespaceTrivia) ||
                trivia.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                continue;
            }

            signature.Add($"trivia:{trivia.RawKind}:{trivia.ToFullString()}");
        }
    }
}
