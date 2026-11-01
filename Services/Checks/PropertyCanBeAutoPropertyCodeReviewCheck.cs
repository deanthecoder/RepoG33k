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
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

public sealed class PropertyCanBeAutoPropertyCodeReviewCheck : RoslynSemanticCodeReviewCheckBase
{
    public override string RuleId => "property-can-be-auto-property";

    public override string DisplayName => "New properties that can be auto-properties";

    protected override void AnalyzeFile(
        CodeReviewAnalysisContext context,
        CodeReviewChangedFile file,
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        CodeSmellReport report)
    {
        var properties = root.DescendantNodes().OfType<PropertyDeclarationSyntax>();
        foreach (var property in properties)
        {
            if (!RoslynCodeReviewCheckUtilities.IsNodeNew(file, property))
                continue;
            if (RoslynCodeReviewCheckUtilities.IsPrivateProperty(property))
                continue;
            if (RoslynCodeReviewCheckUtilities.IsExplicitInterfaceImplementation(property))
                continue;

            if (!RoslynCodeReviewCheckUtilities.TryGetSimplePropertyBackingFieldExpressions(property, out var getterTarget, out var setterTarget))
                continue;
            if (!TargetsSameBackingField(semanticModel, getterTarget, setterTarget))
                continue;

            var lineNumber = RoslynCodeReviewCheckUtilities.GetStartLine(property);
            AddFinding(
                report,
                CodeReviewFindingSeverity.Hint,
                file.Path,
                lineNumber,
                $"Property '{property.Identifier.ValueText}' can likely be converted to an auto-property.");
        }
    }

    private static bool TargetsSameBackingField(SemanticModel semanticModel, ExpressionSyntax getterTarget, ExpressionSyntax setterTarget)
    {
        var getterSymbol = semanticModel.GetSymbolInfo(getterTarget).Symbol as IFieldSymbol;
        var setterSymbol = semanticModel.GetSymbolInfo(setterTarget).Symbol as IFieldSymbol;
        return getterSymbol != null && SymbolEqualityComparer.Default.Equals(getterSymbol, setterSymbol);
    }
}
