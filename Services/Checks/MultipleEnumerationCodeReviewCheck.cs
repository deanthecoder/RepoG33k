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
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

public sealed class MultipleEnumerationCodeReviewCheck : RoslynSemanticCodeReviewCheckBase
{
    private static readonly HashSet<string> LinqTerminalMethodNames =
    [
        "Any",
        "All",
        "Count",
        "LongCount",
        "First",
        "FirstOrDefault",
        "Single",
        "SingleOrDefault",
        "Last",
        "LastOrDefault",
        "ElementAt",
        "ElementAtOrDefault",
        "ToArray",
        "ToList",
        "ToDictionary",
        "ToHashSet",
        "Max",
        "Min",
        "Average",
        "Sum"
    ];

    public override string RuleId => "multiple-enumeration";

    public override string DisplayName => "Multiple enumeration of IEnumerable";

    protected override void AnalyzeFile(
        CodeReviewAnalysisContext context,
        CodeReviewChangedFile file,
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        CodeSmellReport report)
    {
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
        foreach (var method in methods)
        {
            if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, method.Span))
                continue;

            AnalyzeMethod(file, report, semanticModel, method);
        }
    }

    private void AnalyzeMethod(
        CodeReviewChangedFile file,
        CodeSmellReport report,
        SemanticModel semanticModel,
        MethodDeclarationSyntax method)
    {
        var occurrences = new Dictionary<ISymbol, List<int>>(SymbolEqualityComparer.Default);

        var invocations = method.DescendantNodes().OfType<InvocationExpressionSyntax>();
        foreach (var invocation in invocations)
        {
            if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, invocation.Span))
                continue;

            if (!TryGetEnumeratedSymbolFromInvocation(semanticModel, invocation, out var sequenceSymbol))
                continue;
            if (sequenceSymbol == null)
                continue;

            AddOccurrence(occurrences, sequenceSymbol, RoslynCodeReviewCheckUtilities.GetStartLine(invocation));
        }

        var forEachStatements = method.DescendantNodes().OfType<ForEachStatementSyntax>();
        foreach (var forEachStatement in forEachStatements)
        {
            if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, forEachStatement.Span))
                continue;

            if (!TryGetEnumeratedSymbolFromForEach(semanticModel, forEachStatement, out var sequenceSymbol))
                continue;
            if (sequenceSymbol == null)
                continue;

            AddOccurrence(occurrences, sequenceSymbol, RoslynCodeReviewCheckUtilities.GetStartLine(forEachStatement));
        }

        foreach (var pair in occurrences.Where(pair => pair.Value.Count >= 2))
        {
            var repeatedLine = pair.Value.OrderBy(lineNumber => lineNumber).Skip(1).First();
            var sequenceName = pair.Key.Name;
            if (string.IsNullOrWhiteSpace(sequenceName))
                sequenceName = "sequence";

            AddFinding(
                report,
                CodeReviewFindingSeverity.Suggestion,
                file.Path,
                repeatedLine,
                $"Sequence `{sequenceName}` appears to be enumerated multiple times in method `{method.Identifier.ValueText}`. Consider materializing it once.");
        }
    }

    private static void AddOccurrence(IDictionary<ISymbol, List<int>> occurrences, ISymbol sequenceSymbol, int lineNumber)
    {
        if (!occurrences.TryGetValue(sequenceSymbol, out var lines))
        {
            lines = [];
            occurrences[sequenceSymbol] = lines;
        }

        lines.Add(lineNumber);
    }

    private static bool TryGetEnumeratedSymbolFromInvocation(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        out ISymbol sequenceSymbol)
    {
        sequenceSymbol = null;
        var invocationSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (!IsLinqTerminalMethod(invocationSymbol))
            return false;

        ExpressionSyntax sequenceExpression = null;
        if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            sequenceExpression = memberAccess.Expression;
        else if (invocation.ArgumentList.Arguments.Count > 0)
            sequenceExpression = invocation.ArgumentList.Arguments[0].Expression;

        if (sequenceExpression == null)
            return false;

        if (!TryGetSequenceSymbolAndType(semanticModel, sequenceExpression, out sequenceSymbol, out var sequenceType))
            return false;

        return IsEnumerableCandidate(sequenceSymbol, sequenceType);
    }

    private static bool TryGetEnumeratedSymbolFromForEach(
        SemanticModel semanticModel,
        ForEachStatementSyntax forEachStatement,
        out ISymbol sequenceSymbol)
    {
        if (!TryGetSequenceSymbolAndType(semanticModel, forEachStatement.Expression, out sequenceSymbol, out var sequenceType))
            return false;

        return IsEnumerableCandidate(sequenceSymbol, sequenceType);
    }

    private static bool TryGetSequenceSymbolAndType(
        SemanticModel semanticModel,
        ExpressionSyntax expression,
        out ISymbol sequenceSymbol,
        out ITypeSymbol sequenceType)
    {
        sequenceSymbol = null;
        sequenceType = null;
        if (semanticModel == null || expression == null)
            return false;

        if (expression is InvocationExpressionSyntax invocation)
        {
            if (invocation.Expression is MemberAccessExpressionSyntax memberAccess)
            {
                if (TryGetSequenceSymbolAndType(semanticModel, memberAccess.Expression, out sequenceSymbol, out sequenceType))
                    return true;

                sequenceSymbol = semanticModel.GetSymbolInfo(memberAccess.Expression).Symbol;
                sequenceType = semanticModel.GetTypeInfo(memberAccess.Expression).Type;
                return sequenceSymbol != null || sequenceType != null;
            }

            if (invocation.ArgumentList.Arguments.Count > 0)
            {
                var sourceExpression = invocation.ArgumentList.Arguments[0].Expression;
                if (TryGetSequenceSymbolAndType(semanticModel, sourceExpression, out sequenceSymbol, out sequenceType))
                    return true;

                sequenceSymbol = semanticModel.GetSymbolInfo(sourceExpression).Symbol;
                sequenceType = semanticModel.GetTypeInfo(sourceExpression).Type;
                return sequenceSymbol != null || sequenceType != null;
            }
        }

        sequenceSymbol = semanticModel.GetSymbolInfo(expression).Symbol;
        sequenceType = semanticModel.GetTypeInfo(expression).Type;
        return sequenceSymbol != null || sequenceType != null;
    }

    private static bool IsLinqTerminalMethod(IMethodSymbol methodSymbol)
    {
        if (methodSymbol == null)
            return false;
        if (!LinqTerminalMethodNames.Contains(methodSymbol.Name))
            return false;

        var containingType = methodSymbol.ReducedFrom?.ContainingType ?? methodSymbol.ContainingType;
        if (containingType == null || !string.Equals(containingType.Name, "Enumerable", StringComparison.Ordinal))
            return false;

        return string.Equals(containingType.ContainingNamespace?.ToDisplayString(), "System.Linq", StringComparison.Ordinal);
    }

    private static bool IsEnumerableCandidate(ISymbol symbol, ITypeSymbol sequenceType)
    {
        if (symbol == null)
            return false;
        if (symbol is IMethodSymbol)
            return false;

        var typeSymbol = ResolveSymbolType(symbol) ?? sequenceType;
        if (typeSymbol == null)
            return false;
        if (typeSymbol.SpecialType == SpecialType.System_String)
            return false;
        if (IsCheapCollectionType(typeSymbol))
            return false;

        return ImplementsIEnumerable(typeSymbol);
    }

    private static ITypeSymbol ResolveSymbolType(ISymbol symbol)
    {
        return symbol switch
        {
            ILocalSymbol localSymbol => localSymbol.Type,
            IParameterSymbol parameterSymbol => parameterSymbol.Type,
            IFieldSymbol fieldSymbol => fieldSymbol.Type,
            IPropertySymbol propertySymbol => propertySymbol.Type,
            _ => null
        };
    }

    private static bool ImplementsIEnumerable(ITypeSymbol typeSymbol)
    {
        var allInterfaces = typeSymbol.AllInterfaces;
        return allInterfaces.Any(@interface =>
            @interface.SpecialType == SpecialType.System_Collections_IEnumerable ||
            @interface.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);
    }

    private static bool IsCheapCollectionType(ITypeSymbol typeSymbol)
    {
        if (typeSymbol is IArrayTypeSymbol)
            return true;

        var allInterfaces = typeSymbol.AllInterfaces;
        return allInterfaces.Any(@interface =>
            string.Equals(@interface.ToDisplayString(), "System.Collections.ICollection", StringComparison.Ordinal) ||
            @interface.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_ICollection_T ||
            @interface.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IReadOnlyCollection_T ||
            @interface.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IList_T ||
            @interface.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IReadOnlyList_T);
    }
}
