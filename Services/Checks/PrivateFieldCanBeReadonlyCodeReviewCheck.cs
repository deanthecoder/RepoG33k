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

public sealed class PrivateFieldCanBeReadonlyCodeReviewCheck : RoslynSemanticCodeReviewCheckBase
{
    public override string RuleId => "private-field-can-be-readonly";

    public override string DisplayName => "Private field can be readonly";

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
            if (fieldDeclaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.ReadOnlyKeyword) || modifier.IsKind(SyntaxKind.ConstKeyword)))
                continue;

            var containingTypeDeclaration = fieldDeclaration.Parent as TypeDeclarationSyntax;
            if (!CanAnalyzeContainingType(containingTypeDeclaration))
                continue;

            var containingTypeSymbol = semanticModel.GetDeclaredSymbol(containingTypeDeclaration);
            if (containingTypeSymbol == null)
                continue;

            foreach (var variable in fieldDeclaration.Declaration?.Variables ?? [])
            {
                if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, variable.Span))
                    continue;

                if (semanticModel.GetDeclaredSymbol(variable) is not IFieldSymbol fieldSymbol)
                    continue;

                if (!CanBeReadonly(semanticModel, containingTypeDeclaration, containingTypeSymbol, fieldSymbol, variable))
                    continue;

                var lineNumber = variable.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Hint,
                    file.Path,
                    lineNumber,
                    $"Private field `{fieldSymbol.Name}` can be made `readonly`.");
            }
        }
    }

    private static bool CanAnalyzeContainingType(TypeDeclarationSyntax typeDeclaration) =>
        typeDeclaration != null &&
        !typeDeclaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword)) &&
        typeDeclaration is ClassDeclarationSyntax;

    private static bool CanBeReadonly(
        SemanticModel semanticModel,
        TypeDeclarationSyntax containingTypeDeclaration,
        INamedTypeSymbol containingTypeSymbol,
        IFieldSymbol fieldSymbol,
        VariableDeclaratorSyntax variable)
    {
        if (fieldSymbol.DeclaredAccessibility != Accessibility.Private ||
            fieldSymbol.IsConst ||
            fieldSymbol.IsReadOnly ||
            fieldSymbol.IsImplicitlyDeclared ||
            IsUnsupportedReadonlyFieldType(fieldSymbol.Type))
        {
            return false;
        }

        var hasInitializer = variable.Initializer != null;
        var hasAllowedWrite = false;

        foreach (var assignment in containingTypeDeclaration.DescendantNodes().OfType<AssignmentExpressionSyntax>())
        {
            if (!TargetsField(semanticModel, assignment.Left, fieldSymbol))
                continue;

            if (IsWriteAllowedInReadonlyContext(semanticModel, assignment, fieldSymbol, containingTypeSymbol))
            {
                hasAllowedWrite = true;
                continue;
            }

            return false;
        }

        foreach (var unary in containingTypeDeclaration.DescendantNodes().OfType<PrefixUnaryExpressionSyntax>())
        {
            if (!unary.IsKind(SyntaxKind.PreIncrementExpression) && !unary.IsKind(SyntaxKind.PreDecrementExpression))
                continue;
            if (!TargetsField(semanticModel, unary.Operand, fieldSymbol))
                continue;

            if (IsWriteAllowedInReadonlyContext(semanticModel, unary, fieldSymbol, containingTypeSymbol))
            {
                hasAllowedWrite = true;
                continue;
            }

            return false;
        }

        foreach (var unary in containingTypeDeclaration.DescendantNodes().OfType<PostfixUnaryExpressionSyntax>())
        {
            if (!unary.IsKind(SyntaxKind.PostIncrementExpression) && !unary.IsKind(SyntaxKind.PostDecrementExpression))
                continue;
            if (!TargetsField(semanticModel, unary.Operand, fieldSymbol))
                continue;

            if (IsWriteAllowedInReadonlyContext(semanticModel, unary, fieldSymbol, containingTypeSymbol))
            {
                hasAllowedWrite = true;
                continue;
            }

            return false;
        }

        foreach (var argument in containingTypeDeclaration.DescendantNodes().OfType<ArgumentSyntax>())
        {
            if (!argument.RefOrOutKeyword.IsKind(SyntaxKind.RefKeyword) &&
                !argument.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword))
            {
                continue;
            }

            if (!TargetsField(semanticModel, argument.Expression, fieldSymbol))
                continue;

            if (IsWriteAllowedInReadonlyContext(semanticModel, argument, fieldSymbol, containingTypeSymbol))
            {
                hasAllowedWrite = true;
                continue;
            }

            return false;
        }

        return hasInitializer || hasAllowedWrite;
    }

    private static bool IsUnsupportedReadonlyFieldType(ITypeSymbol typeSymbol) =>
        typeSymbol != null &&
        string.Equals(typeSymbol.ToDisplayString(), "System.Runtime.InteropServices.GCHandle", System.StringComparison.Ordinal);

    private static bool TargetsField(SemanticModel semanticModel, ExpressionSyntax expression, IFieldSymbol fieldSymbol)
    {
        var symbol = semanticModel.GetSymbolInfo(expression).Symbol;
        return symbol != null && SymbolEqualityComparer.Default.Equals(symbol, fieldSymbol);
    }

    private static bool IsWriteAllowedInReadonlyContext(
        SemanticModel semanticModel,
        SyntaxNode writeNode,
        IFieldSymbol fieldSymbol,
        INamedTypeSymbol containingTypeSymbol)
    {
        var constructor = writeNode.Ancestors().OfType<ConstructorDeclarationSyntax>().FirstOrDefault();
        if (constructor == null)
            return false;

        if (semanticModel.GetDeclaredSymbol(constructor) is not { } constructorSymbol)
            return false;

        if (!SymbolEqualityComparer.Default.Equals(constructorSymbol.ContainingType, containingTypeSymbol))
            return false;

        if (fieldSymbol.IsStatic)
            return constructorSymbol.IsStatic;

        return !constructorSymbol.IsStatic;
    }
}
