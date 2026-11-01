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
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

/// <summary>
/// Reports `IEnumerable` methods that return an eagerly materialized LINQ collection.
/// </summary>
/// <remarks>
/// Keeps lazy enumerable APIs honest by calling out `ToList()` and `ToArray()` returns when the signature only promises enumeration.
/// </remarks>
public sealed class EagerMaterializedEnumerableReturnCodeReviewCheck : RoslynSemanticCodeReviewCheckBase
{
    public override string RuleId => CodeReviewRuleIds.EagerMaterializedEnumerableReturn;

    public override string DisplayName => "Eagerly materialized IEnumerable return";

    protected override void AnalyzeFile(
        CodeReviewAnalysisContext context,
        CodeReviewChangedFile file,
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        CodeSmellReport report)
    {
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (semanticModel.GetDeclaredSymbol(method) is not IMethodSymbol methodSymbol)
                continue;
            if (!IsEnumerableReturnType(methodSymbol.ReturnType))
                continue;

            foreach (var returnStatement in method.DescendantNodes().OfType<ReturnStatementSyntax>())
            {
                if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, returnStatement.Span))
                    continue;
                if (!TryGetMaterializerName(semanticModel, returnStatement.Expression, out var materializerName))
                    continue;

                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Hint,
                    file.Path,
                    RoslynCodeReviewCheckUtilities.GetStartLine(returnStatement),
                    $"Method `{methodSymbol.Name}` returns `IEnumerable` but eagerly materializes with `{materializerName}()`. Consider returning the sequence directly or changing the return type.");
            }
        }
    }

    private static bool TryGetMaterializerName(
        SemanticModel semanticModel,
        ExpressionSyntax expression,
        out string materializerName)
    {
        materializerName = null;
        if (expression is not InvocationExpressionSyntax invocation)
            return false;

        var methodSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        var methodName = methodSymbol?.Name ?? GetInvocationName(invocation);
        if (!string.Equals(methodName, "ToList", StringComparison.Ordinal) &&
            !string.Equals(methodName, "ToArray", StringComparison.Ordinal))
        {
            return false;
        }

        if (methodSymbol != null && !IsLinqEnumerableMethod(methodSymbol))
            return false;

        materializerName = methodName;
        return true;
    }

    private static string GetInvocationName(InvocationExpressionSyntax invocation) =>
        invocation?.Expression switch
        {
            MemberAccessExpressionSyntax memberAccess => memberAccess.Name.Identifier.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            _ => null
        };

    private static bool IsLinqEnumerableMethod(IMethodSymbol methodSymbol)
    {
        var containingType = methodSymbol?.ReducedFrom?.ContainingType ?? methodSymbol?.ContainingType;
        return string.Equals(containingType?.Name, "Enumerable", StringComparison.Ordinal) &&
               string.Equals(containingType.ContainingNamespace?.ToDisplayString(), "System.Linq", StringComparison.Ordinal);
    }

    private static bool IsEnumerableReturnType(ITypeSymbol returnType)
    {
        if (returnType == null || returnType.SpecialType == SpecialType.System_String)
            return false;

        return IsEnumerableType(returnType);
    }

    private static bool IsEnumerableType(ITypeSymbol typeSymbol)
    {
        if (typeSymbol == null)
            return false;

        var originalDefinition = typeSymbol.OriginalDefinition?.ToDisplayString();
        return string.Equals(originalDefinition, "System.Collections.Generic.IEnumerable<T>", StringComparison.Ordinal) ||
               string.Equals(typeSymbol.ToDisplayString(), "System.Collections.IEnumerable", StringComparison.Ordinal);
    }
}
