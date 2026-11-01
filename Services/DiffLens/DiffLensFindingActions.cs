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
using System.Text.RegularExpressions;
using AvaloniaEdit.Document;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ReviewG33k.Services.DiffLens;

/// <summary>
/// Anchors review findings in an editable document and inserts actionable TODO comments.
/// </summary>
/// <remarks>
/// Anchors follow edits above a finding; changed or deleted target lines are refused.
/// Document edits use the editor's undo history and retain its existing line endings.
/// </remarks>
internal sealed class DiffLensFindingActions
{
    private readonly string m_path;
    private readonly TextDocument m_document;
    private readonly Dictionary<CodeSmellFinding, (TextAnchor Anchor, string Text)> m_targets = [];

    public DiffLensFindingActions(string path, TextDocument document, IEnumerable<CodeSmellFinding> findings)
    {
        m_path = path;
        m_document = document;
        foreach (var finding in findings)
        {
            if (finding.LineNumber < 1 || finding.LineNumber > document.LineCount)
                continue;
            var line = document.GetLineByNumber(finding.LineNumber);
            var anchor = document.CreateAnchor(line.Offset + Math.Min(1, line.Length));
            anchor.MovementType = AnchorMovementType.AfterInsertion;
            m_targets[finding] = (anchor, document.GetText(line));
        }
    }

    public int GetCurrentLine(CodeSmellFinding finding) =>
        m_targets.TryGetValue(finding, out var target)
            ? target.Anchor.IsDeleted ? 0 : target.Anchor.Line
            : finding.LineNumber;

    public string GetFixInstructions(CodeSmellFinding finding)
    {
        var line = m_targets.TryGetValue(finding, out var target) && !target.Anchor.IsDeleted
            ? target.Anchor.Line : finding.LineNumber;
        return $"File: {m_path}:{line}\nRule: {finding.RuleId}\nIssue: {finding.Message}\n\n" +
               "Implement a concise fix for this issue. Keep behavior unchanged except for resolving it. Run relevant build/tests if practical.";
    }

    public bool CanInsertTodo(CodeSmellFinding finding, out string reason) =>
        TryGetTarget(finding, out _, out _, out reason);

    public bool TryInsertTodo(CodeSmellFinding finding, out int lineNumber, out string reason)
    {
        lineNumber = 0;
        if (!TryGetTarget(finding, out var line, out var comment, out reason))
            return false;
        var text = m_document.GetText(line);
        var indentation = text[..(text.Length - text.TrimStart(' ', '\t').Length)];
        var newline = line.DelimiterLength > 0 ? m_document.GetText(line.EndOffset, line.DelimiterLength)
            : m_document.Text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        lineNumber = line.LineNumber;
        m_document.Insert(line.Offset, indentation + comment + newline);
        return true;
    }

    private bool TryGetTarget(CodeSmellFinding finding, out DocumentLine line, out string comment, out string reason)
    {
        line = null;
        comment = null;
        reason = null;
        if (!string.Equals(Path.GetExtension(m_path), ".cs", StringComparison.OrdinalIgnoreCase))
        {
            reason = "TODO insertion is available for C# files.";
            return false;
        }
        if (!m_targets.TryGetValue(finding, out var target) || target.Anchor.IsDeleted)
        {
            reason = "The issue's original line is no longer available. Rerun the review.";
            return false;
        }
        line = m_document.GetLineByNumber(target.Anchor.Line);
        if (m_document.GetText(line) != target.Text)
        {
            reason = "The issue's line has changed. Rerun the review before adding a TODO.";
            return false;
        }
        var root = CSharpSyntaxTree.ParseText(m_document.Text).GetRoot();
        var token = root.FindToken(line.Offset);
        var trivia = root.FindTrivia(line.Offset);
        if ((token.SpanStart < line.Offset && token.Span.End > line.Offset) ||
            trivia.IsKind(SyntaxKind.MultiLineCommentTrivia) || trivia.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
        {
            reason = "A TODO cannot be inserted inside a multiline string or comment.";
            return false;
        }
        var rule = Regex.Replace(finding.RuleId, @"[^a-zA-Z0-9_.-]", "-");
        var message = Regex.Replace(finding.Message, @"\s+", " ").Trim();
        comment = $"// TODO [{rule}]: {message}";
        for (var previous = line.PreviousLine; previous != null; previous = previous.PreviousLine)
        {
            var previousText = m_document.GetText(previous).Trim();
            if (!previousText.StartsWith("// TODO [", StringComparison.Ordinal))
                break;
            if (previousText == comment)
            {
                reason = "This TODO is already present above the issue.";
                return false;
            }
        }
        return true;
    }
}
