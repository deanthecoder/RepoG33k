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
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

/// <summary>
/// Detects simple pass-through lambdas that can be replaced with a method group.
/// </summary>
/// <remarks>
/// This keeps delegate assignments and LINQ-style calls shorter and easier to scan when the lambda is only forwarding
/// its parameters directly into a single method call.
/// </remarks>
public sealed class LambdaCanBeMethodGroupCodeReviewCheck : RoslynSemanticCodeReviewCheckBase, IFixableCodeReviewCheck
{
    public override string RuleId => CodeReviewRuleIds.LambdaCanBeMethodGroup;

    public override string DisplayName => "Lambda can be method group";

    public override CodeReviewCheckScope Scope => CodeReviewCheckScope.AddedLinesOnly;

    public bool CanFix(CodeSmellFinding finding) =>
        finding != null &&
        string.Equals(finding.RuleId, RuleId, StringComparison.OrdinalIgnoreCase) &&
        finding.LineNumber > 0;

    public bool TryFix(CodeSmellFinding finding, FileInfo resolvedFile, out string resultMessage)
    {
        if (!this.TryPrepareFix(finding, resolvedFile, out var sourceText, out var lineIndex, out resultMessage))
            return false;

        var file = new CodeReviewChangedFile(
            "M",
            resolvedFile.FullName,
            resolvedFile.FullName,
            sourceText.ToString(),
            sourceText.Lines.Select(line => line.ToString()).ToArray(),
            null);
        if (!RoslynCodeReviewCheckUtilities.TryGetSemanticAnalysis(
                file,
                out var root,
                out var semanticModel,
                out _,
                out _))
        {
            resultMessage = "Could not perform semantic analysis for fix.";
            return false;
        }

        var lineSpan = new TextSpan(sourceText.Lines[lineIndex].Start, sourceText.Lines[lineIndex].Span.Length);
        var lambda = root.DescendantNodes()
            .OfType<LambdaExpressionSyntax>()
            .FirstOrDefault(node =>
                node.Span.IntersectsWith(lineSpan) &&
                TryCreateMethodGroupExpression(semanticModel, node, out _));
        if (lambda == null)
        {
            resultMessage = "Could not find a simplifiable lambda on the target line.";
            return false;
        }

        TryCreateMethodGroupExpression(semanticModel, lambda, out var replacementExpression);
        var updatedRoot = root.ReplaceNode(lambda, replacementExpression.WithTriviaFrom(lambda));
        if (!this.TryWriteUpdatedText(resolvedFile, updatedRoot.ToFullString(), out resultMessage))
            return false;

        resultMessage = $"Simplified lambda to method group `{replacementExpression}`.";
        return true;
    }

    protected override void AnalyzeFile(
        CodeReviewAnalysisContext context,
        CodeReviewChangedFile file,
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        CodeSmellReport report)
    {
        foreach (var lambda in root.DescendantNodes().OfType<LambdaExpressionSyntax>())
        {
            if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, lambda.Span))
                continue;
            if (!TryCreateMethodGroupExpression(semanticModel, lambda, out var methodGroupExpression))
                continue;

            AddFinding(
                report,
                CodeReviewFindingSeverity.Hint,
                file.Path,
                RoslynCodeReviewCheckUtilities.GetStartLine(lambda),
                $"Lambda expression can be simplified to method group `{methodGroupExpression}`.");
        }
    }

    private static bool TryCreateMethodGroupExpression(
        SemanticModel semanticModel,
        LambdaExpressionSyntax lambda,
        out ExpressionSyntax methodGroupExpression)
    {
        methodGroupExpression = null;
        if (semanticModel == null || lambda == null)
            return false;
        if (lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword))
            return false;
        if (lambda.Body is not InvocationExpressionSyntax invocation)
            return false;
        if (!TryGetLambdaParameters(lambda, out var parameters))
            return false;
        if (invocation.ArgumentList == null || invocation.ArgumentList.Arguments.Count != parameters.Count)
            return false;
        if (invocation.Expression is MemberBindingExpressionSyntax or ConditionalAccessExpressionSyntax)
            return false;
        if (semanticModel.GetSymbolInfo(invocation).Symbol is not IMethodSymbol invokedMethod)
            return false;
        if (semanticModel.GetTypeInfo(lambda).ConvertedType is not INamedTypeSymbol delegateType ||
            delegateType.DelegateInvokeMethod is not { } delegateInvokeMethod)
        {
            return false;
        }
        if (delegateInvokeMethod.Parameters.Length != invokedMethod.Parameters.Length)
            return false;

        for (var i = 0; i < parameters.Count; i++)
        {
            var argument = invocation.ArgumentList.Arguments[i];
            if (argument.NameColon != null)
                return false;
            if (argument.RefKindKeyword.IsKind(SyntaxKind.RefKeyword) ||
                argument.RefKindKeyword.IsKind(SyntaxKind.OutKeyword) ||
                argument.RefKindKeyword.IsKind(SyntaxKind.InKeyword))
            {
                return false;
            }

            if (argument.Expression is not IdentifierNameSyntax identifier)
                return false;
            if (!string.Equals(identifier.Identifier.ValueText, parameters[i].Identifier.ValueText, StringComparison.Ordinal))
                return false;

            if (delegateInvokeMethod.Parameters[i].RefKind != invokedMethod.Parameters[i].RefKind)
                return false;

            var parameterConversion = semanticModel.Compilation.ClassifyConversion(
                delegateInvokeMethod.Parameters[i].Type,
                invokedMethod.Parameters[i].Type);
            if (!parameterConversion.Exists || !parameterConversion.IsImplicit)
                return false;
        }

        if (delegateInvokeMethod.ReturnsVoid != invokedMethod.ReturnsVoid)
            return false;
        if (!delegateInvokeMethod.ReturnsVoid)
        {
            var returnConversion = semanticModel.Compilation.ClassifyConversion(
                invokedMethod.ReturnType,
                delegateInvokeMethod.ReturnType);
            if (!returnConversion.Exists || !returnConversion.IsImplicit)
                return false;
        }

        methodGroupExpression = invocation.Expression.WithoutTrivia();
        return methodGroupExpression != null;
    }

    private static bool TryGetLambdaParameters(LambdaExpressionSyntax lambda, out IReadOnlyList<ParameterSyntax> parameters)
    {
        parameters = null;
        switch (lambda)
        {
            case ParenthesizedLambdaExpressionSyntax parenthesizedLambda:
                parameters = parenthesizedLambda.ParameterList?.Parameters.ToArray() ?? [];
                return true;

            case SimpleLambdaExpressionSyntax simpleLambda:
                parameters = [simpleLambda.Parameter];
                return true;

            default:
                return false;
        }
    }
}
