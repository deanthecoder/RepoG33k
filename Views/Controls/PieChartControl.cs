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
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ReviewG33k.ViewModels;

namespace ReviewG33k.Views.Controls;

/// <summary>
/// Draws a stylized pie chart for the current review finding categories.
/// </summary>
/// <remarks>
/// Useful for giving the review-results workflow a compact visual breakdown of issue distribution while
/// staying bound to the same category filter state that drives the main results list.
/// </remarks>
public class PieChartControl : Control
{
    public static readonly StyledProperty<IEnumerable<ReviewCategoryFilterItemViewModel>> SegmentsProperty =
        AvaloniaProperty.Register<PieChartControl, IEnumerable<ReviewCategoryFilterItemViewModel>>(nameof(Segments));

    private INotifyCollectionChanged m_segmentCollection;

    static PieChartControl()
    {
        SegmentsProperty.Changed.AddClassHandler<PieChartControl>((control, _) => control.HookSegments());
    }

    public IEnumerable<ReviewCategoryFilterItemViewModel> Segments
    {
        get => GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        var segments = (Segments ?? [])
            .Where(segment => segment != null && segment.Count > 0)
            .ToArray();
        var total = segments.Sum(segment => segment.Count);
        if (total <= 0)
            return;

        var bounds = Bounds.Deflate(16);
        var radius = Math.Max(0, Math.Min(bounds.Width, bounds.Height) / 2.0 - 4);
        if (radius <= 0)
            return;

        var center = bounds.Center;
        var shadowBrush = new SolidColorBrush(Color.Parse("#2A000000"));
        var outlinePen = new Pen(new SolidColorBrush(Color.Parse("#12263C")), 2);
        var startAngle = -90.0;

        context.DrawEllipse(
            shadowBrush,
            null,
            new Point(center.X + 8, center.Y + 12),
            radius,
            radius);

        foreach (var segment in segments)
        {
            var sweepAngle = segment.Count / (double)total * 360.0;
            using (context.PushOpacity(segment.IsVisible ? 1.0 : 0.28))
            {
                if (sweepAngle >= 359.99)
                {
                    context.DrawEllipse(segment.ColorBrush, outlinePen, center, radius, radius);
                }
                else
                {
                    var geometry = BuildSliceGeometry(center, radius, startAngle, sweepAngle);
                    context.DrawGeometry(segment.ColorBrush, outlinePen, geometry);
                }
            }

            startAngle += sweepAngle;
        }

        var innerBrush = new SolidColorBrush(Color.Parse("#0C1422"));
        var innerPen = new Pen(new SolidColorBrush(Color.Parse("#21405F")), 1.5);
        context.DrawEllipse(innerBrush, innerPen, center, radius * 0.42, radius * 0.42);
    }

    private void HookSegments()
    {
        if (m_segmentCollection != null)
            m_segmentCollection.CollectionChanged -= SegmentCollection_OnCollectionChanged;

        foreach (var segment in Segments ?? [])
            segment.PropertyChanged -= Segment_OnPropertyChanged;

        m_segmentCollection = Segments as INotifyCollectionChanged;
        if (m_segmentCollection != null)
            m_segmentCollection.CollectionChanged += SegmentCollection_OnCollectionChanged;

        foreach (var segment in Segments ?? [])
            segment.PropertyChanged += Segment_OnPropertyChanged;

        InvalidateVisual();
    }

    private void SegmentCollection_OnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var oldItem in e.OldItems.OfType<ReviewCategoryFilterItemViewModel>())
                oldItem.PropertyChanged -= Segment_OnPropertyChanged;
        }

        if (e.NewItems != null)
        {
            foreach (var newItem in e.NewItems.OfType<ReviewCategoryFilterItemViewModel>())
                newItem.PropertyChanged += Segment_OnPropertyChanged;
        }

        InvalidateVisual();
    }

    private void Segment_OnPropertyChanged(object sender, PropertyChangedEventArgs e) => InvalidateVisual();

    private static Geometry BuildSliceGeometry(Point center, double radius, double startAngle, double sweepAngle)
    {
        var geometry = new StreamGeometry();
        using (var geometryContext = geometry.Open())
        {
            var startPoint = PointOnCircle(center, radius, startAngle);
            var endPoint = PointOnCircle(center, radius, startAngle + sweepAngle);

            geometryContext.BeginFigure(center, true);
            geometryContext.LineTo(startPoint);
            geometryContext.ArcTo(
                endPoint,
                new Size(radius, radius),
                0,
                sweepAngle > 180,
                SweepDirection.Clockwise);
            geometryContext.EndFigure(true);
        }

        return geometry;
    }

    private static Point PointOnCircle(Point center, double radius, double angleInDegrees)
    {
        var angleInRadians = angleInDegrees * Math.PI / 180.0;
        return new Point(
            center.X + Math.Cos(angleInRadians) * radius,
            center.Y + Math.Sin(angleInRadians) * radius);
    }
}
