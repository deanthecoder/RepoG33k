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
using System.IO;
using System.Linq;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

public sealed class ResxUnexpectedExtraKeysCodeReviewCheck : CodeReviewCheckBase
{
    private const int MaxPreviewKeys = 5;

    public override string RuleId => "resx-unexpected-extra-keys";

    public override string DisplayName => "RESX locale unexpected extra keys";

    public override CodeReviewCheckScope Scope => CodeReviewCheckScope.ChangedFileSet;

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        foreach (var localeFile in context.ResxFiles.Where(file => file.IsAdded))
        {
            if (!ResxCodeReviewUtilities.TryGetLocaleMetadata(localeFile, out var baseRelativePath, out var baseFullPath, out _))
                continue;
            if (!ResxCodeReviewUtilities.TryGetResxEntries(localeFile, out var localeEntries))
                continue;
            if (!ResxCodeReviewUtilities.TryGetBaseResxEntries(context, baseRelativePath, baseFullPath, out var baseEntries))
                continue;

            var unexpectedKeys = localeEntries.Keys
                .Except(baseEntries.Keys, StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToArray();
            if (unexpectedKeys.Length == 0)
                continue;

            var preview = BuildPreview(unexpectedKeys);
            AddFinding(
                report,
                CodeReviewFindingSeverity.Hint,
                localeFile.Path,
                1,
                $"Locale resource `{Path.GetFileName(localeFile.Path)}` contains {unexpectedKeys.Length} key(s) not found in `{Path.GetFileName(baseRelativePath)}`: {preview}.");
        }
    }

    private static string BuildPreview(IReadOnlyList<string> keys)
    {
        if (keys == null || keys.Count == 0)
            return "N/A";

        var previewKeys = keys.Take(MaxPreviewKeys).Select(key => $"`{key}`").ToArray();
        return keys.Count > MaxPreviewKeys
            ? $"{string.Join(", ", previewKeys)} ..."
            : string.Join(", ", previewKeys);
    }
}
