// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DiffPlex.DiffBuilder.Model;
using ReviewG33k.Services.DiffLens;

namespace ReviewG33k.Tests;

public sealed class DiffLensOverviewTests
{
    [Test]
    public void OverviewRetainsChangesEvenWhenManySourceRowsShareAPixel()
    {
        var rows = Enumerable.Range(1, 100).Select(line => new DiffLensRow(null,
            new DiffPiece { Text = "unchanged", Type = ChangeType.Unchanged, Position = line })).ToArray();
        rows[47] = new DiffLensRow(null, new DiffPiece { Text = "added", Type = ChangeType.Inserted, Position = 48 });
        rows[48] = new DiffLensRow(new DiffPiece { Text = "removed", Type = ChangeType.Deleted, Position = 49 }, null);
        var overview = new DiffLensOverview(rows);

        Assert.That(overview.Changes(0, 50), Is.EqualTo((true, true)));
        Assert.That(overview.Changes(50, 50), Is.EqualTo((false, false)));
        Assert.That(overview.Changes(47, 1), Is.EqualTo((true, false)));
        Assert.That(overview.Changes(48, 1), Is.EqualTo((false, true)));
    }

    [Test]
    public void EditingInvalidatesTheCachedOverviewWithoutChangingLineIdentities()
    {
        var cell = new DiffLensCell(new DiffLensEntry("file.cs", null, null));
        cell.Update("old", "new");
        var previous = cell.Overview(0);
        Assert.That(cell.Overview(0), Is.SameAs(previous));

        cell.Update("same", "same");

        Assert.That(cell.Overview(0), Is.Not.SameAs(previous));
        Assert.That(cell.Overview(0).Changes(0, cell.Rows(0).Count), Is.EqualTo((false, false)));
        Assert.That(cell.Rows(0)[0].NewLine, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void SharedDiffModelPreservesUnifiedAndSplitRows(bool split)
    {
        const string before = "one\ntwo\nthree";
        const string after = "one\nreplacement\nthree\nfour";
        var both = DiffLensRows.BuildBoth(before, after, 4);
        var expected = DiffLensRows.Build(before, after, 4, split);
        var actual = split ? both.Split : both.Unified;

        Assert.That(actual.Select(row => (row.OldLine, row.NewLine, row.Left?.Text, row.Right?.Text, row.HiddenLines)),
            Is.EqualTo(expected.Select(row => (row.OldLine, row.NewLine, row.Left?.Text, row.Right?.Text, row.HiddenLines))));
    }
}
