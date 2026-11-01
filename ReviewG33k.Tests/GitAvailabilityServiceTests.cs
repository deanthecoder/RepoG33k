// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using ReviewG33k.Services;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class GitAvailabilityServiceTests
{
    [Test]
    public void ConstructorWhenRunnerIsNullThrows()
    {
        Assert.That(() => new GitAvailabilityService(null), Throws.ArgumentNullException);
    }

    [Test]
    public async Task CheckAvailabilityAsyncWhenGitReturnsSuccessMarksAvailable()
    {
        var service = new GitAvailabilityService(
            (_, _) => Task.FromResult(new GitCommandResult(0, "git version 2.47.0", string.Empty, "git --version")));

        var result = await service.CheckAvailabilityAsync(@"C:\repo");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsAvailable, Is.True);
            Assert.That(result.FailureDetails, Is.Null);
        });
    }

    [Test]
    public async Task CheckAvailabilityAsyncWhenGitFailsIncludesFailureDetails()
    {
        var service = new GitAvailabilityService(
            (_, _) => Task.FromResult(new GitCommandResult(1, string.Empty, "git: command not found", "git --version")));

        var result = await service.CheckAvailabilityAsync(@"C:\repo");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsAvailable, Is.False);
            Assert.That(result.FailureDetails, Does.Contain("command not found"));
        });
    }

    [Test]
    public async Task CheckAvailabilityAsyncWhenRunnerThrowsIncludesExceptionMessage()
    {
        var service = new GitAvailabilityService(
            (_, _) => throw new InvalidOperationException("boom"));

        var result = await service.CheckAvailabilityAsync(@"C:\repo");

        Assert.Multiple(() =>
        {
            Assert.That(result.IsAvailable, Is.False);
            Assert.That(result.FailureDetails, Is.EqualTo("boom"));
        });
    }
}
