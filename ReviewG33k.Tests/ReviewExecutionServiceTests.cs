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
using System.Text;
using ReviewG33k.Models;
using ReviewG33k.Services;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class ReviewExecutionServiceTests
{
    [Test]
    public void ConstructorWhenGitRunnerIsNullThrows()
    {
        Assert.That(
            () => new ReviewExecutionService(
                null,
                new CodeReviewOrchestrator(new GitCommandRunner()),
                new CodeSmellReportAnalyzer(new GitCommandRunner()),
                new BitbucketPullRequestMetadataClient(new HttpClient(new StubHttpMessageHandler([])))),
            Throws.ArgumentNullException);
    }

    [Test]
    public async Task ExecutePullRequestReviewAsyncWhenPullRequestIsClosedReturnsWithoutPreparation()
    {
        using var handler = new StubHttpMessageHandler(
        [
            CreateJsonResponse(
                HttpStatusCode.OK,
                """
                {
                  "toRef": { "displayId": "main" },
                  "title": "Closed PR",
                  "author": { "user": { "displayName": "Dean" } },
                  "state": "DECLINED"
                }
                """)
        ]);
        using var httpClient = new HttpClient(handler);
        using var metadataClient = new BitbucketPullRequestMetadataClient(httpClient);
        var gitRunner = new GitCommandRunner();
        var service = new ReviewExecutionService(
            gitRunner,
            new CodeReviewOrchestrator(gitRunner),
            new CodeSmellReportAnalyzer(gitRunner),
            metadataClient);
        var pullRequest = new BitbucketPullRequestReference(
            "bitbucket.example.com",
            "PROJ",
            "sample-repo",
            42,
            "https://bitbucket.example.com/projects/PROJ/repos/sample-repo/pull-requests/42");

        var logMessages = new List<string>();
        var result = await service.ExecutePullRequestReviewAsync(
            repositoryRoot: @"C:\does-not-matter",
            pullRequest,
            includeFullModifiedFiles: false,
            appendLog: logMessages.Add,
            updateBusyProgress: (_, _, _) => { },
            cancellationToken: default);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsPullRequestOpen, Is.False);
            Assert.That(result.PrepareResult, Is.Null);
            Assert.That(result.Report, Is.Null);
            Assert.That(result.Metadata, Is.Not.Null);
            Assert.That(result.Metadata.State, Is.EqualTo("DECLINED"));
            Assert.That(logMessages, Is.Empty);
        });
    }

    private static HttpResponseMessage CreateJsonResponse(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json ?? string.Empty, Encoding.UTF8, "application/json")
        };

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
