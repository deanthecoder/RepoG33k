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
using System.Xml;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;
using AvaloniaEdit.Highlighting.Xshd;

namespace ReviewG33k.Services.DiffLens;

/// <summary>
/// Shares a dark syntax palette between the diff canvas and editable source.
/// </summary>
/// <remarks>
/// Lexical highlighting supports C-family source without requiring a compiler or project model.
/// </remarks>
internal static class DiffLensSyntax
{
    private static readonly Lazy<IHighlightingDefinition> s_definition = new(CreateDefinition);

    public static IHighlightingDefinition ForPath(string path) => Path.GetExtension(path).ToLowerInvariant() is
        ".cs" or ".c" or ".h" or ".cc" or ".hh" or ".cpp" or ".hpp" or ".cxx" or ".hxx" or ".inl" or
        ".glsl" or ".vert" or ".frag" or ".geom" or ".tesc" or ".tese" or ".comp" or ".vs" or ".fs" or ".hlsl"
        ? s_definition.Value : null;

    private static IHighlightingDefinition CreateDefinition()
    {
        const string definition = """
            <SyntaxDefinition name="DiffLens C-family" xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
              <Color name="Comment" foreground="#91AD87" />
              <Color name="String" foreground="#E6BD87" />
              <Color name="Keyword" foreground="#C5A5E8" />
              <Color name="Type" foreground="#87C9DF" />
              <Color name="Number" foreground="#A5D6B0" />
              <Color name="Symbol" foreground="#A5B8CD" />
              <Color name="Directive" foreground="#E9BC70" />
              <RuleSet>
                <Span color="Comment" begin="//" />
                <Span color="Comment" multiline="true" begin="/\*" end="\*/" />
                <Span color="String" begin="&quot;" end="&quot;">
                  <RuleSet><Span begin="\\" end="." /></RuleSet>
                </Span>
                <Span color="String" begin="'" end="'">
                  <RuleSet><Span begin="\\" end="." /></RuleSet>
                </Span>
                <Rule color="Directive">^\s*\#\s*\w+</Rule>
                <Rule color="Number">\b(?:0[xX][0-9a-fA-F]+|0[bB][01]+|\d+(?:\.\d*)?(?:[eE][+-]?\d+)?)[uUlLfF]*\b|\.\d+(?:[eE][+-]?\d+)?[fF]?\b</Rule>
                <Keywords color="Keyword">
                  <Word>if</Word><Word>else</Word><Word>for</Word><Word>while</Word><Word>do</Word><Word>switch</Word><Word>case</Word><Word>default</Word>
                  <Word>return</Word><Word>break</Word><Word>continue</Word><Word>discard</Word><Word>goto</Word>
                  <Word>class</Word><Word>struct</Word><Word>enum</Word><Word>namespace</Word><Word>using</Word><Word>typedef</Word><Word>template</Word><Word>typename</Word>
                  <Word>public</Word><Word>private</Word><Word>protected</Word><Word>internal</Word><Word>static</Word><Word>const</Word><Word>constexpr</Word><Word>consteval</Word>
                  <Word>inline</Word><Word>virtual</Word><Word>override</Word><Word>final</Word><Word>explicit</Word><Word>extern</Word><Word>volatile</Word><Word>mutable</Word>
                  <Word>new</Word><Word>delete</Word><Word>this</Word><Word>nullptr</Word><Word>null</Word><Word>true</Word><Word>false</Word><Word>sizeof</Word><Word>noexcept</Word>
                  <Word>try</Word><Word>catch</Word><Word>throw</Word><Word>finally</Word><Word>async</Word><Word>await</Word><Word>readonly</Word><Word>sealed</Word><Word>var</Word><Word>auto</Word>
                  <Word>layout</Word><Word>uniform</Word><Word>buffer</Word><Word>shared</Word><Word>in</Word><Word>out</Word><Word>inout</Word><Word>flat</Word><Word>smooth</Word><Word>centroid</Word>
                  <Word>coherent</Word><Word>restrict</Word><Word>writeonly</Word><Word>highp</Word><Word>mediump</Word><Word>lowp</Word><Word>precision</Word>
                </Keywords>
                <Keywords color="Type">
                  <Word>void</Word><Word>bool</Word><Word>char</Word><Word>short</Word><Word>int</Word><Word>uint</Word><Word>long</Word><Word>float</Word><Word>double</Word><Word>signed</Word><Word>unsigned</Word><Word>string</Word><Word>size_t</Word>
                  <Word>vec2</Word><Word>vec3</Word><Word>vec4</Word><Word>ivec2</Word><Word>ivec3</Word><Word>ivec4</Word><Word>uvec2</Word><Word>uvec3</Word><Word>uvec4</Word><Word>bvec2</Word><Word>bvec3</Word><Word>bvec4</Word>
                  <Word>mat2</Word><Word>mat3</Word><Word>mat4</Word><Word>sampler2D</Word><Word>sampler3D</Word><Word>samplerCube</Word><Word>image2D</Word><Word>uimage2D</Word>
                </Keywords>
                <Rule color="Symbol">[{}()\[\];,.?:~!%^*+\-/=&amp;|&lt;&gt;]</Rule>
              </RuleSet>
            </SyntaxDefinition>
            """;
        using var reader = XmlReader.Create(new StringReader(definition));
        return HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}

/// <summary>
/// Maintains multiline lexical state for one side of a file's diff.
/// </summary>
/// <remarks>
/// Highlighting reads the complete source so comments spanning hidden context remain correct.
/// </remarks>
internal sealed class DiffLensHighlightedText(string text, IHighlightingDefinition definition)
{
    private TextDocument m_document;
    private DocumentHighlighter m_highlighter;
    private readonly Dictionary<int, HighlightedLine> m_lines = new();

    public HighlightedLine Line(int number)
    {
        m_document ??= new TextDocument(text.Replace("\t", "    "));
        if (number < 1 || number > m_document.LineCount)
            return null;
        m_highlighter ??= new DocumentHighlighter(m_document, definition);
        if (!m_lines.TryGetValue(number, out var line))
            m_lines[number] = line = m_highlighter.HighlightLine(number);
        return line;
    }
}
