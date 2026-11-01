using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace rockDrill.MyControls;

/// <summary>
/// 工业仪表条：带圆角轨道、目标刻线，用于压力/转速/行程等模拟量显示。
/// </summary>
public class LevelBar : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<LevelBar, double>(nameof(Value));

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<LevelBar, double>(nameof(Minimum));

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<LevelBar, double>(nameof(Maximum), 100d);

    /// <summary>目标值刻线位置（按量程百分比 0~1），小于 0 表示不显示。</summary>
    public static readonly StyledProperty<double> TargetProperty =
        AvaloniaProperty.Register<LevelBar, double>(nameof(Target), -1d);

    public static readonly StyledProperty<IBrush> TrackBrushProperty =
        AvaloniaProperty.Register<LevelBar, IBrush>(nameof(TrackBrush),
            new SolidColorBrush(Color.Parse("#14222C")));

    public static readonly StyledProperty<IBrush> FillBrushProperty =
        AvaloniaProperty.Register<LevelBar, IBrush>(nameof(FillBrush),
            new SolidColorBrush(Color.Parse("#23A9F0")));

    public static readonly StyledProperty<IBrush> TargetBrushProperty =
        AvaloniaProperty.Register<LevelBar, IBrush>(nameof(TargetBrush),
            new SolidColorBrush(Color.Parse("#E9F5FC")));

    public static readonly StyledProperty<double> BarHeightProperty =
        AvaloniaProperty.Register<LevelBar, double>(nameof(BarHeight), 8d);

    static LevelBar()
    {
        AffectsRender<LevelBar>(ValueProperty, MinimumProperty, MaximumProperty, TargetProperty,
            TrackBrushProperty, FillBrushProperty, TargetBrushProperty, BarHeightProperty);
    }

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Target { get => GetValue(TargetProperty); set => SetValue(TargetProperty, value); }
    public IBrush TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush FillBrush { get => GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }
    public IBrush TargetBrush { get => GetValue(TargetBrushProperty); set => SetValue(TargetBrushProperty, value); }
    public double BarHeight { get => GetValue(BarHeightProperty); set => SetValue(BarHeightProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
        => new(double.IsInfinity(availableSize.Width) ? 120 : availableSize.Width, BarHeight);

    public override void Render(DrawingContext context)
    {
        var h = BarHeight;
        var w = Bounds.Width;
        if (w <= 0 || h <= 0) return;

        var radius = h / 2;
        var track = new Rect(0, 0, w, h);
        context.DrawRectangle(TrackBrush, null, new RoundedRect(track, radius));

        var range = Maximum - Minimum;
        var pct = range <= 0 ? 0 : Math.Clamp((Value - Minimum) / range, 0, 1);

        if (pct > 0)
        {
            var fillRect = new Rect(0, 0, Math.Max(h, w * pct), h);
            context.DrawRectangle(FillBrush, null, new RoundedRect(fillRect, radius));
        }

        if (Target >= 0 && Target <= 1)
        {
            var x = Math.Clamp(w * Target, 1, w - 1);
            var pen = new Pen(TargetBrush, 1.5);
            context.DrawLine(pen, new Point(x, -2), new Point(x, h + 2));
        }
    }
}
