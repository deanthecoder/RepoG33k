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

public sealed class DisposeMethodWithoutIDisposableCodeReviewCheck : RoslynSemanticCodeReviewCheckBase
{
    public override string RuleId => "dispose-without-idisposable";

    public override string DisplayName => "`Dispose()` method without `IDisposable`";

    protected override void AnalyzeFile(
        CodeReviewAnalysisContext context,
        CodeReviewChangedFile file,
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        CodeSmellReport report)
    {
        var disposableInterface = semanticModel.Compilation.GetTypeByMetadataName("System.IDisposable");
        if (disposableInterface == null)
            return;

        foreach (var classDeclaration in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            if (classDeclaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword)))
                continue;

            var disposeMethods = classDeclaration.Members
                .OfType<MethodDeclarationSyntax>()
                .Where(IsDisposeMethod)
                .ToArray();
            if (disposeMethods.Length == 0)
                continue;

            var addedDisposeMethod = disposeMethods
                .FirstOrDefault(method => RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, method.Span));
            if (addedDisposeMethod == null)
                continue;

            var classSymbol = semanticModel.GetDeclaredSymbol(classDeclaration);
            if (!CanReliablyResolveInheritance(semanticModel, classDeclaration, classSymbol))
                continue;

            if (classSymbol.AllInterfaces.Any(@interface => SymbolEqualityComparer.Default.Equals(@interface, disposableInterface)))
                continue;

            AddFinding(
                report,
                CodeReviewFindingSeverity.Suggestion,
                file.Path,
                RoslynCodeReviewCheckUtilities.GetStartLine(addedDisposeMethod),
                $"Type `{classDeclaration.Identifier.ValueText}` defines `Dispose()` but does not implement `IDisposable`.");
        }
    }

    private static bool IsDisposeMethod(MethodDeclarationSyntax method)
    {
        if (method == null)
            return false;
        if (method.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.StaticKeyword)))
            return false;
        if (!string.Equals(method.Identifier.ValueText, "Dispose", StringComparison.Ordinal))
            return false;
        if (method.ParameterList?.Parameters.Count != 0)
            return false;

        var returnType = method.ReturnType as PredefinedTypeSyntax;
        return returnType?.Keyword.IsKind(SyntaxKind.VoidKeyword) == true;
    }

    private static bool CanReliablyResolveInheritance(
        SemanticModel semanticModel,
        ClassDeclarationSyntax classDeclaration,
        INamedTypeSymbol classSymbol)
    {
        if (classDeclaration == null || classSymbol == null)
            return false;
        if (classSymbol.BaseType is IErrorTypeSymbol)
            return false;
        if (classSymbol.Interfaces.Any(@interface => @interface is IErrorTypeSymbol))
            return false;

        if (classDeclaration.BaseList == null)
            return true;

        foreach (var baseType in classDeclaration.BaseList.Types)
        {
            var resolvedType = semanticModel.GetTypeInfo(baseType.Type).Type;
            if (resolvedType == null || resolvedType is IErrorTypeSymbol || resolvedType.TypeKind == TypeKind.Error)
                return false;
        }

        return true;
    }
}
