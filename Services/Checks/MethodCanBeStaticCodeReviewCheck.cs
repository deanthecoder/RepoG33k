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
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

public sealed class MethodCanBeStaticCodeReviewCheck : RoslynSemanticCodeReviewCheckBase, IFixableCodeReviewCheck
{
    public override string RuleId => "method-can-be-static";

    public override string DisplayName => "Methods that can be static";

    public bool CanFix(CodeSmellFinding finding) =>
        finding != null &&
        string.Equals(finding.RuleId, RuleId, StringComparison.OrdinalIgnoreCase) &&
        finding.LineNumber > 0;

    public bool TryFix(CodeSmellFinding finding, FileInfo resolvedFile, out string resultMessage)
    {
        if (!this.TryPrepareFix(
                finding,
                resolvedFile,
                out var sourceText,
                out var lineIndex,
                out resultMessage))
        {
            return false;
        }

        var root = CSharpSyntaxTree.ParseText(sourceText).GetCompilationUnitRoot();
        var lineSpan = TextSpan.FromBounds(sourceText.Lines[lineIndex].Start, sourceText.Lines[lineIndex].End);

        var method = root
            .DescendantNodes()
            .OfType<MethodDeclarationSyntax>()
            .FirstOrDefault(node => GetStartLine(node) == lineIndex + 1 && !node.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.StaticKeyword))) ??
            root.DescendantNodes()
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault(node => node.Span.IntersectsWith(lineSpan) && !node.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.StaticKeyword)));
        if (method == null)
        {
            resultMessage = "Target line does not contain a non-static method.";
            return false;
        }

        var updatedMethod = AddStaticModifier(method);
        var updatedRoot = root.ReplaceNode(method, updatedMethod);
        var updatedText = updatedRoot.ToFullString();
        updatedText = CodeReviewFixTextUtilities.CollapseConsecutiveBlankLinesNearLine(updatedText, lineIndex);

        if (!this.TryWriteUpdatedText(resolvedFile, updatedText, out resultMessage))
            return false;

        var methodName = method.Identifier.ValueText;
        resultMessage = string.IsNullOrWhiteSpace(methodName)
            ? "Added static modifier to method."
            : $"Added static modifier to method `{methodName}`.";
        return true;
    }

    protected override void AnalyzeFile(
        CodeReviewAnalysisContext context,
        CodeReviewChangedFile file,
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        CodeSmellReport report)
    {
        if (CodeReviewFileClassification.IsTestFilePath(file.Path))
            return;

        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>();
        foreach (var method in methods)
        {
            if (!RoslynCodeReviewCheckUtilities.IsNodeNew(file, method))
                continue;
            if (!CanMethodBeStatic(semanticModel, method, out var methodSymbol))
                continue;
            if (UsesInstanceState(semanticModel, method, methodSymbol.ContainingType))
                continue;

            var lineNumber = RoslynCodeReviewCheckUtilities.GetStartLine(method);
            AddFinding(
                report,
                CodeReviewFindingSeverity.Hint,
                file.Path,
                lineNumber,
                string.IsNullOrWhiteSpace(method.Identifier.ValueText)
                    ? "This method can likely be made static."
                    : $"Method `{method.Identifier.ValueText}` can likely be made static.");
        }
    }

    private static bool CanMethodBeStatic(SemanticModel semanticModel, MethodDeclarationSyntax method, out IMethodSymbol methodSymbol)
    {
        methodSymbol = semanticModel.GetDeclaredSymbol(method);
        if (methodSymbol == null)
            return false;
        if (methodSymbol.DeclaredAccessibility != Accessibility.Private)
            return false;

        if (methodSymbol.IsStatic ||
            methodSymbol.IsAbstract ||
            methodSymbol.IsOverride ||
            methodSymbol.IsVirtual ||
            methodSymbol.IsExtern ||
            methodSymbol.IsImplicitlyDeclared)
        {
            return false;
        }

        if (methodSymbol.ContainingType == null || methodSymbol.ContainingType.TypeKind == TypeKind.Interface)
            return false;
        if (methodSymbol.ExplicitInterfaceImplementations.Length > 0)
            return false;
        if (ImplementsInterfaceMember(methodSymbol))
            return false;
        if (ContainsUnqualifiedSameNameInvocation(method, methodSymbol.Name))
            return false;
        if (IsInPartialType(method))
            return false;
        if (CouldBeImplicitInterfaceImplementationWithoutResolution(semanticModel, method, methodSymbol.ContainingType))
            return false;
        if (method.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword)))
            return false;

        return true;
    }

    private static bool ContainsUnqualifiedSameNameInvocation(MethodDeclarationSyntax method, string methodName)
    {
        if (method == null || string.IsNullOrWhiteSpace(methodName))
            return false;

        return method.DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation =>
                invocation.Expression is IdentifierNameSyntax identifier &&
                string.Equals(identifier.Identifier.ValueText, methodName, StringComparison.Ordinal));
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

                if (SymbolsAreEquivalent(methodSymbol, implementation))
                    return true;
            }
        }

        return false;
    }

    private static bool SymbolsAreEquivalent(IMethodSymbol left, IMethodSymbol right) =>
        left != null &&
        right != null &&
        (SymbolEqualityComparer.Default.Equals(left, right) ||
         SymbolEqualityComparer.Default.Equals(left.OriginalDefinition, right.OriginalDefinition));

    private static bool IsInPartialType(MethodDeclarationSyntax method)
    {
        var containingType = method?.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        return containingType?.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword)) == true;
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

    private static bool UsesInstanceState(SemanticModel semanticModel, MethodDeclarationSyntax method, INamedTypeSymbol containingType)
    {
        if (containingType == null)
            return true;

        if (method.DescendantNodes().OfType<ThisExpressionSyntax>().Any() ||
            method.DescendantNodes().OfType<BaseExpressionSyntax>().Any())
        {
            return true;
        }

        foreach (var invocation in method.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (InvocationRequiresInstance(semanticModel, invocation, containingType))
                return true;
        }

        foreach (var node in method.DescendantNodes())
        {
            if (node is not IdentifierNameSyntax && node is not MemberAccessExpressionSyntax)
                continue;
            if (node.Parent is InvocationExpressionSyntax invocation && invocation.Expression == node)
                continue;

            var symbolInfo = semanticModel.GetSymbolInfo(node);
            var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();
            if (symbol == null)
            {
                // In single-file semantic analysis (for example XAML code-behind), generated members may be unresolved.
                // Be conservative to avoid false "can be static" suggestions when instance state is likely involved.
                if (CouldReferenceInstanceMember(node))
                    return true;

                continue;
            }

            if (!RequiresInstance(symbol, containingType))
                continue;

            return true;
        }

        return false;
    }

    private static bool InvocationRequiresInstance(
        SemanticModel semanticModel,
        InvocationExpressionSyntax invocation,
        INamedTypeSymbol containingType)
    {
        if (semanticModel == null || invocation == null || containingType == null)
            return false;

        var targetSymbolInfo = semanticModel.GetSymbolInfo(invocation.Expression);
        var targetSymbols = targetSymbolInfo.Symbol != null
            ? [targetSymbolInfo.Symbol]
            : targetSymbolInfo.CandidateSymbols;
        foreach (var targetSymbol in targetSymbols)
        {
            if (RequiresInstance(targetSymbol, containingType))
                return true;
        }

        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
        var candidateMethods = symbolInfo.Symbol != null
            ? [symbolInfo.Symbol]
            : symbolInfo.CandidateSymbols;

        foreach (var candidateMethod in candidateMethods.OfType<IMethodSymbol>())
        {
            if (RequiresInstance(candidateMethod, containingType))
                return true;
        }

        return symbolInfo.Symbol == null &&
               symbolInfo.CandidateSymbols.Length == 0 &&
               CouldReferenceInstanceMember(invocation.Expression);
    }

    private static bool RequiresInstance(ISymbol symbol, INamedTypeSymbol containingType)
    {
        if (symbol == null)
            return false;

        if (symbol is IFieldSymbol fieldSymbol)
            return !fieldSymbol.IsStatic && IsInTypeHierarchy(fieldSymbol.ContainingType, containingType);
        if (symbol is IPropertySymbol propertySymbol)
            return !propertySymbol.IsStatic && IsInTypeHierarchy(propertySymbol.ContainingType, containingType);
        if (symbol is IEventSymbol eventSymbol)
            return !eventSymbol.IsStatic && IsInTypeHierarchy(eventSymbol.ContainingType, containingType);
        if (symbol is IMethodSymbol methodSymbol)
            return !methodSymbol.IsStatic && IsInTypeHierarchy(methodSymbol.ContainingType, containingType);

        return false;
    }

    private static bool IsInTypeHierarchy(INamedTypeSymbol candidateType, INamedTypeSymbol containingType)
    {
        if (candidateType == null || containingType == null)
            return false;

        for (var current = containingType; current != null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(candidateType, current))
                return true;
        }

        return false;
    }

    private static bool CouldReferenceInstanceMember(SyntaxNode node) =>
        node is IdentifierNameSyntax or MemberAccessExpressionSyntax;

    private static int GetStartLine(SyntaxNode node) =>
        node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;

    private static MethodDeclarationSyntax AddStaticModifier(MethodDeclarationSyntax method)
    {
        var staticToken = SyntaxFactory.Token(SyntaxKind.StaticKeyword)
            .WithTrailingTrivia(SyntaxFactory.Space);
        var modifiers = method.Modifiers;
        var insertionIndex = 0;
        for (var index = 0; index < modifiers.Count; index++)
        {
            if (modifiers[index].IsKind(SyntaxKind.PublicKeyword) ||
                modifiers[index].IsKind(SyntaxKind.PrivateKeyword) ||
                modifiers[index].IsKind(SyntaxKind.ProtectedKeyword) ||
                modifiers[index].IsKind(SyntaxKind.InternalKeyword))
            {
                insertionIndex = index + 1;
            }
        }

        return method.WithModifiers(modifiers.Insert(insertionIndex, staticToken));
    }
}
