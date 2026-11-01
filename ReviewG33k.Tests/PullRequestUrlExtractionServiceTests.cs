// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Text;
using Avalonia.Input;
using ReviewG33k.Services;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class PullRequestUrlExtractionServiceTests
{
    [Test]
    public void TryExtractPullRequestUrlWhenTextFormatContainsBitbucketUrlReturnsCanonicalUrl()
    {
        var service = new PullRequestUrlExtractionService();
        var dataObject = new FakeDataObject(
            new Dictionary<string, object>
            {
                [DataFormats.Text] = "https://bitbucket.example.com/projects/PROJ/repos/repo/pull-requests/42?foo=bar"
            });

        var parsed = service.TryExtractPullRequestUrl(dataObject, out var url);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True);
            Assert.That(url, Is.EqualTo("https://bitbucket.example.com/projects/PROJ/repos/repo/pull-requests/42"));
        });
    }

    [Test]
    public void TryExtractPullRequestUrlWhenUnicodeBytesContainBitbucketUrlReturnsCanonicalUrl()
    {
        var service = new PullRequestUrlExtractionService();
        var byteData = Encoding.Unicode.GetBytes("https://bitbucket.example.com/projects/PROJ/repos/repo/pull-requests/17");
        var dataObject = new FakeDataObject(
            new Dictionary<string, object>
            {
                ["custom"] = byteData
            });

        var parsed = service.TryExtractPullRequestUrl(dataObject, out var url);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True);
            Assert.That(url, Is.EqualTo("https://bitbucket.example.com/projects/PROJ/repos/repo/pull-requests/17"));
        });
    }

    [Test]
    public void TryExtractPullRequestUrlWhenNoSupportedUrlReturnsFalse()
    {
        var service = new PullRequestUrlExtractionService();
        var dataObject = new FakeDataObject(
            new Dictionary<string, object>
            {
                [DataFormats.Text] = "not a pull request"
            });

        var parsed = service.TryExtractPullRequestUrl(dataObject, out var url);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.False);
            Assert.That(url, Is.Null);
        });
    }

    private sealed class FakeDataObject : IDataObject
    {
        private readonly Dictionary<string, object> m_values;

        public FakeDataObject(Dictionary<string, object> values)
        {
            m_values = values ?? [];
        }

        public IEnumerable<string> GetDataFormats() => m_values.Keys;

        public bool Contains(string dataFormat) => m_values.ContainsKey(dataFormat);

        public object Get(string dataFormat) => m_values.TryGetValue(dataFormat, out var value) ? value : null;
    }
}
