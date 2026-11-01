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
/// Detects method calls that pass two or more boolean literals positionally in sequence.
/// </summary>
/// <remarks>
/// Useful for readability-focused code reviews because consecutive `true`/`false` arguments are easy
/// to misread and often hide intent. This check nudges contributors toward named arguments so each flag's
/// meaning is clear at the call site.
/// </remarks>
public sealed class ConsecutiveBooleanArgumentsCodeReviewCheck : RoslynSemanticCodeReviewCheckBase
{
    public override string RuleId => CodeReviewRuleIds.ConsecutiveBooleanArguments;

    public override string DisplayName => "Consecutive positional boolean arguments";

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
                "Consecutive boolean literal arguments are passed positionally. Consider naming these arguments for clarity (for example `isEnabled: true`).");
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
            var isUnnamedBooleanLiteralArgument = argument.NameColon == null && IsBooleanLiteralExpression(argument.Expression);
            if (isUnnamedBooleanLiteralArgument)
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

    private static bool IsBooleanLiteralExpression(ExpressionSyntax expression)
    {
        if (expression == null)
            return false;
        if (expression is ParenthesizedExpressionSyntax parenthesizedExpression)
            return IsBooleanLiteralExpression(parenthesizedExpression.Expression);

        return expression.IsKind(SyntaxKind.TrueLiteralExpression) ||
               expression.IsKind(SyntaxKind.FalseLiteralExpression);
    }
}
