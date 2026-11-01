// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software.

using System.Text;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;
using ReviewG33k.Services;
using ReviewG33k.Services.DiffLens;
using ReviewG33k.ViewModels;

namespace RepoG33k.Services;

/// <summary>Runs ReviewG33k's checks against the exact snapshots displayed in DiffLens.</summary>
/// <remarks>Staged and commit reviews never substitute the working file for the selected snapshot.</remarks>
internal static class WorkspaceReviewService
{
    public static Task<CodeSmellReport> ReviewAsync(IReadOnlyList<DiffLensEntry> entries, Action<string> progress, CancellationToken token)
    {
        var files = entries.Where(e => e.File != null).Select(e =>
        {
            var file = e.File;
            var model = InlineDiffBuilder.Diff(file.Baseline, file.WorkingText);
            var added = model.Lines.Where(l => l.Type == ChangeType.Inserted && l.Position.HasValue).Select(l => l.Position!.Value).ToHashSet();
            return new CodeReviewChangedFile(file.Baseline.Length == 0 ? "A" : "M", e.Path, file.FullPath, file.WorkingText,
                file.WorkingText.Replace("\r\n", "\n").Split('\n'), added, file.Baseline, Encoding.UTF8.GetBytes(file.WorkingText), Encoding.UTF8.GetBytes(file.Baseline));
        }).ToArray();
        var skipped = entries.Count(e => e.File == null);
        var source = new CodeReviewChangedFileSourceResult(files, skipped == 0 ? [] : [$"Skipped {skipped} binary or unavailable file(s)."]);
        var analyzer = new CodeSmellReportAnalyzer(new GitCommandRunner(), new CodeReviewRulePreferenceService(Settings.Instance));
        return analyzer.AnalyzeLoadedFilesAsync(source, progressLogger: progress, cancellationToken: token);
    }
}
