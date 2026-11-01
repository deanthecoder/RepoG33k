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

/// <summary>
/// Detects RESX values with accidental leading or trailing whitespace.
/// </summary>
/// <remarks>
/// Resource strings are often copied into UI labels, prompts, and formatting logic, so stray boundary spaces can
/// create hard-to-spot rendering issues. This check keeps those mistakes visible while staying focused on changed
/// entries.
/// </remarks>
public sealed class ResxValueBoundaryWhitespaceCodeReviewCheck : CodeReviewCheckBase
{
    public override string RuleId => CodeReviewRuleIds.ResxValueBoundaryWhitespace;

    public override string DisplayName => "RESX value boundary whitespace";

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        foreach (var resxFile in context.ResxFiles)
        {
            if (!ResxCodeReviewUtilities.TryGetResxEntries(resxFile, out var entries))
                continue;

            foreach (var entry in entries.Values.Where(IsCandidateForFinding))
            {
                var hasLeadingWhitespace = entry.Value.Length > 0 && char.IsWhiteSpace(entry.Value[0]);
                var hasTrailingWhitespace = entry.Value.Length > 0 && char.IsWhiteSpace(entry.Value[^1]);
                if (!hasLeadingWhitespace && !hasTrailingWhitespace)
                    continue;

                var message = hasLeadingWhitespace && hasTrailingWhitespace
                    ? $"Resource value for key `{entry.Key}` has leading and trailing whitespace."
                    : hasLeadingWhitespace
                        ? $"Resource value for key `{entry.Key}` has leading whitespace."
                        : $"Resource value for key `{entry.Key}` has trailing whitespace.";

                AddFinding(
                    report,
                    CodeReviewFindingSeverity.Suggestion,
                    resxFile.Path,
                    entry.LineNumber > 0 ? entry.LineNumber : 1,
                    message);
            }

            bool IsCandidateForFinding(ResxEntry entry)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.Value))
                    return false;
                if (resxFile.IsAdded)
                    return true;

                return entry.LineNumber > 0 && resxFile.AddedLineNumbers.Contains(entry.LineNumber);
            }
        }
    }
}
