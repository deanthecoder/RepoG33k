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

public sealed class CodeReviewChangedFile
{
    public CodeReviewChangedFile(string status, string path, string fullPath, string text, IReadOnlyList<string> lines, IReadOnlySet<int> addedLineNumbers)
        : this(status, path, fullPath, text, lines, addedLineNumbers, null, null, null, null)
    {
    }

    public CodeReviewChangedFile(
        string status,
        string path,
        string fullPath,
        string text,
        IReadOnlyList<string> lines,
        IReadOnlySet<int> addedLineNumbers,
        string baselineText,
        byte[] currentBytes,
        byte[] baselineBytes)
        : this(status, path, fullPath, text, lines, addedLineNumbers, baselineText, currentBytes, baselineBytes, null)
    {
    }

    internal CodeReviewChangedFile(
        string status,
        string path,
        string fullPath,
        string text,
        IReadOnlyList<string> lines,
        IReadOnlySet<int> addedLineNumbers,
        string baselineText,
        byte[] currentBytes,
        byte[] baselineBytes,
        object roslynCacheKey)
    {
        Status = status ?? string.Empty;
        Path = path ?? string.Empty;
        FullPath = fullPath ?? string.Empty;
        Text = text ?? string.Empty;
        Lines = lines ?? [];
        AddedLineNumbers = addedLineNumbers ?? new HashSet<int>();
        BaselineText = baselineText;
        CurrentBytes = currentBytes;
        BaselineBytes = baselineBytes;
        RoslynCacheKey = roslynCacheKey ?? this;
    }

    public string Status { get; }

    public string Path { get; }

    public string FullPath { get; }

    public string Text { get; }

    public IReadOnlyList<string> Lines { get; }

    public IReadOnlySet<int> AddedLineNumbers { get; }

    public string BaselineText { get; }

    public byte[] CurrentBytes { get; }

    public byte[] BaselineBytes { get; }

    public bool IsAdded => Status.Equals("A", StringComparison.OrdinalIgnoreCase);

    public bool HasComparableBaseline => BaselineText != null || BaselineBytes != null;

    internal object RoslynCacheKey { get; }
}
