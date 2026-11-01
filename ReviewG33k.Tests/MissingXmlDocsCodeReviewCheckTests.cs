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
public sealed class MissingXmlDocsCodeReviewCheckTests
{
    [Test]
    public void AnalyzeWhenAddedPublicTypeHasNoXmlDocsReportsHint()
    {
        const string source = """
            public sealed class SmartRipOpcDocumentation
            {
            }
            """;

        var report = AnalyzeAddedFile("Services/SmartRipOpcDocumentation.cs", source);

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.MissingXmlDocs));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
    }

    [Test]
    public void AnalyzeWhenAddedInternalTypeHasNoXmlDocsReportsHint()
    {
        const string source = """
            internal sealed class IndexedApplication
            {
            }
            """;

        var report = AnalyzeAddedFile("Models/IndexedApplication.cs", source);

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.MissingXmlDocs));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
    }

    [Test]
    public void AnalyzeWhenAddedProgramTypeHasNoXmlDocsDoesNotReport()
    {
        const string source = """
            internal static class Program
            {
            }
            """;

        var report = AnalyzeAddedFile("Program.cs", source);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenAddedAppTypeHasNoXmlDocsDoesNotReport()
    {
        const string source = """
            public class App
            {
            }
            """;

        var report = AnalyzeAddedFile("Views/App.axaml.cs", source);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenAddedStartupTypeHasNoXmlDocsDoesNotReport()
    {
        const string source = """
            public sealed class Startup
            {
            }
            """;

        var report = AnalyzeAddedFile("Startup.cs", source);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenAddedStaticExtensionsTypeHasNoXmlDocsDoesNotReport()
    {
        const string source = """
            public static class OutputTypeExtensions
            {
            }
            """;

        var report = AnalyzeAddedFile("Extensions/OutputTypeExtensions.cs", source);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenAddedTypeIsATestFixtureDoesNotReport()
    {
        const string source = """
            [TestFixture]
            public sealed class SmartRipOpcDocumentation
            {
            }
            """;

        var report = AnalyzeAddedFile("Services/SmartRipOpcDocumentation.cs", source);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenAddedTypeHasXmlDocsAboveAttributesDoesNotReport()
    {
        const string source = """
            /// <summary>
            /// Represents the OPC node used to expose low-level Meteor operations.
            /// </summary>
            [Description("Meteor-specific control and status for the active print run.")]
            [OpcNodeName("Meteor")]
            public class OpcMeteor
            {
            }
            """;

        var report = AnalyzeAddedFile("SPC/SmartRip/OPC/OpcMeteor.cs", source);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenAddedTypeHasDescriptionAttributeDoesNotReport()
    {
        const string source = """
            [Description("Meteor-specific control and status for the active print run.")]
            public class OpcMeteor
            {
            }
            """;

        var report = AnalyzeAddedFile("SPC/SmartRip/OPC/OpcMeteor.cs", source);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenAddedTypeHasDescriptionAttributeSuffixDoesNotReport()
    {
        const string source = """
            [System.ComponentModel.DescriptionAttribute("Meteor-specific control and status for the active print run.")]
            public class OpcMeteor
            {
            }
            """;

        var report = AnalyzeAddedFile("SPC/SmartRip/OPC/OpcMeteor.cs", source);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeWhenAddedTypeHasEmptyDescriptionAttributeReportsHint()
    {
        const string source = """
            [Description(" ")]
            public class OpcMeteor
            {
            }
            """;

        var report = AnalyzeAddedFile("SPC/SmartRip/OPC/OpcMeteor.cs", source);

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.MissingXmlDocs));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
    }

    [Test]
    public void AnalyzeWhenAddedTypeHasXmlDocsAboveMultilineAttributesDoesNotReport()
    {
        const string source = """
            /// <summary>
            /// Represents the OPC node used to expose low-level Meteor operations.
            /// </summary>
            [Description("Meteor-specific control and status for the active print run.")]
            [ExtendedDescription(
                "The `Meteor` node exposes low-level Meteor methods.",
                "",
                "These methods are only available while a Meteor print run is active.")]
            [OpcNodeName("Meteor")]
            public class OpcMeteor
            {
            }
            """;

        var report = AnalyzeAddedFile("SPC/SmartRip/OPC/OpcMeteor.cs", source);

        Assert.That(report.Findings, Is.Empty);
    }

    private static CodeSmellReport AnalyzeAddedFile(string path, string source)
    {
        var normalizedSource = (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalizedSource.Split('\n');
        var changedFile = new CodeReviewChangedFile(
            "A",
            path,
            path,
            normalizedSource,
            lines,
            new HashSet<int>(Enumerable.Range(1, lines.Length)));
        var context = new CodeReviewAnalysisContext(
            [changedFile],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var report = new CodeSmellReport();
        var check = new MissingXmlDocsCodeReviewCheck();
        check.Analyze(context, report);
        return report;
    }
}
