// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software.

using System.Collections.Generic;

namespace ReviewG33k.Services;

/// <summary>Keeps ignored findings separate from selections for review actions.</summary>
/// <remarks>Shared by DiffLens and its results dialog for the lifetime of one review.</remarks>
public sealed class ReviewFindingSelection
{
    private readonly HashSet<(string Path, string Rule, int Line, string Message)> m_ignored = [];
    private readonly HashSet<(string Path, string Rule, int Line, string Message)> m_unselected = [];

    public bool IsIgnored(CodeSmellFinding finding) => m_ignored.Contains(Key(finding));
    public bool IsSelected(CodeSmellFinding finding) => !m_unselected.Contains(Key(finding));

    public void SetIgnored(CodeSmellFinding finding, bool ignored)
    {
        if (ignored) m_ignored.Add(Key(finding));
        else m_ignored.Remove(Key(finding));
    }

    public void SetSelected(CodeSmellFinding finding, bool selected)
    {
        if (selected) m_unselected.Remove(Key(finding));
        else m_unselected.Add(Key(finding));
    }

    private static (string Path, string Rule, int Line, string Message) Key(CodeSmellFinding finding) =>
        (finding.FilePath.Replace('\\', '/'), finding.RuleId, finding.LineNumber, finding.Message);
}
