// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using ReviewG33k.Services;
using ReviewG33k.Services.Checks;
using ReviewG33k.Views;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class ReviewResultsStateServiceTests
{
    [Test]
    public void BuildSummaryTextWhenRowsAreEmptyReturnsNoFindingsMessage()
    {
        var service = new ReviewResultsStateService();

        var summaryText = service.BuildSummaryText([]);

        Assert.That(summaryText, Is.EqualTo("No review findings"));
    }

    [Test]
    public void BuildSummaryTextUsesDistinctFileCount()
    {
        var service = new ReviewResultsStateService();
        var rows = new[]
        {
            CreateRow("RuleA", "src/A.cs", 3),
            CreateRow("RuleB", "src/A.cs", 9),
            CreateRow("RuleC", "src/B.cs", 12)
        };

        var summaryText = service.BuildSummaryText(rows);

        Assert.That(summaryText, Is.EqualTo("3 finding(s) across 2 file(s)"));
    }

    [Test]
    public void BuildSummaryTextWhenRowsAreFilteredIncludesHiddenCount()
    {
        var service = new ReviewResultsStateService();
        var visibleRows = new[]
        {
            CreateRow("RuleA", "src/A.cs", 3)
        };

        var summaryText = service.BuildSummaryText(visibleRows, totalRowCount: 4);

        Assert.That(summaryText, Is.EqualTo("1 finding(s) across 1 file(s) (3 hidden)"));
    }

    [Test]
    public void BuildBatchActionStateReflectsCommentingAndSelection()
    {
        var service = new ReviewResultsStateService();
        var enabledRow = CreateRow("RuleA", "src/A.cs", 3, commentAvailability: ReviewResultRow.ActionAvailability.Enabled);
        var hiddenCommentRow = CreateRow("RuleB", "src/B.cs", 4, commentAvailability: ReviewResultRow.ActionAvailability.Hidden);
        hiddenCommentRow.IsIncluded = false;

        var enabledState = service.BuildBatchActionState(
            [enabledRow, hiddenCommentRow],
            isBulkCommenting: false,
            canCommentInBitbucket: true);

        Assert.Multiple(() =>
        {
            Assert.That(enabledState.CanToggleAll, Is.True);
            Assert.That(enabledState.CanExportToClipboard, Is.True);
            Assert.That(enabledState.CanCommentSelected, Is.True);
        });

        var busyState = service.BuildBatchActionState(
            [enabledRow, hiddenCommentRow],
            isBulkCommenting: true,
            canCommentInBitbucket: true);
        Assert.That(busyState.CanCommentSelected, Is.False);

        var unavailableState = service.BuildBatchActionState(
            [enabledRow, hiddenCommentRow],
            isBulkCommenting: false,
            canCommentInBitbucket: false);
        Assert.That(unavailableState.CanCommentSelected, Is.False);
    }

    [Test]
    public void ToggleAllIncludedFlipsToMajorityTargetState()
    {
        var service = new ReviewResultsStateService();
        var row1 = CreateRow("RuleA", "src/A.cs", 1);
        var row2 = CreateRow("RuleB", "src/B.cs", 2);
        var row3 = CreateRow("RuleC", "src/C.cs", 3);

        row2.IsIncluded = false;
        row3.IsIncluded = false;
        service.ToggleAllIncluded([row1, row2, row3]);
        Assert.That(new[] { row1.IsIncluded, row2.IsIncluded, row3.IsIncluded }, Is.EqualTo(new[] { true, true, true }));

        service.ToggleAllIncluded([row1, row2, row3]);
        Assert.That(new[] { row1.IsIncluded, row2.IsIncluded, row3.IsIncluded }, Is.EqualTo(new[] { false, false, false }));
    }

    [Test]
    public void SetSameTypeIncludedStateMatchesRuleIdIgnoringCase()
    {
        var service = new ReviewResultsStateService();
        var rowA = CreateRow("RuleA", "src/A.cs", 1);
        var rowB = CreateRow("rulea", "src/B.cs", 2);
        var rowC = CreateRow("RuleB", "src/C.cs", 3);

        service.SetSameTypeIncludedState([rowA, rowB, rowC], "RULEA", isIncluded: false);

        Assert.Multiple(() =>
        {
            Assert.That(rowA.IsIncluded, Is.False);
            Assert.That(rowB.IsIncluded, Is.False);
            Assert.That(rowC.IsIncluded, Is.True);
        });
    }

    [Test]
    public void SetSameFileIncludedStateMatchesFilePathIgnoringCase()
    {
        var service = new ReviewResultsStateService();
        var rowA = CreateRow("RuleA", "src/A.cs", 1);
        var rowB = CreateRow("RuleB", "SRC/A.cs", 2);
        var rowC = CreateRow("RuleC", "src/C.cs", 3);

        service.SetSameFileIncludedState([rowA, rowB, rowC], "src/a.cs", isIncluded: false);

        Assert.Multiple(() =>
        {
            Assert.That(rowA.IsIncluded, Is.False);
            Assert.That(rowB.IsIncluded, Is.False);
            Assert.That(rowC.IsIncluded, Is.True);
        });
    }

    [Test]
    public void SetAllInFileIncludedStateMatchesFilePathIgnoringCase()
    {
        var service = new ReviewResultsStateService();
        var rowA = CreateRow("RuleA", "src/A.cs", 1);
        var rowB = CreateRow("RuleB", "SRC/A.cs", 2);
        var rowC = CreateRow("RuleC", "src/C.cs", 3);
        rowA.IsIncluded = false;
        rowB.IsIncluded = false;
        rowC.IsIncluded = false;

        service.SetAllInFileIncludedState([rowA, rowB, rowC], "src/a.cs");

        Assert.Multiple(() =>
        {
            Assert.That(rowA.IsIncluded, Is.True);
            Assert.That(rowB.IsIncluded, Is.True);
            Assert.That(rowC.IsIncluded, Is.False);
        });
    }

    [Test]
    public void BuildExportTextReturnsExpectedCountAndOutput()
    {
        var service = new ReviewResultsStateService();
        var includedRow = CreateRow("RuleA", "src/A.cs", 10, message: "First issue");
        var excludedRow = CreateRow("RuleB", "src/B.cs", 20, message: "Second issue");
        excludedRow.IsIncluded = false;

        var exportText = service.BuildExportText([includedRow, excludedRow], out var exportedCount);

        Assert.Multiple(() =>
        {
            Assert.That(exportedCount, Is.EqualTo(1));
            Assert.That(exportText, Does.Contain("ReviewG33k Findings"));
            Assert.That(exportText, Does.Contain("Count: 1"));
            Assert.That(exportText, Does.Contain($"[{includedRow.CategoryText}] src/A.cs:10"));
            Assert.That(exportText, Does.Contain("First issue"));
            Assert.That(exportText, Does.Not.Contain("Second issue"));
        });
    }

    [Test]
    public void BuildExportTextWhenNoIncludedRowsReturnsEmpty()
    {
        var service = new ReviewResultsStateService();
        var row = CreateRow("RuleA", "src/A.cs", 10);
        row.IsIncluded = false;

        var exportText = service.BuildExportText([row], out var exportedCount);

        Assert.Multiple(() =>
        {
            Assert.That(exportedCount, Is.EqualTo(0));
            Assert.That(exportText, Is.EqualTo(string.Empty));
        });
    }

    [Test]
    public void BuildCategorySummariesReturnsOrderedCategoryCounts()
    {
        var service = new ReviewResultsStateService();
        var rows = new[]
        {
            CreateRow(CodeReviewRuleIds.ConsecutiveBooleanArguments, "src/A.cs", 3),
            CreateRow(CodeReviewRuleIds.EmptyCatch, "src/B.cs", 4),
            CreateRow(CodeReviewRuleIds.EmptyCatch, "src/C.cs", 5)
        };

        var summaries = service.BuildCategorySummaries(rows);

        Assert.Multiple(() =>
        {
            Assert.That(summaries.Select(summary => summary.CategoryName),
                Is.EqualTo(new[] { "Correctness", "Readability" }));
            Assert.That(summaries[0].Count, Is.EqualTo(2));
            Assert.That(summaries[1].Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void FilterRowsByVisibleCategoriesReturnsOnlyMatchingRows()
    {
        var service = new ReviewResultsStateService();
        var correctnessRow = CreateRow(CodeReviewRuleIds.EmptyCatch, "src/A.cs", 3);
        var testingRow = CreateRow(CodeReviewRuleIds.MissingTests, "src/B.cs", 4);

        var filteredRows = service.FilterRowsByVisibleCategories(
            [correctnessRow, testingRow],
            ["Correctness"]);

        Assert.That(filteredRows, Is.EqualTo(new[] { correctnessRow }));
    }

    private static ReviewResultRow CreateRow(
        string ruleId,
        string filePath,
        int lineNumber,
        string message = "Sample finding",
        ReviewResultRow.ActionAvailability commentAvailability = ReviewResultRow.ActionAvailability.Enabled)
    {
        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Important,
            ruleId,
            filePath,
            lineNumber,
            message);

        return new ReviewResultRow(
            finding,
            canOpen: true,
            commentAvailability,
            ReviewResultRow.ActionAvailability.Hidden,
            ReviewResultRow.ActionAvailability.Hidden);
    }
}
