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

public sealed class CodeReviewAnalysisContext
{
    public CodeReviewAnalysisContext(
        IReadOnlyList<CodeReviewChangedFile> files,
        IReadOnlySet<string> addedTestFilesByName,
        IReadOnlyList<CodeReviewChangedFile> resxFiles = null,
        IReadOnlyList<CodeReviewChangedFile> allChangedFiles = null,
        bool isEntireRepositoryScan = false)
        : this(files, addedTestFilesByName, addedTestFilesByName, resxFiles, allChangedFiles, isEntireRepositoryScan)
    {
    }

    public CodeReviewAnalysisContext(
        IReadOnlyList<CodeReviewChangedFile> files,
        IReadOnlySet<string> addedTestFilesByName,
        IReadOnlySet<string> changedTestFilesByName,
        IReadOnlyList<CodeReviewChangedFile> resxFiles = null,
        IReadOnlyList<CodeReviewChangedFile> allChangedFiles = null,
        bool isEntireRepositoryScan = false)
    {
        Files = files ?? throw new ArgumentNullException(nameof(files));
        AddedTestFilesByName = addedTestFilesByName ?? throw new ArgumentNullException(nameof(addedTestFilesByName));
        ChangedTestFilesByName = changedTestFilesByName ?? throw new ArgumentNullException(nameof(changedTestFilesByName));
        ResxFiles = resxFiles ?? [];
        AllChangedFiles = allChangedFiles ?? Files;
        IsEntireRepositoryScan = isEntireRepositoryScan;
    }

    public IReadOnlyList<CodeReviewChangedFile> Files { get; }

    public IReadOnlySet<string> AddedTestFilesByName { get; }

    public IReadOnlySet<string> ChangedTestFilesByName { get; }

    public IReadOnlyList<CodeReviewChangedFile> ResxFiles { get; }

    public IReadOnlyList<CodeReviewChangedFile> AllChangedFiles { get; }

    public bool IsEntireRepositoryScan { get; }

    public bool HasAnyAddedTestFiles => AddedTestFilesByName.Count > 0;

    public bool HasAnyChangedTestFiles => ChangedTestFilesByName.Count > 0;
}
