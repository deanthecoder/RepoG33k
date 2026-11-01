// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software.

using RepoG33k.Services;
using ReviewG33k.Models;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class RepositoryBranchStatusTests
{
    [Test]
    public void SlowerBackgroundReadCannotReplaceANewerRepositorySnapshot()
    {
        var item = new RepositoryPickerItem(new LocalRepositoryOption("/repo", "repo"));
        var newer = new RepositoryBranchStatus("feature/new", "origin/feature/new", 2, 0, null, DateTimeOffset.UtcNow);
        item.Update(newer);
        item.Update(newer with { Branch = "main", Ahead = 0, CheckedAt = newer.CheckedAt.AddSeconds(-5) });
        Assert.That(item.Badge, Is.EqualTo("↑ 2 Ahead"));
        Assert.That(item.Status.Branch, Is.EqualTo("feature/new"));
        Assert.That(item.Description, Does.Contain("Fetch time unknown"));
        Assert.That(item.Description, Does.Contain("fetch to check the server"));
    }
}
