using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace NIRAAgent.UI.Gallery;

/// <summary>
/// A responsive masonry panel for the media library. Children are measured at
/// the *current viewport's* column width with unbounded height, so an image's
/// own aspect ratio, not a fixed tile height, determines its dimensions.
/// Tiles are placed in the shortest column, avoiding WrapPanel row gaps.
/// All layout is local WPF work; no image queries or model calls are made.
/// </summary>
public sealed class NIRAMasonryPanel : Panel
{
    public static readonly DependencyProperty PreferredColumnWidthProperty =
        DependencyProperty.Register(nameof(PreferredColumnWidth), typeof(double),
            typeof(NIRAMasonryPanel), new FrameworkPropertyMetadata(235.0,
                FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ColumnSpacingProperty =
        DependencyProperty.Register(nameof(ColumnSpacing), typeof(double),
            typeof(NIRAMasonryPanel), new FrameworkPropertyMetadata(14.0,
                FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty RowSpacingProperty =
        DependencyProperty.Register(nameof(RowSpacing), typeof(double),
            typeof(NIRAMasonryPanel), new FrameworkPropertyMetadata(15.0,
                FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double PreferredColumnWidth
    {
        get => (double)GetValue(PreferredColumnWidthProperty);
        set => SetValue(PreferredColumnWidthProperty, value);
    }

    public double ColumnSpacing
    {
        get => (double)GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    public double RowSpacing
    {
        get => (double)GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    private readonly List<Rect> _placements = new();
    private double _measuredWidth;

    protected override Size MeasureOverride(Size availableSize)
    {
        double width = double.IsFinite(availableSize.Width)
            ? Math.Max(0, availableSize.Width)
            : Math.Max(300, ActualWidth);
        _measuredWidth = width;
        _placements.Clear();

        int count = InternalChildren.Count;
        if (width <= 0 || count == 0)
            return new Size(width, 0);

        double gap = Math.Max(0, ColumnSpacing);
        double rowGap = Math.Max(0, RowSpacing);
        double ideal = Math.Max(160, PreferredColumnWidth);
        int fittingColumns = Math.Max(1, (int)Math.Floor((width + gap) / (ideal + gap)));
        int columns = Math.Min(count, fittingColumns);
        // One picture should be comfortably sized, not enlarged to fill an
        // entire ultrawide workspace. The bound is viewport-relative, not fixed.
        double columnWidth = columns == 1 && width > ideal * 2.5
            ? width * 0.66
            : (width - (columns - 1) * gap) / columns;
        columnWidth = Math.Max(1, columnWidth);

        double[] bottoms = new double[columns];
        for (int i = 0; i < count; i++)
        {
            UIElement child = InternalChildren[i];
            child.Measure(new Size(columnWidth, double.PositiveInfinity));
            int shortest = 0;
            for (int j = 1; j < bottoms.Length; j++)
                if (bottoms[j] < bottoms[shortest]) shortest = j;

            double tileHeight = Math.Max(0, child.DesiredSize.Height);
            double left = shortest * (columnWidth + gap);
            double top = bottoms[shortest];
            _placements.Add(new Rect(left, top, columnWidth, tileHeight));
            bottoms[shortest] += tileHeight + rowGap;
        }

        double totalHeight = Math.Max(0, Max(bottoms) - rowGap);
        return new Size(width, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Math.Abs(finalSize.Width - _measuredWidth) > 1)
            InvalidateMeasure(); // Reflow on viewport/window size changes.

        for (int i = 0; i < InternalChildren.Count && i < _placements.Count; i++)
            InternalChildren[i].Arrange(_placements[i]);
        return finalSize;
    }

    private static double Max(double[] values)
    {
        double max = 0;
        foreach (double value in values) max = Math.Max(max, value);
        return max;
    }
}
