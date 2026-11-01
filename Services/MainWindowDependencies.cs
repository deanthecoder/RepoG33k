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
using ReviewG33k.ViewModels;

namespace ReviewG33k.Services;

/// <summary>
/// Aggregates constructor dependencies required by <see cref="Views.MainWindow"/>.
/// </summary>
/// <remarks>
/// Useful for keeping the window constructor stable and explicit while allowing composition
/// and testing code to build and pass a single dependency object.
/// </remarks>
internal sealed class MainWindowDependencies
{
    public MainWindowDependencies(
        Settings settings,
        CodeLocationOpener codeLocationOpener,
        LogNavigationService logNavigationService,
        MainWindowLogFeedService logFeedService,
        ReviewFindingInteractionService reviewFindingInteractionService,
        PullRequestReviewStampService pullRequestReviewStampService,
        PullRequestUrlExtractionService pullRequestUrlExtractionService,
        MainWindowActionStateService actionStateService,
        LocalBaseBranchService localBaseBranchService,
        LocalFindingResampleService localFindingResampleService,
        PullRequestPreviewService pullRequestPreviewService,
        CodeSmellReportAnalyzer codeSmellReportAnalyzer,
        BitbucketPullRequestMetadataClient pullRequestMetadataClient,
        CodeReviewOrchestrator reviewOrchestrator,
        MainWindowStartupService startupService,
        MainWindowReviewWorkflowService reviewWorkflowService)
    {
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        CodeLocationOpener = codeLocationOpener ?? throw new ArgumentNullException(nameof(codeLocationOpener));
        LogNavigationService = logNavigationService ?? throw new ArgumentNullException(nameof(logNavigationService));
        LogFeedService = logFeedService ?? throw new ArgumentNullException(nameof(logFeedService));
        ReviewFindingInteractionService = reviewFindingInteractionService ?? throw new ArgumentNullException(nameof(reviewFindingInteractionService));
        PullRequestReviewStampService = pullRequestReviewStampService ?? throw new ArgumentNullException(nameof(pullRequestReviewStampService));
        PullRequestUrlExtractionService = pullRequestUrlExtractionService ?? throw new ArgumentNullException(nameof(pullRequestUrlExtractionService));
        ActionStateService = actionStateService ?? throw new ArgumentNullException(nameof(actionStateService));
        LocalBaseBranchService = localBaseBranchService ?? throw new ArgumentNullException(nameof(localBaseBranchService));
        LocalFindingResampleService = localFindingResampleService ?? throw new ArgumentNullException(nameof(localFindingResampleService));
        PullRequestPreviewService = pullRequestPreviewService ?? throw new ArgumentNullException(nameof(pullRequestPreviewService));
        CodeSmellReportAnalyzer = codeSmellReportAnalyzer ?? throw new ArgumentNullException(nameof(codeSmellReportAnalyzer));
        PullRequestMetadataClient = pullRequestMetadataClient ?? throw new ArgumentNullException(nameof(pullRequestMetadataClient));
        ReviewOrchestrator = reviewOrchestrator ?? throw new ArgumentNullException(nameof(reviewOrchestrator));
        StartupService = startupService ?? throw new ArgumentNullException(nameof(startupService));
        ReviewWorkflowService = reviewWorkflowService ?? throw new ArgumentNullException(nameof(reviewWorkflowService));
    }

    public Settings Settings { get; }

    public CodeLocationOpener CodeLocationOpener { get; }

    public LogNavigationService LogNavigationService { get; }

    public MainWindowLogFeedService LogFeedService { get; }

    public ReviewFindingInteractionService ReviewFindingInteractionService { get; }

    public PullRequestReviewStampService PullRequestReviewStampService { get; }

    public PullRequestUrlExtractionService PullRequestUrlExtractionService { get; }

    public MainWindowActionStateService ActionStateService { get; }

    public LocalBaseBranchService LocalBaseBranchService { get; }

    public LocalFindingResampleService LocalFindingResampleService { get; }

    public PullRequestPreviewService PullRequestPreviewService { get; }

    public CodeSmellReportAnalyzer CodeSmellReportAnalyzer { get; }

    public BitbucketPullRequestMetadataClient PullRequestMetadataClient { get; }

    public CodeReviewOrchestrator ReviewOrchestrator { get; }

    public MainWindowStartupService StartupService { get; }

    public MainWindowReviewWorkflowService ReviewWorkflowService { get; }
}
