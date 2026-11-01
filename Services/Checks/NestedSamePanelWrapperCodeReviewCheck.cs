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

public sealed class NestedSamePanelWrapperCodeReviewCheck : CodeReviewCheckBase
{
    public override string RuleId => CodeReviewRuleIds.NestedSamePanelWrapper;

    public override string DisplayName => "Nested same-panel wrappers with no effect";

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

                var visualChildren = MarkupContainerCodeReviewUtilities.GetVisualChildren(container);
                if (visualChildren.Count != 1)
                    continue;

                var child = visualChildren[0];
                if (MarkupContainerCodeReviewUtilities.HasCodeBehindHookAttributes(child))
                    continue;
                if (!MarkupContainerCodeReviewUtilities.IsNestedSamePanelWithoutEffect(container, child))
                    continue;
                if (!MarkupContainerCodeReviewUtilities.IsElementLineRelevant(file, container) &&
                    !MarkupContainerCodeReviewUtilities.IsElementLineRelevant(file, child))
                {
                    continue;
                }

                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Hint,
                    file.Path,
                    MarkupContainerCodeReviewUtilities.GetLineNumber(container),
                    $"Nested `{container.Name.LocalName}` wrapper has no effect. Consider flattening.");
            }
        }
    }
}
