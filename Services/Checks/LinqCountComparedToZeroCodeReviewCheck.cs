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
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

/// <summary>
/// Reports LINQ `Count()` comparisons to zero where `Any()` communicates intent better.
/// </summary>
/// <remarks>
/// Avoids unnecessary full enumeration of deferred sequences in changed code by nudging zero-count checks toward `Any()`.
/// </remarks>
public sealed class LinqCountComparedToZeroCodeReviewCheck : RoslynSemanticCodeReviewCheckBase
{
    public override string RuleId => CodeReviewRuleIds.LinqCountComparedToZero;

    public override string DisplayName => "LINQ Count compared to zero";

    protected override void AnalyzeFile(
        CodeReviewAnalysisContext context,
        CodeReviewChangedFile file,
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        CodeSmellReport report)
    {
        foreach (var binaryExpression in root.DescendantNodes().OfType<BinaryExpressionSyntax>())
        {
            if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, binaryExpression.Span))
                continue;
            if (!TryGetCountZeroComparison(semanticModel, binaryExpression, out var useNegatedAny))
                continue;

            var replacement = useNegatedAny ? "!Any()" : "Any()";
            AddFinding(
                report,
                CodeReviewFindingSeverity.Hint,
                file.Path,
                RoslynCodeReviewCheckUtilities.GetStartLine(binaryExpression),
                $"LINQ `Count()` is compared to zero; use `{replacement}` to avoid fully enumerating the sequence.");
        }
    }

    private static bool TryGetCountZeroComparison(
        SemanticModel semanticModel,
        BinaryExpressionSyntax binaryExpression,
        out bool useNegatedAny)
    {
        useNegatedAny = false;
        if (binaryExpression == null)
            return false;

        var leftIsCount = IsLinqCountInvocation(semanticModel, binaryExpression.Left);
        var rightIsCount = IsLinqCountInvocation(semanticModel, binaryExpression.Right);
        if (leftIsCount == rightIsCount)
            return false;

        var zeroExpression = leftIsCount ? binaryExpression.Right : binaryExpression.Left;
        if (!IsZeroLiteral(zeroExpression))
            return false;

        if (binaryExpression.IsKind(SyntaxKind.EqualsExpression))
        {
            useNegatedAny = true;
            return true;
        }

        if (binaryExpression.IsKind(SyntaxKind.GreaterThanExpression) && leftIsCount)
            return true;
        if (binaryExpression.IsKind(SyntaxKind.LessThanExpression) && rightIsCount)
            return true;

        return false;
    }

    private static bool IsLinqCountInvocation(SemanticModel semanticModel, ExpressionSyntax expression)
    {
        if (expression is not InvocationExpressionSyntax invocation)
            return false;

        var methodSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (methodSymbol == null)
            return false;
        if (!string.Equals(methodSymbol.Name, "Count", StringComparison.Ordinal) &&
            !string.Equals(methodSymbol.Name, "LongCount", StringComparison.Ordinal))
        {
            return false;
        }

        var containingType = methodSymbol.ReducedFrom?.ContainingType ?? methodSymbol.ContainingType;
        return string.Equals(containingType?.Name, "Enumerable", StringComparison.Ordinal) &&
               string.Equals(containingType.ContainingNamespace?.ToDisplayString(), "System.Linq", StringComparison.Ordinal);
    }

    private static bool IsZeroLiteral(ExpressionSyntax expression)
    {
        expression = StripParentheses(expression);
        if (expression is not LiteralExpressionSyntax literal ||
            !literal.IsKind(SyntaxKind.NumericLiteralExpression) ||
            literal.Token.Value == null)
        {
            return false;
        }

        return Convert.ToInt64(literal.Token.Value, CultureInfo.InvariantCulture) == 0;
    }

    private static ExpressionSyntax StripParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesizedExpression)
            expression = parenthesizedExpression.Expression;

        return expression;
    }
}
