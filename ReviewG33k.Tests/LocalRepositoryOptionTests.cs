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
using ReviewG33k.Models;

namespace ReviewG33k.Tests;

public sealed class LocalRepositoryOptionTests
{
    [Test]
    public void TopLevelRepositoryOmitsRootPrefix()
    {
        using var root = new TempDirectory();
        var path = root.GetDir("poc").FullName;
        var option = LocalRepositoryOption.Create(path, root.FullName);
        Assert.That(option.DisplayName, Is.EqualTo("poc"));
        Assert.That(option.FullPath, Is.EqualTo(path));
    }

    [Test]
    public void NestedRepositoryUsesRepositoryAncestorsRatherThanSourceFolders()
    {
        using var root = new TempDirectory();
        root.GetDir("poc/.git").Create();
        root.GetDir("poc/SPC/external/ApexRenderer/.git").Create();
        var path = root.GetDir("poc/SPC/external/ApexRenderer").FullName;
        var option = LocalRepositoryOption.Create(path, root.FullName);
        Assert.That(option.DisplayName, Is.EqualTo("poc › ApexRenderer"));
        Assert.That(option.AncestorLabel, Is.EqualTo("poc › "));
        Assert.That(option.RepositoryName, Is.EqualTo("ApexRenderer"));
    }

    [Test]
    public void NestedGitFileRepositoriesKeepTheirHierarchy()
    {
        using var root = new TempDirectory();
        root.GetDir("poc/.git").Create();
        root.GetDir("poc/external/ApexRenderer/packages").Create();
        root.GetFile("poc/external/ApexRenderer/.git").WriteAllText("gitdir: elsewhere");
        var path = root.GetDir("poc/external/ApexRenderer/packages").FullName;
        Assert.That(LocalRepositoryOption.Create(path, root.FullName).DisplayName,
            Is.EqualTo("poc › ApexRenderer › packages"));
    }

    [Test]
    public void RootRepositoryUsesItsNameAndOutsidePathRemainsUnambiguous()
    {
        using var root = new TempDirectory();
        Assert.That(LocalRepositoryOption.Create(root.FullName, root.FullName).DisplayName,
            Is.EqualTo(new DirectoryInfo(root.FullName).Name));
        using var outside = new TempDirectory();
        Assert.That(LocalRepositoryOption.Create(outside.FullName, root.FullName).DisplayName,
            Is.EqualTo(outside.FullName));
    }
}
