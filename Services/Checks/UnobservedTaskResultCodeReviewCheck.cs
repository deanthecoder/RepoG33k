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

public sealed class UnobservedTaskResultCodeReviewCheck : RoslynSemanticCodeReviewCheckBase
{
    public override string RuleId => "unobserved-task-result";

    public override string DisplayName => "Unobserved Task/ValueTask results";

    protected override void AnalyzeFile(
        CodeReviewAnalysisContext context,
        CodeReviewChangedFile file,
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        CodeSmellReport report)
    {
        var statements = root.DescendantNodes().OfType<ExpressionStatementSyntax>();
        foreach (var statement in statements)
        {
            if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, statement.Span))
                continue;
            if (statement.Expression is AwaitExpressionSyntax)
                continue;
            if (statement.Expression is not InvocationExpressionSyntax invocation)
                continue;
            if (!ReturnsTaskLike(semanticModel, invocation))
                continue;

            var lineNumber = RoslynCodeReviewCheckUtilities.GetStartLine(statement);
            var invokedMethod = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
            var invokedMethodName = invokedMethod?.Name;
            var methodHint = string.IsNullOrWhiteSpace(invokedMethodName)
                ? "Result of async call is ignored."
                : IsTaskRunMethod(invokedMethod)
                    ? "`Task.Run` result is ignored."
                    : $"Result of async call `{invokedMethodName}` is ignored.";
            AddFinding(
                report,
                CodeReviewFindingSeverity.Suggestion,
                file.Path,
                lineNumber,
                $"{methodHint} Consider awaiting or explicitly handling the returned `Task`/`ValueTask`.");
        }
    }

    private static bool ReturnsTaskLike(SemanticModel semanticModel, InvocationExpressionSyntax invocation)
    {
        var returnType = semanticModel.GetTypeInfo(invocation).Type as INamedTypeSymbol;
        if (returnType == null)
            return false;

        var typeName = returnType.Name;
        var namespaceName = returnType.ContainingNamespace?.ToDisplayString();
        return string.Equals(namespaceName, "System.Threading.Tasks", StringComparison.Ordinal) &&
               (string.Equals(typeName, "Task", StringComparison.Ordinal) ||
                string.Equals(typeName, "ValueTask", StringComparison.Ordinal));
    }

    private static bool IsTaskRunMethod(IMethodSymbol methodSymbol)
    {
        if (methodSymbol == null || !string.Equals(methodSymbol.Name, "Run", StringComparison.Ordinal))
            return false;

        var containingType = methodSymbol.ContainingType;
        return string.Equals(containingType?.Name, "Task", StringComparison.Ordinal) &&
               string.Equals(containingType.ContainingNamespace?.ToDisplayString(), "System.Threading.Tasks", StringComparison.Ordinal);
    }
}
