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
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using DTC.Core.Commands;
using DTC.Core.ViewModels;
using ReviewG33k.Models;
using ReviewG33k.Services;

namespace ReviewG33k.ViewModels;

/// <summary>
/// View-model state for the main ReviewG33k window.
/// </summary>
public sealed class MainWindowViewModel : ViewModelBase
{
    private const int PullRequestReviewModeIndex = 0;
    private const int LocalCommittedReviewModeIndex = 1;
    private const int LocalUncommittedReviewModeIndex = 2;
    private const int LocalRepositoryReviewModeIndex = 3;

    private readonly Settings m_settings;
    private string m_repositoryRootPath;
    private string m_localRepositoryPath;
    private string m_localBaseBranch;
    private string m_pullRequestUrl = string.Empty;
    private string m_pullRequestMetadataText = string.Empty;
    private string m_statusText = "Ready.";
    private string m_previewPullRequestState;
    private string m_previewPullRequestTitle;
    private string m_latestReviewWorktreePath;
    private string m_latestSolutionPath;
    private int m_reviewModeIndex;
    private int m_scanScopeIndex;
    private bool m_isBusy;
    private bool? m_previewPullRequestIsOpen;
    private bool m_isGitAvailable = true;
    private bool m_canReviewCurrentPullRequest = true;
    private bool m_hasValidPullRequestInput;
    private bool m_hasValidPullRequestPrepareInputs;
    private bool m_hasValidLocalPrepareInputs;
    private bool m_hasAvailableSolution;
    private bool m_hasPostedPullRequestReviewStamp;
    private bool m_canCancelCurrentOperation;
    private bool m_isCancellationRequested;
    private bool m_busyProgressIsIndeterminate = true;
    private double m_busyProgressMaximum = 1;
    private double m_busyProgressValue;
    private int m_latestPullRequestFindingCount;
    private int m_latestPullRequestImportantFindingCount;
    private int m_latestPostedInlineCommentCount;
    private bool m_gitAvailabilityChecked;
    private CommandBase m_browseRepositoryRootCommandImpl;
    private CommandBase m_prepareReviewCommandImpl;
    private CommandBase m_postPullRequestReviewStampCommandImpl;
    private CommandBase m_cancelProcessingCommandImpl;
    private CommandBase m_openPullRequestCommandImpl;
    private CommandBase m_openSolutionCommandImpl;

    public ObservableCollection<LocalRepositoryOption> LocalRepositoryOptions { get; } = [];
    public ObservableCollection<string> LocalBaseBranchOptions { get; } = [];
    public ICommand BrowseRepositoryRootCommand { get; private set; }
    public ICommand PrepareReviewCommand { get; private set; }
    public ICommand PostPullRequestReviewStampCommand { get; private set; }
    public ICommand CancelProcessingCommand { get; private set; }
    public ICommand OpenPullRequestCommand { get; private set; }
    public ICommand OpenSolutionCommand { get; private set; }

    public MainWindowViewModel()
    {
        InitializeDisabledCommands();
    }

    public MainWindowViewModel(Settings settings)
    {
        m_settings = settings ?? throw new ArgumentNullException(nameof(settings));
        m_repositoryRootPath = (m_settings.RepositoryRootPath ?? string.Empty).Trim();
        m_localRepositoryPath = (m_settings.LocalReviewRepositoryPath ?? string.Empty).Trim();
        m_localBaseBranch = NormalizeBaseBranch(m_settings.LocalReviewBaseBranch);
        m_reviewModeIndex = ResolveInitialReviewModeIndex(m_settings);
        m_scanScopeIndex = m_settings.IncludeFullModifiedFiles ? 1 : 0;
        SetLocalBaseBranchOptions([m_localBaseBranch], m_localBaseBranch);
        InitializeDisabledCommands();
    }

    public string RepositoryRootPath
    {
        get => m_repositoryRootPath;
        set
        {
            var normalizedValue = (value ?? string.Empty).Trim();
            if (!SetField(ref m_repositoryRootPath, normalizedValue))
                return;

            m_settings.RepositoryRootPath = normalizedValue;
            SaveSettingsSafely();
        }
    }

    public string LocalRepositoryPath
    {
        get => m_localRepositoryPath;
        set
        {
            var normalizedValue = (value ?? string.Empty).Trim();
            if (!SetField(ref m_localRepositoryPath, normalizedValue))
                return;

            m_settings.LocalReviewRepositoryPath = normalizedValue;
            SaveSettingsSafely();
        }
    }

    public string LocalBaseBranch
    {
        get => m_localBaseBranch;
        set
        {
            var normalizedValue = NormalizeBaseBranch(value);
            if (!SetField(ref m_localBaseBranch, normalizedValue))
                return;

            m_settings.LocalReviewBaseBranch = normalizedValue;
            SaveSettingsSafely();
        }
    }

    public int ReviewModeIndex
    {
        get => m_reviewModeIndex;
        set
        {
            var normalizedValue = value == 4
                ? LocalRepositoryReviewModeIndex
                : value is >= 0 and <= LocalRepositoryReviewModeIndex ? value : PullRequestReviewModeIndex;
            if (!SetField(ref m_reviewModeIndex, normalizedValue))
                return;

            m_settings.ReviewModeIndex = normalizedValue;
            m_settings.UseLocalCommittedReview = normalizedValue == 1;
            SaveSettingsSafely();
            OnReviewModeUiChanged();
            OnActionStateChanged();
        }
    }

    public int ScanScopeIndex
    {
        get => m_scanScopeIndex;
        set
        {
            var normalizedValue = value == 1 ? 1 : 0;
            if (!SetField(ref m_scanScopeIndex, normalizedValue))
                return;

            m_settings.IncludeFullModifiedFiles = normalizedValue == 1;
            SaveSettingsSafely();
            OnPropertyChanged(nameof(ScanScopeInfoTooltip));
            OnPropertyChanged(nameof(IncludeFullModifiedFiles));
        }
    }

    public string PullRequestUrl
    {
        get => m_pullRequestUrl;
        set
        {
            if (SetField(ref m_pullRequestUrl, NormalizePullRequestUrl(value)))
                OnActionStateChanged();
        }
    }

    public string PullRequestMetadataText
    {
        get => m_pullRequestMetadataText;
        set
        {
            var normalizedValue = value ?? string.Empty;
            if (!SetField(ref m_pullRequestMetadataText, normalizedValue))
                return;

            OnPropertyChanged(nameof(ShowPullRequestMetadata));
        }
    }

    public string StatusText
    {
        get => m_statusText;
        set => SetField(ref m_statusText, value ?? string.Empty);
    }

    public string PreviewPullRequestTitle
    {
        get => m_previewPullRequestTitle;
        private set => SetField(ref m_previewPullRequestTitle, value);
    }

    public string PreviewPullRequestState
    {
        get => m_previewPullRequestState;
        private set
        {
            if (!SetField(ref m_previewPullRequestState, value))
                return;

            OnPropertyChanged(nameof(PreviewPullRequestStateDisplay));
        }
    }

    public bool? PreviewPullRequestIsOpen
    {
        get => m_previewPullRequestIsOpen;
        private set => SetField(ref m_previewPullRequestIsOpen, value);
    }

    public string PreviewPullRequestStateDisplay =>
        string.IsNullOrWhiteSpace(m_previewPullRequestState)
            ? "N/A"
            : m_previewPullRequestState.Trim().ToUpperInvariant();

    internal BitbucketPullRequestReference LatestPullRequest { get; private set; }

    internal string LatestReviewWorktreePath => m_latestReviewWorktreePath;

    internal string LatestPullRequestBaseBranch { get; private set; }

    public bool CanOpenDiffLens => !IsBusy && (!IsPullRequestReviewMode ||
        (CurrentPullRequestMatchesLatestReview() &&
         !string.IsNullOrWhiteSpace(LatestPullRequestBaseBranch) &&
         RepositoryUtilities.IsGitRepository(LatestReviewWorktreePath)));

    internal string LatestSolutionPath => m_latestSolutionPath;

    public bool IsBusy
    {
        get => m_isBusy;
        set
        {
            if (!SetField(ref m_isBusy, value))
                return;

            OnActionStateChanged();
        }
    }

    public bool IsGitAvailable
    {
        get => m_isGitAvailable;
        set
        {
            if (!SetField(ref m_isGitAvailable, value))
                return;

            OnActionStateChanged();
        }
    }

    public bool ShowPullRequestInputs => m_reviewModeIndex == PullRequestReviewModeIndex;

    public bool IsPullRequestReviewMode => m_reviewModeIndex == PullRequestReviewModeIndex;

    public bool IsLocalCommittedReviewMode => m_reviewModeIndex == LocalCommittedReviewModeIndex;

    public bool IsLocalUncommittedReviewMode => m_reviewModeIndex == LocalUncommittedReviewModeIndex;

    public bool IsLocalRepositoryReviewMode => m_reviewModeIndex == LocalRepositoryReviewModeIndex;

    public bool IsAnyLocalReviewMode => !IsPullRequestReviewMode;

    public bool CanChooseScanScope => !IsLocalRepositoryReviewMode;

    public bool ShowScanScopeOptions => CanChooseScanScope;

    public bool ShowPullRequestMetadata =>
        ShowPullRequestInputs && !string.IsNullOrWhiteSpace(m_pullRequestMetadataText);

    public bool ShowLocalBaseBranch => m_reviewModeIndex == LocalCommittedReviewModeIndex;

    public string PrepareReviewButtonText => m_reviewModeIndex == PullRequestReviewModeIndex
        ? "Review PR"
        : "Review Local";

    public string ReviewModeInfoTooltip => m_reviewModeIndex switch
    {
        LocalCommittedReviewModeIndex =>
            "Reviews committed changes in your local repo against the selected base branch (for example origin/main).",
        LocalUncommittedReviewModeIndex =>
            "Reviews your uncommitted working tree changes, without requiring commits.",
        LocalRepositoryReviewModeIndex =>
            "Reviews all analyzable files in your local repository folder, regardless of Git change state.",
        _ =>
            "Reviews a Bitbucket pull request by preparing an isolated local worktree for that PR."
    };

    public string ScanScopeInfoTooltip => CanChooseScanScope
        ? m_scanScopeIndex == 1
            ? "Runs checks across entire modified files. More thorough, but slower."
            : "Runs checks only on newly added lines. Faster for targeted reviews."
        : "Whole-tree scans always review every analyzable line, so scan scope does not apply.";

    public bool IncludeFullModifiedFiles => CanChooseScanScope && m_scanScopeIndex == 1;

    internal bool MarkGitAvailabilityChecked()
    {
        if (m_gitAvailabilityChecked)
            return false;

        m_gitAvailabilityChecked = true;
        return true;
    }

    public bool CanPrepareReview => !m_isBusy &&
                                    m_isGitAvailable &&
                                    (ShowPullRequestInputs
                                        ? m_canReviewCurrentPullRequest && m_hasValidPullRequestPrepareInputs
                                        : m_hasValidLocalPrepareInputs);

    public bool CanOpenPullRequest => !m_isBusy &&
                                      ShowPullRequestInputs &&
                                      m_hasValidPullRequestInput;

    public bool CanPostPullRequestReviewStamp => !m_isBusy &&
                                                 IsPullRequestReviewMode &&
                                                 LatestPullRequest != null &&
                                                 CurrentPullRequestMatchesLatestReview() &&
                                                 m_latestPullRequestFindingCount >= 0 &&
                                                 !m_hasPostedPullRequestReviewStamp;

    public bool CanOpenSolution => !m_isBusy && m_hasAvailableSolution;

    public bool CanCancelProcessing => m_isBusy &&
                                       !m_isCancellationRequested &&
                                       m_canCancelCurrentOperation;

    public bool ShowCancelStoppingText => m_isBusy && m_isCancellationRequested;

    public bool BusyProgressIsIndeterminate
    {
        get => m_busyProgressIsIndeterminate;
        private set => SetField(ref m_busyProgressIsIndeterminate, value);
    }

    public double BusyProgressMaximum
    {
        get => m_busyProgressMaximum;
        private set => SetField(ref m_busyProgressMaximum, value);
    }

    public double BusyProgressValue
    {
        get => m_busyProgressValue;
        private set => SetField(ref m_busyProgressValue, value);
    }

    public void ConfigureCommands(
        Func<Task> browseRepositoryRootAsync,
        Func<Task> prepareReviewAsync,
        Func<Task> postPullRequestReviewStampAsync,
        Action cancelProcessing,
        Action openPullRequest,
        Func<Task> openSolutionAsync)
    {
        ArgumentNullException.ThrowIfNull(browseRepositoryRootAsync);
        ArgumentNullException.ThrowIfNull(prepareReviewAsync);
        ArgumentNullException.ThrowIfNull(postPullRequestReviewStampAsync);
        ArgumentNullException.ThrowIfNull(cancelProcessing);
        ArgumentNullException.ThrowIfNull(openPullRequest);
        ArgumentNullException.ThrowIfNull(openSolutionAsync);

        m_browseRepositoryRootCommandImpl = new AsyncRelayCommand(
            _ => browseRepositoryRootAsync(),
            _ => !IsBusy);
        m_prepareReviewCommandImpl = new AsyncRelayCommand(
            _ => prepareReviewAsync(),
            _ => CanPrepareReview);
        m_postPullRequestReviewStampCommandImpl = new AsyncRelayCommand(
            _ => postPullRequestReviewStampAsync(),
            _ => CanPostPullRequestReviewStamp);
        m_cancelProcessingCommandImpl = new RelayCommand(
            _ => cancelProcessing(),
            _ => CanCancelProcessing);
        m_openPullRequestCommandImpl = new RelayCommand(
            _ => openPullRequest(),
            _ => CanOpenPullRequest);
        m_openSolutionCommandImpl = new AsyncRelayCommand(
            _ => openSolutionAsync(),
            _ => CanOpenSolution);

        BrowseRepositoryRootCommand = m_browseRepositoryRootCommandImpl;
        PrepareReviewCommand = m_prepareReviewCommandImpl;
        PostPullRequestReviewStampCommand = m_postPullRequestReviewStampCommandImpl;
        CancelProcessingCommand = m_cancelProcessingCommandImpl;
        OpenPullRequestCommand = m_openPullRequestCommandImpl;
        OpenSolutionCommand = m_openSolutionCommandImpl;

        OnPropertyChanged(nameof(BrowseRepositoryRootCommand));
        OnPropertyChanged(nameof(PrepareReviewCommand));
        OnPropertyChanged(nameof(PostPullRequestReviewStampCommand));
        OnPropertyChanged(nameof(CancelProcessingCommand));
        OnPropertyChanged(nameof(OpenPullRequestCommand));
        OnPropertyChanged(nameof(OpenSolutionCommand));
        RaiseCommandCanExecuteChanged();
    }

    public void AddLocalBaseBranchOption(string baseBranch)
    {
        var normalizedBranch = NormalizeBranchOption(baseBranch);
        if (string.IsNullOrWhiteSpace(normalizedBranch))
            return;

        if (!LocalBaseBranchOptions.Any(branch => branch.Equals(normalizedBranch, StringComparison.OrdinalIgnoreCase)))
            LocalBaseBranchOptions.Add(normalizedBranch);
    }

    public void SetLocalBaseBranchOptions(IEnumerable<string> branchOptions, string selectedBaseBranch)
    {
        var normalizedOptions = (branchOptions ?? [])
            .Select(NormalizeBranchOption)
            .Where(branch => !string.IsNullOrWhiteSpace(branch))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var normalizedSelection = NormalizeBranchOption(selectedBaseBranch);

        if (!string.IsNullOrWhiteSpace(normalizedSelection) &&
            !normalizedOptions.Any(branch => branch.Equals(normalizedSelection, StringComparison.OrdinalIgnoreCase)))
        {
            normalizedOptions.Add(normalizedSelection);
        }

        if (normalizedOptions.Count == 0)
            normalizedOptions.Add("main");

        LocalBaseBranchOptions.Clear();
        foreach (var branchOption in normalizedOptions)
            LocalBaseBranchOptions.Add(branchOption);

        var selectedBranch = LocalBaseBranchOptions.FirstOrDefault(
                                 branch => branch.Equals(normalizedSelection, StringComparison.OrdinalIgnoreCase)) ??
                             LocalBaseBranchOptions.FirstOrDefault(branch => !string.IsNullOrWhiteSpace(branch)) ??
                             "main";
        ApplyLocalBaseBranchSelection(selectedBranch);
    }

    public void UpdateActionStateInputs(
        bool canReviewCurrentPullRequest,
        bool hasValidPullRequestInput,
        bool hasValidPullRequestPrepareInputs,
        bool hasValidLocalPrepareInputs,
        bool hasAvailableSolution,
        bool canCancelCurrentOperation,
        bool isCancellationRequested)
    {
        var hasAnyChange = false;
        hasAnyChange |= SetIfDifferent(ref m_canReviewCurrentPullRequest, canReviewCurrentPullRequest);
        hasAnyChange |= SetIfDifferent(ref m_hasValidPullRequestInput, hasValidPullRequestInput);
        hasAnyChange |= SetIfDifferent(ref m_hasValidPullRequestPrepareInputs, hasValidPullRequestPrepareInputs);
        hasAnyChange |= SetIfDifferent(ref m_hasValidLocalPrepareInputs, hasValidLocalPrepareInputs);
        hasAnyChange |= SetIfDifferent(ref m_hasAvailableSolution, hasAvailableSolution);
        hasAnyChange |= SetIfDifferent(ref m_canCancelCurrentOperation, canCancelCurrentOperation);
        hasAnyChange |= SetIfDifferent(ref m_isCancellationRequested, isCancellationRequested);

        if (hasAnyChange)
            OnActionStateChanged();
    }

    public void UpdatePullRequestReviewState(string title, string state)
    {
        PreviewPullRequestTitle = string.IsNullOrWhiteSpace(title)
            ? null
            : title.Trim();
        PreviewPullRequestState = state?.Trim();
        PreviewPullRequestIsOpen = string.IsNullOrWhiteSpace(PreviewPullRequestState)
            ? null
            : string.Equals(PreviewPullRequestState, "OPEN", StringComparison.OrdinalIgnoreCase);
    }

    public void UpdatePullRequestMetadataPreview(string title, string author, string state)
    {
        UpdatePullRequestReviewState(title, state);

        if (string.IsNullOrWhiteSpace(title) &&
            string.IsNullOrWhiteSpace(author) &&
            string.IsNullOrWhiteSpace(state))
        {
            PullRequestMetadataText = string.Empty;
            return;
        }

        var normalizedTitle = string.IsNullOrWhiteSpace(title) ? "(no title)" : title.Trim();
        var normalizedAuthor = string.IsNullOrWhiteSpace(author) ? "Unknown author" : author.Trim();
        PullRequestMetadataText = $"Title: {normalizedTitle} | Author: {normalizedAuthor} | State: {PreviewPullRequestStateDisplay}";
    }

    public void ClearPullRequestPreview() =>
        UpdatePullRequestMetadataPreview(null, null, null);

    internal void ClearPullRequestReviewContext()
    {
        LatestPullRequest = null;
        ResetPullRequestReviewStampState();
        ClearPullRequestPreview();
    }

    internal void ApplyReviewWorkflowResult(MainWindowReviewWorkflowApplyResult applyResult)
    {
        if (applyResult == null)
            throw new ArgumentNullException(nameof(applyResult));

        LatestPullRequest = applyResult.PullRequest;
        ResetPullRequestReviewStampState();
        if (applyResult.Mode == MainWindowReviewPreparationMode.PullRequest)
        {
            UpdatePullRequestReviewState(
                applyResult.PullRequestTitle,
                applyResult.PullRequestState);

            var findings = applyResult.Report?.Findings ?? [];
            m_latestPullRequestFindingCount = findings.Count;
            m_latestPullRequestImportantFindingCount = findings.Count(finding => finding.Severity == CodeReviewFindingSeverity.Important);
        }

        var normalizedResolvedBaseBranch = LocalBaseBranchService.NormalizeBranchName(applyResult.ResolvedLocalBaseBranch);
        if (!string.IsNullOrWhiteSpace(normalizedResolvedBaseBranch))
        {
            AddLocalBaseBranchOption(normalizedResolvedBaseBranch);
            LocalBaseBranch = normalizedResolvedBaseBranch;
        }

        m_latestReviewWorktreePath = applyResult.ReviewWorktreePath;
        m_latestSolutionPath = applyResult.SolutionPath;
        LatestPullRequestBaseBranch = applyResult.PullRequestBaseBranch;
        OnActionStateChanged();
    }

    internal async Task<PullRequestPreviewResult> RefreshPullRequestPreviewAsync(
        PullRequestPreviewService pullRequestPreviewService,
        CancellationToken cancellationToken)
    {
        if (pullRequestPreviewService == null)
            throw new ArgumentNullException(nameof(pullRequestPreviewService));

        ClearPullRequestPreview();

        var previewResult = await pullRequestPreviewService.TryBuildPreviewAsync(
            IsPullRequestReviewMode,
            PullRequestUrl,
            cancellationToken);
        if (cancellationToken.IsCancellationRequested)
            return PullRequestPreviewResult.Empty;

        UpdatePullRequestMetadataPreview(
            previewResult.Metadata?.Title,
            previewResult.Metadata?.Author,
            previewResult.Metadata?.State);
        return previewResult;
    }

    internal void RefreshActionState(
        MainWindowActionStateService actionStateService,
        bool canCancelCurrentOperation,
        bool isCancellationRequested)
    {
        if (actionStateService == null)
            throw new ArgumentNullException(nameof(actionStateService));

        var actionState = actionStateService.BuildSnapshot(
            RepositoryRootPath,
            LocalRepositoryPath,
            LocalBaseBranchService.NormalizeBranchName(LocalBaseBranch),
            PullRequestUrl,
            IsAnyLocalReviewMode,
            IsLocalCommittedReviewMode,
            PreviewPullRequestIsOpen,
            PreviewPullRequestState,
            m_latestSolutionPath,
            m_latestReviewWorktreePath,
            canCancelCurrentOperation,
            isCancellationRequested);

        UpdateActionStateInputs(
            canReviewCurrentPullRequest: actionState.CanReviewCurrentPullRequest,
            hasValidPullRequestInput: actionState.HasValidPullRequestInput,
            hasValidPullRequestPrepareInputs: actionState.HasValidPullRequestPrepareInputs,
            hasValidLocalPrepareInputs: actionState.IsAnyLocalReviewMode && actionState.HasValidLocalPrepareInputs,
            hasAvailableSolution: actionState.HasAvailableSolution,
            canCancelCurrentOperation: actionState.CanCancelCurrentOperation,
            isCancellationRequested: actionState.IsCancellationRequested);
        m_latestSolutionPath = actionState.ResolvedSolutionPath;
    }

    internal async Task<bool> TryAutoDetectLocalBaseBranchAsync(
        LocalBaseBranchService localBaseBranchService,
        bool logWhenUpdated,
        Action<string> appendLog)
    {
        if (localBaseBranchService == null)
            throw new ArgumentNullException(nameof(localBaseBranchService));
        if (!IsGitAvailable)
            return false;

        var localRepositoryPath = LocalRepositoryPath?.Trim();
        if (!RepositoryUtilities.IsGitRepository(localRepositoryPath))
        {
            var fallbackBaseBranch = LocalBaseBranchService.NormalizeBranchName(LocalBaseBranch) ?? "main";
            SetLocalBaseBranchOptions([fallbackBaseBranch], fallbackBaseBranch);
            return false;
        }

        var currentBaseBranch = LocalBaseBranchService.NormalizeBranchName(LocalBaseBranch);
        var resolvedBaseBranch = await localBaseBranchService.ResolveLocalBaseBranchAsync(
            localRepositoryPath,
            currentBaseBranch,
            logWhenChanged: logWhenUpdated,
            appendLog);
        if (string.IsNullOrWhiteSpace(resolvedBaseBranch))
            return false;

        var branchOptions = await localBaseBranchService.GetLocalBaseBranchOptionsAsync(localRepositoryPath);
        if (!branchOptions.Any())
            branchOptions = [resolvedBaseBranch];

        if (!branchOptions.Any(branch => branch.Equals(resolvedBaseBranch, StringComparison.OrdinalIgnoreCase)))
            branchOptions.Add(resolvedBaseBranch);

        SetLocalBaseBranchOptions(branchOptions, resolvedBaseBranch);
        LocalBaseBranch = resolvedBaseBranch;
        return true;
    }

    internal bool TryApplyPullRequestUrlFromClipboard(string clipboardText)
    {
        if (!IsPullRequestReviewMode)
            return false;
        if (!string.IsNullOrWhiteSpace(PullRequestUrl))
            return false;
        if (!BitbucketPrUrlParser.TryParse(clipboardText, out var pullRequest, out _))
            return false;

        PullRequestUrl = pullRequest.SourceUrl;
        return true;
    }

    public bool TryParsePullRequestUrl(out BitbucketPullRequestReference pullRequest, out string parseError)
    {
        var prUrlText = m_pullRequestUrl?.Trim();
        return BitbucketPrUrlParser.TryParse(prUrlText, out pullRequest, out parseError);
    }

    public void ResetBusyProgress()
    {
        BusyProgressIsIndeterminate = true;
        BusyProgressMaximum = 1;
        BusyProgressValue = 0;
    }

    public void SetBusyProgressIndeterminate() =>
        ResetBusyProgress();

    public void UpdateBusyProgress(int completed, int total)
    {
        if (total <= 0)
        {
            SetBusyProgressIndeterminate();
            return;
        }

        BusyProgressIsIndeterminate = false;
        BusyProgressMaximum = total;
        BusyProgressValue = Math.Clamp(completed, 0, total);
    }

    private static int ResolveInitialReviewModeIndex(Settings settings)
    {
        var configuredIndex = settings.ReviewModeIndex;
        // Migrate the retired fifth mode to whole-repository review.
        if (configuredIndex == 4)
            return LocalRepositoryReviewModeIndex;
        if (configuredIndex is >= PullRequestReviewModeIndex and <= LocalRepositoryReviewModeIndex)
            return configuredIndex;

        return settings.UseLocalCommittedReview ? 1 : 0;
    }

    private static string NormalizeBaseBranch(string baseBranch) =>
        string.IsNullOrWhiteSpace(baseBranch)
            ? "main"
            : baseBranch.Trim();

    private static string NormalizeBranchOption(string baseBranch)
    {
        var normalizedBranch = baseBranch?.Trim();
        return string.IsNullOrWhiteSpace(normalizedBranch)
            ? null
            : normalizedBranch;
    }

    private static string NormalizePullRequestUrl(string value)
    {
        var normalizedValue = (value ?? string.Empty).Trim();
        if (!BitbucketPrUrlParser.TryParse(normalizedValue, out var pullRequest, out _))
            return normalizedValue;

        return pullRequest.SourceUrl;
    }

    private void ApplyLocalBaseBranchSelection(string selectedBranch)
    {
        var normalizedSelection = NormalizeBaseBranch(selectedBranch);
        var matchingOption = LocalBaseBranchOptions.FirstOrDefault(
            branch => branch.Equals(normalizedSelection, StringComparison.OrdinalIgnoreCase));
        var effectiveSelection = matchingOption ?? normalizedSelection;

        if (!SetField(ref m_localBaseBranch, effectiveSelection))
        {
            // Keep the selection tied to the current items source instance so ComboBox selection stays visible.
            m_localBaseBranch = effectiveSelection;
            OnPropertyChanged(nameof(LocalBaseBranch));
            return;
        }

        m_settings.LocalReviewBaseBranch = effectiveSelection;
        SaveSettingsSafely();
    }

    private void SaveSettingsSafely()
    {
        try
        {
            m_settings.Save();
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"Failed to persist settings: {exception}");
        }
    }

    private void OnReviewModeUiChanged()
    {
        OnPropertyChanged(nameof(ShowPullRequestInputs));
        OnPropertyChanged(nameof(IsPullRequestReviewMode));
        OnPropertyChanged(nameof(IsLocalCommittedReviewMode));
        OnPropertyChanged(nameof(IsLocalUncommittedReviewMode));
        OnPropertyChanged(nameof(IsLocalRepositoryReviewMode));
        OnPropertyChanged(nameof(IsAnyLocalReviewMode));
        OnPropertyChanged(nameof(CanChooseScanScope));
        OnPropertyChanged(nameof(ShowScanScopeOptions));
        OnPropertyChanged(nameof(ShowPullRequestMetadata));
        OnPropertyChanged(nameof(ShowLocalBaseBranch));
        OnPropertyChanged(nameof(PrepareReviewButtonText));
        OnPropertyChanged(nameof(ReviewModeInfoTooltip));
        OnPropertyChanged(nameof(ScanScopeInfoTooltip));
        OnPropertyChanged(nameof(IncludeFullModifiedFiles));
        RaiseCommandCanExecuteChanged();
    }

    private void OnActionStateChanged()
    {
        OnPropertyChanged(nameof(CanPrepareReview));
        OnPropertyChanged(nameof(CanOpenPullRequest));
        OnPropertyChanged(nameof(CanPostPullRequestReviewStamp));
        OnPropertyChanged(nameof(CanOpenSolution));
        OnPropertyChanged(nameof(CanOpenDiffLens));
        OnPropertyChanged(nameof(CanCancelProcessing));
        OnPropertyChanged(nameof(ShowCancelStoppingText));
        RaiseCommandCanExecuteChanged();
    }

    private void InitializeDisabledCommands()
    {
        m_browseRepositoryRootCommandImpl = new AsyncRelayCommand(_ => Task.CompletedTask, _ => false);
        m_prepareReviewCommandImpl = new AsyncRelayCommand(_ => Task.CompletedTask, _ => false);
        m_postPullRequestReviewStampCommandImpl = new AsyncRelayCommand(_ => Task.CompletedTask, _ => false);
        m_cancelProcessingCommandImpl = new RelayCommand(_ => { }, _ => false);
        m_openPullRequestCommandImpl = new RelayCommand(_ => { }, _ => false);
        m_openSolutionCommandImpl = new RelayCommand(_ => { }, _ => false);

        BrowseRepositoryRootCommand = m_browseRepositoryRootCommandImpl;
        PrepareReviewCommand = m_prepareReviewCommandImpl;
        PostPullRequestReviewStampCommand = m_postPullRequestReviewStampCommandImpl;
        CancelProcessingCommand = m_cancelProcessingCommandImpl;
        OpenPullRequestCommand = m_openPullRequestCommandImpl;
        OpenSolutionCommand = m_openSolutionCommandImpl;
    }

    private void RaiseCommandCanExecuteChanged()
    {
        m_browseRepositoryRootCommandImpl?.RaiseCanExecuteChanged();
        m_prepareReviewCommandImpl?.RaiseCanExecuteChanged();
        m_postPullRequestReviewStampCommandImpl?.RaiseCanExecuteChanged();
        m_cancelProcessingCommandImpl?.RaiseCanExecuteChanged();
        m_openPullRequestCommandImpl?.RaiseCanExecuteChanged();
        m_openSolutionCommandImpl?.RaiseCanExecuteChanged();
    }

    internal void RecordPostedInlineReviewComment()
    {
        m_latestPostedInlineCommentCount++;
        OnActionStateChanged();
    }

    internal void MarkPullRequestReviewStampPosted()
    {
        if (m_hasPostedPullRequestReviewStamp)
            return;

        m_hasPostedPullRequestReviewStamp = true;
        OnActionStateChanged();
    }

    internal int LatestPullRequestFindingCount => m_latestPullRequestFindingCount;

    internal int LatestPullRequestImportantFindingCount => m_latestPullRequestImportantFindingCount;

    internal int LatestPostedInlineCommentCount => m_latestPostedInlineCommentCount;

    private void ResetPullRequestReviewStampState()
    {
        m_latestPullRequestFindingCount = -1;
        m_latestPullRequestImportantFindingCount = 0;
        m_latestPostedInlineCommentCount = 0;
        m_hasPostedPullRequestReviewStamp = false;
        OnActionStateChanged();
    }

    private bool CurrentPullRequestMatchesLatestReview()
    {
        if (LatestPullRequest == null)
            return false;

        var normalizedCurrentUrl = NormalizePullRequestUrl(PullRequestUrl);
        return string.Equals(normalizedCurrentUrl, LatestPullRequest.SourceUrl, StringComparison.OrdinalIgnoreCase);
    }

    private static bool SetIfDifferent<T>(ref T field, T value)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        return true;
    }
}
