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

namespace ReviewG33k.Services;

public sealed class CodeReviewChangedFileSourceResult
{
    public CodeReviewChangedFileSourceResult(
        IReadOnlyList<CodeReviewChangedFile> files,
        IReadOnlyList<string> infoMessages,
        bool isEntireRepositoryScan = false)
    {
        Files = files ?? Array.Empty<CodeReviewChangedFile>();
        InfoMessages = infoMessages ?? Array.Empty<string>();
        IsEntireRepositoryScan = isEntireRepositoryScan;
    }

    public IReadOnlyList<CodeReviewChangedFile> Files { get; }

    public IReadOnlyList<string> InfoMessages { get; }

    public bool IsEntireRepositoryScan { get; }
}
