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
using System.Text;
using ReviewG33k.Services.Checks.Support;
using ReviewG33k.Views;

namespace ReviewG33k.Services;

/// <summary>
/// Contains reusable state calculations and mutations for review-results UI behavior.
/// </summary>
/// <remarks>
/// Useful for keeping summary, export, and inclusion-toggle logic out of window code-behind so
/// behavior stays centralized and easier to test.
/// </remarks>
internal sealed class ReviewResultsStateService
{
    public string BuildSummaryText(IEnumerable<ReviewResultRow> rows, int totalRowCount = -1)
    {
        var rowList = rows?.Where(row => row != null).ToArray() ?? [];
        if (rowList.Length == 0)
        {
            if (totalRowCount > 0)
                return $"0 finding(s) across 0 file(s) ({totalRowCount} hidden)";

            return "No review findings";
        }

        var fileCount = rowList
            .Select(row => row.Finding?.FilePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        var visibleCount = rowList.Length;
        var hiddenCount = totalRowCount > visibleCount
            ? totalRowCount - visibleCount
            : 0;
        return hiddenCount > 0
            ? $"{visibleCount} finding(s) across {fileCount} file(s) ({hiddenCount} hidden)"
            : $"{visibleCount} finding(s) across {fileCount} file(s)";
    }

    public ReviewResultsBatchActionState BuildBatchActionState(
        IEnumerable<ReviewResultRow> rows,
        bool isBulkCommenting,
        bool canCommentInBitbucket)
    {
        var rowList = rows?.Where(row => row != null).ToArray() ?? [];
        var hasIncludedFindings = rowList.Any(row => row.IsIncluded);
        var canCommentSelected = !isBulkCommenting &&
                                 canCommentInBitbucket &&
                                 rowList.Any(row => row.IsIncluded && row.CanCommentActive);

        return new ReviewResultsBatchActionState(
            CanToggleAll: rowList.Length > 0,
            CanExportToClipboard: hasIncludedFindings,
            CanCommentSelected: canCommentSelected);
    }

    public void ToggleAllIncluded(IList<ReviewResultRow> rows)
    {
        if (rows == null || rows.Count == 0)
            return;

        var includedCount = rows.Count(row => row.IsIncluded);
        var excludedCount = rows.Count - includedCount;
        var nextIncludedState = includedCount <= excludedCount;

        foreach (var row in rows)
            row.IsIncluded = nextIncludedState;
    }

    public void SetSameTypeIncludedState(IEnumerable<ReviewResultRow> rows, string ruleId, bool isIncluded)
    {
        if (rows == null || string.IsNullOrWhiteSpace(ruleId))
            return;

        foreach (var row in rows.Where(row => string.Equals(row.RuleId, ruleId, StringComparison.OrdinalIgnoreCase)))
            row.IsIncluded = isIncluded;
    }

    public void SetSameFileIncludedState(IEnumerable<ReviewResultRow> rows, string filePath, bool isIncluded)
    {
        if (rows == null || string.IsNullOrWhiteSpace(filePath))
            return;

        foreach (var row in rows.Where(row => RepositoryUtilities.AreSameRepoPath(row.Finding?.FilePath, filePath)))
            row.IsIncluded = isIncluded;
    }

    public void SetAllInFileIncludedState(IEnumerable<ReviewResultRow> rows, string filePath)
    {
        SetSameFileIncludedState(rows, filePath, isIncluded: true);
    }

    public IReadOnlyList<ReviewCategorySummary> BuildCategorySummaries(IEnumerable<ReviewResultRow> rows)
    {
        return (rows ?? [])
            .Where(row => row != null)
            .GroupBy(row => row.CategoryText ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .Select(group => new ReviewCategorySummary(
                group.First().CategoryText ?? string.Empty,
                group.Count(),
                GetCategoryColorHex(group.First().CategoryText)))
            .OrderByDescending(summary => summary.Count)
            .ThenBy(summary => GetCategorySortOrder(summary.CategoryName))
            .ThenBy(summary => summary.CategoryName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<ReviewResultRow> FilterRowsByVisibleCategories(
        IEnumerable<ReviewResultRow> rows,
        IEnumerable<string> visibleCategories)
    {
        var visibleCategorySet = new HashSet<string>(
            visibleCategories ?? [],
            StringComparer.OrdinalIgnoreCase);

        return (rows ?? [])
            .Where(row => row != null)
            .Where(row => visibleCategorySet.Contains(row.CategoryText))
            .ToArray();
    }

    public int CompareRows(ReviewResultRow left, ReviewResultRow right)
    {
        if (ReferenceEquals(left, right))
            return 0;
        if (left == null)
            return 1;
        if (right == null)
            return -1;

        var leftFinding = left.Finding;
        var rightFinding = right.Finding;
        var categoryCompare = GetCategorySortOrder(left.CategoryText)
            .CompareTo(GetCategorySortOrder(right.CategoryText));
        if (categoryCompare != 0)
            return categoryCompare;

        var ruleCompare = string.Compare(leftFinding?.RuleId, rightFinding?.RuleId, StringComparison.OrdinalIgnoreCase);
        if (ruleCompare != 0)
            return ruleCompare;

        var fileCompare = string.Compare(leftFinding?.FilePath, rightFinding?.FilePath, StringComparison.OrdinalIgnoreCase);
        if (fileCompare != 0)
            return fileCompare;

        return (leftFinding?.LineNumber ?? int.MaxValue).CompareTo(rightFinding?.LineNumber ?? int.MaxValue);
    }

    public int GetCategorySortOrder(string category) =>
        category switch
        {
            CodeReviewFindingCategoryResolver.Correctness => 0,
            CodeReviewFindingCategoryResolver.Threading => 1,
            CodeReviewFindingCategoryResolver.Performance => 2,
            CodeReviewFindingCategoryResolver.Resources => 3,
            CodeReviewFindingCategoryResolver.ApiDesign => 4,
            CodeReviewFindingCategoryResolver.Readability => 5,
            CodeReviewFindingCategoryResolver.Maintainability => 6,
            CodeReviewFindingCategoryResolver.Testing => 7,
            CodeReviewFindingCategoryResolver.Documentation => 8,
            CodeReviewFindingCategoryResolver.Ui => 9,
            CodeReviewFindingCategoryResolver.RepoHygiene => 10,
            _ => 11
        };

    public string GetCategoryColorHex(string category) =>
        category switch
        {
            CodeReviewFindingCategoryResolver.Correctness => "#FF6B6B",
            CodeReviewFindingCategoryResolver.Threading => "#5EEAD4",
            CodeReviewFindingCategoryResolver.Performance => "#FFD166",
            CodeReviewFindingCategoryResolver.Resources => "#7DD3FC",
            CodeReviewFindingCategoryResolver.ApiDesign => "#60A5FA",
            CodeReviewFindingCategoryResolver.Readability => "#34D399",
            CodeReviewFindingCategoryResolver.Maintainability => "#F472B6",
            CodeReviewFindingCategoryResolver.Testing => "#FBBF24",
            CodeReviewFindingCategoryResolver.Documentation => "#C084FC",
            CodeReviewFindingCategoryResolver.Ui => "#22D3EE",
            CodeReviewFindingCategoryResolver.RepoHygiene => "#94A3B8",
            _ => "#9FB0D0"
        };

    public string BuildExportText(IEnumerable<ReviewResultRow> rows, out int exportedCount)
    {
        var rowsToExport = rows?
            .Where(row => row != null && row.IsIncluded)
            .ToArray() ?? [];
        exportedCount = rowsToExport.Length;
        if (exportedCount == 0)
            return string.Empty;

        var builder = new StringBuilder();
        builder.AppendLine("ReviewG33k Findings");
        builder.AppendLine("Scope: Included findings");
        builder.Append("Count: ")
            .Append(exportedCount)
            .AppendLine();
        builder.AppendLine();

        for (var index = 0; index < rowsToExport.Length; index++)
        {
            var row = rowsToExport[index];
            var finding = row.Finding;
            var location = !string.IsNullOrWhiteSpace(finding.FilePath)
                ? finding.LineNumber > 0
                    ? $"{finding.FilePath}:{finding.LineNumber}"
                    : finding.FilePath
                : "(no file)";

            builder.Append(index + 1)
                .Append(". [")
                .Append(row.CategoryText)
                .Append("] ")
                .Append(location)
                .AppendLine();

            builder.Append("   ")
                .AppendLine((finding.Message ?? string.Empty).Trim());
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd('\r', '\n');
    }
}

internal readonly record struct ReviewCategorySummary(
    string CategoryName,
    int Count,
    string ColorHex);

internal readonly record struct ReviewResultsBatchActionState(
    bool CanToggleAll,
    bool CanExportToClipboard,
    bool CanCommentSelected);
