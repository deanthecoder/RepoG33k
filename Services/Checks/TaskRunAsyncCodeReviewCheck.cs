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

public sealed class TaskRunAsyncCodeReviewCheck : RoslynSemanticCodeReviewCheckBase
{
    public override string RuleId => "task-run-async";

    public override string DisplayName => "Task.Run(async ...)";

    protected override void AnalyzeFile(
        CodeReviewAnalysisContext context,
        CodeReviewChangedFile file,
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        CodeSmellReport report)
    {
        var invocations = root.DescendantNodes().OfType<InvocationExpressionSyntax>();
        foreach (var invocation in invocations)
        {
            if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, invocation.Span))
                continue;
            if (!UsesExplicitAsyncDelegate(invocation))
                continue;
            var invokedMethod = GetInvokedMethodSymbol(semanticModel, invocation);
            if (!IsTaskRunMethod(invokedMethod))
                continue;
            if (!FirstDelegateParameterReturnsTaskLike(invokedMethod))
                continue;

            var lineNumber = RoslynCodeReviewCheckUtilities.GetStartLine(invocation);
            AddFinding(report, CodeReviewFindingSeverity.Suggestion, file.Path, lineNumber, "Task.Run(async ...) detected. Consider awaiting the async work directly unless background scheduling is genuinely needed.");
        }
    }

    private static bool UsesExplicitAsyncDelegate(InvocationExpressionSyntax invocation)
    {
        var expression = invocation.ArgumentList?.Arguments.FirstOrDefault()?.Expression;
        return expression switch
        {
            ParenthesizedLambdaExpressionSyntax lambda => lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword),
            SimpleLambdaExpressionSyntax lambda => lambda.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword),
            AnonymousMethodExpressionSyntax anonymousMethod => anonymousMethod.AsyncKeyword.IsKind(SyntaxKind.AsyncKeyword),
            _ => false
        };
    }

    private static IMethodSymbol GetInvokedMethodSymbol(SemanticModel semanticModel, InvocationExpressionSyntax invocation)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        return symbolInfo.Symbol as IMethodSymbol ??
               symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();
    }

    private static bool IsTaskRunMethod(IMethodSymbol methodSymbol)
    {
        if (methodSymbol == null || !string.Equals(methodSymbol.Name, "Run", StringComparison.Ordinal))
            return false;

        var containingType = methodSymbol.ContainingType;
        if (containingType == null || !string.Equals(containingType.Name, "Task", StringComparison.Ordinal))
            return false;

        return string.Equals(containingType.ContainingNamespace?.ToDisplayString(), "System.Threading.Tasks", StringComparison.Ordinal);
    }

    private static bool FirstDelegateParameterReturnsTaskLike(IMethodSymbol methodSymbol)
    {
        if (methodSymbol?.Parameters.Length < 1)
            return false;

        if (methodSymbol.Parameters[0].Type is not INamedTypeSymbol delegateType || delegateType.TypeKind != TypeKind.Delegate)
            return false;

        var returnType = delegateType.DelegateInvokeMethod?.ReturnType;
        return IsTaskLike(returnType);
    }

    private static bool IsTaskLike(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is not INamedTypeSymbol namedType)
            return false;

        var namespaceName = namedType.ContainingNamespace?.ToDisplayString();
        if (!string.Equals(namespaceName, "System.Threading.Tasks", StringComparison.Ordinal))
            return false;

        return string.Equals(namedType.Name, "Task", StringComparison.Ordinal) ||
               string.Equals(namedType.Name, "ValueTask", StringComparison.Ordinal);
    }
}
