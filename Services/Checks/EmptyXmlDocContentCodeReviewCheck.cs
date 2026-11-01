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

public sealed class EmptyXmlDocContentCodeReviewCheck : CodeReviewCheckBase
{
    public override string RuleId => CodeReviewRuleIds.EmptyXmlDocContent;

    public override string DisplayName => "XML docs have meaningful content";

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        foreach (var file in context.Files)
        {
            if (string.IsNullOrWhiteSpace(file?.Path) || !file.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                continue;

            var root = RoslynCodeReviewCheckUtilities.ParseRoot(file);
            foreach (var trivia in root.DescendantTrivia(descendIntoTrivia: true))
            {
                if (!trivia.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia) &&
                    !trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
                {
                    continue;
                }

                if (trivia.GetStructure() is not DocumentationCommentTriviaSyntax documentation)
                    continue;

                foreach (var node in documentation.Content)
                {
                    if (node is XmlElementSyntax element)
                    {
                        AnalyzeElement(file, report, element);
                        continue;
                    }

                    if (node is XmlEmptyElementSyntax emptyElement)
                        AnalyzeEmptyElement(file, report, emptyElement);
                }
            }
        }
    }

    private static void AnalyzeElement(CodeReviewChangedFile file, CodeSmellReport report, XmlElementSyntax element)
    {
        var tagName = element?.StartTag?.Name?.LocalName.ValueText;
        if (!IsSupportedTag(tagName))
            return;
        if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, element.Span))
            return;

        if (HasMeaningfulContent(element))
            return;

        AddEmptyDocFinding(file, report, element, tagName, GetNameAttributeValue(element.StartTag?.Attributes ?? default));
    }

    private static void AnalyzeEmptyElement(CodeReviewChangedFile file, CodeSmellReport report, XmlEmptyElementSyntax element)
    {
        var tagName = element?.Name?.LocalName.ValueText;
        if (!IsSupportedTag(tagName))
            return;
        if (!RoslynCodeReviewCheckUtilities.SpanContainsAddedLine(file, element.Span))
            return;

        AddEmptyDocFinding(file, report, element, tagName, GetNameAttributeValue(element.Attributes));
    }

    private static void AddEmptyDocFinding(
        CodeReviewChangedFile file,
        CodeSmellReport report,
        SyntaxNode elementNode,
        string tagName,
        string nameAttributeValue)
    {
        var lineNumber = RoslynCodeReviewCheckUtilities.GetStartLine(elementNode);
        var tagDisplay = string.Equals(tagName, "param", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(nameAttributeValue)
            ? $"<{tagName} name=\"{nameAttributeValue}\">"
            : $"<{tagName}>";

        report.AddFinding(
            CodeReviewFindingSeverity.Hint,
            CodeReviewRuleIds.EmptyXmlDocContent,
            file.Path,
            lineNumber,
            $"XML doc tag `{tagDisplay}` has no content.");
    }

    private static bool IsSupportedTag(string tagName)
    {
        return string.Equals(tagName, "summary", StringComparison.Ordinal) ||
               string.Equals(tagName, "param", StringComparison.Ordinal) ||
               string.Equals(tagName, "returns", StringComparison.Ordinal);
    }

    private static bool HasMeaningfulContent(XmlElementSyntax element)
    {
        if (element == null)
            return false;

        foreach (var contentNode in element.Content)
        {
            if (contentNode is XmlTextSyntax xmlText)
            {
                var textValue = string.Concat(xmlText.TextTokens.Select(token => token.ValueText));
                if (!string.IsNullOrWhiteSpace(textValue))
                    return true;

                continue;
            }

            if (contentNode is XmlElementSyntax or XmlEmptyElementSyntax or XmlCDataSectionSyntax)
                return true;
        }

        return false;
    }

    private static string GetNameAttributeValue(SyntaxList<XmlAttributeSyntax> attributes)
    {
        foreach (var attribute in attributes)
        {
            if (attribute is not XmlNameAttributeSyntax nameAttribute)
                continue;
            if (!string.Equals(nameAttribute.Name?.LocalName.ValueText, "name", StringComparison.Ordinal))
                continue;

            if (nameAttribute.Identifier?.Identifier is { } identifierToken)
                return identifierToken.ValueText;
        }

        return null;
    }
}
