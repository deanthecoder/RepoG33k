// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using ReviewG33k.Services.DiffLens;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class DiffLensSyntaxTests
{
    [TestCase("shader.glsl")]
    [TestCase("shader.frag")]
    [TestCase("shader.comp")]
    [TestCase("renderer.cpp")]
    [TestCase("renderer.hpp")]
    [TestCase("Example.cs")]
    public void SourceTokensUseTheSharedPalette(string path)
    {
        var definition = DiffLensSyntax.ForPath(path);
        Assert.That(definition, Is.Not.Null);
        var text = new DiffLensHighlightedText("uniform vec3 color = vec3(0.5); // shade", definition);
        var colors = text.Line(1).Sections.Select(s => s.Color.Name).ToArray();
        Assert.That(colors, Does.Contain("Keyword"));
        Assert.That(colors, Does.Contain("Type"));
        Assert.That(colors, Does.Contain("Number"));
        Assert.That(colors, Does.Contain("Symbol"));
        Assert.That(colors, Does.Contain("Comment"));
    }

    [Test]
    public void MultilineCommentsRemainHighlightedWhenEarlierLinesAreHidden()
    {
        var text = new DiffLensHighlightedText("/* start\nhidden context\nfloat stillComment;\n*/\nfloat value = 1;",
            DiffLensSyntax.ForPath("shader.vert"));
        Assert.That(text.Line(3).Sections.Single().Color.Name, Is.EqualTo("Comment"));
        Assert.That(text.Line(5).Sections.Select(s => s.Color.Name), Does.Contain("Type"));
    }

    [Test]
    public void EscapedQuotesKeepCommentMarkersInsideStrings()
    {
        var text = new DiffLensHighlightedText("\tconst char* s = \"escaped \\\" // string\"; // comment",
            DiffLensSyntax.ForPath("renderer.cpp"));
        var line = text.Line(1);
        Assert.That(line.Sections.Count(s => s.Color.Name == "Comment"), Is.EqualTo(1));
        Assert.That(line.Sections.Last().Color.Name, Is.EqualTo("Comment"));
        Assert.That(line.Sections.Where(s => s.Color.Name == "String").Sum(s => s.Length), Is.GreaterThan(15));
    }

    [Test]
    public void UnrecognizedFilesRemainPlainText()
    {
        Assert.That(DiffLensSyntax.ForPath("notes.txt"), Is.Null);
    }
}
