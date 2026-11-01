// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

namespace ReviewG33k.Models;

public sealed class BitbucketPullRequestMetadata
{
    public BitbucketPullRequestMetadata(string targetBranch, string title, string author, string state)
    {
        TargetBranch = targetBranch;
        Title = title;
        Author = author;
        State = state;
    }

    public string TargetBranch { get; }

    public string Title { get; }

    public string Author { get; }

    public string State { get; }
}
