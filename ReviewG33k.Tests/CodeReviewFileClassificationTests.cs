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
using Support = ReviewG33k.Services.Checks.Support;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class CodeReviewFileClassificationTests
{
    [Test]
    public void IsAnalyzableChangedCSharpPathWhenPathIsAxamlCodeBehindReturnsTrue()
    {
        var result = InvokeIsAnalyzableChangedCSharpPath("Views/ReviewResultsWindow.axaml.cs");

        Assert.That(result, Is.True);
    }

    [Test]
    public void IsAnalyzableChangedCSharpPathWhenPathIsXamlCodeBehindReturnsTrue()
    {
        var result = InvokeIsAnalyzableChangedCSharpPath("Views/MainWindow.xaml.cs");

        Assert.That(result, Is.True);
    }

    [Test]
    public void IsAnalyzableChangedCSharpPathWhenPathIsPlainCSharpReturnsTrue()
    {
        var result = InvokeIsAnalyzableChangedCSharpPath("Services/Worker.cs");

        Assert.That(result, Is.True);
    }

    [Test]
    public void IsAnalyzableChangedCSharpPathWhenPathIsDesignerCSharpReturnsFalse()
    {
        var result = InvokeIsAnalyzableChangedCSharpPath("Views/MainWindow.Designer.cs");

        Assert.That(result, Is.False);
    }

    [Test]
    public void IsAnalyzableChangedCSharpPathWhenPathIsUnderObjReturnsFalse()
    {
        var result = InvokeIsAnalyzableChangedCSharpPath("DTC.Core/CSharp.Core/obj/Debug/net7.0/CSharp.Core.AssemblyInfo.cs");

        Assert.That(result, Is.False);
    }

    [TestCase("src/bin/Debug/Foo.cs")]
    [TestCase("src/dist/generated/Foo.cs")]
    [TestCase("src/node_modules/pkg/index.js")]
    [TestCase(".git/hooks/pre-commit.cs")]
    public void IsAnalyzableChangedCSharpPathWhenPathIsUnderIgnoredDirectoryReturnsFalse(string path)
    {
        var result = InvokeIsAnalyzableChangedCSharpPath(path);

        Assert.That(result, Is.False);
    }

    [Test]
    public void IsAnalyzableChangedCSharpPathWhenPathIsProjectFileReturnsTrue()
    {
        var result = InvokeIsAnalyzableChangedCSharpPath("Packages/CSharp.Core/CSharp.Core.csproj");

        Assert.That(result, Is.True);
    }

    [Test]
    public void IsDuplicateCodeCheckPathWhenPathIsProjectFileReturnsFalse()
    {
        var result = InvokeIsDuplicateCodeCheckPath("Packages/CSharp.Core/CSharp.Core.csproj");

        Assert.That(result, Is.False);
    }

    [Test]
    public void IsDuplicateCodeCheckPathWhenPathIsMarkupOrCSharpReturnsTrue()
    {
        var csharpResult = InvokeIsDuplicateCodeCheckPath("Services/Worker.cs");
        var axamlResult = InvokeIsDuplicateCodeCheckPath("Views/ReviewResultsWindow.axaml");
        var xamlResult = InvokeIsDuplicateCodeCheckPath("Views/MainWindow.xaml");

        Assert.Multiple(() =>
        {
            Assert.That(csharpResult, Is.True);
            Assert.That(axamlResult, Is.True);
            Assert.That(xamlResult, Is.True);
        });
    }

    [Test]
    public void IsAnalyzableChangedCSharpPathWhenPathIsMarkupReturnsTrue()
    {
        var axamlResult = InvokeIsAnalyzableChangedCSharpPath("Views/ReviewResultsWindow.axaml");
        var xamlResult = InvokeIsAnalyzableChangedCSharpPath("Views/MainWindow.xaml");

        Assert.Multiple(() =>
        {
            Assert.That(axamlResult, Is.True);
            Assert.That(xamlResult, Is.True);
        });
    }

    [Test]
    public void IsCodeBehindFilePathWhenPathIsAxamlOrXamlCodeBehindReturnsTrue()
    {
        var axamlResult = InvokeIsCodeBehindFilePath("Views/ReviewResultsWindow.axaml.cs");
        var xamlResult = InvokeIsCodeBehindFilePath("Views/MainWindow.xaml.cs");

        Assert.Multiple(() =>
        {
            Assert.That(axamlResult, Is.True);
            Assert.That(xamlResult, Is.True);
        });
    }

    [Test]
    public void IsMarkupFilePathWhenPathIsAxamlOrXamlReturnsTrue()
    {
        var axamlResult = InvokeIsMarkupFilePath("Views/ReviewResultsWindow.axaml");
        var xamlResult = InvokeIsMarkupFilePath("Views/MainWindow.xaml");

        Assert.Multiple(() =>
        {
            Assert.That(axamlResult, Is.True);
            Assert.That(xamlResult, Is.True);
        });
    }

    [Test]
    public void IsLikelyTestCodeFileWhenFileContainsTextFixtureAttributeReturnsTrue()
    {
        const string source = """
            [TextFixture]
            public sealed class SmartRipOpcDocumentation
            {
            }
            """;
        var file = new CodeReviewChangedFile(
            "A",
            "Packages/CSharp.Core/SmartRipOpcDocumentation.cs",
            "Packages/CSharp.Core/SmartRipOpcDocumentation.cs",
            source,
            source.Split('\n'),
            new HashSet<int> { 1, 2, 3, 4 });

        var result = InvokeIsLikelyTestCodeFile(file);

        Assert.That(result, Is.True);
    }

    [TestCase("Tests/MeteorInterfaceTests.cs", true)]
    [TestCase("UnitTests/MeteorInterfaceTests.cs", true)]
    [TestCase("CSharp.Tests/MeteorOpc/MeteorInterfaceTests.cs", true)]
    [TestCase("CSharp.UnitTests/MeteorOpc/MeteorInterfaceTests.cs", true)]
    [TestCase("SPC/CSharp.UnitTests/MeteorOpc/MeteorInterfaceTests.cs", true)]
    [TestCase("SPC/CSharp.Integration/MeteorOpc/MeteorInterfaceTests.cs", true)]
    [TestCase("SPC/CSharp.Unit/MeteorOpc/MeteorInterface.cs", false)]
    [TestCase("SPC/CSharp/MeteorOpc/MeteorInterface.cs", false)]
    public void IsTestFilePathReturnsExpectedResult(string path, bool expected)
    {
        var result = Support.CodeReviewFileClassification.IsTestFilePath(path);

        Assert.That(result, Is.EqualTo(expected));
    }

    [TestCase("src/Foo.cs", true)]
    [TestCase("src/Foo.CS", true)]
    [TestCase("native/Foo.cpp", true)]
    [TestCase("native/Foo.H", true)]
    [TestCase("web/app.js", true)]
    [TestCase("web/app.ts", true)]
    [TestCase("docs/readme.md", false)]
    [TestCase("assets/logo.png", false)]
    [TestCase("src/Foo.g.cs", false)]
    [TestCase("src/Foo.generated.cs", false)]
    [TestCase("src/obj/Debug/net8.0/Foo.cs", false)]
    [TestCase(null, false)]
    [TestCase("", false)]
    [TestCase("   ", false)]
    public void IsLikelySourceCodePathReturnsExpectedResult(string path, bool expected)
    {
        var actual = InvokeIsLikelySourceCodePath(path);
        Assert.That(actual, Is.EqualTo(expected));
    }

    private static bool InvokeIsAnalyzableChangedCSharpPath(string path)
    {
        return Support.CodeReviewFileClassification.IsAnalyzableChangedCSharpPath(path);
    }

    private static bool InvokeIsCodeBehindFilePath(string path)
    {
        return Support.CodeReviewFileClassification.IsCodeBehindFilePath(path);
    }

    private static bool InvokeIsDuplicateCodeCheckPath(string path)
    {
        return Support.CodeReviewFileClassification.IsDuplicateCodeCheckPath(path);
    }

    private static bool InvokeIsMarkupFilePath(string path)
    {
        return Support.CodeReviewFileClassification.IsMarkupFilePath(path);
    }

    private static bool InvokeIsLikelySourceCodePath(string path)
    {
        return Support.CodeReviewFileClassification.IsLikelySourceCodePath(path);
    }

    private static bool InvokeIsLikelyTestCodeFile(CodeReviewChangedFile file)
    {
        return Support.CodeReviewFileClassification.IsLikelyTestCodeFile(file);
    }
}
