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
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

public sealed class UnusedLocalVariableCodeReviewCheck : RoslynSemanticCodeReviewCheckBase
{
    public override string RuleId => CodeReviewRuleIds.UnusedLocalVariable;

    public override string DisplayName => "Unused local variables";

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
            foreach (var variable in localDeclaration.Declaration.Variables)
            {
                if (semanticModel.GetDeclaredSymbol(variable) is not ILocalSymbol localSymbol)
                    continue;
                if (string.Equals(localSymbol.Name, "_", StringComparison.Ordinal))
                    continue;
                if (IsRead(semanticModel, localDeclaration, localSymbol))
                    continue;

                var lineNumber = RoslynCodeReviewCheckUtilities.GetStartLine(variable);
                if (!file.IsAdded && !file.AddedLineNumbers.Contains(lineNumber))
                    continue;

                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Hint,
                    file.Path,
                    lineNumber,
                    $"Local variable `{localSymbol.Name}` is never read.");
            }
        }
    }

    private static bool IsRead(SemanticModel semanticModel, LocalDeclarationStatementSyntax declaration, ILocalSymbol localSymbol)
    {
        var containingBlock = declaration.FirstAncestorOrSelf<BlockSyntax>();
        if (containingBlock == null)
            return true;

        var dataFlow = semanticModel.AnalyzeDataFlow(containingBlock);
        if (!dataFlow.Succeeded)
            return true;

        return dataFlow.ReadInside.Any(symbol => SymbolEqualityComparer.Default.Equals(symbol, localSymbol)) ||
               dataFlow.ReadOutside.Any(symbol => SymbolEqualityComparer.Default.Equals(symbol, localSymbol));
    }
}
