// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ReviewG33k.Services.Checks.Support;

internal static class IfElseBraceUtilities
{
    public static bool HasOnlyWhitespaceTrivia(SyntaxTriviaList trivia) =>
        trivia.All(item => item.IsKind(SyntaxKind.WhitespaceTrivia) || item.IsKind(SyntaxKind.EndOfLineTrivia));

    public static bool CanUseWithoutBraces(StatementSyntax statement) =>
        statement is not LocalDeclarationStatementSyntax and not IfStatementSyntax;

    public static bool CanSafelyRemoveBlockBraces(BlockSyntax block, bool requireSingleLineStatement)
    {
        if (block == null || block.Statements.Count != 1)
            return false;
        if (!HasSafeBraceTrivia(block))
            return false;
        if (ContainsCommentTrivia(block))
            return false;

        var statement = block.Statements[0];
        if (!CanUseWithoutBraces(statement))
            return false;
        if (requireSingleLineStatement && !StatementSpansSingleLine(statement))
            return false;

        return true;
    }

    public static StatementSyntax UnwrapSingleStatementBlock(BlockSyntax block) =>
        block?.Statements.Single();

    private static bool HasSafeBraceTrivia(BlockSyntax block)
    {
        if (!HasOnlyWhitespaceTrivia(block.OpenBraceToken.LeadingTrivia) ||
            !HasOnlyWhitespaceTrivia(block.OpenBraceToken.TrailingTrivia) ||
            !HasOnlyWhitespaceTrivia(block.CloseBraceToken.LeadingTrivia) ||
            !HasOnlyWhitespaceTrivia(block.CloseBraceToken.TrailingTrivia))
        {
            return false;
        }

        return true;
    }

    private static bool ContainsCommentTrivia(BlockSyntax block) =>
        block.DescendantTrivia(descendIntoTrivia: true).Any(IsCommentTrivia);

    private static bool IsCommentTrivia(SyntaxTrivia trivia) =>
        trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
        trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) ||
        trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) ||
        trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia);

    private static bool StatementSpansSingleLine(StatementSyntax statement)
    {
        if (statement == null)
            return false;

        var lineSpan = statement.GetLocation().GetLineSpan();
        return lineSpan.StartLinePosition.Line == lineSpan.EndLinePosition.Line;
    }
}
