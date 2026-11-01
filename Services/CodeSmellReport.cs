// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace ReviewG33k.Services;

public sealed class CodeSmellReport
{
    private static readonly Regex SingleQuotedCodeRegex = new("'([^'\\r\\n]+)'", RegexOptions.Compiled);

    private readonly object m_syncRoot = new();
    private readonly List<string> m_info = [];
    private readonly List<CodeSmellFinding> m_findings = [];

    public IReadOnlyList<string> Info => m_info;

    public IReadOnlyList<CodeSmellFinding> Findings => m_findings;

    public void AddInfo(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        lock (m_syncRoot)
            m_info.Add(message.Trim());
    }

    public void AddFinding(
        CodeReviewFindingSeverity severity,
        string ruleId,
        string filePath,
        int lineNumber,
        string message,
        string currentText = null,
        string baselineText = null)
    {
        lock (m_syncRoot)
            m_findings.Add(new CodeSmellFinding(severity, ruleId, filePath, lineNumber, FormatMessage(message), currentText, baselineText));
    }

    private static string FormatMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return string.Empty;

        return SingleQuotedCodeRegex.Replace(message, static match => $"`{match.Groups[1].Value}`");
    }
}
