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
using System.Collections.Generic;
using System.Linq;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

/// <summary>
/// Detects mixed American and British English spelling in neutral RESX files.
/// </summary>
/// <remarks>
/// Base resource files usually set the tone for the rest of the UI. This check helps keep wording consistent so the
/// product does not drift between dialects as strings are added over time.
/// </remarks>
public sealed class ResxMixedEnglishDialectCodeReviewCheck : CodeReviewCheckBase
{
    private const int MatchThreshold = 2;
    private const int MaxPreviewWords = 4;

    public override string RuleId => CodeReviewRuleIds.ResxMixedEnglishDialect;

    public override string DisplayName => "RESX mixed US/UK English";

    public override CodeReviewCheckScope Scope => CodeReviewCheckScope.ChangedFileSet;

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        foreach (var file in context.ResxFiles.Where(ResxCodeReviewUtilities.IsNeutralResxFile))
        {
            if (!ResxCodeReviewUtilities.TryGetDialectScanResult(file, out var result))
                continue;
            if (result.AmericanMatches.Count < MatchThreshold || result.BritishMatches.Count < MatchThreshold)
                continue;

            var lineNumber = result.AmericanMatches
                .Concat(result.BritishMatches)
                .Select(match => match.LineNumber)
                .DefaultIfEmpty(1)
                .Min();

            AddFinding(
                report,
                CodeReviewFindingSeverity.Suggestion,
                file.Path,
                lineNumber,
                $"Mixed US/UK English detected in `{file.Path}` (US: {result.AmericanMatches.Count}, UK: {result.BritishMatches.Count}). Examples: {BuildWordPreview(result.AmericanMatches)} and {BuildWordPreview(result.BritishMatches)}.");
        }
    }

    private static string BuildWordPreview(IReadOnlyList<ResxDialectWordMatch> matches)
    {
        var preview = (matches ?? Array.Empty<ResxDialectWordMatch>())
            .Select(match => $"`{match.Word}`")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxPreviewWords)
            .ToArray();

        return preview.Length == 0 ? "N/A" : string.Join(", ", preview);
    }
}
