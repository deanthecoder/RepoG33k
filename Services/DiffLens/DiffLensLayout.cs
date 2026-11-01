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
using System.Linq;
using Avalonia;

namespace ReviewG33k.Services.DiffLens;

/// <summary>
/// Caches a file's detail levels and current canvas footprint.
/// </summary>
/// <remarks>
/// The footprint matches the visible detail so compact views leave no reserved gaps.
/// </remarks>
internal sealed class DiffLensCell
{
    public const double Width = 1040;
    public const double HeaderHeight = 68;
    public const double RowHeight = 22;
    public DiffLensEntry Entry { get; }
    public Point Position { get; set; }
    public IReadOnlyList<DiffLensRow>[] Levels { get; private set; }
    public int Detail { get; set; }
    public bool IsReviewed { get; set; }
    private string m_baseline;
    private string m_working;
    public DiffLensHighlightedText BaselineSyntax { get; private set; }
    public DiffLensHighlightedText WorkingSyntax { get; private set; }
    private DiffLensOverview[] m_overviews = new DiffLensOverview[4];
    public DiffLensOverview Overview(int detail) => m_overviews[detail] ??= new DiffLensOverview(Rows(detail));
    public IReadOnlyList<CodeSmellFinding> Findings { get; set; } = [];
    public double Height => DisplayHeight(Detail);
    public Rect Bounds => new(Position, new Size(Width, Height));
    public IReadOnlyList<DiffLensRow> Rows(int detail) => Levels[detail];
    public double DisplayHeight(int detail) => IsReviewed ? HeaderHeight : Math.Max(160, HeaderHeight + Rows(detail).Count * RowHeight + 24);

    public DiffLensCell(DiffLensEntry entry)
    {
        Entry = entry;
        Update(entry.File?.Baseline ?? string.Empty, entry.File?.WorkingText ?? string.Empty);
    }

    public void RestoreReviewState(DiffLensCell previous)
    {
        Detail = previous.Detail;
        IsReviewed = previous.IsReviewed && Entry.File != null && previous.Entry.File != null &&
            Entry.File.CanEdit == previous.Entry.File.CanEdit &&
            m_baseline == previous.m_baseline && m_working == previous.m_working;
    }

    public int FindingRow(int line, int detail)
    {
        var rows = Rows(detail);
        var lastLine = 0;
        for (var i = 0; i < rows.Count; i++)
        {
            if (rows[i].NewLine is { } current)
            {
                if (current == line)
                    return i;
                lastLine = current;
            }
            else if (rows[i].HiddenLines > 0)
            {
                if (line > lastLine && line <= lastLine + rows[i].HiddenLines)
                    return i;
                lastLine += rows[i].HiddenLines;
            }
        }
        return -1;
    }

    public void Update(string baseline, string working)
    {
        if (m_baseline != baseline || m_working != working)
            IsReviewed = false;
        m_baseline = baseline;
        m_working = working;
        var syntax = DiffLensSyntax.ForPath(Entry.Path);
        BaselineSyntax = syntax == null ? null : new DiffLensHighlightedText(baseline, syntax);
        WorkingSyntax = syntax == null ? null : new DiffLensHighlightedText(working, syntax);
        var (unified, split) = DiffLensRows.BuildBoth(baseline, working, 4);
        Levels = [unified, unified, unified, split];
        m_overviews = new DiffLensOverview[4];
    }
}

/// <summary>
/// Represents a folder frame and its contained changed files and folders.
/// </summary>
/// <remarks>
/// Folder bounds enclose the visible footprint of every descendant.
/// </remarks>
internal sealed class DiffLensFolder(string name)
{
    public string Name { get; set; } = name;
    public string RelativePath { get; set; } = string.Empty;
    public List<DiffLensFolder> Folders { get; } = [];
    public List<DiffLensCell> Files { get; } = [];
    public Rect Bounds { get; set; }
    public int FileCount => Files.Count + Folders.Sum(folder => folder.FileCount);
    public IEnumerable<DiffLensFolder> Descendants => new[] { this }.Concat(Folders.SelectMany(folder => folder.Descendants));
    public IEnumerable<DiffLensCell> ReviewableFiles => Descendants.SelectMany(folder => folder.Files).Where(cell => cell.Entry.File != null);
    public bool IsReviewed => ReviewableFiles.Any() && ReviewableFiles.All(cell => cell.IsReviewed);

    public void MarkReviewed()
    {
        foreach (var cell in ReviewableFiles)
            cell.IsReviewed = true;
    }

}

/// <summary>
/// Packs changed files into nested folder frames rooted at the repository.
/// </summary>
/// <remarks>
/// Empty ancestry is compacted into path labels; children retain nonoverlapping natural heights.
/// </remarks>
internal static class DiffLensLayout
{
    public const double Padding = 40;
    public const double FolderHeader = 80;

    public static DiffLensFolder Build(string rootName, IReadOnlyList<DiffLensCell> cells, int? detail = null)
    {
        var root = new DiffLensFolder(rootName);
        foreach (var cell in cells.OrderBy(cell => cell.Entry.Path, StringComparer.Ordinal))
        {
            if (detail.HasValue)
                cell.Detail = detail.Value;
            var folder = root;
            var parts = cell.Entry.Path.Replace('\\', '/').Split('/');
            foreach (var segment in parts.SkipLast(1))
            {
                var child = folder.Folders.FirstOrDefault(item => item.Name == segment);
                if (child == null)
                {
                    child = new DiffLensFolder(segment)
                    {
                        RelativePath = string.IsNullOrEmpty(folder.RelativePath) ? segment : $"{folder.RelativePath}/{segment}"
                    };
                    folder.Folders.Add(child);
                }
                folder = child;
            }
            folder.Files.Add(cell);
        }
        foreach (var child in root.Folders)
            Compact(child);
        Measure(root);
        Position(root, new Point(0, 0));
        return root;
    }

    private static void Compact(DiffLensFolder folder)
    {
        while (folder.Files.Count == 0 && folder.Folders.Count == 1)
        {
            var child = folder.Folders[0];
            folder.Name += " / " + child.Name;
            folder.RelativePath = child.RelativePath;
            folder.Files.AddRange(child.Files);
            folder.Folders.Clear();
            folder.Folders.AddRange(child.Folders);
        }
        foreach (var child in folder.Folders)
            Compact(child);
    }

    private static void Measure(DiffLensFolder folder)
    {
        foreach (var child in folder.Folders)
            Measure(child);
        var sizes = folder.Files.Select(cell => new Size(DiffLensCell.Width, cell.Height))
            .Concat(folder.Folders.Select(child => child.Bounds.Size)).ToArray();
        var packed = DiffLensRectanglePacker.Pack(sizes);
        var placed = new List<Rect>();
        for (var index = 0; index < packed.Count; index++)
        {
            var bounds = packed[index].Translate(new Vector(Padding, FolderHeader));
            placed.Add(bounds);
            if (index < folder.Files.Count)
                folder.Files[index].Position = bounds.Position;
            else
                folder.Folders[index - folder.Files.Count].Bounds = bounds;
        }
        folder.Bounds = new Rect(0, 0, placed.Count == 0 ? DiffLensCell.Width + Padding * 2 : placed.Max(rect => rect.Right) + Padding,
            placed.Count == 0 ? FolderHeader + Padding : placed.Max(rect => rect.Bottom) + Padding);
    }

    private static void Position(DiffLensFolder folder, Point origin)
    {
        folder.Bounds = new Rect(origin, folder.Bounds.Size);
        foreach (var file in folder.Files)
            file.Position += (Vector)origin;
        foreach (var child in folder.Folders)
            Position(child, child.Bounds.Position + (Vector)origin);
    }
}
