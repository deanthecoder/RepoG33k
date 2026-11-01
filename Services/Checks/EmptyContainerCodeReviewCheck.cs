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
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

public sealed class EmptyContainerCodeReviewCheck : CodeReviewCheckBase
{
    public override string RuleId => CodeReviewRuleIds.EmptyContainer;

    public override string DisplayName => "Empty multi-child containers";

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
                if (!MarkupContainerCodeReviewUtilities.IsElementLineRelevant(file, container))
                    continue;
                if (MarkupContainerCodeReviewUtilities.HasCodeBehindHookAttributes(container))
                    continue;

                var visualChildren = MarkupContainerCodeReviewUtilities.GetVisualChildren(container);
                if (visualChildren.Count != 0)
                    continue;

                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Hint,
                    file.Path,
                    MarkupContainerCodeReviewUtilities.GetLineNumber(container),
                    $"`{container.Name.LocalName}` has no child elements.");
            }
        }
    }
}
