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
/// Reports ordinary methods whose body contains no statements.
/// </summary>
/// <remarks>
/// Catches accidental placeholder methods while avoiding polymorphic and interface contracts where an empty body can be intentional.
/// </remarks>
public sealed class EmptyMethodCodeReviewCheck : RoslynSemanticCodeReviewCheckBase
{
    public override string RuleId => CodeReviewRuleIds.EmptyMethod;

    public override string DisplayName => "Empty method";

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
            if (method.Body == null || method.Body.Statements.Count != 0)
                continue;
            if (!IsEligibleEmptyMethod(semanticModel, method, out var methodSymbol))
                continue;

            AddFinding(
                report,
                CodeReviewFindingSeverity.Suggestion,
                file.Path,
                RoslynCodeReviewCheckUtilities.GetStartLine(method),
                $"Method `{methodSymbol.Name}` has an empty body; remove it or add the intended implementation.");
        }
    }

    private static bool IsEligibleEmptyMethod(
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

        if (string.Equals(methodSymbol.Name, "Dispose", StringComparison.Ordinal) &&
            methodSymbol.Parameters.Length == 0 &&
            methodSymbol.ReturnsVoid)
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
