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

public sealed class UnusedPrivateMemberCodeReviewCheck : RoslynSemanticCodeReviewCheckBase, IFixableCodeReviewCheck
{
    private const string PrivateMethodPrefix = "Private method `";

    public override string RuleId => CodeReviewRuleIds.UnusedPrivateMember;

    public override string DisplayName => "Unused private members";

    public bool CanFix(CodeSmellFinding finding) =>
        finding != null &&
        finding.LineNumber > 0 &&
        string.Equals(finding.RuleId, RuleId, StringComparison.OrdinalIgnoreCase) &&
        IsPrivateMethodFindingMessage(finding.Message);

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

        var tree = CSharpSyntaxTree.ParseText(sourceText);
        var root = tree.GetCompilationUnitRoot();
        var targetMethod = FindPrivateMethodForLine(root, sourceText, lineIndex);
        if (targetMethod == null)
        {
            resultMessage = "Could not find a private method at the selected line.";
            return false;
        }

        var methodName = targetMethod.Identifier.ValueText;
        var spanToRemove = targetMethod.FullSpan;
        var updatedText = sourceText.WithChanges(new TextChange(spanToRemove, string.Empty)).ToString();
        updatedText = CodeReviewFixTextUtilities.CollapseConsecutiveBlankLinesNearLine(updatedText, lineIndex);

        if (!this.TryWriteUpdatedText(resolvedFile, updatedText, out resultMessage))
            return false;

        resultMessage = string.IsNullOrWhiteSpace(methodName)
            ? "Removed unused private method."
            : $"Removed unused private method `{methodName}`.";
        return true;
    }

    protected override void AnalyzeFile(
        CodeReviewAnalysisContext context,
        CodeReviewChangedFile file,
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        CodeSmellReport report)
    {
        AnalyzeFields(file, report, semanticModel, root);
        AnalyzeMethods(file, report, semanticModel, root);
        AnalyzeProperties(file, report, semanticModel, root);
    }

    private void AnalyzeFields(CodeReviewChangedFile file, CodeSmellReport report, SemanticModel semanticModel, CompilationUnitSyntax root)
    {
        foreach (var field in root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            if (!field.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PrivateKeyword)))
                continue;
            if (field.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.ConstKeyword)))
                continue;
            if (IsInPartialType(field))
                continue;

            foreach (var variable in field.Declaration.Variables)
            {
                if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, variable.Span))
                    continue;

                if (semanticModel.GetDeclaredSymbol(variable) is not IFieldSymbol symbol || symbol.IsImplicitlyDeclared)
                    continue;
                if (HasAnyReference(root, semanticModel, symbol))
                    continue;

                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Hint,
                    file.Path,
                    variable.GetLocation().GetLineSpan().StartLinePosition.Line + 1,
                    $"Private field `{symbol.Name}` appears to be unused.");
            }
        }
    }

    private void AnalyzeMethods(CodeReviewChangedFile file, CodeSmellReport report, SemanticModel semanticModel, CompilationUnitSyntax root)
    {
        foreach (var method in root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, method.Span))
                continue;
            if (method.ExplicitInterfaceSpecifier != null)
                continue;
            if (IsInPartialType(method))
                continue;

            if (semanticModel.GetDeclaredSymbol(method) is not { } symbol || symbol.IsImplicitlyDeclared)
                continue;
            if (symbol.DeclaredAccessibility != Accessibility.Private)
                continue;
            if (symbol.MethodKind != MethodKind.Ordinary)
                continue;
            if (symbol.IsOverride || symbol.IsAbstract)
                continue;
            if (IsProgramMainEntryPoint(symbol))
                continue;
            if (HasAnyReference(root, semanticModel, symbol))
                continue;

            AddFinding(
                report,
                CodeReviewFindingSeverity.Hint,
                file.Path,
                RoslynCodeReviewCheckUtilities.GetStartLine(method),
                $"Private method `{symbol.Name}` appears to be unused.");
        }
    }

    private void AnalyzeProperties(CodeReviewChangedFile file, CodeSmellReport report, SemanticModel semanticModel, CompilationUnitSyntax root)
    {
        foreach (var property in root.DescendantNodes().OfType<PropertyDeclarationSyntax>())
        {
            if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, property.Span))
                continue;
            if (RoslynCodeReviewCheckUtilities.IsExplicitInterfaceImplementation(property))
                continue;
            if (IsInPartialType(property))
                continue;

            if (semanticModel.GetDeclaredSymbol(property) is not { } symbol || symbol.IsImplicitlyDeclared)
                continue;
            if (symbol.DeclaredAccessibility != Accessibility.Private)
                continue;
            if (symbol.ExplicitInterfaceImplementations.Length > 0)
                continue;
            if (HasAnyReference(root, semanticModel, symbol))
                continue;

            AddFinding(
                report,
                CodeReviewFindingSeverity.Hint,
                file.Path,
                RoslynCodeReviewCheckUtilities.GetStartLine(property),
                $"Private property `{symbol.Name}` appears to be unused.");
        }
    }

    private static bool HasAnyReference(CompilationUnitSyntax root, SemanticModel semanticModel, ISymbol symbol)
    {
        foreach (var name in root.DescendantNodes().OfType<SimpleNameSyntax>())
        {
            var symbolInfo = semanticModel.GetSymbolInfo(name);
            if (SymbolMatches(symbolInfo, symbol))
                return true;
        }

        return false;
    }

    private static bool SymbolMatches(SymbolInfo symbolInfo, ISymbol symbol)
    {
        if (SymbolsAreEquivalent(symbolInfo.Symbol, symbol))
            return true;

        return symbolInfo.CandidateSymbols.Any(candidateSymbol => SymbolsAreEquivalent(candidateSymbol, symbol));
    }

    private static bool SymbolsAreEquivalent(ISymbol left, ISymbol right)
    {
        if (left == null || right == null)
            return false;

        var normalizedLeft = NormalizeSymbolForComparison(left);
        var normalizedRight = NormalizeSymbolForComparison(right);

        return SymbolEqualityComparer.Default.Equals(normalizedLeft, normalizedRight) ||
               SymbolEqualityComparer.Default.Equals(normalizedLeft?.OriginalDefinition, normalizedRight?.OriginalDefinition);
    }

    private static ISymbol NormalizeSymbolForComparison(ISymbol symbol)
    {
        if (symbol is not IMethodSymbol methodSymbol)
            return symbol;

        // Extension invocation syntax (`value.ExtensionMethod(...)`) often produces a reduced method symbol.
        // Compare against the underlying declared extension method to avoid false "unused" reports.
        return methodSymbol.ReducedFrom ?? methodSymbol.ConstructedFrom ?? methodSymbol;
    }

    private static bool IsInPartialType(SyntaxNode node)
    {
        var typeDeclaration = node.Ancestors().OfType<TypeDeclarationSyntax>().FirstOrDefault();
        return typeDeclaration?.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword)) == true;
    }

    private static bool IsProgramMainEntryPoint(IMethodSymbol symbol)
    {
        if (symbol == null)
            return false;
        if (!string.Equals(symbol.Name, "Main", StringComparison.Ordinal))
            return false;
        if (symbol.MethodKind != MethodKind.Ordinary)
            return false;

        var containingType = symbol.ContainingType;
        return containingType != null &&
               containingType.TypeKind == TypeKind.Class &&
               string.Equals(containingType.Name, "Program", StringComparison.Ordinal);
    }

    private static MethodDeclarationSyntax FindPrivateMethodForLine(
        CompilationUnitSyntax root,
        SourceText sourceText,
        int lineIndex)
    {
        if (root == null || sourceText == null || lineIndex < 0 || lineIndex >= sourceText.Lines.Count)
            return null;

        var lineSpan = sourceText.Lines[lineIndex].Span;
        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
        var byStartLine = methods.FirstOrDefault(method =>
            method.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PrivateKeyword)) &&
            method.GetLocation().GetLineSpan().StartLinePosition.Line == lineIndex);
        if (byStartLine != null)
            return byStartLine;

        return methods.FirstOrDefault(method =>
            method.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PrivateKeyword)) &&
            method.Span.IntersectsWith(lineSpan));
    }

    private static bool IsPrivateMethodFindingMessage(string message) =>
        !string.IsNullOrWhiteSpace(message) &&
        message.StartsWith(PrivateMethodPrefix, StringComparison.Ordinal);
}
