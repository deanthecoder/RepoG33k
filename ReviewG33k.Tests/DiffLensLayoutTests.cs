// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using Avalonia;
using ReviewG33k.Services.DiffLens;

namespace ReviewG33k.Tests;

[TestFixture]
public sealed class DiffLensLayoutTests
{
    [TestCase(0)]
    [TestCase(2)]
    [TestCase(3)]
    public void NestedFoldersContainFilesWithoutOverlapAndCompactSingleChildPaths(int detail)
    {
        var cells = new[] { Cell("src/Core/first.cs"), Cell("src/Core/second.cs"), Cell("tests/unit/Test.cs"), Cell("README.md") };
        cells[0].Update("", string.Join("\n", Enumerable.Range(1, 500).Select(i => $"line {i}")));
        var root = DiffLensLayout.Build("repo", cells, detail);
        Assert.That(root.FileCount, Is.EqualTo(4));
        Assert.That(root.Folders.Select(folder => folder.Name), Is.EquivalentTo(new[] { "src / Core", "tests / unit" }));
        Assert.That(cells[0].Height, Is.GreaterThan(11000));
        foreach (var folder in root.Descendants)
        {
            var children = folder.Files.Select(file => file.Bounds).Concat(folder.Folders.Select(child => child.Bounds)).ToArray();
            foreach (var bounds in children)
            {
                Assert.That(folder.Bounds.Contains(bounds.TopLeft), Is.True);
                Assert.That(folder.Bounds.Contains(bounds.BottomRight), Is.True);
            }
            for (var i = 0; i < children.Length; i++)
                for (var j = i + 1; j < children.Length; j++)
                    Assert.That(children[i].Intersects(children[j]), Is.False);
        }
    }

    [Test]
    public void UnifiedZoomLevelsKeepTheSameContextAndLayout()
    {
        var cells = new[] { Cell("src/first.cs"), Cell("src/second.cs") };
        var baseline = string.Join("\n", Enumerable.Range(1, 40).Select(i => $"line {i}"));
        foreach (var cell in cells)
            cell.Update(baseline, baseline.Replace("line 20", "changed"));
        var compact = DiffLensLayout.Build("repo", cells, 0).Bounds;
        Assert.That(cells.All(cell => cell.Height == cell.DisplayHeight(0)), Is.True);
        var expanded = DiffLensLayout.Build("repo", cells, 2).Bounds;
        Assert.That(expanded, Is.EqualTo(compact));
        foreach (var cell in cells)
        {
            Assert.That(cell.Rows(0), Is.SameAs(cell.Rows(1)));
            Assert.That(cell.Rows(0), Is.SameAs(cell.Rows(2)));
            for (var detail = 0; detail < 4; detail++)
            {
                var context = cell.Rows(detail).Where(row => !row.Changed && row.HiddenLines == 0)
                    .Select(row => row.NewLine).Where(line => line != null);
                Assert.That(context, Is.EqualTo(new int?[] { 16, 17, 18, 19, 21, 22, 23, 24 }));
            }
        }
        var again = DiffLensLayout.Build("repo", cells, 0).Bounds;
        Assert.That(again, Is.EqualTo(compact));
    }

    [Test]
    public void ShortSiblingUsesSpaceBesideTallFileInsteadOfStartingBelowIt()
    {
        var cells = new[] { Cell("a.cs"), Cell("b.cs"), Cell("c.cs") };
        cells[0].Update("", string.Join("\n", Enumerable.Range(1, 100)));
        DiffLensLayout.Build("repo", cells);
        Assert.That(cells[2].Position.X, Is.EqualTo(cells[1].Position.X));
        Assert.That(cells[2].Position.Y, Is.EqualTo(cells[1].Bounds.Bottom + DiffLensLayout.Padding));
        Assert.That(cells[2].Bounds.Bottom, Is.LessThan(cells[0].Bounds.Bottom));
    }

    [TestCase(0)]
    [TestCase(2)]
    [TestCase(3)]
    public void WiderFolderFitsDirectlyBelowReadmeWithoutCollidingWithSource(int detail)
    {
        var cells = new[] { Cell("README.md"), Cell("src/Core/Download.cs"), Cell("src/Core/Retry.cs"), Cell("src/UI/View.cs"), Cell("tests/DownloadTests.cs") };
        cells[0].Update("", string.Join("\n", Enumerable.Range(1, 30)));
        foreach (var cell in cells.Skip(1))
            cell.Update("", string.Join("\n", Enumerable.Range(1, 8)));
        var root = DiffLensLayout.Build("repo", cells, detail);
        var tests = root.Folders.Single(folder => folder.Name == "tests");
        var source = root.Folders.Single(folder => folder.Name == "src");
        Assert.That(tests.Bounds.Width, Is.GreaterThan(cells[0].Bounds.Width));
        Assert.That(tests.Bounds.X, Is.EqualTo(cells[0].Bounds.X));
        Assert.That(tests.Bounds.Y, Is.EqualTo(cells[0].Bounds.Bottom + DiffLensLayout.Padding));
        Assert.That(tests.Bounds.Intersects(source.Bounds), Is.False);
    }

    [TestCase(0)]
    [TestCase(2)]
    [TestCase(3)]
    public void WideFolderKeepsAdjacentFilesTogetherWithoutOverlap(int detail)
    {
        var cells = new[] { Cell("a.cs"), Cell("b.cs"), Cell("src/one.cs"), Cell("src/two.cs") };
        foreach (var cell in cells)
            cell.Update("", string.Join("\n", Enumerable.Range(1, 300)));
        var root = DiffLensLayout.Build("repo", cells, detail);
        var source = root.Folders.Single();
        Assert.That(source.Bounds.Width, Is.GreaterThan(cells[0].Bounds.Width * 2));
        Assert.That(cells[1].Position.Y, Is.EqualTo(cells[0].Position.Y));
        Assert.That(cells[1].Position.X, Is.EqualTo(cells[0].Bounds.Right + DiffLensLayout.Padding));
        Assert.That(cells.Take(2).All(cell => !source.Bounds.Intersects(cell.Bounds)), Is.True);
        var right = Math.Max(source.Bounds.Right, cells.Take(2).Max(cell => cell.Bounds.Right));
        Assert.That(root.Bounds.Right, Is.EqualTo(right + DiffLensLayout.Padding));
    }

    [Test]
    public void PerFilePresentationSurvivesLayoutAndContentUpdates()
    {
        var cells = new[] { Cell("a.cs"), Cell("b.cs") };
        foreach (var cell in cells)
            cell.Update("old\n", "new\n");
        cells[0].Detail = 3;
        var root = DiffLensLayout.Build("repo", cells);
        var before = cells.Select(cell => cell.Bounds).ToArray();
        cells[0].Update("old\n", "edited\n");
        DiffLensLayout.Build("repo", cells);
        Assert.That(cells[0].Detail, Is.EqualTo(3));
        Assert.That(cells[1].Detail, Is.Zero);
        Assert.That(cells.Select(cell => cell.Bounds), Is.EqualTo(before));
        Assert.That(cells[0].Rows(3).Any(row => row.Left != null && row.Right != null && row.Changed), Is.True);
        Assert.That(root.Bounds.Contains(cells[0].Bounds.BottomRight), Is.True);
    }

    [Test]
    public void PackingPreservesSizesAndPaddingAcrossMixedBlocks()
    {
        var random = new Random(42);
        var sizes = Enumerable.Range(0, 150)
            .Select(_ => new Size(random.Next(100, 3000), random.Next(100, 10000))).ToArray();
        var packed = DiffLensRectanglePacker.Pack(sizes);
        Assert.That(packed.Select(rect => rect.Size), Is.EqualTo(sizes));
        Assert.That(packed.All(rect => rect.X >= 0 && rect.Y >= 0), Is.True);
        for (var i = 0; i < packed.Count; i++)
        {
            var padded = new Rect(packed[i].Position,
                new Size(packed[i].Width + DiffLensLayout.Padding, packed[i].Height + DiffLensLayout.Padding));
            for (var j = i + 1; j < packed.Count; j++)
            {
                var other = new Rect(packed[j].Position,
                    new Size(packed[j].Width + DiffLensLayout.Padding, packed[j].Height + DiffLensLayout.Padding));
                Assert.That(padded.Intersects(other), Is.False, $"Blocks {i} and {j} overlap or lose padding.");
            }
        }
        Assert.That(DiffLensRectanglePacker.Pack(sizes), Is.EqualTo(packed));
    }

    [Test]
    public void SmallBlocksFillSpaceBesideTallBlock()
    {
        var sizes = new[] { new Size(1000, 2000) }
            .Concat(Enumerable.Repeat(new Size(1000, 300), 6)).ToArray();
        var packed = DiffLensRectanglePacker.Pack(sizes);
        var area = packed.Max(rect => rect.Right) * packed.Max(rect => rect.Bottom);
        // All six short blocks fit beside the tall one, rather than extending the canvas below it.
        Assert.That(area, Is.LessThanOrEqualTo(2040 * 2000));
    }

    [Test]
    public void EmptyAndSingleBlockLayoutsKeepNaturalSizes()
    {
        Assert.That(DiffLensRectanglePacker.Pack([]), Is.Empty);
        Assert.That(DiffLensRectanglePacker.Pack([new Size(100, 200)]),
            Is.EqualTo(new[] { new Rect(0, 0, 100, 200) }));
    }

    [Test]
    public void LayoutIsStableWhenInputFileOrderChanges()
    {
        var cells = new[] { Cell("src/a.cs"), Cell("src/b.cs"), Cell("tests/test.cs"), Cell("README.md") };
        cells[0].Update("", string.Join("\n", Enumerable.Range(1, 300)));
        DiffLensLayout.Build("repo", cells);
        var before = cells.Select(cell => cell.Bounds).ToArray();
        DiffLensLayout.Build("repo", Enumerable.Reverse(cells).ToArray());
        Assert.That(cells.Select(cell => cell.Bounds), Is.EqualTo(before));
    }

    [Test]
    public void GroupReviewIncludesNestedFilesButNotSiblingsAndNeverUnticks()
    {
        DiffLensCell TextCell(string path) => new(new DiffLensEntry(path,
            new DiffLensFile(path, path, "HEAD", "old", System.Text.Encoding.UTF8.GetBytes("new")), null));
        var direct = TextCell("src/direct.cpp");
        var nested = TextCell("src/shaders/main.glsl");
        var sibling = TextCell("tests/test.cpp");
        var binary = Cell("src/image.bin");
        var cells = new[] { direct, nested, sibling, binary };
        var root = DiffLensLayout.Build("repo", cells);
        var folder = root.Folders.Single(f => f.RelativePath == "src");
        folder.MarkReviewed();
        folder.MarkReviewed();
        Assert.That(direct.IsReviewed && nested.IsReviewed, Is.True);
        Assert.That(sibling.IsReviewed || binary.IsReviewed, Is.False);
        Assert.That(folder.IsReviewed, Is.True);
        nested.Update("old", "changed again");
        Assert.That(folder.IsReviewed, Is.False);
        root.MarkReviewed();
        Assert.That(root.IsReviewed, Is.True);
        Assert.That(sibling.IsReviewed, Is.True);
    }

    private static DiffLensCell Cell(string path) => new(new DiffLensEntry(path, null, "Unsupported"));
}
