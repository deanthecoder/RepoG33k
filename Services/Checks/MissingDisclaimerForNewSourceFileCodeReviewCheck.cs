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
using System.Text.RegularExpressions;
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services.Checks;

/// <summary>
/// Heuristically flags added source files that appear to be missing a disclaimer/header comment
/// when modified source files in the same changeset already contain one.
/// </summary>
public sealed class MissingDisclaimerForNewSourceFileCodeReviewCheck : CodeReviewCheckBase
{
    private const int DisclaimerScanLineLimit = 30;
    private static readonly Regex DisclaimerMarkerRegex = new(
        "Code authored by|copyright|all rights reserved|licensed under|SPDX-License-Identifier|permission is hereby granted|THE SOFTWARE IS PROVIDED",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public override string RuleId => CodeReviewRuleIds.MissingDisclaimerForNewSourceFile;

    public override string DisplayName => "New source file disclaimer/header";

    public override CodeReviewCheckScope Scope => CodeReviewCheckScope.ChangedFileSet;

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        var changedFiles = context.AllChangedFiles
            .Where(file => file != null)
            .Where(file => CodeReviewFileClassification.IsLikelySourceCodePath(file.Path))
            .ToArray();
        if (changedFiles.Length == 0)
            return;

        var usesDisclaimers = changedFiles
            .Where(file => !file.IsAdded)
            .Any(HasDisclaimerHeader);
        if (!usesDisclaimers)
            return;

        foreach (var file in changedFiles.Where(file => file.IsAdded && !HasDisclaimerHeader(file)))
        {
            AddFinding(
                report,
                CodeReviewFindingSeverity.Hint,
                file.Path,
                1,
                "New source file might be missing a disclaimer/header comment. See similar files for reference.");
        }
    }

    private static bool HasDisclaimerHeader(CodeReviewChangedFile file)
    {
        if (file?.Lines == null || file.Lines.Count == 0)
            return false;

        var linesToScan = Math.Min(file.Lines.Count, DisclaimerScanLineLimit);
        for (var lineIndex = 0; lineIndex < linesToScan; lineIndex++)
        {
            var line = file.Lines[lineIndex];
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (DisclaimerMarkerRegex.IsMatch(line))
                return true;
        }

        return false;
    }
}
