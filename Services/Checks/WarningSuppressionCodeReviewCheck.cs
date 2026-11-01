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

public sealed class WarningSuppressionCodeReviewCheck : CodeReviewCheckBase, IFixableCodeReviewCheck
{
    public override string RuleId => "warning-suppression";

    public override string DisplayName => "Warning suppressions (`#pragma` / `[SuppressMessage]`)";

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

        var line = sourceText.Lines[lineIndex];
        var trimmed = line.ToString().TrimStart();

        string updatedText;
        if (trimmed.StartsWith("#pragma warning disable", StringComparison.Ordinal))
        {
            var spanToRemove = TextSpan.FromBounds(line.Start, line.EndIncludingLineBreak);
            updatedText = sourceText.WithChanges(new TextChange(spanToRemove, string.Empty)).ToString();
            updatedText = CodeReviewFixTextUtilities.CollapseConsecutiveBlankLinesNearLine(updatedText, lineIndex);

            if (!this.TryWriteUpdatedText(resolvedFile, updatedText, out resultMessage))
            {
                return false;
            }

            resultMessage = "Removed `#pragma warning disable` suppression.";
            return true;
        }

        var root = CSharpSyntaxTree.ParseText(sourceText).GetCompilationUnitRoot();
        var lineSpan = TextSpan.FromBounds(line.Start, line.End);

        var attribute = root
            .DescendantNodes()
            .OfType<AttributeSyntax>()
            .FirstOrDefault(node => node.Span.IntersectsWith(lineSpan) && IsSuppressMessageAttribute(node));
        if (attribute == null)
        {
            resultMessage = "Target line is not a supported warning suppression.";
            return false;
        }

        var attributeList = attribute.Parent as AttributeListSyntax;
        if (attributeList == null)
        {
            resultMessage = "Could not resolve suppression attribute list.";
            return false;
        }

        SyntaxNode updatedRootNode;
        var updatedAttributes = attributeList.Attributes.Remove(attribute);
        if (updatedAttributes.Count == 0)
        {
            updatedRootNode = root.RemoveNode(attributeList, SyntaxRemoveOptions.KeepNoTrivia);
        }
        else
        {
            var updatedList = attributeList.WithAttributes(updatedAttributes);
            updatedRootNode = root.ReplaceNode(attributeList, updatedList);
        }

        updatedText = updatedRootNode.ToFullString();
        updatedText = CodeReviewFixTextUtilities.CollapseConsecutiveBlankLinesNearLine(updatedText, lineIndex);

        if (!this.TryWriteUpdatedText(resolvedFile, updatedText, out resultMessage))
        {
            return false;
        }

        resultMessage = "Removed `[SuppressMessage]` suppression.";
        return true;
    }

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        foreach (var file in context.Files)
        {
            var root = RoslynCodeReviewCheckUtilities.ParseRoot(file);

            var pragmaDirectives = root
                .DescendantTrivia(descendIntoTrivia: true)
                .Select(trivia => trivia.GetStructure())
                .OfType<PragmaWarningDirectiveTriviaSyntax>();
            foreach (var directive in pragmaDirectives)
            {
                if (!IsDisablePragma(directive))
                    continue;

                var lineNumber = directive.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
                if (!file.IsAdded && !file.AddedLineNumbers.Contains(lineNumber))
                    continue;

                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Important,
                    file.Path,
                    lineNumber,
                    "Warning suppression via `#pragma warning disable` added. Prefer fixing the warning or documenting why suppression is required.");
            }

            foreach (var attribute in root.DescendantNodes().OfType<AttributeSyntax>())
            {
                if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, attribute.Span))
                    continue;
                if (!IsSuppressMessageAttribute(attribute))
                    continue;

                var lineNumber = RoslynCodeReviewCheckUtilities.GetStartLine(attribute);
                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Important,
                    file.Path,
                    lineNumber,
                    "Warning suppression via `[SuppressMessage]` added. Prefer fixing the warning or documenting why suppression is required.");
            }
        }
    }

    private static bool IsDisablePragma(PragmaWarningDirectiveTriviaSyntax directive) =>
        directive != null &&
        string.Equals(directive.DisableOrRestoreKeyword.ValueText, "disable", StringComparison.OrdinalIgnoreCase);

    private static bool IsSuppressMessageAttribute(AttributeSyntax attribute)
    {
        var name = GetAttributeName(attribute?.Name);
        if (string.IsNullOrWhiteSpace(name))
            return false;

        return string.Equals(name, "SuppressMessage", StringComparison.Ordinal) ||
               string.Equals(name, "SuppressMessageAttribute", StringComparison.Ordinal);
    }

    private static string GetAttributeName(NameSyntax nameSyntax)
    {
        return nameSyntax switch
        {
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            QualifiedNameSyntax qualified => GetAttributeName(qualified.Right),
            AliasQualifiedNameSyntax aliasQualified => aliasQualified.Name.Identifier.ValueText,
            GenericNameSyntax generic => generic.Identifier.ValueText,
            _ => nameSyntax?.ToString()
        };
    }
}
