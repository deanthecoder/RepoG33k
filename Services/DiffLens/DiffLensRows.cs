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
using System.Linq;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace ReviewG33k.Services.DiffLens;

/// <summary>
/// Projects the same diff into compact, contextual and aligned split rows.
/// </summary>
/// <remarks>
/// Source line identities survive presentation changes so the camera can retain its anchor.
/// </remarks>
internal static class DiffLensRows
{
    public const int MaxPreviewLines = 5000;

    public static int FindWorkingLine(IReadOnlyList<DiffLensRow> rows, int index)
    {
        if (index < 0)
            return 1;
        // Deleted lines and collapsed gaps have no working position. Prefer the
        // next surviving line, then the preceding line at the end of the file.
        for (var i = index; i < rows.Count; i++)
            if (rows[i].NewLine is { } next)
                return next;
        for (var i = System.Math.Min(index, rows.Count - 1); i >= 0; i--)
            if (rows[i].NewLine is { } previous)
                return previous;
        return 1;
    }

    public static IReadOnlyList<DiffLensRow> Build(string baseline, string working, int context, bool split)
    {
        return Project(BuildModelRows(baseline, working), context, split);
    }

    public static (IReadOnlyList<DiffLensRow> Unified, IReadOnlyList<DiffLensRow> Split) BuildBoth(string baseline, string working, int context)
    {
        var rows = BuildModelRows(baseline, working);
        return (Project(rows, context, false), Project(rows, context, true));
    }

    private static DiffLensRow[] BuildModelRows(string baseline, string working)
    {
        var model = new SideBySideDiffBuilder().BuildDiffModel(baseline, working, ignoreWhitespace: false);
        return model.OldText.Lines.Zip(model.NewText.Lines, (left, right) => new DiffLensRow(left, right)).ToArray();
    }

    private static IReadOnlyList<DiffLensRow> Project(DiffLensRow[] all, int context, bool split)
    {
        var visible = new bool[all.Length];
        for (var i = 0; i < all.Length; i++)
        {
            if (!all[i].Changed)
                continue;
            for (var j = System.Math.Max(0, i - context); j <= System.Math.Min(all.Length - 1, i + context); j++)
                visible[j] = true;
        }
        // An unchanged file is still inspectable.
        if (!all.Any(row => row.Changed))
            System.Array.Fill(visible, true);
        var rows = new List<DiffLensRow>();
        for (var i = 0; i < all.Length; i++)
        {
            if (!visible[i])
            {
                var start = i;
                while (i + 1 < all.Length && !visible[i + 1])
                    i++;
                rows.Add(new DiffLensRow(null, null, i - start + 1));
            }
            else if (!split && all[i].Changed)
            {
                if (all[i].Left.Type != ChangeType.Imaginary)
                    rows.Add(new DiffLensRow(all[i].Left, null));
                if (all[i].Right.Type != ChangeType.Imaginary)
                    rows.Add(new DiffLensRow(null, all[i].Right));
            }
            else
                rows.Add(all[i]);
        }
        if (rows.Count > MaxPreviewLines)
        {
            rows.RemoveRange(MaxPreviewLines, rows.Count - MaxPreviewLines);
            rows.Add(new DiffLensRow(null, null, Notice: "Preview limited to 5,000 lines — double-click to open the full file."));
        }
        return rows;
    }
}

/// <summary>
/// Maps a displayed row to the original lines, or a collapsed unchanged region.
/// </summary>
internal sealed record DiffLensRow(DiffPiece Left, DiffPiece Right, int HiddenLines = 0, string Notice = null)
{
    public bool Changed => Left?.Type is ChangeType.Deleted or ChangeType.Modified || Right?.Type is ChangeType.Inserted or ChangeType.Modified;
    public int? OldLine => Left?.Position;
    public int? NewLine => Right?.Position;
}
