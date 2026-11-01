// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software.

using System.Collections.Generic;

namespace ReviewG33k.Services.DiffLens;

/// <summary>Keeps a bounded set of prepared immutable Git comparisons.</summary>
/// <remarks>Keys contain resolved commit or staged-tree identities; editable working files never enter this cache.</remarks>
internal sealed class DiffLensComparisonCache
{
    private readonly Dictionary<string, DiffLensLoadedComparison> m_items = new();
    private readonly Queue<string> m_order = new();

    public bool TryGet(string key, out DiffLensLoadedComparison comparison)
    {
        comparison = null;
        return key != null && m_items.TryGetValue(key, out comparison);
    }

    public void Store(string key, DiffLensLoadedComparison comparison)
    {
        if (key == null || m_items.ContainsKey(key))
            return;
        while (m_order.Count >= 8)
            m_items.Remove(m_order.Dequeue());
        m_items.Add(key, comparison);
        m_order.Enqueue(key);
    }
}

/// <summary>Retains both file snapshots and the prepared canvas rows for an immutable comparison.</summary>
/// <remarks>Returning to a branch does not repeat Git blob reads or diff preparation.</remarks>
internal sealed record DiffLensLoadedComparison((string Target, List<DiffLensScope> Scopes) Catalog,
    DiffLensScope Scope, DiffLensChangeSet ChangeSet, DiffLensCell[] Cells);
