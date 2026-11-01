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

internal sealed class LocalReviewExecutionResult
{
    public LocalReviewExecutionResult(
        string reviewWorktreePath,
        string solutionPath,
        CodeReviewChangedFileSourceResult changedFileSourceResult,
        CodeSmellReport report)
    {
        ReviewWorktreePath = reviewWorktreePath;
        SolutionPath = solutionPath;
        ChangedFileSourceResult = changedFileSourceResult;
        Report = report;
    }

    public string ReviewWorktreePath { get; }

    public string SolutionPath { get; }

    public CodeReviewChangedFileSourceResult ChangedFileSourceResult { get; }

    public CodeSmellReport Report { get; }
}
