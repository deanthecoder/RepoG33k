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

public sealed class LocalVariableCanBeConstCodeReviewCheck : RoslynSemanticCodeReviewCheckBase, IFixableCodeReviewCheck
{
    public override string RuleId => "local-variable-can-be-const";

    public override string DisplayName => "Local variable can be const";

    public override CodeReviewCheckScope Scope => CodeReviewCheckScope.AddedLinesOnly;

    protected override void AnalyzeFile(
        CodeReviewAnalysisContext context,
        CodeReviewChangedFile file,
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        CodeSmellReport report)
    {
        foreach (var localDeclaration in root.DescendantNodes().OfType<LocalDeclarationStatementSyntax>())
        {
            if (localDeclaration.IsConst)
                continue;

            foreach (var variable in localDeclaration.Declaration.Variables)
            {
                if (semanticModel.GetDeclaredSymbol(variable) is not ILocalSymbol localSymbol)
                    continue;

                if (localSymbol.Type.TypeKind == TypeKind.Error)
                    continue;

                // Only handle simple types that can be const: string, numeric, boolean, char, null.
                if (!IsConstableType(localSymbol.Type))
                    continue;

                if (variable.Initializer == null)
                    continue;

                var constantValue = semanticModel.GetConstantValue(variable.Initializer.Value);
                if (!constantValue.HasValue)
                    continue;

                // Check for assignments in the same method/block.
                var methodBody = localDeclaration.Ancestors().OfType<BlockSyntax>().FirstOrDefault();
                if (methodBody == null)
                    continue;

                var isModified = methodBody.DescendantNodes()
                    .OfType<AssignmentExpressionSyntax>()
                    .Any(assignment => TargetsLocal(semanticModel, assignment.Left, localSymbol)) ||
                    methodBody.DescendantNodes()
                    .OfType<PrefixUnaryExpressionSyntax>()
                    .Any(unary => (unary.IsKind(SyntaxKind.PreIncrementExpression) || unary.IsKind(SyntaxKind.PreDecrementExpression)) && TargetsLocal(semanticModel, unary.Operand, localSymbol)) ||
                    methodBody.DescendantNodes()
                    .OfType<PostfixUnaryExpressionSyntax>()
                    .Any(unary => (unary.IsKind(SyntaxKind.PostIncrementExpression) || unary.IsKind(SyntaxKind.PostDecrementExpression)) && TargetsLocal(semanticModel, unary.Operand, localSymbol)) ||
                    methodBody.DescendantNodes()
                    .OfType<ArgumentSyntax>()
                    .Any(argument => (argument.RefOrOutKeyword.IsKind(SyntaxKind.RefKeyword) || argument.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword)) && TargetsLocal(semanticModel, argument.Expression, localSymbol));

                if (isModified)
                    continue;

                var lineNumber = variable.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                if (!file.IsAdded && !file.AddedLineNumbers.Contains(lineNumber))
                    continue;

                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Hint,
                    file.Path,
                    lineNumber,
                    $"Local variable `{localSymbol.Name}` can be made `const`.");
            }
        }
    }

    private static bool IsConstableType(ITypeSymbol type)
    {
        return type.SpecialType switch
        {
            SpecialType.System_Boolean => true,
            SpecialType.System_Char => true,
            SpecialType.System_SByte => true,
            SpecialType.System_Byte => true,
            SpecialType.System_Int16 => true,
            SpecialType.System_UInt16 => true,
            SpecialType.System_Int32 => true,
            SpecialType.System_UInt32 => true,
            SpecialType.System_Int64 => true,
            SpecialType.System_UInt64 => true,
            SpecialType.System_Single => true,
            SpecialType.System_Double => true,
            SpecialType.System_Decimal => true,
            SpecialType.System_String => true,
            _ => false
        };
    }

    private static bool TargetsLocal(SemanticModel semanticModel, ExpressionSyntax expression, ILocalSymbol localSymbol)
    {
        var symbol = semanticModel.GetSymbolInfo(expression).Symbol;
        return symbol != null && SymbolEqualityComparer.Default.Equals(symbol, localSymbol);
    }

    public bool CanFix(CodeSmellFinding finding) =>
        finding != null &&
        string.Equals(finding.RuleId, RuleId, StringComparison.OrdinalIgnoreCase);

    public bool TryFix(CodeSmellFinding finding, FileInfo resolvedFile, out string resultMessage)
    {
        if (!this.TryPrepareFix(finding, resolvedFile, out var sourceText, out var lineIndex, out resultMessage))
            return false;

        var line = sourceText.Lines[lineIndex];
        var syntaxTree = CSharpSyntaxTree.ParseText(sourceText);
        var root = syntaxTree.GetCompilationUnitRoot();

        var node = root.FindNode(new TextSpan(line.Start, line.Span.Length));
        if (node.AncestorsAndSelf().OfType<LocalDeclarationStatementSyntax>().FirstOrDefault() == null)
        {
            resultMessage = "Could not find local variable declaration.";
            return false;
        }

        // We need semantic model for accurate type if it's 'var'
        var file = new CodeReviewChangedFile(
            "M",
            resolvedFile.FullName,
            resolvedFile.FullName,
            sourceText.ToString(),
            sourceText.Lines.Select(l => l.ToString()).ToArray(),
            null);
        if (!RoslynCodeReviewCheckUtilities.TryGetSemanticAnalysis(
                file,
                out var fixRoot,
                out var semanticModel,
                out _,
                out _))
        {
            resultMessage = "Could not perform semantic analysis for fix.";
            return false;
        }

        var variable = fixRoot.DescendantNodes()
            .OfType<VariableDeclaratorSyntax>()
            .FirstOrDefault(v => v.GetLocation().GetLineSpan().StartLinePosition.Line == lineIndex);
        if (variable == null)
        {
            resultMessage = "Could not find the specific variable in declaration.";
            return false;
        }

        if (semanticModel.GetDeclaredSymbol(variable) is not ILocalSymbol localSymbol)
        {
             resultMessage = "Could not resolve local symbol.";
             return false;
        }

        var localDeclaration = variable.Ancestors().OfType<LocalDeclarationStatementSyntax>().First();

        var typeString = localSymbol.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        // Construct the new declaration.
        // E.g. var x = 1; -> const int x = 1;
        // E.g. string x = "a"; -> const string x = "a";

        var newLocalDeclaration = localDeclaration
            .WithModifiers(SyntaxFactory.TokenList(SyntaxFactory.Token(SyntaxKind.ConstKeyword).WithTrailingTrivia(SyntaxFactory.Space)))
            .WithDeclaration(localDeclaration.Declaration.WithType(SyntaxFactory.ParseTypeName(typeString).WithTrailingTrivia(SyntaxFactory.Space)));

        // Preserve trivia
        newLocalDeclaration = newLocalDeclaration.WithLeadingTrivia(localDeclaration.GetLeadingTrivia()).WithTrailingTrivia(localDeclaration.GetTrailingTrivia());

        var updatedRoot = fixRoot.ReplaceNode(localDeclaration, newLocalDeclaration);
        var updatedText = updatedRoot.ToFullString();

        if (!this.TryWriteUpdatedText(resolvedFile, updatedText, out resultMessage))
            return false;

        resultMessage = $"Made local variable `{localSymbol.Name}` constant.";
        return true;
    }
}
