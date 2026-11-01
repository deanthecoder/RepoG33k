// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace ReviewG33k.Services.Checks;

public abstract class CodeReviewCheckBase : ICodeReviewCheck
{
    public abstract string RuleId { get; }

    public abstract string DisplayName { get; }

    public virtual CodeReviewCheckScope Scope => CodeReviewCheckScope.AddedLinesOnly;

    public abstract void Analyze(CodeReviewAnalysisContext context, CodeSmellReport report);

    protected void AddFinding(
        CodeSmellReport report,
        CodeReviewFindingSeverity severity,
        string filePath,
        int lineNumber,
        string message,
        string currentText = null,
        string baselineText = null) =>
        report.AddFinding(severity, RuleId, filePath, lineNumber, message, currentText, baselineText);
}
