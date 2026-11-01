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

[TestFixture]
public sealed class LogNavigationServiceTests
{
    [Test]
    public void TryParseLogLocationWhenValidReturnsPathAndLine()
    {
        var service = new LogNavigationService();

        var parsed = service.TryParseLogLocation(
            "[12:34:56] WARNING: [src/Foo.cs:27] Something happened",
            out var filePath,
            out var lineNumber);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True);
            Assert.That(filePath, Is.EqualTo("src/Foo.cs"));
            Assert.That(lineNumber, Is.EqualTo(27));
        });
    }

    [Test]
    public void TryParseLogLocationWhenMissingLocationReturnsFalse()
    {
        var service = new LogNavigationService();
        var parsed = service.TryParseLogLocation("No location here", out _, out _);
        Assert.That(parsed, Is.False);
    }

    [Test]
    public void TryResolveLogFileWhenAbsolutePathExistsReturnsFile()
    {
        var service = new LogNavigationService();
        using var tempFile = new TempFile(".txt");
        tempFile.WriteAllText("sample");

        var resolved = service.TryResolveLogFile(tempFile.FullName, null, out var fileInfo);
        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.True);
            Assert.That(fileInfo, Is.Not.Null);
            Assert.That(fileInfo.FullName, Is.EqualTo(tempFile.FullName));
        });
    }

    [Test]
    public void TryResolveLogFileWhenRelativePathUsesReviewWorktree()
    {
        var service = new LogNavigationService();
        using var tempDirectory = new TempDirectory();
        var sourceDirectory = tempDirectory.GetDir("src");
        sourceDirectory.Create();
        var sourceFile = sourceDirectory.GetFile("Bar.cs").WriteAllText("class Bar {}");

        var resolved = service.TryResolveLogFile("src/Bar.cs", tempDirectory.FullName, out var fileInfo);
        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.True);
            Assert.That(fileInfo, Is.Not.Null);
            Assert.That(fileInfo.FullName, Is.EqualTo(sourceFile.FullName));
        });
    }
}
