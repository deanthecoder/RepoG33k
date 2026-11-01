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

public sealed class UnnecessaryEnumMemberValueCodeReviewCheck : RoslynSemanticCodeReviewCheckBase, IFixableCodeReviewCheck
{
    public override string RuleId => CodeReviewRuleIds.UnnecessaryEnumMemberValue;

    public override string DisplayName => "Unnecessary enum member values";

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

        var syntaxTree = CSharpSyntaxTree.ParseText(sourceText);
        var root = syntaxTree.GetCompilationUnitRoot();

        var lineStart = sourceText.Lines[lineIndex].Start;
        var lineEnd = sourceText.Lines[lineIndex].End;
        var lineSpan = TextSpan.FromBounds(lineStart, lineEnd);

        var member = root.DescendantNodes()
            .OfType<EnumMemberDeclarationSyntax>()
            .FirstOrDefault(node => node.EqualsValue != null && node.Span.IntersectsWith(lineSpan));
        if (member == null)
        {
            resultMessage = "Could not find an enum member assignment on the target line.";
            return false;
        }

        var memberName = member.Identifier.ValueText;
        var memberTrailingTrivia = member.GetTrailingTrivia();
        var updatedMember = member
            .WithIdentifier(
                member.Identifier.WithTrailingTrivia(
                    TrimTrailingWhitespaceTrivia(member.Identifier.TrailingTrivia)))
            .WithEqualsValue(null)
            .WithTrailingTrivia(memberTrailingTrivia);
        var updatedRoot = root.ReplaceNode(member, updatedMember);
        var updatedText = updatedRoot.ToFullString();
        if (!this.TryWriteUpdatedText(resolvedFile, updatedText, out resultMessage))
            return false;

        resultMessage = string.IsNullOrWhiteSpace(memberName)
            ? "Removed unnecessary enum member assignment."
            : $"Removed unnecessary enum member assignment for `{memberName}`.";
        return true;
    }

    protected override void AnalyzeFile(
        CodeReviewAnalysisContext context,
        CodeReviewChangedFile file,
        CompilationUnitSyntax root,
        SemanticModel semanticModel,
        CodeSmellReport report)
    {
        foreach (var enumDeclaration in root.DescendantNodes().OfType<EnumDeclarationSyntax>())
        {
            decimal? previousValue = null;
            foreach (var member in enumDeclaration.Members)
            {
                var expectedValue = previousValue.HasValue ? previousValue.Value + 1m : 0m;

                if (TryGetConstantNumericValue(semanticModel, member, out var currentValue))
                {
                    if (member.EqualsValue != null &&
                        !ContainsLeftShiftExpression(member.EqualsValue.Value) &&
                        currentValue == expectedValue &&
                        RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, member.Span))
                    {
                        var lineNumber = RoslynCodeReviewCheckUtilities.GetStartLine(member);
                        AddFinding(
                            report,
                            CodeReviewFindingSeverity.Hint,
                            file.Path,
                            lineNumber,
                            $"Enum member `{member.Identifier.ValueText}` has explicit value `{member.EqualsValue.Value}` that matches the default sequence.");
                    }

                    previousValue = currentValue;
                }
                else
                {
                    previousValue = null;
                }
            }
        }
    }

    private static bool ContainsLeftShiftExpression(ExpressionSyntax expression) =>
        expression != null &&
        expression
            .DescendantNodesAndSelf()
            .OfType<BinaryExpressionSyntax>()
            .Any(node => node.IsKind(SyntaxKind.LeftShiftExpression));

    private static bool TryGetConstantNumericValue(SemanticModel semanticModel, EnumMemberDeclarationSyntax member, out decimal value)
    {
        value = 0m;
        if (semanticModel.GetDeclaredSymbol(member) is not { } fieldSymbol)
            return false;
        if (!fieldSymbol.HasConstantValue)
            return false;

        return TryConvertToDecimal(fieldSymbol.ConstantValue, out value);
    }

    private static bool TryConvertToDecimal(object rawValue, out decimal value)
    {
        value = 0m;
        if (rawValue == null)
            return false;

        try
        {
            value = rawValue switch
            {
                byte byteValue => byteValue,
                sbyte sbyteValue => sbyteValue,
                short shortValue => shortValue,
                ushort ushortValue => ushortValue,
                int intValue => intValue,
                uint uintValue => uintValue,
                long longValue => longValue,
                ulong ulongValue => ulongValue,
                _ => Convert.ToDecimal(rawValue)
            };

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static SyntaxTriviaList TrimTrailingWhitespaceTrivia(SyntaxTriviaList triviaList)
    {
        if (triviaList.Count == 0)
            return triviaList;

        var trimToIndex = triviaList.Count - 1;
        while (trimToIndex >= 0 && triviaList[trimToIndex].IsKind(SyntaxKind.WhitespaceTrivia))
            trimToIndex--;

        return trimToIndex == triviaList.Count - 1
            ? triviaList
            : SyntaxFactory.TriviaList(triviaList.Take(trimToIndex + 1));
    }
}
