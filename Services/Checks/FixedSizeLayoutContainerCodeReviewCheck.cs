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
using System.Xml.Linq;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

public sealed class FixedSizeLayoutContainerCodeReviewCheck : CodeReviewCheckBase
{
    public override string RuleId => CodeReviewRuleIds.FixedSizeLayoutContainer;

    public override string DisplayName => "Fixed size on layout containers";

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        foreach (var file in context.Files)
        {
            if (!CodeReviewFileClassification.IsMarkupFilePath(file.Path))
                continue;
            if (!MarkupContainerCodeReviewUtilities.TryParseDocument(file.Text, out var document))
                continue;

            foreach (var container in document.Descendants().Where(MarkupContainerCodeReviewUtilities.IsTargetMultiChildContainer))
            {
                if (MarkupContainerCodeReviewUtilities.HasCodeBehindHookAttributes(container))
                    continue;
                if (!MarkupContainerCodeReviewUtilities.IsElementOrAnyAttributeLineRelevant(file, container, "Width", "Height"))
                    continue;

                var widthAttribute = FindFixedSizeAttribute(container, "Width");
                var heightAttribute = FindFixedSizeAttribute(container, "Height");
                if (widthAttribute == null && heightAttribute == null)
                    continue;

                var dimensions = widthAttribute != null && heightAttribute != null
                    ? "Width/Height"
                    : widthAttribute != null
                        ? "Width"
                        : "Height";

                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Suggestion,
                    file.Path,
                    MarkupContainerCodeReviewUtilities.GetLineNumber(container),
                    $"`{container.Name.LocalName}` sets fixed `{dimensions}`. Consider flexible layout sizing.");
            }
        }
    }

    private static XAttribute FindFixedSizeAttribute(XElement container, string attributeName)
    {
        return container.Attributes().FirstOrDefault(attribute =>
            !attribute.IsNamespaceDeclaration &&
            string.Equals(attribute.Name.LocalName, attributeName, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(attribute.Value));
    }
}
