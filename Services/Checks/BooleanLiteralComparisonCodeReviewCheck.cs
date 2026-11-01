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

public sealed class BooleanLiteralComparisonCodeReviewCheck : RoslynSemanticCodeReviewCheckBase
{
    public override string RuleId => "boolean-literal-comparison";

    public override string DisplayName => "Boolean literal comparison";

    protected override void AnalyzeFile(
        CodeReviewAnalysisContext context,
        CodeReviewChangedFile file,
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        CodeSmellReport report)
    {
        var binaryExpressions = root.DescendantNodes().OfType<BinaryExpressionSyntax>();
        foreach (var binaryExpression in binaryExpressions)
        {
            if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, binaryExpression.Span))
                continue;
            if (!TryGetBooleanLiteralComparison(binaryExpression, out var literalValue, out var comparedExpression))
                continue;
            if (!IsNonNullableBoolean(semanticModel, comparedExpression))
                continue;

            var simplifiedExpression = BuildSimplifiedExpression(binaryExpression.Kind(), literalValue, comparedExpression.ToString());
            if (string.IsNullOrWhiteSpace(simplifiedExpression))
                continue;

            AddFinding(
                report,
                CodeReviewFindingSeverity.Hint,
                file.Path,
                RoslynCodeReviewCheckUtilities.GetStartLine(binaryExpression),
                $"Boolean comparison `{binaryExpression}` can be simplified to `{simplifiedExpression}`.");
        }
    }

    private static bool TryGetBooleanLiteralComparison(
        BinaryExpressionSyntax binaryExpression,
        out bool literalValue,
        out ExpressionSyntax comparedExpression)
    {
        literalValue = false;
        comparedExpression = null;

        if (binaryExpression == null)
            return false;

        if (!binaryExpression.IsKind(SyntaxKind.EqualsExpression) &&
            !binaryExpression.IsKind(SyntaxKind.NotEqualsExpression))
        {
            return false;
        }

        if (TryGetBooleanLiteralValue(binaryExpression.Right, out literalValue))
        {
            comparedExpression = binaryExpression.Left;
            return true;
        }

        if (TryGetBooleanLiteralValue(binaryExpression.Left, out literalValue))
        {
            comparedExpression = binaryExpression.Right;
            return true;
        }

        return false;
    }

    private static bool TryGetBooleanLiteralValue(ExpressionSyntax expression, out bool value)
    {
        value = false;
        if (expression is not LiteralExpressionSyntax literalExpression)
            return false;

        if (literalExpression.IsKind(SyntaxKind.TrueLiteralExpression))
        {
            value = true;
            return true;
        }

        if (literalExpression.IsKind(SyntaxKind.FalseLiteralExpression))
        {
            value = false;
            return true;
        }

        return false;
    }

    private static bool IsNonNullableBoolean(SemanticModel semanticModel, ExpressionSyntax expression)
    {
        var typeSymbol = semanticModel.GetTypeInfo(expression).Type;
        return typeSymbol?.SpecialType == SpecialType.System_Boolean;
    }

    private static string BuildSimplifiedExpression(SyntaxKind comparisonKind, bool literalValue, string comparedExpressionText)
    {
        var normalizedExpression = WrapIfNeeded(comparedExpressionText);

        if (comparisonKind == SyntaxKind.EqualsExpression)
            return literalValue ? normalizedExpression : $"!{normalizedExpression}";

        if (comparisonKind == SyntaxKind.NotEqualsExpression)
            return literalValue ? $"!{normalizedExpression}" : normalizedExpression;

        return null;
    }

    private static string WrapIfNeeded(string expressionText)
    {
        if (string.IsNullOrWhiteSpace(expressionText))
            return expressionText;

        return expressionText.Any(char.IsWhiteSpace) ? $"({expressionText})" : expressionText;
    }
}
