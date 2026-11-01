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

public sealed class ConstructorEventSubscriptionLifecycleCodeReviewCheck : RoslynSemanticCodeReviewCheckBase
{
    public override string RuleId => CodeReviewRuleIds.ConstructorEventSubscriptionLifecycle;

    public override string DisplayName => "Constructor event subscriptions without lifecycle cleanup";

    protected override void AnalyzeFile(
        CodeReviewAnalysisContext context,
        CodeReviewChangedFile file,
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        CodeSmellReport report)
    {
        if (CodeReviewFileClassification.IsCodeBehindFilePath(file.Path))
            return;

        var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>();
        foreach (var classDeclaration in classes)
        {
            var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration);
            if (classSymbol == null)
                continue;

            var constructors = classDeclaration.Members
                .OfType<ConstructorDeclarationSyntax>()
                .Where(constructor => RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, constructor.Span));

            foreach (var constructor in constructors)
            {
                var subscribedEvents = GetNewConstructorSubscriptions(file, semanticModel, constructor, classSymbol);
                if (subscribedEvents.Count == 0)
                    continue;

                var hasUnsubscribe = ClassHasUnsubscribe(classDeclaration, semanticModel, subscribedEvents);
                var implementsDisposable = ImplementsDisposable(classSymbol);

                if (hasUnsubscribe && implementsDisposable)
                    continue;

                var lineNumber = RoslynCodeReviewCheckUtilities.GetStartLine(constructor);
                var constructorName = constructor.Identifier.ValueText;
                var gap = !hasUnsubscribe && !implementsDisposable
                    ? "no unsubscribe and no `IDisposable`"
                    : !hasUnsubscribe
                        ? "no unsubscribe"
                        : "no `IDisposable`";

                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Important,
                    file.Path,
                    lineNumber,
                    $"Constructor `{constructorName}` subscribes to event(s): {gap}.");
            }
        }
    }

    private static HashSet<IEventSymbol> GetNewConstructorSubscriptions(
        CodeReviewChangedFile file,
        SemanticModel semanticModel,
        ConstructorDeclarationSyntax constructor,
        INamedTypeSymbol classSymbol)
    {
        var result = new HashSet<IEventSymbol>(SymbolEqualityComparer.Default);
        var assignments = constructor.DescendantNodes().OfType<AssignmentExpressionSyntax>();

        foreach (var assignment in assignments)
        {
            if (!assignment.IsKind(SyntaxKind.AddAssignmentExpression))
                continue;
            if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, assignment.Span))
                continue;

            var eventSymbol = GetEventSymbol(semanticModel, assignment.Left);
            if (eventSymbol == null)
                continue;
            if (IsSameInstanceClassEvent(eventSymbol, classSymbol))
                continue;

            result.Add(eventSymbol);
        }

        return result;
    }

    private static bool ClassHasUnsubscribe(
        ClassDeclarationSyntax classDeclaration,
        SemanticModel semanticModel,
        HashSet<IEventSymbol> subscribedEvents)
    {
        var assignments = classDeclaration.DescendantNodes().OfType<AssignmentExpressionSyntax>();
        foreach (var assignment in assignments)
        {
            if (!assignment.IsKind(SyntaxKind.SubtractAssignmentExpression))
                continue;

            var eventSymbol = GetEventSymbol(semanticModel, assignment.Left);
            if (eventSymbol == null)
                continue;

            if (subscribedEvents.Contains(eventSymbol))
                return true;
        }

        return false;
    }

    private static IEventSymbol GetEventSymbol(SemanticModel semanticModel, ExpressionSyntax expression)
    {
        var symbolInfo = semanticModel.GetSymbolInfo(expression);
        var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
        return symbol as IEventSymbol;
    }

    private static bool IsSameInstanceClassEvent(IEventSymbol eventSymbol, INamedTypeSymbol classSymbol)
    {
        if (eventSymbol == null || classSymbol == null)
            return false;

        return !eventSymbol.IsStatic && SymbolEqualityComparer.Default.Equals(eventSymbol.ContainingType, classSymbol);
    }

    private static bool ImplementsDisposable(INamedTypeSymbol classSymbol)
    {
        if (classSymbol == null)
            return false;

        return classSymbol.AllInterfaces.Any(@interface =>
            string.Equals(@interface.ToDisplayString(), "System.IDisposable", StringComparison.Ordinal));
    }
}
