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
/// Detects American English spellings inside <c>en-GB</c> RESX files.
/// </summary>
/// <remarks>
/// Locale-specific resource files should reinforce the wording that locale promises. This check helps catch US
/// spellings slipping into British English resource sets before they reach users.
/// </remarks>
public sealed class ResxAmericanEnglishInBritishLocaleCodeReviewCheck : CodeReviewCheckBase
{
    private const int MatchThreshold = 2;
    private const int MaxPreviewWords = 4;

    public override string RuleId => CodeReviewRuleIds.ResxAmericanEnglishInBritishLocale;

    public override string DisplayName => "RESX American English in en-GB locale";

    public override CodeReviewCheckScope Scope => CodeReviewCheckScope.ChangedFileSet;

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        foreach (var file in context.ResxFiles.Where(candidate => ResxCodeReviewUtilities.IsLocaleResxFile(candidate, "en-GB")))
        {
            if (!ResxCodeReviewUtilities.TryGetDialectScanResult(file, out var result))
                continue;
            if (result.AmericanMatches.Count < MatchThreshold)
                continue;

            var lineNumber = result.AmericanMatches
                .Select(match => match.LineNumber)
                .DefaultIfEmpty(1)
                .Min();

            AddFinding(
                report,
                CodeReviewFindingSeverity.Suggestion,
                file.Path,
                lineNumber,
                $"British English resource `{file.Path}` contains American spellings ({result.AmericanMatches.Count} match(es)). Examples: {BuildWordPreview(result.AmericanMatches)}.");
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
