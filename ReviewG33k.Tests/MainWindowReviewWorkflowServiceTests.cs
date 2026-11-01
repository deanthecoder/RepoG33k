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
using ReviewG33k.Models;
using ReviewG33k.Services;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class MainWindowReviewWorkflowServiceTests
{
    [Test]
    public void ConstructorWhenReviewPreparationServiceIsNullThrows()
    {
        Assert.That(() => new MainWindowReviewWorkflowService(null), Throws.ArgumentNullException);
    }

    [Test]
    public void GetPrepareReviewStatusTextReturnsExpectedMessageForMode()
    {
        var service = CreateService();

        Assert.Multiple(() =>
        {
            Assert.That(service.GetPrepareReviewStatusText(isPullRequestReviewMode: true, isLocalCommittedReviewMode: false, isLocalRepositoryReviewMode: false), Is.EqualTo("Reviewing pull request..."));
            Assert.That(service.GetPrepareReviewStatusText(isPullRequestReviewMode: false, isLocalCommittedReviewMode: true, isLocalRepositoryReviewMode: false), Is.EqualTo("Reviewing local committed changes..."));
            Assert.That(service.GetPrepareReviewStatusText(isPullRequestReviewMode: false, isLocalCommittedReviewMode: false, isLocalRepositoryReviewMode: false), Is.EqualTo("Reviewing local uncommitted changes..."));
            Assert.That(service.GetPrepareReviewStatusText(isPullRequestReviewMode: false, isLocalCommittedReviewMode: false, isLocalRepositoryReviewMode: true), Is.EqualTo("Reviewing local repository files..."));
        });
    }

    [Test]
    public void BuildApplyResultWhenPullRequestClosedAndNoReportRequestsNonOpenNotice()
    {
        var service = CreateService();
        var preparationResult = CreatePullRequestPreparationResult(isOpen: false, report: null, state: "MERGED");

        var applyResult = service.BuildApplyResult(preparationResult, @"C:\repo");

        Assert.Multiple(() =>
        {
            Assert.That(applyResult.Mode, Is.EqualTo(MainWindowReviewPreparationMode.PullRequest));
            Assert.That(applyResult.NotifyNonOpenPullRequest, Is.True);
            Assert.That(applyResult.StatusMessage, Is.Null);
            Assert.That(applyResult.LogMessage, Is.Null);
            Assert.That(applyResult.HasReportFindings, Is.False);
        });
    }

    [Test]
    public void BuildApplyResultWhenPullRequestClosedWithFallbackReportEmitsHintAndReviewCompleteStatus()
    {
        var service = CreateService();
        var report = new CodeSmellReport();
        report.AddFinding(CodeReviewFindingSeverity.Hint, "R1000", "src/File.cs", 12, "Example finding");
        var preparationResult = CreatePullRequestPreparationResult(isOpen: false, report, state: "MERGED");

        var applyResult = service.BuildApplyResult(preparationResult, @"C:\repo");

        Assert.Multiple(() =>
        {
            Assert.That(applyResult.NotifyNonOpenPullRequest, Is.False);
            Assert.That(applyResult.StatusMessage, Is.EqualTo("Review complete."));
            Assert.That(applyResult.LogMessage, Is.EqualTo("HINT: Pull request is not OPEN. ReviewG33k analyzed the merge commit fallback."));
            Assert.That(applyResult.HasReportFindings, Is.True);
        });
    }

    [Test]
    public void BuildApplyResultWhenLocalCommittedBuildsCommittedCacheUpdate()
    {
        var service = CreateService();
        var changedFile = new CodeReviewChangedFile(
            "M",
            "src/File.cs",
            @"C:\repo\src\File.cs",
            "class C {}",
            ["class C {}"],
            new HashSet<int> { 1 });
        var sourceResult = new CodeReviewChangedFileSourceResult([changedFile], []);
        var report = new CodeSmellReport();
        var localExecutionResult = new LocalReviewExecutionResult(
            @"C:\repo",
            @"C:\repo\Repo.sln",
            sourceResult,
            report);
        var preparationResult = MainWindowReviewPreparationResult.Success(
            MainWindowReviewPreparationMode.LocalCommitted,
            pullRequest: null,
            resolvedBaseBranch: "main",
            pullRequestExecutionResult: null,
            localExecutionResult);

        var applyResult = service.BuildApplyResult(preparationResult, @"C:\repo");

        Assert.Multiple(() =>
        {
            Assert.That(applyResult.StatusMessage, Is.EqualTo("Local review complete."));
            Assert.That(applyResult.ResolvedLocalBaseBranch, Is.EqualTo("main"));
            Assert.That(applyResult.LocalFindingCacheUpdate, Is.Not.Null);
            Assert.That(applyResult.LocalFindingCacheUpdate?.RepositoryPath, Is.EqualTo(@"C:\repo"));
            Assert.That(applyResult.LocalFindingCacheUpdate?.BaseBranch, Is.EqualTo("main"));
            Assert.That(applyResult.LocalFindingCacheUpdate?.Mode, Is.EqualTo(LocalReviewResampleMode.Committed));
            Assert.That(applyResult.LocalFindingCacheUpdate?.Files, Is.Not.Null);
            Assert.That(applyResult.LocalFindingCacheUpdate?.Files.Count, Is.EqualTo(1));
        });
    }

    [Test]
    public void BuildApplyResultWhenLocalRepositoryLeavesCacheUpdateEmpty()
    {
        var service = CreateService();
        var report = new CodeSmellReport();
        var sourceResult = new CodeReviewChangedFileSourceResult([], []);
        var localExecutionResult = new LocalReviewExecutionResult(
            @"C:\repo",
            @"C:\repo\Repo.sln",
            sourceResult,
            report);
        var preparationResult = MainWindowReviewPreparationResult.Success(
            MainWindowReviewPreparationMode.LocalRepository,
            pullRequest: null,
            resolvedBaseBranch: null,
            pullRequestExecutionResult: null,
            localExecutionResult);

        var applyResult = service.BuildApplyResult(preparationResult, @"C:\repo");

        Assert.Multiple(() =>
        {
            Assert.That(applyResult.Mode, Is.EqualTo(MainWindowReviewPreparationMode.LocalRepository));
            Assert.That(applyResult.StatusMessage, Is.EqualTo("Repository review complete."));
            Assert.That(applyResult.LocalFindingCacheUpdate, Is.Null);
            Assert.That(applyResult.ResolvedLocalBaseBranch, Is.Null);
        });
    }

    [TestCase(true, "OPEN", "main")]
    [TestCase(false, "MERGED", "HEAD^")]
    public void PullRequestDiffLensUsesReviewedComparison(bool open, string state, string expectedBase)
    {
        var result = CreateService().BuildApplyResult(
            CreatePullRequestPreparationResult(open, new CodeSmellReport(), state), @"C:\unrelated");
        Assert.That(result.PullRequestBaseBranch, Is.EqualTo(expectedBase));
        Assert.That(result.ReviewWorktreePath, Is.EqualTo(@"C:\repo\CodeReview\PR98"));
    }

    private static MainWindowReviewWorkflowService CreateService()
    {
        var gitRunner = new GitCommandRunner();
        var metadataClient = new BitbucketPullRequestMetadataClient(new HttpClient(new StubHttpMessageHandler([])));
        var reviewExecutionService = new ReviewExecutionService(
            gitRunner,
            new CodeReviewOrchestrator(gitRunner),
            new CodeSmellReportAnalyzer(gitRunner),
            metadataClient);
        var preparationService = new MainWindowReviewPreparationService(
            new MainWindowInputValidationService(),
            new LocalBaseBranchService(gitRunner),
            reviewExecutionService);
        return new MainWindowReviewWorkflowService(preparationService);
    }

    private static MainWindowReviewPreparationResult CreatePullRequestPreparationResult(
        bool? isOpen,
        CodeSmellReport report,
        string state)
    {
        var pullRequest = new BitbucketPullRequestReference(
            "bitbucket.example.com",
            "PROJ",
            "repo",
            98,
            "https://bitbucket.example.com/projects/PROJ/repos/repo/pull-requests/98");
        var metadata = new BitbucketPullRequestMetadata("main", "Title", "Author", state);
        var prepareResult = new PrepareReviewResult(@"C:\repo", @"C:\repo\CodeReview\PR98", @"C:\repo\CodeReview\PR98\Repo.sln");
        var executionResult = new PullRequestReviewExecutionResult(metadata, isOpen, prepareResult, report);
        return MainWindowReviewPreparationResult.Success(
            MainWindowReviewPreparationMode.PullRequest,
            pullRequest,
            resolvedBaseBranch: null,
            executionResult,
            localExecutionResult: null);
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
