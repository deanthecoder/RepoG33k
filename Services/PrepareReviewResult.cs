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

public sealed class PrepareReviewResult
{
    public PrepareReviewResult(string localRepositoryPath, string reviewWorktreePath, string solutionPath)
    {
        LocalRepositoryPath = localRepositoryPath;
        ReviewWorktreePath = reviewWorktreePath;
        SolutionPath = solutionPath;
    }

    public string LocalRepositoryPath { get; }

    public string ReviewWorktreePath { get; }

    public string SolutionPath { get; }
}