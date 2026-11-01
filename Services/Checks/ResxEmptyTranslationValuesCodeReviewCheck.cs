// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Linq;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

public sealed class ResxEmptyTranslationValuesCodeReviewCheck : CodeReviewCheckBase
{
    public override string RuleId => "resx-empty-translation-values";

    public override string DisplayName => "RESX empty translation values";

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        foreach (var localeFile in context.ResxFiles)
        {
            if (!ResxCodeReviewUtilities.TryGetLocaleMetadata(localeFile, out _, out _, out _))
                continue;
            if (!ResxCodeReviewUtilities.TryGetResxEntries(localeFile, out var entries))
                continue;

            foreach (var entry in entries.Values.Where(IsCandidateForFinding))
            {
                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Suggestion,
                    localeFile.Path,
                    entry.LineNumber > 0 ? entry.LineNumber : 1,
                    $"Translation value for key `{entry.Key}` is empty.");
            }

            bool IsCandidateForFinding(ResxEntry entry)
            {
                if (!string.IsNullOrWhiteSpace(entry.Value))
                    return false;
                if (localeFile.IsAdded)
                    return true;

                return entry.LineNumber > 0 && localeFile.AddedLineNumbers.Contains(entry.LineNumber);
            }
        }
    }
}
