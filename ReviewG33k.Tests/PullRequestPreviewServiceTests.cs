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
using ReviewG33k.Services;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class PullRequestPreviewServiceTests
{
    [Test]
    public void ConstructorWhenMetadataClientIsNullThrows()
    {
        Assert.That(() => new PullRequestPreviewService(null), Throws.ArgumentNullException);
    }

    [Test]
    public async Task TryBuildPreviewAsyncWhenNotPullRequestModeReturnsEmpty()
    {
        using var metadataClient = CreateMetadataClient([]);
        var service = new PullRequestPreviewService(metadataClient);

        var result = await service.TryBuildPreviewAsync(
            isPullRequestReviewMode: false,
            pullRequestUrl: "https://bitbucket.example.com/projects/PROJ/repos/repo/pull-requests/1",
            cancellationToken: default);

        Assert.Multiple(() =>
        {
            Assert.That(result.PullRequest, Is.Null);
            Assert.That(result.Metadata, Is.Null);
        });
    }

    [Test]
    public async Task TryBuildPreviewAsyncWhenUrlIsInvalidReturnsEmpty()
    {
        using var metadataClient = CreateMetadataClient([]);
        var service = new PullRequestPreviewService(metadataClient);

        var result = await service.TryBuildPreviewAsync(
            isPullRequestReviewMode: true,
            pullRequestUrl: "not a pr url",
            cancellationToken: default);

        Assert.Multiple(() =>
        {
            Assert.That(result.PullRequest, Is.Null);
            Assert.That(result.Metadata, Is.Null);
        });
    }

    [Test]
    public async Task TryBuildPreviewAsyncWhenUrlIsValidReturnsMetadata()
    {
        using var metadataClient = CreateMetadataClient(
        [
            CreateJsonResponse(
                HttpStatusCode.OK,
                """
                {
                  "toRef": { "displayId": "main" },
                  "title": "Preview title",
                  "author": { "user": { "displayName": "Dean" } },
                  "state": "OPEN"
                }
                """)
        ]);
        var service = new PullRequestPreviewService(metadataClient);

        var result = await service.TryBuildPreviewAsync(
            isPullRequestReviewMode: true,
            pullRequestUrl: "https://bitbucket.example.com/projects/PROJ/repos/repo/pull-requests/27",
            cancellationToken: default);

        Assert.Multiple(() =>
        {
            Assert.That(result.PullRequest, Is.Not.Null);
            Assert.That(result.PullRequest.PullRequestId, Is.EqualTo(27));
            Assert.That(result.Metadata, Is.Not.Null);
            Assert.That(result.Metadata.Title, Is.EqualTo("Preview title"));
            Assert.That(result.Metadata.State, Is.EqualTo("OPEN"));
        });
    }

    private static BitbucketPullRequestMetadataClient CreateMetadataClient(IEnumerable<HttpResponseMessage> responses)
    {
        var httpClient = new HttpClient(new StubHttpMessageHandler(responses));
        return new BitbucketPullRequestMetadataClient(httpClient);
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
