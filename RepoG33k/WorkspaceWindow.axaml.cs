// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software.

using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using RepoG33k.Services;
using ReviewG33k.Services;
using ReviewG33k.Services.DiffLens;
using ReviewG33k.ViewModels;
using ReviewG33k.Views.DiffLens;
using ReviewG33k.Models;
using ReviewG33k.Views;
using Material.Icons;

namespace RepoG33k;

/// <summary>Hosts repository navigation, Git actions, and the shared editable DiffLens workspace.</summary>
/// <remarks>Selection changes the comparison; selecting a file focuses the existing canvas.</remarks>
public partial class WorkspaceWindow : Window
{
    private readonly RepositoryService m_repository = new();
    private readonly WorkspacePreferences m_preferences = WorkspacePreferences.Load();
    private readonly CodexClient m_codex = new();
    private readonly TemporaryReviewWorkspace m_temporaryReviews = new();
    private DiffLensView m_reviewedView;
    private string m_reviewedScopeId;
    private string m_reviewedLabel;
    private CodeSmellFinding[] m_reviewFindings = [];
    private Dictionary<string, string> m_reviewContents = new(StringComparer.Ordinal);
    private readonly string[] m_args;
    private readonly HashSet<string> m_discoveredRepositories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, RepositoryPickerItem> m_repositoryItems = new(StringComparer.Ordinal);
    private readonly HashSet<string> m_statusReads = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim m_statusReadSlots = new(2);
    private readonly CancellationTokenSource m_statusReadCancellation = new();
    private RepositorySnapshot m_snapshot;
    private DiffLensView m_view;
    private string m_base;
    private string m_index;
    private bool m_busy;
    private bool m_keepEditorActive;
    private bool m_isCancellable;
    private bool m_settingPicker;
    private bool m_closeReady;
    private bool m_closing;
    private bool m_workspaceFormOpen;
    private CancellationTokenSource m_cancellation;
    private readonly DispatcherTimer m_changeTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };

    public WorkspaceWindow() : this([])
    {
    }

    public WorkspaceWindow(string[] args)
    {
        InitializeComponent();
        m_args = args;
        Control RepositoryName(LocalRepositoryOption option)
        {
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            label.Children.Add(new TextBlock { Text = option?.AncestorLabel, Foreground = Brush.Parse("#8B9DAF") });
            label.Children.Add(new TextBlock { Text = option?.RepositoryName, Foreground = Brush.Parse("#E9BC70"), FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            ToolTip.SetTip(label, option?.FullPath);
            return label;
        }
        RepositoryPicker.SelectionBoxItemTemplate = new FuncDataTemplate<RepositoryPickerItem>((item, _) => RepositoryName(item?.Option));
        RepositoryPicker.ItemTemplate = new FuncDataTemplate<RepositoryPickerItem>((item, _) =>
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), MinWidth = 430, DataContext = item };
            var name = RepositoryName(item?.Option);
            ToolTip.SetTip(name, null);
            row.Children.Add(name);
            var badge = new TextBlock { FontSize = 11, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            badge.Bind(TextBlock.TextProperty, new Binding(nameof(RepositoryPickerItem.Badge)));
            badge.Bind(TextBlock.ForegroundProperty, new Binding(nameof(RepositoryPickerItem.Color)));
            Grid.SetColumn(badge, 1);
            row.Children.Add(badge);
            row.Bind(ToolTip.TipProperty, new Binding(nameof(RepositoryPickerItem.Description)));
            return row;
        });
        RepositoryPicker.DropDownOpened += async (_, _) => await RefreshPickerStatusesAsync();
        RepositoryRootButton.Click += async (_, _) => await ChooseRepositoryRootAsync();
        var showFolder = new MenuItem { Header = OperatingSystem.IsMacOS() ? "Show repository in Finder" : "Open repository folder" };
        showFolder.Click += (_, _) =>
        {
            if (m_snapshot != null)
                Process.Start(new ProcessStartInfo(m_snapshot.Root) { UseShellExecute = true });
        };
        var copyPath = new MenuItem { Header = "Copy repository path" };
        copyPath.Click += async (_, _) =>
        {
            if (m_snapshot != null && Clipboard != null)
                await Clipboard.SetTextAsync(m_snapshot.Root);
        };
        var repositoryMenu = new ContextMenu { ItemsSource = new[] { showFolder, copyPath } };
        repositoryMenu.Opening += (_, _) => showFolder.IsEnabled = copyPath.IsEnabled = m_snapshot != null;
        RepositoryPathLabel.ContextMenu = repositoryMenu;
        ToolTip.SetTip(RepositoryPathLabel, "Right-click to open the repository folder or copy its path.");
        var rescan = new MenuItem { Header = "Refresh repositories in parent folder" };
        rescan.Click += async (_, _) => await DiscoverRepositoriesAsync();
        RepositoryRootButton.ContextMenu = new ContextMenu { ItemsSource = new[] { rescan } };
        RepositoryPicker.SelectionChanged += async (_, _) =>
        {
            if (!m_settingPicker && RepositoryPicker.SelectedItem is RepositoryPickerItem option && !RepositoryUtilities.AreSameRepoPath(option.FullPath, m_snapshot?.Root))
                await PerformAsync("Opening repository…", () => OpenAsync(option.FullPath), cancellable: true);
        };
        AllChangesButton.Click += async (_, _) => await SelectScopeAsync(new DiffLensScope("local", "All working changes"));
        StagedButton.Click += async (_, _) => await SelectScopeAsync(new DiffLensScope("staged", "Staged changes"));
        UnstagedButton.Click += async (_, _) => await SelectScopeAsync(new DiffLensScope("unstaged", "Unstaged changes"));
        foreach (var (button, scope) in new[]
                 {
                     (StagedButton, new DiffLensScope("staged", "Staged changes")),
                     (UnstagedButton, new DiffLensScope("unstaged", "Unstaged changes"))
                 })
        {
            MenuItem changeIndex = null;
            if (scope.Id == "unstaged")
            {
                changeIndex = new MenuItem { Header = "Stage all" };
                changeIndex.Click += async (_, _) => await StageAllAsync();
            }
            else if (scope.Id == "staged")
            {
                changeIndex = new MenuItem { Header = "Unstage all" };
                changeIndex.Click += async (_, _) => await UnstageAllAsync();
            }
            var menu = new ContextMenu { ItemsSource = new[] { changeIndex } };
            menu.Opening += (_, _) =>
            {
                if (changeIndex != null)
                    changeIndex.IsEnabled = !m_busy && m_view?.IsLoading != true && m_snapshot?.Files.Any(f => scope.Id == "staged" ? f.IsStaged : f.IsUnstaged || f.IsConflict) == true;
            };
            button.ContextMenu = menu;
        }
        RefreshButton.Click += async (_, _) => await PerformAsync("Refreshing…", RefreshAsync, cancellable: true);
        FetchButton.Click += async (_, _) => await MutateAsync("Fetching remotes…", () => m_repository.RunAsync(m_snapshot.Root, "fetch", "--all", "--prune"));
        PullButton.Click += async (_, _) =>
        {
            if (m_snapshot?.Upstream == null) { await MessageAsync("No upstream", "Choose an upstream by pushing this branch first."); return; }
            await MutateAsync("Pulling updates…", () => m_repository.RunAsync(m_snapshot.Root, "pull", "--ff-only"));
        };
        PushButton.Click += async (_, _) => await PushAsync();
        var leasePush = new MenuItem { Header = "Push rewritten commits with lease…" };
        leasePush.Click += async (_, _) =>
        {
            if (m_snapshot?.UpstreamHead == null)
            {
                await MessageAsync("No upstream", "Publish this branch with Push before updating rewritten commits.");
                return;
            }
            var expected = m_snapshot.UpstreamHead;
            if (await ConfirmAsync("Push rewritten commits?", $"Replace {m_snapshot.Upstream} with {m_snapshot.Branch}? Git will refuse if the remote has moved since the state displayed here. Use this after an amend or rebase of published commits.", "Push with lease"))
                await MutateAsync("Pushing with an explicit lease…", () => m_repository.PushWithLeaseAsync(m_snapshot.Root, expected));
        };
        PushButton.ContextMenu = new ContextMenu { ItemsSource = new[] { leasePush } };
        ToolTip.SetTip(PushButton, "Push normally. Right-click for an explicit force-with-lease push after rebasing or amending published commits.");
        NewBranchButton.Click += async (_, _) => await NewBranchAsync();
        RebaseButton.Click += async (_, _) => await RebaseAsync();
        ContinueButton.Click += async (_, _) => await MutateAsync("Continuing rebase…", () => m_repository.RunAsync(m_snapshot.Root, "-c", "core.editor=true", "rebase", "--continue"));
        AbortButton.Click += async (_, _) =>
        {
            if (await ConfirmAsync("Abort rebase?", "Git will restore the branch to its state before the rebase. Conflict-resolution edits will be discarded.", "Abort rebase"))
                await MutateAsync("Aborting rebase…", () => m_repository.RunAsync(m_snapshot.Root, "rebase", "--abort"));
        };
        ResolveButton.Click += async (_, _) => await CopyConflictTaskAsync();
        CommitButton.Click += async (_, _) => await CommitAsync();
        SummaryBox.TextChanged += (_, _) => UpdateEnabledState();
        ConventionButton.Click += async (_, _) => await ConventionsAsync();
        DraftButton.Click += async (_, _) => await DraftAsync();
        AccountButton.Click += async (_, _) => await ConnectAsync();
        ReviewButton.Click += async (_, _) => await ReviewAsync();
        PullRequestButton.Click += async (_, _) => await OpenPullRequestAsync();
        CancelButton.Click += (_, _) => { m_cancellation?.Cancel(); m_view?.CancelLoad(); };
        AmendButton.IsCheckedChanged += async (_, _) =>
        {
            if (AmendButton.IsChecked == true && m_snapshot?.Head != null)
                await PerformAsync("Reading last commit…", async () =>
                {
                    var commit = m_snapshot.Head;
                    var summary = (await m_repository.RunAsync(m_snapshot.Root, "log", "-1", "--format=%s", commit)).Trim();
                    var details = (await m_repository.RunAsync(m_snapshot.Root, "log", "-1", "--format=%b", commit)).Trim();
                    var hasDraft = !string.IsNullOrWhiteSpace(SummaryBox.Text) || !string.IsNullOrWhiteSpace(DetailsBox.Text);
                    var sameMessage = SummaryBox.Text?.Trim() == summary && (DetailsBox.Text?.Trim() ?? string.Empty) == details;
                    if (hasDraft && !sameMessage)
                    {
                        var preview = new TextBox { Text = summary + "\n\n" + details, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 100, MaxHeight = 250 };
                        if (!await FormAsync("Use the last commit's message?", [Label("Amend is enabled. Replace your current draft with this previous commit message?"), preview], "Use last message", cancelText: "Keep current message"))
                            return;
                    }
                    SummaryBox.Text = summary;
                    DetailsBox.Text = details;
                });
        };
        foreach (var section in Sections)
        {
            section.IsExpanded = section != HistorySection && !m_preferences.CollapsedSections.Contains(section.Name);
            section.PropertyChanged += (_, e) =>
            {
                if (e.Property == Expander.IsExpandedProperty)
                {
                    m_preferences.CollapsedSections = Sections.Where(s => !s.IsExpanded).Select(s => s.Name).ToList();
                    SavePreferences();
                }
            };
        }
        m_changeTimer.Tick += async (_, _) =>
        {
            m_changeTimer.Stop();
            if (!m_busy)
                await PerformAsync("Updating working changes…", RefreshMetadataAsync, keepEditorActive: true);
        };
        m_codex.Progress += text => Dispatcher.UIThread.Post(() => SetStatus(text));
        m_codex.AccountChanged += () => Dispatcher.UIThread.Post(RenderCodexAccount);
        AddHandler(KeyDownEvent, async (_, e) =>
        {
            if (e.Key == Key.F5 && !m_busy && !m_workspaceFormOpen)
            {
                e.Handled = true;
                await PerformAsync("Refreshing…", RefreshAsync, cancellable: true);
            }
        }, RoutingStrategies.Tunnel);
        Opened += async (_, _) =>
        {
            RefreshPicker();
            var index = Array.IndexOf(m_args, "--repo");
            var initial = index >= 0 && index + 1 < m_args.Length ? m_args[index + 1] : m_preferences.Repositories.FirstOrDefault();
            initial ??= RepositoryUtilities.IsGitRepository(Environment.CurrentDirectory) ? Environment.CurrentDirectory : null;
            if (initial != null)
                await PerformAsync("Opening repository…", () => OpenAsync(initial), cancellable: true);
            if (!string.IsNullOrWhiteSpace(m_preferences.RepositoryRoot))
                await DiscoverRepositoriesAsync();
            UpdateEnabledState();
        };
        Closing += async (_, e) =>
        {
            if (m_closeReady)
                return;
            e.Cancel = true;
            if (m_closing)
                return;
            if (m_busy) { m_cancellation?.Cancel(); SetStatus("Wait for the current operation to finish before closing."); return; }
            m_closing = true;
            if (m_view != null && !await m_view.PrepareToCloseAsync()) { m_closing = false; return; }
            StoreDraft();
            SavePreferences();
            m_codex.Dispose();
            m_changeTimer.Stop();
            m_statusReadCancellation.Cancel();
            DispatcherTimer.RunOnce(() => { m_closeReady = true; Close(); }, TimeSpan.FromMilliseconds(1), DispatcherPriority.Background);
        };
        UpdateEnabledState();
    }

    private Expander[] Sections => [WorkingSection, BranchSection, RemoteSection, HistorySection, CommitPanel];
    private RepositoryPreferences Preferences => m_preferences.For(m_snapshot.Root);
    private void SetStatus(string message) { StatusLabel.Text = message; ToolTip.SetTip(StatusLabel, message); }

    private async Task PerformAsync(string message, Func<Task> action, bool cancellable = false, bool keepEditorActive = false)
    {
        if (m_busy || m_closing || m_view?.IsLoading == true)
            return;
        m_busy = true;
        m_keepEditorActive = keepEditorActive;
        m_isCancellable = cancellable;
        using var cancellation = new CancellationTokenSource();
        m_cancellation = cancellation;
        CancelButton.IsVisible = cancellable;
        UpdateEnabledState();
        SetStatus(message);
        try
        {
            if (m_view != null && !await m_view.FlushAsync())
                throw new InvalidOperationException("DiffLens could not save your edits. Resolve the save error before continuing.");
            await action();
            if (StatusLabel.Text == message)
                SetStatus(m_snapshot == null ? "Ready" : $"{m_snapshot.Branch} · {m_snapshot.Ahead} outgoing · {m_snapshot.Behind} incoming");
        }
        catch (OperationCanceledException) { SetStatus("Cancelled."); }
        catch (Exception ex)
        {
            SetStatus(ex.Message);
            await MessageAsync("Could not finish", ex.Message);
        }
        finally
        {
            m_cancellation = null;
            CancelButton.IsVisible = false;
            m_busy = false;
            m_keepEditorActive = false;
            m_isCancellable = false;
            UpdateEnabledState();
        }
    }

    private async Task MutateAsync(string message, Func<Task> action)
    {
        if (m_snapshot == null)
            return;
        await PerformAsync(message, async () =>
        {
            try
            {
                var current = await m_repository.ReadAsync(m_snapshot.Root);
                if (current.Head != m_snapshot.Head || current.Branch != m_snapshot.Branch)
                    throw new InvalidOperationException("The current branch changed outside RepoG33k. Review the refreshed workspace and try again.");
                await action();
            }
            finally { await RefreshAsync(); }
        });
    }

    private void UpdateEnabledState()
    {
        var ready = !m_busy && m_snapshot != null && m_view?.IsLoading != true;
        foreach (var control in new Control[] { FetchButton, PullButton, PushButton, NewBranchButton, RebaseButton, ReviewButton, CommitFields, WorkingSection, BranchSection, RemoteSection, HistorySection, RefreshButton })
            control.IsEnabled = ready;
        RepositoryPicker.IsEnabled = RepositoryRootButton.IsEnabled = AccountButton.IsEnabled = PullRequestButton.IsEnabled = !m_busy;
        DiffLensHost.IsEnabled = !m_busy || m_keepEditorActive || m_view?.IsLoading == true;
        CancelButton.IsVisible = m_view?.IsLoading == true || (m_busy && m_isCancellable);
        RebaseButton.IsEnabled = ready && !m_snapshot.Rebasing && !m_snapshot.Merging;
        CommitButton.IsEnabled = ready && !string.IsNullOrWhiteSpace(SummaryBox.Text) && !m_snapshot.Rebasing && !m_snapshot.Merging && !m_snapshot.Files.Any(f => f.IsConflict) && m_snapshot.Files.Any(f => f.IsStaged || f.IsUnstaged);
        OperationBanner.IsVisible = m_snapshot?.Rebasing == true || m_snapshot?.Merging == true || m_snapshot?.Files.Any(f => f.IsConflict) == true;
        ContinueButton.IsVisible = AbortButton.IsVisible = m_snapshot?.Rebasing == true;
        ContinueButton.IsEnabled = ready;
        AbortButton.IsEnabled = ready;
        ResolveButton.IsEnabled = ready;
        OperationLabel.Text = m_snapshot?.Rebasing == true ? "Rebase in progress · resolve conflicts, then continue or abort" : "Unresolved Git operation · inspect conflicts before continuing";
    }

    private async Task OpenAsync(string root)
    {
        var state = await m_repository.ReadAsync(root);
        if (m_snapshot != null)
            StoreDraft();
        if (m_view != null && !await m_view.PrepareToCloseAsync())
            throw new InvalidOperationException("The current editor still has unsaved changes.");
        m_snapshot = state;
        Commits.Children.Clear();
        var candidates = new[] { "refs/remotes/origin/main", "refs/remotes/origin/master", "refs/heads/main", "refs/heads/master" };
        m_base = candidates.Select(c => state.Branches.FirstOrDefault(b => b.FullName == c)?.FullName).FirstOrDefault(c => c != null)
                 ?? state.Branches.FirstOrDefault(b => !b.Remote)?.FullName ?? "HEAD";
        m_view = new DiffLensView(["--repo", state.Root, "--base", m_base], showScopePicker: false);
        m_view.CanStageFile = path => m_view.CurrentScope?.Id is "local" or "unstaged" or "branch" &&
            m_snapshot.Files.Any(f => f.Path == path && (f.IsUnstaged || f.IsConflict));
        m_view.StageFileAsync = async path =>
        {
            var staged = false;
            var file = m_snapshot.Files.FirstOrDefault(f => f.Path == path);
            if (file == null)
                return false;
            await MutateAsync("Staging file…", async () =>
            {
                await m_repository.StageAsync(m_snapshot.Root, file);
                staged = true;
            });
            return staged;
        };
        m_view.CreateFileGitActions = CreateFileGitActions;
        m_view.ShowReviewResults = ShowReviewResultsAsync;
        m_view.SnapshotChanged += (_, _) => RenderScope();
        m_view.LoadingChanged += (_, _) => UpdateEnabledState();
        m_view.FileSaved += (_, _) => { m_changeTimer.Stop(); m_changeTimer.Start(); };
        DiffLensHost.Content = m_view;
        m_preferences.Repositories.Remove(state.Root);
        m_preferences.Repositories.Insert(0, state.Root);
        RefreshPicker();
        SummaryBox.Text = Preferences.Summary;
        DetailsBox.Text = Preferences.Details;
        AmendButton.IsChecked = false;
        RenderMetadata();
        await m_view.ShowScopeAsync(new DiffLensScope("local", "All working changes"), m_cancellation?.Token ?? default);
        await RefreshIndexAsync();
        SavePreferences();
        SetStatus($"Opened {state.Root}");
    }

    private async Task RefreshIndexAsync() => m_index = m_snapshot.Files.Any(f => f.IsConflict) ? null : await m_repository.IndexTreeAsync(m_snapshot.Root);

    private async Task RefreshMetadataAsync()
    {
        if (m_snapshot == null)
            return;
        var previous = m_snapshot.Branch;
        m_snapshot = await m_repository.ReadAsync(m_snapshot.Root);
        if (m_snapshot.Branch != previous)
            Preferences.JiraOverride = null;
        await RefreshIndexAsync();
        RenderMetadata();
    }

    private async Task RefreshAsync()
    {
        if (m_snapshot == null)
            return;
        await RefreshMetadataAsync();
        if (m_snapshot.Files.Any(f => f.IsConflict))
            await m_view.ShowScopeAsync(new DiffLensScope("local", "All working changes"), m_cancellation?.Token ?? default);
        else
            await m_view.RefreshAsync(m_cancellation?.Token ?? default);
        if (m_view.LastLoadCancelled)
        {
            SetStatus("Comparison loading cancelled. The previous view is retained.");
            return;
        }
        SetStatus("Repository ready.");
    }

    private void RefreshPicker()
    {
        m_settingPicker = true;
        var options = m_preferences.Repositories.Concat(m_discoveredRepositories).Distinct(StringComparer.Ordinal)
            .Where(Directory.Exists).Select(p =>
            {
                var option = LocalRepositoryOption.Create(p, m_preferences.RepositoryRoot);
                return Path.IsPathRooted(option.DisplayName) ? new LocalRepositoryOption(option.FullPath, option.RepositoryName) : option;
            })
            .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        var items = options.Select(option =>
        {
            if (!m_repositoryItems.TryGetValue(option.FullPath, out var item) || item.Option.DisplayName != option.DisplayName)
            {
                var updated = new RepositoryPickerItem(option);
                if (item?.Status != null)
                    updated.Update(item.Status);
                m_repositoryItems[option.FullPath] = item = updated;
            }
            return item;
        }).ToArray();
        RepositoryPicker.ItemsSource = items;
        RepositoryPicker.SelectedItem = items.FirstOrDefault(p => RepositoryUtilities.AreSameRepoPath(p.FullPath, m_snapshot?.Root));
        m_settingPicker = false;
        _ = RefreshPickerStatusesAsync();
    }

    private async Task RefreshPickerStatusesAsync()
    {
        var token = m_statusReadCancellation.Token;
        var items = (RepositoryPicker.ItemsSource as RepositoryPickerItem[] ?? [])
            .Where(item => (item.Status == null || DateTimeOffset.UtcNow - item.Status.CheckedAt > TimeSpan.FromMinutes(1)) && m_statusReads.Add(item.FullPath)).ToArray();
        try
        {
            await Task.WhenAll(items.Select(async item =>
            {
                try
                {
                    await m_statusReadSlots.WaitAsync(token);
                    try
                    {
                        var status = await m_repository.ReadBranchStatusAsync(item.FullPath, token);
                        if (!token.IsCancellationRequested)
                        {
                            item.Update(status);
                        }
                    }
                    finally { m_statusReadSlots.Release(); }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { }
                catch (Exception ex)
                {
                    item.Update(new RepositoryBranchStatus(null, null, 0, 0, null, DateTimeOffset.UtcNow, ex.Message));
                }
                finally { m_statusReads.Remove(item.FullPath); }
            }));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private async Task ChooseRepositoryRootAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Choose the parent folder containing your repositories", AllowMultiple = false });
        var path = folders.FirstOrDefault()?.TryGetLocalPath();
        if (path != null)
            await DiscoverRepositoriesAsync(path);
    }

    private async Task DiscoverRepositoriesAsync(string path = null)
    {
        var root = path ?? m_preferences.RepositoryRoot;
        if (string.IsNullOrWhiteSpace(root))
            return;
        await PerformAsync("Discovering repositories…", async () =>
        {
            var token = m_cancellation.Token;
            var paths = await Task.Run(() => LocalRepositoryDiscoveryService.Discover(root, token).ToArray(), token);
            token.ThrowIfCancellationRequested();
            m_preferences.RepositoryRoot = root;
            m_discoveredRepositories.Clear();
            foreach (var discovered in paths)
                m_discoveredRepositories.Add(discovered);
            RefreshPicker();
            SavePreferences();
            ToolTip.SetTip(RepositoryRootButton, $"Repositories in {root}\nRight-click to refresh the list.");
            SetStatus($"Found {paths.Length} repositories in {root}");
        }, cancellable: true);
    }

    private void StoreDraft()
    {
        if (m_snapshot == null)
            return;
        Preferences.Summary = SummaryBox.Text ?? string.Empty;
        Preferences.Details = DetailsBox.Text ?? string.Empty;
    }

    private void SavePreferences()
    {
        try { m_preferences.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { SetStatus("Could not save workspace preferences: " + ex.Message); }
    }

    private void RenderMetadata()
    {
        var status = new RepositoryBranchStatus(m_snapshot.Branch, m_snapshot.Upstream, m_snapshot.Ahead, m_snapshot.Behind, m_snapshot.LastFetched, DateTimeOffset.UtcNow);
        if (m_repositoryItems.TryGetValue(m_snapshot.Root, out var item))
            item.Update(status);
        RepositoryPathLabel.Text = m_snapshot.Root;
        ToolTip.SetTip(RepositoryPathLabel, $"{m_snapshot.Root}\nRight-click to open the folder or copy its path.");
        ToolTip.SetTip(RepositoryPicker, m_snapshot.Root);
        Title = $"RepoG33k — {Path.GetFileName(m_snapshot.Root)} · {m_snapshot.Branch}";
        AllChangesButton.Content = $"▧  All changes     {m_snapshot.Files.Count}";
        StagedButton.Content = $"✓  Staged     {m_snapshot.Files.Count(f => f.IsStaged)}";
        UnstagedButton.Content = $"◌  Unstaged     {m_snapshot.Files.Count(f => f.IsUnstaged)}";
        PushButton.Content = $"↑  Push{(m_snapshot.Ahead > 0 ? "  " + m_snapshot.Ahead : string.Empty)}";
        PullButton.Content = $"↓  Pull{(m_snapshot.Behind > 0 ? "  " + m_snapshot.Behind : string.Empty)}";
        RenderConvention();
        LocalBranches.Children.Clear();
        RemoteBranches.Children.Clear();
        foreach (var branch in m_snapshot.Branches)
        {
            var button = NavButton((branch.Remote ? "☁  " : "⑂  ") + branch.Name);
            if (branch.Name == m_snapshot.Branch)
            {
                button.Classes.Add("selected");
                if (button.Content is TextBlock label)
                {
                    label.Text = "★  " + branch.Name;
                    label.FontWeight = FontWeight.SemiBold;
                }
            }
            button.Click += async (_, _) => await SelectScopeAsync(new DiffLensScope("ref:" + branch.FullName, branch.Name + " · compared with " + m_base, BranchTip: branch.FullName));
            var compare = new MenuItem { Header = "Compare in DiffLens" };
            compare.Click += async (_, _) => await SelectScopeAsync(new DiffLensScope("ref:" + branch.FullName, branch.Name, BranchTip: branch.FullName));
            var review = new MenuItem { Header = "Review branch" };
            review.Click += async (_, _) => { await SelectScopeAsync(new DiffLensScope("ref:" + branch.FullName, branch.Name, BranchTip: branch.FullName)); await ReviewAsync(); };
            var checkout = new MenuItem { Header = branch.Remote ? "Create local tracking branch…" : "Switch to branch" };
            checkout.Click += async (_, _) =>
            {
                if (branch.Remote) { await NewBranchAsync(branch.FullName); return; }
                await MutateAsync("Switching branches…", () => m_repository.RunAsync(m_snapshot.Root, "switch", "--", branch.Name));
                await SelectScopeAsync(new DiffLensScope("local", "All working changes"));
            };
            var delete = new MenuItem
            {
                Header = branch.Remote ? "Delete remote branch…" : "Delete local branch…",
                IsEnabled = branch.Remote || branch.FullName != "refs/heads/" + m_snapshot.Branch
            };
            delete.Click += async (_, _) => await DeleteBranchAsync(branch);
            if (!delete.IsEnabled)
                ToolTip.SetTip(delete, "Switch to another branch before deleting this one.");
            button.ContextMenu = new ContextMenu { ItemsSource = new[] { compare, review, checkout, delete } };
            (branch.Remote ? RemoteBranches : LocalBranches).Children.Add(button);
        }
    }

    private void RenderConvention()
    {
        var ids = CommitConvention.DetectJiraIds(m_snapshot.Branch);
        ConventionButton.Content = Preferences.Convention == "jira"
            ? !string.IsNullOrWhiteSpace(Preferences.JiraOverride) ? $"Jira · [{Preferences.JiraOverride}] · override  ⌄"
                : ids.Length == 1 ? $"Jira · {ids[0]} · from branch  ⌄" : "Jira · choose story ID  ⌄"
            : $"Home · {Preferences.Category}  ⌄";
        ToolTip.SetTip(ConventionButton, ids.Length == 1 ? $"Detected {ids[0]} in {m_snapshot.Branch}" : "Set the commit convention for this repository");
    }

    private async Task SelectScopeAsync(DiffLensScope scope) => await PerformAsync("Loading comparison…", async () =>
    {
        if (m_view == null)
            return;
        await m_view.ShowScopeAsync(scope, m_cancellation?.Token ?? default);
        if (m_view.LastLoadCancelled)
            SetStatus("Comparison loading cancelled. The previous view is retained.");
        else if (!m_view.LastLoadSucceeded)
            SetStatus("Comparison could not be loaded; see the DiffLens status below.");
        else
            SetStatus(m_view.CurrentScope.Label);
    }, cancellable: true);

    private void RenderScope()
    {
        var scope = m_view.CurrentScope;
        ScopeHeading.Text = scope?.Label ?? "DiffLens";
        ToolTip.SetTip(ScopeHeading, scope?.Commit != null ? $"{scope.Commit} · first-parent comparison (read-only)"
            : scope?.BranchTip != null ? $"Merge base with {m_base} (read-only)"
            : scope?.Id == "staged" ? "Staged snapshot (read-only)" : "Working-copy files can be edited in DiffLens");
        CommitPanel.IsVisible = scope?.Id is "local" or "staged" or "unstaged";
        AllChangesButton.Classes.Set("selected", scope?.Id == "local");
        StagedButton.Classes.Set("selected", scope?.Id == "staged");
        UnstagedButton.Classes.Set("selected", scope?.Id == "unstaged");
        ReviewButton.Content = "Run review";
        Commits.Children.Clear();
        foreach (var commit in m_view.Scopes.Where(s => s.Commit != null).Take(100))
        {
            var button = NavButton(commit.Label);
            ToolTip.SetTip(button, $"{commit.Date} · {commit.Commit}\n{commit.Label}");
            button.Click += async (_, _) => await SelectScopeAsync(commit);
            var review = new MenuItem { Header = "Review commit" };
            review.Click += async (_, _) => { await SelectScopeAsync(commit); await ReviewAsync(); };
            var copyId = new MenuItem { Header = "Copy commit ID" };
            copyId.Click += async (_, _) =>
            {
                if (Clipboard != null)
                    await Clipboard.SetTextAsync(commit.Commit);
            };
            button.ContextMenu = new ContextMenu { ItemsSource = new[] { review, copyId } };
            Commits.Children.Add(button);
        }
    }

    private IReadOnlyList<MenuItem> CreateFileGitActions(string path)
    {
        var actions = new List<MenuItem>();
        if (m_view.CurrentScope?.Id is not ("local" or "staged" or "unstaged"))
            return actions;
        var file = m_snapshot.Files.FirstOrDefault(f => f.Path == path);
        if (file == null)
            return actions;
        var ready = !m_busy && m_view.IsLoading != true;
        if (file.IsUnstaged || file.IsConflict)
        {
            var stage = new MenuItem { Header = file.IsConflict ? "Mark resolved and stage" : "Stage", IsEnabled = ready };
            stage.Click += async (_, _) => await MutateAsync("Staging file…", () => m_repository.StageAsync(m_snapshot.Root, file));
            actions.Add(stage);
        }
        if (file.IsStaged)
        {
            var unstage = new MenuItem { Header = "Unstage", IsEnabled = ready };
            unstage.Click += async (_, _) => await MutateAsync("Unstaging file…", () => m_repository.UnstageAsync(m_snapshot.Root, file));
            actions.Add(unstage);
        }
        return actions;
    }

    private static Button NavButton(string text)
    {
        var button = new Button { Content = new TextBlock { Text = text, TextTrimming = TextTrimming.CharacterEllipsis }, HorizontalContentAlignment = HorizontalAlignment.Left };
        button.Classes.Add("nav");
        ToolTip.SetTip(button, text);
        return button;
    }

    private async Task PushAsync()
    {
        if (m_snapshot == null)
            return;
        if (m_snapshot.Upstream == null)
        {
            var remotes = (await m_repository.RunAsync(m_snapshot.Root, "remote")).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (remotes.Length == 0) { await MessageAsync("No remote", "Add a Git remote before pushing this repository."); return; }
            var remote = remotes.Contains("origin") ? "origin" : remotes[0];
            if (!await ConfirmAsync("Publish this branch?", $"Push {m_snapshot.Branch} to {remote} and set it as the upstream?", "Push branch"))
                return;
            await MutateAsync("Publishing branch…", () => m_repository.RunAsync(m_snapshot.Root, "push", "--set-upstream", remote, m_snapshot.Branch));
        }
        else
            await MutateAsync("Pushing commits…", () => m_repository.RunAsync(m_snapshot.Root, "push"));
    }

    private async Task CommitAsync()
    {
        if (m_snapshot == null || m_index == null)
            return;
        string summary;
        try { summary = CommitConvention.Apply(SummaryBox.Text, Preferences, m_snapshot.Branch); }
        catch (InvalidOperationException ex) { await MessageAsync("Commit convention", ex.Message); return; }
        var amend = AmendButton.IsChecked == true;
        if (amend && !await ConfirmAsync("Amend the last commit?", "This rewrites the last commit. If it has already been pushed, updating the remote will require a separate force-with-lease push.", "Amend commit"))
            return;
        await MutateAsync(amend ? "Amending commit…" : "Creating commit…", async () =>
        {
            await m_repository.CommitWorkingOrStagedAsync(m_snapshot.Root, summary, DetailsBox.Text, amend, m_index);
            SummaryBox.Text = DetailsBox.Text = string.Empty;
            AmendButton.IsChecked = false;
            Preferences.JiraOverride = null;
            StoreDraft();
            SavePreferences();
        });
    }

    private Task StageAllAsync() => MutateAsync("Staging all working changes…", () => m_repository.RunAsync(m_snapshot.Root, "add", "--all"));

    private Task UnstageAllAsync() => MutateAsync("Unstaging changes…", async () =>
    {
        foreach (var file in m_snapshot.Files.Where(f => f.IsStaged))
            await m_repository.UnstageAsync(m_snapshot.Root, file);
    });

    private async Task NewBranchAsync(string startingPoint = null)
    {
        if (m_snapshot == null)
            return;
        var name = new TextBox { Watermark = "feature/TS-1234-SpeedUpApp" };
        var start = BranchPicker(m_snapshot.Branches, startingPoint ?? "refs/heads/" + m_snapshot.Branch);
        string Validate() => string.IsNullOrWhiteSpace(name.Text) ? "Enter a branch name."
            : start.SelectedItem is not RepositoryRef ? "Choose a starting branch." : null;
        if (await FormAsync("Create a branch", [Label("Branch name"), name, Label("Starting point"), start], "Create branch", validate: Validate))
        {
            await MutateAsync("Creating branch…", () => m_repository.CreateBranchAsync(m_snapshot.Root, name.Text.Trim(), ((RepositoryRef)start.SelectedItem).FullName));
            await SelectScopeAsync(new DiffLensScope("local", "All working changes"));
        }
    }

    private async Task DeleteBranchAsync(RepositoryRef branch)
    {
        await PerformAsync("Inspecting branch…", async () =>
        {
            var root = m_snapshot.Root;
            var tip = (await m_repository.RunAsync(root, "rev-parse", "--verify", branch.FullName + "^{commit}")).Trim();
            var allowUnmerged = new CheckBox
            {
                Content = new TextBlock { Text = "Allow deleting an unmerged branch (keep a recovery reference)", TextWrapping = TextWrapping.Wrap }
            };
            var fields = new List<Control>
            {
                Label($"Delete {branch.Name} at {tip[..7]}?"),
                Label(branch.Remote
                    ? "This deletes the branch from the remote server. Local branches are kept. Deletion is refused if the remote branch has moved."
                    : "This deletes the local branch. Working files and remote branches are kept. By default, Git refuses to delete an unmerged branch.")
            };
            if (!branch.Remote)
                fields.Add(allowUnmerged);
            if (!await FormAsync(branch.Remote ? "Delete remote branch" : "Delete local branch", fields.ToArray(), "Delete branch"))
                return;
            var recovery = await m_repository.DeleteBranchAsync(root, branch, tip, allowUnmerged.IsChecked == true);
            // Reopen on working changes so a deleted comparison or base ref is never reloaded.
            await OpenAsync(root);
            SetStatus($"Deleted {(branch.Remote ? "remote" : "local")} branch {branch.Name}" + (recovery == null ? string.Empty : $" · Recovery: {recovery}"));
        });
    }

    private static ComboBox BranchPicker(IEnumerable<RepositoryRef> branches, string selectedRef)
    {
        var choices = branches.ToArray();
        return new ComboBox
        {
            ItemsSource = choices,
            ItemTemplate = new FuncDataTemplate<RepositoryRef>((branch, _) => new TextBlock { Text = branch == null ? null : $"{branch.Name} ({(branch.Remote ? "remote" : "local")})" }),
            SelectedItem = choices.FirstOrDefault(b => b.FullName == selectedRef || b.Name == selectedRef),
            PlaceholderText = "Choose a branch…",
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
    }

    private async Task RebaseAsync()
    {
        if (m_snapshot == null)
            return;
        var target = BranchPicker(m_snapshot.Branches.Where(b => b.FullName != "refs/heads/" + m_snapshot.Branch), m_base);
        string Validate() => target.SelectedItem is not RepositoryRef ? "Choose the branch to rebase onto." : null;
        if (await FormAsync("Rebase feature branch", [Label($"Replay {m_snapshot.Branch} onto"), target, Label("Commit or stash working changes first. A recovery reference preserves the original branch tip. Fetch first to use the latest remote state.")], "Rebase", validate: Validate))
            await MutateAsync("Rebasing branch…", () => m_repository.RebaseAsync(m_snapshot.Root, ((RepositoryRef)target.SelectedItem).FullName));
    }

    private async Task ConventionsAsync()
    {
        if (m_snapshot == null)
            return;
        var convention = new ComboBox { ItemsSource = new[] { "Home · Fix / Feature / Other", "Work · Jira story ID" }, SelectedIndex = Preferences.Convention == "jira" ? 1 : 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var category = new ComboBox { ItemsSource = new[] { "Fix", "Feature", "Other" }, SelectedItem = Preferences.Category, HorizontalAlignment = HorizontalAlignment.Stretch };
        var ids = CommitConvention.DetectJiraIds(m_snapshot.Branch);
        var detected = Label(ids.Length == 1 ? $"Detected {ids[0]} from {m_snapshot.Branch}" : ids.Length == 0 ? "No Jira ID in the branch name. Enter one below." : "Multiple Jira IDs found. Enter the ID for this commit below.");
        var story = new TextBox { Watermark = ids.Length == 1 ? $"Automatic: {ids[0]}" : "TS-1234", Text = Preferences.JiraOverride };
        var example = Label(string.Empty);
        void Update()
        {
            category.IsVisible = convention.SelectedIndex == 0;
            detected.IsVisible = story.IsVisible = convention.SelectedIndex == 1;
            example.Text = convention.SelectedIndex == 1 ? $"[{(string.IsNullOrWhiteSpace(story.Text) ? ids.FirstOrDefault() ?? "STORY-ID" : story.Text)}] Fix the slow app startup" : $"{category.SelectedItem}: Update the README with new images";
        }
        convention.SelectionChanged += (_, _) => Update();
        category.SelectionChanged += (_, _) => Update();
        story.TextChanged += (_, _) => Update();
        Update();
        if (await FormAsync("Commit message convention", [Label("Saved for this repository"), convention, category, detected, story, Label("Story overrides apply to this commit only."), example], "Save convention"))
        {
            Preferences.Convention = convention.SelectedIndex == 1 ? "jira" : "home";
            Preferences.Category = category.SelectedItem as string ?? "Feature";
            Preferences.JiraOverride = story.Text?.Trim();
            RenderConvention();
            SavePreferences();
        }
    }

    private async Task ConnectAsync() => await PerformAsync("Connecting to Codex…", async () =>
    {
        if (!await m_codex.ReadAccountAsync(m_cancellation.Token))
        {
            SetStatus("Finish signing in with ChatGPT in your browser…");
            await m_codex.SignInAsync(uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }), m_cancellation.Token);
        }
        RenderCodexAccount();
        SetStatus("ChatGPT connected. Drafting uses your Codex account's available usage.");
    }, cancellable: true);

    private void RenderCodexAccount()
    {
        AccountButton.Content = m_codex.AccountLabel == null ? "✦  Sign in with ChatGPT" : "✦  ChatGPT connected";
        ToolTip.SetTip(AccountButton, m_codex.AccountLabel ?? "Connect your ChatGPT account for Codex commit drafts.");
    }

    private async Task DraftAsync()
    {
        if (m_snapshot == null)
            return;
        await PerformAsync("Preparing changes for Codex…", async () =>
        {
            var tree = await m_repository.IndexTreeAsync(m_snapshot.Root);
            var stagedFiles = await m_repository.RunAsync(m_snapshot.Root, "diff", "--cached", "--name-only", "--no-ext-diff", "--no-textconv", "--");
            var staged = !string.IsNullOrWhiteSpace(stagedFiles);
            if (!staged && !(await m_repository.ReadAsync(m_snapshot.Root)).Files.Any(file => file.IsUnstaged))
                throw new InvalidOperationException("There are no changes to draft a commit for.");
            var draft = await m_codex.DraftAsync(m_snapshot.Root, m_cancellation.Token, staged);
            if (await m_repository.IndexTreeAsync(m_snapshot.Root) != tree)
                throw new InvalidOperationException("The staged changes changed while Codex was drafting. Refresh and generate a new message.");
            var summary = CommitConvention.Apply(draft.Summary, Preferences, m_snapshot.Branch);
            var summaryPreview = new TextBox { Text = summary, Watermark = "Summary", MaxLines = 1, AcceptsReturn = false };
            var detailsPreview = new TextBox { Text = draft.Details, Watermark = "Details (optional)", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 150, MaxHeight = 350 };
            string ValidateDraft() => string.IsNullOrWhiteSpace(summaryPreview.Text) ? "Enter a commit summary." : null;
            if (await FormAsync("Use this commit draft?", [Label(staged ? "Based on staged changes. Edit the draft before applying it." : "Based on working changes, including untracked files. Edit the draft before applying it."), Label("Summary"), summaryPreview, Label("Details"), detailsPreview], "Use draft", validate: ValidateDraft))
            {
                SummaryBox.Text = summaryPreview.Text.Trim();
                DetailsBox.Text = detailsPreview.Text ?? string.Empty;
                m_index = tree;
                StoreDraft();
                SavePreferences();
            }
        }, cancellable: true);
    }

    private async Task ReviewAsync()
    {
        await PerformAsync("Reviewing selected comparison…", async () =>
        {
            if (m_view == null || !m_view.LastLoadSucceeded)
                throw new InvalidOperationException("Load a comparison before reviewing it.");
            await m_view.RefreshAsync(m_cancellation?.Token ?? default);
            if (!m_view.LastLoadSucceeded)
                throw new InvalidOperationException("Could not refresh the comparison for review.");
            var entries = m_view.Entries;
            var report = await Task.Run(() => WorkspaceReviewService.ReviewAsync(entries, text => Dispatcher.UIThread.Post(() => SetStatus(text)), m_cancellation.Token));
            var snapshot = DiffLensReviewSnapshot.Create(m_snapshot.Root, m_view.ComparisonTarget, m_snapshot.Root, report.Findings, m_view.ReviewMode);
            await m_view.ShowReviewAsync(snapshot);
            m_reviewedView = m_view;
            m_reviewedScopeId = m_view.CurrentScope?.Id;
            m_reviewedLabel = m_view.CurrentScope?.Label;
            m_reviewFindings = snapshot.Findings.ToArray();
            m_reviewContents = entries.Where(e => e.File != null).ToDictionary(e => e.Path, e => e.File.WorkingText, StringComparer.Ordinal);
            SetStatus(entries.Any(e => e.File == null) ? "Review complete. Some binary or unavailable files were skipped." : "Review complete.");
        }, cancellable: true);
    }

    private async Task ShowReviewResultsAsync()
    {
        if (m_reviewedView != m_view || m_view?.CurrentScope?.Id != m_reviewedScopeId)
            return;
        ReviewResultsWindow results = null;
        results = new ReviewResultsWindow(m_reviewFindings,
            canOpenInVsCode: true, canCommentInBitbucket: false, canFixLocally: false,
            findingFixer: null,
            openFindingAction: finding => { results.Close(); m_view.FocusFinding(finding); },
            openFindingWithTargetAction: null,
            isOpenTargetAvailable: _ => true,
            openTargetChangedAction: null,
            initialOpenTarget: CodeLocationOpenTarget.Clipboard,
            openTargetDefinitions: [new CodeLocationOpenTargetDefinition(CodeLocationOpenTarget.Clipboard, "DiffLens", MaterialIconKind.CodeTags, true, () => true, null)],
            commentFindingAction: null, resolveFindingPath: null, resampleFileFindingsAction: null,
            pullRequestTitle: m_reviewedLabel,
            readSnapshotText: finding => m_reviewContents.GetValueOrDefault(finding.FilePath),
            findingSelection: m_view.FindingSelection);
        await results.ShowDialog(this);
    }

    private async Task OpenPullRequestAsync()
    {
        var url = new TextBox { Watermark = "https://bitbucket.example.com/projects/TEAM/repos/repo/pull-requests/123" };
        string Validate() => !BitbucketPrUrlParser.TryParse(url.Text, out _, out var error) ? error : null;
        if (!await FormAsync("Open a Bitbucket pull request", [Label("Pull request URL"), url, Label("The PR will open in a temporary workspace, deleted when RepoG33k closes.")], "Review PR", validate: Validate, withinWorkspace: true))
            return;
        await PerformAsync("Preparing pull request review…", async () =>
        {
            var dependencies = MainWindowCompositionRoot.CreateDependencies();
            var result = await dependencies.ReviewWorkflowService.PrepareReviewByModeAsync(true, false, false, m_temporaryReviews.GetRepositoryRoot(), url.Text, null, null, false,
                text => Dispatcher.UIThread.Post(() => SetStatus(text)), null, m_cancellation.Token);
            if (!result.IsSuccess)
                throw new InvalidOperationException(result.Error?.DialogMessage ?? "Could not prepare pull request review.");
            var applied = dependencies.ReviewWorkflowService.BuildApplyResult(result, null);
            if (applied.Report == null || string.IsNullOrWhiteSpace(applied.ReviewWorktreePath))
                throw new InvalidOperationException("This PR did not produce a review workspace. It may be closed without an available merge commit.");
            var target = applied.PullRequestBaseBranch ?? "HEAD";
            var snapshot = DiffLensReviewSnapshot.Create(applied.ReviewWorktreePath, target, applied.ReviewWorktreePath, applied.Report.Findings);
            var window = new ReviewG33k.Views.DiffLens.DiffLensWindow(["--repo", applied.ReviewWorktreePath, "--base", target], snapshot);
            window.Show(this);
            SetStatus("PR opened in its isolated DiffLens review window.");
        }, cancellable: true);
    }

    internal void CleanupTemporaryReviews()
    {
        try { m_temporaryReviews.Dispose(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Could not remove temporary PR workspace: {ex.Message}");
        }
    }

    private async Task CopyConflictTaskAsync()
    {
        if (m_snapshot == null || Clipboard == null)
            return;
        await Clipboard.SetTextAsync($"Resolve the current Git conflicts in {m_snapshot.Root}. Inspect Git state and the intent of both sides. Preserve unrelated work. Run focused validation and explain any uncertain decisions. Leave the Git operation paused for me to inspect in DiffLens; do not continue, commit, abort, or push. Conflicted paths: {string.Join(", ", m_snapshot.Files.Where(f => f.IsConflict).Select(f => f.Path))}");
        SetStatus("Conflict task copied. Paste it into Codex, then refresh DiffLens to inspect the result.");
    }

    private static TextBlock Label(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse("#A6B7C8"), FontSize = 12 };

    private async Task<bool> ConfirmAsync(string title, string message, string accept) => await FormAsync(title, [Label(message)], accept);

    private async Task MessageAsync(string title, string message) => await FormAsync(title, [Label(message)], "OK", cancel: false);

    private async Task<bool> FormAsync(string title, Control[] fields, string accept, bool cancel = true, Func<string> validate = null, string cancelText = "Cancel", bool withinWorkspace = false)
    {
        var completion = new TaskCompletionSource<bool>();
        var dialog = withinWorkspace ? null : new Window { Title = title, Width = cancel ? 510 : 440, SizeToContent = SizeToContent.Height, MaxHeight = 700, CanResize = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = Brush.Parse("#171F2A"), Foreground = Brush.Parse("#D9E3EE") };
        void Complete(bool result)
        {
            if (withinWorkspace)
                completion.TrySetResult(result);
            else
                dialog.Close(result);
        }
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12, VerticalAlignment = VerticalAlignment.Top };
        if (cancel)
            panel.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap });
        foreach (var field in fields)
            panel.Children.Add(field);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 8, 0, 0) };
        if (cancel)
        {
            var dismiss = new Button { Content = cancelText, IsCancel = true };
            dismiss.Click += (_, _) => Complete(false);
            actions.Children.Add(dismiss);
        }
        var validation = Label(string.Empty);
        validation.IsVisible = validate != null;
        panel.Children.Add(validation);
        var confirm = new Button { Content = accept, MinWidth = 80, IsDefault = true };
        confirm.Classes.Add("primary");
        void UpdateValidation()
        {
            var error = validate?.Invoke();
            validation.Text = error ?? string.Empty;
            validation.IsVisible = !string.IsNullOrEmpty(error);
            confirm.IsEnabled = string.IsNullOrEmpty(error);
        }
        foreach (var field in fields)
        {
            if (field is TextBox textBox)
                textBox.TextChanged += (_, _) => UpdateValidation();
            if (field is ComboBox comboBox)
                comboBox.SelectionChanged += (_, _) => UpdateValidation();
        }
        UpdateValidation();
        confirm.Click += (_, _) =>
        {
            UpdateValidation();
            if (confirm.IsEnabled)
                Complete(true);
        };
        actions.Children.Add(confirm);
        panel.Children.Add(actions);
        var content = new ScrollViewer { Content = panel, VerticalAlignment = VerticalAlignment.Top, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        if (withinWorkspace)
        {
            var root = (Grid)Content;
            var previousFocus = FocusManager?.GetFocusedElement();
            var controls = root.Children.Select(control => (Control: control, WasEnabled: control.IsEnabled)).ToArray();
            var overlay = new Border
            {
                Background = Brush.Parse("#99000000"),
                Child = new Border
                {
                    Width = 510, MaxWidth = Math.Max(0, Bounds.Width - 40), MaxHeight = Math.Max(0, Bounds.Height - 40),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Background = Brush.Parse("#171F2A"), BorderBrush = Brush.Parse("#38495B"), BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8), Child = content
                }
            };
            Grid.SetRowSpan(overlay, 3);
            KeyboardNavigation.SetTabNavigation(overlay, KeyboardNavigationMode.Cycle);
            overlay.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape && cancel) { Complete(false); e.Handled = true; }
                else if (e.Key == Key.Enter && confirm.IsEnabled && e.Source is TextBox { AcceptsReturn: false }) { Complete(true); e.Handled = true; }
            };
            m_workspaceFormOpen = true;
            foreach (var item in controls)
                item.Control.IsEnabled = false;
            root.Children.Add(overlay);
            (fields.FirstOrDefault(field => field.Focusable) ?? confirm).Focus();
            try { return await completion.Task; }
            finally
            {
                root.Children.Remove(overlay);
                m_workspaceFormOpen = false;
                foreach (var item in controls)
                    item.Control.IsEnabled = item.WasEnabled;
                previousFocus?.Focus();
            }
        }
        dialog.Content = content;
        return await dialog.ShowDialog<bool>(this);
    }
}
