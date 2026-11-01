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
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

public sealed class StringConcatenationToSameTargetCodeReviewCheck : RoslynSemanticCodeReviewCheckBase
{
    private const int MinConcatsBeforeFinding = 4;

    public override string RuleId => CodeReviewRuleIds.StringConcatSameTarget;

    public override string DisplayName => "Repeated string concatenation to same target";

    protected override void AnalyzeFile(
        CodeReviewAnalysisContext context,
        CodeReviewChangedFile file,
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        CodeSmellReport report)
    {
        var blocks = root.DescendantNodes().OfType<BlockSyntax>();
        foreach (var block in blocks)
        {
            var targetCounts = new Dictionary<string, TargetConcatInfo>(StringComparer.Ordinal);

            var assignments = block
                .DescendantNodes()
                .OfType<AssignmentExpressionSyntax>()
                .Where(assignment => IsDirectChildBlockAssignment(assignment, block));

            foreach (var assignment in assignments)
            {
                if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, assignment.Span))
                    continue;
                if (!TryGetConcatenationTarget(semanticModel, assignment, out var targetKey, out var displayName, out var lineNumber))
                    continue;

                if (!targetCounts.TryGetValue(targetKey, out var existingInfo))
                {
                    targetCounts[targetKey] = new TargetConcatInfo(displayName, lineNumber, 1);
                    continue;
                }

                targetCounts[targetKey] = existingInfo with { Count = existingInfo.Count + 1 };
            }

            foreach (var targetInfo in targetCounts.Values)
            {
                if (targetInfo.Count < MinConcatsBeforeFinding)
                    continue;

                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Suggestion,
                    file.Path,
                    targetInfo.FirstLineNumber,
                    $"String target `{targetInfo.DisplayName}` is concatenated {targetInfo.Count} times in the same block. Consider using `StringBuilder`.");
            }
        }
    }

    private static bool IsDirectChildBlockAssignment(AssignmentExpressionSyntax assignment, BlockSyntax block)
    {
        var nearestBlock = assignment.Ancestors().OfType<BlockSyntax>().FirstOrDefault();
        return nearestBlock != null && nearestBlock == block;
    }

    private static bool TryGetConcatenationTarget(
        SemanticModel semanticModel,
        AssignmentExpressionSyntax assignment,
        out string targetKey,
        out string displayName,
        out int lineNumber)
    {
        targetKey = null;
        displayName = null;
        lineNumber = 0;

        if (!IsStringType(semanticModel.GetTypeInfo(assignment.Left).Type))
            return false;

        if (assignment.IsKind(SyntaxKind.AddAssignmentExpression))
        {
            BuildTarget(semanticModel, assignment.Left, out targetKey, out displayName, out lineNumber);
            return true;
        }

        if (!assignment.IsKind(SyntaxKind.SimpleAssignmentExpression))
            return false;

        if (assignment.Right is not BinaryExpressionSyntax binaryExpression ||
            !binaryExpression.IsKind(SyntaxKind.AddExpression))
        {
            return false;
        }

        if (!IsStringType(semanticModel.GetTypeInfo(binaryExpression).Type))
            return false;

        if (!ExpressionContainsTarget(semanticModel, binaryExpression, assignment.Left))
            return false;

        BuildTarget(semanticModel, assignment.Left, out targetKey, out displayName, out lineNumber);
        return true;
    }

    private static bool ExpressionContainsTarget(SemanticModel semanticModel, ExpressionSyntax expression, ExpressionSyntax target)
    {
        var targetSymbol = GetTargetSymbol(semanticModel, target);
        var normalizedTargetText = target.WithoutTrivia().ToString();

        foreach (var candidate in expression.DescendantNodesAndSelf().OfType<ExpressionSyntax>())
        {
            var candidateSymbol = GetTargetSymbol(semanticModel, candidate);
            if (targetSymbol != null && candidateSymbol != null && SymbolEqualityComparer.Default.Equals(targetSymbol, candidateSymbol))
                return true;

            if (targetSymbol == null && string.Equals(candidate.WithoutTrivia().ToString(), normalizedTargetText, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static void BuildTarget(
        SemanticModel semanticModel,
        ExpressionSyntax left,
        out string targetKey,
        out string displayName,
        out int lineNumber)
    {
        targetKey = null;
        displayName = null;
        lineNumber = left.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

        var targetSymbol = GetTargetSymbol(semanticModel, left);
        displayName = left.WithoutTrivia().ToString();
        if (targetSymbol == null)
        {
            targetKey = $"text::{displayName}";
            return;
        }

        var symbolName = targetSymbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat);
        targetKey = $"symbol::{symbolName}";
        displayName = targetSymbol.Name;
    }

    private static ISymbol GetTargetSymbol(SemanticModel semanticModel, ExpressionSyntax expression)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(expression);
        return symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
    }

    private static bool IsStringType(ITypeSymbol typeSymbol) =>
        typeSymbol?.SpecialType == SpecialType.System_String;

    private sealed record TargetConcatInfo(string DisplayName, int FirstLineNumber, int Count);
}
