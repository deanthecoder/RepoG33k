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
using AvaloniaEdit.Document;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using DTC.Core;
using Material.Icons;
using Material.Icons.Avalonia;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using System.Globalization;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using AvaloniaEdit;
using Avalonia.Animation;
using Avalonia.Threading;
using ReviewG33k.Services.DiffLens;
using ReviewG33k.Services;
using ReviewG33k.ViewModels;

namespace ReviewG33k.Views.DiffLens;

/// <summary>
/// Embeds the DiffLens repository canvas and working-copy editor in either application.
/// </summary>
/// <remarks>
/// The editor document stays alive while switching back to the zoomable diff.
/// </remarks>
internal sealed class DiffLensView : UserControl
{
    private readonly string m_repositoryPath;
    private string m_targetBranch;
    private readonly string m_relativePath;
    private readonly TextBlock m_status = new() { Text = "No repository selected.", TextWrapping = TextWrapping.Wrap };
    private readonly DiffLensControl m_canvas = new();
    private readonly TextEditor m_editor = new()
    {
        Options = { ConvertTabsToSpaces = true, IndentationSize = 4 },
        ShowLineNumbers = true, FontFamily = new FontFamily("Cascadia Mono,Consolas,monospace"), FontSize = 16,
        Background = Brush.Parse("#151E29"), Foreground = Brush.Parse("#D7E3EE"), IsVisible = false
    };
    private DiffLensReviewSnapshot m_review;
    private readonly CommandLineReviewMode m_reviewMode;
    private DiffLensFile m_file;
    private readonly Dictionary<string, DiffLensFile> m_files = new();
    private readonly Dictionary<string, TextDocument> m_documents = new();
    private readonly Dictionary<string, DiffLensFindingActions> m_findingActions = new();
    private readonly DispatcherTimer m_autoSave = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly SemaphoreSlim m_saveGate = new(1, 1);
    private TaskCompletionSource<bool> m_loadCompleted;
    private string m_reviewScopeId;
    private bool m_isClosing;
    private bool m_isClosed;
    private bool m_loading;
    private bool m_settingText;
    private bool m_isEditing;
    private int m_transitionVersion;

    private readonly Button m_refresh = new()
    {
        Content = new MaterialIcon { Kind = MaterialIconKind.Refresh, Width = 18, Height = 18 },
        Padding = new Thickness(8)
    };
    private readonly ProgressBar m_loadBar = new() { Width = 130, Height = 5, IsVisible = false };
    private readonly Button m_cancelLoad = new() { Content = "×", IsVisible = false, Padding = new Thickness(8, 2) };
    private CancellationTokenSource m_loadCancellation;
    private ProgressToken m_loadProgress;

    private readonly ComboBox m_scopePicker = new() { MinWidth = 360, MaxWidth = 760, MinHeight = 36, Height = 36, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly TextBlock m_scopeNote = new() { VerticalAlignment = VerticalAlignment.Center, Foreground = Brush.Parse("#E9BC70") };
    private readonly Grid m_surface = new() { ColumnDefinitions = new ColumnDefinitions("*,0") };
    private readonly DiffLensFilePanel m_filePanel;
    private readonly ColumnDefinition m_fileColumn;
    private readonly GridSplitter m_fileSplitter;
    private readonly DispatcherTimer m_panelSaveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private Control m_findingsPanel;
    private bool m_findingsExpanded = true;
    private readonly Button m_findingsToggle = new() { IsVisible = false };

    private DiffLensScope m_scope;
    private string m_scopeTarget;
    private bool m_settingScope;
    private readonly DiffLensComparisonCache m_comparisonCache = new();
    private readonly bool m_showScopePicker;

    private bool ScopeMatchesReview => m_review != null && m_scope?.Id == (m_reviewScopeId ?? (m_reviewMode switch
    {
        CommandLineReviewMode.Committed => "branch",
        CommandLineReviewMode.Tree => "tree",
        _ => "local"
    }));

    private string ViewHint => m_scope?.IsReadOnly == true ? "Double-click to inspect · Read-only snapshot" : CanvasStatusHint;

    private const string CanvasStatusHint = "Double-click to edit";

    private static string NavigationHint => OperatingSystem.IsMacOS()
        ? "Two-finger scroll: pan · Pinch or ⌘+scroll: zoom · Shift+scroll: long lines"
        : "Scroll or drag: pan · Ctrl+wheel: zoom · Shift+wheel: long lines";

    public DiffLensView(string[] args = null, DiffLensReviewSnapshot review = null, CommandLineReviewMode? reviewMode = null, bool showScopePicker = true)
    {
        m_showScopePicker = showScopePicker;
        m_review = review;
        m_reviewMode = reviewMode ?? review?.Mode ??
            (Argument(args, "--base") is { } branch && branch != "HEAD" ? CommandLineReviewMode.Committed : CommandLineReviewMode.Uncommitted);
        Title = "ReviewG33k DiffLens";
        Background = Brush.Parse("#0C1219");
        m_repositoryPath = Argument(args, "--repo") ?? Settings.Instance.LocalReviewRepositoryPath;
        m_targetBranch = Argument(args, "--base") ?? "HEAD";
        m_relativePath = Argument(args, "--file");
        Title = $"ReviewG33k DiffLens — {m_repositoryPath} ({m_targetBranch})";
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var panelExpanded = Settings.Instance.DiffLensFilePanelExpanded;
        m_filePanel = new DiffLensFilePanel(panelExpanded);
        m_fileColumn = new ColumnDefinition { Width = new GridLength(panelExpanded ? Math.Clamp(Settings.Instance.DiffLensFilePanelWidth, 180, 480) : 0), MinWidth = panelExpanded ? 180 : 0, MaxWidth = panelExpanded ? 480 : 0 };
        m_fileSplitter = new GridSplitter { Width = 5, ResizeDirection = GridResizeDirection.Columns, Background = Brush.Parse("#10161F"), IsVisible = panelExpanded };
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions { m_fileColumn, new() { Width = GridLength.Auto }, new() { Width = new GridLength(1, GridUnitType.Star) } } };
        content.Children.Add(m_filePanel);
        Grid.SetColumn(m_fileSplitter, 1);
        content.Children.Add(m_fileSplitter);
        var surface = m_surface;
        Grid.SetColumn(surface, 2);
        surface.Children.Add(m_canvas);
        surface.Children.Add(m_editor);
        RebuildFindingsPanel();
        m_canvas.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(180) } };
        m_editor.Transitions = new Transitions { new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(180) } };
        m_editor.Opacity = 0;
        content.Children.Add(surface);
        Grid.SetRow(content, 1);
        root.Children.Add(content);
        m_filePanel.FileSelected += FocusFile;
        m_filePanel.ReportError = text => m_status.Text = text;
        var filesToggle = new Button { Content = panelExpanded ? "Hide files" : "Show files" };
        filesToggle.Click += (_, _) => m_filePanel.SetExpanded(!m_filePanel.IsExpanded);
        m_filePanel.ExpandedChanged += expanded =>
        {
            m_canvas.PreserveViewDuringPanelResize();
            filesToggle.Content = expanded ? "Hide files" : "Show files";
            if (!expanded)
                SaveFilePanelWidth(force: true);
            m_fileColumn.MinWidth = expanded ? 180 : 0;
            m_fileColumn.MaxWidth = expanded ? 480 : double.PositiveInfinity;
            m_fileColumn.Width = new GridLength(expanded ? Math.Clamp(Settings.Instance.DiffLensFilePanelWidth, 180, 480) : 0);
            m_fileSplitter.IsVisible = expanded;
            Settings.Instance.DiffLensFilePanelExpanded = expanded;
            SaveFilePanelPreferences();
            Dispatcher.UIThread.Post(() => m_canvas.Fit(), DispatcherPriority.Loaded);
        };
        m_filePanel.SizeChanged += (_, e) =>
        {
            if (!e.WidthChanged)
                return;
            m_canvas.PreserveViewDuringPanelResize();
            if (m_filePanel.IsExpanded)
            {
                m_panelSaveTimer.Stop();
                m_panelSaveTimer.Start();
            }
        };
        m_panelSaveTimer.Tick += (_, _) => { m_panelSaveTimer.Stop(); SaveFilePanelWidth(); };
        var footer = new DockPanel { Margin = new Thickness(12, 8) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var fit = new Button { Content = "Fit all" };
        fit.Click += (_, _) =>
        {
            if (m_isEditing)
                ToggleEditor();
            m_canvas.Fit();
        };
        ToolTip.SetTip(fit, "Fit all changed files.");
        actions.Children.Add(filesToggle);
        actions.Children.Add(fit);
        ToolTip.SetTip(m_refresh, "Refresh files from disk (F5)");
        Avalonia.Automation.AutomationProperties.SetName(m_refresh, "Refresh files from disk");
        m_refresh.Click += async (_, _) => await LoadSnapshotAsync();
        var comparisons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(12, 8) };
        comparisons.IsVisible = showScopePicker;
        m_scopePicker.SelectionBoxItemTemplate = new FuncDataTemplate<DiffLensScope>((scope, _) => new TextBlock
        {
            Text = scope?.Label, FontSize = 13, FontWeight = FontWeight.SemiBold,
            Foreground = Brush.Parse("#E9BC70"), TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        });
        m_scopePicker.ItemTemplate = new FuncDataTemplate<DiffLensScope>((scope, _) =>
        {
            if (scope == null)
                return new TextBlock();
            var item = new StackPanel { Spacing = 3, Margin = new Thickness(0, 3), MaxWidth = 680 };
            if (scope.Commit != null)
            {
                var date = DateTime.TryParseExact(scope.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var parsed) ? parsed.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) : scope.Date;
                item.Children.Add(new TextBlock
                {
                    Text = $"{date}  ·  {scope.ShortHash ?? scope.Commit[..Math.Min(7, scope.Commit.Length)]}",
                    FontSize = 11, Foreground = Brush.Parse("#91A3B8")
                });
            }
            item.Children.Add(new TextBlock
            {
                Text = scope.Label, FontSize = 13, FontWeight = FontWeight.SemiBold,
                Foreground = Brush.Parse("#E9BC70"), TextTrimming = TextTrimming.CharacterEllipsis
            });
            ToolTip.SetTip(item, scope.Commit == null ? scope.Label : $"{scope.Label}\n{scope.Commit}");
            return item;
        });
        comparisons.Children.Add(m_scopePicker);
        m_refresh.Height = 36;
        m_refresh.VerticalAlignment = VerticalAlignment.Center;
        comparisons.Children.Add(m_refresh);
        comparisons.Children.Add(m_scopeNote);
        root.Children.Add(comparisons);
        ToolTip.SetTip(m_scopePicker, "Choose the Git comparison. Commit views compare against their first parent.");
        m_scopePicker.SelectionChanged += async (_, _) =>
        {
            if (!m_settingScope && m_scopePicker.SelectedItem is DiffLensScope selected && selected.Id != m_scope?.Id)
                await LoadSnapshotAsync(selected);
        };
        DockPanel.SetDock(actions, Dock.Left);
        actions.Margin = new Thickness(0, 0, 12, 0);
        m_status.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(actions);
        DockPanel.SetDock(m_findingsToggle, Dock.Right);
        footer.Children.Add(m_findingsToggle);
        m_findingsToggle.Click += (_, _) =>
        {
            m_findingsExpanded = !m_findingsExpanded;
            UpdateFindingsVisibility();
            Dispatcher.UIThread.Post(() => m_canvas.Fit(), DispatcherPriority.Loaded);
        };
        ToolTip.SetTip(footer, NavigationHint);
        var loadingControls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        loadingControls.Children.Add(m_loadBar);
        loadingControls.Children.Add(m_cancelLoad);
        DockPanel.SetDock(loadingControls, Dock.Right);
        footer.Children.Add(loadingControls);
        footer.Children.Add(m_status);
        ToolTip.SetTip(m_cancelLoad, "Cancel loading");
        Avalonia.Automation.AutomationProperties.SetName(m_cancelLoad, "Cancel loading");
        m_cancelLoad.Click += (_, _) => m_loadProgress?.Cancel();
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        Content = root;
        m_canvas.FindingLineResolver = finding => m_findingActions.TryGetValue(finding.FilePath, out var actions)
            ? actions.GetCurrentLine(finding) : finding.LineNumber;
        m_canvas.ViewChanged += (_, _) => UpdateView();
        m_canvas.EditRequested += (_, request) => { SelectFile(request.Path); ToggleEditor(request.Line); };
        m_canvas.ContextRequested += (_, e) =>
        {
            var findings = e.TryGetPosition(m_canvas, out var point) ? m_canvas.FindingsAt(point) : m_canvas.SelectedFindings;
            if (findings.Count == 0)
                return;
            OpenFindingMenu(m_canvas, findings);
            e.Handled = true;
        };
        m_autoSave.Tick += async (_, _) =>
        {
            m_autoSave.Stop();
            await SaveAsync();
        };
        m_editor.TextChanged += (_, _) =>
        {
            if (m_settingText || m_file?.CanEdit != true)
                return;
            m_autoSave.Stop();
            m_autoSave.Start();
            m_status.Text = "Saving...";
        };
        AddHandler(KeyDownEvent, async (_, e) =>
        {
            if (e.Key == Key.S && e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                await SaveAsync();
                e.Handled = true;
            }
            else if (e.Key == Key.F5 && e.KeyModifiers == KeyModifiers.None)
            {
                await LoadSnapshotAsync();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && m_isEditing)
            {
                ToggleEditor();
                e.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
    }

    public string Title { get; private set; }
    public bool IsLoading => m_loading;
    public DiffLensScope CurrentScope => m_scope;
    public Func<string, bool> CanStageFile { get => m_canvas.CanStageFile; set => m_canvas.CanStageFile = value; }
    public Func<string, Task<bool>> StageFileAsync { get => m_canvas.StageFileAsync; set => m_canvas.StageFileAsync = value; }
    public ReviewFindingSelection FindingSelection { get; private set; } = new();
    public Func<Task> ShowReviewResults { get; set; }

    public Func<string, IReadOnlyList<MenuItem>> CreateFileGitActions { get => m_filePanel.CreateGitActions; set => m_filePanel.CreateGitActions = value; }
    public IReadOnlyList<DiffLensEntry> Entries { get; private set; } = [];
    public IReadOnlyList<DiffLensScope> Scopes { get; private set; } = [];
    public event EventHandler SnapshotChanged;
    public event EventHandler FileSaved;
    public event EventHandler LoadingChanged;
    public string ComparisonTarget => m_targetBranch;
    public CommandLineReviewMode ReviewMode => m_reviewMode;
    public bool LastLoadSucceeded { get; private set; }
    public bool LastLoadCancelled { get; private set; }
    public bool LastLoadWasCached { get; private set; }
    public string StatusText => m_status.Text;

    public Task InitializeAsync() => LoadSnapshotAsync();
    public Task RefreshAsync(CancellationToken token = default) => LoadSnapshotAsync(m_scope, token);
    public Task ShowScopeAsync(DiffLensScope scope, CancellationToken token = default) => LoadSnapshotAsync(scope, token);
    public void CancelLoad() => m_loadCancellation?.Cancel();

    private void SaveFilePanelWidth(bool force = false)
    {
        if ((!m_filePanel.IsExpanded && !force) || m_fileColumn.ActualWidth < 180)
            return;
        Settings.Instance.DiffLensFilePanelWidth = Math.Clamp(m_fileColumn.ActualWidth, 180, 480);
        SaveFilePanelPreferences();
    }

    private void SaveFilePanelPreferences()
    {
        try { Settings.Instance.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { m_status.Text = "Could not save file panel preferences: " + ex.Message; }
    }

    public void FocusFile(string path)
    {
        if (m_isEditing)
            ToggleEditor();
        m_canvas.FocusFile(path);
    }

    public void FocusFinding(CodeSmellFinding finding)
    {
        if (m_isEditing)
            ToggleEditor();
        var line = m_findingActions.TryGetValue(finding.FilePath, out var actions) ? actions.GetCurrentLine(finding) : finding.LineNumber;
        m_status.Text = m_canvas.FocusFinding(finding.FilePath, line) ? string.Empty : "Finding is outside this view.";
    }

    public async Task ShowReviewAsync(DiffLensReviewSnapshot review)
    {
        m_review = review;
        FindingSelection = new ReviewFindingSelection();
        m_reviewScopeId = m_scope?.Id;
        m_findingsExpanded = true;
        RebuildFindingsPanel();
        await LoadSnapshotAsync(m_scope);
    }

    public async Task<bool> FlushAsync()
    {
        m_autoSave.Stop();
        return await SaveAsync();
    }

    public async Task<bool> PrepareToCloseAsync()
    {
        if (m_isClosed)
            return true;
        m_isClosing = true;
        m_loadCancellation?.Cancel();
        if (m_loadCompleted != null)
            await m_loadCompleted.Task;
        m_autoSave.Stop();
        if (!await SaveAsync())
        {
            m_isClosing = false;
            return false;
        }
        m_isClosed = true;
        ++m_transitionVersion;
        m_panelSaveTimer.Stop();
        SaveFilePanelWidth();
        m_canvas.StopAnimations();
        return true;
    }

    private IReadOnlyList<CodeSmellFinding> ActiveFindingsFor(string root, string path) => ScopeMatchesReview
        ? m_review.FindingsFor(root, m_targetBranch, m_reviewMode, path).Where(finding => !FindingSelection.IsIgnored(finding)).ToArray()
        : [];

    private async Task OpenReviewResultsAsync()
    {
        if (!ScopeMatchesReview)
            return;
        if (ShowReviewResults != null)
            await ShowReviewResults();
        else if (TopLevel.GetTopLevel(this) is Window owner)
        {
            ReviewResultsWindow dialog = null;
            dialog = new ReviewResultsWindow(m_review.Findings, true, false, false, null,
                finding => { dialog.Close(); FocusFinding(finding); }, null, _ => true, null,
                CodeLocationOpenTarget.Clipboard,
                [new CodeLocationOpenTargetDefinition(CodeLocationOpenTarget.Clipboard, "DiffLens", MaterialIconKind.CodeTags, true, () => true, null)],
                null, null, null, findingSelection: FindingSelection);
            await dialog.ShowDialog(owner);
        }
        RebuildFindingsPanel();
        m_canvas.UpdateFindings(path => ActiveFindingsFor(m_repositoryPath, path));
    }

    private void RebuildFindingsPanel()
    {
        if (m_findingsPanel != null)
            m_surface.Children.Remove(m_findingsPanel);
        m_findingsPanel = null;
        if (m_review?.Findings.Count > 0)
        {
            m_findingsPanel = CreateFindingsPanel();
            Grid.SetColumn(m_findingsPanel, 1);
            m_surface.Children.Add(m_findingsPanel);
        }
        UpdateFindingsVisibility();
    }

    private void UpdateFindingsVisibility()
    {
        var available = m_findingsPanel != null && ScopeMatchesReview;
        m_canvas.PreserveViewDuringPanelResize();
        m_findingsToggle.IsVisible = available;
        m_findingsToggle.Content = m_findingsExpanded ? "Hide issues" : "Show issues";
        if (m_findingsPanel != null)
            m_findingsPanel.IsVisible = available && m_findingsExpanded;
        m_surface.ColumnDefinitions[1].Width = new GridLength(available && m_findingsExpanded ? 340 : 0);
    }

    private Control CreateFindingsPanel()
    {
        var panel = new DockPanel();
        var heading = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 0, 12) };
        heading.Children.Add(new TextBlock { Text = "Review findings", FontSize = 18 });
        ToolTip.SetTip(heading, "Findings are from the last review. Run review again after editing to update them.");
        DockPanel.SetDock(heading, Dock.Top);
        panel.Children.Add(heading);
        var results = new Button { Content = "Results…", HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        results.Click += async (_, _) => await OpenReviewResultsAsync();
        DockPanel.SetDock(results, Dock.Bottom);
        panel.Children.Add(results);
        var items = new StackPanel { Spacing = 8 };
        var activeFindings = m_review.Findings.Where(finding => !FindingSelection.IsIgnored(finding)).ToArray();
        if (activeFindings.Length == 0)
            items.Children.Add(new TextBlock { Text = "No active findings", Foreground = Brush.Parse("#91A3B8") });
        foreach (var finding in activeFindings.OrderBy(finding => finding.FilePath).ThenBy(finding => finding.LineNumber))
        {
            var label = new StackPanel { Spacing = 4 };
            label.Children.Add(new TextBlock { Text = $"{finding.Severity}", Foreground = Brush.Parse("#E9BC70"), TextWrapping = TextWrapping.Wrap });
            label.Children.Add(new TextBlock { Text = $"{finding.FilePath}:{finding.LineNumber}", FontSize = 12, Foreground = Brush.Parse("#91A3B8"), TextWrapping = TextWrapping.Wrap });
            label.Children.Add(new TextBlock { Text = finding.Message, TextWrapping = TextWrapping.Wrap });
            var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(10) };
            button.Click += (_, _) =>
            {
                FocusFinding(finding);
            };
            button.ContextRequested += (_, e) =>
            {
                OpenFindingMenu(button, [finding]);
                e.Handled = true;
            };
            items.Children.Add(button);
        }
        panel.Children.Add(new ScrollViewer { Content = items, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        return new Border
        {
            Background = Brush.Parse("#121C27"),
            BorderBrush = Brush.Parse("#2A3948"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(10, 12, 12, 0),
            Padding = new Thickness(14),
            Child = panel
        };
    }

    private TextDocument GetDocument(DiffLensFile file)
    {
        if (!m_documents.TryGetValue(file.RelativePath, out var document))
        {
            m_documents[file.RelativePath] = document = new TextDocument(file.WorkingText);
            var findings = m_review?.FindingsFor(m_repositoryPath, m_targetBranch, m_reviewMode, file.RelativePath) ?? [];
            m_findingActions[file.RelativePath] = new DiffLensFindingActions(file.RelativePath, document, findings);
        }
        return document;
    }

    private void OpenFindingMenu(Control target, IReadOnlyList<CodeSmellFinding> findings)
    {
        if (m_loading || m_isClosing || m_isClosed)
            return;
        ToolTip.SetIsOpen(m_canvas, false);
        var menu = new ContextMenu();
        foreach (var finding in findings)
        {
            var items = CreateFindingMenuItems(finding);
            if (findings.Count == 1)
                menu.ItemsSource = items;
            else
                menu.Items.Add(new MenuItem
                {
                    Header = new TextBlock { Text = finding.Message, MaxWidth = 340, TextWrapping = TextWrapping.Wrap },
                    ItemsSource = items
                });
        }
        target.ContextMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (target.ContextMenu == menu)
                target.ContextMenu = null;
        };
        menu.Open(target);
    }

    private MenuItem[] CreateFindingMenuItems(CodeSmellFinding finding)
    {
        DiffLensFindingActions actions = null;
        var reason = "The issue's file is unavailable for editing.";
        var canInsert = false;
        if (m_files.TryGetValue(finding.FilePath, out var file) && file.CanEdit)
        {
            GetDocument(file);
            actions = m_findingActions[file.RelativePath];
            canInsert = actions.CanInsertTodo(finding, out reason);
        }
        var add = new MenuItem { Header = "Add TODO above this line", IsEnabled = canInsert };
        ToolTip.SetTip(add, canInsert ? "Insert an undoable TODO using this issue's instructions" : reason);
        add.Click += async (_, _) => await AddFindingTodoAsync(finding);
        var copy = new MenuItem { Header = "Copy fix instructions", IsEnabled = TopLevel.GetTopLevel(this)?.Clipboard != null };
        copy.Click += async (_, _) =>
        {
            try
            {
                var instructions = actions?.GetFixInstructions(finding) ??
                    $"File: {finding.FilePath}:{finding.LineNumber}\nRule: {finding.RuleId}\nIssue: {finding.Message}";
                if (TopLevel.GetTopLevel(this)?.Clipboard != null)
                {
                    await TopLevel.GetTopLevel(this)!.Clipboard!.SetTextAsync(instructions);
                    m_status.Text = "Fix instructions copied.";
                }
            }
            catch (Exception ex)
            {
                m_status.Text = $"Could not copy instructions: {ex.Message}";
            }
        };
        return [add, copy];
    }

    private async Task AddFindingTodoAsync(CodeSmellFinding finding)
    {
        if (m_loading || m_isClosing || m_isClosed || !m_files.TryGetValue(finding.FilePath, out var file) || !file.CanEdit)
            return;
        GetDocument(file);
        if (!m_findingActions[file.RelativePath].TryInsertTodo(finding, out var line, out var reason))
        {
            m_status.Text = reason;
            return;
        }
        SelectFile(file.RelativePath);
        if (!m_isEditing)
            ToggleEditor(line);
        else
        {
            m_editor.TextArea.Caret.Line = line;
            m_editor.ScrollToLine(line);
        }
        await SaveAsync();
    }

    private void SelectFile(string path)
    {
        if (path == null || !m_files.TryGetValue(path, out var file))
        {
            m_file = null;
            return;
        }
        if (m_file == file)
            return;
        m_file = file;
        var document = GetDocument(file);
        m_settingText = true;
        m_editor.Document = document;
        m_settingText = false;
        m_editor.SyntaxHighlighting = DiffLensSyntax.ForPath(path);
    }

    private void UpdateView()
    {
        if (!m_isEditing)
            SelectFile(m_canvas.SelectedPath);
        m_filePanel.SelectPath(m_canvas.SelectedPath);
    }

    private async Task LoadSnapshotAsync(DiffLensScope requested = null, CancellationToken token = default)
    {
        if (m_loading || m_isClosing || m_isClosed)
            return;
        LastLoadSucceeded = false;
        LastLoadCancelled = LastLoadWasCached = false;
        m_loadCompleted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        SetBusy(true);
        m_autoSave.Stop();
        if (!await SaveAsync())
        {
            m_settingScope = true;
            m_scopePicker.SelectedItem = (m_scopePicker.ItemsSource as IEnumerable<DiffLensScope>)?.FirstOrDefault(s => s.Id == m_scope?.Id);
            m_settingScope = false;
            SetBusy(false);
            m_loadCompleted.TrySetResult(true);
            return;
        }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        m_loadCancellation = cancellation;
        m_loadProgress = new ProgressToken(isCancelSupported: true);
        m_loadProgress.Cancelled += (_, _) =>
        {
            cancellation.Cancel();
            m_cancelLoad.IsEnabled = false;
            m_status.Text = "Cancelling…";
        };
        m_loadBar.IsVisible = m_cancelLoad.IsVisible = true;
        m_loadBar.IsIndeterminate = true;
        m_cancelLoad.IsEnabled = true;
        var progress = new Progress<DiffLensLoadProgress>(update =>
        {
            if (cancellation.IsCancellationRequested || !ReferenceEquals(m_loadCancellation, cancellation))
                return;
            m_loadProgress.IsIndeterminate = update.Total == 0;
            if (update.Total > 0)
                m_loadProgress.Progress = (int)(100L * update.Completed / update.Total);
            m_loadBar.IsIndeterminate = m_loadProgress.IsIndeterminate;
            m_loadBar.Value = m_loadProgress.Progress;
            m_status.Text = update.Total == 0 ? update.Stage : $"{update.Stage}… {update.Completed:N0} / {update.Total:N0}";
        });
        m_status.Text = "Finding files…";
        try
        {
            var result = await Task.Run(async () =>
            {
                var loader = new DiffLensFileLoader(cancellation.Token, progress);
                var retained = requested ?? m_scope;
                if (retained == null && (m_review != null || m_reviewMode != CommandLineReviewMode.Uncommitted))
                    retained = m_reviewMode switch
                    {
                        CommandLineReviewMode.Tree => new DiffLensScope("tree", "Entire repository"),
                        CommandLineReviewMode.Committed => new DiffLensScope("branch", "Entire branch"),
                        _ => new DiffLensScope("local", "All local changes")
                    };
                var stagedSnapshot = retained?.Id == "staged" ? await loader.CaptureStagedSnapshotAsync(m_repositoryPath) : null;
                var cacheKey = await loader.GetImmutableCacheKeyAsync(m_repositoryPath, m_scopeTarget ?? m_targetBranch, retained, m_relativePath, stagedSnapshot);
                if (m_comparisonCache.TryGet(cacheKey, out var cached))
                {
                    LastLoadWasCached = true;
                    return cached;
                }
                var catalog = await loader.GetScopesAsync(m_repositoryPath, m_scopeTarget ?? m_targetBranch, retained, m_showScopePicker ? null : 100);
                if (m_review != null || m_reviewMode == CommandLineReviewMode.Tree)
                {
                    var original = m_reviewMode switch
                    {
                        CommandLineReviewMode.Tree => new DiffLensScope("tree", "Entire repository"),
                        CommandLineReviewMode.Committed => new DiffLensScope("branch", "Entire branch"),
                        _ => new DiffLensScope("local", "All local changes")
                    };
                    if (catalog.Scopes.All(s => s.Id != original.Id))
                        catalog.Scopes.Insert(0, original);
                }
                var scope = retained == null ? catalog.Scopes.First() : catalog.Scopes.First(s => s.Id == retained.Id);
                var loaded = await loader.LoadScopeAsync(m_repositoryPath, catalog.Target, scope, m_relativePath, stagedSnapshot);
                var prepared = new List<DiffLensCell>();
                foreach (var entry in loaded.Entries)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    ((IProgress<DiffLensLoadProgress>)progress).Report(new DiffLensLoadProgress("Preparing canvas", prepared.Count, loaded.Entries.Count));
                    prepared.Add(new DiffLensCell(entry));
                }
                cancellation.Token.ThrowIfCancellationRequested();
                var comparison = new DiffLensLoadedComparison(catalog, scope, loaded, prepared.ToArray());
                if (cacheKey != null && cacheKey == await loader.GetImmutableCacheKeyAsync(m_repositoryPath, m_scopeTarget ?? m_targetBranch, retained, m_relativePath))
                    m_comparisonCache.Store(cacheKey, comparison);
                return comparison;
            }, cancellation.Token);
            var changeSet = result.ChangeSet;
            var cells = result.Cells;
            cancellation.Token.ThrowIfCancellationRequested();
            var sameScope = m_scope?.Id == result.Scope.Id;
            m_scope = result.Scope;
            m_scopeTarget = result.Catalog.Target;
            m_settingScope = true;
            m_scopePicker.ItemsSource = result.Catalog.Scopes;
            m_scopePicker.SelectedItem = result.Catalog.Scopes.First(s => s.Id == m_scope.Id);
            m_settingScope = false;
            m_refresh.IsVisible = m_scope.Commit == null;
            Title = $"ReviewG33k DiffLens — {m_repositoryPath} ({m_scope.Label})";
            UpdateFindingsVisibility();
            m_scopeNote.Text = m_review?.Findings.Count > 0 && !ScopeMatchesReview
                ? "Findings belong to the original review comparison" : string.Empty;
            m_files.Clear();
            m_documents.Clear();
            m_findingActions.Clear();
            m_file = null;
            foreach (var entry in changeSet.Entries.Where(entry => entry.File != null))
                m_files[entry.Path] = entry.File;
            m_editor.IsVisible = false;
            m_transitionVersion++;
            m_isEditing = false;
            m_editor.Opacity = 0;
            m_canvas.IsVisible = true;
            m_canvas.Opacity = 1;
            m_canvas.IsHitTestVisible = true;
            foreach (var cell in cells)
                cell.Findings = ActiveFindingsFor(changeSet.Root, cell.Entry.Path);
            m_canvas.RepositoryRootPath = changeSet.Root;
            m_canvas.SetFiles(Path.GetFileName(changeSet.Root), cells, preserveReview: sameScope);
            Entries = changeSet.Entries;
            Scopes = result.Catalog.Scopes;
            m_filePanel.UpdateFiles(changeSet.Root, m_scope, Entries, m_canvas.SelectedPath);
            LastLoadSucceeded = true;
            SnapshotChanged?.Invoke(this, EventArgs.Empty);
            m_status.Text = cells.Length == 0 ? $"No files in {m_scope.Label}." : ViewHint;
        }
        catch (OperationCanceledException)
        {
            LastLoadCancelled = true;
            m_status.Text = "Loading cancelled.";
        }
        catch (Exception ex)
        {
            m_status.Text = $"Unable to load: {ex.Message}";
        }
        finally
        {
            m_settingScope = true;
            m_scopePicker.SelectedItem = (m_scopePicker.ItemsSource as IEnumerable<DiffLensScope>)?.FirstOrDefault(s => s.Id == m_scope?.Id);
            m_settingScope = false;
            m_loadCancellation = null;
            m_loadProgress = null;
            m_loadBar.IsVisible = m_cancelLoad.IsVisible = false;
            SetBusy(false);
            m_loadCompleted.TrySetResult(true);
        }
    }

    private void ToggleEditor(int? line = null)
    {
        if (m_file == null || (!m_file.CanEdit && !m_file.IsReadOnly) || m_loading || m_isClosing || m_isClosed)
            return;
        m_isEditing = !m_isEditing;
        m_editor.IsReadOnly = !m_file.CanEdit;
        var version = ++m_transitionVersion;
        m_editor.IsVisible = true;
        m_canvas.IsVisible = true;
        m_editor.IsHitTestVisible = m_isEditing;
        m_canvas.IsHitTestVisible = !m_isEditing;
        m_editor.Opacity = m_isEditing ? 1 : 0;
        // The canvas stays opaque underneath the editor throughout the crossfade.
        m_canvas.Opacity = 1;
        _ = FinishTransitionAsync(version);
        if (m_isEditing)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (m_isClosing || m_isClosed || version != m_transitionVersion || !m_isEditing)
                    return;
                m_editor.Focus();
                if (line is { } requestedLine)
                {
                    var targetLine = Math.Clamp(requestedLine, 1, m_editor.Document.LineCount);
                    m_editor.TextArea.Caret.Line = targetLine;
                    m_editor.TextArea.Caret.Column = 1;
                    m_editor.ScrollToLine(targetLine);
                }
            }, DispatcherPriority.Loaded);
            m_status.Text = m_file.CanEdit ? "Edits save automatically · Esc to return" : "Read-only snapshot · Esc to return";
        }
        else
        {
            m_canvas.SetFile(m_file.RelativePath, m_file.Baseline, m_editor.Text, false);
            m_canvas.Focus();
            m_status.Text = ViewHint;
            m_autoSave.Stop();
            _ = SaveAsync();
        }
        UpdateView();
    }

    private async Task FinishTransitionAsync(int version)
    {
        await Task.Delay(180);
        if (m_isClosing || m_isClosed || version != m_transitionVersion)
            return;
        m_editor.IsVisible = m_isEditing;
        m_canvas.IsVisible = !m_isEditing;
    }

    private async Task<bool> SaveAsync()
    {
        // Serialize writes without disabling typing. Always read the captured document,
        // since the user may select another file while a write is in progress.
        await m_saveGate.WaitAsync();
        try
        {
            foreach (var (path, document) in m_documents.ToArray())
            {
                var file = m_files[path];
                if (!file.CanEdit)
                    continue;
                while (document.Text != file.WorkingText)
                {
                    await file.SaveAsync(document.Text);
                    m_canvas.SetFile(path, file.Baseline, document.Text, false);
                    FileSaved?.Invoke(this, EventArgs.Empty);
                    m_status.Text = m_isEditing ? "Saved." : CanvasStatusHint;
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            m_status.Text = $"Could not save automatically: {ex.Message} Your edits are retained; Ctrl+S retries.";
            return false;
        }
        finally { m_saveGate.Release(); }
    }

    private void SetBusy(bool busy)
    {
        m_loading = busy;
        LoadingChanged?.Invoke(this, EventArgs.Empty);
        m_refresh.IsEnabled = !busy;
        m_scopePicker.IsEnabled = !busy;
        m_canvas.IsHitTestVisible = !busy && !m_isEditing;
        m_editor.IsReadOnly = busy || m_file?.CanEdit != true;
        UpdateView();
    }

    private static string Argument(string[] args, string name)
    {
        var index = Array.IndexOf(args ?? [], name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
