using DTC.Core;
using DTC.Core.Extensions;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ReviewG33k.Services;
using ReviewG33k.Services.Checks;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class FixableCodeReviewChecksTests
{
    [Test]
    public void UnusedUsingCheckTryFixRemovesTheLineAndDoesNotLeaveTripleBlankLines()
    {
        using var tempFile = new TempFile(".cs");
        const string source = """
            using System;
            using System.Text;

            public sealed class Sample
            {
                public string X() => new StringBuilder().ToString();
            }
            """;

        tempFile.WriteAllText(source);

        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Hint,
            "unused-usings-roslyn",
            "Sample.cs",
            1,
            "Unused using");

        var check = new UnusedUsingRoslynCodeReviewCheck();
        var success = check.TryFix(finding, tempFile, out var message);

        Assert.That(success, Is.True);
        Assert.That(message, Is.Not.Empty);

        var updated = tempFile.ReadAllText();
        Assert.That(updated, Does.Not.Contain("using System;"));
        Assert.That(updated, Does.Contain("using System.Text;"));
        Assert.That(updated, Does.Not.Contain("\n\n\n"));
    }

    [Test]
    public void UnusedUsingCheckTryFixWhenLineIsGlobalUsingReturnsFalse()
    {
        using var tempFile = new TempFile(".cs");
        const string source = """
            global using System.Text;

            public sealed class Sample
            {
                public int Add(int a, int b) => a + b;
            }
            """;

        tempFile.WriteAllText(source);

        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Hint,
            "unused-usings-roslyn",
            "Sample.cs",
            1,
            "Unused global using");

        var check = new UnusedUsingRoslynCodeReviewCheck();
        var success = check.TryFix(finding, tempFile, out var message);

        Assert.That(success, Is.False);
        Assert.That(message, Is.EqualTo("Global using directives are excluded from this check."));
        Assert.That(tempFile.ReadAllText().Replace("\r\n", "\n"), Is.EqualTo(source.Replace("\r\n", "\n")));
    }

    [Test]
    public void ThrowExCheckTryFixReplacesWithThrow()
    {
        using var tempFile = new TempFile(".cs");
        const string source = """
            using System;

            public sealed class Sample
            {
                public void Go()
                {
                    try
                    {
                        throw new Exception("x");
                    }
                    catch (Exception ex)
                    {
                        throw ex;
                    }
                }
            }
            """;

        tempFile.WriteAllText(source);

        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Important,
            "throw-ex-in-catch",
            "Sample.cs",
            13,
            "Use throw; instead of throw ex;");

        var check = new ThrowExCodeReviewCheck();
        var success = check.TryFix(finding, tempFile, out var message);

        Assert.That(success, Is.True);
        Assert.That(message, Is.Not.Empty);

        var updated = tempFile.ReadAllText();
        Assert.That(updated, Does.Not.Contain("throw ex;"));
        Assert.That(updated, Does.Contain("throw;"));
        Assert.That(updated, Does.Not.Contain("\n\n\n"));
    }

    [Test]
    public void IfElseBraceConsistencyCheckTryFixRemovesUnnecessaryBraceWhenSafe()
    {
        using var tempFile = new TempFile(".cs");
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

        tempFile.WriteAllText(source);

        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Suggestion,
            CodeReviewRuleIds.IfElseBraceConsistency,
            "Sample.cs",
            5,
            "If/else branches should both use braces when either branch uses braces.");

        var check = new IfElseBraceConsistencyCodeReviewCheck();
        var success = check.TryFix(finding, tempFile, out var message);

        Assert.That(success, Is.True);
        Assert.That(message, Is.Not.Empty);

        var updated = tempFile.ReadAllText();
        Assert.That(updated, Does.Not.Contain("\n\n\n"));

        var root = CSharpSyntaxTree.ParseText(updated).GetCompilationUnitRoot();
        var ifStatement = root.DescendantNodes().OfType<IfStatementSyntax>().Single();
        Assert.That(ifStatement.Statement, Is.Not.TypeOf<BlockSyntax>());
        Assert.That(ifStatement.Else.Statement, Is.Not.TypeOf<BlockSyntax>());
    }

    [Test]
    public void IfElseBraceConsistencyCheckTryFixAddsMissingBracesWhenRemovalIsUnsafe()
    {
        using var tempFile = new TempFile(".cs");
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
                        return 0;
                }
            }
            """;

        tempFile.WriteAllText(source);

        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Suggestion,
            CodeReviewRuleIds.IfElseBraceConsistency,
            "Sample.cs",
            5,
            "If/else branches should both use braces when either branch uses braces.");

        var check = new IfElseBraceConsistencyCodeReviewCheck();
        var success = check.TryFix(finding, tempFile, out var message);

        Assert.That(success, Is.True);
        Assert.That(message, Is.Not.Empty);

        var updated = tempFile.ReadAllText();
        Assert.That(updated, Does.Not.Contain("\n\n\n"));
        var normalized = updated.Replace("\r\n", "\n");
        Assert.That(normalized, Does.Not.Contain("else\n\n{"));
        Assert.That(normalized, Does.Match(@"else\s*\n\s*\{\s*\n\s*return 0;\s*\n\s*\}"));

        var root = CSharpSyntaxTree.ParseText(updated).GetCompilationUnitRoot();
        var ifStatement = root.DescendantNodes().OfType<IfStatementSyntax>().Single();
        Assert.That(ifStatement.Statement, Is.TypeOf<BlockSyntax>());
        Assert.That(ifStatement.Else.Statement, Is.TypeOf<BlockSyntax>());
    }

    [Test]
    public void IfElseUnnecessaryBracesCheckTryFixRemovesBracesFromBothBranches()
    {
        using var tempFile = new TempFile(".cs");
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

        tempFile.WriteAllText(source);

        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Hint,
            CodeReviewRuleIds.IfElseUnnecessaryBraces,
            "Sample.cs",
            5,
            "If/else braces are unnecessary when each branch contains a single simple statement.");

        var check = new IfElseUnnecessaryBracesCodeReviewCheck();
        var success = check.TryFix(finding, tempFile, out var message);

        Assert.That(success, Is.True);
        Assert.That(message, Is.Not.Empty);

        var updated = tempFile.ReadAllText();
        Assert.That(updated, Does.Not.Contain("\n\n\n"));

        var root = CSharpSyntaxTree.ParseText(updated).GetCompilationUnitRoot();
        var ifStatement = root.DescendantNodes().OfType<IfStatementSyntax>().Single();
        Assert.That(ifStatement.Statement, Is.Not.TypeOf<BlockSyntax>());
        Assert.That(ifStatement.Else.Statement, Is.Not.TypeOf<BlockSyntax>());
    }

    [Test]
    public void IfElseUnnecessaryBracesCheckTryFixWhenStatementSpansMultipleLinesReturnsFalse()
    {
        using var tempFile = new TempFile(".cs");
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

        tempFile.WriteAllText(source);

        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Hint,
            CodeReviewRuleIds.IfElseUnnecessaryBraces,
            "Sample.cs",
            5,
            "If/else braces are unnecessary when each branch contains a single simple statement.");

        var check = new IfElseUnnecessaryBracesCodeReviewCheck();
        var success = check.TryFix(finding, tempFile, out var message);

        Assert.That(success, Is.False);
        Assert.That(message, Is.EqualTo("Target line does not contain an if/else with unnecessary braces."));
        Assert.That(tempFile.ReadAllText().Replace("\r\n", "\n"), Is.EqualTo(source.Replace("\r\n", "\n")));
    }

    [Test]
    public void IfElseUnnecessaryBracesCheckTryFixForElseIfChainReturnsFalse()
    {
        using var tempFile = new TempFile(".cs");
        const string source = """
            public sealed class Sample
            {
                public async Task RunAsync(bool first, bool second, string path)
                {
                    HashSet<int> addedLineNumbers;
                    if (first)
                    {
                        addedLineNumbers = await GetAddedLineNumbersAsync("base", path);
                    }
                    else if (second)
                        addedLineNumbers = new HashSet<int>(Enumerable.Range(1, 2));
                    else
                        addedLineNumbers = await GetAddedLineNumbersAsync("head", path);
                }
            }
            """;

        tempFile.WriteAllText(source);

        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Hint,
            CodeReviewRuleIds.IfElseUnnecessaryBraces,
            "Sample.cs",
            6,
            "If/else braces are unnecessary when each branch contains a single simple statement.");

        var check = new IfElseUnnecessaryBracesCodeReviewCheck();
        var success = check.TryFix(finding, tempFile, out var message);

        Assert.That(success, Is.False);
        Assert.That(message, Is.EqualTo("Target line does not contain an if/else with unnecessary braces."));
        Assert.That(tempFile.ReadAllText().Replace("\r\n", "\n"), Is.EqualTo(source.Replace("\r\n", "\n")));
    }

    [Test]
    public void WarningSuppressionCheckTryFixWhenPragmaRemovesTheLine()
    {
        using var tempFile = new TempFile(".cs");
        const string source = """
            using System;

            #pragma warning disable CS0168

            public sealed class Sample
            {
            }
            """;

        tempFile.WriteAllText(source);

        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Important,
            "warning-suppression",
            "Sample.cs",
            3,
            "Suppression added");

        var check = new WarningSuppressionCodeReviewCheck();
        var success = check.TryFix(finding, tempFile, out var message);

        Assert.That(success, Is.True);
        Assert.That(message, Is.Not.Empty);

        var updated = tempFile.ReadAllText();
        Assert.That(updated, Does.Not.Contain("#pragma warning disable"));
        Assert.That(updated, Does.Not.Contain("\n\n\n"));
    }

    [Test]
    public void WarningSuppressionCheckTryFixWhenSuppressMessageRemovesTheAttribute()
    {
        using var tempFile = new TempFile(".cs");
        const string source = """
            using System;
            using System.Diagnostics.CodeAnalysis;

            [SuppressMessage("Usage", "CA1801:Review unused parameters", Justification = "Test.")]
            public sealed class Sample
            {
            }
            """;

        tempFile.WriteAllText(source);

        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Important,
            "warning-suppression",
            "Sample.cs",
            4,
            "Suppression added");

        var check = new WarningSuppressionCodeReviewCheck();
        var success = check.TryFix(finding, tempFile, out var message);

        Assert.That(success, Is.True);
        Assert.That(message, Is.Not.Empty);

        var updated = tempFile.ReadAllText();
        Assert.That(updated, Does.Not.Contain("SuppressMessage"));
        Assert.That(updated, Does.Not.Contain("\n\n\n"));
    }

    [Test]
    public void FileNewlineChangedCheckTryFixRestoresBaselineLineEndings()
    {
        using var tempFile = new TempFile(".cs");
        const string source = "public sealed class Sample\r\n{\r\n    public void Run()\r\n    {\r\n    }\r\n}\r\n";
        const string baseline = "public sealed class Sample\n{\n    public void Run()\n    {\n    }\n}\n";

        tempFile.WriteAllText(source);

        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Hint,
            CodeReviewRuleIds.FileNewlineChanged,
            "Sample.cs",
            1,
            "File newline style changed from LF to CRLF.",
            currentText: source,
            baselineText: baseline);

        var check = new FileNewlineChangedCodeReviewCheck();
        var success = check.TryFix(finding, tempFile, out var message);

        Assert.That(success, Is.True);
        Assert.That(message, Is.EqualTo("Restored original line ending style (LF)."));
        Assert.That(tempFile.ReadAllText(), Is.EqualTo(baseline));
    }

    [Test]
    public void TrailingWhitespaceOnlyChangeCheckTryFixRemovesTrailingWhitespace()
    {
        using var tempFile = new TempFile(".cs");
        const string source = "public sealed class Sample\r\n{\r\n    public void Run()\t \r\n}\r\n";

        tempFile.WriteAllText(source);

        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Hint,
            CodeReviewRuleIds.TrailingWhitespaceOnlyChange,
            "Sample.cs",
            1,
            "Only trailing whitespace appears to have changed in this file.",
            currentText: source,
            baselineText: "public sealed class Sample\r\n{\r\n    public void Run()\r\n}\r\n");

        var check = new TrailingWhitespaceOnlyChangeCodeReviewCheck();
        var success = check.TryFix(finding, tempFile, out var message);

        Assert.That(success, Is.True);
        Assert.That(message, Is.EqualTo("Removed trailing whitespace from the file."));
        Assert.That(tempFile.ReadAllText(), Is.EqualTo("public sealed class Sample\r\n{\r\n    public void Run()\r\n}\r\n"));
    }

    [Test]
    public void MissingBlankLineBetweenMethodsCheckTryFixInsertsBlankLineBeforeSecondMethod()
    {
        using var tempFile = new TempFile(".cs");
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

        tempFile.WriteAllText(source);

        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Hint,
            "missing-blank-line-between-methods",
            "Sample.cs",
            7,
            "Add a blank line between methods.");

        var check = new MissingBlankLineBetweenMethodsCodeReviewCheck();
        var success = check.TryFix(finding, tempFile, out var message);

        Assert.That(success, Is.True);
        Assert.That(message, Is.Not.Empty);

        var updated = tempFile.ReadAllText();
        Assert.That(updated.Replace("\r\n", "\n"), Does.Contain("}\n\n    private int Bar()"));
    }

    [Test]
    public void BlankLineBetweenBracePairsCheckTryFixRemovesBlankLineBetweenClosingBraces()
    {
        using var tempFile = new TempFile(".cs");
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

        tempFile.WriteAllText(source);

        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Hint,
            CodeReviewRuleIds.BlankLineBetweenBracePairs,
            "Sample.cs",
            9,
            "Remove blank line between consecutive closing braces.");

        var check = new BlankLineBetweenBracePairsCodeReviewCheck();
        var success = check.TryFix(finding, tempFile, out var message);

        Assert.That(success, Is.True);
        Assert.That(message, Is.EqualTo("Removed blank line between consecutive braces."));

        var updated = tempFile.ReadAllText().Replace("\r\n", "\n");
        Assert.That(updated, Does.Contain("        }\n    }\n}"));
        Assert.That(updated, Does.Not.Contain("        }\n\n    }"));
    }

    [Test]
    public void UnusedPrivateMemberCheckCanFixWhenFindingTargetsPrivateMethod()
    {
        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Hint,
            CodeReviewRuleIds.UnusedPrivateMember,
            "Sample.cs",
            10,
            "Private method `RunMachineForDuration` appears to be unused.");

        var check = new UnusedPrivateMemberCodeReviewCheck();

        Assert.That(check.CanFix(finding), Is.True);
    }

    [Test]
    public void MethodCanBeStaticCheckTryFixAddsStaticModifier()
    {
        using var tempFile = new TempFile(".cs");
        const string source = """
            public sealed class Sample
            {
                private int Add(int a, int b)
                {
                    return a + b;
                }
            }
            """;

        tempFile.WriteAllText(source);

        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Hint,
            CodeReviewRuleIds.MethodCanBeStatic,
            "Sample.cs",
            3,
            "Method `Add` can likely be made static.");

        var check = new MethodCanBeStaticCodeReviewCheck();
        var success = check.TryFix(finding, tempFile, out var message);

        Assert.That(success, Is.True);
        Assert.That(message, Does.Contain("Add"));

        var updated = tempFile.ReadAllText();
        Assert.That(updated, Does.Contain("private static int Add"));

        var root = CSharpSyntaxTree.ParseText(updated).GetCompilationUnitRoot();
        var method = root.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(node => node.Identifier.ValueText == "Add");
        Assert.That(method.Modifiers.Any(modifier => modifier.RawKind == (int)SyntaxKind.StaticKeyword), Is.True);
    }

    [Test]
    public void RedundantSelfLookupCheckTryFixReplacesLookupCallWithThis()
    {
        using var tempFile = new TempFile(".cs");
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

        tempFile.WriteAllText(source);

        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Hint,
            CodeReviewRuleIds.RedundantSelfLookup,
            "Sample.cs",
            10,
            "Redundant self lookup.");

        var check = new RedundantSelfLookupCodeReviewCheck();
        var success = check.TryFix(finding, tempFile, out var message);

        Assert.That(success, Is.True);
        Assert.That(message, Does.Contain("this"));

        var updated = tempFile.ReadAllText();
        Assert.That(updated, Does.Not.Contain("Owner.GetOwner(this)"));
        Assert.That(updated, Does.Contain("return this.ToString();"));
    }

    [Test]
    public void UnusedPrivateMemberCheckTryFixRemovesPrivateMethod()
    {
        using var tempFile = new TempFile(".cs");
        const string source = """
            public sealed class Sample
            {
                public int Run()
                {
                    return 42;
                }

                private void RunMachineForDuration()
                {
                    var x = 1;
                }
            }
            """;

        tempFile.WriteAllText(source);

        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Hint,
            CodeReviewRuleIds.UnusedPrivateMember,
            "Sample.cs",
            8,
            "Private method `RunMachineForDuration` appears to be unused.");

        var check = new UnusedPrivateMemberCodeReviewCheck();
        var success = check.TryFix(finding, tempFile, out var message);

        Assert.That(success, Is.True);
        Assert.That(message, Does.Contain("RunMachineForDuration"));

        var updated = tempFile.ReadAllText();
        Assert.That(updated, Does.Not.Contain("RunMachineForDuration"));
        Assert.That(updated, Does.Contain("public int Run()"));
        Assert.That(updated, Does.Not.Contain("\n\n\n"));

        var root = CSharpSyntaxTree.ParseText(updated).GetCompilationUnitRoot();
        Assert.That(root.DescendantNodes().OfType<MethodDeclarationSyntax>().Any(method => method.Identifier.ValueText == "RunMachineForDuration"), Is.False);
    }

    [Test]
    public void UnnecessaryVerbatimStringPrefixCheckTryFixRemovesPrefixFromLiteral()
    {
        using var tempFile = new TempFile(".cs");
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

        tempFile.WriteAllText(source);

        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Hint,
            "unnecessary-verbatim-string-prefix",
            "Sample.cs",
            5,
            "Unnecessary verbatim string prefix.");

        var check = new UnnecessaryVerbatimStringPrefixCodeReviewCheck();
        var success = check.TryFix(finding, tempFile, out var message);

        Assert.That(success, Is.True);
        Assert.That(message, Is.Not.Empty);

        var updated = tempFile.ReadAllText();
        Assert.That(updated, Does.Contain("var foo = \"hello\";"));
        Assert.That(updated, Does.Not.Contain("@\"hello\""));
    }

    [Test]
    public void UnnecessaryVerbatimStringPrefixCheckTryFixRemovesPrefixFromInterpolatedLiteral()
    {
        using var tempFile = new TempFile(".cs");
        const string source = """
            public sealed class Sample
            {
                public string Run(string name)
                {
                    return $@"hello {name}";
                }
            }
            """;

        tempFile.WriteAllText(source);

        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Hint,
            "unnecessary-verbatim-string-prefix",
            "Sample.cs",
            5,
            "Unnecessary verbatim string prefix.");

        var check = new UnnecessaryVerbatimStringPrefixCodeReviewCheck();
        var success = check.TryFix(finding, tempFile, out var message);

        Assert.That(success, Is.True);
        Assert.That(message, Is.Not.Empty);

        var updated = tempFile.ReadAllText();
        Assert.That(updated, Does.Contain("return $\"hello {name}\";"));
        Assert.That(updated, Does.Not.Contain("$@\""));
        Assert.That(updated, Does.Not.Contain("@$\""));
    }

    [Test]
    public void LocalVariableCanBeConstCheckTryFixAddsConstKeyword()
    {
        using var tempFile = new TempFile(".cs");
        const string source = """
            public sealed class Sample
            {
                public void Run()
                {
                    var name = "World";
                    System.Console.WriteLine($"Hello {name}");
                }
            }
            """;

        tempFile.WriteAllText(source);

        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Hint,
            "local-variable-can-be-const",
            "Sample.cs",
            5,
            "Local variable `name` can be made `const`.");

        var check = new LocalVariableCanBeConstCodeReviewCheck();
        var success = check.TryFix(finding, new FileInfo(tempFile.FullName), out var message);

        Assert.That(success, Is.True);
        Assert.That(message, Is.Not.Empty);

        var updated = tempFile.ReadAllText();
        Assert.That(updated, Does.Contain("const string name = \"World\";"));
    }

    [Test]
    public void LocalVariableCanBeConstCheckDoesNotDetectIfModified()
    {
        using var tempFile = new TempFile(".cs");
        const string source = """
            public sealed class Sample
            {
                public void Run()
                {
                    var name = "World";
                    name = "Something else";
                    System.Console.WriteLine($"Hello {name}");
                }
            }
            """;

        tempFile.WriteAllText(source);

        var context = new CodeReviewAnalysisContext(
            new[] { new CodeReviewChangedFile("M", "Sample.cs", tempFile.FullName, source, source.Split('\n'), new HashSet<int> { 5 }) },
            new HashSet<string>());
        var report = new CodeSmellReport();
        var check = new LocalVariableCanBeConstCodeReviewCheck();
        check.Analyze(context, report);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void LocalVariableCanBeConstCheckDetectsIfNeverModified()
    {
        using var tempFile = new TempFile(".cs");
        const string source = """
            public sealed class Sample
            {
                public void Run()
                {
                    var name = "World";
                    System.Console.WriteLine($"Hello {name}");
                }
            }
            """;

        tempFile.WriteAllText(source);

        var context = new CodeReviewAnalysisContext(
            new[] { new CodeReviewChangedFile("M", "Sample.cs", tempFile.FullName, source, source.Split('\n'), new HashSet<int> { 5 }) },
            new HashSet<string>());
        var report = new CodeSmellReport();
        var check = new LocalVariableCanBeConstCodeReviewCheck();
        check.Analyze(context, report);

        Assert.That(report.Findings, Has.Count.EqualTo(1));
        Assert.That(report.Findings[0].RuleId, Is.EqualTo("local-variable-can-be-const"));
        Assert.That(report.Findings[0].LineNumber, Is.EqualTo(5));
    }

    [Test]
    public void LocalVariableCanBeConstCheckSkipsChangedFileWhenLineIsNotInAddedSet()
    {
        using var tempFile = new TempFile(".cs");
        const string source = """
            public sealed class Sample
            {
                public void Run()
                {
                    var title = "ReviewG33k";
                    System.Console.WriteLine(title);
                }
            }
            """;

        tempFile.WriteAllText(source);

        var context = new CodeReviewAnalysisContext(
            new[] { new CodeReviewChangedFile("M", "Sample.cs", tempFile.FullName, source, source.Split('\n'), new HashSet<int> { 2 }) },
            new HashSet<string>());
        var report = new CodeSmellReport();
        var check = new LocalVariableCanBeConstCodeReviewCheck();
        check.Analyze(context, report);

        Assert.That(report.Findings, Is.Empty);
    }

    [Test]
    public void UnnecessaryEnumMemberValueCheckTryFixRemovesExplicitValueAssignment()
    {
        using var tempFile = new TempFile(".cs");
        const string source = """
            public enum CodeReviewCheckScope
            {
                AddedLinesOnly = 0,
                ChangedFileSet = 1,
                ExtraScope = 2
            }
            """;

        tempFile.WriteAllText(source);

        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Hint,
            CodeReviewRuleIds.UnnecessaryEnumMemberValue,
            "Sample.cs",
            3,
            "Enum member has explicit default value.");

        var check = new UnnecessaryEnumMemberValueCodeReviewCheck();
        Assert.That(check.CanFix(finding), Is.True);

        var success = check.TryFix(finding, tempFile, out var message);

        Assert.That(success, Is.True);
        Assert.That(message, Does.Contain("AddedLinesOnly"));

        var updated = tempFile.ReadAllText();
        Assert.That(updated, Does.Contain("AddedLinesOnly,"));
        Assert.That(updated, Does.Not.Contain("AddedLinesOnly = 0"));
        Assert.That(updated, Does.Contain("ChangedFileSet = 1"));
    }

    [Test]
    public void UnnecessaryEnumMemberValueCheckTryFixOnLastMemberKeepsBraceOnNextLine()
    {
        using var tempFile = new TempFile(".cs");
        var source = string.Join(
            "\r\n",
            "private enum ReviewMode",
            "{",
            "    PullRequest = 0,",
            "    LocalCommittedChanges = 1,",
            "    LocalUncommittedChanges = 2",
            "}");

        tempFile.WriteAllText(source);

        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Hint,
            CodeReviewRuleIds.UnnecessaryEnumMemberValue,
            "Sample.cs",
            5,
            "Enum member has explicit default value.");

        var check = new UnnecessaryEnumMemberValueCodeReviewCheck();
        var success = check.TryFix(finding, tempFile, out _);

        Assert.That(success, Is.True);

        var updated = tempFile.ReadAllText();
        Assert.That(updated, Does.Contain("LocalUncommittedChanges\r\n}"));
        Assert.That(updated, Does.Not.Contain("LocalUncommittedChanges = 2"));
    }

    [Test]
    public void UnnecessaryEnumMemberValueCheckTryFixOnLastMemberWithMixedAssignmentsKeepsBraceOnNextLine()
    {
        using var tempFile = new TempFile(".cs");
        var source = string.Join(
            "\r\n",
            "private enum ReviewMode",
            "{",
            "    PullRequest = 0,",
            "    LocalCommittedChanges,",
            "    LocalUncommittedChanges = 2",
            "}");

        tempFile.WriteAllText(source);

        var finding = new CodeSmellFinding(
            CodeReviewFindingSeverity.Hint,
            CodeReviewRuleIds.UnnecessaryEnumMemberValue,
            "Sample.cs",
            5,
            "Enum member has explicit default value.");

        var check = new UnnecessaryEnumMemberValueCodeReviewCheck();
        var success = check.TryFix(finding, tempFile, out _);

        Assert.That(success, Is.True);

        var updated = tempFile.ReadAllText();
        Assert.That(updated, Does.Contain("LocalUncommittedChanges\r\n}"));
        Assert.That(updated, Does.Not.Contain("LocalUncommittedChanges = 2"));
    }
}
