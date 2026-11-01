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
public sealed class BitbucketPullRequestMetadataClientTests
{
    [Test]
    public async Task TryGetMergeCommitHashAsyncWhenMetadataContainsMergeCommitReturnsHash()
    {
        const string mergeCommit = "0a1b2c3d4e5f67890123456789abcdef01234567";
        using var handler = new StubHttpMessageHandler(
        [
            CreateJsonResponse(
                HttpStatusCode.OK,
                $$"""
                  {
                    "state": "MERGED",
                    "properties": {
                      "mergeCommit": {
                        "id": "{{mergeCommit}}"
                      }
                    }
                  }
                  """)
        ]);
        using var httpClient = new HttpClient(handler);
        var pullRequest = new BitbucketPullRequestReference(
            "bitbucket.example.com",
            "PROJ",
            "sample-repo",
            42,
            "https://bitbucket.example.com/projects/PROJ/repos/sample-repo/pull-requests/42");

        using var client = new BitbucketPullRequestMetadataClient(httpClient);
        var hash = await client.TryGetMergeCommitHashAsync(pullRequest);

        Assert.That(hash, Is.EqualTo(mergeCommit));
    }

    [Test]
    public async Task TryGetMergeCommitHashAsyncWhenMetadataMissingFallsBackToActivities()
    {
        const string mergeCommit = "abcdef0123456789abcdef0123456789abcdef01";
        using var handler = new StubHttpMessageHandler(
        [
            CreateJsonResponse(
                HttpStatusCode.OK,
                """
                {
                  "state": "MERGED",
                  "properties": { }
                }
                """),
            CreateJsonResponse(
                HttpStatusCode.OK,
                $$"""
                {
                  "values": [
                    {
                      "action": "COMMENTED"
                    },
                    {
                      "action": "MERGED",
                      "commit": {
                        "id": "{{mergeCommit}}"
                      }
                    }
                  ]
                }
                """)
        ]);
        using var httpClient = new HttpClient(handler);
        var pullRequest = new BitbucketPullRequestReference(
            "bitbucket.example.com",
            "PROJ",
            "sample-repo",
            42,
            "https://bitbucket.example.com/projects/PROJ/repos/sample-repo/pull-requests/42");

        using var client = new BitbucketPullRequestMetadataClient(httpClient);
        var hash = await client.TryGetMergeCommitHashAsync(pullRequest);

        Assert.That(hash, Is.EqualTo(mergeCommit));
    }

    [Test]
    public async Task TryAddInlineCommentAsyncWhenAddedLineAnchorFailsRetriesWithContextAnchor()
    {
        using var handler = new StubHttpMessageHandler(
        [
            CreateJsonResponse(HttpStatusCode.BadRequest, "{\"errors\":[{\"message\":\"Anchor line must be an added line.\"}]}"),
            CreateJsonResponse(HttpStatusCode.Created, "{}")
        ]);
        using var httpClient = new HttpClient(handler);
        var pullRequest = new BitbucketPullRequestReference(
            "bitbucket.example.com",
            "PROJ",
            "sample-repo",
            42,
            "https://bitbucket.example.com/projects/PROJ/repos/sample-repo/pull-requests/42");

        using var client = new BitbucketPullRequestMetadataClient(httpClient);
        var result = await client.TryAddInlineCommentAsync(pullRequest, "src\\Feature\\Foo.cs", 27, "Please simplify this branch.");

        Assert.That(result.Success, Is.True);
        Assert.That(handler.RequestBodies, Has.Count.EqualTo(2));
        Assert.That(handler.RequestBodies[0], Does.Contain("\"anchor\":"));
        Assert.That(handler.RequestBodies[0], Does.Contain("\"lineType\":\"ADDED\""));
        Assert.That(handler.RequestBodies[1], Does.Contain("\"anchor\":"));
        Assert.That(handler.RequestBodies[1], Does.Contain("\"lineType\":\"CONTEXT\""));
    }

    [Test]
    public async Task TryAddInlineCommentAsyncWhenInlineAnchorsFailFallsBackToPullRequestComment()
    {
        using var handler = new StubHttpMessageHandler(
        [
            CreateJsonResponse(HttpStatusCode.BadRequest, "{\"errors\":[{\"message\":\"Anchor line must be an added line.\"}]}"),
            CreateJsonResponse(HttpStatusCode.BadRequest, "{\"errors\":[{\"message\":\"Line is not available in diff context.\"}]}"),
            CreateJsonResponse(HttpStatusCode.Created, "{}")
        ]);
        using var httpClient = new HttpClient(handler);
        var pullRequest = new BitbucketPullRequestReference(
            "bitbucket.example.com",
            "PROJ",
            "sample-repo",
            42,
            "https://bitbucket.example.com/projects/PROJ/repos/sample-repo/pull-requests/42");

        using var client = new BitbucketPullRequestMetadataClient(httpClient);
        var result = await client.TryAddInlineCommentAsync(pullRequest, "src\\Feature\\Foo.cs", 27, "Please simplify this branch.");

        Assert.That(result.Success, Is.True);
        Assert.That(handler.RequestBodies, Has.Count.EqualTo(3));
        Assert.That(handler.RequestBodies[0], Does.Contain("\"lineType\":\"ADDED\""));
        Assert.That(handler.RequestBodies[1], Does.Contain("\"lineType\":\"CONTEXT\""));
        Assert.That(handler.RequestBodies[2], Does.Not.Contain("\"anchor\":"));
        Assert.That(handler.RequestBodies[2], Does.Contain("[src/Feature/Foo.cs:27] Please simplify this branch."));
    }

    [Test]
    public async Task TryAddInlineCommentAsyncWhenBadRequestIsNotAnchorRelatedDoesNotFallback()
    {
        using var handler = new StubHttpMessageHandler(
        [
            CreateJsonResponse(HttpStatusCode.BadRequest, "{\"errors\":[{\"message\":\"Comment text is too long.\"}]}")
        ]);
        using var httpClient = new HttpClient(handler);
        var pullRequest = new BitbucketPullRequestReference(
            "bitbucket.example.com",
            "PROJ",
            "sample-repo",
            42,
            "https://bitbucket.example.com/projects/PROJ/repos/sample-repo/pull-requests/42");

        using var client = new BitbucketPullRequestMetadataClient(httpClient);
        var result = await client.TryAddInlineCommentAsync(pullRequest, "src/Foo.cs", 8, "x");

        Assert.That(result.Success, Is.False);
        Assert.That(handler.RequestBodies, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task TryAddPullRequestCommentAsyncWhenCalledPostsGeneralCommentBody()
    {
        using var handler = new StubHttpMessageHandler(
        [
            CreateJsonResponse(HttpStatusCode.Created, "{}")
        ]);
        using var httpClient = new HttpClient(handler);
        var pullRequest = new BitbucketPullRequestReference(
            "bitbucket.example.com",
            "PROJ",
            "sample-repo",
            42,
            "https://bitbucket.example.com/projects/PROJ/repos/sample-repo/pull-requests/42");

        using var client = new BitbucketPullRequestMetadataClient(httpClient);
        var result = await client.TryAddPullRequestCommentAsync(pullRequest, "Reviewed with ReviewG33k.");

        Assert.That(result.Success, Is.True);
        Assert.That(handler.RequestBodies, Has.Count.EqualTo(1));
        Assert.That(handler.RequestBodies[0], Does.Not.Contain("\"anchor\":"));
        Assert.That(handler.RequestBodies[0], Does.Contain("\"text\":\"Reviewed with ReviewG33k.\""));
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

        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            RequestBodies.Add(body);

            return m_responses.Count > 0
                ? m_responses.Dequeue()
                : CreateJsonResponse(HttpStatusCode.Created, "{}");
        }
    }
}
