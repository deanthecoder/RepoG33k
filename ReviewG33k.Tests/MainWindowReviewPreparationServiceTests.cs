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
using DTC.Core.Extensions;
using ReviewG33k.Services;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class MainWindowReviewPreparationServiceTests
{
    [Test]
    public void ConstructorWhenInputValidationServiceIsNullThrows()
    {
        Assert.That(
            () => new MainWindowReviewPreparationService(
                null,
                new LocalBaseBranchService(new GitCommandRunner()),
                CreateReviewExecutionService()),
            Throws.ArgumentNullException);
    }

    [Test]
    public async Task PreparePullRequestReviewAsyncWhenUrlCannotBeParsedReturnsFailure()
    {
        using var tempRoot = new TempDirectory();
        var service = CreateService();
        var result = await service.PreparePullRequestReviewAsync(
            tempRoot.FullName,
            "not-a-valid-pr-url",
            includeFullModifiedFiles: false,
            appendLog: _ => { },
            updateBusyProgress: (_, _, _) => { },
            cancellationToken: default);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Is.Not.Null);
            Assert.That(result.Error?.DialogTitle, Is.EqualTo("Invalid pull request URL"));
            Assert.That(result.PullRequestExecutionResult, Is.Null);
        });
    }

    [Test]
    public async Task PrepareLocalUncommittedReviewAsyncWhenRepositoryDoesNotExistReturnsFailure()
    {
        var service = CreateService();
        using var tempRoot = new TempDirectory();
        var missingPath = tempRoot.GetDir("ReviewG33k-MissingRepo");
        var result = await service.PrepareLocalUncommittedReviewAsync(
            missingPath.FullName,
            includeFullModifiedFiles: false,
            appendLog: _ => { },
            updateBusyProgress: (_, _, _) => { },
            cancellationToken: default);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Error, Is.Not.Null);
            Assert.That(result.Error?.DialogTitle, Is.EqualTo("Local repository not found"));
            Assert.That(result.LocalExecutionResult, Is.Null);
        });
    }

    private static MainWindowReviewPreparationService CreateService() =>
        new(
            new MainWindowInputValidationService(),
            new LocalBaseBranchService(new GitCommandRunner()),
            CreateReviewExecutionService());

    private static ReviewExecutionService CreateReviewExecutionService()
    {
        var gitRunner = new GitCommandRunner();
        var metadataClient = new BitbucketPullRequestMetadataClient(new HttpClient(new StubHttpMessageHandler([])));
        return new ReviewExecutionService(
            gitRunner,
            new CodeReviewOrchestrator(gitRunner),
            new CodeSmellReportAnalyzer(gitRunner),
            metadataClient);
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
