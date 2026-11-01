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

public sealed class DisposableNotDisposedCodeReviewCheck : RoslynSemanticCodeReviewCheckBase
{
    public override string RuleId => "disposable-not-disposed";

    public override string DisplayName => "Disposable created without disposal";

    protected override void AnalyzeFile(
        CodeReviewAnalysisContext context,
        CodeReviewChangedFile file,
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        CodeSmellReport report)
    {
        var localDeclarations = root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>();
        foreach (var localDeclaration in localDeclarations)
        {
            if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, localDeclaration.Span))
                continue;
            if (localDeclaration.UsingKeyword.IsKind(SyntaxKind.UsingKeyword))
                continue;

            var declarationBlock = localDeclaration.Parent as BlockSyntax;
            foreach (var variable in localDeclaration.Declaration?.Variables ?? [])
            {
                if (variable.Initializer?.Value is not ObjectCreationExpressionSyntax &&
                    variable.Initializer?.Value is not ImplicitObjectCreationExpressionSyntax)
                {
                    continue;
                }

                var localSymbol = semanticModel.GetDeclaredSymbol(variable) as ILocalSymbol;
                var typeSymbol = localSymbol?.Type ?? semanticModel.GetTypeInfo(variable.Initializer.Value).Type;
                if (!ImplementsDisposable(typeSymbol))
                    continue;

                if (localSymbol != null &&
                    declarationBlock != null &&
                    (IsDisposedLater(semanticModel, declarationBlock, localDeclaration.SpanStart, localSymbol) ||
                     IsReturnedLater(semanticModel, declarationBlock, localDeclaration.SpanStart, localSymbol) ||
                     IsPassedAsArgumentLater(semanticModel, declarationBlock, localDeclaration.SpanStart, localSymbol)))
                {
                    continue;
                }

                var lineNumber = variable.Identifier.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                var typeName = typeSymbol?.Name ?? "disposable";
                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Suggestion,
                    file.Path,
                    lineNumber,
                    $"Disposable `{typeName}` is created without `using`/`await using` or explicit dispose call.");
            }
        }
    }

    private static bool IsDisposedLater(
        SemanticModel semanticModel,
        BlockSyntax declarationBlock,
        int declarationSpanStart,
        ILocalSymbol localSymbol)
    {
        var invocations = declarationBlock.DescendantNodes().OfType<InvocationExpressionSyntax>();
        foreach (var invocation in invocations)
        {
            if (invocation.SpanStart <= declarationSpanStart)
                continue;
            if (invocation.Expression is not MemberAccessExpressionSyntax memberAccess)
                continue;

            var invokedName = memberAccess.Name.Identifier.ValueText;
            if (!string.Equals(invokedName, "Dispose", StringComparison.Ordinal) &&
                !string.Equals(invokedName, "DisposeAsync", StringComparison.Ordinal))
            {
                continue;
            }

            var targetSymbol = semanticModel.GetSymbolInfo(memberAccess.Expression).Symbol;
            if (targetSymbol != null && SymbolEqualityComparer.Default.Equals(targetSymbol, localSymbol))
                return true;
        }

        return false;
    }

    private static bool IsReturnedLater(
        SemanticModel semanticModel,
        BlockSyntax declarationBlock,
        int declarationSpanStart,
        ILocalSymbol localSymbol)
    {
        foreach (var returnStatement in declarationBlock.DescendantNodes().OfType<ReturnStatementSyntax>())
        {
            if (returnStatement.SpanStart <= declarationSpanStart)
                continue;
            if (returnStatement.Expression == null)
                continue;

            var returnedSymbol = semanticModel.GetSymbolInfo(returnStatement.Expression).Symbol;
            if (returnedSymbol != null && SymbolEqualityComparer.Default.Equals(returnedSymbol, localSymbol))
                return true;
        }

        return false;
    }

    private static bool IsPassedAsArgumentLater(
        SemanticModel semanticModel,
        BlockSyntax declarationBlock,
        int declarationSpanStart,
        ILocalSymbol localSymbol)
    {
        foreach (var invocation in declarationBlock.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (invocation.SpanStart <= declarationSpanStart)
                continue;
            if (ContainsArgumentReferencingLocal(semanticModel, invocation.ArgumentList?.Arguments, localSymbol))
                return true;
        }

        foreach (var creation in declarationBlock.DescendantNodes().OfType<ObjectCreationExpressionSyntax>())
        {
            if (creation.SpanStart <= declarationSpanStart)
                continue;
            if (ContainsArgumentReferencingLocal(semanticModel, creation.ArgumentList?.Arguments, localSymbol))
                return true;
        }

        foreach (var implicitCreation in declarationBlock.DescendantNodes().OfType<ImplicitObjectCreationExpressionSyntax>())
        {
            if (implicitCreation.SpanStart <= declarationSpanStart)
                continue;
            if (ContainsArgumentReferencingLocal(semanticModel, implicitCreation.ArgumentList?.Arguments, localSymbol))
                return true;
        }

        return false;
    }

    private static bool ContainsArgumentReferencingLocal(
        SemanticModel semanticModel,
        SeparatedSyntaxList<ArgumentSyntax>? arguments,
        ILocalSymbol localSymbol)
    {
        if (arguments == null)
            return false;

        foreach (var argument in arguments.Value)
        {
            var symbol = semanticModel.GetSymbolInfo(argument.Expression).Symbol;
            if (symbol != null && SymbolEqualityComparer.Default.Equals(symbol, localSymbol))
                return true;
        }

        return false;
    }

    private static bool ImplementsDisposable(ITypeSymbol typeSymbol)
    {
        if (typeSymbol == null)
            return false;

        var interfaces = typeSymbol.AllInterfaces;
        return interfaces.Any(@interface =>
            string.Equals(@interface.ToDisplayString(), "System.IDisposable", StringComparison.Ordinal) ||
            string.Equals(@interface.ToDisplayString(), "System.IAsyncDisposable", StringComparison.Ordinal));
    }
}
