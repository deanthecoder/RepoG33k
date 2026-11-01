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

/// <summary>
/// Detects local variables that use field-style prefixes such as <c>m_</c> or leading underscores.
/// </summary>
/// <remarks>
/// This helps keep method-local names visually distinct from fields, which makes code easier to scan and avoids
/// accidental drift from the repository's usual naming style.
/// </remarks>
public sealed class LocalVariablePrefixCodeReviewCheck : RoslynSemanticCodeReviewCheckBase
{
    public override string RuleId => CodeReviewRuleIds.LocalVariablePrefix;

    public override string DisplayName => "Local variable field-style prefixes";

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

                if (!HasDisallowedPrefix(localSymbol.Name))
                    continue;

                var lineNumber = RoslynCodeReviewCheckUtilities.GetStartLine(variable);
                if (!file.IsAdded && !file.AddedLineNumbers.Contains(lineNumber))
                    continue;

                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Hint,
                    file.Path,
                    lineNumber,
                    $"Local variable `{localSymbol.Name}` uses a field-style prefix. Prefer a plain local name.");
            }
        }
    }

    private static bool HasDisallowedPrefix(string name) =>
        !string.IsNullOrWhiteSpace(name) &&
        !string.Equals(name, "_", StringComparison.Ordinal) &&
        (name.StartsWith("m_", StringComparison.Ordinal) ||
         name.StartsWith('_'));
}
