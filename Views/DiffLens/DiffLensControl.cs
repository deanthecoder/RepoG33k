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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using DTC.Core;
using Avalonia.Threading;
using System.Diagnostics;
using DiffPlex.DiffBuilder.Model;
using ReviewG33k.Services.DiffLens;
using ReviewG33k.Services;

namespace ReviewG33k.Views.DiffLens;

/// <summary>
/// Displays changed files in folder groups on a pointer-centered canvas with per-file diff presentation.
/// </summary>
/// <remarks>
/// Lightweight drawing keeps zoom independent of editor layout; rows retain source identities.
/// </remarks>
internal sealed class DiffLensControl : Control
{
    private const double CellWidth = 1040;
    private const double HeaderHeight = 68;
    private const double RowHeight = 22;
    private const double TextZoomThreshold = 0.2;
    private LruCache<(string Text, double Size, IBrush Brush), FormattedText> m_textCache = new(8192);
    private LruCache<(string Path, double Size, double Width), FormattedText> m_titleCache = new(1024);
    private readonly LruCache<(bool Accent, double Thickness), ImmutablePen> m_iconPens = new(32);
    private readonly Func<(string Text, double Size, IBrush Brush), FormattedText> m_createText;
    private readonly Func<(string Path, double Size, double Width), FormattedText> m_createTitle;
    private readonly Func<(bool Accent, double Thickness), ImmutablePen> m_createIconPen;
    private readonly Func<(DiffLensHighlightedText Syntax, int Line, string Text), FormattedText> m_createCode;
    private readonly IImmutableBrush m_gridBrush = Brush.Parse("#24303E").ToImmutable();
    private readonly IImmutableBrush m_accentBrush = Brush.Parse("#66DCC1").ToImmutable();
    private readonly IImmutableBrush m_modeSelectedBrush = Brush.Parse("#28544C").ToImmutable();
    private readonly IImmutableBrush m_modeBrush = Brush.Parse("#202E3D").ToImmutable();
    private readonly ImmutablePen m_folderPen = new(Brush.Parse("#63816D").ToImmutable(), 1);
    private readonly ImmutablePen m_borderPen = new(Brush.Parse("#36546A").ToImmutable(), 1);
    private readonly ImmutablePen m_selectedPen = new(Brush.Parse("#E9BC70").ToImmutable(), 1.5);
    private readonly Typeface m_font = new("Cascadia Mono,Consolas,monospace");
    private readonly IImmutableBrush m_background = Brush.Parse("#0C1219").ToImmutable();
    private readonly IImmutableBrush m_card = Brush.Parse("#151E29").ToImmutable();
    private readonly IImmutableBrush m_selectedCard = Brush.Parse("#29251B").ToImmutable();
    private readonly IImmutableBrush m_findingMarker = Brush.Parse("#E9BC70").ToImmutable();
    private readonly IImmutableBrush m_text = Brush.Parse("#D7E3EE").ToImmutable();
    private readonly IImmutableBrush m_muted = Brush.Parse("#8194A8").ToImmutable();
    private readonly IImmutableBrush m_added = Brush.Parse("#173C31").ToImmutable();
    private readonly IImmutableBrush m_removed = Brush.Parse("#44282E").ToImmutable();
    private IReadOnlyList<DiffLensCell> m_cells = [];
    private DiffLensFolder m_root;
    private int? m_previousDetail;
    private DiffLensCell m_fadingCell;
    public string SelectedPath { get; private set; }
    public Func<string, bool> CanStageFile { get; set; }
    public Func<string, Task<bool>> StageFileAsync { get; set; }
    private bool m_fileActionBusy;
    private Vector m_offset = new(60, 45);
    private Point? m_drag;
    private string m_fitFilePath;
    private string m_fitFolderPath;
    private readonly DispatcherTimer m_resizeTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer m_panelResizeTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private double m_zoom = 0.6;
    private double m_horizontalScroll;
    private string m_hoverHint;
    private readonly DispatcherTimer m_hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly Stopwatch m_fitClock = new();
    private readonly DispatcherTimer m_fitTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private double m_fitStartZoom;
    private double m_fitEndZoom;
    private Vector m_fitStartOffset;
    private Vector m_fitEndOffset;
    private readonly Stopwatch m_fadeClock = new();
    private readonly DispatcherTimer m_fadeTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };

    public DiffLensControl()
    {
        m_createText = key => new FormattedText(key.Text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, m_font, key.Size, key.Brush);
        m_createTitle = key => new FormattedText(Path.GetFileName(key.Path), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, m_font, key.Size, m_text)
        {
            MaxTextWidth = key.Width, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis
        };
        m_createIconPen = key => new ImmutablePen(key.Accent ? m_accentBrush : m_muted, key.Thickness);
        m_createCode = CreateCodeText;
        m_resizeTimer.Tick += (_, _) => RefitAfterResize();
        m_panelResizeTimer.Tick += (_, _) => m_panelResizeTimer.Stop();
        SizeChanged += (_, _) =>
        {
            m_resizeTimer.Stop();
            if (!m_panelResizeTimer.IsEnabled && m_root != null && Bounds.Width > 0 && Bounds.Height > 0)
                m_resizeTimer.Start();
        };
        ClipToBounds = true;
        Focusable = true;
        // The whole canvas is one control; manage hover as the pointer crosses drawn rows.
        ToolTip.SetServiceEnabled(this, false);
        m_hoverTimer.Tick += (_, _) =>
        {
            m_hoverTimer.Stop();
            if (m_hoverHint != null && m_drag == null && ContextMenu?.IsOpen != true && TopLevel.GetTopLevel(this) != null)
                ToolTip.SetIsOpen(this, true);
        };
        Gestures.AddPointerTouchPadGestureMagnifyHandler(this, (_, e) =>
        {
            // macOS supplies incremental magnification, centered on the pointer.
            HideHoverHint();
            SetZoom(m_zoom * (1 + e.Delta.Y), e.GetPosition(this));
            e.Handled = true;
        });
        m_fadeTimer.Tick += (_, _) =>
        {
            InvalidateVisual();
            if (m_fadeClock.ElapsedMilliseconds >= 180)
            {
                m_fadeTimer.Stop();
                m_previousDetail = null;
            }
        };
        m_fitTimer.Tick += (_, _) =>
        {
            var progress = Math.Clamp(m_fitClock.Elapsed.TotalMilliseconds / 250, 0, 1);
            var eased = 1 - Math.Pow(1 - progress, 3);
            // Use the same easing and frame for position and scale, including the border.
            m_zoom = m_fitStartZoom + (m_fitEndZoom - m_fitStartZoom) * eased;
            m_offset = m_fitStartOffset + (m_fitEndOffset - m_fitStartOffset) * eased;
            if (progress >= 1)
            {
                m_zoom = m_fitEndZoom;
                m_offset = m_fitEndOffset;
                m_fitTimer.Stop();
            }
            InvalidateVisual();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        };
        DetachedFromVisualTree += (_, _) => StopAnimations();
    }

    public void StopAnimations()
    {
        m_resizeTimer.Stop();
        m_panelResizeTimer.Stop();
        HideHoverHint();
        m_fitTimer.Stop();
        m_fadeTimer.Stop();
    }

    public void PreserveViewDuringPanelResize()
    {
        m_resizeTimer.Stop();
        m_fitTimer.Stop();
        m_panelResizeTimer.Stop();
        m_panelResizeTimer.Start();
    }

    public event EventHandler ViewChanged;
    public event EventHandler<(string Path, int Line)> EditRequested;
    public double Zoom => m_zoom;
    public string RepositoryRootPath { get; set; }
    public Func<CodeSmellFinding, int> FindingLineResolver { get; set; }

    private int FindingLine(CodeSmellFinding finding) => FindingLineResolver?.Invoke(finding) ?? finding.LineNumber;

    public void SetFiles(string rootName, IReadOnlyList<DiffLensCell> cells, bool preserveReview = true)
    {
        m_textCache = new(8192);
        m_codeCache = new(8192);
        m_titleCache = new(1024);
        var previousCells = m_cells.ToDictionary(cell => cell.Entry.Path);
        var selectedPath = preserveReview ? SelectedPath : null;
        foreach (var cell in cells)
            if (preserveReview && previousCells.TryGetValue(cell.Entry.Path, out var previous))
                cell.RestoreReviewState(previous);
        m_previousDetail = null;
        m_cells = cells;
        m_root = DiffLensLayout.Build(rootName, cells);
        SelectedPath = cells.FirstOrDefault(cell => cell.Entry.Path == selectedPath)?.Entry.Path
            ?? cells.FirstOrDefault(cell => cell.Entry.File != null)?.Entry.Path;
        Fit(false);
    }

    public void SetFile(string path, string baseline, string working, bool resetCamera = true)
    {
        var cell = m_cells.FirstOrDefault(item => item.Entry.Path == path);
        if (cell == null)
            return;
        m_fitTimer.Stop();
        var position = cell.Position;
        // Replacing this cell's syntax snapshots invalidates only its code-cache keys.
        cell.Update(baseline, working);
        m_root = DiffLensLayout.Build(m_root.Name, m_cells);
        m_offset += (position - cell.Position) * m_zoom;
        m_previousDetail = null;
        if (resetCamera)
            Fit();
        InvalidateVisual();
    }

    public void Fit(bool animate = true)
    {
        if (m_root != null)
            FitBounds(m_root, animate);
    }

    private void FitBounds(DiffLensFolder folder, bool animate = true)
    {
        m_fitFilePath = null;
        m_fitFolderPath = folder.RelativePath;
        FitBounds(folder.Bounds, animate);
    }

    public bool FocusFile(string path)
    {
        var cell = m_cells.FirstOrDefault(item => item.Entry.Path == path);
        if (cell == null)
            return false;
        SelectedPath = path;
        FitFile(cell);
        ViewChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void FitFile(DiffLensCell cell)
    {
        m_fitFilePath = cell.Entry.Path;
        m_fitFolderPath = null;
        FitBounds(cell.Bounds, maximumZoom: 2.5);
    }

    private void RefitAfterResize()
    {
        m_resizeTimer.Stop();
        var cell = m_cells.FirstOrDefault(item => item.Entry.Path == m_fitFilePath);
        if (cell != null)
            FitFile(cell);
        else if (m_root != null)
            FitBounds(m_root.Descendants.FirstOrDefault(folder => folder.RelativePath == m_fitFolderPath) ?? m_root);
    }

    private void FitBounds(Rect bounds, bool animate = true, double maximumZoom = 0.72)
    {
        m_resizeTimer.Stop();
        HideHoverHint();
        var zoom = Math.Clamp(Math.Min((Bounds.Width - 60) / bounds.Width, (Bounds.Height - 60) / bounds.Height), 0.001, maximumZoom);
        var offset = new Vector((Bounds.Width - bounds.Width * zoom) / 2 - bounds.X * zoom,
            (Bounds.Height - bounds.Height * zoom) / 2 - bounds.Y * zoom);
        m_fitTimer.Stop();
        m_previousDetail = null;
        m_horizontalScroll = 0;
        if (animate)
        {
            m_fitStartZoom = m_zoom;
            m_fitEndZoom = zoom;
            m_fitStartOffset = m_offset;
            m_fitEndOffset = offset;
            m_fitClock.Restart();
            m_fitTimer.Start();
        }
        else
        {
            m_zoom = zoom;
            m_offset = offset;
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }
        InvalidateVisual();
    }

    public void ZoomBy(double factor) => SetZoom(m_zoom * factor, new Point(Bounds.Width / 2, Bounds.Height / 2));

    private DiffLensCell HitCell(Point world) => m_cells.FirstOrDefault(cell =>
        new Rect(cell.Position, new Size(CellWidth, cell.Height)).Contains(world));

    private void SetZoom(double zoom, Point pointer)
    {
        HideHoverHint();
        m_fitTimer.Stop();
        zoom = Math.Clamp(zoom, 0.001, 2.5);
        var local = (pointer - m_offset) / m_zoom;
        m_zoom = zoom;
        m_offset = pointer - local * zoom;
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetMode(DiffLensCell cell, int detail)
    {
        HideHoverHint();
        m_fitTimer.Stop();
        if (cell.Detail == detail)
            return;
        var position = cell.Position;
        m_fadingCell = cell;
        m_previousDetail = cell.Detail;
        cell.Detail = detail;
        m_root = DiffLensLayout.Build(m_root.Name, m_cells);
        // Keep the clicked file header anchored while its contents and neighbors resize.
        m_offset += (position - cell.Position) * m_zoom;
        m_fadeClock.Restart();
        m_fadeTimer.Start();
        InvalidateVisual();
    }

    private void ToggleReviewed(DiffLensCell cell)
    {
        HideHoverHint();
        m_fitTimer.Stop();
        var position = cell.Position;
        cell.IsReviewed = !cell.IsReviewed;
        m_previousDetail = null;
        m_root = DiffLensLayout.Build(m_root.Name, m_cells);
        m_offset += (position - cell.Position) * m_zoom;
        InvalidateVisual();
    }

    internal async Task ActivateFileActionAsync(DiffLensCell cell)
    {
        if (m_fileActionBusy)
            return;
        var path = cell.Entry.Path;
        if (!cell.IsReviewed && StageFileAsync != null && CanStageFile?.Invoke(path) == true)
        {
            m_fileActionBusy = true;
            try
            {
                if (await StageFileAsync(path))
                {
                    // Staging refreshes the comparison. Collapse its new card, if it remains visible.
                    var refreshed = m_cells.FirstOrDefault(c => c.Entry.Path == path);
                    if (refreshed is { IsReviewed: false })
                        ToggleReviewed(refreshed);
                }
            }
            finally { m_fileActionBusy = false; }
            return;
        }
        ToggleReviewed(cell);
    }

    private Rect FolderReviewButton(DiffLensFolder folder)
    {
        var size = Math.Min(18, 32 * m_zoom);
        var title = FolderTitle(folder);
        return new Rect(m_offset.X + folder.Bounds.Left * m_zoom + 6,
            m_offset.Y + folder.Bounds.Top * m_zoom + 3 + Math.Max(0, (title.Height - size) / 2), size, size);
    }

    private void MarkFolderReviewed(DiffLensFolder folder)
    {
        HideHoverHint();
        m_fitTimer.Stop();
        var anchor = FolderReviewButton(folder).Position;
        folder.MarkReviewed();
        m_previousDetail = null;
        m_root = DiffLensLayout.Build(m_root.Name, m_cells);
        var updated = m_root.Descendants.First(item => item.RelativePath == folder.RelativePath);
        m_offset += anchor - FolderReviewButton(updated).Position;
        InvalidateVisual();
    }

    private Rect ReviewButton(DiffLensCell cell)
    {
        var mode = ModeButton(cell, true);
        return mode.Translate(new Vector(mode.Width + 6 * m_zoom, 0));
    }

    private Rect ModeButton(DiffLensCell cell, bool split)
    {
        var size = Math.Min(26, 32 * m_zoom);
        var gap = 6 * m_zoom;
        var right = m_offset.X + cell.Bounds.Right * m_zoom - gap - size - gap;
        return new Rect(right - size - (split ? 0 : size + gap),
            m_offset.Y + cell.Position.Y * m_zoom + gap, size, size);
    }

    public bool FocusFinding(string path, int line)
    {
        HideHoverHint();
        var cell = m_cells.FirstOrDefault(item => string.Equals(item.Entry.Path, path, StringComparison.OrdinalIgnoreCase));
        if (cell == null)
            return false;
        m_fitTimer.Stop();
        SelectedPath = cell.Entry.Path;
        if (cell.IsReviewed)
            ToggleReviewed(cell);
        var row = Math.Max(0, cell.FindingRow(line, cell.Detail));
        m_zoom = Math.Clamp((Bounds.Width - 60) / CellWidth, 0.45, 1.2);
        m_offset = new Vector((Bounds.Width - CellWidth * m_zoom) / 2 - cell.Position.X * m_zoom,
            Bounds.Height / 2 - (cell.Position.Y + HeaderHeight + row * RowHeight) * m_zoom);
        InvalidateVisual();
        ViewChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void UpdateFindings(Func<string, IReadOnlyList<CodeSmellFinding>> findingsForPath)
    {
        ArgumentNullException.ThrowIfNull(findingsForPath);
        foreach (var cell in m_cells)
            cell.Findings = findingsForPath(cell.Entry.Path);
        InvalidateVisual();
    }

    public IReadOnlyList<CodeSmellFinding> SelectedFindings =>
        m_cells.FirstOrDefault(cell => cell.Entry.Path == SelectedPath)?.Findings ?? [];

    public IReadOnlyList<CodeSmellFinding> FindingsAt(Point point)
    {
        foreach (var cell in m_cells.Reverse())
        {
            if (cell.IsReviewed)
                continue;
            var left = m_offset.X + cell.Position.X * m_zoom;
            var top = m_offset.Y + (cell.Position.Y + HeaderHeight) * m_zoom;
            var margin = point.X >= left - 8 && point.X <= left + Math.Max(18, 8 * m_zoom);
            if (!margin && !cell.Bounds.Contains((point - m_offset) / m_zoom))
                continue;
            if (margin)
            {
                var closest = cell.Findings.Select(finding => (Finding: finding, Row: cell.FindingRow(FindingLine(finding), cell.Detail)))
                    .Where(item => item.Row >= 0)
                    .Select(item => (item.Finding, item.Row, Distance: Math.Abs(point.Y - (top + (item.Row + 0.5) * RowHeight * m_zoom))))
                    .Where(item => item.Distance <= Math.Max(7, RowHeight * m_zoom / 2))
                    .OrderBy(item => item.Distance).ToArray();
                if (closest.Length > 0)
                    return closest.Where(item => item.Row == closest[0].Row).Select(item => item.Finding).ToArray();
            }
            else
            {
                var row = (int)Math.Floor((point.Y - top) / (RowHeight * m_zoom));
                var findings = cell.Findings.Where(finding => cell.FindingRow(FindingLine(finding), cell.Detail) == row && row >= 0).ToArray();
                if (findings.Length > 0)
                    return findings;
            }
        }
        return [];
    }

    private void DrawFindings(DrawingContext context, DiffLensCell cell, Vector offset)
    {
        if (cell.Findings.Count == 0 || cell.IsReviewed)
            return;
        var marker = m_findingMarker;
        using (context.PushTransform(Matrix.CreateScale(m_zoom, m_zoom) * Matrix.CreateTranslation(offset)))
        {
            if (m_zoom >= TextZoomThreshold)
                DrawText(context, $"! {cell.Findings.Count}", 12, 39, marker, 15);
            foreach (var row in cell.Findings.Select(finding => cell.FindingRow(FindingLine(finding), cell.Detail)).Where(row => row >= 0).Distinct())
                context.FillRectangle(marker, new Rect(1, HeaderHeight + row * RowHeight, Math.Max(5, 2 / m_zoom), Math.Max(RowHeight, 1 / m_zoom)));
        }
    }

    private void DrawGroupCollapse(DrawingContext context, Rect bounds, bool reviewed)
    {
        context.FillRectangle(reviewed ? m_modeSelectedBrush : m_modeBrush, bounds, 2);
        var icon = bounds.Deflate(bounds.Width * 0.18);
        var pen = m_iconPens.GetOrAdd((reviewed, Math.Min(1.3, m_zoom * 2)), m_createIconPen);
        for (var corner = 0; corner < 4; corner++)
        {
            var dx = (corner & 1) == 0 ? -1 : 1;
            var dy = (corner & 2) == 0 ? -1 : 1;
            var tip = icon.Center + new Vector(dx * icon.Width * 0.13, dy * icon.Height * 0.13);
            var tail = icon.Center + new Vector(dx * icon.Width / 2, dy * icon.Height / 2);
            context.DrawLine(pen, tail, tip);
            context.DrawLine(pen, tip, tip + new Vector(dx * icon.Width * 0.25, 0));
            context.DrawLine(pen, tip, tip + new Vector(0, dy * icon.Height * 0.25));
        }
    }

    private void DrawReviewTick(DrawingContext context, Rect bounds, bool reviewed)
    {
        context.FillRectangle(reviewed ? m_modeSelectedBrush : m_modeBrush, bounds, 2);
        var icon = bounds.Deflate(bounds.Width * 0.24);
        var pen = m_iconPens.GetOrAdd((reviewed, Math.Min(2, m_zoom * 3)), m_createIconPen);
        var elbow = new Point(icon.Left + icon.Width * 0.35, icon.Bottom);
        context.DrawLine(pen, new Point(icon.Left, icon.Center.Y), elbow);
        context.DrawLine(pen, elbow, new Point(icon.Right, icon.Top));
    }

    private void DrawModeButtons(DrawingContext context, DiffLensCell cell)
    {
        if (32 * m_zoom < 6)
            return;
        if (cell.Entry.File != null)
        {
            var bounds = ReviewButton(cell);
            DrawReviewTick(context, bounds, cell.IsReviewed);
        }
        for (var mode = 0; mode < 2; mode++)
        {
            var split = mode == 1;
            var bounds = ModeButton(cell, split);
            var selected = (cell.Detail >= 3) == split;
            context.FillRectangle(selected ? m_modeSelectedBrush : m_modeBrush, bounds, 2);
            var icon = bounds.Deflate(bounds.Width * 0.24);
            var pen = m_iconPens.GetOrAdd((selected, Math.Min(1.3, m_zoom * 2)), m_createIconPen);
            context.DrawRectangle(pen, icon);
            if (split)
                context.DrawLine(pen, new Point(icon.Center.X, icon.Top), new Point(icon.Center.X, icon.Bottom));
            else
            {
                context.DrawLine(pen, new Point(icon.Left, icon.Top + icon.Height / 3), new Point(icon.Right, icon.Top + icon.Height / 3));
                context.DrawLine(pen, new Point(icon.Left, icon.Top + icon.Height * 2 / 3), new Point(icon.Right, icon.Top + icon.Height * 2 / 3));
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(m_background, new Rect(Bounds.Size));
        for (double x = m_offset.X % 32; x < Bounds.Width; x += 32)
            for (double y = m_offset.Y % 32; y < Bounds.Height; y += 32)
                context.FillRectangle(m_gridBrush, new Rect(x, y, 1, 1));
        // Retain an opaque outgoing layer beneath the incoming layer. Fading both
        // independently would dim their combined alpha and expose the background.
        if (m_root == null)
            return;
        foreach (var folder in m_root.Descendants)
        {
            var rect = new Rect(folder.Bounds.Position * m_zoom + m_offset, folder.Bounds.Size * m_zoom);
            if (!rect.Intersects(new Rect(Bounds.Size)))
                continue;
            context.DrawRectangle(m_folderPen, rect, 8);
            var label = FolderLabel(folder);
            if (label.Height < 7 || !label.Intersects(new Rect(Bounds.Size)))
                continue;
            var tick = FolderReviewButton(folder);
            var showTick = 32 * m_zoom >= 6 && folder.ReviewableFiles.Any();
            var titleLeft = showTick ? tick.Right + 6 : label.Left + 6;
            using (context.PushClip(new Rect(titleLeft, label.Top, Math.Max(0, label.Right - titleLeft - 6), label.Height)))
                context.DrawText(FolderTitle(folder), new Point(titleLeft, label.Y + 3));
            if (showTick)
                DrawGroupCollapse(context, tick, folder.IsReviewed);
        }
        var progress = m_previousDetail == null ? 1 : Math.Clamp(m_fadeClock.Elapsed.TotalMilliseconds / 180, 0, 1);
        foreach (var cell in m_cells)
        {
            var offset = m_offset + (Vector)cell.Position * m_zoom;
            if (!new Rect(cell.Position * m_zoom + m_offset, cell.Bounds.Size * m_zoom).Intersects(new Rect(Bounds.Size)))
                continue;
            // One opaque card and one geometry for content, border and hit testing.
            // Only the row content fades; outgoing content cannot leave a ghost card.
            using (context.PushTransform(Matrix.CreateScale(m_zoom, m_zoom) * Matrix.CreateTranslation(offset)))
                context.FillRectangle(cell.Entry.Path == SelectedPath ? m_selectedCard : m_card, new Rect(0, 0, CellWidth, cell.Height), 8);
            using (context.PushClip(new Rect(cell.Position * m_zoom + m_offset,
                       new Size(CellWidth * m_zoom, cell.Height * m_zoom))))
            {
                if (!cell.IsReviewed && cell == m_fadingCell && m_previousDetail is { } previous && progress < 1)
                    DrawLayer(context, cell, previous, offset, 1);
                if (!cell.IsReviewed)
                    DrawLayer(context, cell, cell.Detail, offset, cell == m_fadingCell ? progress : 1);
            }
            context.DrawRectangle(null, cell.Entry.Path == SelectedPath ? m_selectedPen : m_borderPen,
                new Rect(cell.Position * m_zoom + m_offset, new Size(CellWidth * m_zoom, cell.Height * m_zoom)), 8 * m_zoom, 8 * m_zoom);
            DrawTitle(context, cell, offset);
            DrawModeButtons(context, cell);
            DrawFindings(context, cell, offset);
            if (cell.Entry.Error != null)
            {
                using (context.PushClip(new Rect(cell.Position * m_zoom + m_offset, cell.Bounds.Size * m_zoom)))
                    DrawText(context, "Preview unavailable: " + cell.Entry.Error, offset.X + 12 * m_zoom, offset.Y + HeaderHeight * m_zoom, m_muted, 14 * m_zoom);
            }

        }
    }

    private FormattedText FolderTitle(DiffLensFolder folder) => m_textCache.GetOrAdd(
        ($"{folder.Name}  ·  {folder.FileCount} files", Math.Min(14, 38 * m_zoom), m_muted), m_createText);

    private Rect FolderLabel(DiffLensFolder folder) => new(folder.Bounds.Position * m_zoom + m_offset,
        new Size(folder.Bounds.Width * m_zoom, DiffLensLayout.FolderHeader * m_zoom));

    private void DrawLayer(DrawingContext context, DiffLensCell cell, int detail, Vector offset, double opacity)
    {
        var rows = cell.Rows(detail);
        using (context.PushOpacity(opacity))
        using (context.PushTransform(Matrix.CreateScale(m_zoom, m_zoom) * Matrix.CreateTranslation(offset)))
        {
            var height = Math.Max(160, HeaderHeight + rows.Count * RowHeight + 24);
            context.FillRectangle(m_card, new Rect(0, HeaderHeight, CellWidth, height - HeaderHeight));
            using (context.PushClip(new Rect(0, HeaderHeight, CellWidth, height - HeaderHeight)))
            {
                var first = Math.Max(0, (int)((-offset.Y / m_zoom - HeaderHeight) / RowHeight));
                var last = Math.Min(rows.Count, (int)((Bounds.Height - offset.Y) / m_zoom / RowHeight) + 2);
                if (m_zoom < TextZoomThreshold)
                {
                    DrawOverview(context, cell, rows, detail, first, last);
                    return;
                }
                for (var i = first; i < last; i++)
                {
                    var row = rows[i];
                    var y = HeaderHeight + i * RowHeight;
                    if (row.Notice != null)
                    {
                        DrawText(context, row.Notice, 18, y + 2, m_findingMarker);
                        continue;
                    }
                    if (row.HiddenLines > 0)
                    {
                        DrawText(context, $"⋯  {row.HiddenLines} unchanged lines", 18, y + 2, m_muted);
                        continue;
                    }
                    if (detail >= 3)
                    {
                        DrawPiece(context, cell, row.Left, 0, y, CellWidth / 2, true, row.Changed);
                        DrawPiece(context, cell, row.Right, CellWidth / 2, y, CellWidth / 2, false, row.Changed);
                    }
                    else
                        DrawPiece(context, cell, row.Right ?? row.Left, 0, y, CellWidth, row.Right == null, row.Changed);
                }
            }
            if (detail >= 3)
                context.DrawLine(m_borderPen, new Point(CellWidth / 2, 38), new Point(CellWidth / 2, height));
        }
    }

    private void DrawTitle(DrawingContext context, DiffLensCell cell, Vector offset)
    {
        // Do not shape text that is too small to read in the overview.
        if (HeaderHeight * m_zoom < 7 || CellWidth * m_zoom < 40)
            return;
        var titleSize = Math.Min(14, 36 * m_zoom);
        var titleBounds = new Rect(offset.X + 2, offset.Y + 1, Math.Max(1, ModeButton(cell, false).Left - offset.X - 6), Math.Max(1, HeaderHeight * m_zoom - 2));
        using (context.PushClip(titleBounds))
        {
            var title = m_titleCache.GetOrAdd((cell.Entry.Path, titleSize, Math.Max(1, Math.Round(titleBounds.Width - 12, 2))), m_createTitle);
            context.DrawText(title, new Point(titleBounds.X + 6, titleBounds.Y + Math.Min(6, m_zoom * 6)));
        }
        if (!cell.IsReviewed && cell.Detail >= 3 && m_zoom >= 0.45)
        {
            DrawText(context, "BRANCH BASE", offset.X + (cell.Findings.Count > 0 ? 90 : 12) * m_zoom, offset.Y + 40 * m_zoom, m_muted, 11);
            DrawText(context, "WORKING FILE", offset.X + CellWidth * m_zoom / 2 + 12, offset.Y + 40 * m_zoom, m_accentBrush, 11);
        }
    }

    private void DrawPiece(DrawingContext context, DiffLensCell cell, DiffPiece piece, double x, double y, double width, bool oldSide, bool changed)
    {
        if (piece == null || piece.Type == ChangeType.Imaginary)
            return;
        using (context.PushClip(new Rect(x, y, width, RowHeight)))
        {
            if (changed)
                context.FillRectangle(oldSide ? m_removed : m_added, new Rect(x, y, width, RowHeight));
            var marker = changed ? oldSide ? "−" : "+" : " ";
            DrawText(context, $"{piece.Position,4} {marker}", x + 8, y + 2, m_muted, 12);
            using (context.PushClip(new Rect(x + 68, y, width - 68, RowHeight)))
                DrawCode(context, cell, piece, oldSide, x + 70 - m_horizontalScroll, y + 2);
        }
    }

    private void DrawOverview(DrawingContext context, DiffLensCell cell, IReadOnlyList<DiffLensRow> rows, int detail, int first, int last)
    {
        var overview = cell.Overview(detail);
        var stride = Math.Max(1, (int)Math.Ceiling(2 / (RowHeight * m_zoom)));
        for (var index = first - first % stride; index < last; index += stride)
        {
            var count = Math.Min(stride, rows.Count - index);
            var change = overview.Changes(index, count);
            var y = HeaderHeight + index * RowHeight;
            var height = count * RowHeight;
            var split = detail >= 3;
            if (change.Removed)
                context.FillRectangle(m_removed, new Rect(0, y, split || change.Added ? CellWidth / 2 : CellWidth, height));
            if (change.Added)
                context.FillRectangle(m_added, new Rect(split || change.Removed ? CellWidth / 2 : 0, y, split || change.Removed ? CellWidth / 2 : CellWidth, height));
            if (CellWidth * m_zoom < 28)
                continue;
            var row = rows[index];
            using (context.PushOpacity(0.35))
            {
                if (split)
                {
                    DrawOverviewStroke(context, row.Left, 0, y + height / 2, CellWidth / 2);
                    DrawOverviewStroke(context, row.Right, CellWidth / 2, y + height / 2, CellWidth / 2);
                }
                else
                    DrawOverviewStroke(context, row.Right ?? row.Left, 0, y + height / 2, CellWidth);
            }
        }
    }

    private void DrawOverviewStroke(DrawingContext context, DiffPiece piece, double x, double y, double width)
    {
        var length = piece?.Text?.Length ?? 0;
        if (length > 0)
            context.FillRectangle(m_muted, new Rect(x + 70, y, Math.Min(width - 80, length * 8.4), 1 / m_zoom));
    }

    private LruCache<(DiffLensHighlightedText Syntax, int Line, string Text), FormattedText> m_codeCache = new(8192);

    private void DrawCode(DrawingContext context, DiffLensCell cell, DiffPiece piece, bool oldSide, double x, double y)
    {
        var syntax = oldSide ? cell.BaselineSyntax : cell.WorkingSyntax;
        if (syntax == null || piece.Position is not { } lineNumber)
        {
            DrawText(context, piece.Text.Replace("\t", "    "), x, y, m_text);
            return;
        }
        var formatted = m_codeCache.GetOrAdd((syntax, lineNumber, piece.Text), m_createCode);
        context.DrawText(formatted, new Point(x, y));
    }

    private FormattedText CreateCodeText((DiffLensHighlightedText Syntax, int Line, string Text) key)
    {
        var formatted = new FormattedText(key.Text.Replace("\t", "    "), CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight, m_font, 14, m_text);
        var line = key.Syntax.Line(key.Line);
        if (line != null)
            foreach (var section in line.Sections)
                if (section.Color.Foreground is { } foreground)
                    formatted.SetForegroundBrush(foreground.GetBrush(null).ToImmutable(), section.Offset - line.DocumentLine.Offset, section.Length);
        return formatted;
    }

    private void DrawText(DrawingContext context, string text, double x, double y, IBrush brush, double size = 14)
    {
        var formatted = m_textCache.GetOrAdd((text, size, brush), m_createText);
        context.DrawText(formatted, new Point(x, y));
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        HideHoverHint();
        m_fitTimer.Stop();
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control) ||
            (OperatingSystem.IsMacOS() && e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
            SetZoom(m_zoom * Math.Pow(1.15, e.Delta.Y), e.GetPosition(this));
        else
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
                m_horizontalScroll = Math.Max(0, m_horizontalScroll - (e.Delta.X != 0 ? e.Delta.X : e.Delta.Y) * 45);
            else
                m_offset += new Vector(e.Delta.X * 45, e.Delta.Y * 45);
            InvalidateVisual();
        }
        e.Handled = true;
    }

    protected override async void OnPointerPressed(PointerPressedEventArgs e)
    {
        HideHoverHint();
        base.OnPointerPressed(e);
        m_fitTimer.Stop();
        Focus();
        var local = (e.GetPosition(this) - m_offset) / m_zoom;
        var cell = HitCell(local);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            if (cell != null)
            {
                SelectedPath = cell.Entry.Path;
                ViewChanged?.Invoke(this, EventArgs.Empty);
                InvalidateVisual();
                var point = e.GetPosition(this);
                if (cell.Entry.File != null && ReviewButton(cell).Contains(point))
                {
                    m_drag = null;
                    e.Pointer.Capture(null);
                    e.Handled = true;
                    try { await ActivateFileActionAsync(cell); }
                    catch (Exception ex) { ToolTip.SetTip(this, "Could not stage file: " + ex.Message); }
                    return;
                }
                if (ModeButton(cell, false).Contains(point) || ModeButton(cell, true).Contains(point))
                {
                    m_drag = null;
                    e.Pointer.Capture(null);
                    SetMode(cell, ModeButton(cell, true).Contains(point) ? 3 : 0);
                    e.Handled = true;
                    return;
                }
                if (local.Y < cell.Position.Y + HeaderHeight)
                {
                    m_drag = null;
                    e.Pointer.Capture(null);
                    FitFile(cell);
                    e.Handled = true;
                    return;
                }
                if (e.ClickCount == 2)
                {
                    m_drag = null;
                    e.Pointer.Capture(null);
                    var rowIndex = (int)Math.Floor((local.Y - cell.Position.Y - HeaderHeight) / RowHeight);
                    EditRequested?.Invoke(this, (cell.Entry.Path, DiffLensRows.FindWorkingLine(cell.Rows(cell.Detail), rowIndex)));
                    e.Handled = true;
                    return;
                }
            }
            else
            {
                if (e.ClickCount == 2)
                {
                    var group = m_root?.Descendants.Reverse().FirstOrDefault(item => item.Bounds.Contains(local));
                    m_drag = null;
                    e.Pointer.Capture(null);
                    if (group != null)
                        FitBounds(group);
                    else
                        Fit();
                    e.Handled = true;
                    return;
                }
                var folder = m_root?.Descendants.Reverse().FirstOrDefault(item => FolderLabel(item).Contains(e.GetPosition(this)));
                if (folder != null)
                {
                    if (folder.ReviewableFiles.Any() && FolderReviewButton(folder).Contains(e.GetPosition(this)))
                        MarkFolderReviewed(folder);
                    else
                        FitBounds(folder);
                    e.Handled = true;
                    return;
                }
            }
        }
        if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed || e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            m_drag = e.GetPosition(this);
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (m_drag is not { } start)
        {
            var hoverPoint = e.GetPosition(this);
            var cell = HitCell((hoverPoint - m_offset) / m_zoom);
            var hint = cell == null ? null : cell.Entry.File != null && ReviewButton(cell).Contains(hoverPoint)
                ? (cell.IsReviewed ? "Mark unreviewed and expand" : StageFileAsync != null && CanStageFile?.Invoke(cell.Entry.Path) == true ? "Stage file and collapse (all working changes)" : "Mark reviewed and collapse") : ModeButton(cell, false).Contains(hoverPoint) ? "Unified diff (U)" :
                ModeButton(cell, true).Contains(hoverPoint) ? "Side-by-side diff (S)" : null;
            if (hint == null)
            {
                var findings = FindingsAt(hoverPoint);
                if (findings.Count > 0)
                    hint = string.Join("\n", findings.Select(finding => $"{finding.Severity}: {finding.Message}"));
            }
            if (hint == null)
            {
                var folder = m_root?.Descendants.Reverse().FirstOrDefault(item => FolderLabel(item).Contains(hoverPoint));
                if (folder != null)
                    hint = folder.ReviewableFiles.Any() && FolderReviewButton(folder).Contains(hoverPoint)
                        ? "Mark all files in this group reviewed and collapse"
                        : Path.Combine(RepositoryRootPath ?? m_root.Name, folder.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            }
            // Keep an unchanged tip alive while the pointer moves inside a marker's hit area.
            if (hint != m_hoverHint)
            {
                HideHoverHint();
                m_hoverHint = hint;
                ToolTip.SetTip(this, hint);
                if (hint != null)
                    m_hoverTimer.Start();
            }
            return;
        }
        var point = e.GetPosition(this);
        m_offset += point - start;
        m_drag = point;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.U or Key.S))
            return;
        var cell = m_cells.FirstOrDefault(item => item.Entry.Path == SelectedPath);
        if (cell == null)
            return;
        SetMode(cell, e.Key == Key.S ? 3 : 0);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        m_drag = null;
        e.Pointer.Capture(null);
        // Control raises ContextRequested here for a right-button release.
        base.OnPointerReleased(e);
    }

    private void HideHoverHint()
    {
        m_hoverTimer.Stop();
        m_hoverHint = null;
        ToolTip.SetIsOpen(this, false);
        ToolTip.SetTip(this, null);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        HideHoverHint();
        base.OnPointerExited(e);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        m_drag = null;
    }
}
