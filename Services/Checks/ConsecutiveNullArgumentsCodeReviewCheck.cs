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
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

/// <summary>
/// Detects method calls that pass two or more null literals positionally in sequence.
/// </summary>
/// <remarks>
/// Consecutive positional nulls make call sites hard to understand during review because the reader cannot easily tell
/// which parameter each null is intended for. This check nudges contributors toward named arguments for clarity.
/// </remarks>
public sealed class ConsecutiveNullArgumentsCodeReviewCheck : RoslynSemanticCodeReviewCheckBase
{
    public override string RuleId => CodeReviewRuleIds.ConsecutiveNullArguments;

    public override string DisplayName => "Consecutive positional null arguments";

    protected override void AnalyzeFile(
        CodeReviewAnalysisContext context,
        CodeReviewChangedFile file,
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        CodeSmellReport report)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.ArgumentList?.Arguments.Count < 2)
                continue;
            if (!TryFindFlaggedRun(file, invocation, out var lineNumber))
                continue;

            AddFinding(
                report,
                CodeReviewFindingSeverity.Hint,
                file.Path,
                lineNumber,
                "Consecutive null arguments are passed positionally. Consider naming these arguments for clarity (for example `source: null`).");
        }
    }

    private static bool TryFindFlaggedRun(
        CodeReviewChangedFile file,
        InvocationExpressionSyntax invocation,
        out int lineNumber)
    {
        lineNumber = 0;
        var runLength = 0;
        var runHasAddedLine = false;
        var runStartLine = 0;

        foreach (var argument in invocation.ArgumentList.Arguments)
        {
            var isUnnamedNullLiteralArgument = argument.NameColon == null && IsNullLiteralExpression(argument.Expression);
            if (isUnnamedNullLiteralArgument)
            {
                runLength++;
                if (runLength == 1)
                    runStartLine = RoslynCodeReviewCheckUtilities.GetStartLine(argument);
                if (RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, argument.Span))
                    runHasAddedLine = true;
                continue;
            }

            if (runLength >= 2 && runHasAddedLine)
            {
                lineNumber = runStartLine;
                return true;
            }

            runLength = 0;
            runHasAddedLine = false;
            runStartLine = 0;
        }

        if (runLength >= 2 && runHasAddedLine)
        {
            lineNumber = runStartLine;
            return true;
        }

        return false;
    }

    private static bool IsNullLiteralExpression(ExpressionSyntax expression)
    {
        if (expression == null)
            return false;
        if (expression is ParenthesizedExpressionSyntax parenthesizedExpression)
            return IsNullLiteralExpression(parenthesizedExpression.Expression);

        return expression.IsKind(SyntaxKind.NullLiteralExpression);
    }
}
