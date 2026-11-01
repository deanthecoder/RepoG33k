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
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

/// <summary>
/// Finds synchronous methods that expose task-based results by wrapping immediate values.
/// </summary>
/// <remarks>
/// Useful for keeping unnecessary async-shaped APIs from spreading through callers when no asynchronous work is being performed.
/// </remarks>
public sealed class UnnecessaryTaskCompletionCodeReviewCheck : RoslynSemanticCodeReviewCheckBase
{
    public override string RuleId => CodeReviewRuleIds.UnnecessaryTaskCompletion;

    public override string DisplayName => "Unnecessary task completion";

    protected override void AnalyzeFile(
        CodeReviewAnalysisContext context,
        CodeReviewChangedFile file,
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        CodeSmellReport report)
    {
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (!ShouldAnalyzeMethod(file, semanticModel, method))
                continue;

            foreach (var expression in GetReturnedExpressions(method))
            {
                if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, expression.Span))
                    continue;
                if (!IsTaskCompletionExpression(semanticModel, expression))
                    continue;

                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Suggestion,
                    file.Path,
                    RoslynCodeReviewCheckUtilities.GetStartLine(expression),
                    "This method returns an already-completed Task without doing asynchronous work. Consider returning the value directly and keeping callers synchronous unless an async contract is required.");
            }
        }
    }

    private static bool ShouldAnalyzeMethod(CodeReviewChangedFile file, SemanticModel semanticModel, MethodDeclarationSyntax method)
    {
        if (method == null || !RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, method.Span))
            return false;
        if (method.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.AsyncKeyword) || modifier.IsKind(SyntaxKind.OverrideKeyword)))
            return false;
        if (method.ExplicitInterfaceSpecifier != null)
            return false;
        if (ContainsAwait(method))
            return false;

        var methodSymbol = semanticModel.GetDeclaredSymbol(method);
        if (!ReturnsTask(methodSymbol))
            return false;
        if (methodSymbol.ExplicitInterfaceImplementations.Length > 0)
            return false;
        if (methodSymbol.OverriddenMethod != null)
            return false;
        if (methodSymbol.ContainingType?.AllInterfaces.Any(interfaceSymbol =>
                interfaceSymbol.GetMembers(methodSymbol.Name)
                    .OfType<IMethodSymbol>()
                    .Any(interfaceMethod => methodSymbol.Equals(
                        methodSymbol.ContainingType.FindImplementationForInterfaceMember(interfaceMethod),
                        SymbolEqualityComparer.Default))) == true)
        {
            return false;
        }

        return true;
    }

    private static bool ContainsAwait(MethodDeclarationSyntax method) =>
        method.DescendantNodes().OfType<AwaitExpressionSyntax>().Any();

    private static bool ReturnsTask(IMethodSymbol methodSymbol)
    {
        if (methodSymbol?.ReturnType is not INamedTypeSymbol returnType)
            return false;

        return IsSystemThreadingTasksType(returnType, "Task");
    }

    private static bool IsTaskCompletionExpression(SemanticModel semanticModel, ExpressionSyntax expression)
    {
        if (expression is InvocationExpressionSyntax invocation)
        {
            var methodSymbol = GetInvokedMethodSymbol(semanticModel, invocation);
            return IsTaskFromResultMethod(methodSymbol);
        }

        if (expression is MemberAccessExpressionSyntax memberAccess)
        {
            var propertySymbol = semanticModel.GetSymbolInfo(memberAccess).Symbol as IPropertySymbol;
            return IsTaskCompletedTaskProperty(propertySymbol);
        }

        return false;
    }

    private static bool IsTaskFromResultMethod(IMethodSymbol methodSymbol)
    {
        if (methodSymbol == null || !string.Equals(methodSymbol.Name, "FromResult", StringComparison.Ordinal))
            return false;

        return IsSystemThreadingTasksType(methodSymbol.ContainingType, "Task");
    }

    private static bool IsTaskCompletedTaskProperty(IPropertySymbol propertySymbol)
    {
        if (propertySymbol == null || !string.Equals(propertySymbol.Name, "CompletedTask", StringComparison.Ordinal))
            return false;

        return IsSystemThreadingTasksType(propertySymbol.ContainingType, "Task");
    }

    private static bool IsSystemThreadingTasksType(INamedTypeSymbol typeSymbol, string typeName)
    {
        if (typeSymbol == null || !string.Equals(typeSymbol.Name, typeName, StringComparison.Ordinal))
            return false;

        return string.Equals(typeSymbol.ContainingNamespace?.ToDisplayString(), "System.Threading.Tasks", StringComparison.Ordinal);
    }

    private static IMethodSymbol GetInvokedMethodSymbol(SemanticModel semanticModel, InvocationExpressionSyntax invocation)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        return symbolInfo.Symbol as IMethodSymbol ??
               symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
    }

    private static ExpressionSyntax[] GetReturnedExpressions(MethodDeclarationSyntax method)
    {
        var bodyReturns = method.Body?.DescendantNodes()
            .OfType<ReturnStatementSyntax>()
            .Select(returnStatement => returnStatement.Expression)
            .Where(expression => expression != null)
            .ToArray() ?? [];

        if (method.ExpressionBody?.Expression == null)
            return bodyReturns;

        return bodyReturns.Append(method.ExpressionBody.Expression).ToArray();
    }
}
