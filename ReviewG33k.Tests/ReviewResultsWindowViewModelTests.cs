// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Material.Icons;
using ReviewG33k.Services;
using ReviewG33k.Services.Checks;
using ReviewG33k.ViewModels;
using ReviewG33k.Views;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class ReviewResultsWindowViewModelTests
{
    [Test]
    public void PreviewFileNameWhenSetUpdatesCopyAvailabilityAndTooltip()
    {
        var viewModel = new ReviewResultsWindowViewModel();

        viewModel.PreviewFileName = "  Sample.cs  ";

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.PreviewFileName, Is.EqualTo("Sample.cs"));
            Assert.That(viewModel.CanCopyPreviewFileName, Is.True);
            Assert.That(viewModel.CopyPreviewFileNameToolTip, Is.EqualTo("Copy 'Sample.cs' to clipboard"));
        });

        viewModel.PreviewFileName = "   ";

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.PreviewFileName, Is.Null);
            Assert.That(viewModel.CanCopyPreviewFileName, Is.False);
            Assert.That(viewModel.CopyPreviewFileNameToolTip, Is.EqualTo("Copy file name to clipboard"));
        });
    }

    [Test]
    public void UpdateSummaryUsesFindingCountAndDistinctFileCount()
    {
        var viewModel = new ReviewResultsWindowViewModel();
        var stateService = new ReviewResultsStateService();
        var rows = new[]
        {
            CreateRow("src/A.cs", lineNumber: 5),
            CreateRow("src/A.cs", lineNumber: 14),
            CreateRow("src/B.cs", lineNumber: 2)
        };

        viewModel.ApplySummaryText(stateService.BuildSummaryText(rows));

        Assert.That(viewModel.SummaryText, Is.EqualTo("3 finding(s) across 2 file(s)"));

        viewModel.ApplySummaryText(stateService.BuildSummaryText([]));

        Assert.That(viewModel.SummaryText, Is.EqualTo("No review findings"));
    }

    [Test]
    public void UpdateBatchActionsReflectsIncludedAndCommentState()
    {
        var viewModel = new ReviewResultsWindowViewModel();
        var stateService = new ReviewResultsStateService();
        var commentableRow = CreateRow("src/A.cs", lineNumber: 5, canComment: true);
        var hiddenCommentRow = CreateRow("src/B.cs", lineNumber: 6, canComment: false);
        hiddenCommentRow.IsIncluded = false;

        viewModel.ApplyBatchActionState(stateService.BuildBatchActionState(
            [commentableRow, hiddenCommentRow],
            isBulkCommenting: false,
            canCommentInBitbucket: true));

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.CanToggleAll, Is.True);
            Assert.That(viewModel.CanExportToClipboard, Is.True);
            Assert.That(viewModel.CanCommentSelected, Is.True);
        });

        viewModel.ApplyBatchActionState(stateService.BuildBatchActionState(
            [commentableRow, hiddenCommentRow],
            isBulkCommenting: true,
            canCommentInBitbucket: true));
        Assert.That(viewModel.CanCommentSelected, Is.False);

        viewModel.ApplyBatchActionState(stateService.BuildBatchActionState(
            [commentableRow, hiddenCommentRow],
            isBulkCommenting: false,
            canCommentInBitbucket: false));
        Assert.That(viewModel.CanCommentSelected, Is.False);
    }

    [Test]
    public void OpenSelectionStateDependsOnSelectedRowAndTargetAvailability()
    {
        var viewModel = new ReviewResultsWindowViewModel();
        var openableRow = CreateRow("src/A.cs", lineNumber: 12);

        viewModel.SelectedRow = openableRow;
        viewModel.UpdateOpenTargetState("VS Code", MaterialIconKind.Web, isSelectedTargetAvailable: false);
        Assert.That(viewModel.CanOpenSelected, Is.False);

        viewModel.UpdateOpenTargetState("VS Code", MaterialIconKind.Web, isSelectedTargetAvailable: true);
        Assert.That(viewModel.CanOpenSelected, Is.True);

        openableRow.IsIncluded = false;
        viewModel.RefreshOpenSelectedState();
        Assert.That(viewModel.CanOpenSelected, Is.False);
    }

    [Test]
    public void UpdateOpenTargetStateUpdatesDisplayTextIconAndTooltip()
    {
        var viewModel = new ReviewResultsWindowViewModel();

        viewModel.UpdateOpenTargetState("  Rider  ", MaterialIconKind.Web, isSelectedTargetAvailable: true);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.OpenSelectedTargetText, Is.EqualTo("Open (Rider)"));
            Assert.That(viewModel.OpenSelectedTargetIconKind, Is.EqualTo(MaterialIconKind.Web));
            Assert.That(viewModel.OpenSelectedToolTip, Is.EqualTo("Open selected finding using Rider"));
        });
    }

    [Test]
    public void CommandsCanExecuteTransitionsFollowRowAndBatchState()
    {
        var viewModel = new ReviewResultsWindowViewModel();
        viewModel.ConfigureCommands(
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            () => { },
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => { },
            _ => { },
            _ => { });

        var actionableRow = new ReviewResultRow(
            new CodeSmellFinding(CodeReviewFindingSeverity.Important, "RULE-123", "src/File.cs", 7, "Issue"),
            canOpen: true,
            ReviewResultRow.ActionAvailability.Enabled,
            ReviewResultRow.ActionAvailability.Enabled,
            ReviewResultRow.ActionAvailability.Enabled);
        var missingRuleRow = new ReviewResultRow(
            new CodeSmellFinding(CodeReviewFindingSeverity.Important, "", "src/Other.cs", 4, "Other issue"),
            canOpen: true,
            ReviewResultRow.ActionAvailability.Hidden,
            ReviewResultRow.ActionAvailability.Hidden,
            ReviewResultRow.ActionAvailability.Hidden);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.FixFindingCommand.CanExecute(actionableRow), Is.True);
            Assert.That(viewModel.CommentFindingCommand.CanExecute(actionableRow), Is.True);
            Assert.That(viewModel.CreateCodexPromptCommand.CanExecute(actionableRow), Is.True);
            Assert.That(viewModel.TickSameTypeCommand.CanExecute(actionableRow), Is.True);
            Assert.That(viewModel.UntickSameTypeCommand.CanExecute(actionableRow), Is.True);
            Assert.That(viewModel.TickSameTypeCommand.CanExecute(missingRuleRow), Is.False);
            Assert.That(viewModel.UntickSameTypeCommand.CanExecute(missingRuleRow), Is.False);
            Assert.That(viewModel.FixFindingCommand.CanExecute(null), Is.False);
        });

        actionableRow.IsIncluded = false;
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.FixFindingCommand.CanExecute(actionableRow), Is.False);
            Assert.That(viewModel.CommentFindingCommand.CanExecute(actionableRow), Is.False);
            Assert.That(viewModel.CreateCodexPromptCommand.CanExecute(actionableRow), Is.False);
        });

        actionableRow.IsIncluded = true;
        viewModel.SelectedRow = actionableRow;
        viewModel.UpdateOpenTargetState("VS Code", MaterialIconKind.Web, isSelectedTargetAvailable: true);
        Assert.That(viewModel.OpenSelectedCommand.CanExecute(null), Is.True);

        viewModel.UpdateOpenTargetState("VS Code", MaterialIconKind.Web, isSelectedTargetAvailable: false);
        Assert.That(viewModel.OpenSelectedCommand.CanExecute(null), Is.False);

        viewModel.ApplyBatchActionState(new ReviewResultsBatchActionState(
            CanToggleAll: true,
            CanExportToClipboard: true,
            CanCommentSelected: true));
        viewModel.PreviewFileName = "File.cs";
        viewModel.SetRows([actionableRow]);
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.ToggleAllCommand.CanExecute(null), Is.True);
            Assert.That(viewModel.ExportToClipboardCommand.CanExecute(null), Is.True);
            Assert.That(viewModel.CommentSelectedCommand.CanExecute(null), Is.True);
            Assert.That(viewModel.CopyPreviewFileNameCommand.CanExecute(null), Is.True);
            Assert.That(viewModel.ShowCategoryBreakdownCommand.CanExecute(null), Is.True);
        });

        viewModel.ApplyBatchActionState(new ReviewResultsBatchActionState(
            CanToggleAll: false,
            CanExportToClipboard: false,
            CanCommentSelected: false));
        viewModel.PreviewFileName = null;
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.ToggleAllCommand.CanExecute(null), Is.False);
            Assert.That(viewModel.ExportToClipboardCommand.CanExecute(null), Is.False);
            Assert.That(viewModel.CommentSelectedCommand.CanExecute(null), Is.False);
            Assert.That(viewModel.CopyPreviewFileNameCommand.CanExecute(null), Is.False);
        });
    }

    [Test]
    public void SetRowsCreatesCategoryFiltersAndHidesRowsForUntickedCategories()
    {
        var viewModel = new ReviewResultsWindowViewModel();
        var threadingRow = CreateRow("src/A.cs", 5, CodeReviewRuleIds.AsyncVoid);
        var correctnessRow = CreateRow("src/B.cs", 9, CodeReviewRuleIds.EmptyCatch);

        viewModel.SetRows([threadingRow, correctnessRow]);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Rows.Count, Is.EqualTo(2));
            Assert.That(viewModel.CategoryFilters.Select(filter => filter.CategoryName),
                Is.EqualTo(new[]
                {
                    Services.Checks.Support.CodeReviewFindingCategoryResolver.Correctness,
                    Services.Checks.Support.CodeReviewFindingCategoryResolver.Threading
                }));
            Assert.That(viewModel.CanShowCategoryBreakdown, Is.True);
        });

        var threadingFilter = viewModel.CategoryFilters.Single(filter =>
            filter.CategoryName == Services.Checks.Support.CodeReviewFindingCategoryResolver.Threading);
        threadingFilter.IsVisible = false;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Rows.Count, Is.EqualTo(1));
            Assert.That(viewModel.Rows[0], Is.SameAs(correctnessRow));
            Assert.That(viewModel.SummaryText, Is.EqualTo("1 finding(s) across 1 file(s) (1 hidden)"));
            Assert.That(viewModel.CategoryBreakdownText, Does.Contain("1 hidden"));
        });
    }

    [Test]
    public void HidingSelectedCategoryMovesSelectionToNextVisibleRow()
    {
        var viewModel = new ReviewResultsWindowViewModel();
        var readabilityRow = CreateRow("src/A.cs", 5, CodeReviewRuleIds.ConsecutiveBooleanArguments);
        var testingRow = CreateRow("src/B.cs", 9, CodeReviewRuleIds.MissingTests);
        viewModel.SetRows([readabilityRow, testingRow]);
        viewModel.SelectedRow = readabilityRow;

        var readabilityFilter = viewModel.CategoryFilters.Single(filter =>
            filter.CategoryName == Services.Checks.Support.CodeReviewFindingCategoryResolver.Readability);
        readabilityFilter.IsVisible = false;

        Assert.That(viewModel.SelectedRow, Is.SameAs(testingRow));
    }

    private static ReviewResultRow CreateRow(
        string filePath,
        int lineNumber,
        string ruleId = "sample-rule",
        bool canComment = true)
    {
        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Important,
            ruleId,
            filePath,
            lineNumber,
            "Sample finding");

        return new ReviewResultRow(
            finding,
            canOpen: true,
            canComment
                ? ReviewResultRow.ActionAvailability.Enabled
                : ReviewResultRow.ActionAvailability.Hidden,
            ReviewResultRow.ActionAvailability.Hidden,
            ReviewResultRow.ActionAvailability.Hidden);
    }
}
