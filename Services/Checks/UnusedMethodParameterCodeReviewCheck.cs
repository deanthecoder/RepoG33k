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
/// Reports ordinary method parameters that are never read by the method body.
/// </summary>
/// <remarks>
/// Helps catch stale signatures in newly added code while avoiding polymorphic and interface contracts where parameters may be required.
/// </remarks>
public sealed class UnusedMethodParameterCodeReviewCheck : RoslynSemanticCodeReviewCheckBase
{
    public override string RuleId => CodeReviewRuleIds.UnusedMethodParameter;

    public override string DisplayName => "Unused method parameter";

    protected override void AnalyzeFile(
        CodeReviewAnalysisContext context,
        CodeReviewChangedFile file,
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        CodeSmellReport report)
    {
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (!RoslynCodeReviewCheckUtilities.IsNodeNew(file, method))
                continue;
            if (method.Body == null || method.ParameterList.Parameters.Count == 0)
                continue;
            if (!IsEligibleMethod(semanticModel, method, out var methodSymbol))
                continue;
            if (IsEventHandlerLikeMethod(method, methodSymbol))
                continue;

            foreach (var parameter in method.ParameterList.Parameters)
            {
                var parameterName = parameter.Identifier.ValueText;
                if (string.IsNullOrWhiteSpace(parameterName) ||
                    string.Equals(parameterName, "_", StringComparison.Ordinal))
                {
                    continue;
                }

                if (semanticModel.GetDeclaredSymbol(parameter) is not { } parameterSymbol)
                    continue;
                if (IsParameterReferenced(method.Body, semanticModel, parameterSymbol))
                    continue;

                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Hint,
                    file.Path,
                    parameter.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    $"Parameter `{parameterName}` is not used by method `{methodSymbol.Name}`.");
            }
        }
    }

    private static bool IsEligibleMethod(
        SemanticModel semanticModel,
        MethodDeclarationSyntax method,
        out IMethodSymbol methodSymbol)
    {
        methodSymbol = semanticModel.GetDeclaredSymbol(method);
        if (methodSymbol == null)
            return false;
        if (methodSymbol.MethodKind != MethodKind.Ordinary)
            return false;
        if (methodSymbol.IsAbstract ||
            methodSymbol.IsOverride ||
            methodSymbol.IsVirtual ||
            methodSymbol.IsExtern ||
            methodSymbol.IsImplicitlyDeclared)
        {
            return false;
        }

        if (methodSymbol.ContainingType?.TypeKind == TypeKind.Interface)
            return false;
        if (method.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword)))
            return false;
        if (method.ExplicitInterfaceSpecifier != null)
            return false;
        if (methodSymbol.ExplicitInterfaceImplementations.Length > 0)
            return false;
        if (ImplementsInterfaceMember(methodSymbol))
            return false;
        if (CouldBeImplicitInterfaceImplementationWithoutResolution(semanticModel, method))
            return false;

        return true;
    }

    private static bool IsParameterReferenced(
        BlockSyntax methodBody,
        SemanticModel semanticModel,
        IParameterSymbol parameterSymbol)
    {
        foreach (var identifier in methodBody.DescendantNodes().OfType<IdentifierNameSyntax>())
        {
            var symbol = semanticModel.GetSymbolInfo(identifier).Symbol;
            if (SymbolEqualityComparer.Default.Equals(symbol, parameterSymbol))
                return true;
        }

        return false;
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

    private static bool IsEventHandlerLikeMethod(MethodDeclarationSyntax method, IMethodSymbol methodSymbol)
    {
        if (method?.ParameterList?.Parameters.Count != 2 || methodSymbol?.Parameters.Length != 2)
            return false;

        var senderParameter = method.ParameterList.Parameters[0];
        if (!string.Equals(senderParameter.Identifier.ValueText, "sender", StringComparison.Ordinal))
            return false;
        if (!IsObjectType(senderParameter.Type, methodSymbol.Parameters[0].Type))
            return false;

        return IsEventArgsLikeType(method.ParameterList.Parameters[1].Type, methodSymbol.Parameters[1].Type);
    }

    private static bool IsObjectType(TypeSyntax typeSyntax, ITypeSymbol typeSymbol)
    {
        if (typeSymbol?.SpecialType == SpecialType.System_Object)
            return true;

        var typeText = typeSyntax?.ToString().TrimEnd('?');
        return string.Equals(typeText, "object", StringComparison.Ordinal) ||
               string.Equals(typeText, "Object", StringComparison.Ordinal) ||
               string.Equals(typeText, "System.Object", StringComparison.Ordinal) ||
               string.Equals(typeText, "global::System.Object", StringComparison.Ordinal);
    }

    private static bool IsEventArgsLikeType(TypeSyntax typeSyntax, ITypeSymbol typeSymbol)
    {
        for (var current = typeSymbol; current != null; current = current.BaseType)
        {
            if (string.Equals(current.Name, "EventArgs", StringComparison.Ordinal) &&
                string.Equals(current.ContainingNamespace?.ToDisplayString(), "System", StringComparison.Ordinal))
            {
                return true;
            }

            if (current.Name?.EndsWith("EventArgs", StringComparison.Ordinal) == true)
                return true;
        }

        var typeName = GetSimpleTypeName(typeSyntax);
        return typeName?.EndsWith("EventArgs", StringComparison.Ordinal) == true;
    }

    private static string GetSimpleTypeName(TypeSyntax typeSyntax)
    {
        if (typeSyntax == null)
            return null;

        var typeText = typeSyntax.ToString().TrimEnd('?');
        var dotIndex = typeText.LastIndexOf('.');
        return dotIndex >= 0 ? typeText[(dotIndex + 1)..] : typeText;
    }

    private static bool CouldBeImplicitInterfaceImplementationWithoutResolution(
        SemanticModel semanticModel,
        MethodDeclarationSyntax method)
    {
        if (semanticModel == null || method == null)
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
