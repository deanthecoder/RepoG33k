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
using DTC.Core.Extensions;
using ReviewG33k.Services;

namespace ReviewG33k.Tests;

public sealed class LocalRepositoryDiscoveryServiceTests
{
    [Test]
    public void DiscoverIncludesRootNestedRepositoriesAndGitFiles()
    {
        using var root = new TempDirectory();
        root.GetDir(".git").Create();
        root.GetDir("src/Child/.git").Create();
        root.GetDir("modules/Submodule").Create();
        root.GetFile("modules/Submodule/.git").WriteAllText("gitdir: ../../.git/modules/Submodule");
        root.GetDir("ordinary/source").Create();

        Assert.That(LocalRepositoryDiscoveryService.Discover(root.FullName), Is.EquivalentTo(new[]
        {
            root.FullName, root.GetDir("src/Child").FullName, root.GetDir("modules/Submodule").FullName
        }));
    }

    [Test]
    public void DiscoverSkipsGeneratedFoldersAndGitInternals()
    {
        using var root = new TempDirectory();
        root.GetDir("node_modules/Dependency/.git").Create();
        root.GetDir("bin/Output/.git").Create();
        root.GetDir("Project/.git/internal/.git").Create();
        root.GetDir("Project/External/Library/.git").Create();

        Assert.That(LocalRepositoryDiscoveryService.Discover(root.FullName), Is.EquivalentTo(new[]
        {
            root.GetDir("Project").FullName, root.GetDir("Project/External/Library").FullName
        }));
    }

    [Test]
    public void DiscoverMissingRootReturnsNoRepositories()
    {
        using var root = new TempDirectory();
        Assert.That(LocalRepositoryDiscoveryService.Discover(root.GetDir("missing").FullName), Is.Empty);
        Assert.That(LocalRepositoryDiscoveryService.Discover(string.Empty), Is.Empty);
    }

    [Test]
    public void DiscoverHonorsCancellationBetweenResults()
    {
        using var root = new TempDirectory();
        root.GetDir(".git").Create();
        root.GetDir("Child/.git").Create();
        using var cancellation = new CancellationTokenSource();
        using var results = LocalRepositoryDiscoveryService.Discover(root.FullName, cancellation.Token).GetEnumerator();
        Assert.That(results.MoveNext(), Is.True);
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => results.MoveNext());
    }
}
