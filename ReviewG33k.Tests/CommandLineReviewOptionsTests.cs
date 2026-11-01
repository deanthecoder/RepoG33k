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
public sealed class CommandLineReviewOptionsTests
{
    [Test]
    public void IsCommandLineReviewWhenCliSwitchIsPresentReturnsTrue()
    {
        Assert.That(CommandLineReviewOptions.IsCommandLineReview(["--cli"]), Is.True);
    }

    [Test]
    public void IsCommandLineReviewWhenWindowsHelpSwitchIsPresentReturnsTrue()
    {
        Assert.That(CommandLineReviewOptions.IsCommandLineReview(["/?"]), Is.True);
    }

    [Test]
    public void ParseWhenHelpSwitchIsProvidedReturnsHelpOptions()
    {
        var options = CommandLineReviewOptions.Parse(["/help"]);

        Assert.Multiple(() =>
        {
            Assert.That(options.ShouldRun, Is.True);
            Assert.That(options.ShowHelp, Is.True);
            Assert.That(options.Error, Is.Null);
        });
    }

    [Test]
    public void ParseWhenRepositoryAndCommittedModeAreProvidedReturnsOptions()
    {
        var options = CommandLineReviewOptions.Parse([
            "--cli",
            "--repo=/tmp/sample",
            "--mode",
            "committed",
            "--base",
            "develop",
            "--full"
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(options.ShouldRun, Is.True);
            Assert.That(options.Mode, Is.EqualTo(CommandLineReviewMode.Committed));
            Assert.That(options.RepositoryPath, Is.EqualTo(Path.GetFullPath("/tmp/sample")));
            Assert.That(options.BaseBranch, Is.EqualTo("develop"));
            Assert.That(options.IncludeFullModifiedFiles, Is.True);
            Assert.That(options.Error, Is.Null);
        });
    }

    [Test]
    public void ParseWhenWindowsStyleOptionsAreProvidedReturnsOptions()
    {
        var options = CommandLineReviewOptions.Parse([
            "/cli",
            "/repo",
            "/tmp/sample",
            "/mode",
            "committed",
            "/base",
            "develop",
            "/full"
        ]);

        Assert.Multiple(() =>
        {
            Assert.That(options.ShouldRun, Is.True);
            Assert.That(options.Mode, Is.EqualTo(CommandLineReviewMode.Committed));
            Assert.That(options.RepositoryPath, Is.EqualTo(Path.GetFullPath("/tmp/sample")));
            Assert.That(options.BaseBranch, Is.EqualTo("develop"));
            Assert.That(options.IncludeFullModifiedFiles, Is.True);
            Assert.That(options.Error, Is.Null);
        });
    }

    [Test]
    public void ParseWhenModeIsUnknownReturnsError()
    {
        var options = CommandLineReviewOptions.Parse(["--cli", "--mode", "surprise"]);

        Assert.That(options.Error, Does.Contain("Unknown review mode"));
    }

    [Test]
    public void ParseWhenJsonRequestHasAnErrorRetainsJsonOutput()
    {
        var options = CommandLineReviewOptions.Parse(["--json", "--mode", "surprise"]);

        Assert.Multiple(() =>
        {
            Assert.That(options.Error, Does.Contain("Unknown review mode"));
            Assert.That(options.OutputFormat, Is.EqualTo(CommandLineOutputFormat.Json));
        });
    }

    [Test]
    public void ParseWhenTreeModeIsProvidedReturnsTreeMode()
    {
        var options = CommandLineReviewOptions.Parse(["--cli", "--mode", "tree"]);

        Assert.That(options.Mode, Is.EqualTo(CommandLineReviewMode.Tree));
    }

    [Test]
    public void ParseWhenJsonSwitchIsProvidedReturnsJsonOutput()
    {
        var options = CommandLineReviewOptions.Parse(["--json"]);

        Assert.Multiple(() =>
        {
            Assert.That(options.ShouldRun, Is.True);
            Assert.That(options.OutputFormat, Is.EqualTo(CommandLineOutputFormat.Json));
            Assert.That(options.Mode, Is.EqualTo(CommandLineReviewMode.Uncommitted));
        });
    }

    [TestCase("console", "Console")]
    [TestCase("json", "Json")]
    public void ParseWhenOutputFormatIsProvidedReturnsRequestedFormat(string value, string expected)
    {
        var options = CommandLineReviewOptions.Parse(["--cli", "--format", value]);

        Assert.That(options.OutputFormat.ToString(), Is.EqualTo(expected));
    }
}
