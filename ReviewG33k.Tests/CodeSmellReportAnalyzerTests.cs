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
using ReviewG33k.ViewModels;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class CodeSmellReportAnalyzerTests
{
    [Test]
    public void AnalyzeFilesWhenChangedFilesIsNullThrowsArgumentNullException()
    {
        var analyzer = new CodeSmellReportAnalyzer(new GitCommandRunner());

        Assert.Throws<ArgumentNullException>(() => analyzer.AnalyzeFiles(null));
    }

    [Test]
    public void AnalyzeFilesWhenChangedFilesIsEmptyReturnsReportWithoutFindings()
    {
        var analyzer = new CodeSmellReportAnalyzer(new GitCommandRunner());

        var report = analyzer.AnalyzeFiles([]);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void ChecksHaveExpectedScopes()
    {
        var analyzer = new CodeSmellReportAnalyzer(new GitCommandRunner());

        var missingXmlDocsCheck = analyzer.Checks.OfType<MissingXmlDocsCodeReviewCheck>().Single();
        var missingUnitTestsCheck = analyzer.Checks.OfType<MissingUnitTestsCodeReviewCheck>().Single();
        var missingTestsForPublicMethodsCheck = analyzer.Checks.OfType<MissingTestsForNewPublicMethodsCodeReviewCheck>().Single();
        var missingTestsForPublicPropertiesCheck = analyzer.Checks.OfType<MissingTestsForNewPublicPropertiesCodeReviewCheck>().Single();
        var missingReadmeCheck = analyzer.Checks.OfType<MissingReadmeForNewProjectCodeReviewCheck>().Single();
        var missingTypedBindingContextCheck = analyzer.Checks.OfType<MissingTypedBindingContextCodeReviewCheck>().Single();
        var missingBlankLineCheck = analyzer.Checks.OfType<MissingBlankLineBetweenMethodsCodeReviewCheck>().Single();
        var stringConcatCheck = analyzer.Checks.OfType<StringConcatenationToSameTargetCodeReviewCheck>().Single();
        var constructorEventSubscriptionCheck = analyzer.Checks.OfType<ConstructorEventSubscriptionLifecycleCodeReviewCheck>().Single();
        var consecutiveBooleanArgumentsCheck = analyzer.Checks.OfType<ConsecutiveBooleanArgumentsCodeReviewCheck>().Single();
        var consecutiveNullArgumentsCheck = analyzer.Checks.OfType<ConsecutiveNullArgumentsCodeReviewCheck>().Single();
        var duplicateCodeBlockCheck = analyzer.Checks.OfType<DuplicateCodeBlockCodeReviewCheck>().Single();
        var booleanMethodAlwaysReturnsSameValueCheck = analyzer.Checks.OfType<BooleanMethodAlwaysReturnsSameValueCodeReviewCheck>().Single();
        var privateBooleanMethodAlwaysReturnsSameValueCheck = analyzer.Checks.OfType<PrivateBooleanMethodAlwaysReturnsSameValueCodeReviewCheck>().Single();
        var resxMissingLocaleKeysCheck = analyzer.Checks.OfType<ResxMissingLocaleKeysCodeReviewCheck>().Single();
        var resxUnexpectedExtraKeysCheck = analyzer.Checks.OfType<ResxUnexpectedExtraKeysCodeReviewCheck>().Single();
        var unusedUsingCheck = analyzer.Checks.OfType<UnusedUsingRoslynCodeReviewCheck>().Single();
        var fileEncodingChangedCheck = analyzer.Checks.OfType<FileEncodingChangedCodeReviewCheck>().Single();
        var fileNewlineChangedCheck = analyzer.Checks.OfType<FileNewlineChangedCodeReviewCheck>().Single();
        var trailingWhitespaceOnlyChangeCheck = analyzer.Checks.OfType<TrailingWhitespaceOnlyChangeCodeReviewCheck>().Single();
        var localVariableCanBeConstCheck = analyzer.Checks.OfType<LocalVariableCanBeConstCodeReviewCheck>().Single();
        var unusedLocalVariableCheck = analyzer.Checks.OfType<UnusedLocalVariableCodeReviewCheck>().Single();
        var privateFieldUsedInSingleMethodCheck = analyzer.Checks.OfType<PrivateFieldUsedInSingleMethodCodeReviewCheck>().Single();

        Assert.That(missingXmlDocsCheck.Scope, Is.EqualTo(CodeReviewCheckScope.AddedLinesOnly));
        Assert.That(missingUnitTestsCheck.Scope, Is.EqualTo(CodeReviewCheckScope.AddedLinesOnly));
        Assert.That(missingTestsForPublicMethodsCheck.Scope, Is.EqualTo(CodeReviewCheckScope.AddedLinesOnly));
        Assert.That(missingTestsForPublicPropertiesCheck.Scope, Is.EqualTo(CodeReviewCheckScope.AddedLinesOnly));
        Assert.That(missingReadmeCheck.Scope, Is.EqualTo(CodeReviewCheckScope.AddedLinesOnly));
        Assert.That(missingTypedBindingContextCheck.Scope, Is.EqualTo(CodeReviewCheckScope.AddedLinesOnly));
        Assert.That(missingBlankLineCheck.Scope, Is.EqualTo(CodeReviewCheckScope.AddedLinesOnly));
        Assert.That(stringConcatCheck.Scope, Is.EqualTo(CodeReviewCheckScope.AddedLinesOnly));
        Assert.That(constructorEventSubscriptionCheck.Scope, Is.EqualTo(CodeReviewCheckScope.AddedLinesOnly));
        Assert.That(consecutiveBooleanArgumentsCheck.Scope, Is.EqualTo(CodeReviewCheckScope.AddedLinesOnly));
        Assert.That(consecutiveNullArgumentsCheck.Scope, Is.EqualTo(CodeReviewCheckScope.AddedLinesOnly));
        Assert.That(duplicateCodeBlockCheck.Scope, Is.EqualTo(CodeReviewCheckScope.AddedLinesOnly));
        Assert.That(booleanMethodAlwaysReturnsSameValueCheck.Scope, Is.EqualTo(CodeReviewCheckScope.AddedLinesOnly));
        Assert.That(privateBooleanMethodAlwaysReturnsSameValueCheck.Scope, Is.EqualTo(CodeReviewCheckScope.AddedLinesOnly));
        Assert.That(resxMissingLocaleKeysCheck.Scope, Is.EqualTo(CodeReviewCheckScope.ChangedFileSet));
        Assert.That(resxUnexpectedExtraKeysCheck.Scope, Is.EqualTo(CodeReviewCheckScope.ChangedFileSet));
        Assert.That(unusedUsingCheck.Scope, Is.EqualTo(CodeReviewCheckScope.ChangedFileSet));
        Assert.That(fileEncodingChangedCheck.Scope, Is.EqualTo(CodeReviewCheckScope.ChangedFileSet));
        Assert.That(fileNewlineChangedCheck.Scope, Is.EqualTo(CodeReviewCheckScope.ChangedFileSet));
        Assert.That(trailingWhitespaceOnlyChangeCheck.Scope, Is.EqualTo(CodeReviewCheckScope.ChangedFileSet));
        Assert.That(localVariableCanBeConstCheck.Scope, Is.EqualTo(CodeReviewCheckScope.AddedLinesOnly));
        Assert.That(unusedLocalVariableCheck.Scope, Is.EqualTo(CodeReviewCheckScope.AddedLinesOnly));
        Assert.That(privateFieldUsedInSingleMethodCheck.Scope, Is.EqualTo(CodeReviewCheckScope.AddedLinesOnly));
    }

    [Test]
    public void AnalyzeFilesWhenAddedLineCheckTargetIsOutsideAddedLinesDoesNotReportByDefault()
    {
        var analyzer = new CodeSmellReportAnalyzer(new GitCommandRunner());
        var changedFile = CreateChangedFile(CreateStringConcatSource(), [1]);

        var report = analyzer.AnalyzeFiles([changedFile]);

        Assert.That(
            report.Findings.Any(finding => finding.RuleId == CodeReviewRuleIds.StringConcatSameTarget),
            Is.False);
    }

    [Test]
    public void AnalyzeFilesWhenFullModifiedFileScopeEnabledReportsAddedLineChecksAcrossFile()
    {
        var analyzer = new CodeSmellReportAnalyzer(new GitCommandRunner());
        var changedFile = CreateChangedFile(CreateStringConcatSource(), [1]);

        var report = analyzer.AnalyzeFiles([changedFile], includeFullModifiedFiles: true);

        Assert.That(
            report.Findings.Any(finding => finding.RuleId == CodeReviewRuleIds.StringConcatSameTarget),
            Is.True);
    }

    [Test]
    public void AnalyzeFilesWhenChangedLineScopeSkipsLocalVariableConstOutsideAddedLines()
    {
        var analyzer = new CodeSmellReportAnalyzer(new GitCommandRunner());
        var changedFile = CreateChangedFile(CreateConstCandidateSource(), [1]);

        var report = analyzer.AnalyzeFiles([changedFile]);

        Assert.That(
            report.Findings.Any(finding => finding.RuleId == "local-variable-can-be-const"),
            Is.False);
    }

    [Test]
    public void AnalyzeFilesWhenUnusedUsingIsOutsideAddedLinesStillReports()
    {
        var analyzer = new CodeSmellReportAnalyzer(new GitCommandRunner());
        var changedFile = CreateChangedFile(
            """
            using System.Text;

            public sealed class Sample
            {
                public int Add(int a, int b) => a + b;
            }
            """,
            [5]);

        var report = analyzer.AnalyzeFiles([changedFile]);

        Assert.That(
            report.Findings.Any(finding => finding.RuleId == CodeReviewRuleIds.UnusedUsingsRoslyn),
            Is.True);
    }

    [Test]
    public void AnalyzeFilesWhenFullModifiedFileScopeEnabledIncludesLocalVariableConstOutsideAddedLines()
    {
        var analyzer = new CodeSmellReportAnalyzer(new GitCommandRunner());
        var changedFile = CreateChangedFile(CreateConstCandidateSource(), [1]);

        var report = analyzer.AnalyzeFiles([changedFile], includeFullModifiedFiles: true);

        Assert.That(
            report.Findings.Any(finding => finding.RuleId == "local-variable-can-be-const"),
            Is.True);
    }

    [Test]
    public void AnalyzeFilesWhenFileIsUnderObjFolderSkipsAllChecks()
    {
        var analyzer = new CodeSmellReportAnalyzer(
            new GitCommandRunner(),
            [new FindingTestCheck()]);
        var changedFile = new CodeReviewChangedFile(
            "M",
            "DTC.Core/CSharp.Core/obj/Debug/net7.0/CSharp.Core.AssemblyInfo.cs",
            "DTC.Core/CSharp.Core/obj/Debug/net7.0/CSharp.Core.AssemblyInfo.cs",
            "using System;",
            ["using System;"],
            new HashSet<int> { 1 });

        var report = analyzer.AnalyzeFiles([changedFile]);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AnalyzeFilesWhenOneCheckThrowsContinuesRunningRemainingChecks()
    {
        var analyzer = new CodeSmellReportAnalyzer(
            new GitCommandRunner(),
            [new ThrowingTestCheck(), new FindingTestCheck()]);
        var changedFile = CreateChangedFile("public sealed class Sample { }", [1]);

        var report = analyzer.AnalyzeFiles([changedFile]);

        Assert.That(
            report.Info.Any(info => info.Contains("CHECK ERROR:", StringComparison.OrdinalIgnoreCase) &&
                                    info.Contains("[throwing-test-check]", StringComparison.OrdinalIgnoreCase)),
            Is.True);
        Assert.That(report.Findings.Any(finding => finding.RuleId == "finding-test-check"), Is.True);
    }

    [Test]
    public void AnalyzeFilesWhenCheckIsDisabledSkipsThatCheck()
    {
        var settings = new Settings
        {
            DisabledCodeReviewRuleIds = ["finding-test-check"]
        };
        var analyzer = new CodeSmellReportAnalyzer(
            new GitCommandRunner(),
            [new FindingTestCheck()],
            new CodeReviewRulePreferenceService(settings));
        var changedFile = CreateChangedFile("public sealed class Sample { }", [1]);

        var report = analyzer.AnalyzeFiles([changedFile]);

        Assert.Multiple(() =>
        {
            Assert.That(report.Findings, Is.Empty);
            Assert.That(analyzer.AllChecks, Has.Count.EqualTo(1));
            Assert.That(analyzer.Checks, Is.Empty);
        });
    }

    [Test]
    public void RulePreferenceNormalizationRemovesBlankAndDuplicateRuleIds()
    {
        var normalizedRuleIds = CodeReviewRulePreferenceService.NormalizeRuleIds(
            [" missing-tests ", "", "MISSING-TESTS", null, "public-method-argument-guards"]);

        Assert.That(
            normalizedRuleIds,
            Is.EqualTo(new[] { "missing-tests", "public-method-argument-guards" }));
    }

    [Test]
    public async Task AnalyzeLoadedFilesAsyncWhenOneCheckThrowsContinuesRunningRemainingChecks()
    {
        var analyzer = new CodeSmellReportAnalyzer(
            new GitCommandRunner(),
            [new ThrowingTestCheck(), new FindingTestCheck()]);
        var changedFile = CreateChangedFile("public sealed class Sample { }", [1]);
        var sourceResult = new CodeReviewChangedFileSourceResult([changedFile], []);

        var report = await analyzer.AnalyzeLoadedFilesAsync(sourceResult);

        Assert.That(
            report.Info.Any(info => info.Contains("CHECK ERROR:", StringComparison.OrdinalIgnoreCase) &&
                                    info.Contains("[throwing-test-check]", StringComparison.OrdinalIgnoreCase)),
            Is.True);
        Assert.That(report.Findings.Any(finding => finding.RuleId == "finding-test-check"), Is.True);
    }

    private static CodeReviewChangedFile CreateChangedFile(string source, IEnumerable<int> addedLines)
    {
        var normalizedSource = (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalizedSource.Split('\n');
        return new CodeReviewChangedFile(
            "M",
            "Services/Sample.cs",
            "Services/Sample.cs",
            normalizedSource,
            lines,
            new HashSet<int>(addedLines ?? []));
    }

    private static string CreateStringConcatSource() =>
        """
        public sealed class Sample
        {
            public void Run()
            {
                var output = string.Empty;
                output += "a";
                output += "b";
                output += "c";
                output += "d";
            }
        }
        """;

    private static string CreateConstCandidateSource() =>
        """
        public sealed class Sample
        {
            public void Run()
            {
                var title = "ReviewG33k";
                System.Console.WriteLine(title);
            }
        }
        """;

    private sealed class ThrowingTestCheck : CodeReviewCheckBase
    {
        public override string RuleId => "throwing-test-check";

        public override string DisplayName => "throwing test check";

        public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report) =>
            throw new InvalidOperationException("Test failure.");
    }

    private sealed class FindingTestCheck : CodeReviewCheckBase
    {
        public override string RuleId => "finding-test-check";

        public override string DisplayName => "finding test check";

        public override void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report)
        {
            if (context.Files.Count == 0)
                return;

            AddFinding(
                report,
                CodeReviewFindingSeverity.Hint,
                context.Files[0].Path,
                1,
                "Found by test check.");
        }
    }
}
