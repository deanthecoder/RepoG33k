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
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using DTC.Core.Commands;
using Material.Icons;
using ReviewG33k.Services;
using ReviewG33k.Services.Checks;
using ReviewG33k.Services.Checks.Support;
using ReviewG33k.ViewModels;

namespace ReviewG33k.Views;

public partial class ReviewResultsWindow : Window
{
    private const string BaseWindowTitle = "Review Results";
    private const int PreviewLinesBefore = 4;
    private const int PreviewLinesAfter = 24;
    private const int CodexPromptLinesBefore = 8;
    private const int CodexPromptLinesAfter = 14;

    private readonly Action<CodeSmellFinding> m_openFindingAction;
    private readonly Func<CodeSmellFinding, CodeLocationOpenTarget, Task<(bool Success, string Message)>> m_openFindingWithTargetAction;
    private readonly Func<CodeLocationOpenTarget, bool> m_isOpenTargetAvailable;
    private readonly Action<CodeLocationOpenTarget> m_openTargetChangedAction;
    private readonly Func<CodeSmellFinding, Task<bool>> m_commentFindingAction;
    private readonly Func<CodeSmellFinding, string> m_resolveFindingPath;
    private readonly Func<CodeSmellFinding, string> m_readSnapshotText;
    private readonly Func<string, Task<IReadOnlyList<CodeSmellFinding>>> m_resampleFileFindingsAction;
    private readonly ICodeReviewFindingFixer m_findingFixer;
    private readonly ReviewResultsStateService m_stateService = new();
    private readonly ReviewResultsFileContextService m_fileContextService = new();
    private readonly List<ReviewResultRow> m_rows;
    private readonly ReviewFindingSelection m_findingSelection;
    private readonly bool m_canOpenInVsCode;
    private readonly bool m_canCommentInBitbucket;
    private readonly bool m_canFixLocally;
    private readonly bool m_hasPathResolver;
    private readonly ReviewResultsWindowViewModel m_viewModel = new();
    private readonly IReadOnlyList<CodeLocationOpenTargetDefinition> m_openTargetDefinitions;
    private readonly IReadOnlyDictionary<CodeLocationOpenTarget, CodeLocationOpenTargetDefinition> m_openTargetDefinitionsByTarget;
    private readonly Dictionary<CodeLocationOpenTarget, MenuItem> m_openTargetMenuItems = [];
    private readonly CommandBase m_selectOpenTargetCommandImpl;
    private readonly CommandBase m_tickSameFileCommandImpl;
    private readonly CommandBase m_untickSameFileCommandImpl;
    private CodeLocationOpenTarget m_selectedOpenTarget;
    private ReviewCategoryBreakdownWindow m_categoryBreakdownWindow;
    private Cursor m_previousCursor;
    private bool m_isBulkCommenting;
    private bool m_isApplyingFix;
    private string m_previewFileName;

    public ICommand ToggleIgnoredCommand { get; }
    public ICommand OpenTargetMenuCommand { get; }
    public ICommand FixFindingCommand => m_viewModel.FixFindingCommand;
    public ICommand CreateCodexPromptCommand => m_viewModel.CreateCodexPromptCommand;
    public ICommand CommentFindingCommand => m_viewModel.CommentFindingCommand;
    public ICommand TickSameTypeCommand => m_viewModel.TickSameTypeCommand;
    public ICommand UntickSameTypeCommand => m_viewModel.UntickSameTypeCommand;
    public ICommand TickSameFileCommand => m_tickSameFileCommandImpl;
    public ICommand UntickSameFileCommand => m_untickSameFileCommandImpl;

    public ReviewResultsWindow(
        IEnumerable<CodeSmellFinding> findings,
        bool canOpenInVsCode,
        bool canCommentInBitbucket,
        bool canFixLocally,
        ICodeReviewFindingFixer findingFixer,
        Action<CodeSmellFinding> openFindingAction,
        Func<CodeSmellFinding, CodeLocationOpenTarget, Task<(bool Success, string Message)>> openFindingWithTargetAction,
        Func<CodeLocationOpenTarget, bool> isOpenTargetAvailable,
        Action<CodeLocationOpenTarget> openTargetChangedAction,
        CodeLocationOpenTarget initialOpenTarget,
        IReadOnlyList<CodeLocationOpenTargetDefinition> openTargetDefinitions,
        Func<CodeSmellFinding, Task<bool>> commentFindingAction,
        Func<CodeSmellFinding, string> resolveFindingPath,
        Func<string, Task<IReadOnlyList<CodeSmellFinding>>> resampleFileFindingsAction,
        string pullRequestTitle = null,
        Action<Window, IReadOnlyList<CodeSmellFinding>> openDiffLens = null,
        Func<CodeSmellFinding, string> readSnapshotText = null,
        ReviewFindingSelection findingSelection = null)
    {
        m_findingSelection = findingSelection;
        ToggleIgnoredCommand = new RelayCommand(parameter =>
        {
            if (parameter is ReviewResultRow row)
                row.IsIgnored = !row.IsIgnored;
        });
        m_canOpenInVsCode = canOpenInVsCode;
        m_canCommentInBitbucket = canCommentInBitbucket;
        m_canFixLocally = canFixLocally;
        m_hasPathResolver = resolveFindingPath != null;
        m_openFindingAction = openFindingAction;
        m_openFindingWithTargetAction = openFindingWithTargetAction;
        m_isOpenTargetAvailable = isOpenTargetAvailable;
        m_openTargetChangedAction = openTargetChangedAction;
        m_selectedOpenTarget = initialOpenTarget;
        m_openTargetDefinitions = (openTargetDefinitions ?? []).Where(definition => definition != null).ToArray();
        m_openTargetDefinitionsByTarget = m_openTargetDefinitions.ToDictionary(definition => definition.Target);
        m_commentFindingAction = commentFindingAction;
        m_resolveFindingPath = resolveFindingPath;
        m_readSnapshotText = readSnapshotText;
        m_resampleFileFindingsAction = resampleFileFindingsAction;
        m_findingFixer = findingFixer;
        CommandBase openTargetMenuCommandImpl = new RelayCommand(_ => ShowOpenTargetMenu());
        m_selectOpenTargetCommandImpl = new RelayCommand(
            parameter =>
            {
                if (parameter is CodeLocationOpenTarget target)
                    SelectOpenTarget(target);
            },
            parameter => parameter is CodeLocationOpenTarget);
        m_tickSameFileCommandImpl = new RelayCommand(
            parameter => SetSameFileIncludedState(parameter as ReviewResultRow, isIncluded: true),
            parameter => parameter is ReviewResultRow row && !string.IsNullOrWhiteSpace(row.Finding?.FilePath));
        m_untickSameFileCommandImpl = new RelayCommand(
            parameter => SetSameFileIncludedState(parameter as ReviewResultRow, isIncluded: false),
            parameter => parameter is ReviewResultRow row && !string.IsNullOrWhiteSpace(row.Finding?.FilePath));
        OpenTargetMenuCommand = openTargetMenuCommandImpl;
        m_viewModel.ConfigureCommands(
            FixFindingAsync,
            CreateCodexPromptAsync,
            CommentFindingAsync,
            ToggleAllIncluded,
            CommentSelectedAsync,
            ExportToClipboardAsync,
            CopyPreviewFileNameAsync,
            OpenSelectedAsync,
            ShowCategoryBreakdown,
            row => SetSameTypeIncludedState(row, isIncluded: true),
            row => SetSameTypeIncludedState(row, isIncluded: false));
        InitializeComponent();
        DataContext = m_viewModel;
        m_viewModel.PropertyChanged += ViewModel_OnPropertyChanged;
        m_viewModel.VisibleRowsChanged += ViewModel_OnVisibleRowsChanged;
        InitializeOpenTargetMenuItems();
        if (m_openTargetDefinitions.Count > 0 && !m_openTargetDefinitionsByTarget.ContainsKey(m_selectedOpenTarget))
            m_selectedOpenTarget = m_openTargetDefinitions[0].Target;
        Title = BuildWindowTitle(pullRequestTitle);
        CommentSelectedButton.IsVisible = canCommentInBitbucket;

        m_rows = (findings ?? [])
            .Where(finding => finding != null)
            .Where(finding => finding.Severity != CodeReviewFindingSeverity.Ok)
            .OrderBy(finding => m_stateService.GetCategorySortOrder(CodeReviewFindingCategoryResolver.ResolveCategory(finding.RuleId)))
            .ThenBy(finding => finding.RuleId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(finding => finding.FilePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(finding => finding.LineNumber)
            .Select(finding => MapToRow(finding, canOpenInVsCode, canCommentInBitbucket, canFixLocally, resolveFindingPath != null, findingFixer))
            .ToList();

        foreach (var row in m_rows)
        {
            row.CanIgnore = findingSelection != null;
            row.IsIgnored = findingSelection?.IsIgnored(row.Finding) == true;
            row.IsIncluded = findingSelection?.IsSelected(row.Finding) ?? true;
            row.PropertyChanged += ReviewResultRow_OnPropertyChanged;
        }

        DiffLensResultsButton.IsVisible = openDiffLens != null;
        DiffLensResultsButton.Click += (_, _) => openDiffLens?.Invoke(this, m_rows.Where(row => row.IsIncluded).Select(row => row.Finding).ToArray());
        m_viewModel.SetRows(m_rows);
        m_viewModel.SelectedRow = m_rows.FirstOrDefault();
        if (m_viewModel.SelectedRow == null)
            SetPreviewText("Select an issue to preview surrounding file content.", "Preview");

        UpdateBatchActionButtonStates();
        UpdateOpenTargetUi();
    }

    private static string BuildWindowTitle(string pullRequestTitle) =>
        string.IsNullOrWhiteSpace(pullRequestTitle)
            ? BaseWindowTitle
            : $"{BaseWindowTitle} - {pullRequestTitle.Trim()}";

    private async Task CommentFindingAsync(ReviewResultRow row)
    {
        if (row == null || !row.CanCommentActive || m_commentFindingAction == null)
            return;

        row.IsPostingComment = true;
        try
        {
            var success = await m_commentFindingAction(row.Finding);
            if (success)
                row.HasPostedComment = true;
        }
        catch (Exception exception)
        {
            SetPreviewText($"Failed to post comment: {exception.Message}", "Preview");
        }
        finally
        {
            row.IsPostingComment = false;
        }
    }

    private async Task FixFindingAsync(ReviewResultRow row)
    {
        if (m_isApplyingFix || row == null || !row.CanFixActive || row.Finding == null)
            return;

        if (!m_fileContextService.TryResolveFindingFile(row.Finding, m_resolveFindingPath, out var resolvedFile))
        {
            SetPreviewText("Could not resolve file path for fix.", "Preview");
            return;
        }

        m_isApplyingFix = true;
        SetFixUiBusy(true);
        row.IsFixing = true;
        try
        {
            if (m_findingFixer == null)
            {
                SetPreviewText("No fixer is available in this mode.", "Preview");
                return;
            }

            if (!m_findingFixer.TryFix(row.Finding, resolvedFile, out var fixMessage))
            {
                SetPreviewText(fixMessage ?? "Fix failed.", "Preview");
                return;
            }

            SetPreviewText(fixMessage ?? "Fixed.", "Preview");

            if (!string.IsNullOrWhiteSpace(row.Finding.FilePath) && m_resampleFileFindingsAction != null)
            {
                var previousRowIndex = m_rows.IndexOf(row);
                await RefreshFindingsForFileAsync(row.Finding.FilePath, row.Finding.LineNumber, previousRowIndex);
            }
            else
            {
                row.HasBeenFixed = true;
                row.IsIncluded = false;
                if (m_viewModel.SelectedRow is { } selectedRow &&
                    ReferenceEquals(selectedRow, row))
                {
                    UpdatePreviewForFinding(row.Finding);
                }
            }
        }
        catch (Exception exception)
        {
            SetPreviewText($"Fix failed: {exception.Message}", "Preview");
        }
        finally
        {
            row.IsFixing = false;
            m_isApplyingFix = false;
            SetFixUiBusy(false);
        }
    }

    private void SetFixUiBusy(bool isBusy)
    {
        if (isBusy)
        {
            m_previousCursor = Cursor;
            Cursor = new Cursor(StandardCursorType.Wait);
        }
        else
        {
            Cursor = m_previousCursor;
            m_previousCursor = null;
        }

        if (WindowContentGrid != null)
            WindowContentGrid.IsEnabled = !isBusy;
    }

    private async Task CreateCodexPromptAsync(ReviewResultRow row)
    {
        if (row == null || !row.CanCodexPromptActive)
            return;

        var clipboard = Clipboard;
        if (clipboard == null)
        {
            SetPreviewText("Clipboard is unavailable in this window.", "Preview");
            return;
        }

        if (!m_fileContextService.TryBuildCodexPrompt(
                row.Finding,
                m_resolveFindingPath,
                CodexPromptLinesBefore,
                CodexPromptLinesAfter,
                out var promptText,
                out var failureReason))
        {
            SetPreviewText(failureReason ?? "Could not create Codex prompt.", "Preview");
            return;
        }

        try
        {
            await clipboard.SetTextAsync(promptText);
            SetPreviewText("Copied Codex prompt to clipboard.", "Preview");
        }
        catch (Exception exception)
        {
            SetPreviewText($"Failed to copy Codex prompt: {exception.Message}", "Preview");
        }
    }

    private async Task CopyPreviewFileNameAsync()
    {
        if (string.IsNullOrWhiteSpace(m_previewFileName))
            return;

        var clipboard = Clipboard;
        if (clipboard == null)
        {
            SetPreviewText("Clipboard is unavailable in this window.", "Preview", m_previewFileName);
            return;
        }

        try
        {
            await clipboard.SetTextAsync(m_previewFileName);
        }
        catch (Exception exception)
        {
            SetPreviewText($"Failed to copy file name: {exception.Message}", $"Preview: {m_previewFileName}", m_previewFileName);
        }
    }

    private void ToggleAllIncluded()
    {
        m_stateService.ToggleAllIncluded(m_viewModel.GetVisibleRows().ToList());
        UpdateBatchActionButtonStates();
    }

    private async Task CommentSelectedAsync()
    {
        if (m_isBulkCommenting || m_commentFindingAction == null)
            return;

        var rowsToComment = m_viewModel.GetVisibleRows()
            .Where(row => row.IsIncluded && row.CanCommentActive)
            .ToArray();
        if (rowsToComment.Length == 0)
            return;

        m_isBulkCommenting = true;
        UpdateBatchActionButtonStates();

        var successCount = 0;
        var failureCount = 0;
        foreach (var row in rowsToComment)
        {
            row.IsPostingComment = true;
            try
            {
                var success = await m_commentFindingAction(row.Finding);
                if (success)
                {
                    row.HasPostedComment = true;
                    successCount++;
                }
                else
                {
                    failureCount++;
                }
            }
            catch (Exception exception)
            {
                failureCount++;
                SetPreviewText($"Failed to post comment: {exception.Message}", "Preview");
            }
            finally
            {
                row.IsPostingComment = false;
            }
        }

        m_isBulkCommenting = false;
        UpdateBatchActionButtonStates();

        if (failureCount == 0)
            SetPreviewText($"Posted {successCount} comment(s).", "Preview");
        else
            SetPreviewText($"Posted {successCount} comment(s). {failureCount} failed. See log for details.", "Preview");
    }

    private async Task ExportToClipboardAsync()
    {
        var clipboard = Clipboard;
        if (clipboard == null)
        {
            SetPreviewText("Clipboard is unavailable in this window.", "Preview");
            return;
        }

        var exportText = m_stateService.BuildExportText(m_viewModel.GetVisibleRows(), out var exportedCount);
        if (exportedCount == 0)
        {
            SetPreviewText("No included findings available to export.", "Preview");
            return;
        }

        try
        {
            await clipboard.SetTextAsync(exportText);
            SetPreviewText($"Copied {exportedCount} included finding(s) to the clipboard.", "Preview");
        }
        catch (Exception exception)
        {
            SetPreviewText($"Failed to copy to clipboard: {exception.Message}", "Preview");
        }
    }

    private void SetSameTypeIncludedState(ReviewResultRow sourceRow, bool isIncluded)
    {
        if (sourceRow == null || string.IsNullOrWhiteSpace(sourceRow.RuleId))
            return;

        if (m_findingSelection != null)
        {
            foreach (var row in m_viewModel.GetVisibleRows().Where(row => string.Equals(row.RuleId, sourceRow.RuleId, StringComparison.OrdinalIgnoreCase)))
                row.IsIgnored = !isIncluded;
        }
        else
        {
            m_stateService.SetSameTypeIncludedState(m_viewModel.GetVisibleRows(), sourceRow.RuleId, isIncluded);
        }

        UpdateBatchActionButtonStates();
    }

    private void SetSameFileIncludedState(ReviewResultRow sourceRow, bool isIncluded)
    {
        if (sourceRow == null || string.IsNullOrWhiteSpace(sourceRow.Finding?.FilePath))
            return;

        if (m_findingSelection != null)
        {
            foreach (var row in m_rows.Where(row => string.Equals(row.Finding?.FilePath, sourceRow.Finding.FilePath, StringComparison.OrdinalIgnoreCase)))
                row.IsIgnored = !isIncluded;
        }
        else
            m_stateService.SetSameFileIncludedState(m_rows, sourceRow.Finding.FilePath, isIncluded);
        UpdateBatchActionButtonStates();
    }

    private async Task OpenSelectedAsync()
    {
        if (m_viewModel.SelectedRow is not { } row || !row.CanOpenActive)
            return;

        if (m_openFindingWithTargetAction != null)
        {
            var result = await m_openFindingWithTargetAction(row.Finding, m_selectedOpenTarget);
            if (!result.Success && !string.IsNullOrWhiteSpace(result.Message))
                SetPreviewText(result.Message, "Preview");
            return;
        }

        m_openFindingAction?.Invoke(row.Finding);
    }

    private void ShowOpenTargetMenu()
    {
        if (OpenTargetMenu == null || OpenTargetMenuButton == null)
            return;

        OpenTargetMenu.PlacementTarget = OpenTargetMenuButton;
        OpenTargetMenu.Open();
    }

    private ReviewResultRow MapToRow(CodeSmellFinding finding) =>
        MapToRow(
            finding,
            m_canOpenInVsCode,
            m_canCommentInBitbucket,
            m_canFixLocally,
            m_hasPathResolver,
            m_findingFixer);

    private static ReviewResultRow MapToRow(
        CodeSmellFinding finding,
        bool canOpenInVsCode,
        bool canCommentInBitbucket,
        bool canFixLocally,
        bool hasPathResolver,
        ICodeReviewFindingFixer findingFixer)
    {
        var hasFileAndLine = !string.IsNullOrWhiteSpace(finding.FilePath) && finding.LineNumber > 0;
        var canOpen = canOpenInVsCode && hasFileAndLine;
        var canComment = canCommentInBitbucket && hasFileAndLine;
        var canFix = canFixLocally && hasPathResolver && hasFileAndLine && findingFixer != null && findingFixer.CanFix(finding);
        var canCreateCodexPrompt = canFixLocally &&
                                   hasPathResolver &&
                                   hasFileAndLine &&
                                   !canFix &&
                                   CanUseCodexPromptForFinding(finding);
        var commentAvailability = canCommentInBitbucket
            ? canComment
                ? ReviewResultRow.ActionAvailability.Enabled
                : ReviewResultRow.ActionAvailability.Disabled
            : ReviewResultRow.ActionAvailability.Hidden;
        var fixAvailability = canFix
            ? ReviewResultRow.ActionAvailability.Enabled
            : ReviewResultRow.ActionAvailability.Hidden;
        var codexPromptAvailability = canCreateCodexPrompt
            ? ReviewResultRow.ActionAvailability.Enabled
            : ReviewResultRow.ActionAvailability.Hidden;

        return new ReviewResultRow(
            finding,
            canOpen,
            commentAvailability,
            fixAvailability,
            codexPromptAvailability);
    }

    private static bool CanUseCodexPromptForFinding(CodeSmellFinding finding)
    {
        var ruleId = finding?.RuleId;
        return !string.Equals(ruleId, CodeReviewRuleIds.EmptyCatch, StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(ruleId, CodeReviewRuleIds.SwallowingCatch, StringComparison.OrdinalIgnoreCase);
    }

    private void ReviewResultRow_OnPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (sender is ReviewResultRow row && m_findingSelection != null)
        {
            if (e.PropertyName == nameof(ReviewResultRow.IsIgnored))
                m_findingSelection.SetIgnored(row.Finding, row.IsIgnored);
            else if (e.PropertyName == nameof(ReviewResultRow.IsIncluded))
                m_findingSelection.SetSelected(row.Finding, row.IsIncluded);
        }
        if (e.PropertyName is nameof(ReviewResultRow.IsIncluded) or nameof(ReviewResultRow.IsPostingComment) or nameof(ReviewResultRow.HasPostedComment) or nameof(ReviewResultRow.IsFixing) or nameof(ReviewResultRow.HasBeenFixed))
        {
            m_viewModel.RefreshOpenSelectedState();
            UpdateBatchActionButtonStates();
        }
    }

    private async Task RefreshFindingsForFileAsync(string filePath, int preferredLineNumber, int preferredRowIndex = -1)
    {
        if (m_resampleFileFindingsAction == null || string.IsNullOrWhiteSpace(filePath))
            return;

        IReadOnlyList<CodeSmellFinding> refreshedFindings;
        try
        {
            refreshedFindings = await m_resampleFileFindingsAction(filePath) ?? [];
        }
        catch (Exception exception)
        {
            SetPreviewText($"Fixed, but failed to refresh findings: {exception.Message}", "Preview");
            return;
        }

        var rowsToRemove = m_rows
            .Where(row => RepositoryUtilities.AreSameRepoPath(row.Finding?.FilePath, filePath))
            .ToArray();
        foreach (var row in rowsToRemove)
        {
            row.PropertyChanged -= ReviewResultRow_OnPropertyChanged;
            m_rows.Remove(row);
        }

        var refreshedRows = refreshedFindings
            .Where(finding => finding != null)
            .Where(finding => finding.Severity != CodeReviewFindingSeverity.Ok)
            .Where(finding => RepositoryUtilities.AreSameRepoPath(finding.FilePath, filePath))
            .Select(MapToRow)
            .ToArray();
        foreach (var row in refreshedRows)
        {
            row.PropertyChanged += ReviewResultRow_OnPropertyChanged;
            m_rows.Add(row);
        }

        m_rows.Sort(m_stateService.CompareRows);
        m_viewModel.SetRows(m_rows);
        UpdateBatchActionButtonStates();

        if (m_viewModel.Rows.Count == 0)
        {
            m_viewModel.SelectedRow = null;
            SetPreviewText("No review findings.", "Preview");
            return;
        }

        ReviewResultRow nextRow = null;
        if (preferredRowIndex >= 0)
        {
            var boundedIndex = Math.Clamp(preferredRowIndex, 0, m_viewModel.Rows.Count - 1);
            nextRow = m_viewModel.Rows[boundedIndex];
        }

        nextRow ??= m_viewModel.Rows
            .Where(row => RepositoryUtilities.AreSameRepoPath(row.Finding?.FilePath, filePath))
            .OrderBy(row => Math.Abs((row.Finding?.LineNumber ?? 0) - preferredLineNumber))
            .ThenBy(row => row.Finding?.LineNumber ?? int.MaxValue)
            .FirstOrDefault() ?? m_viewModel.Rows[0];

        m_viewModel.SelectedRow = nextRow;
    }

    private void UpdatePreviewForFinding(CodeSmellFinding finding)
    {
        if (!m_fileContextService.TryBuildPreview(
                finding,
                m_resolveFindingPath,
                PreviewLinesBefore,
                PreviewLinesAfter,
                out var previewData,
                out var failureReason,
                m_readSnapshotText))
        {
            SetPreviewText(failureReason ?? "Could not build preview.", "Preview");
            return;
        }

        SetPreviewText(previewData.Text, previewData.Header, previewData.PreviewFileName);
    }

    private void SetPreviewText(string text, string header, string previewFileName = null)
    {
        m_previewFileName = string.IsNullOrWhiteSpace(previewFileName) ? null : previewFileName.Trim();
        m_viewModel.PreviewFileName = m_previewFileName;
        m_viewModel.PreviewHeaderText = header ?? "Preview";
        m_viewModel.PreviewText = text ?? string.Empty;
        PreviewTextBox.CaretIndex = 0;
        Dispatcher.UIThread.Post(() =>
        {
            if (!string.IsNullOrEmpty(m_viewModel.PreviewText))
                PreviewTextBox.ScrollToLine(0);
        });
    }

    private void UpdateBatchActionButtonStates()
    {
        var actionState = m_stateService.BuildBatchActionState(
            m_viewModel.GetVisibleRows(),
            m_isBulkCommenting,
            m_commentFindingAction != null);
        m_viewModel.ApplyBatchActionState(actionState);
    }

    private void UpdateOpenSelectedButtonState()
    {
        m_viewModel.RefreshOpenSelectedState();
    }

    private bool IsOpenTargetAvailable(CodeLocationOpenTarget target) =>
        m_isOpenTargetAvailable?.Invoke(target) ?? true;

    private void SelectOpenTarget(CodeLocationOpenTarget target)
    {
        m_selectedOpenTarget = target;
        m_openTargetChangedAction?.Invoke(target);
        UpdateOpenTargetUi();
    }

    private void UpdateOpenTargetUi()
    {
        foreach (var definition in m_openTargetDefinitions)
        {
            if (m_openTargetMenuItems.TryGetValue(definition.Target, out var menuItem))
                UpdateOpenTargetMenuItem(menuItem, definition);
        }

        var selectedTargetDefinition = GetOpenTargetDefinition(m_selectedOpenTarget);
        var selectedTargetName = selectedTargetDefinition?.DisplayName ?? m_selectedOpenTarget.ToString();
        var selectedTargetIcon = selectedTargetDefinition?.IconKind ?? MaterialIconKind.CodeTags;
        m_viewModel.UpdateOpenTargetState(
            selectedTargetName,
            selectedTargetIcon,
            IsOpenTargetAvailable(m_selectedOpenTarget));
    }

    private void ViewModel_OnPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ReviewResultsWindowViewModel.SelectedRow))
            return;

        UpdateOpenSelectedButtonState();

        if (m_viewModel.SelectedRow?.Finding != null)
        {
            UpdatePreviewForFinding(m_viewModel.SelectedRow.Finding);
            return;
        }

        SetPreviewText("Select an issue to preview surrounding file content.", "Preview");
    }

    private void ViewModel_OnVisibleRowsChanged(object sender, EventArgs e)
    {
        UpdateBatchActionButtonStates();

        if (m_viewModel.SelectedRow != null)
            return;

        SetPreviewText("Select an issue to preview surrounding file content.", "Preview");
    }

    private void UpdateOpenTargetMenuItem(MenuItem menuItem, CodeLocationOpenTargetDefinition definition)
    {
        if (menuItem == null || definition == null)
            return;

        var isAvailable = IsOpenTargetAvailable(definition.Target);
        var isSelected = definition.Target == m_selectedOpenTarget;
        menuItem.Header = isSelected
            ? $"✓ {definition.DisplayName}"
            : definition.DisplayName;
        menuItem.IsEnabled = isAvailable;
    }

    private void InitializeOpenTargetMenuItems()
    {
        if (OpenTargetMenu == null)
            return;

        var menuItems = new List<MenuItem>();
        foreach (var definition in m_openTargetDefinitions)
        {
            var menuItem = new MenuItem
            {
                Tag = definition.Target,
                Command = m_selectOpenTargetCommandImpl,
                CommandParameter = definition.Target
            };
            m_openTargetMenuItems[definition.Target] = menuItem;
            menuItems.Add(menuItem);
        }

        OpenTargetMenu.ItemsSource = menuItems;
    }

    private CodeLocationOpenTargetDefinition GetOpenTargetDefinition(CodeLocationOpenTarget target)
    {
        return m_openTargetDefinitionsByTarget.TryGetValue(target, out var definition)
            ? definition
            : null;
    }

    private void ShowCategoryBreakdown()
    {
        if (!m_viewModel.CanShowCategoryBreakdown)
            return;

        if (m_categoryBreakdownWindow != null)
        {
            m_categoryBreakdownWindow.Activate();
            return;
        }

        m_categoryBreakdownWindow = new ReviewCategoryBreakdownWindow(m_viewModel);
        m_categoryBreakdownWindow.Closed += (_, _) => m_categoryBreakdownWindow = null;
        m_categoryBreakdownWindow.Show(this);
    }
}
