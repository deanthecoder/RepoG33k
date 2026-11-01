// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Core.Extensions;
using ReviewG33k.Services;
using ReviewG33k.Services.Checks;

namespace ReviewG33k.Tests;

public sealed class ResxCodeReviewChecksTests
{
    [Test]
    public void MissingLocaleKeysCheckWhenLocaleFileOmitsBaseKeysReportsFinding()
    {
        const string baseResx = """
            <?xml version="1.0" encoding="utf-8"?>
            <root>
              <data name="Hello">
                <value>Hello</value>
              </data>
              <data name="Bye">
                <value>Bye</value>
              </data>
            </root>
            """;

        const string localeResx = """
            <?xml version="1.0" encoding="utf-8"?>
            <root>
              <data name="Hello">
                <value>Bonjour</value>
              </data>
            </root>
            """;

        var baseFile = CreateResxChangedFile("M", "Resources/Strings.resx", baseResx);
        var localeFile = CreateResxChangedFile("A", "Resources/Strings.fr.resx", localeResx);

        var report = Analyze(new ResxMissingLocaleKeysCodeReviewCheck(), baseFile, localeFile);

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Message, Does.Contain("`Bye`"));
    }

    [Test]
    public void UnexpectedExtraKeysCheckWhenLocaleContainsUnknownKeysReportsFinding()
    {
        const string baseResx = """
            <?xml version="1.0" encoding="utf-8"?>
            <root>
              <data name="Hello">
                <value>Hello</value>
              </data>
            </root>
            """;

        const string localeResx = """
            <?xml version="1.0" encoding="utf-8"?>
            <root>
              <data name="Hello">
                <value>Bonjour</value>
              </data>
              <data name="ExtraKey">
                <value>Extra</value>
              </data>
            </root>
            """;

        var baseFile = CreateResxChangedFile("M", "Resources/Strings.resx", baseResx);
        var localeFile = CreateResxChangedFile("A", "Resources/Strings.fr.resx", localeResx);

        var report = Analyze(new ResxUnexpectedExtraKeysCodeReviewCheck(), baseFile, localeFile);

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Message, Does.Contain("`ExtraKey`"));
    }

    [Test]
    public void EmptyTranslationValuesCheckWhenLocaleContainsEmptyValueReportsFinding()
    {
        const string localeResx = """
            <?xml version="1.0" encoding="utf-8"?>
            <root>
              <data name="Hello">
                <value> </value>
              </data>
              <data name="Bye">
                <value>Au revoir</value>
              </data>
            </root>
            """;

        var localeFile = CreateResxChangedFile("A", "Resources/Strings.fr.resx", localeResx);

        var report = Analyze(new ResxEmptyTranslationValuesCodeReviewCheck(), localeFile);

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Message, Does.Contain("`Hello`"));
    }

    [Test]
    public void ValueBoundaryWhitespaceCheckWhenChangedEntryHasLeadingOrTrailingWhitespaceReportsFinding()
    {
        const string resx = """
            <?xml version="1.0" encoding="utf-8"?>
            <root>
              <data name="Hello">
                <value> Hello</value>
              </data>
              <data name="Bye">
                <value>Bye </value>
              </data>
            </root>
            """;

        var changedFile = CreateResxChangedFile("M", "Resources/Strings.resx", resx);

        var report = Analyze(new ResxValueBoundaryWhitespaceCodeReviewCheck(), changedFile);

        Assert.That(report.Findings, Has.Count.EqualTo(2));
        Assert.That(report.Findings.Select(finding => finding.Message), Has.Some.Contains("`Hello`"));
        Assert.That(report.Findings.Select(finding => finding.Message), Has.Some.Contains("`Bye`"));
    }

    [Test]
    public void MixedEnglishDialectCheckWhenNeutralResxContainsEnoughUsAndUkWordsReportsFinding()
    {
        const string resx = """
            <?xml version="1.0" encoding="utf-8"?>
            <root>
              <data name="Palette">
                <value>Choose a color and analyze the results.</value>
              </data>
              <data name="Summary">
                <value>The behaviour and colour should stay consistent.</value>
              </data>
            </root>
            """;

        var changedFile = CreateResxChangedFile("M", "Resources/Strings.resx", resx);

        var report = Analyze(new ResxMixedEnglishDialectCodeReviewCheck(), changedFile);

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Message, Does.Contain("US: 2"));
        Assert.That(report.Findings[0].Message, Does.Contain("UK: 2"));
    }

    [Test]
    public void MixedEnglishDialectCheckWhenNeutralResxDoesNotReachThresholdDoesNotReport()
    {
        const string resx = """
            <?xml version="1.0" encoding="utf-8"?>
            <root>
              <data name="Palette">
                <value>Choose a color for the toolbar.</value>
              </data>
              <data name="Summary">
                <value>The behaviour should stay consistent.</value>
              </data>
            </root>
            """;

        var changedFile = CreateResxChangedFile("M", "Resources/Strings.resx", resx);

        var report = Analyze(new ResxMixedEnglishDialectCodeReviewCheck(), changedFile);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AmericanEnglishInBritishLocaleCheckWhenEnoughUsSpellingsAppearReportsFinding()
    {
        const string resx = """
            <?xml version="1.0" encoding="utf-8"?>
            <root>
              <data name="Palette">
                <value>Pick a color and analyze the result.</value>
              </data>
            </root>
            """;

        var changedFile = CreateResxChangedFile("M", "Resources/Strings.en-GB.resx", resx);

        var report = Analyze(new ResxAmericanEnglishInBritishLocaleCodeReviewCheck(), changedFile);

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Message, Does.Contain("American spellings"));
    }

    [Test]
    public void BritishEnglishInAmericanLocaleCheckWhenEnoughUkSpellingsAppearReportsFinding()
    {
        const string resx = """
            <?xml version="1.0" encoding="utf-8"?>
            <root>
              <data name="Palette">
                <value>The colour and behaviour should match.</value>
              </data>
            </root>
            """;

        var changedFile = CreateResxChangedFile("M", "Resources/Strings.en-US.resx", resx);

        var report = Analyze(new ResxBritishEnglishInAmericanLocaleCodeReviewCheck(), changedFile);

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Message, Does.Contain("British spellings"));
    }

    private static CodeSmellReport Analyze(ICodeReviewCheck check, params CodeReviewChangedFile[] resxFiles)
    {
        var context = new CodeReviewAnalysisContext(
            [],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            resxFiles ?? []);
        var report = new CodeSmellReport();
        check.Analyze(context, report);
        return report;
    }

    private static CodeReviewChangedFile CreateResxChangedFile(string status, string relativePath, string text)
    {
        var normalizedText = (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalizedText.Split('\n');
        var addedLines = new HashSet<int>(Enumerable.Range(1, lines.Length));
        var fullPath = Path.GetTempPath().ToDir().GetFile(relativePath.Replace('/', Path.DirectorySeparatorChar));
        return new CodeReviewChangedFile(status, relativePath, fullPath.FullName, normalizedText, lines, addedLines);
    }
}
