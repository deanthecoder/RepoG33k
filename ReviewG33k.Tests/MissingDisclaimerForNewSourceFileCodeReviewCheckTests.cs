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
using ReviewG33k.Services.Checks;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class MissingDisclaimerForNewSourceFileCodeReviewCheckTests
{
    [Test]
    public void AnalyzeWhenModifiedSourceHasDisclaimerAndAddedSourceDoesNotReportsHint()
    {
        var existingFile = CreateChangedFile(
            status: "M",
            path: "src/Existing.cs",
            """
            // Code authored by Dean Edis (DeanTheCoder).
            public class Existing {}
            """);
        var newFile = CreateChangedFile(
            status: "A",
            path: "web/app.js",
            "export const value = 1;");

        var report = AnalyzeWith([existingFile, newFile]);

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.MissingDisclaimerForNewSourceFile));
            Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
            Assert.That(report.Findings[0].FilePath, Is.EqualTo("web/app.js"));
            Assert.That(report.Findings[0].LineNumber, Is.EqualTo(1));
        });
    }

    [Test]
    public void AnalyzeWhenNoModifiedSourceContainsDisclaimerDoesNotReport()
    {
        var existingFile = CreateChangedFile(
            status: "M",
            path: "src/Existing.cs",
            "public class Existing {}");
        var newFile = CreateChangedFile(
            status: "A",
            path: "src/NewFile.cs",
            "public class NewFile {}");

        var report = AnalyzeWith([existingFile, newFile]);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenAddedSourceAlreadyHasDisclaimerDoesNotReport()
    {
        var existingFile = CreateChangedFile(
            status: "M",
            path: "src/Existing.cs",
            """
            // Copyright (c) 2026
            public class Existing {}
            """);
        var newFile = CreateChangedFile(
            status: "A",
            path: "native/module.cpp",
            """
            // SPDX-License-Identifier: MIT
            int main() { return 0; }
            """);

        var report = AnalyzeWith([existingFile, newFile]);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenAddedFileIsNotSourceDoesNotReport()
    {
        var existingFile = CreateChangedFile(
            status: "M",
            path: "src/Existing.cs",
            """
            // Code authored by Dean Edis (DeanTheCoder).
            public class Existing {}
            """);
        var markdownFile = CreateChangedFile(
            status: "A",
            path: "docs/notes.md",
            "# Notes");

        var report = AnalyzeWith([existingFile, markdownFile]);

        Assert.That(report.Findings, Is.Empty);
    }

    private static CodeSmellReport AnalyzeWith(IReadOnlyList<CodeReviewChangedFile> changedFiles)
    {
        var check = new MissingDisclaimerForNewSourceFileCodeReviewCheck();
        var report = new CodeSmellReport();
        var context = new CodeReviewAnalysisContext(
            files: [],
            addedTestFilesByName: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            allChangedFiles: changedFiles);

        check.Analyze(context, report);
        return report;
    }

    private static CodeReviewChangedFile CreateChangedFile(string status, string path, string text)
    {
        var normalizedText = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalizedText.Split('\n');

        return new CodeReviewChangedFile(
            status,
            path,
            path,
            normalizedText,
            lines,
            new HashSet<int>());
    }
}
