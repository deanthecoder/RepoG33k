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

public sealed class RepositoryUtilitiesTests
{
    [Test]
    public void FindTopLevelSolutionFileWhenRootContainsSolutionPrefersTopLevelFile()
    {
        using var tempRoot = new TempDirectory();
        tempRoot.GetFile("Root.sln").WriteAllText(string.Empty);
        tempRoot.GetDir("src/App").Create();
        tempRoot.GetFile("src/App/Nested.sln").WriteAllText(string.Empty);

        var solutionPath = RepositoryUtilities.FindTopLevelSolutionFile(tempRoot.FullName);

        Assert.That(solutionPath, Is.EqualTo(tempRoot.GetFile("Root.sln").FullName));
    }

    [Test]
    public void FindTopLevelSolutionFileWhenOnlyBinContainsSolutionIgnoresGeneratedFolder()
    {
        using var tempRoot = new TempDirectory();
        tempRoot.GetDir("bin/Debug").Create();
        tempRoot.GetFile("bin/Debug/Generated.sln").WriteAllText(string.Empty);
        tempRoot.GetDir("src/App").Create();
        tempRoot.GetFile("src/App/Actual.sln").WriteAllText(string.Empty);

        var solutionPath = RepositoryUtilities.FindTopLevelSolutionFile(tempRoot.FullName);

        Assert.That(solutionPath, Is.EqualTo(tempRoot.GetFile("src/App/Actual.sln").FullName));
    }

    [Test]
    public void FindTopLevelSolutionFileWhenRootContainsSlnxPrefersItOverNestedSln()
    {
        using var tempRoot = new TempDirectory();
        tempRoot.GetFile("Root.slnx").WriteAllText(string.Empty);
        tempRoot.GetDir("src").Create();
        tempRoot.GetFile("src/Nested.sln").WriteAllText(string.Empty);

        var solutionPath = RepositoryUtilities.FindTopLevelSolutionFile(tempRoot.FullName);

        Assert.That(solutionPath, Is.EqualTo(tempRoot.GetFile("Root.slnx").FullName));
    }

    [Test]
    public void FindTopLevelSolutionFilePrefersShallowestSolutionThenAlphabeticalPath()
    {
        using var tempRoot = new TempDirectory();
        tempRoot.GetDir("a/deep").Create();
        tempRoot.GetDir("b").Create();
        tempRoot.GetFile("a/deep/First.sln").WriteAllText(string.Empty);
        tempRoot.GetFile("b/Zebra.sln").WriteAllText(string.Empty);
        tempRoot.GetFile("b/Alpha.slnx").WriteAllText(string.Empty);

        var solutionPath = RepositoryUtilities.FindTopLevelSolutionFile(tempRoot.FullName);

        Assert.That(solutionPath, Is.EqualTo(tempRoot.GetFile("b/Alpha.slnx").FullName));
    }

    [TestCase(".git")]
    [TestCase(".vs")]
    [TestCase(".idea")]
    [TestCase("bin")]
    [TestCase("obj")]
    [TestCase("dist")]
    [TestCase("node_modules")]
    public void FindTopLevelSolutionFileWhenOnlyExcludedFolderContainsSolutionReturnsNull(string folder)
    {
        using var tempRoot = new TempDirectory();
        tempRoot.GetDir($"{folder}/nested").Create();
        tempRoot.GetFile($"{folder}/nested/Generated.slnx").WriteAllText(string.Empty);

        Assert.That(RepositoryUtilities.FindTopLevelSolutionFile(tempRoot.FullName), Is.Null);
    }

}
