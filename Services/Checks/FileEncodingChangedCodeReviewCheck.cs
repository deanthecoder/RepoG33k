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

/// <summary>
/// Reports changed text files whose detected encoding changed.
/// </summary>
/// <remarks>
/// Helps reviewers spot accidental byte-order mark or UTF-family changes that are easy to miss in code diffs.
/// </remarks>
public sealed class FileEncodingChangedCodeReviewCheck : CodeReviewCheckBase
{
    public override string RuleId => CodeReviewRuleIds.FileEncodingChanged;

    public override string DisplayName => "File encoding changed";

    public override CodeReviewCheckScope Scope => CodeReviewCheckScope.ChangedFileSet;

    public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
    {
        foreach (var file in context.AllChangedFiles)
        {
            if (file == null || file.IsAdded || file.CurrentBytes == null || file.BaselineBytes == null)
                continue;

            var baselineEncoding = TextFileChangeUtilities.DetectEncoding(file.BaselineBytes);
            var currentEncoding = TextFileChangeUtilities.DetectEncoding(file.CurrentBytes);
            if (baselineEncoding == currentEncoding ||
                baselineEncoding is TextEncodingKind.None or TextEncodingKind.Unknown ||
                currentEncoding is TextEncodingKind.None or TextEncodingKind.Unknown)
            {
                continue;
            }

            AddFinding(
                report,
                CodeReviewFindingSeverity.Hint,
                file.Path,
                1,
                $"File encoding changed from {TextFileChangeUtilities.GetEncodingDisplayName(baselineEncoding)} to {TextFileChangeUtilities.GetEncodingDisplayName(currentEncoding)}.");
        }
    }
}
