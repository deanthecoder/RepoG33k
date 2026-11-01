// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Newtonsoft.Json.Linq;
using ReviewG33k.Services;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class CommandLineReviewJsonFormatterTests
{
    [Test]
    public void FormatResultIncludesVersionedFindingDetails()
    {
        var options = CommandLineReviewOptions.Parse([
            "--json", "--repo", "/tmp/sample", "--mode", "committed", "--base", "main"
        ]);
        var report = new CodeSmellReport();
        report.AddFinding(
            CodeReviewFindingSeverity.Important,
            "empty-method",
            "/tmp/sample/Foo.cs",
            12,
            "Method `DoThing` is empty.");
        var result = new MainWindowReviewWorkflowApplyResult(
            MainWindowReviewPreparationMode.LocalCommitted,
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
            "develop");

        var json = new CommandLineReviewJsonFormatter().FormatResult(options, result);
        var document = JObject.Parse(json);

        Assert.Multiple(() =>
        {
            Assert.That(document["schemaVersion"]?.Value<int>(), Is.EqualTo(1));
            Assert.That(document["mode"]?.Value<string>(), Is.EqualTo("committed"));
            Assert.That(document["baseBranch"]?.Value<string>(), Is.EqualTo("develop"));
            Assert.That(document["summary"]?["findingCount"]?.Value<int>(), Is.EqualTo(1));
            Assert.That(document["findings"]?[0]?["ruleId"]?.Value<string>(), Is.EqualTo("empty-method"));
            Assert.That(document["findings"]?[0]?["severity"]?.Value<string>(), Is.EqualTo("important"));
            Assert.That(document["findings"]?[0]?["category"]?.Value<string>(), Is.EqualTo("Correctness"));
            Assert.That(document["findings"]?[0]?["file"]?.Value<string>(), Is.EqualTo("/tmp/sample/Foo.cs"));
            Assert.That(document["findings"]?[0]?["line"]?.Value<int>(), Is.EqualTo(12));
        });
    }

    [Test]
    public void FormatErrorProducesVersionedJson()
    {
        var document = JObject.Parse(new CommandLineReviewJsonFormatter().FormatError("No repository."));

        Assert.Multiple(() =>
        {
            Assert.That(document["schemaVersion"]?.Value<int>(), Is.EqualTo(1));
            Assert.That(document["error"]?.Value<string>(), Is.EqualTo("No repository."));
        });
    }
}
