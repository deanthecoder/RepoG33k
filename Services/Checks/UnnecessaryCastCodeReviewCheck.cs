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

public sealed class UnnecessaryCastCodeReviewCheck : RoslynSemanticCodeReviewCheckBase
{
    public override string RuleId => "unnecessary-cast";

    public override string DisplayName => "Unnecessary cast";

    protected override void AnalyzeFile(
        CodeReviewAnalysisContext context,
        CodeReviewChangedFile file,
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        CodeSmellReport report)
    {
        foreach (var cast in root.DescendantNodes().OfType<CastExpressionSyntax>())
        {
            if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, cast.Span))
                continue;
            if (!IsClearlyUnnecessaryCast(semanticModel, cast, out var typeName))
                continue;

            var lineNumber = RoslynCodeReviewCheckUtilities.GetStartLine(cast);
            AddFinding(
                report,
                CodeReviewFindingSeverity.Hint,
                file.Path,
                lineNumber,
                $"Unnecessary cast to `{typeName}`; expression is already `{typeName}`.");
        }
    }

    private static bool IsClearlyUnnecessaryCast(
        SemanticModel semanticModel,
        CastExpressionSyntax cast,
        out string typeName)
    {
        typeName = null;

        var targetType = semanticModel.GetTypeInfo(cast.Type).Type;
        var expressionType = semanticModel.GetTypeInfo(cast.Expression).Type;
        if (targetType == null || expressionType == null)
            return false;
        if (!SymbolEqualityComparer.Default.Equals(targetType, expressionType))
            return false;

        typeName = targetType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        return !string.IsNullOrWhiteSpace(typeName);
    }
}
