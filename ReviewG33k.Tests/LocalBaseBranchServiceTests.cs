// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Core;
using ReviewG33k.Services;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class LocalBaseBranchServiceTests
{
    [Test]
    public void ConstructorWhenRunnerIsNullThrows()
    {
        Assert.That(() => new LocalBaseBranchService(null), Throws.ArgumentNullException);
    }

    [TestCase("main", "main")]
    [TestCase("origin/main", "main")]
    [TestCase("refs/remotes/origin/main", "main")]
    [TestCase("refs/heads/feature/test", "feature/test")]
    [TestCase("remotes/origin/release/1.2.3", "release/1.2.3")]
    public void NormalizeBranchNameWhenValidReturnsNormalizedName(string raw, string expected)
    {
        Assert.That(LocalBaseBranchService.NormalizeBranchName(raw), Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("HEAD")]
    [TestCase("origin/HEAD")]
    [TestCase("origin")]
    public void NormalizeBranchNameWhenInvalidReturnsNull(string raw)
    {
        Assert.That(LocalBaseBranchService.NormalizeBranchName(raw), Is.Null);
    }

    [Test]
    public async Task ResolveLocalBaseBranchAsyncWhenNotGitRepositoryReturnsTrimmedRequestedBranch()
    {
        var service = new LocalBaseBranchService(new GitCommandRunner());
        using var tempDirectory = new TempDirectory();

        var resolved = await service.ResolveLocalBaseBranchAsync(
            tempDirectory.FullName,
            " main ",
            logWhenChanged: true,
            _ => { });

        Assert.That(resolved, Is.EqualTo("main"));
    }
}
