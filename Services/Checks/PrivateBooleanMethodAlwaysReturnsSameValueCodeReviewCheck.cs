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

/// <summary>
/// Flags private bool methods whose control flow always returns the same literal value.
/// </summary>
/// <remarks>
/// For private methods this is usually a stronger smell because callers within the same type can often be simplified directly, or the
/// method can become void if the bool no longer carries meaning.
/// </remarks>
public sealed class PrivateBooleanMethodAlwaysReturnsSameValueCodeReviewCheck : RoslynSemanticCodeReviewCheckBase
{
    public override string RuleId => CodeReviewRuleIds.PrivateBooleanMethodAlwaysReturnsSameValue;

    public override string DisplayName => "Private bool methods that always return the same value";

    protected override void AnalyzeFile(
        CodeReviewAnalysisContext context,
        CodeReviewChangedFile file,
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        CodeSmellReport report)
    {
        if (CodeReviewFileClassification.IsLikelyTestCodeFile(file))
            return;

        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (!RoslynCodeReviewCheckUtilities.IsNodeNew(file, method))
                continue;
            if (semanticModel.GetDeclaredSymbol(method) is not IMethodSymbol methodSymbol)
                continue;
            if (!BooleanMethodReturnValueCodeReviewCheckUtilities.IsEligibleBooleanMethod(method, methodSymbol, semanticModel, requirePrivate: true))
                continue;
            if (!BooleanMethodReturnValueCodeReviewCheckUtilities.HasMeaningfulControlFlowToSimplify(method))
                continue;
            if (!BooleanMethodReturnValueCodeReviewCheckUtilities.TryGetConstantBooleanReturnValue(method, out var returnValue))
                continue;

            AddFinding(
                report,
                CodeReviewFindingSeverity.Suggestion,
                file.Path,
                RoslynCodeReviewCheckUtilities.GetStartLine(method),
                $"Private method `{method.Identifier.ValueText}` always returns `{returnValue.ToString().ToLowerInvariant()}`. Its callers can likely be simplified, or the method can likely be changed to `void`.");
        }
    }
}
