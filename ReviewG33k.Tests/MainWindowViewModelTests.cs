// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Net;
using DTC.Core;
using ReviewG33k.Models;
using ReviewG33k.Services;
using ReviewG33k.ViewModels;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class MainWindowViewModelTests
{
    [Test]
    public void DiffLensRequiresMatchingPreparedPullRequestWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        try
        {
            const string url = "https://bitbucket.example.com/projects/PROJ/repos/repo/pull-requests/42";
            var vm = new MainWindowViewModel(new Settings()) { ReviewModeIndex = 0, PullRequestUrl = url };
            Assert.That(vm.CanOpenDiffLens, Is.False);
            var changed = new List<string>();
            vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            vm.ApplyReviewWorkflowResult(new MainWindowReviewWorkflowApplyResult(
                MainWindowReviewPreparationMode.PullRequest,
                new BitbucketPullRequestReference("bitbucket.example.com", "PROJ", "repo", 42, url),
                "Example", "OPEN", root, null, "Complete", null, false, new CodeSmellReport(), null, null, "develop"));
            Assert.That(vm.CanOpenDiffLens, Is.True);
            Assert.That(vm.LatestPullRequestBaseBranch, Is.EqualTo("develop"));
            Assert.That(changed, Does.Contain(nameof(vm.CanOpenDiffLens)));
            vm.IsBusy = true;
            Assert.That(vm.CanOpenDiffLens, Is.False);
            vm.IsBusy = false;
            vm.PullRequestUrl = url.Replace("42", "43");
            Assert.That(vm.CanOpenDiffLens, Is.False);
            vm.PullRequestUrl = url;
            Assert.That(vm.CanOpenDiffLens, Is.True);
            vm.ClearPullRequestReviewContext();
            Assert.That(vm.CanOpenDiffLens, Is.False);
            vm.ReviewModeIndex = 2;
            Assert.That(vm.CanOpenDiffLens, Is.True);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public void RetiredSavedModeFallsBackToEntireRepository()
    {
        var viewModel = new MainWindowViewModel(new Settings { ReviewModeIndex = 4 });
        Assert.That(viewModel.IsLocalRepositoryReviewMode, Is.True);
        Assert.That(viewModel.ReviewModeIndex, Is.EqualTo(3));
        Assert.That(viewModel.ShowScanScopeOptions, Is.False);
    }

    [Test]
    public void RetiredModeAssignmentPersistsEntireRepositorySelection()
    {
        var settings = new Settings { ReviewModeIndex = 0 };
        var viewModel = new MainWindowViewModel(settings) { ReviewModeIndex = 4 };
        Assert.That(viewModel.ReviewModeIndex, Is.EqualTo(3));
        Assert.That(settings.ReviewModeIndex, Is.EqualTo(3));
    }

    [Test]
    public void ConstructorWhenSettingsIsNullThrows()
    {
        Assert.That(() => new MainWindowViewModel(null), Throws.ArgumentNullException);
    }

    [Test]
    public void ConstructorInitializesStateFromSettings()
    {
        var settings = new Settings
        {
            RepositoryRootPath = "  C:\\RepoRoot  ",
            LocalReviewRepositoryPath = "  C:\\LocalRepo  ",
            LocalReviewBaseBranch = "  ",
            ReviewModeIndex = 2,
            IncludeFullModifiedFiles = true
        };

        var viewModel = new MainWindowViewModel(settings);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.RepositoryRootPath, Is.EqualTo("C:\\RepoRoot"));
            Assert.That(viewModel.LocalRepositoryPath, Is.EqualTo("C:\\LocalRepo"));
            Assert.That(viewModel.LocalBaseBranch, Is.EqualTo("main"));
            Assert.That(viewModel.ReviewModeIndex, Is.EqualTo(2));
            Assert.That(viewModel.ScanScopeIndex, Is.EqualTo(1));
        });
    }

    [Test]
    public void ConstructorWhenReviewModeIndexIsOutOfRangeFallsBackToLegacyFlag()
    {
        var settings = new Settings
        {
            ReviewModeIndex = -1,
            UseLocalCommittedReview = true
        };

        var viewModel = new MainWindowViewModel(settings);

        Assert.That(viewModel.ReviewModeIndex, Is.EqualTo(1));
    }

    [Test]
    public void PullRequestUrlWhenSetToNullNormalizesToEmptyString()
    {
        var viewModel = new MainWindowViewModel(new Settings());
        viewModel.PullRequestUrl = null;
        Assert.That(viewModel.PullRequestUrl, Is.EqualTo(string.Empty));
    }

    [Test]
    public void PullRequestUrlWhenValidBitbucketUrlCanonicalizesValue()
    {
        var viewModel = new MainWindowViewModel(new Settings());
        viewModel.PullRequestUrl = " https://bitbucket.example.com/projects/PROJ/repos/sample-repo/pull-requests/42?foo=bar ";
        Assert.That(viewModel.PullRequestUrl, Is.EqualTo("https://bitbucket.example.com/projects/PROJ/repos/sample-repo/pull-requests/42"));
    }

    [Test]
    public void AddLocalBaseBranchOptionWhenDuplicateIgnoringCaseDoesNotAddAgain()
    {
        var viewModel = new MainWindowViewModel(new Settings());

        viewModel.SetLocalBaseBranchOptions(["main"], "main");
        viewModel.AddLocalBaseBranchOption("MAIN");
        viewModel.AddLocalBaseBranchOption("develop");

        Assert.That(viewModel.LocalBaseBranchOptions, Is.EqualTo(new[] { "main", "develop" }));
    }

    [Test]
    public void SetLocalBaseBranchOptionsWhenSelectionMissingAddsAndSelectsIt()
    {
        var viewModel = new MainWindowViewModel(new Settings());

        viewModel.SetLocalBaseBranchOptions(["main", "develop"], "release/1.0");

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.LocalBaseBranchOptions, Does.Contain("main"));
            Assert.That(viewModel.LocalBaseBranchOptions, Does.Contain("develop"));
            Assert.That(viewModel.LocalBaseBranchOptions, Does.Contain("release/1.0"));
            Assert.That(viewModel.LocalBaseBranch, Is.EqualTo("release/1.0"));
        });
    }

    [Test]
    public void SetLocalBaseBranchOptionsWhenSelectionTextMatchesOptionUsesOptionInstance()
    {
        var initialBranch = new string("main".ToCharArray());
        var refreshedBranch = new string("main".ToCharArray());
        var viewModel = new MainWindowViewModel(
            new Settings
            {
                LocalReviewBaseBranch = initialBranch
            });

        viewModel.SetLocalBaseBranchOptions([refreshedBranch], refreshedBranch);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.LocalBaseBranch, Is.EqualTo("main"));
            Assert.That(viewModel.LocalBaseBranchOptions, Is.EqualTo(new[] { "main" }));
            Assert.That(ReferenceEquals(viewModel.LocalBaseBranch, viewModel.LocalBaseBranchOptions[0]), Is.True);
        });
    }

    [Test]
    public void ReviewModeUiPropertiesWhenPullRequestModeIsSelectedShowPullRequestInputs()
    {
        var viewModel = new MainWindowViewModel(new Settings())
        {
            ReviewModeIndex = 0
        };

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.ShowPullRequestInputs, Is.True);
            Assert.That(viewModel.IsPullRequestReviewMode, Is.True);
            Assert.That(viewModel.IsAnyLocalReviewMode, Is.False);
            Assert.That(viewModel.ShowLocalBaseBranch, Is.False);
            Assert.That(viewModel.PrepareReviewButtonText, Is.EqualTo("Review PR"));
        });
    }

    [Test]
    public void ReviewModeUiPropertiesWhenLocalCommittedModeIsSelectedShowBaseBranch()
    {
        var viewModel = new MainWindowViewModel(new Settings())
        {
            ReviewModeIndex = 1
        };

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.ShowPullRequestInputs, Is.False);
            Assert.That(viewModel.IsLocalCommittedReviewMode, Is.True);
            Assert.That(viewModel.IsAnyLocalReviewMode, Is.True);
            Assert.That(viewModel.ShowLocalBaseBranch, Is.True);
            Assert.That(viewModel.PrepareReviewButtonText, Is.EqualTo("Review Local"));
        });
    }

    [Test]
    public void ReviewModeFlagsWhenLocalUncommittedSelectedReflectState()
    {
        var viewModel = new MainWindowViewModel(new Settings())
        {
            ReviewModeIndex = 2
        };

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsPullRequestReviewMode, Is.False);
            Assert.That(viewModel.IsLocalCommittedReviewMode, Is.False);
            Assert.That(viewModel.IsLocalUncommittedReviewMode, Is.True);
            Assert.That(viewModel.IsAnyLocalReviewMode, Is.True);
        });
    }

    [Test]
    public void ReviewModeFlagsWhenLocalRepositorySelectedReflectState()
    {
        var viewModel = new MainWindowViewModel(new Settings())
        {
            ReviewModeIndex = 3
        };

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsPullRequestReviewMode, Is.False);
            Assert.That(viewModel.IsLocalCommittedReviewMode, Is.False);
            Assert.That(viewModel.IsLocalUncommittedReviewMode, Is.False);
            Assert.That(viewModel.IsLocalRepositoryReviewMode, Is.True);
            Assert.That(viewModel.IsAnyLocalReviewMode, Is.True);
            Assert.That(viewModel.ShowLocalBaseBranch, Is.False);
        });
    }

    [Test]
    public void ShowPullRequestMetadataDependsOnModeAndMetadataText()
    {
        var viewModel = new MainWindowViewModel(new Settings())
        {
            ReviewModeIndex = 0
        };

        viewModel.PullRequestMetadataText = "Metadata";
        Assert.That(viewModel.ShowPullRequestMetadata, Is.True);

        viewModel.ReviewModeIndex = 1;
        Assert.That(viewModel.ShowPullRequestMetadata, Is.False);
    }

    [Test]
    public void UpdateActionStateInputsWhenPullRequestIsClosedDisablesPrepare()
    {
        var viewModel = new MainWindowViewModel(new Settings())
        {
            ReviewModeIndex = 0
        };

        viewModel.UpdateActionStateInputs(
            canReviewCurrentPullRequest: false,
            hasValidPullRequestInput: true,
            hasValidPullRequestPrepareInputs: true,
            hasValidLocalPrepareInputs: false,
            hasAvailableSolution: true,
            canCancelCurrentOperation: true,
            isCancellationRequested: false);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.CanPrepareReview, Is.False);
            Assert.That(viewModel.CanOpenPullRequest, Is.True);
            Assert.That(viewModel.CanOpenSolution, Is.True);
        });
    }

    [Test]
    public void UpdateActionStateInputsWhenLocalModeAndIdleEnablesPrepare()
    {
        var viewModel = new MainWindowViewModel(new Settings())
        {
            ReviewModeIndex = 1
        };

        viewModel.UpdateActionStateInputs(
            canReviewCurrentPullRequest: true,
            hasValidPullRequestInput: true,
            hasValidPullRequestPrepareInputs: true,
            hasValidLocalPrepareInputs: true,
            hasAvailableSolution: false,
            canCancelCurrentOperation: true,
            isCancellationRequested: false);

        Assert.That(viewModel.CanPrepareReview, Is.True);

        viewModel.IsBusy = true;
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.CanPrepareReview, Is.False);
            Assert.That(viewModel.CanCancelProcessing, Is.True);
        });
    }

    [Test]
    public void IsGitAvailableWhenFalseDisablesGitBackedPrepareReview()
    {
        var viewModel = new MainWindowViewModel(new Settings())
        {
            ReviewModeIndex = 1
        };

        viewModel.UpdateActionStateInputs(
            canReviewCurrentPullRequest: true,
            hasValidPullRequestInput: true,
            hasValidPullRequestPrepareInputs: true,
            hasValidLocalPrepareInputs: true,
            hasAvailableSolution: false,
            canCancelCurrentOperation: false,
            isCancellationRequested: false);
        Assert.That(viewModel.CanPrepareReview, Is.True);

        viewModel.IsGitAvailable = false;
        Assert.That(viewModel.CanPrepareReview, Is.False);
    }

    [Test]
    public void UpdateActionStateInputsWhenCancellationRequestedShowsStoppingText()
    {
        var viewModel = new MainWindowViewModel(new Settings())
        {
            ReviewModeIndex = 0,
            IsBusy = true
        };

        viewModel.UpdateActionStateInputs(
            canReviewCurrentPullRequest: true,
            hasValidPullRequestInput: true,
            hasValidPullRequestPrepareInputs: true,
            hasValidLocalPrepareInputs: false,
            hasAvailableSolution: false,
            canCancelCurrentOperation: true,
            isCancellationRequested: true);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.ShowCancelStoppingText, Is.True);
            Assert.That(viewModel.CanCancelProcessing, Is.False);
        });
    }

    [Test]
    public void UpdateBusyProgressWhenTotalPositiveSetsBoundedValues()
    {
        var viewModel = new MainWindowViewModel(new Settings());

        viewModel.UpdateBusyProgress(completed: 12, total: 10);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.BusyProgressIsIndeterminate, Is.False);
            Assert.That(viewModel.BusyProgressMaximum, Is.EqualTo(10));
            Assert.That(viewModel.BusyProgressValue, Is.EqualTo(10));
        });
    }

    [Test]
    public void SetBusyProgressIndeterminateResetsProgressFields()
    {
        var viewModel = new MainWindowViewModel(new Settings());
        viewModel.UpdateBusyProgress(completed: 3, total: 10);

        viewModel.SetBusyProgressIndeterminate();

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.BusyProgressIsIndeterminate, Is.True);
            Assert.That(viewModel.BusyProgressMaximum, Is.EqualTo(1));
            Assert.That(viewModel.BusyProgressValue, Is.EqualTo(0));
        });
    }

    [Test]
    public void ReviewModeInfoTooltipReflectsSelectedMode()
    {
        var viewModel = new MainWindowViewModel(new Settings())
        {
            ReviewModeIndex = 0
        };
        Assert.That(viewModel.ReviewModeInfoTooltip, Does.Contain("Bitbucket pull request"));

        viewModel.ReviewModeIndex = 1;
        Assert.That(viewModel.ReviewModeInfoTooltip, Does.Contain("committed changes"));

        viewModel.ReviewModeIndex = 2;
        Assert.That(viewModel.ReviewModeInfoTooltip, Does.Contain("uncommitted"));

        viewModel.ReviewModeIndex = 3;
        Assert.That(viewModel.ReviewModeInfoTooltip, Does.Contain("regardless of Git change state"));
    }

    [Test]
    public void ScanScopeInfoTooltipReflectsSelectedScope()
    {
        var viewModel = new MainWindowViewModel(new Settings())
        {
            ScanScopeIndex = 0
        };
        Assert.That(viewModel.ScanScopeInfoTooltip, Does.Contain("newly added lines"));
        Assert.That(viewModel.IncludeFullModifiedFiles, Is.False);

        viewModel.ScanScopeIndex = 1;
        Assert.That(viewModel.ScanScopeInfoTooltip, Does.Contain("entire modified files"));
        Assert.That(viewModel.IncludeFullModifiedFiles, Is.True);

        viewModel.ReviewModeIndex = 3;
        Assert.That(viewModel.CanChooseScanScope, Is.False);
        Assert.That(viewModel.ShowScanScopeOptions, Is.False);
        Assert.That(viewModel.IncludeFullModifiedFiles, Is.False);

        viewModel.ReviewModeIndex = 3;
        Assert.That(viewModel.CanChooseScanScope, Is.False);
        Assert.That(viewModel.ShowScanScopeOptions, Is.False);
        Assert.That(viewModel.ScanScopeInfoTooltip, Does.Contain("does not apply"));
        Assert.That(viewModel.IncludeFullModifiedFiles, Is.False);
    }

    [Test]
    public void StatusTextDefaultsToReady()
    {
        var viewModel = new MainWindowViewModel(new Settings());
        Assert.That(viewModel.StatusText, Is.EqualTo("Ready."));
    }

    [Test]
    public void StatusTextWhenSetToNullNormalizesToEmptyString()
    {
        var viewModel = new MainWindowViewModel(new Settings());
        viewModel.StatusText = null;
        Assert.That(viewModel.StatusText, Is.EqualTo(string.Empty));
    }

    [Test]
    public void UpdatePullRequestReviewStateSetsPreviewFieldsAndOpenFlag()
    {
        var viewModel = new MainWindowViewModel(new Settings());
        viewModel.UpdatePullRequestReviewState("  PR title  ", " open ");

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.PreviewPullRequestTitle, Is.EqualTo("PR title"));
            Assert.That(viewModel.PreviewPullRequestState, Is.EqualTo("open"));
            Assert.That(viewModel.PreviewPullRequestIsOpen, Is.True);
        });
    }

    [Test]
    public void UpdatePullRequestReviewStateWhenStateIsMissingSetsOpenFlagToNull()
    {
        var viewModel = new MainWindowViewModel(new Settings());
        viewModel.UpdatePullRequestReviewState("Title", null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.PreviewPullRequestTitle, Is.EqualTo("Title"));
            Assert.That(viewModel.PreviewPullRequestState, Is.Null);
            Assert.That(viewModel.PreviewPullRequestIsOpen, Is.Null);
        });
    }

    [Test]
    public void PreviewPullRequestStateDisplayWhenStateMissingReturnsNa()
    {
        var viewModel = new MainWindowViewModel(new Settings());
        viewModel.UpdatePullRequestReviewState("Title", null);
        Assert.That(viewModel.PreviewPullRequestStateDisplay, Is.EqualTo("N/A"));
    }

    [Test]
    public void UpdatePullRequestMetadataPreviewFormatsSummaryText()
    {
        var viewModel = new MainWindowViewModel(new Settings());
        viewModel.UpdatePullRequestMetadataPreview("  Title ", "  Dean ", " open ");

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.PreviewPullRequestTitle, Is.EqualTo("Title"));
            Assert.That(viewModel.PreviewPullRequestStateDisplay, Is.EqualTo("OPEN"));
            Assert.That(viewModel.PullRequestMetadataText, Is.EqualTo("Title: Title | Author: Dean | State: OPEN"));
        });
    }

    [Test]
    public void UpdatePullRequestMetadataPreviewWhenAllValuesMissingClearsSummaryText()
    {
        var viewModel = new MainWindowViewModel(new Settings());
        viewModel.UpdatePullRequestMetadataPreview("Title", "Dean", "OPEN");

        viewModel.UpdatePullRequestMetadataPreview(null, null, null);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.PullRequestMetadataText, Is.EqualTo(string.Empty));
            Assert.That(viewModel.PreviewPullRequestTitle, Is.Null);
            Assert.That(viewModel.PreviewPullRequestStateDisplay, Is.EqualTo("N/A"));
            Assert.That(viewModel.PreviewPullRequestIsOpen, Is.Null);
        });
    }

    [Test]
    public void TryParsePullRequestUrlWhenValidReturnsPullRequest()
    {
        var viewModel = new MainWindowViewModel(new Settings())
        {
            PullRequestUrl = "https://bitbucket.example.com/projects/PROJ/repos/sample-repo/pull-requests/42"
        };

        var parsed = viewModel.TryParsePullRequestUrl(out var pullRequest, out var parseError);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True);
            Assert.That(parseError, Is.Null.Or.Empty);
            Assert.That(pullRequest, Is.Not.Null);
            Assert.That(pullRequest.SourceUrl, Is.EqualTo("https://bitbucket.example.com/projects/PROJ/repos/sample-repo/pull-requests/42"));
        });
    }

    [Test]
    public void TryParsePullRequestUrlWhenInvalidReturnsFalse()
    {
        var viewModel = new MainWindowViewModel(new Settings())
        {
            PullRequestUrl = "not-a-valid-pr-url"
        };

        var parsed = viewModel.TryParsePullRequestUrl(out var pullRequest, out var parseError);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.False);
            Assert.That(pullRequest, Is.Null);
            Assert.That(parseError, Is.Not.Null.And.Not.Empty);
        });
    }

    [Test]
    public void RefreshActionStateWhenPullRequestIsMergedKeepsPrepareEnabled()
    {
        using var tempRoot = new TempDirectory();
        var settings = new Settings
        {
            RepositoryRootPath = tempRoot.FullName
        };
        var viewModel = new MainWindowViewModel(settings)
        {
            ReviewModeIndex = 0,
            PullRequestUrl = "https://bitbucket.example.com/projects/PROJ/repos/repo/pull-requests/19"
        };
        viewModel.UpdatePullRequestReviewState("Merged PR", "MERGED");

        viewModel.RefreshActionState(
            new MainWindowActionStateService(new MainWindowInputValidationService()),
            canCancelCurrentOperation: false,
            isCancellationRequested: false);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.LatestSolutionPath, Is.Null);
            Assert.That(viewModel.CanPrepareReview, Is.True);
        });
    }

    [Test]
    public void ApplyReviewWorkflowResultUpdatesLatestSessionState()
    {
        var viewModel = new MainWindowViewModel(new Settings());
        var pullRequest = new BitbucketPullRequestReference(
            "bitbucket.example.com",
            "PROJ",
            "repo",
            42,
            "https://bitbucket.example.com/projects/PROJ/repos/repo/pull-requests/42");
        var applyResult = new MainWindowReviewWorkflowApplyResult(
            MainWindowReviewPreparationMode.PullRequest,
            pullRequest,
            "Example PR",
            "OPEN",
            @"C:\repo\CodeReview\PR42",
            @"C:\repo\CodeReview\PR42\Repo.sln",
            "Review complete.",
            null,
            false,
            new CodeSmellReport(),
            null,
            null);

        viewModel.ApplyReviewWorkflowResult(applyResult);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.LatestPullRequest, Is.SameAs(pullRequest));
            Assert.That(viewModel.LatestReviewWorktreePath, Is.EqualTo(@"C:\repo\CodeReview\PR42"));
            Assert.That(viewModel.LatestSolutionPath, Is.EqualTo(@"C:\repo\CodeReview\PR42\Repo.sln"));
            Assert.That(viewModel.PreviewPullRequestTitle, Is.EqualTo("Example PR"));
            Assert.That(viewModel.PreviewPullRequestStateDisplay, Is.EqualTo("OPEN"));
        });
    }

    [Test]
    public async Task RefreshPullRequestPreviewAsyncWhenNotInPullRequestModeClearsPreview()
    {
        var viewModel = new MainWindowViewModel(new Settings())
        {
            ReviewModeIndex = 1,
            PullRequestUrl = "https://bitbucket.example.com/projects/PROJ/repos/repo/pull-requests/27"
        };
        viewModel.UpdatePullRequestMetadataPreview("Existing", "Dean", "OPEN");

        using var metadataClient = CreateMetadataClient([]);
        var previewService = new PullRequestPreviewService(metadataClient);
        var preview = await viewModel.RefreshPullRequestPreviewAsync(previewService, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(preview.PullRequest, Is.Null);
            Assert.That(preview.Metadata, Is.Null);
            Assert.That(viewModel.PullRequestMetadataText, Is.EqualTo(string.Empty));
        });
    }

    [Test]
    public void MarkGitAvailabilityCheckedReturnsTrueOnlyOnce()
    {
        var viewModel = new MainWindowViewModel(new Settings());

        var first = viewModel.MarkGitAvailabilityChecked();
        var second = viewModel.MarkGitAvailabilityChecked();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.True);
            Assert.That(second, Is.False);
        });
    }

    [Test]
    public void TryApplyPullRequestUrlFromClipboardWhenEligibleAppliesCanonicalUrl()
    {
        var viewModel = new MainWindowViewModel(new Settings())
        {
            ReviewModeIndex = 0
        };

        var applied = viewModel.TryApplyPullRequestUrlFromClipboard(
            " https://bitbucket.example.com/projects/PROJ/repos/sample-repo/pull-requests/42?foo=bar ");

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.True);
            Assert.That(viewModel.PullRequestUrl, Is.EqualTo("https://bitbucket.example.com/projects/PROJ/repos/sample-repo/pull-requests/42"));
        });
    }

    [Test]
    public void TryApplyPullRequestUrlFromClipboardWhenNotPullRequestModeDoesNotApply()
    {
        var viewModel = new MainWindowViewModel(new Settings())
        {
            ReviewModeIndex = 1
        };

        var applied = viewModel.TryApplyPullRequestUrlFromClipboard(
            "https://bitbucket.example.com/projects/PROJ/repos/sample-repo/pull-requests/42");

        Assert.Multiple(() =>
        {
            Assert.That(applied, Is.False);
            Assert.That(viewModel.PullRequestUrl, Is.EqualTo(string.Empty));
        });
    }

    [Test]
    public void ConfigureCommandsWhenPrepareIsAllowedEnablesPrepareReviewCommand()
    {
        var viewModel = new MainWindowViewModel(new Settings())
        {
            ReviewModeIndex = 1
        };
        viewModel.UpdateActionStateInputs(
            canReviewCurrentPullRequest: true,
            hasValidPullRequestInput: false,
            hasValidPullRequestPrepareInputs: false,
            hasValidLocalPrepareInputs: true,
            hasAvailableSolution: false,
            canCancelCurrentOperation: false,
            isCancellationRequested: false);

        viewModel.ConfigureCommands(
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => { },
            () => { },
            () => Task.CompletedTask);

        Assert.That(viewModel.PrepareReviewCommand.CanExecute(null), Is.True);
    }

    [Test]
    public async Task PrepareReviewCommandWhenExecutedRunsConfiguredAsyncDelegate()
    {
        var viewModel = new MainWindowViewModel(new Settings())
        {
            ReviewModeIndex = 1
        };
        viewModel.UpdateActionStateInputs(
            canReviewCurrentPullRequest: true,
            hasValidPullRequestInput: false,
            hasValidPullRequestPrepareInputs: false,
            hasValidLocalPrepareInputs: true,
            hasAvailableSolution: false,
            canCancelCurrentOperation: false,
            isCancellationRequested: false);

        var commandInvoked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.ConfigureCommands(
            () => Task.CompletedTask,
            () =>
            {
                commandInvoked.TrySetResult(true);
                return Task.CompletedTask;
            },
            () => Task.CompletedTask,
            () => { },
            () => { },
            () => Task.CompletedTask);

        viewModel.PrepareReviewCommand.Execute(null);
        var completed = await Task.WhenAny(commandInvoked.Task, Task.Delay(1000));
        Assert.That(completed, Is.EqualTo(commandInvoked.Task));
    }

    [Test]
    public void CancelProcessingCommandCanExecuteTracksCancellationState()
    {
        var viewModel = new MainWindowViewModel(new Settings())
        {
            ReviewModeIndex = 0,
            IsBusy = true
        };
        viewModel.ConfigureCommands(
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => Task.CompletedTask,
            () => { },
            () => { },
            () => Task.CompletedTask);

        viewModel.UpdateActionStateInputs(
            canReviewCurrentPullRequest: true,
            hasValidPullRequestInput: true,
            hasValidPullRequestPrepareInputs: true,
            hasValidLocalPrepareInputs: false,
            hasAvailableSolution: false,
            canCancelCurrentOperation: true,
            isCancellationRequested: false);
        Assert.That(viewModel.CancelProcessingCommand.CanExecute(null), Is.True);

        viewModel.UpdateActionStateInputs(
            canReviewCurrentPullRequest: true,
            hasValidPullRequestInput: true,
            hasValidPullRequestPrepareInputs: true,
            hasValidLocalPrepareInputs: false,
            hasAvailableSolution: false,
            canCancelCurrentOperation: true,
            isCancellationRequested: true);
        Assert.That(viewModel.CancelProcessingCommand.CanExecute(null), Is.False);
    }

    [Test]
    public void ApplyReviewWorkflowResultWhenPullRequestReviewCompletedEnablesPostPullRequestReviewStamp()
    {
        var viewModel = new MainWindowViewModel(new Settings())
        {
            ReviewModeIndex = 0,
            PullRequestUrl = "https://bitbucket.example.com/projects/PROJ/repos/repo/pull-requests/42"
        };
        var pullRequest = new BitbucketPullRequestReference(
            "bitbucket.example.com",
            "PROJ",
            "repo",
            42,
            "https://bitbucket.example.com/projects/PROJ/repos/repo/pull-requests/42");
        var report = new CodeSmellReport();
        report.AddFinding(CodeReviewFindingSeverity.Suggestion, "sample-rule", "src/Sample.cs", 8, "Suggestion");
        var applyResult = new MainWindowReviewWorkflowApplyResult(
            MainWindowReviewPreparationMode.PullRequest,
            pullRequest,
            "Example PR",
            "OPEN",
            @"C:\repo\CodeReview\PR42",
            @"C:\repo\CodeReview\PR42\Repo.sln",
            "Review complete.",
            null,
            false,
            report,
            null,
            null);

        viewModel.ApplyReviewWorkflowResult(applyResult);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.CanPostPullRequestReviewStamp, Is.True);
            Assert.That(viewModel.LatestPullRequestFindingCount, Is.EqualTo(1));
            Assert.That(viewModel.LatestPostedInlineCommentCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void RecordPostedInlineReviewCommentWhenCalledIncrementsCount()
    {
        var viewModel = new MainWindowViewModel(new Settings())
        {
            ReviewModeIndex = 0,
            PullRequestUrl = "https://bitbucket.example.com/projects/PROJ/repos/repo/pull-requests/42"
        };
        var pullRequest = new BitbucketPullRequestReference(
            "bitbucket.example.com",
            "PROJ",
            "repo",
            42,
            "https://bitbucket.example.com/projects/PROJ/repos/repo/pull-requests/42");
        var applyResult = new MainWindowReviewWorkflowApplyResult(
            MainWindowReviewPreparationMode.PullRequest,
            pullRequest,
            "Example PR",
            "OPEN",
            @"C:\repo\CodeReview\PR42",
            @"C:\repo\CodeReview\PR42\Repo.sln",
            "Review complete.",
            null,
            false,
            new CodeSmellReport(),
            null,
            null);

        viewModel.ApplyReviewWorkflowResult(applyResult);
        viewModel.RecordPostedInlineReviewComment();

        Assert.That(viewModel.LatestPostedInlineCommentCount, Is.EqualTo(1));
    }

    private static BitbucketPullRequestMetadataClient CreateMetadataClient(IEnumerable<HttpResponseMessage> responses)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(responses));
        return new BitbucketPullRequestMetadataClient(httpClient);
    }
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> m_responses;

        public StubHttpMessageHandler(IEnumerable<HttpResponseMessage> responses)
        {
            m_responses = new Queue<HttpResponseMessage>(responses ?? []);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (m_responses.Count == 0)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            return Task.FromResult(m_responses.Dequeue());
        }
    }
}
