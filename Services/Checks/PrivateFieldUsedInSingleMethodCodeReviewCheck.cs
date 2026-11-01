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

/// <summary>
/// Detects private readonly fields whose usage is confined to a constructor.
/// </summary>
/// <remarks>
/// Useful for spotting constructor-only temporary state that was promoted to a field unnecessarily.
/// Converting these fields to locals often reduces class complexity and keeps initialization code easier to follow.
/// </remarks>
public sealed class PrivateFieldUsedInSingleMethodCodeReviewCheck : RoslynSemanticCodeReviewCheckBase
{
    public override string RuleId => CodeReviewRuleIds.PrivateFieldUsedInSingleMethod;

    public override string DisplayName => "Private readonly field only used in constructor";

    protected override void AnalyzeFile(
        CodeReviewAnalysisContext context,
        CodeReviewChangedFile file,
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        CodeSmellReport report)
    {
        foreach (var fieldDeclaration in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            if (!fieldDeclaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PrivateKeyword)))
                continue;
            if (!fieldDeclaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.ReadOnlyKeyword)))
                continue;
            if (fieldDeclaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.ConstKeyword)))
                continue;
            if (IsInPartialType(fieldDeclaration, file))
                continue;

            foreach (var variable in fieldDeclaration.Declaration?.Variables ?? [])
            {
                if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, variable.Span))
                    continue;
                if (variable.Initializer != null)
                    continue;

                if (semanticModel.GetDeclaredSymbol(variable) is not IFieldSymbol fieldSymbol)
                    continue;

                if (!TryGetSingleUsingMethod(root, semanticModel, fieldSymbol, out var methodSymbol))
                    continue;
                if (methodSymbol.MethodKind != MethodKind.Constructor)
                    continue;
                if (!TryFindMemberDeclaration(root, semanticModel, methodSymbol, out var memberDeclaration))
                    continue;
                if (!CanBeConvertedToLocal(memberDeclaration, semanticModel, fieldSymbol))
                    continue;

                var lineNumber = RoslynCodeReviewCheckUtilities.GetStartLine(variable);
                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Hint,
                    file.Path,
                    lineNumber,
                    $"Private readonly field `{fieldSymbol.Name}` is only used in the constructor and can likely be a local variable.");
            }
        }
    }

    private static bool IsInPartialType(FieldDeclarationSyntax fieldDeclaration, CodeReviewChangedFile file)
    {
        if (CodeReviewFileClassification.IsCodeBehindFilePath(file?.Path))
            return false;

        return fieldDeclaration.Ancestors().OfType<TypeDeclarationSyntax>().Any(typeDeclaration =>
            typeDeclaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword)));
    }

    private static bool TryGetSingleUsingMethod(
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        IFieldSymbol fieldSymbol,
        out IMethodSymbol methodSymbol)
    {
        methodSymbol = null;
        foreach (var identifier in root.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            if (!SymbolMatches(semanticModel.GetSymbolInfo(identifier), fieldSymbol))
                continue;

            var containingMethod = identifier.Ancestors().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault();
            if (containingMethod == null)
                return false;

            if (semanticModel.GetDeclaredSymbol(containingMethod) is not { } containingMethodSymbol)
                return false;
            if (containingMethodSymbol.MethodKind != MethodKind.Constructor)
            {
                return false;
            }

            if (methodSymbol == null)
            {
                methodSymbol = containingMethodSymbol;
                continue;
            }

            if (!SymbolEqualityComparer.Default.Equals(methodSymbol, containingMethodSymbol))
                return false;
        }

        return methodSymbol != null;
    }

    private static bool TryFindMemberDeclaration(
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        IMethodSymbol targetMethodSymbol,
        out BaseMethodDeclarationSyntax memberDeclaration)
    {
        memberDeclaration = root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>().FirstOrDefault(method =>
            SymbolEqualityComparer.Default.Equals(semanticModel.GetDeclaredSymbol(method), targetMethodSymbol));
        return memberDeclaration != null;
    }

    private static bool CanBeConvertedToLocal(
        BaseMethodDeclarationSyntax memberDeclaration,
        SemanticModel semanticModel,
        IFieldSymbol fieldSymbol)
    {
        var references = memberDeclaration.DescendantNodes()
            .OfType<IdentifierNameSyntax>()
            .Where(identifier => SymbolMatches(semanticModel.GetSymbolInfo(identifier), fieldSymbol))
            .OrderBy(identifier => identifier.SpanStart)
            .ToArray();
        if (references.Length == 0)
            return false;

        var simpleAssignments = memberDeclaration.DescendantNodes()
            .OfType<AssignmentExpressionSyntax>()
            .Where(assignment =>
                assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                TargetsField(semanticModel, assignment.Left, fieldSymbol))
            .ToArray();
        if (simpleAssignments.Length != 1)
            return false;

        var firstAssignment = simpleAssignments[0];
        var firstReference = references[0];
        if (!firstAssignment.Left.Span.Contains(firstReference.Span))
            return false;
        if (ContainsFieldReference(firstAssignment.Right, semanticModel, fieldSymbol))
            return false;

        if (memberDeclaration.DescendantNodes().OfType<AssignmentExpressionSyntax>().Any(assignment =>
                !assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                TargetsField(semanticModel, assignment.Left, fieldSymbol)))
        {
            return false;
        }

        if (memberDeclaration.DescendantNodes()
            .OfType<ArgumentSyntax>()
            .Any(argument =>
                (argument.RefOrOutKeyword.IsKind(SyntaxKind.RefKeyword) ||
                 argument.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword)) &&
                TargetsField(semanticModel, argument.Expression, fieldSymbol)))
        {
            return false;
        }

        if (memberDeclaration.DescendantNodes()
            .OfType<PrefixUnaryExpressionSyntax>()
            .Any(unary =>
                (unary.IsKind(SyntaxKind.PreIncrementExpression) || unary.IsKind(SyntaxKind.PreDecrementExpression)) &&
                TargetsField(semanticModel, unary.Operand, fieldSymbol)))
        {
            return false;
        }

        if (memberDeclaration.DescendantNodes()
            .OfType<PostfixUnaryExpressionSyntax>()
            .Any(unary =>
                (unary.IsKind(SyntaxKind.PostIncrementExpression) || unary.IsKind(SyntaxKind.PostDecrementExpression)) &&
                TargetsField(semanticModel, unary.Operand, fieldSymbol)))
        {
            return false;
        }

        return true;
    }

    private static bool ContainsFieldReference(
        SyntaxNode node,
        SemanticModel semanticModel,
        IFieldSymbol fieldSymbol)
    {
        if (node == null)
            return false;

        return node.DescendantNodesAndSelf()
            .OfType<IdentifierNameSyntax>()
            .Any(identifier => SymbolMatches(semanticModel.GetSymbolInfo(identifier), fieldSymbol));
    }

    private static bool TargetsField(SemanticModel semanticModel, ExpressionSyntax expression, IFieldSymbol fieldSymbol) =>
        SymbolMatches(semanticModel.GetSymbolInfo(expression), fieldSymbol);

    private static bool SymbolMatches(SymbolInfo symbolInfo, IFieldSymbol fieldSymbol)
    {
        if (fieldSymbol == null)
            return false;

        if (SymbolEqualityComparer.Default.Equals(symbolInfo.Symbol, fieldSymbol))
            return true;

        return symbolInfo.CandidateSymbols.Any(candidateSymbol =>
            SymbolEqualityComparer.Default.Equals(candidateSymbol, fieldSymbol));
    }
}
