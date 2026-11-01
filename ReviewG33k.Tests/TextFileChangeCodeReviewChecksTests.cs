// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Text;
using ReviewG33k.Services;
using ReviewG33k.Services.Checks;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class TextFileChangeCodeReviewChecksTests
{
    [Test]
    public void EncodingCheckWhenUtf8BomIsAddedReportsHint()
    {
        const string baselineText = "public sealed class Sample\n{\n}\n";
        const string currentText = baselineText;
        var currentBytes = Encoding.UTF8.GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(currentText))
            .ToArray();
        var file = CreateChangedFile(currentText, baselineText, currentBytes, Encoding.UTF8.GetBytes(baselineText));

        var report = Analyze(new FileEncodingChangedCodeReviewCheck(), file);

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.FileEncodingChanged));
        Assert.That(report.Findings[0].Message, Does.Contain("UTF-8"));
        Assert.That(report.Findings[0].Message, Does.Contain("BOM"));
    }

    [Test]
    public void NewlineCheckWhenLfChangesToCrlfReportsHint()
    {
        const string baselineText = "public sealed class Sample\n{\n}\n";
        const string currentText = "public sealed class Sample\r\n{\r\n}\r\n";
        var file = CreateChangedFile(currentText, baselineText);

        var report = Analyze(new FileNewlineChangedCodeReviewCheck(), file);

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.FileNewlineChanged));
        Assert.That(report.Findings[0].Message, Does.Contain("LF"));
        Assert.That(report.Findings[0].Message, Does.Contain("CRLF"));
    }

    [Test]
    public void NewlineCheckWhenContentChangesWithDifferentCheckoutNewlinesDoesNotReport()
    {
        const string baselineText = "public sealed class Sample\n{\n}\n";
        const string currentText = "public sealed class RenamedSample\r\n{\r\n}\r\n";
        var file = CreateChangedFile(currentText, baselineText);

        var report = Analyze(new FileNewlineChangedCodeReviewCheck(), file);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void TrailingWhitespaceOnlyCheckWhenOnlyTrailingSpacesChangeReportsHint()
    {
        const string baselineText = "public sealed class Sample\n{\n}\n";
        const string currentText = "public sealed class Sample   \n{\t\n}\n";
        var file = CreateChangedFile(currentText, baselineText, addedLines: [1, 2]);

        var report = Analyze(new TrailingWhitespaceOnlyChangeCodeReviewCheck(), file);

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.TrailingWhitespaceOnlyChange));
        Assert.That(report.Findings[0].LineNumber, Is.EqualTo(1));
    }

    [Test]
    public void TrailingWhitespaceOnlyCheckWhenContentAlsoChangesDoesNotReport()
    {
        const string baselineText = "public sealed class Sample\n{\n}\n";
        const string currentText = "public sealed class RenamedSample   \n{\n}\n";
        var file = CreateChangedFile(currentText, baselineText);

        var report = Analyze(new TrailingWhitespaceOnlyChangeCodeReviewCheck(), file);

        Assert.That(report.Findings, Is.Empty);
    }

    private static CodeSmellReport Analyze(ICodeReviewCheck check, CodeReviewChangedFile file)
    {
        var context = new CodeReviewAnalysisContext(
            [file],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            allChangedFiles: [file]);

        var report = new CodeSmellReport();
        check.Analyze(context, report);
        return report;
    }

    private static CodeReviewChangedFile CreateChangedFile(
        string currentText,
        string baselineText,
        byte[] currentBytes = null,
        byte[] baselineBytes = null,
        IEnumerable<int> addedLines = null)
    {
        var normalizedText = currentText ?? string.Empty;
        return new CodeReviewChangedFile(
            "M",
            "Services/Sample.cs",
            "Services/Sample.cs",
            normalizedText,
            normalizedText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'),
            new HashSet<int>(addedLines ?? [1]),
            baselineText,
            currentBytes ?? Encoding.UTF8.GetBytes(normalizedText),
            baselineBytes ?? Encoding.UTF8.GetBytes(baselineText ?? string.Empty));
    }
}
