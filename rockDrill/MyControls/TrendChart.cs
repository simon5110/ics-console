using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace rockDrill.MyControls;

/// <summary>趋势图中的一条通道。</summary>
public sealed class TrendSeries
{
    public string Name { get; init; } = string.Empty;
    public IBrush Stroke { get; init; } = Brushes.DeepSkyBlue;
    public IList<double> Values { get; init; } = Array.Empty<double>();
}

/// <summary>
/// 多通道实时趋势图（凿岩压力/流量等），无引线、带网格与末端高亮。
/// </summary>
public class TrendChart : Control
{
    public static readonly StyledProperty<IEnumerable<TrendSeries>?> SeriesProperty =
        AvaloniaProperty.Register<TrendChart, IEnumerable<TrendSeries>?>(nameof(Series));

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<TrendChart, double>(nameof(Minimum));

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<TrendChart, double>(nameof(Maximum), 30d);

    /// <summary>由 ViewModel 递增，用于强制刷新。</summary>
    public static readonly StyledProperty<int> TickProperty =
        AvaloniaProperty.Register<TrendChart, int>(nameof(Tick));

    public static readonly StyledProperty<IBrush> GridBrushProperty =
        AvaloniaProperty.Register<TrendChart, IBrush>(nameof(GridBrush),
            new SolidColorBrush(Color.Parse("#16242F")));

    public static readonly StyledProperty<bool> ShowAreaProperty =
        AvaloniaProperty.Register<TrendChart, bool>(nameof(ShowArea), true);

    static TrendChart()
    {
        AffectsRender<TrendChart>(SeriesProperty, MinimumProperty, MaximumProperty, TickProperty,
            GridBrushProperty, ShowAreaProperty);
    }

    public IEnumerable<TrendSeries>? Series { get => GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public int Tick { get => GetValue(TickProperty); set => SetValue(TickProperty, value); }
    public IBrush GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }
    public bool ShowArea { get => GetValue(ShowAreaProperty); set => SetValue(ShowAreaProperty, value); }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 2 || h <= 2) return;

        var gridPen = new Pen(GridBrush, 1);

        // 网格
        const int rows = 4;
        for (var i = 1; i < rows; i++)
        {
            var y = h * i / rows;
            context.DrawLine(gridPen, new Point(0, y), new Point(w, y));
        }
        const int cols = 8;
        for (var i = 1; i < cols; i++)
        {
            var x = w * i / cols;
            context.DrawLine(gridPen, new Point(x, 0), new Point(x, h));
        }

        var series = Series;
        if (series is null) return;

        var range = Maximum - Minimum;
        if (range <= 0) range = 1;

        foreach (var s in series)
        {
            var values = s.Values;
            var n = values.Count;
            if (n < 2) continue;

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                var firstY = h - (Math.Clamp(values[0], Minimum, Maximum) - Minimum) / range * h;
                ctx.BeginFigure(new Point(0, firstY), false);
                for (var i = 1; i < n; i++)
                {
                    var x = w * i / (n - 1);
                    var y = h - (Math.Clamp(values[i], Minimum, Maximum) - Minimum) / range * h;
                    ctx.LineTo(new Point(x, y));
                }
            }

            context.DrawGeometry(null, new Pen(s.Stroke, 2), geo);

            // 曲线下方淡色填充
            if (ShowArea)
            {
                var area = new StreamGeometry();
                using (var ctx = area.Open())
                {
                    var firstY = h - (Math.Clamp(values[0], Minimum, Maximum) - Minimum) / range * h;
                    ctx.BeginFigure(new Point(0, h), true);
                    ctx.LineTo(new Point(0, firstY));
                    for (var i = 1; i < n; i++)
                    {
                        var x = w * i / (n - 1);
                        var y = h - (Math.Clamp(values[i], Minimum, Maximum) - Minimum) / range * h;
                        ctx.LineTo(new Point(x, y));
                    }
                    ctx.LineTo(new Point(w, h));
                    ctx.EndFigure(true);
                }

                var fill = new LinearGradientBrush
                {
                    StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                    EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                };
                if (s.Stroke is ISolidColorBrush scb)
                {
                    fill.GradientStops.Add(new GradientStop(Color.FromArgb(56, scb.Color.R, scb.Color.G, scb.Color.B), 0));
                    fill.GradientStops.Add(new GradientStop(Color.FromArgb(0, scb.Color.R, scb.Color.G, scb.Color.B), 1));
                }
                context.DrawGeometry(fill, null, area);
            }

            // 末端高亮
            var lastX = w;
            var lastY = h - (Math.Clamp(values[n - 1], Minimum, Maximum) - Minimum) / range * h;
            context.DrawEllipse(s.Stroke, null, new Point(lastX, lastY), 3, 3);
            context.DrawEllipse(new SolidColorBrush(Colors.White, 0.85), null, new Point(lastX, lastY), 1.4, 1.4);
        }
    }
}
