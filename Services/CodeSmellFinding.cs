// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace ReviewG33k.Services;

public sealed class CodeSmellFinding
{
    public CodeSmellFinding(
        CodeReviewFindingSeverity severity,
        string ruleId,
        string filePath,
        int lineNumber,
        string message,
        string currentText = null,
        string baselineText = null)
    {
        Severity = severity;
        RuleId = ruleId ?? string.Empty;
        FilePath = filePath ?? string.Empty;
        LineNumber = lineNumber;
        Message = message ?? string.Empty;
        CurrentText = currentText;
        BaselineText = baselineText;
    }

    public CodeReviewFindingSeverity Severity { get; }

    public string RuleId { get; }

    public string FilePath { get; }

    public int LineNumber { get; }

    public string Message { get; }

    public string CurrentText { get; }

    public string BaselineText { get; }
}
