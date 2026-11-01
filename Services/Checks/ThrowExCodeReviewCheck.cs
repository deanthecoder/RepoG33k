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

public sealed class ThrowExCodeReviewCheck : CodeReviewCheckBase, IFixableCodeReviewCheck
{
    public override string RuleId => "throw-ex-in-catch";

    public override string DisplayName => "`throw ex;` in catch blocks";

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
                out var lineIndex,
                out resultMessage))
        {
            return false;
        }

        var root = CSharpSyntaxTree.ParseText(sourceText).GetCompilationUnitRoot();
        var lineSpan = TextSpan.FromBounds(sourceText.Lines[lineIndex].Start, sourceText.Lines[lineIndex].End);

        var throwStatement = root
            .DescendantNodes()
            .OfType<ThrowStatementSyntax>()
            .FirstOrDefault(node => node.Span.IntersectsWith(lineSpan));

        if (throwStatement?.Expression is not IdentifierNameSyntax identifier)
        {
            resultMessage = "Target line is not a `throw ex;` statement.";
            return false;
        }

        var catchClause = throwStatement.Ancestors().OfType<CatchClauseSyntax>().FirstOrDefault();
        var caughtExceptionName = catchClause?.Declaration?.Identifier.ValueText;
        if (string.IsNullOrWhiteSpace(caughtExceptionName) ||
            !string.Equals(identifier.Identifier.ValueText, caughtExceptionName, StringComparison.Ordinal))
        {
            resultMessage = "Throw statement does not rethrow the caught exception variable.";
            return false;
        }

        var fixedThrow = throwStatement
            .WithExpression(null)
            .WithThrowKeyword(throwStatement.ThrowKeyword.WithTrailingTrivia(SyntaxTriviaList.Empty))
            .WithSemicolonToken(throwStatement.SemicolonToken.WithLeadingTrivia(SyntaxTriviaList.Empty));
        var updatedRoot = root.ReplaceNode(throwStatement, fixedThrow);
        var updatedText = updatedRoot.ToFullString();
        updatedText = CodeReviewFixTextUtilities.CollapseConsecutiveBlankLinesNearLine(updatedText, lineIndex);

        if (!this.TryWriteUpdatedText(resolvedFile, updatedText, out resultMessage))
        {
            return false;
        }

        resultMessage = "Replaced `throw ex;` with `throw;`.";
        return true;
    }

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        foreach (var file in context.Files)
        {
            var root = RoslynCodeReviewCheckUtilities.ParseRoot(file);
            var throwStatements = root.DescendantNodes().OfType<ThrowStatementSyntax>();
            foreach (var throwStatement in throwStatements)
            {
                if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, throwStatement.Span))
                    continue;
                if (throwStatement.Expression is not IdentifierNameSyntax identifier)
                    continue;

                var catchClause = throwStatement.Ancestors().OfType<CatchClauseSyntax>().FirstOrDefault();
                var caughtExceptionName = catchClause?.Declaration?.Identifier.ValueText;
                if (string.IsNullOrWhiteSpace(caughtExceptionName))
                    continue;
                if (!string.Equals(identifier.Identifier.ValueText, caughtExceptionName, StringComparison.Ordinal))
                    continue;

                var lineNumber = RoslynCodeReviewCheckUtilities.GetStartLine(throwStatement);
                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Important,
                    file.Path,
                    lineNumber,
                    "Use `throw;` instead of `throw ex;` to preserve the original stack trace.");
            }
        }
    }
}
