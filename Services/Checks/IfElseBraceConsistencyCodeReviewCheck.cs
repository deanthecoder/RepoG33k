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

public sealed class IfElseBraceConsistencyCodeReviewCheck : CodeReviewCheckBase, IFixableCodeReviewCheck
{
    public override string RuleId => "if-else-brace-consistency";

    public override string DisplayName => "If/else brace consistency";

    public bool CanFix(CodeSmellFinding finding) =>
        finding != null &&
        string.Equals(finding.RuleId, RuleId, StringComparison.OrdinalIgnoreCase) &&
        finding.LineNumber > 0;

    public bool TryFix(CodeSmellFinding finding, FileInfo resolvedFile, out string resultMessage)
    {
        ArgumentNullException.ThrowIfNull(finding);
        if (resolvedFile == null)
        {
            resultMessage = "A valid file path is required.";
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

        var root = CSharpSyntaxTree.ParseText(sourceText).GetCompilationUnitRoot();
        var lineSpan = TextSpan.FromBounds(sourceText.Lines[lineIndex].Start, sourceText.Lines[lineIndex].End);

        var ifStatement = root
            .DescendantNodes()
            .OfType<IfStatementSyntax>()
            .FirstOrDefault(node => node.Span.IntersectsWith(lineSpan) && HasInconsistentBraces(node));
        if (ifStatement == null)
        {
            resultMessage = "Target line does not contain an inconsistent if/else brace pattern.";
            return false;
        }

        var hasIfBraces = ifStatement.Statement is BlockSyntax;
        var hasElseBraces = ifStatement.Else?.Statement is BlockSyntax;

        IfStatementSyntax updatedIfStatement;
        if (!hasIfBraces && hasElseBraces)
        {
            var elseBlock = ifStatement.Else.Statement as BlockSyntax;
            if (IfElseBraceUtilities.CanUseWithoutBraces(ifStatement.Statement) &&
                IfElseBraceUtilities.CanSafelyRemoveBlockBraces(elseBlock, requireSingleLineStatement: false))
            {
                var unwrappedElseStatement = IfElseBraceUtilities.UnwrapSingleStatementBlock(elseBlock);
                updatedIfStatement = ifStatement.WithElse(ifStatement.Else.WithStatement(unwrappedElseStatement));
                resultMessage = "Removed unnecessary braces to make if/else branches consistent.";
            }
            else
            {
                var wrappedIfStatement = WrapInBlock(ifStatement.Statement);
                updatedIfStatement = ifStatement.WithStatement(wrappedIfStatement);
                resultMessage = "Added missing braces to make if/else branches consistent.";
            }
        }
        else if (hasIfBraces && !hasElseBraces)
        {
            var ifBlock = ifStatement.Statement as BlockSyntax;
            if (IfElseBraceUtilities.CanSafelyRemoveBlockBraces(ifBlock, requireSingleLineStatement: false) &&
                IfElseBraceUtilities.CanUseWithoutBraces(ifStatement.Else.Statement))
            {
                var unwrappedIfStatement = IfElseBraceUtilities.UnwrapSingleStatementBlock(ifBlock);
                updatedIfStatement = ifStatement.WithStatement(unwrappedIfStatement);
                resultMessage = "Removed unnecessary braces to make if/else branches consistent.";
            }
            else
            {
                var wrappedElseStatement = WrapInBlock(ifStatement.Else.Statement);
                updatedIfStatement = ifStatement.WithElse(ifStatement.Else.WithStatement(wrappedElseStatement));
                resultMessage = "Added missing braces to make if/else branches consistent.";
            }
        }
        else
        {
            resultMessage = "If/else branches are already brace-consistent.";
            return false;
        }

        var updatedRoot = root.ReplaceNode(ifStatement, updatedIfStatement);
        var updatedText = updatedRoot.ToFullString();
        updatedText = CodeReviewFixTextUtilities.CollapseConsecutiveBlankLinesNearLine(updatedText, lineIndex);

        if (!this.TryWriteUpdatedText(resolvedFile, updatedText, out resultMessage))
            return false;
        return true;
    }

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        foreach (var file in context.Files)
        {
            var root = RoslynCodeReviewCheckUtilities.ParseRoot(file);
            var ifStatements = root.DescendantNodes().OfType<IfStatementSyntax>();
            foreach (var ifStatement in ifStatements)
            {
                if (!HasInconsistentBraces(ifStatement))
                    continue;
                if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, ifStatement.Span))
                    continue;

                var lineNumber = ifStatement.IfKeyword.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Suggestion,
                    file.Path,
                    lineNumber,
                    "If/else branches should both use braces when either branch uses braces.");
            }
        }
    }

    private static bool HasInconsistentBraces(IfStatementSyntax ifStatement)
    {
        if (ifStatement?.Else == null || RoslynCodeReviewCheckUtilities.IsElseIf(ifStatement))
            return false;

        var hasIfBraces = ifStatement.Statement is BlockSyntax;
        var hasElseBraces = ifStatement.Else.Statement is BlockSyntax;
        return hasIfBraces != hasElseBraces;
    }

    private static BlockSyntax WrapInBlock(StatementSyntax statement)
    {
        var leading = statement.GetLeadingTrivia();
        var trailing = statement.GetTrailingTrivia();
        var endOfLine = DetectEndOfLine(leading, trailing);
        var statementIndent = GetLineIndent(leading);
        var blockIndent = RemoveSingleIndentLevel(statementIndent);

        var statementText = statement
            .WithLeadingTrivia(SyntaxTriviaList.Empty)
            .WithTrailingTrivia(SyntaxTriviaList.Empty)
            .ToFullString()
            .TrimEnd();
        var blockText =
            "{" + endOfLine +
            statementIndent + statementText + endOfLine +
            blockIndent + "}";

        var block = SyntaxFactory.ParseStatement(blockText) as BlockSyntax ?? SyntaxFactory.Block(statement);
        return block
            .WithLeadingTrivia(ReindentLeadingTriviaForBlock(leading))
            .WithTrailingTrivia(trailing);
    }

    private static string DetectEndOfLine(SyntaxTriviaList leading, SyntaxTriviaList trailing)
    {
        var trivia = leading.Concat(trailing).FirstOrDefault(item => item.IsKind(SyntaxKind.EndOfLineTrivia));
        return trivia.ToFullString() switch
        {
            "\r\n" => "\r\n",
            "\n" => "\n",
            "\r" => "\r",
            _ => Environment.NewLine
        };
    }

    private static string GetLineIndent(SyntaxTriviaList leadingTrivia)
    {
        var text = leadingTrivia.ToFullString();
        var lastNewLine = text.LastIndexOf('\n');
        if (lastNewLine >= 0)
            return text[(lastNewLine + 1)..];

        var lastCarriageReturn = text.LastIndexOf('\r');
        if (lastCarriageReturn >= 0)
            return text[(lastCarriageReturn + 1)..];

        return text;
    }

    private static string RemoveSingleIndentLevel(string indent)
    {
        if (string.IsNullOrEmpty(indent))
            return indent;
        if (indent.EndsWith('\t'))
            return indent[..^1];
        if (indent.Length >= 4 && indent.EndsWith("    ", StringComparison.Ordinal))
            return indent[..^4];

        return indent[..^1];
    }

    private static SyntaxTriviaList ReindentLeadingTriviaForBlock(SyntaxTriviaList leadingTrivia)
    {
        var text = leadingTrivia.ToFullString();
        if (string.IsNullOrEmpty(text))
            return leadingTrivia;

        var lastNewLine = Math.Max(text.LastIndexOf('\n'), text.LastIndexOf('\r'));
        if (lastNewLine < 0)
            return SyntaxFactory.ParseLeadingTrivia(RemoveSingleIndentLevel(text));

        var prefix = text[..(lastNewLine + 1)];
        var indent = text[(lastNewLine + 1)..];
        var blockIndent = RemoveSingleIndentLevel(indent);
        return SyntaxFactory.ParseLeadingTrivia(prefix + blockIndent);
    }
}
