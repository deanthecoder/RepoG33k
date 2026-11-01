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
/// Packs differently sized file and folder blocks into reusable rectangular gaps.
/// </summary>
/// <remarks>
/// Blocks retain their natural size and orientation. A small deterministic width search
/// balances compactness with a landscape overview; input order breaks equal-size ties.
/// </remarks>
internal static class DiffLensRectanglePacker
{
    private const double PreferredAspect = 1.5;

    public static IReadOnlyList<Rect> Pack(IReadOnlyList<Size> sizes)
    {
        if (sizes.Count == 0)
            return [];
        var padding = DiffLensLayout.Padding;
        var blocks = sizes.Select(size => new Size(size.Width + padding, size.Height + padding)).ToArray();
        var maxWidth = blocks.Max(size => size.Width);
        var totalWidth = blocks.Sum(size => size.Width);
        var idealWidth = Math.Sqrt(blocks.Sum(size => size.Width * size.Height) * PreferredAspect);
        var widths = new[] { maxWidth, idealWidth * 0.75, idealWidth, idealWidth * 1.25, idealWidth * 1.5,
                blocks.Max(size => size.Height) * PreferredAspect }
            .Select(width => Math.Clamp(Math.Ceiling(width), maxWidth, totalWidth)).Distinct().OrderBy(width => width);
        var order = Enumerable.Range(0, sizes.Count).OrderByDescending(index => blocks[index].Height)
            .ThenByDescending(index => blocks[index].Width).ThenBy(index => index).ToArray();
        Rect[] best = null;
        var bestFit = double.PositiveInfinity;
        var bestArea = double.PositiveInfinity;
        foreach (var width in widths)
        {
            var placed = PackWidth(blocks, order, width);
            var usedWidth = placed.Max(rect => rect.Right);
            var usedHeight = placed.Max(rect => rect.Bottom);
            var fit = Math.Max(usedWidth / PreferredAspect, usedHeight);
            var area = usedWidth * usedHeight;
            if (fit < bestFit || (fit == bestFit && area < bestArea))
            {
                best = placed;
                bestFit = fit;
                bestArea = area;
            }
        }
        return best.Select((rect, index) => new Rect(rect.Position, sizes[index])).ToArray();
    }

    private static Rect[] PackWidth(IReadOnlyList<Size> blocks, IReadOnlyList<int> order, double width)
    {
        var free = new List<Rect> { new(0, 0, width, blocks.Sum(size => size.Height)) };
        var placed = new Rect[blocks.Count];
        foreach (var index in order)
        {
            var size = blocks[index];
            var space = free.Where(rect => rect.Width >= size.Width && rect.Height >= size.Height)
                .OrderBy(rect => rect.Y + size.Height).ThenBy(rect => rect.X)
                .ThenBy(rect => rect.Width * rect.Height).First();
            var used = new Rect(space.Position, size);
            placed[index] = used;
            // Split every intersecting free rectangle, preserving gaps on all four sides.
            for (var i = free.Count - 1; i >= 0; i--)
            {
                var rect = free[i];
                if (!rect.Intersects(used))
                    continue;
                free.RemoveAt(i);
                if (used.Left > rect.Left)
                    free.Add(new Rect(rect.Left, rect.Top, used.Left - rect.Left, rect.Height));
                if (used.Right < rect.Right)
                    free.Add(new Rect(used.Right, rect.Top, rect.Right - used.Right, rect.Height));
                if (used.Top > rect.Top)
                    free.Add(new Rect(rect.Left, rect.Top, rect.Width, used.Top - rect.Top));
                if (used.Bottom < rect.Bottom)
                    free.Add(new Rect(rect.Left, used.Bottom, rect.Width, rect.Bottom - used.Bottom));
            }
            for (var i = free.Count - 1; i >= 0; i--)
            {
                for (var j = 0; j < free.Count; j++)
                {
                    if (i == j || !free[j].Contains(free[i]))
                        continue;
                    free.RemoveAt(i);
                    break;
                }
            }
        }
        return placed;
    }
}
