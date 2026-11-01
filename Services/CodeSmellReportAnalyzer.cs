// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ReviewG33k.Services.Checks;
using Support = ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Services;

public sealed class CodeSmellReportAnalyzer
{
    private const string CheckErrorInfoPrefix = "CHECK ERROR:";
    private readonly GitCommandRunner m_gitCommandRunner;
    private readonly IReadOnlyList<ICodeReviewCheck> m_checks;
    private readonly CodeReviewRulePreferenceService m_rulePreferenceService;

    public CodeSmellReportAnalyzer(GitCommandRunner gitCommandRunner)
        : this(gitCommandRunner, CreateChecks(gitCommandRunner), null)
    {
    }

    public CodeSmellReportAnalyzer(GitCommandRunner gitCommandRunner, CodeReviewRulePreferenceService rulePreferenceService)
        : this(gitCommandRunner, CreateChecks(gitCommandRunner), rulePreferenceService)
    {
    }

    internal CodeSmellReportAnalyzer(GitCommandRunner gitCommandRunner, IReadOnlyList<ICodeReviewCheck> checks)
        : this(gitCommandRunner, checks, null)
    {
    }

    internal CodeSmellReportAnalyzer(
        GitCommandRunner gitCommandRunner,
        IReadOnlyList<ICodeReviewCheck> checks,
        CodeReviewRulePreferenceService rulePreferenceService)
    {
        m_gitCommandRunner = gitCommandRunner ?? throw new ArgumentNullException(nameof(gitCommandRunner));
        m_checks = (checks ?? throw new ArgumentNullException(nameof(checks)))
            .Where(check => check != null)
            .ToArray();
        m_rulePreferenceService = rulePreferenceService;
    }

    public IReadOnlyList<ICodeReviewCheck> Checks => GetEnabledChecks();

    public IReadOnlyList<ICodeReviewCheck> AllChecks => m_checks;

    public async Task<CodeSmellReport> AnalyzeAsync(
        string reviewWorktreePath,
        string targetBranch,
        Action<string> progressLogger = null,
        Action<int, int, string> progressReporter = null,
        bool includeFullModifiedFiles = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var changedFileSource = new GitBranchComparisonChangedFileSource(
            m_gitCommandRunner,
            reviewWorktreePath,
            targetBranch,
            fetchTargetBranch: true);

        return await AnalyzeAsync(
            changedFileSource,
            progressLogger,
            progressReporter,
            includeFullModifiedFiles,
            cancellationToken);
    }

    public async Task<CodeSmellReport> AnalyzeAsync(
        ICodeReviewChangedFileSource changedFileSource,
        Action<string> progressLogger = null,
        Action<int, int, string> progressReporter = null,
        bool includeFullModifiedFiles = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (changedFileSource == null)
        {
            var report = new CodeSmellReport();
            report.AddInfo("Code review scan skipped: Changed file source unavailable.");
            return report;
        }

        progressLogger?.Invoke("Code review scan: Gathering changed files...");
        var sourceResult = await changedFileSource.LoadAsync(progressLogger);
        cancellationToken.ThrowIfCancellationRequested();
        progressLogger?.Invoke($"Code review scan: Changed files ready ({sourceResult?.Files?.Count ?? 0}).");
        return await AnalyzeLoadedFilesAsync(
            sourceResult,
            progressLogger,
            progressReporter,
            includeFullModifiedFiles,
            cancellationToken);
    }

    public async Task<CodeSmellReport> AnalyzeLoadedFilesAsync(
        CodeReviewChangedFileSourceResult sourceResult,
        Action<string> progressLogger = null,
        Action<int, int, string> progressReporter = null,
        bool includeFullModifiedFiles = false,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var report = new CodeSmellReport();
        foreach (var infoMessage in sourceResult?.InfoMessages ?? [])
            report.AddInfo(infoMessage);

        var changedFiles = sourceResult?.Files?.Where(file => file != null).ToArray() ?? [];
        if (changedFiles.Length == 0)
            return report;

        progressLogger?.Invoke($"Code review scan: Building analysis context for {changedFiles.Length} file(s)...");
        var scopedContexts = BuildScopedContexts(changedFiles, sourceResult?.IsEntireRepositoryScan == true);
        var enabledChecks = GetEnabledChecks();
        var disabledCheckCount = m_checks.Count - enabledChecks.Count;
        if (disabledCheckCount > 0)
            progressLogger?.Invoke($"Code review scan: {disabledCheckCount} check(s) disabled by settings.");

        var totalChecks = enabledChecks.Count;
        progressReporter?.Invoke(0, totalChecks, null);
        progressLogger?.Invoke($"Code review scan: Running {totalChecks} checks...");

        var pendingCheckTasks = enabledChecks
            .Select(check => Task.Run(() => AnalyzeCheck(
                check,
                scopedContexts,
                includeFullModifiedFiles,
                cancellationToken), cancellationToken))
            .ToList();

        while (pendingCheckTasks.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var completedTask = await Task.WhenAny(pendingCheckTasks);
            pendingCheckTasks.Remove(completedTask);
            var checkResult = await completedTask;
            var completedChecks = totalChecks - pendingCheckTasks.Count;
            progressLogger?.Invoke($"CHECK RUN: {completedChecks}/{totalChecks} {checkResult.Check.DisplayName}");
            MergeReport(report, checkResult.Report);
            progressReporter?.Invoke(completedChecks, totalChecks, checkResult.Check.DisplayName);
        }

        return report;
    }

    public CodeSmellReport AnalyzeFiles(
        IReadOnlyList<CodeReviewChangedFile> changedFiles,
        bool includeFullModifiedFiles = false)
    {
        ArgumentNullException.ThrowIfNull(changedFiles);

        var report = new CodeSmellReport();
        var files = changedFiles.Where(file => file != null).ToArray();
        if (files.Length == 0)
            return report;

        var scopedContexts = BuildScopedContexts(files, isEntireRepositoryScan: false);
        var checkReports = GetEnabledChecks()
            .AsParallel()
            .Select(check => AnalyzeCheck(
                check,
                scopedContexts,
                includeFullModifiedFiles,
                CancellationToken.None).Report)
            .ToArray();

        foreach (var checkReport in checkReports)
            MergeReport(report, checkReport);

        return report;
    }

    private IReadOnlyList<ICodeReviewCheck> GetEnabledChecks()
    {
        if (m_rulePreferenceService == null)
            return m_checks;

        return m_checks
            .Where(check => m_rulePreferenceService.IsRuleEnabled(check.RuleId))
            .ToArray();
    }

    private static void MergeReport(CodeSmellReport destination, CodeSmellReport source)
    {
        if (destination == null || source == null)
            return;

        foreach (var info in source.Info)
            destination.AddInfo(info);

        foreach (var finding in source.Findings)
            destination.AddFinding(finding.Severity, finding.RuleId, finding.FilePath, finding.LineNumber, finding.Message);
    }

    private static (ICodeReviewCheck Check, CodeSmellReport Report) AnalyzeCheck(
        ICodeReviewCheck check,
        CodeReviewCheckContextSet scopedContexts,
        bool includeFullModifiedFiles,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var checkReport = new CodeSmellReport();
        try
        {
            var scopedContext = GetContextForScope(scopedContexts, check.Scope, includeFullModifiedFiles);
            check.Analyze(scopedContext, checkReport);
        }
        catch (Exception exception)
        {
            checkReport.AddInfo(
                $"{CheckErrorInfoPrefix} [{check.RuleId}] {check.DisplayName} failed. {exception.GetType().Name}: {exception.Message}");
        }

        return (check, checkReport);
    }

    private static CodeReviewCheckContextSet BuildScopedContexts(IReadOnlyList<CodeReviewChangedFile> changedFiles, bool isEntireRepositoryScan)
    {
        var addedLinesContext = BuildContext(changedFiles, includeAllFileLines: false, isEntireRepositoryScan);
        var fullFileContext = BuildContext(changedFiles, includeAllFileLines: true, isEntireRepositoryScan);

        return new CodeReviewCheckContextSet(addedLinesContext, fullFileContext);
    }

    private static CodeReviewAnalysisContext GetContextForScope(
        CodeReviewCheckContextSet contexts,
        CodeReviewCheckScope scope,
        bool includeFullModifiedFiles)
    {
        return scope switch
        {
            CodeReviewCheckScope.ChangedFileSet => contexts.FullFileContext,
            _ => includeFullModifiedFiles
                ? contexts.FullFileContext
                : contexts.AddedLinesOnlyContext
        };
    }

    private static CodeReviewAnalysisContext BuildContext(
        IReadOnlyList<CodeReviewChangedFile> changedFiles,
        bool includeAllFileLines,
        bool isEntireRepositoryScan)
    {
        var relevantChangedFiles = changedFiles
            .Where(file => file != null && !Support.CodeReviewFileClassification.IsIgnoredPath(file.Path))
            .ToArray();
        var sourceFiles = includeAllFileLines
            ? relevantChangedFiles.Select(CreateWholeFileClone).ToArray()
            : relevantChangedFiles.ToArray();

        var csharpFiles = sourceFiles
            .Where(file => Support.CodeReviewFileClassification.IsAnalyzableChangedCSharpPath(file.Path))
            .ToArray();
        var resxFiles = sourceFiles
            .Where(file => Support.CodeReviewFileClassification.IsAnalyzableResxPath(file.Path))
            .ToArray();

        var addedTestFilesByName = new HashSet<string>(
            csharpFiles
                .Where(file => file.IsAdded)
                .Where(Support.CodeReviewFileClassification.IsLikelyTestCodeFile)
                .Select(file => Path.GetFileName(file.Path)),
            StringComparer.OrdinalIgnoreCase);
        var changedTestFilesByName = new HashSet<string>(
            csharpFiles
                .Where(Support.CodeReviewFileClassification.IsLikelyTestCodeFile)
                .Select(file => Path.GetFileName(file.Path)),
            StringComparer.OrdinalIgnoreCase);

        return new CodeReviewAnalysisContext(csharpFiles, addedTestFilesByName, changedTestFilesByName, resxFiles, sourceFiles, isEntireRepositoryScan);
    }

    private static CodeReviewChangedFile CreateWholeFileClone(CodeReviewChangedFile file)
    {
        if (file == null)
            return null;

        var allLines = new HashSet<int>(Enumerable.Range(1, file.Lines.Count));
        return new CodeReviewChangedFile(
            file.Status,
            file.Path,
            file.FullPath,
            file.Text,
            file.Lines,
            allLines,
            file.BaselineText,
            file.CurrentBytes,
            file.BaselineBytes,
            file.RoslynCacheKey);
    }

    private sealed class CodeReviewCheckContextSet
    {
        public CodeReviewCheckContextSet(CodeReviewAnalysisContext addedLinesOnlyContext, CodeReviewAnalysisContext fullFileContext)
        {
            AddedLinesOnlyContext = addedLinesOnlyContext;
            FullFileContext = fullFileContext;
        }

        public CodeReviewAnalysisContext AddedLinesOnlyContext { get; }

        public CodeReviewAnalysisContext FullFileContext { get; }
    }

    private static IReadOnlyList<ICodeReviewCheck> CreateChecks(GitCommandRunner gitCommandRunner) =>
    [
        new AsyncVoidCodeReviewCheck(),
        new AsyncMethodNameSuffixCodeReviewCheck(),
        new PublicMethodArgumentGuardsCodeReviewCheck(),
        new EmptyCatchCodeReviewCheck(),
        new EmptyMethodCodeReviewCheck(),
        new EmptyIfCodeReviewCheck(),
        new SwallowingCatchCodeReviewCheck(),
        new LockTargetCodeReviewCheck(),
        new TaskRunAsyncCodeReviewCheck(),
        new UnnecessaryTaskCompletionCodeReviewCheck(),
        new PropertyCanBeAutoPropertyCodeReviewCheck(),
        new PrivateGetOnlyAutoPropertyShouldBeFieldCodeReviewCheck(),
        new PrivatePropertyShouldBeFieldCodeReviewCheck(),
        new PropertySetterIgnoresValueCodeReviewCheck(),
        new PrivateFieldCanBeReadonlyCodeReviewCheck(),
        new PrivateFieldUsedInSingleMethodCodeReviewCheck(),
        new MissingBlankLineBetweenMethodsCodeReviewCheck(),
        new BlankLineBetweenBracePairsCodeReviewCheck(),
        new MethodParameterCountCodeReviewCheck(),
        new GenericTypeNameSuffixCodeReviewCheck(),
        new IfElseBraceConsistencyCodeReviewCheck(),
        new IfElseUnnecessaryBracesCodeReviewCheck(),
        new ConstructorTooLongCodeReviewCheck(),
        new StringConcatenationToSameTargetCodeReviewCheck(),
        new ConstructorEventSubscriptionLifecycleCodeReviewCheck(),
        new ThreadSleepCodeReviewCheck(),
        new ThrowExCodeReviewCheck(),
        new NamedReflectionMemberLookupCodeReviewCheck(),
        new MethodCanBeStaticCodeReviewCheck(),
        new RedundantSelfLookupCodeReviewCheck(),
        new BooleanLiteralComparisonCodeReviewCheck(),
        new BooleanMethodAlwaysReturnsSameValueCodeReviewCheck(),
        new PrivateBooleanMethodAlwaysReturnsSameValueCodeReviewCheck(),
        new ConsecutiveBooleanArgumentsCodeReviewCheck(),
        new ConsecutiveNullArgumentsCodeReviewCheck(),
        new LambdaCanBeMethodGroupCodeReviewCheck(),
        new DuplicateCodeBlockCodeReviewCheck(gitCommandRunner),
        new EagerMaterializedEnumerableReturnCodeReviewCheck(),
        new LinqCountComparedToZeroCodeReviewCheck(),
        new NumericStringCultureForFileWriteCodeReviewCheck(),
        new UnnecessaryCastCodeReviewCheck(),
        new UnnecessaryEnumMemberValueCodeReviewCheck(),
        new UnnecessaryVerbatimStringPrefixCodeReviewCheck(),
        new UnobservedTaskResultCodeReviewCheck(),
        new EmptyDisposeCodeReviewCheck(),
        new DisposeMethodWithoutIDisposableCodeReviewCheck(),
        new DisposableNotDisposedCodeReviewCheck(),
        new MultipleEnumerationCodeReviewCheck(),
        new PublicMutableStaticStateCodeReviewCheck(),
        new UnusedPrivateMemberCodeReviewCheck(),
        new UnusedMethodParameterCodeReviewCheck(),
        new MissingXmlDocsCodeReviewCheck(),
        new EmptyXmlDocContentCodeReviewCheck(),
        new MissingUnitTestsCodeReviewCheck(),
        new MissingTestsForNewPublicMethodsCodeReviewCheck(),
        new MissingTestsForNewPublicPropertiesCodeReviewCheck(),
        new MissingReadmeForNewProjectCodeReviewCheck(),
        new MissingDisclaimerForNewSourceFileCodeReviewCheck(),
        new MissingTypedBindingContextCodeReviewCheck(),
        new FixedSizeLayoutContainerCodeReviewCheck(),
        new SingleChildWrapperContainerCodeReviewCheck(),
        new NestedSamePanelWrapperCodeReviewCheck(),
        new EmptyContainerCodeReviewCheck(),
        new MultipleClassesPerFileCodeReviewCheck(),
        new SourceFileNameMismatchCodeReviewCheck(),
        new ResxMissingLocaleKeysCodeReviewCheck(),
        new ResxUnexpectedExtraKeysCodeReviewCheck(),
        new ResxEmptyTranslationValuesCodeReviewCheck(),
        new ResxValueBoundaryWhitespaceCodeReviewCheck(),
        new ResxMixedEnglishDialectCodeReviewCheck(),
        new ResxAmericanEnglishInBritishLocaleCodeReviewCheck(),
        new ResxBritishEnglishInAmericanLocaleCodeReviewCheck(),
        new WarningSuppressionCodeReviewCheck(),
        new FileEncodingChangedCodeReviewCheck(),
        new FileNewlineChangedCodeReviewCheck(),
        new FormattingOnlyChangeCodeReviewCheck(),
        new TrailingWhitespaceOnlyChangeCodeReviewCheck(),
        new JsonTrailingCommaCodeReviewCheck(),
        new UnusedUsingRoslynCodeReviewCheck(),
        new LocalVariableCanBeConstCodeReviewCheck(),
        new LocalVariablePrefixCodeReviewCheck(),
        new UnusedLocalVariableCodeReviewCheck()
    ];
}
