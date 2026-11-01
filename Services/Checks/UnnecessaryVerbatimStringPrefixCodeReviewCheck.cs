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
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

public sealed class UnnecessaryVerbatimStringPrefixCodeReviewCheck : RoslynSemanticCodeReviewCheckBase, IFixableCodeReviewCheck
{
    public override string RuleId => "unnecessary-verbatim-string-prefix";

    public override string DisplayName => "Unnecessary verbatim string prefix";

    public bool CanFix(CodeSmellFinding finding) =>
        finding != null &&
        string.Equals(finding.RuleId, RuleId, StringComparison.OrdinalIgnoreCase) &&
        finding.LineNumber > 0;

    public bool TryFix(CodeSmellFinding finding, FileInfo resolvedFile, out string resultMessage)
    {
        if (finding == null)
        {
            resultMessage = "Finding is required.";
            return false;
        }

        if (resolvedFile == null)
        {
            resultMessage = "File path could not be resolved.";
            return false;
        }

        if (!this.TryPrepareFix(
                finding,
                resolvedFile,
                out var sourceText,
                out var lineIndex,
                out resultMessage))
        {
            return false;
        }

        var syntaxTree = CSharpSyntaxTree.ParseText(sourceText);
        var root = syntaxTree.GetCompilationUnitRoot();

        var lineStart = sourceText.Lines[lineIndex].Start;
        var lineEnd = sourceText.Lines[lineIndex].End;
        var lineSpan = TextSpan.FromBounds(lineStart, lineEnd);

        var literal = root.DescendantNodes()
            .OfType<LiteralExpressionSyntax>()
            .FirstOrDefault(node =>
                node.Span.IntersectsWith(lineSpan) &&
                node.Token.Text.StartsWith("@\"", StringComparison.Ordinal) &&
                CanBeRegularString(node.Token.ValueText));
        if (literal != null)
        {
            var replacementTokenText = literal.Token.Text[1..];
            var updatedText = sourceText.WithChanges(new TextChange(literal.Token.Span, replacementTokenText)).ToString();

            if (!this.TryWriteUpdatedText(resolvedFile, updatedText, out resultMessage))
            {
                return false;
            }

            resultMessage = "Removed unnecessary verbatim string prefix.";
            return true;
        }

        var interpolated = root.DescendantNodes()
            .OfType<InterpolatedStringExpressionSyntax>()
            .FirstOrDefault(node =>
                node.Span.IntersectsWith(lineSpan) &&
                node.StringStartToken.Text.Contains('@', StringComparison.Ordinal) &&
                node.Contents.OfType<InterpolatedStringTextSyntax>().All(textNode => CanBeRegularString(textNode.TextToken.ValueText)));
        if (interpolated != null)
        {
            var startTokenText = interpolated.StringStartToken.Text;
            var atIndex = startTokenText.IndexOf('@');
            if (atIndex < 0)
            {
                resultMessage = "Interpolated string does not contain a verbatim prefix.";
                return false;
            }

            var replacementTokenText = startTokenText.Remove(atIndex, 1);
            var updatedText = sourceText.WithChanges(new TextChange(interpolated.StringStartToken.Span, replacementTokenText)).ToString();

            if (!this.TryWriteUpdatedText(resolvedFile, updatedText, out resultMessage))
            {
                return false;
            }

            resultMessage = "Removed unnecessary verbatim string prefix.";
            return true;
        }

        resultMessage = "Could not find an unnecessary verbatim prefix on the target line.";
        return false;
    }

    protected override void AnalyzeFile(
        CodeReviewAnalysisContext context,
        CodeReviewChangedFile file,
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        CodeSmellReport report)
    {
        AnalyzeVerbatimStringLiterals(file, root, report);
        AnalyzeVerbatimInterpolatedStrings(file, root, report);
    }

    private void AnalyzeVerbatimStringLiterals(CodeReviewChangedFile file, CompilationUnitSyntax root, CodeSmellReport report)
    {
        foreach (var literal in root.DescendantNodes().OfType<LiteralExpressionSyntax>())
        {
            if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, literal.Span))
                continue;

            var token = literal.Token;
            if (!token.Text.StartsWith("@\"", StringComparison.Ordinal))
                continue;
            if (!CanBeRegularString(token.ValueText))
                continue;

            AddFinding(
                report,
                CodeReviewFindingSeverity.Hint,
                file.Path,
                RoslynCodeReviewCheckUtilities.GetStartLine(literal),
                "Unnecessary verbatim string prefix `@`.");
        }
    }

    private void AnalyzeVerbatimInterpolatedStrings(CodeReviewChangedFile file, CompilationUnitSyntax root, CodeSmellReport report)
    {
        foreach (var interpolated in root.DescendantNodes().OfType<InterpolatedStringExpressionSyntax>())
        {
            if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, interpolated.Span))
                continue;
            if (!interpolated.StringStartToken.Text.Contains('@', StringComparison.Ordinal))
                continue;
            if (!interpolated.Contents.OfType<InterpolatedStringTextSyntax>().All(text => CanBeRegularString(text.TextToken.ValueText)))
                continue;

            AddFinding(
                report,
                CodeReviewFindingSeverity.Hint,
                file.Path,
                RoslynCodeReviewCheckUtilities.GetStartLine(interpolated),
                "Unnecessary verbatim string prefix `@`.");
        }
    }

    private static bool CanBeRegularString(string valueText) =>
        valueText != null &&
        valueText.IndexOf('\\') < 0 &&
        valueText.IndexOf('"') < 0 &&
        valueText.IndexOf('\r') < 0 &&
        valueText.IndexOf('\n') < 0;
}
