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
using ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Tests;

public sealed class RepositoryTestFileSearchTests
{
    [TestCase(".git")]
    [TestCase(".vs")]
    [TestCase(".idea")]
    [TestCase("bin")]
    [TestCase("obj")]
    [TestCase("dist")]
    [TestCase("node_modules")]
    public void FolderSearchIgnoresGeneratedTreesAndFindsRealTests(string excludedFolder)
    {
        using var root = new TempDirectory();
        root.GetDir($"{excludedFolder}/Tests").Create();
        root.GetFile($"{excludedFolder}/Tests/GeneratedTests.cs").WriteAllText("generated");
        root.GetDir("src/Tests").Create();
        root.GetFile("src/Tests/RealTests.cs").WriteAllText("test source");
        root.GetFile("src/Tests/Generated.g.cs").WriteAllText("generated");
        root.GetFile("src/Tests/notes.txt").WriteAllText("not C#");

        var files = RepositoryTestFileSearch.Enumerate(root.FullName);

        Assert.That(files.Select(file => file.FullName), Is.EqualTo(new[] { root.GetFile("src/Tests/RealTests.cs").FullName }));
    }

    [Test]
    public void FolderSearchDoesNotFollowSymlinksOrDirectoryLoops()
    {
        if (OperatingSystem.IsWindows())
            Assert.Ignore("Creating symlinks requires additional privileges on Windows.");
        using var root = new TempDirectory();
        using var external = new TempDirectory();
        external.GetFile("ExternalTests.cs").WriteAllText("external test");
        root.GetFile("RealTests.cs").WriteAllText("real test");
        Directory.CreateSymbolicLink(root.GetDir("linked-tests").FullName, external.FullName);
        Directory.CreateSymbolicLink(root.GetDir("loop").FullName, root.FullName);

        var files = RepositoryTestFileSearch.Enumerate(root.FullName);

        Assert.That(files.Select(file => file.Name), Is.EqualTo(new[] { "RealTests.cs" }));
    }

    [Test]
    public async Task GitSearchIncludesTrackedAndUntrackedTestsAndRespectsGitignore()
    {
        using var root = new TempDirectory();
        var git = new GitCommandRunner();
        Assert.That((await git.RunAsync(root.FullName, "init")).IsSuccess, Is.True);
        root.GetDir("Tests").Create();
        root.GetFile("Tests/TrackedTests.cs").WriteAllText("tracked");
        Assert.That((await git.RunAsync(root.FullName, "add", "Tests/TrackedTests.cs")).IsSuccess, Is.True);
        root.GetFile("Tests/UntrackedTests.cs").WriteAllText("untracked");
        root.GetFile(".gitignore").WriteAllText(".cache/\n");
        root.GetDir(".cache/Tests").Create();
        root.GetFile(".cache/Tests/IgnoredTests.cs").WriteAllText("ignored");

        var files = RepositoryTestFileSearch.Enumerate(root.FullName);

        Assert.That(files.Select(file => file.Name), Is.EquivalentTo(new[] { "TrackedTests.cs", "UntrackedTests.cs" }));
    }
}
