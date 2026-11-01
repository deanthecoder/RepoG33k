// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

public sealed class EmptyCatchCodeReviewCheck : CodeReviewCheckBase
{
    public override string RuleId => "empty-catch";

    public override string DisplayName => "Empty catch blocks";

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        foreach (var file in context.Files)
        {
            foreach (var catchBlock in CodeReviewCheckUtilities.EnumerateAddedCatchBlocks(file))
            {
                if (CodeReviewCheckUtilities.IsCancellationCatch(catchBlock))
                    continue;

                if (string.IsNullOrWhiteSpace(catchBlock.BodyWithoutComments))
                {
                    var severity = catchBlock.HasComments
                        ? CodeReviewFindingSeverity.Suggestion
                        : CodeReviewFindingSeverity.Important;
                    AddFinding(report, severity, file.Path, catchBlock.StartLine, "Empty catch block detected.");
                }
            }
        }
    }
}
