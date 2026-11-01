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

public sealed class RedundantSelfLookupCodeReviewCheck : RoslynSemanticCodeReviewCheckBase, IFixableCodeReviewCheck
{
    public override string RuleId => CodeReviewRuleIds.RedundantSelfLookup;

    public override string DisplayName => "Redundant self lookups";

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
        var invocation = root
            .DescendantNodes()
            .OfType<InvocationExpressionSyntax>()
            .FirstOrDefault(node => node.Span.IntersectsWith(lineSpan) && TryGetRedundantSelfLookupPattern(node, out _, out _));
        if (invocation == null)
        {
            resultMessage = "Target line does not contain a redundant self lookup.";
            return false;
        }

        var updatedRoot = root.ReplaceNode(invocation, SyntaxFactory.ThisExpression().WithTriviaFrom(invocation));
        var updatedText = updatedRoot.ToFullString();
        updatedText = CodeReviewFixTextUtilities.CollapseConsecutiveBlankLinesNearLine(updatedText, lineIndex);

        if (!this.TryWriteUpdatedText(resolvedFile, updatedText, out resultMessage))
            return false;

        resultMessage = "Replaced redundant self lookup with `this`.";
        return true;
    }

    protected override void AnalyzeFile(
        CodeReviewAnalysisContext context,
        CodeReviewChangedFile file,
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        CodeSmellReport report)
    {
        foreach (var invocation in root.DescendantNodes().OfType<InvocationExpressionSyntax>())
        {
            if (!TryGetRedundantSelfLookupPattern(invocation, out var ownerTypeName, out var lookupCall))
                continue;
            if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, invocation.Span))
                continue;
            if (!IsLikelyRedundantForContainingType(invocation, semanticModel, ownerTypeName))
                continue;

            AddFinding(
                report,
                CodeReviewFindingSeverity.Hint,
                file.Path,
                RoslynCodeReviewCheckUtilities.GetStartLine(invocation),
                $"Redundant self lookup. Use `this` instead of `{lookupCall}`.");
        }
    }

    private static bool TryGetRedundantSelfLookupPattern(
        InvocationExpressionSyntax invocation,
        out string ownerTypeName,
        out string lookupCall)
    {
        ownerTypeName = null;
        lookupCall = null;

        if (invocation?.Expression is not MemberAccessExpressionSyntax
            {
                Name: IdentifierNameSyntax methodIdentifier
            } memberAccess)
        {
            return false;
        }

        if (invocation.ArgumentList?.Arguments.Count != 1 ||
            invocation.ArgumentList.Arguments[0].Expression is not ThisExpressionSyntax)
        {
            return false;
        }

        var methodName = methodIdentifier.Identifier.ValueText;
        if (string.IsNullOrWhiteSpace(methodName) || !methodName.StartsWith("Get", StringComparison.Ordinal) || methodName.Length <= 3)
            return false;

        var ownerFromMethod = methodName[3..];
        var ownerFromExpression = GetRightMostIdentifier(memberAccess.Expression);
        if (string.IsNullOrWhiteSpace(ownerFromMethod) ||
            string.IsNullOrWhiteSpace(ownerFromExpression) ||
            !string.Equals(ownerFromExpression, ownerFromMethod, StringComparison.Ordinal))
        {
            return false;
        }

        ownerTypeName = ownerFromMethod;
        lookupCall = $"{ownerFromExpression}.{methodName}(this)";
        return true;
    }

    private static bool IsLikelyRedundantForContainingType(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        string ownerTypeName)
    {
        if (invocation == null || semanticModel == null || string.IsNullOrWhiteSpace(ownerTypeName))
            return false;

        var methodSymbol = semanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
        if (methodSymbol != null)
        {
            if (!methodSymbol.IsStatic || !string.Equals(methodSymbol.ContainingType?.Name, ownerTypeName, StringComparison.Ordinal))
                return false;

            var containingType = semanticModel.GetEnclosingSymbol(invocation.SpanStart)?.ContainingType;
            return IsInTypeHierarchy(methodSymbol.ContainingType, containingType);
        }

        // Fallback when semantic resolution is incomplete.
        var containingTypeSyntax = invocation.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        if (containingTypeSyntax == null)
            return false;
        if (string.Equals(containingTypeSyntax.Identifier.ValueText, ownerTypeName, StringComparison.Ordinal))
            return true;

        return containingTypeSyntax.BaseList?.Types.Any(type =>
            string.Equals(GetRightMostIdentifier(type.Type), ownerTypeName, StringComparison.Ordinal)) == true;
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

    private static string GetRightMostIdentifier(SyntaxNode node) =>
        node switch
        {
            IdentifierNameSyntax identifierName => identifierName.Identifier.ValueText,
            QualifiedNameSyntax qualifiedName => qualifiedName.Right.Identifier.ValueText,
            AliasQualifiedNameSyntax aliasQualifiedName => aliasQualifiedName.Name.Identifier.ValueText,
            MemberAccessExpressionSyntax memberAccess => GetRightMostIdentifier(memberAccess.Name),
            GenericNameSyntax genericName => genericName.Identifier.ValueText,
            _ => null
        };
}
