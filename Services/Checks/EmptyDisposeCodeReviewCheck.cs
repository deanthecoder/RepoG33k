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
/// Reports `Dispose()` methods whose body contains no cleanup statements.
/// </summary>
/// <remarks>
/// Helps reviewers spot placeholder disposal implementations that make ownership look handled when nothing is actually released.
/// </remarks>
public sealed class EmptyDisposeCodeReviewCheck : RoslynSemanticCodeReviewCheckBase
{
    public override string RuleId => CodeReviewRuleIds.EmptyDispose;

    public override string DisplayName => "Empty `Dispose()` method";

    protected override void AnalyzeFile(
        CodeReviewAnalysisContext context,
        CodeReviewChangedFile file,
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        CodeSmellReport report)
    {
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (!IsDisposeMethod(method))
                continue;
            if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, method.Span))
                continue;
            if (method.Body == null || method.Body.Statements.Count != 0)
                continue;

            AddFinding(
                report,
                CodeReviewFindingSeverity.Suggestion,
                file.Path,
                RoslynCodeReviewCheckUtilities.GetStartLine(method),
                "`Dispose()` has an empty body; remove it or add the intended cleanup.");
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
        if (method.TypeParameterList != null)
            return false;

        var returnType = method.ReturnType as PredefinedTypeSyntax;
        return returnType?.Keyword.IsKind(SyntaxKind.VoidKeyword) == true;
    }
}
