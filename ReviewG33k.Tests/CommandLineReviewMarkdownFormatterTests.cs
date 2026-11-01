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
public sealed class CommandLineReviewMarkdownFormatterTests
{
    [Test]
    public void FormatHelpUsesListInsteadOfTable()
    {
        var markdown = new CommandLineReviewMarkdownFormatter().FormatHelp();

        Assert.Multiple(() =>
        {
            Assert.That(markdown, Does.Contain("Options:"));
            Assert.That(markdown, Does.Contain("- `--help`, `/help`, `/?` shows this usage."));
            Assert.That(markdown, Does.Not.Contain("| Option | Description |"));
        });
    }

    [Test]
    public void FormatResultWhenFindingsExistIncludesExitUsefulDetails()
    {
        var options = CommandLineReviewOptions.Parse(["--cli", "--repo", "/tmp/sample"]);
        var report = new CodeSmellReport();
        report.AddFinding(
            CodeReviewFindingSeverity.Important,
            "empty-method",
            "/tmp/sample/Foo.cs",
            12,
            "Method `DoThing` is empty.");
        var result = new MainWindowReviewWorkflowApplyResult(
            MainWindowReviewPreparationMode.LocalUncommitted,
            null,
            null,
            null,
            "/tmp/sample",
            "/tmp/sample/Sample.sln",
            "Local review complete.",
            null,
            false,
            report,
            null,
            null);

        var markdown = new CommandLineReviewMarkdownFormatter().FormatResult(options, result);

        Assert.Multiple(() =>
        {
            Assert.That(markdown, Does.Contain("**1 finding(s) reported.**"));
            Assert.That(markdown, Does.Contain("| Location | Description |"));
            Assert.That(markdown, Does.Contain("| --- | :--- |"));
            Assert.That(markdown, Does.Not.Contain("| Category |"));
            Assert.That(markdown, Does.Not.Contain("| Severity |"));
            Assert.That(markdown, Does.Not.Contain("| Message |"));
            Assert.That(markdown, Does.Not.Contain("empty-method"));
            Assert.That(markdown, Does.Contain("/tmp/sample/Foo.cs:12"));
            Assert.That(markdown, Does.Contain("Method `DoThing` is empty."));
            Assert.That(markdown, Does.Not.Contain("## Log"));
        });
    }
}
