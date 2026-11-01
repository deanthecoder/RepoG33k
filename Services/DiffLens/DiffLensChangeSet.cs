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

namespace ReviewG33k.Services.DiffLens;

/// <summary>
/// Captures all working files against one resolved branch baseline.
/// </summary>
/// <remarks>
/// Unsupported entries remain visible in the folder map with an explanation.
/// </remarks>
internal sealed record DiffLensChangeSet(string Root, string BaseRevision, IReadOnlyList<DiffLensEntry> Entries);

/// <summary>
/// Describes either an editable text snapshot or a visible unsupported change.
/// </summary>
/// <remarks>
/// Keeping unsupported paths in the scene makes incomplete previews explicit.
/// </remarks>
internal sealed record DiffLensEntry(string Path, DiffLensFile File, string Error);
