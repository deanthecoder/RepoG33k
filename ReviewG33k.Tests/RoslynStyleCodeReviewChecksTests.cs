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

public sealed class RoslynStyleCodeReviewChecksTests
{
    [Test]
    public void AutoPropertyCheckWhenPublicWrapperPropertyIsAddedReportsHint()
    {
        const string source = """
            public sealed class Sample
            {
                private string name;

                public string Name
                {
                    get { return this.name; }
                    set { this.name = value; }
                }
            }
            """;

        var report = AnalyzeSource(new PropertyCanBeAutoPropertyCodeReviewCheck(), "A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
        Assert.That(report.Findings[0].Message, Does.Contain("Name"));
    }

    [Test]
    public void AutoPropertyCheckWhenPropertyMirrorsAnotherPropertyDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public bool OldProperty { get; set; }

                public bool NewProperty
                {
                    get { return OldProperty; }
                    set { OldProperty = value; }
                }
            }
            """;

        var report = AnalyzeSource(new PropertyCanBeAutoPropertyCodeReviewCheck(), "A", source, Enumerable.Range(1, 11));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void PrivateGetOnlyAutoPropertyFieldCheckWhenSimplePrivateGetOnlyPropertyIsAddedReportsHint()
    {
        const string source = """
            public sealed class Sample
            {
                private int Count { get; }
            }
            """;

        var report = AnalyzeSource(new PrivateGetOnlyAutoPropertyShouldBeFieldCodeReviewCheck(), "A", source, Enumerable.Range(1, 5));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
        Assert.That(report.Findings[0].Message, Does.Contain("`Count`"));
    }

    [Test]
    public void PrivateGetOnlyAutoPropertyFieldCheckWhenPropertyHasSetterDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                private int Count { get; set; }
            }
            """;

        var report = AnalyzeSource(new PrivateGetOnlyAutoPropertyShouldBeFieldCodeReviewCheck(), "A", source, Enumerable.Range(1, 5));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void PrivateGetOnlyAutoPropertyFieldCheckWhenPropertyIsStaticDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                private static int Count { get; }
            }
            """;

        var report = AnalyzeSource(new PrivateGetOnlyAutoPropertyShouldBeFieldCodeReviewCheck(), "A", source, Enumerable.Range(1, 5));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void PrivatePropertyFieldCheckWhenSimplePrivateAutoPropertyIsAddedReportsHint()
    {
        const string source = """
            public sealed class Sample
            {
                private int Count { get; set; }
            }
            """;

        var report = AnalyzeSource(new PrivatePropertyShouldBeFieldCodeReviewCheck(), "A", source, Enumerable.Range(1, 5));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Message, Does.Contain("Count"));
    }

    [Test]
    public void PrivateFieldCanBeReadonlyCheckWhenAssignedOnlyInConstructorReportsHint()
    {
        const string source = """
            public sealed class Sample
            {
                private int count;

                public Sample()
                {
                    this.count = 1;
                }
            }
            """;

        var report = AnalyzeSource(new PrivateFieldCanBeReadonlyCodeReviewCheck(), "A", source, Enumerable.Range(1, 11));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
        Assert.That(report.Findings[0].Message, Does.Contain("`count`"));
        Assert.That(report.Findings[0].Message, Does.Contain("`readonly`"));
    }

    [Test]
    public void PrivateFieldCanBeReadonlyCheckWhenAssignedInMethodDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                private int count;

                public void Update()
                {
                    this.count = 3;
                }
            }
            """;

        var report = AnalyzeSource(new PrivateFieldCanBeReadonlyCodeReviewCheck(), "A", source, Enumerable.Range(1, 11));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void PrivateFieldCanBeReadonlyCheckWhenFieldIsUnassignedDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                private int count;
            }
            """;

        var report = AnalyzeSource(new PrivateFieldCanBeReadonlyCodeReviewCheck(), "A", source, Enumerable.Range(1, 5));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void PrivateFieldCanBeReadonlyCheckWhenStaticFieldAssignedInStaticConstructorReportsHint()
    {
        const string source = """
            public sealed class Sample
            {
                private static int count;

                static Sample()
                {
                    count = 2;
                }
            }
            """;

        var report = AnalyzeSource(new PrivateFieldCanBeReadonlyCodeReviewCheck(), "A", source, Enumerable.Range(1, 11));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Message, Does.Contain("`count`"));
    }

    [Test]
    public void PrivateFieldCanBeReadonlyCheckWhenContainingTypeIsPartialDoesNotReport()
    {
        const string source = """
            public partial class Sample
            {
                private int count;

                public Sample()
                {
                    this.count = 1;
                }
            }
            """;

        var report = AnalyzeSource(new PrivateFieldCanBeReadonlyCodeReviewCheck(), "A", source, Enumerable.Range(1, 11));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void MissingBlankLineBetweenMethodsCheckWhenMethodsAreAdjacentReportsHint()
    {
        const string source = """
            public sealed class Sample
            {
                private int Foo()
                {
                    return 1;
                }
                private int Bar()
                {
                    return 2;
                }
            }
            """;

        var report = AnalyzeSource(new MissingBlankLineBetweenMethodsCodeReviewCheck(), "A", source, Enumerable.Range(1, 12));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
        Assert.That(report.Findings[0].Message, Does.Contain("`Foo`"));
        Assert.That(report.Findings[0].Message, Does.Contain("`Bar`"));
    }

    [Test]
    public void MissingBlankLineBetweenMethodsCheckWhenMethodsAreSeparatedByBlankLineDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                private int Foo()
                {
                    return 1;
                }

                private int Bar()
                {
                    return 2;
                }
            }
            """;

        var report = AnalyzeSource(new MissingBlankLineBetweenMethodsCodeReviewCheck(), "A", source, Enumerable.Range(1, 13));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void MissingBlankLineBetweenMethodsCheckWhenAdjacencyIsOutsideAddedLinesDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                private int Foo()
                {
                    return 1;
                }
                private int Bar()
                {
                    return 2;
                }
            }
            """;

        var report = AnalyzeSource(new MissingBlankLineBetweenMethodsCodeReviewCheck(), "M", source, [1, 2]);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void MissingBlankLineBetweenMethodsCheckWhenMethodsAreInterfaceDeclarationsDoesNotReport()
    {
        const string source = """
            public interface IPrinterProfile
            {
                string InstallMediaWithName(byte[] smdFileBytes, string mediaName);
                string InspectMedia(byte[] smdFileBytes);
            }
            """;

        var report = AnalyzeSource(new MissingBlankLineBetweenMethodsCodeReviewCheck(), "A", source, Enumerable.Range(1, 6));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void MissingBlankLineBetweenMethodsCheckWhenMethodsReturnLambdaDoesNotReport()
    {
        const string source = """
            using System;

            public sealed class Sample
            {
                private Func<int, int> Foo() => x => x + 1;
                private Func<int, int> Bar() => y => y + 2;
            }
            """;

        var report = AnalyzeSource(new MissingBlankLineBetweenMethodsCodeReviewCheck(), "A", source, Enumerable.Range(1, 8));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void MissingBlankLineBetweenMethodsCheckWhenMethodsAreExpressionBodiedDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public bool Connect() => false;
                public bool IsConnected() => false;
                public T1 Read<T1>(string nodePath) => default;
                public bool Write(string nodePath, object value) => false;
            }
            """;

        var report = AnalyzeSource(new MissingBlankLineBetweenMethodsCodeReviewCheck(), "A", source, Enumerable.Range(1, 9));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void BlankLineBetweenBracePairsCheckWhenClosingBracesAreSeparatedByBlankLineReportsHint()
    {
        const string source = """
            public sealed class Sample
            {
                public void Run()
                {
                    if (true)
                    {
                    }

                }
            }
            """;

        var report = AnalyzeSource(new BlankLineBetweenBracePairsCodeReviewCheck(), "A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
        Assert.That(report.Findings[0].Message, Does.Contain("closing braces"));
    }

    [Test]
    public void BlankLineBetweenBracePairsCheckWhenOpeningBracesAreSeparatedByBlankLineReportsHint()
    {
        const string source = """
            public sealed class Sample
            {
                public void Run()
                {

                    {
                        System.Console.WriteLine("Hi");
                    }
                }
            }
            """;

        var report = AnalyzeSource(new BlankLineBetweenBracePairsCodeReviewCheck(), "A", source, Enumerable.Range(1, 11));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Message, Does.Contain("opening braces"));
    }

    [Test]
    public void BlankLineBetweenBracePairsCheckWhenBraceGapIsOutsideAddedLinesDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public void Run()
                {
                    if (true)
                    {
                    }

                }
            }
            """;

        var report = AnalyzeSource(new BlankLineBetweenBracePairsCodeReviewCheck(), "M", source, [1, 2]);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void MethodParameterCountCheckWhenMethodHasEightParametersReportsHint()
    {
        const string source = """
            public sealed class Sample
            {
                public void DoWork(int a, int b, int c, int d, int e, int f, int g, int h)
                {
                }
            }
            """;

        var report = AnalyzeSource(new MethodParameterCountCodeReviewCheck(), "A", source, Enumerable.Range(1, 7));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Message, Does.Contain("8 parameters"));
    }

    [Test]
    public void MethodParameterCountCheckWhenConstructorHasEightParametersReportsHint()
    {
        const string source = """
            public sealed class Sample
            {
                public Sample(int a, int b, int c, int d, int e, int f, int g, int h)
                {
                }
            }
            """;

        var report = AnalyzeSource(new MethodParameterCountCodeReviewCheck(), "A", source, Enumerable.Range(1, 7));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Message, Does.Contain("Constructor `Sample`"));
        Assert.That(report.Findings[0].Message, Does.Contain("8 parameters"));
    }

    [Test]
    public void GenericTypeNameSuffixCheckWhenClassNameEndsWithHelperReportsHint()
    {
        const string source = """
            public sealed class TextHelper
            {
            }
            """;

        var report = AnalyzeSource(new GenericTypeNameSuffixCodeReviewCheck(), "A", source, Enumerable.Range(1, 3));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Message, Does.Contain("TextHelper"));
    }

    [Test]
    public void GenericTypeNameSuffixCheckWhenClassNameEndsWithUtilitiesReportsHint()
    {
        const string source = """
            public static class TextUtilities
            {
            }
            """;

        var report = AnalyzeSource(new GenericTypeNameSuffixCodeReviewCheck(), "A", source, Enumerable.Range(1, 3));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Message, Does.Contain("TextUtilities"));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
    }

    [Test]
    public void GenericTypeNameSuffixCheckWhenClassNameEndsWithUtilitiesForInternalTypeReportsHint()
    {
        const string source = """
            internal static class CodeReviewCheckUtilities
            {
            }
            """;

        var report = AnalyzeSource(new GenericTypeNameSuffixCodeReviewCheck(), "A", source, Enumerable.Range(1, 3));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Message, Does.Contain("CodeReviewCheckUtilities"));
    }

    [Test]
    public void IfElseBraceConsistencyCheckWhenOnlyOneBranchUsesBracesReportsSuggestion()
    {
        const string source = """
            public sealed class Sample
            {
                public int Run(bool flag)
                {
                    if (flag)
                    {
                        return 1;
                    }
                    else
                        return 0;
                }
            }
            """;

        var report = AnalyzeSource(new IfElseBraceConsistencyCodeReviewCheck(), "A", source, Enumerable.Range(1, 12));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Suggestion));
    }

    [Test]
    public void IfElseBraceConsistencyCheckWhenElseIfChainDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public int Run(int value)
                {
                    if (value == 0)
                    {
                        return 0;
                    }
                    else if (value == 1)
                        return 1;
                    else
                        return -1;
                }
            }
            """;

        var report = AnalyzeSource(new IfElseBraceConsistencyCodeReviewCheck(), "A", source, Enumerable.Range(1, 15));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void IfElseUnnecessaryBracesCheckWhenBothBranchesContainSingleStatementReportsHint()
    {
        const string source = """
            public sealed class Sample
            {
                public int Run(bool flag)
                {
                    if (flag)
                    {
                        return 1;
                    }
                    else
                    {
                        return 0;
                    }
                }
            }
            """;

        var report = AnalyzeSource(new IfElseUnnecessaryBracesCodeReviewCheck(), "A", source, Enumerable.Range(1, 14));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
    }

    [Test]
    public void IfElseUnnecessaryBracesCheckWhenEitherBranchHasMultipleStatementsDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public int Run(bool flag)
                {
                    if (flag)
                    {
                        var x = 1;
                        return x;
                    }
                    else
                    {
                        return 0;
                    }
                }
            }
            """;

        var report = AnalyzeSource(new IfElseUnnecessaryBracesCodeReviewCheck(), "A", source, Enumerable.Range(1, 15));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void IfElseUnnecessaryBracesCheckWhenSingleStatementSpansMultipleLinesDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public string Run(bool flag)
                {
                    if (flag)
                    {
                        return string.Concat(
                            "A",
                            "B");
                    }
                    else
                    {
                        return "C";
                    }
                }
            }
            """;

        var report = AnalyzeSource(new IfElseUnnecessaryBracesCodeReviewCheck(), "A", source, Enumerable.Range(1, 17));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void IfElseUnnecessaryBracesCheckWhenIfBranchHasBracesAndElseIfChainHasNoBracesDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public int Run(bool a, bool b)
                {
                    if (a)
                    {
                        return 1;
                    }
                    else if (b)
                        return 2;
                    else
                        return 3;
                }
            }
            """;

        var report = AnalyzeSource(new IfElseUnnecessaryBracesCodeReviewCheck(), "A", source, Enumerable.Range(1, 15));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void IfElseUnnecessaryBracesCheckWhenElseIfBranchHasSingleStatementBracesDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public int Run(bool a, bool b)
                {
                    if (a)
                        return 1;
                    else if (b)
                    {
                        return 2;
                    }

                    return 3;
                }
            }
            """;

        var report = AnalyzeSource(new IfElseUnnecessaryBracesCodeReviewCheck(), "A", source, Enumerable.Range(1, 16));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void IfElseUnnecessaryBracesCheckWhenOwningIfBranchCannotUnwrapDoesNotReportElseIfBranch()
    {
        const string source = """
            public sealed class Sample
            {
                public int Run(string line)
                {
                    var openParenthesisBalance = 0;
                    var hasSeenOpenParenthesis = false;
                    foreach (var character in line)
                    {
                        if (character == '(')
                        {
                            openParenthesisBalance++;
                            hasSeenOpenParenthesis = true;
                        }
                        else if (character == ')')
                        {
                            openParenthesisBalance--;
                        }
                    }

                    return openParenthesisBalance;
                }
            }
            """;

        var report = AnalyzeSource(new IfElseUnnecessaryBracesCodeReviewCheck(), "A", source, Enumerable.Range(1, 24));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void ConstructorTooLongCheckWhenConstructorHasTwentyCodeLinesReportsSuggestion()
    {
        const string source = """
            public sealed class Sample
            {
                public Sample()
                {
                    var a1 = 1;
                    var a2 = 2;
                    var a3 = 3;
                    var a4 = 4;
                    var a5 = 5;
                    var a6 = 6;
                    var a7 = 7;
                    var a8 = 8;
                    var a9 = 9;
                    var a10 = 10;
                    var a11 = 11;
                    var a12 = 12;
                    var a13 = 13;
                    var a14 = 14;
                    var a15 = 15;
                    var a16 = 16;
                    var a17 = 17;
                    var a18 = 18;
                    var a19 = 19;
                    var a20 = 20;
                }
            }
            """;

        var report = AnalyzeSource(new ConstructorTooLongCodeReviewCheck(), "A", source, Enumerable.Range(1, 27));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Suggestion));
        Assert.That(report.Findings[0].Message, Does.Contain("20 code lines"));
    }

    [Test]
    public void ConstructorTooLongCheckWhenConstructorHasNineteenCodeLinesDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public Sample()
                {
                    var a1 = 1;
                    var a2 = 2;
                    var a3 = 3;
                    var a4 = 4;
                    var a5 = 5;
                    var a6 = 6;
                    var a7 = 7;
                    var a8 = 8;
                    var a9 = 9;
                    var a10 = 10;
                    var a11 = 11;
                    var a12 = 12;
                    var a13 = 13;
                    var a14 = 14;
                    var a15 = 15;
                    var a16 = 16;
                    var a17 = 17;
                    var a18 = 18;
                    var a19 = 19;
                }
            }
            """;

        var report = AnalyzeSource(new ConstructorTooLongCodeReviewCheck(), "A", source, Enumerable.Range(1, 21));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void ThreadSleepCheckWhenThreadSleepIsUsedReportsSuggestion()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Sample
            {
                public async Task Run()
                {
                    Thread.Sleep(20);
                }
            }
            """;

        var report = AnalyzeSource(new ThreadSleepCodeReviewCheck(), "A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Suggestion));
        Assert.That(report.Findings[0].Message, Does.Contain("Thread.Sleep"));
    }

    [Test]
    public void ThreadSleepCheckWhenThreadSleepIsUsedInSynchronousMethodDoesNotReport()
    {
        const string source = """
            using System.Threading;

            public sealed class Sample
            {
                public void Run()
                {
                    Thread.Sleep(20);
                }
            }
            """;

        var report = AnalyzeSource(new ThreadSleepCodeReviewCheck(), "A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void ThreadSleepCheckWhenUsingStaticThreadReportsSuggestion()
    {
        const string source = """
            using System.Threading.Tasks;
            using static System.Threading.Thread;

            public sealed class Sample
            {
                public async Task Run()
                {
                    Sleep(20);
                }
            }
            """;

        var report = AnalyzeSource(new ThreadSleepCodeReviewCheck(), "A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
    }

    [Test]
    public void ThreadSleepCheckWhenLocalSleepMethodIsUsedDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public void Run()
                {
                    Sleep(20);
                }

                private static void Sleep(int milliseconds)
                {
                }
            }
            """;

        var report = AnalyzeSource(new ThreadSleepCodeReviewCheck(), "A", source, Enumerable.Range(1, 13));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void ThrowExCheckWhenThrowExUsedInCatchReportsImportant()
    {
        const string source = """
            using System;

            public sealed class Sample
            {
                public void Run()
                {
                    try
                    {
                        ThrowSomething();
                    }
                    catch (Exception ex)
                    {
                        throw ex;
                    }
                }

                private static void ThrowSomething() => throw new Exception();
            }
            """;

        var report = AnalyzeSource(new ThrowExCodeReviewCheck(), "A", source, Enumerable.Range(1, 19));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Important));
        Assert.That(report.Findings[0].Message, Does.Contain("throw ex"));
    }

    [Test]
    public void ThrowExCheckWhenBareThrowUsedDoesNotReport()
    {
        const string source = """
            using System;

            public sealed class Sample
            {
                public void Run()
                {
                    try
                    {
                        ThrowSomething();
                    }
                    catch (Exception)
                    {
                        throw;
                    }
                }

                private static void ThrowSomething() => throw new Exception();
            }
            """;

        var report = AnalyzeSource(new ThrowExCodeReviewCheck(), "A", source, Enumerable.Range(1, 19));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void WarningSuppressionCheckWhenPragmaDisableAddedReportsImportant()
    {
        const string source = """
            public sealed class Sample
            {
                #pragma warning disable CS0168
                public void Run()
                {
                    int unused;
                }
                #pragma warning restore CS0168
            }
            """;

        var report = AnalyzeSource(new WarningSuppressionCodeReviewCheck(), "A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Important));
        Assert.That(report.Findings[0].Message, Does.Contain("#pragma warning disable"));
    }

    [Test]
    public void WarningSuppressionCheckWhenSuppressMessageAttributeAddedReportsImportant()
    {
        const string source = """
            using System.Diagnostics.CodeAnalysis;

            [SuppressMessage("Style", "IDE0001")]
            public sealed class Sample
            {
                public void Run()
                {
                }
            }
            """;

        var report = AnalyzeSource(new WarningSuppressionCodeReviewCheck(), "A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Important));
        Assert.That(report.Findings[0].Message, Does.Contain("SuppressMessage"));
    }

    [Test]
    public void AsyncVoidCheckWhenAsyncVoidMethodIsAddedReportsImportant()
    {
        const string source = """
            public sealed class Sample
            {
                public async void RunAsyncThing()
                {
                    await Task.Delay(1);
                }
            }
            """;

        var report = AnalyzeSource(new AsyncVoidCodeReviewCheck(), "A", source, Enumerable.Range(1, 8));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Important));
    }

    [Test]
    public void AsyncVoidCheckWhenMethodLooksLikeEventHandlerDoesNotReport()
    {
        const string source = """
            using System;
            using System.Threading.Tasks;

            public sealed class Sample
            {
                public async void OnClick(object? sender, EventArgs e)
                {
                    await Task.Delay(1);
                }
            }
            """;

        var report = AnalyzeSource(new AsyncVoidCodeReviewCheck(), "A", source, Enumerable.Range(1, 11));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AsyncVoidCheckWhenMethodLooksLikeCommandExecuteOverrideDoesNotReport()
    {
        const string source = """
            using System.Threading.Tasks;

            public abstract class CommandBase
            {
                public abstract void Execute(object parameter);
            }

            public sealed class SampleCommand : CommandBase
            {
                public override async void Execute(object parameter)
                {
                    await Task.Delay(1);
                }
            }
            """;

        var report = AnalyzeSource(new AsyncVoidCodeReviewCheck(), "A", source, Enumerable.Range(1, 15));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AsyncMethodNameSuffixCheckWhenAsyncMethodMissingSuffixReportsHint()
    {
        const string source = """
            using System.Threading.Tasks;

            public sealed class Sample
            {
                public async Task LoadData()
                {
                    await Task.Delay(1);
                }
            }
            """;

        var report = AnalyzeSource(new AsyncMethodNameSuffixCodeReviewCheck(), "A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
        Assert.That(report.Findings[0].Message, Does.Contain("LoadData"));
    }

    [Test]
    public void AsyncMethodNameSuffixCheckWhenAsyncTestMethodMissingSuffixDoesNotReport()
    {
        const string source = """
            using System.Threading.Tasks;
            using NUnit.Framework;

            [TestFixture]
            public sealed class SampleTests
            {
                [Test]
                public async Task LoadData()
                {
                    await Task.Delay(1);
                }
            }
            """;

        var report = AnalyzeSource(new AsyncMethodNameSuffixCodeReviewCheck(), "A", source, Enumerable.Range(1, 14), "Tests/SampleTests.cs");

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void AsyncMethodNameSuffixCheckWhenAsyncMethodOverridesBaseContractDoesNotReport()
    {
        const string source = """
            using System.Threading.Tasks;

            public abstract class CommandBase
            {
                public abstract void Execute(object parameter);
            }

            public sealed class SampleCommand : CommandBase
            {
                public override async void Execute(object parameter)
                {
                    await Task.Delay(1);
                }
            }
            """;

        var report = AnalyzeSource(new AsyncMethodNameSuffixCodeReviewCheck(), "A", source, Enumerable.Range(1, 15));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void TaskRunAsyncCheckWhenTaskRunUsesAsyncLambdaReportsSuggestion()
    {
        const string source = """
            using System.Threading.Tasks;

            public sealed class Sample
            {
                public Task RunAsync()
                {
                    return Task.Run(async () =>
                    {
                        await Task.Delay(1);
                    });
                }
            }
            """;

        var report = AnalyzeSource(new TaskRunAsyncCodeReviewCheck(), "A", source, Enumerable.Range(1, 13));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Suggestion));
        Assert.That(report.Findings[0].Message, Does.Contain("Consider awaiting the async work directly"));
    }

    [Test]
    public void TaskRunAsyncCheckWhenTaskRunUsesCpuBoundLambdaDoesNotReport()
    {
        const string source = """
            using System.Threading.Tasks;

            public sealed class Sample
            {
                public Task<int> RunAsync()
                {
                    return Task.Run(() => 42);
                }
            }
            """;

        var report = AnalyzeSource(new TaskRunAsyncCodeReviewCheck(), "A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void TaskRunAsyncCheckWhenTaskRunUsesSyncLambdaForBlockingWorkDoesNotReport()
    {
        const string source = """
            using System.Threading.Tasks;

            public sealed class Sample
            {
                public async Task RunAsync()
                {
                    await Task.Run(() => RunSync());
                }

                private void RunSync()
                {
                }
            }
            """;

        var report = AnalyzeSource(new TaskRunAsyncCodeReviewCheck(), "A", source, Enumerable.Range(1, 15));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void TaskRunAsyncCheckWhenTaskRunReturnsAsyncMethodWithoutAsyncLambdaDoesNotReport()
    {
        const string source = """
            using System.Threading.Tasks;

            public sealed class Sample
            {
                public Task RunAsync()
                {
                    return Task.Run(() => LoadAsync());
                }

                private Task LoadAsync() => Task.CompletedTask;
            }
            """;

        var report = AnalyzeSource(new TaskRunAsyncCodeReviewCheck(), "A", source, Enumerable.Range(1, 12));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void TaskRunAsyncCheckWhenFileHasUnrelatedMissingTypeStillReports()
    {
        const string source = """
            using System.Threading.Tasks;

            public sealed class Sample
            {
                public Task RunAsync()
                {
                    MissingType value = null;
                    return Task.Run(async () =>
                    {
                        await Task.Delay(1);
                    });
                }
            }
            """;

        var report = AnalyzeSource(new TaskRunAsyncCodeReviewCheck(), "A", source, Enumerable.Range(1, 14));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Message, Does.Contain("Task.Run"));
    }

    [Test]
    public void UnnecessaryTaskCompletionCheckWhenMethodReturnsTaskFromResultReportsSuggestion()
    {
        const string source = """
            using System.Threading.Tasks;

            public sealed class Sample
            {
                public static Task<bool> GetResultAsync(int a, int b)
                {
                    return Task.FromResult(true);
                }
            }
            """;

        var report = AnalyzeSource(new UnnecessaryTaskCompletionCodeReviewCheck(), "A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.UnnecessaryTaskCompletion));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Suggestion));
        Assert.That(report.Findings[0].Message, Does.Contain("without doing asynchronous work"));
    }

    [Test]
    public void UnnecessaryTaskCompletionCheckWhenMethodReturnsCompletedTaskReportsSuggestion()
    {
        const string source = """
            using System.Threading.Tasks;

            public sealed class Sample
            {
                public Task SaveAsync()
                {
                    return Task.CompletedTask;
                }
            }
            """;

        var report = AnalyzeSource(new UnnecessaryTaskCompletionCodeReviewCheck(), "A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.UnnecessaryTaskCompletion));
    }

    [Test]
    public void UnnecessaryTaskCompletionCheckWhenExpressionBodiedMethodReturnsTaskFromResultReportsSuggestion()
    {
        const string source = """
            using System.Threading.Tasks;

            public sealed class Sample
            {
                public Task<int> CountAsync() => Task.FromResult(1);
            }
            """;

        var report = AnalyzeSource(new UnnecessaryTaskCompletionCodeReviewCheck(), "A", source, Enumerable.Range(1, 6));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].LineNumber, Is.EqualTo(5));
    }

    [Test]
    public void UnnecessaryTaskCompletionCheckWhenMethodIsAsyncDoesNotReport()
    {
        const string source = """
            using System.Threading.Tasks;

            public sealed class Sample
            {
                public async Task<bool> GetResultAsync()
                {
                    await Task.Delay(1);
                    return true;
                }
            }
            """;

        var report = AnalyzeSource(new UnnecessaryTaskCompletionCodeReviewCheck(), "A", source, Enumerable.Range(1, 11));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void UnnecessaryTaskCompletionCheckWhenOverrideReturnsCompletedTaskDoesNotReport()
    {
        const string source = """
            using System.Threading.Tasks;

            public abstract class CommandBase
            {
                public abstract Task ExecuteAsync();
            }

            public sealed class SampleCommand : CommandBase
            {
                public override Task ExecuteAsync()
                {
                    return Task.CompletedTask;
                }
            }
            """;

        var report = AnalyzeSource(new UnnecessaryTaskCompletionCodeReviewCheck(), "A", source, Enumerable.Range(1, 15));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void UnnecessaryTaskCompletionCheckWhenInterfaceImplementationReturnsCompletedTaskDoesNotReport()
    {
        const string source = """
            using System.Threading.Tasks;

            public interface ICommand
            {
                Task ExecuteAsync();
            }

            public sealed class SampleCommand : ICommand
            {
                public Task ExecuteAsync()
                {
                    return Task.CompletedTask;
                }
            }
            """;

        var report = AnalyzeSource(new UnnecessaryTaskCompletionCodeReviewCheck(), "A", source, Enumerable.Range(1, 15));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void UnnecessaryTaskCompletionCheckWhenReturnLineIsUnchangedDoesNotReport()
    {
        const string source = """
            using System.Threading.Tasks;

            public sealed class Sample
            {
                public Task SaveAsync()
                {
                    return Task.CompletedTask;
                }
            }
            """;

        var report = AnalyzeSource(new UnnecessaryTaskCompletionCodeReviewCheck(), "M", source, [1]);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void MethodCanBeStaticCheckWhenPrivateMethodDoesNotUseInstanceStateReportsHint()
    {
        const string source = """
            using System;

            public sealed class Sample
            {
                private int SumAbs(int left, int right)
                {
                    return Math.Abs(left) + Math.Abs(right);
                }
            }
            """;

        var report = AnalyzeSource(new MethodCanBeStaticCodeReviewCheck(), "A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
        Assert.That(report.Findings[0].Message, Does.Contain("`SumAbs`"));
        Assert.That(report.Findings[0].Message, Does.Not.Contain("int left, int right"));
    }

    [Test]
    public void MethodCanBeStaticCheckWhenPublicMethodDoesNotUseInstanceStateDoesNotReport()
    {
        const string source = """
            using System;

            public sealed class Sample
            {
                public int SumAbs(int left, int right)
                {
                    return Math.Abs(left) + Math.Abs(right);
                }
            }
            """;

        var report = AnalyzeSource(new MethodCanBeStaticCodeReviewCheck(), "A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void MethodCanBeStaticCheckWhenMethodUsesInstanceFieldDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                private readonly int value = 3;

                public int Calculate(int input)
                {
                    return input + this.value;
                }
            }
            """;

        var report = AnalyzeSource(new MethodCanBeStaticCodeReviewCheck(), "A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void MethodCanBeStaticCheckWhenMethodUsesInstanceDelegateFieldDoesNotReport()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;

            public sealed class Sample
            {
                private readonly Func<string, CancellationToken, string[], Task<int>> runAsync;

                public Sample(Func<string, CancellationToken, string[], Task<int>> runAsync)
                {
                    this.runAsync = runAsync;
                }

                private async Task<int> FindMatchingRepositoryAsync(string repositoryRoot, CancellationToken cancellationToken)
                {
                    return await this.runAsync(repositoryRoot, cancellationToken, ["x"]);
                }
            }
            """;

        var report = AnalyzeSource(new MethodCanBeStaticCodeReviewCheck(), "A", source, Enumerable.Range(1, 16));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void MethodCanBeStaticCheckWhenMethodIsAlreadyStaticDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public static int Sum(int left, int right)
                {
                    return left + right;
                }
            }
            """;

        var report = AnalyzeSource(new MethodCanBeStaticCodeReviewCheck(), "A", source, Enumerable.Range(1, 8));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void MethodCanBeStaticCheckWhenFileIsTestFileDoesNotReport()
    {
        const string source = """
            using System;

            public sealed class SampleTests
            {
                [Test]
                public int SumAbs(int left, int right)
                {
                    return Math.Abs(left) + Math.Abs(right);
                }
            }
            """;

        var report = AnalyzeSource(new MethodCanBeStaticCodeReviewCheck(), "A", source, Enumerable.Range(1, 12), "Tests/SampleTests.cs");

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void MethodCanBeStaticCheckWhenMethodUsesUnresolvedCodeBehindMemberDoesNotReport()
    {
        const string source = """
            public partial class ReviewResultsWindow
            {
                private void SetPreviewText(string text)
                {
                    PreviewHeaderTextBlock.Text = text ?? string.Empty;
                }
            }
            """;

        var report = AnalyzeSource(
            new MethodCanBeStaticCodeReviewCheck(),
            "A",
            source,
            Enumerable.Range(1, 7),
            "Views/ReviewResultsWindow.axaml.cs");

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void MethodCanBeStaticCheckWhenMethodHasStaticEligibleOverloadReportsOnlyThatOverload()
    {
        const string source = """
            public sealed class Sample
            {
                private void MyMethod(string foo) => MyMethod(foo, 2, true);

                private void MyMethod(string foo, int num, bool enabled)
                {
                    _ = foo.Length + num;
                    _ = enabled;
                }
            }
            """;

        var report = AnalyzeSource(new MethodCanBeStaticCodeReviewCheck(), "A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].LineNumber, Is.EqualTo(5));
        Assert.That(report.Findings[0].Message, Is.EqualTo("Method `MyMethod` can likely be made static."));
    }

    [Test]
    public void MethodCanBeStaticCheckWhenMethodImplementsInterfaceDoesNotReport()
    {
        const string source = """
            public interface IPrinterProfile
            {
                int GetMediaWidthMm(string mediaName);
            }

            public sealed class PrinterProfile : IPrinterProfile
            {
                public int GetMediaWidthMm(string mediaName) => 0;
            }
            """;

        var report = AnalyzeSource(new MethodCanBeStaticCodeReviewCheck(), "A", source, Enumerable.Range(1, 11));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void MethodCanBeStaticCheckWhenInterfaceBaseCannotBeResolvedDoesNotReport()
    {
        const string source = """
            public sealed class PrinterProfile : IRemotePrinterProfile
            {
                public string GetActiveMedia() => null;
            }
            """;

        var report = AnalyzeSource(new MethodCanBeStaticCodeReviewCheck(), "A", source, Enumerable.Range(1, 6));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void RedundantSelfLookupCheckWhenTypeCallsGetTypeWithThisReportsHint()
    {
        const string source = """
            public class Owner
            {
                public static Owner GetOwner(Owner value) => value;
            }

            public sealed class Derived : Owner
            {
                public string Describe()
                {
                    return Owner.GetOwner(this).ToString();
                }
            }
            """;

        var report = AnalyzeSource(new RedundantSelfLookupCodeReviewCheck(), "A", source, Enumerable.Range(1, 12));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
        Assert.That(report.Findings[0].Message, Does.Contain("Owner.GetOwner(this)"));
    }

    [Test]
    public void BooleanLiteralComparisonCheckWhenComparedWithTrueReportsHint()
    {
        const string source = """
            public sealed class Sample
            {
                public bool Run(bool myBool)
                {
                    if (myBool == true)
                        return true;

                    if (myBool != true)
                        return false;

                    return myBool;
                }
            }
            """;

        var report = AnalyzeSource(new BooleanLiteralComparisonCodeReviewCheck(), "A", source, Enumerable.Range(1, 16));

        Assert.That(report.Findings, Has.Count.EqualTo(2));
        Assert.That(report.Findings.All(finding => finding.Severity == CodeReviewFindingSeverity.Hint), Is.True);
        Assert.That(report.Findings.Any(finding => finding.Message.Contains("`myBool == true`", StringComparison.Ordinal)), Is.True);
        Assert.That(report.Findings.Any(finding => finding.Message.Contains("`myBool != true`", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void BooleanLiteralComparisonCheckWhenComparedWithFalseReportsHint()
    {
        const string source = """
            public sealed class Sample
            {
                public bool Run(bool myBool)
                {
                    if (myBool == false)
                        return false;

                    if (myBool != false)
                        return true;

                    return myBool;
                }
            }
            """;

        var report = AnalyzeSource(new BooleanLiteralComparisonCodeReviewCheck(), "A", source, Enumerable.Range(1, 16));

        Assert.That(report.Findings, Has.Count.EqualTo(2));
        Assert.That(report.Findings.Any(finding => finding.Message.Contains("`myBool == false`", StringComparison.Ordinal)), Is.True);
        Assert.That(report.Findings.Any(finding => finding.Message.Contains("`myBool != false`", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void BooleanLiteralComparisonCheckWhenLiteralIsOnLeftReportsHint()
    {
        const string source = """
            public sealed class Sample
            {
                public bool Run(bool myBool)
                {
                    return true == myBool;
                }
            }
            """;

        var report = AnalyzeSource(new BooleanLiteralComparisonCodeReviewCheck(), "A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Message, Does.Contain("`true == myBool`"));
        Assert.That(report.Findings[0].Message, Does.Contain("`myBool`"));
    }

    [Test]
    public void BooleanLiteralComparisonCheckWhenNullableBooleanIsComparedDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public bool Run(bool? myBool)
                {
                    return myBool == true;
                }
            }
            """;

        var report = AnalyzeSource(new BooleanLiteralComparisonCodeReviewCheck(), "A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void UnnecessaryCastCheckWhenExpressionAlreadyHasTargetTypeReportsHint()
    {
        const string source = """
            public sealed class Sample
            {
                public int Run()
                {
                    int bar = 1;
                    int foo = (int)bar;
                    return foo;
                }
            }
            """;

        var report = AnalyzeSource(new UnnecessaryCastCodeReviewCheck(), "A", source, Enumerable.Range(1, 11));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
        Assert.That(report.Findings[0].Message, Does.Contain("Unnecessary cast to `int`"));
    }

    [Test]
    public void UnnecessaryCastCheckWhenCastChangesTypeDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public int Run()
                {
                    long bar = 1;
                    int foo = (int)bar;
                    return foo;
                }
            }
            """;

        var report = AnalyzeSource(new UnnecessaryCastCodeReviewCheck(), "A", source, Enumerable.Range(1, 11));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void UnnecessaryCastCheckWhenCastIsOutsideAddedLinesDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public int Run()
                {
                    int bar = 1;
                    int foo = (int)bar;
                    return foo;
                }
            }
            """;

        var report = AnalyzeSource(new UnnecessaryCastCodeReviewCheck(), "M", source, [1, 2, 3]);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void UnnecessaryEnumMemberValueCheckWhenValuesAreDefaultSequenceReportsHint()
    {
        const string source = """
            public enum CodeReviewCheckScope
            {
                AddedLinesOnly = 0,
                ChangedFileSet = 1,
                ExtraScope = 2
            }
            """;

        var report = AnalyzeSource(new UnnecessaryEnumMemberValueCodeReviewCheck(), "A", source, Enumerable.Range(1, 8));

        Assert.That(report.Findings, Has.Count.EqualTo(3));
        Assert.That(report.Findings.All(finding => finding.Severity == CodeReviewFindingSeverity.Hint), Is.True);
        Assert.That(report.Findings.All(finding => finding.RuleId == CodeReviewRuleIds.UnnecessaryEnumMemberValue), Is.True);
        Assert.That(report.Findings[0].Message, Does.Contain("`AddedLinesOnly`"));
    }

    [Test]
    public void UnnecessaryEnumMemberValueCheckWhenNonSequentialValuesAreUsedDoesNotReport()
    {
        const string source = """
            public enum Mode
            {
                Unknown = -1,
                Primary = 10,
                Secondary = 20
            }
            """;

        var report = AnalyzeSource(new UnnecessaryEnumMemberValueCodeReviewCheck(), "A", source, Enumerable.Range(1, 8));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void UnnecessaryEnumMemberValueCheckWhenExplicitMemberOutsideAddedLinesDoesNotReport()
    {
        const string source = """
            public enum Scope
            {
                First = 0,
                Second = 1
            }
            """;

        var report = AnalyzeSource(new UnnecessaryEnumMemberValueCodeReviewCheck(), "M", source, [1, 2]);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void UnnecessaryEnumMemberValueCheckWhenFlagStyleShiftExpressionIsUsedDoesNotReport()
    {
        const string source = """
            public enum TextStyle
            {
                Bold = 1 << 0,
                Italic = 1 << 1
            }
            """;

        var report = AnalyzeSource(new UnnecessaryEnumMemberValueCodeReviewCheck(), "A", source, Enumerable.Range(1, 7));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void UnnecessaryVerbatimStringPrefixCheckWhenSimpleVerbatimLiteralReportsHint()
    {
        const string source = """
            public sealed class Sample
            {
                public string Run()
                {
                    var foo = @"hello";
                    return foo;
                }
            }
            """;

        var report = AnalyzeSource(new UnnecessaryVerbatimStringPrefixCodeReviewCheck(), "A", source, Enumerable.Range(1, 11));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
        Assert.That(report.Findings[0].Message, Does.Contain("verbatim string prefix `@`"));
    }

    [Test]
    public void UnnecessaryVerbatimStringPrefixCheckWhenVerbatimContainsBackslashesDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public string Run()
                {
                    var foo = @"c:\temp\hello.txt";
                    return foo;
                }
            }
            """;

        var report = AnalyzeSource(new UnnecessaryVerbatimStringPrefixCodeReviewCheck(), "A", source, Enumerable.Range(1, 11));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void UnnecessaryVerbatimStringPrefixCheckWhenVerbatimContainsQuoteEscapesDoesNotReport()
    {
        const string source = """"
            public sealed class Sample
            {
                public string Run()
                {
                    var foo = @"say ""hello""";
                    return foo;
                }
            }
            """";

        var report = AnalyzeSource(new UnnecessaryVerbatimStringPrefixCodeReviewCheck(), "A", source, Enumerable.Range(1, 11));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void UnnecessaryVerbatimStringPrefixCheckWhenSimpleVerbatimInterpolatedStringReportsHint()
    {
        const string source = """
            public sealed class Sample
            {
                public string Run(string name)
                {
                    return $@"hello {name}";
                }
            }
            """;

        var report = AnalyzeSource(new UnnecessaryVerbatimStringPrefixCodeReviewCheck(), "A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Message, Does.Contain("verbatim string prefix `@`"));
    }

    [Test]
    public void UnnecessaryVerbatimStringPrefixCheckWhenInterpolatedStringContainsBackslashesDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public string Run(string fileName)
                {
                    return $@"c:\temp\{fileName}";
                }
            }
            """;

        var report = AnalyzeSource(new UnnecessaryVerbatimStringPrefixCodeReviewCheck(), "A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void UnobservedTaskResultCheckWhenTaskCallIsIgnoredReportsSuggestion()
    {
        const string source = """
            using System.Threading.Tasks;

            public sealed class Sample
            {
                public async Task RunAsync()
                {
                    FireAndForgetAsync();
                    await Task.Delay(1);
                }

                private static Task FireAndForgetAsync() => Task.CompletedTask;
            }
            """;

        var report = AnalyzeSource(new UnobservedTaskResultCodeReviewCheck(), "A", source, Enumerable.Range(1, 15));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Suggestion));
        Assert.That(report.Findings[0].Message, Does.Contain("`FireAndForgetAsync`"));
    }

    [Test]
    public void UnobservedTaskResultCheckWhenTaskIsAwaitedDoesNotReport()
    {
        const string source = """
            using System.Threading.Tasks;

            public sealed class Sample
            {
                public async Task RunAsync()
                {
                    await FireAndForgetAsync();
                }

                private static Task FireAndForgetAsync() => Task.CompletedTask;
            }
            """;

        var report = AnalyzeSource(new UnobservedTaskResultCodeReviewCheck(), "A", source, Enumerable.Range(1, 13));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void UnobservedTaskResultCheckWhenTaskRunIsIgnoredReportsSuggestion()
    {
        const string source = """
            using System.Threading.Tasks;

            public sealed class Sample
            {
                public void Run()
                {
                    Task.Run(() => DoWork());
                }

                private static void DoWork()
                {
                }
            }
            """;

        var report = AnalyzeSource(new UnobservedTaskResultCodeReviewCheck(), "A", source, Enumerable.Range(1, 14));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Suggestion));
        Assert.That(report.Findings[0].Message, Does.Contain("`Task.Run`"));
    }

    [Test]
    public void UnobservedTaskResultCheckWhenValueTaskCallIsIgnoredReportsSuggestion()
    {
        const string source = """
            using System.Threading.Tasks;

            public sealed class Sample
            {
                public void Run()
                {
                    DoAsync();
                }

                private static ValueTask DoAsync() => ValueTask.CompletedTask;
            }
            """;

        var report = AnalyzeSource(new UnobservedTaskResultCodeReviewCheck(), "A", source, Enumerable.Range(1, 13));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Message, Does.Contain("`DoAsync`"));
    }

    [Test]
    public void DisposeMethodWithoutIDisposableCheckWhenClassDefinesDisposeWithoutInterfaceReportsSuggestion()
    {
        const string source = """
            public sealed class Sample
            {
                public void Dispose()
                {
                }
            }
            """;

        var report = AnalyzeSource(new DisposeMethodWithoutIDisposableCodeReviewCheck(), "A", source, Enumerable.Range(1, 7));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Suggestion));
        Assert.That(report.Findings[0].Message, Does.Contain("`IDisposable`"));
        Assert.That(report.Findings[0].Message, Does.Contain("`Sample`"));
    }

    [Test]
    public void DisposeMethodWithoutIDisposableCheckWhenClassImplementsInterfaceDoesNotReport()
    {
        const string source = """
            using System;

            public sealed class Sample : IDisposable
            {
                public void Dispose()
                {
                }
            }
            """;

        var report = AnalyzeSource(new DisposeMethodWithoutIDisposableCodeReviewCheck(), "A", source, Enumerable.Range(1, 9));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void DisposeMethodWithoutIDisposableCheckWhenBaseTypeImplementsInterfaceDoesNotReport()
    {
        const string source = """
            using System;

            public class SampleBase : IDisposable
            {
                public virtual void Dispose()
                {
                }
            }

            public sealed class Sample : SampleBase
            {
                public override void Dispose()
                {
                }
            }
            """;

        var report = AnalyzeSource(new DisposeMethodWithoutIDisposableCodeReviewCheck(), "A", source, Enumerable.Range(1, 16));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void DisposeMethodWithoutIDisposableCheckWhenInheritanceCannotBeResolvedDoesNotReport()
    {
        const string source = """
            public sealed class Sample : UnknownBaseType
            {
                public void Dispose()
                {
                }
            }
            """;

        var report = AnalyzeSource(new DisposeMethodWithoutIDisposableCodeReviewCheck(), "A", source, Enumerable.Range(1, 7));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void EmptyDisposeCheckWhenDisposeBodyIsEmptyReportsSuggestion()
    {
        const string source = """
            using System;

            public sealed class Sample : IDisposable
            {
                public void Dispose()
                {
                }
            }
            """;

        var report = AnalyzeSource(new EmptyDisposeCodeReviewCheck(), "A", source, Enumerable.Range(1, 9));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.EmptyDispose));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Suggestion));
        Assert.That(report.Findings[0].Message, Does.Contain("`Dispose()`"));
    }

    [Test]
    public void EmptyDisposeCheckWhenDisposeBodyContainsOnlyCommentReportsSuggestion()
    {
        const string source = """
            using System;

            public sealed class Sample : IDisposable
            {
                public void Dispose()
                {
                    // Nothing to dispose.
                }
            }
            """;

        var report = AnalyzeSource(new EmptyDisposeCodeReviewCheck(), "A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
    }

    [Test]
    public void EmptyDisposeCheckWhenDisposeContainsCleanupDoesNotReport()
    {
        const string source = """
            using System;

            public sealed class Sample : IDisposable
            {
                private readonly IDisposable m_dependency;

                public Sample(IDisposable dependency)
                {
                    m_dependency = dependency;
                }

                public void Dispose()
                {
                    m_dependency.Dispose();
                }
            }
            """;

        var report = AnalyzeSource(new EmptyDisposeCodeReviewCheck(), "A", source, Enumerable.Range(1, 17));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void EmptyDisposeCheckWhenDisposeMethodWasNotAddedDoesNotReport()
    {
        const string source = """
            using System;

            public sealed class Sample : IDisposable
            {
                public void Dispose()
                {
                }
            }
            """;

        var report = AnalyzeSource(new EmptyDisposeCodeReviewCheck(), "M", source, [1]);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void DisposableNotDisposedCheckWhenDisposableCreatedWithoutUsingReportsSuggestion()
    {
        const string source = """
            using System.IO;

            public sealed class Sample
            {
                public void Run()
                {
                    var stream = new MemoryStream();
                }
            }
            """;

        var report = AnalyzeSource(new DisposableNotDisposedCodeReviewCheck(), "A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Suggestion));
        Assert.That(report.Findings[0].Message, Does.Contain("`MemoryStream`"));
    }

    [Test]
    public void DisposableNotDisposedCheckWhenUsingDeclarationIsUsedDoesNotReport()
    {
        const string source = """
            using System.IO;

            public sealed class Sample
            {
                public void Run()
                {
                    using var stream = new MemoryStream();
                }
            }
            """;

        var report = AnalyzeSource(new DisposableNotDisposedCodeReviewCheck(), "A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void DisposableNotDisposedCheckWhenDisposeCalledLaterDoesNotReport()
    {
        const string source = """
            using System.IO;

            public sealed class Sample
            {
                public void Run()
                {
                    var stream = new MemoryStream();
                    stream.Dispose();
                }
            }
            """;

        var report = AnalyzeSource(new DisposableNotDisposedCodeReviewCheck(), "A", source, Enumerable.Range(1, 11));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void DisposableNotDisposedCheckWhenDisposableIsReturnedDoesNotReport()
    {
        const string source = """
            using System.IO;

            public sealed class Sample
            {
                public MemoryStream CreateStream()
                {
                    var stream = new MemoryStream();
                    return stream;
                }
            }
            """;

        var report = AnalyzeSource(new DisposableNotDisposedCodeReviewCheck(), "A", source, Enumerable.Range(1, 11));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void MultipleEnumerationCheckWhenIEnumerableIsEnumeratedTwiceReportsSuggestion()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;

            public sealed class Sample
            {
                public int Run(IEnumerable<int> values)
                {
                    var first = values.First();
                    var count = values.Count();
                    return first + count;
                }
            }
            """;

        var report = AnalyzeSource(new MultipleEnumerationCodeReviewCheck(), "A", source, Enumerable.Range(1, 14));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Suggestion));
        Assert.That(report.Findings[0].Message, Does.Contain("`values`"));
    }

    [Test]
    public void MultipleEnumerationCheckWhenCollectionTypeIsCheapDoesNotReport()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;

            public sealed class Sample
            {
                public int Run(List<int> values)
                {
                    var first = values.First();
                    var count = values.Count();
                    return first + count;
                }
            }
            """;

        var report = AnalyzeSource(new MultipleEnumerationCodeReviewCheck(), "A", source, Enumerable.Range(1, 14));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void MultipleEnumerationCheckWhenDifferentFilteredSequencesAreEnumeratedDoesNotReport()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;

            public sealed class Sample
            {
                private readonly IEnumerable<int> previousValues = new[] { 1, 2, 3 };
                private readonly IEnumerable<int> values = new[] { 4, 5, 6 };

                public int Run()
                {
                    var total = 0;
                    foreach (var value in previousValues.Where(v => v > 1))
                        total += value;
                    foreach (var value in values.Where(v => v > 4))
                        total += value;
                    return total;
                }
            }
            """;

        var report = AnalyzeSource(new MultipleEnumerationCodeReviewCheck(), "A", source, Enumerable.Range(1, 20));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void MultipleEnumerationCheckWhenSameSequenceFilteredTwiceInForeachReportsSuggestion()
    {
        const string source = """
            using System.Collections.Generic;
            using System.Linq;

            public sealed class Sample
            {
                public int Run(IEnumerable<int> values)
                {
                    var total = 0;
                    foreach (var value in values.Where(v => v > 1))
                        total += value;
                    foreach (var value in values.Where(v => v > 2))
                        total += value;
                    return total;
                }
            }
            """;

        var report = AnalyzeSource(new MultipleEnumerationCodeReviewCheck(), "A", source, Enumerable.Range(1, 18));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Suggestion));
        Assert.That(report.Findings[0].Message, Does.Contain("`values`"));
    }

    [Test]
    public void MultipleEnumerationCheckWhenOfTypeChainsUseDifferentReceiversDoesNotReport()
    {
        const string source = """
            using System.Linq;
            using Microsoft.CodeAnalysis;
            using Microsoft.CodeAnalysis.CSharp.Syntax;

            public sealed class Sample
            {
                public void Run(SyntaxNode node, SyntaxNode variable, SyntaxNode fixRoot)
                {
                    _ = node.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
                    _ = fixRoot.DescendantNodes().OfType<VariableDeclaratorSyntax>().FirstOrDefault();
                    _ = variable.Ancestors().OfType<LocalDeclarationStatementSyntax>().First();
                }
            }
            """;

        var report = AnalyzeSource(new MultipleEnumerationCodeReviewCheck(), "A", source, Enumerable.Range(1, 16));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void PublicMutableStaticStateCheckWhenPublicStaticFieldIsMutableReportsImportant()
    {
        const string source = """
            public sealed class Sample
            {
                public static int Counter = 0;
            }
            """;

        var report = AnalyzeSource(new PublicMutableStaticStateCodeReviewCheck(), "A", source, Enumerable.Range(1, 5));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Important));
        Assert.That(report.Findings[0].Message, Does.Contain("`Counter`"));
    }

    [Test]
    public void PublicMutableStaticStateCheckWhenPublicStaticPropertyHasSetterReportsImportant()
    {
        const string source = """
            public sealed class Sample
            {
                public static string Name { get; set; }
            }
            """;

        var report = AnalyzeSource(new PublicMutableStaticStateCodeReviewCheck(), "A", source, Enumerable.Range(1, 5));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Important));
        Assert.That(report.Findings[0].Message, Does.Contain("`Name`"));
    }

    [Test]
    public void PublicMutableStaticStateCheckWhenFieldReadonlyAndPropertyPrivateSetDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public static readonly int Counter = 0;
                public static string Name { get; private set; }
            }
            """;

        var report = AnalyzeSource(new PublicMutableStaticStateCodeReviewCheck(), "A", source, Enumerable.Range(1, 6));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void UnusedPrivateMemberCheckWhenPrivateFieldMethodAndPropertyAreUnusedReportsHint()
    {
        const string source = """
            public sealed class Sample
            {
                private readonly int count = 1;

                private int Value { get; set; }

                private void DoWork()
                {
                }
            }
            """;

        var report = AnalyzeSource(new UnusedPrivateMemberCodeReviewCheck(), "A", source, Enumerable.Range(1, 12));

        Assert.That(report.Findings, Has.Count.EqualTo(3));
        Assert.That(report.Findings.All(finding => finding.Severity == CodeReviewFindingSeverity.Hint), Is.True);
        Assert.That(report.Findings.Any(finding => finding.Message.Contains("field `count`", StringComparison.Ordinal)), Is.True);
        Assert.That(report.Findings.Any(finding => finding.Message.Contains("property `Value`", StringComparison.Ordinal)), Is.True);
        Assert.That(report.Findings.Any(finding => finding.Message.Contains("method `DoWork`", StringComparison.Ordinal)), Is.True);
    }

    [Test]
    public void UnusedPrivateMemberCheckWhenPrivateMembersAreUsedDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                private int count;

                private int Value { get; set; }

                public int Run()
                {
                    this.count++;
                    this.Value = this.count;
                    this.DoWork();
                    return this.Value;
                }

                private void DoWork()
                {
                }
            }
            """;

        var report = AnalyzeSource(new UnusedPrivateMemberCodeReviewCheck(), "A", source, Enumerable.Range(1, 20));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void UnusedPrivateMemberCheckWhenContainingTypeIsPartialDoesNotReport()
    {
        const string source = """
            public partial class Sample
            {
                private int count;
                private int Value { get; set; }
                private void DoWork()
                {
                }
            }
            """;

        var report = AnalyzeSource(new UnusedPrivateMemberCodeReviewCheck(), "A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void UnusedPrivateMemberCheckWhenGenericPrivateMethodIsUsedDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public void Run()
                {
                    Execute<int>();
                }

                private void Execute<T>()
                {
                }
            }
            """;

        var report = AnalyzeSource(new UnusedPrivateMemberCodeReviewCheck(), "A", source, Enumerable.Range(1, 12));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void UnusedPrivateMemberCheckWhenPrivateExtensionMethodIsUsedViaExtensionSyntaxDoesNotReport()
    {
        const string source = """
            public static class SampleExtensions
            {
                public static void Run(this string input)
                {
                    input.TrySetColor("Accent", 123);
                }

                private static void TrySetColor(this string input, string key, object value)
                {
                }
            }
            """;

        var report = AnalyzeSource(new UnusedPrivateMemberCodeReviewCheck(), "A", source, Enumerable.Range(1, 13));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void UnusedPrivateMemberCheckWhenMethodIsExplicitInterfaceImplementationDoesNotReport()
    {
        const string source = """
            using System.Collections;
            using System.Collections.Generic;

            public sealed class Sample : IEnumerable<int>
            {
                public IEnumerator<int> GetEnumerator()
                {
                    yield break;
                }

                IEnumerator IEnumerable.GetEnumerator() =>
                    GetEnumerator();
            }
            """;

        var report = AnalyzeSource(new UnusedPrivateMemberCodeReviewCheck(), "A", source, Enumerable.Range(1, 14));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void DisposableNotDisposedCheckWhenPassedToConstructorDoesNotReport()
    {
        const string source = """
            using System.Net.Http;

            public sealed class Sample
            {
                public HttpClient CreateClient()
                {
                    var handler = new HttpClientHandler();
                    return new HttpClient(handler);
                }
            }
            """;

        var report = AnalyzeSource(new DisposableNotDisposedCodeReviewCheck(), "A", source, Enumerable.Range(1, 12));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void DisposableNotDisposedCheckWhenPassedToMethodDoesNotReport()
    {
        const string source = """
            using System.IO;

            public sealed class Sample
            {
                public void Run()
                {
                    var stream = new MemoryStream();
                    Consume(stream);
                }

                private static void Consume(Stream stream)
                {
                }
            }
            """;

        var report = AnalyzeSource(new DisposableNotDisposedCodeReviewCheck(), "A", source, Enumerable.Range(1, 16));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void UnusedPrivateMemberCheckWhenPropertyIsExplicitInterfaceImplementationDoesNotReport()
    {
        const string source = """
            public interface IAudioSource
            {
                int ChannelCount { get; }
            }

            public sealed class Sample : IAudioSource
            {
                private int VoiceCount => 3;

                int IAudioSource.ChannelCount => VoiceCount;
            }
            """;

        var report = AnalyzeSource(new UnusedPrivateMemberCodeReviewCheck(), "A", source, Enumerable.Range(1, 12));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void UnusedPrivateMemberCheckWhenExplicitInterfacePropertyHasGetterAndSetterDoesNotReport()
    {
        const string source = """
            public interface IAudioSource
            {
                ulong InstanceId { get; set; }
            }

            public sealed class Sample : IAudioSource
            {
                private ulong instanceId;

                ulong IAudioSource.InstanceId
                {
                    get => instanceId;
                    set => instanceId = value;
                }
            }
            """;

        var report = AnalyzeSource(new UnusedPrivateMemberCodeReviewCheck(), "A", source, Enumerable.Range(1, 15));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void UnusedPrivateMemberCheckWhenPrivateProgramMainExistsDoesNotReport()
    {
        const string source = """
            using System.Threading.Tasks;

            public static class Program
            {
                private static async Task Main(string[] args)
                {
                    await Task.Delay(1);
                }
            }
            """;

        var report = AnalyzeSource(new UnusedPrivateMemberCodeReviewCheck(), "A", source, Enumerable.Range(1, 11), "Program.cs");

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void UnusedLocalVariableCheckWhenVariableIsNeverReadReportsHint()
    {
        const string source = """
            public sealed class Sample
            {
                public int Run()
                {
                    var unused = 123;
                    return 7;
                }
            }
            """;

        var report = AnalyzeSource(new UnusedLocalVariableCodeReviewCheck(), "A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Severity, Is.EqualTo(CodeReviewFindingSeverity.Hint));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo(CodeReviewRuleIds.UnusedLocalVariable));
        Assert.That(report.Findings[0].Message, Does.Contain("`unused`"));
    }

    [Test]
    public void UnusedLocalVariableCheckWhenVariableIsReadDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public int Run()
                {
                    var used = 123;
                    return used;
                }
            }
            """;

        var report = AnalyzeSource(new UnusedLocalVariableCodeReviewCheck(), "A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void UnusedLocalVariableCheckWhenVariableIsOnlyWrittenStillReportsHint()
    {
        const string source = """
            public sealed class Sample
            {
                public int Run()
                {
                    var value = 1;
                    value = 2;
                    return 3;
                }
            }
            """;

        var report = AnalyzeSource(new UnusedLocalVariableCodeReviewCheck(), "A", source, Enumerable.Range(1, 11));

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].Message, Does.Contain("`value`"));
    }

    [Test]
    public void UnusedLocalVariableCheckWhenVariableNameIsUnderscoreDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public int Run()
                {
                    var _ = 1;
                    return 2;
                }
            }
            """;

        var report = AnalyzeSource(new UnusedLocalVariableCodeReviewCheck(), "A", source, Enumerable.Range(1, 10));

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void UnusedLocalVariableCheckWhenLineIsOutsideAddedSetDoesNotReport()
    {
        const string source = """
            public sealed class Sample
            {
                public int Run()
                {
                    var unused = 123;
                    return 7;
                }
            }
            """;

        var report = AnalyzeSource(new UnusedLocalVariableCodeReviewCheck(), "M", source, [1]);

        Assert.That(report.Findings, Is.Empty);
    }


    private static CodeSmellReport AnalyzeSource(ICodeReviewCheck check, string status, string source, IEnumerable<int> addedLines, string path = "Packages/CSharp.Core/Sample.cs")
    {
        var normalizedSource = (source ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = normalizedSource.Split('\n');
        var changedFile = new CodeReviewChangedFile(
            status,
            path,
            path,
            normalizedSource,
            lines,
            new HashSet<int>(addedLines ?? []));
        var context = new CodeReviewAnalysisContext(
            [changedFile],
            new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        var report = new CodeSmellReport();
        check.Analyze(context, report);
        return report;
    }
}
