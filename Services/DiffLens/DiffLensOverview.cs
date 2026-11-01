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
using DiffPlex.DiffBuilder.Model;

namespace ReviewG33k.Services.DiffLens;

/// <summary>
/// Summarizes changed rows for inexpensive zoomed-out previews.
/// </summary>
/// <remarks>
/// Prefix counts preserve narrow changes when several source rows share one screen pixel.
/// The full diff rows and their source-line identities remain unchanged.
/// </remarks>
internal sealed class DiffLensOverview
{
    private readonly int[] m_added;
    private readonly int[] m_removed;

    public DiffLensOverview(IReadOnlyList<DiffLensRow> rows)
    {
        m_added = new int[rows.Count + 1];
        m_removed = new int[rows.Count + 1];
        for (var index = 0; index < rows.Count; index++)
        {
            m_added[index + 1] = m_added[index] + (rows[index].Right?.Type is ChangeType.Inserted or ChangeType.Modified ? 1 : 0);
            m_removed[index + 1] = m_removed[index] + (rows[index].Left?.Type is ChangeType.Deleted or ChangeType.Modified ? 1 : 0);
        }
    }

    public (bool Added, bool Removed) Changes(int first, int count)
    {
        var end = Math.Min(m_added.Length - 1, first + count);
        return (m_added[end] > m_added[first], m_removed[end] > m_removed[first]);
    }
}
