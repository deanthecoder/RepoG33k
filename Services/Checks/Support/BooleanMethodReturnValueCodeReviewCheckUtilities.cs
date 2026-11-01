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

namespace ReviewG33k.Services.Checks.Support;

/// <summary>
/// Provides conservative Roslyn helpers for identifying bool methods whose control flow always returns the same literal.
/// </summary>
/// <remarks>
/// The checks using this helper intentionally bias toward false negatives so interface contracts, overrides, and nested local functions
/// do not trigger noisy findings.
/// </remarks>
internal static class BooleanMethodReturnValueCodeReviewCheckUtilities
{
    public static bool TryGetConstantBooleanReturnValue(MethodDeclarationSyntax method, out bool constantValue)
    {
        constantValue = default;
        if (method == null)
            return false;

        if (method.ExpressionBody != null)
            return TryGetBooleanLiteralValue(method.ExpressionBody.Expression, out constantValue);

        if (method.Body == null)
            return false;

        var returnValues = new List<bool?>();
        foreach (var returnStatement in EnumerateDirectReturns(method))
        {
            var expression = returnStatement.Expression;
            if (expression == null)
                continue;

            if (!TryGetBooleanLiteralValue(expression, out var value))
                return false;

            returnValues.Add(value);
        }

        if (returnValues.Count == 0 || returnValues.Any(value => value == null))
            return false;

        var resolvedValue = returnValues[0]!.Value;
        constantValue = resolvedValue;
        return returnValues.All(value => value == resolvedValue);
    }

    public static bool HasMeaningfulControlFlowToSimplify(MethodDeclarationSyntax method)
    {
        if (method?.Body == null)
            return false;

        var returnCount = EnumerateDirectReturns(method).Count();
        if (returnCount > 1)
            return true;

        return method.Body.DescendantNodes(static node => !IsNestedExecutableBoundary(node)).Any(node =>
            node is IfStatementSyntax or
                SwitchStatementSyntax or
                ConditionalExpressionSyntax or
                TryStatementSyntax or
                ForStatementSyntax or
                ForEachStatementSyntax or
                WhileStatementSyntax or
                DoStatementSyntax);
    }

    public static bool IsEligibleBooleanMethod(
        MethodDeclarationSyntax method,
        IMethodSymbol methodSymbol,
        SemanticModel semanticModel,
        bool requirePrivate)
    {
        if (method == null || methodSymbol == null)
            return false;
        if (method.ReturnType == null || !IsBooleanReturnType(method.ReturnType))
            return false;
        if (method.Body == null && method.ExpressionBody == null)
            return false;
        if (method.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword)))
            return false;
        if (requirePrivate)
        {
            if (methodSymbol.DeclaredAccessibility != Accessibility.Private)
                return false;
        }
        else
        {
            if (methodSymbol.DeclaredAccessibility == Accessibility.Private)
                return false;
        }

        if (methodSymbol.IsOverride ||
            methodSymbol.IsAbstract ||
            methodSymbol.IsVirtual ||
            methodSymbol.IsExtern ||
            methodSymbol.IsImplicitlyDeclared)
        {
            return false;
        }

        if (method.ExplicitInterfaceSpecifier != null)
            return false;
        if (methodSymbol.ExplicitInterfaceImplementations.Length > 0)
            return false;
        if (ImplementsInterfaceMember(methodSymbol))
            return false;
        if (CouldBeImplicitInterfaceImplementationWithoutResolution(semanticModel, method, methodSymbol.ContainingType))
            return false;

        return true;
    }

    private static IEnumerable<ReturnStatementSyntax> EnumerateDirectReturns(MethodDeclarationSyntax method) =>
        method?.Body == null
            ? []
            : method.Body.DescendantNodes(static node => !IsNestedExecutableBoundary(node)).OfType<ReturnStatementSyntax>();

    private static bool TryGetBooleanLiteralValue(ExpressionSyntax expression, out bool value)
    {
        value = default;
        if (expression is LiteralExpressionSyntax literalExpression)
        {
            if (literalExpression.IsKind(SyntaxKind.TrueLiteralExpression))
            {
                value = true;
                return true;
            }

            if (literalExpression.IsKind(SyntaxKind.FalseLiteralExpression))
            {
                value = false;
                return true;
            }
        }

        return false;
    }

    private static bool IsNestedExecutableBoundary(SyntaxNode node) =>
        node is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax;

    private static bool IsBooleanReturnType(TypeSyntax returnType)
    {
        var typeText = returnType?.ToString();
        return string.Equals(typeText, "bool", StringComparison.Ordinal) ||
               string.Equals(typeText, "System.Boolean", StringComparison.Ordinal) ||
               string.Equals(typeText, "global::System.Boolean", StringComparison.Ordinal);
    }

    private static bool ImplementsInterfaceMember(IMethodSymbol methodSymbol)
    {
        var containingType = methodSymbol?.ContainingType;
        if (methodSymbol == null || containingType == null)
            return false;

        foreach (var interfaceType in containingType.AllInterfaces)
        {
            foreach (var interfaceMethod in interfaceType.GetMembers().OfType<IMethodSymbol>())
            {
                if (containingType.FindImplementationForInterfaceMember(interfaceMethod) is not IMethodSymbol implementation)
                    continue;

                if (SymbolEqualityComparer.Default.Equals(methodSymbol, implementation) ||
                    SymbolEqualityComparer.Default.Equals(methodSymbol.OriginalDefinition, implementation.OriginalDefinition))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool CouldBeImplicitInterfaceImplementationWithoutResolution(
        SemanticModel semanticModel,
        MethodDeclarationSyntax method,
        INamedTypeSymbol containingType)
    {
        if (semanticModel == null || method == null || containingType == null)
            return false;
        if (!method.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PublicKeyword)))
            return false;

        var containingTypeDeclaration = method.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (containingTypeDeclaration?.BaseList == null)
            return false;

        foreach (var baseType in containingTypeDeclaration.BaseList.Types)
        {
            var baseTypeSymbol = semanticModel.GetSymbolInfo(baseType.Type).Symbol as INamedTypeSymbol;
            if (baseTypeSymbol?.TypeKind == TypeKind.Interface)
                continue;

            if (baseTypeSymbol == null && LooksLikeInterfaceName(baseType.Type))
                return true;
        }

        return false;
    }

    private static bool LooksLikeInterfaceName(TypeSyntax typeSyntax)
    {
        if (typeSyntax == null)
            return false;

        var name = typeSyntax switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax genericName => genericName.Identifier.ValueText,
            QualifiedNameSyntax qualifiedName => qualifiedName.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax aliasQualifiedName => aliasQualifiedName.Name.Identifier.ValueText,
            _ => typeSyntax.ToString().Split('.').LastOrDefault()
        };

        return !string.IsNullOrWhiteSpace(name) &&
               name.Length >= 2 &&
               name[0] == 'I' &&
               char.IsUpper(name[1]);
    }
}
