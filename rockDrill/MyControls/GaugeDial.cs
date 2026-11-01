using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace rockDrill.MyControls;

/// <summary>
/// 圆弧仪表盘：270° 刻度弧 + 数值 + 单位 + 标题，超过阈值自动变琥珀/红色。
/// </summary>
public class GaugeDial : Control
{
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<GaugeDial, double>(nameof(Value));

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<GaugeDial, double>(nameof(Minimum));

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<GaugeDial, double>(nameof(Maximum), 100d);

    public static readonly StyledProperty<string> TitleProperty =
        AvaloniaProperty.Register<GaugeDial, string>(nameof(Title), string.Empty);

    public static readonly StyledProperty<string> UnitProperty =
        AvaloniaProperty.Register<GaugeDial, string>(nameof(Unit), string.Empty);

    public static readonly StyledProperty<IBrush> AccentBrushProperty =
        AvaloniaProperty.Register<GaugeDial, IBrush>(nameof(AccentBrush),
            new SolidColorBrush(Color.Parse("#23A9F0")));

    public static readonly StyledProperty<IBrush> TrackBrushProperty =
        AvaloniaProperty.Register<GaugeDial, IBrush>(nameof(TrackBrush),
            new SolidColorBrush(Color.Parse("#16242F")));

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<GaugeDial, double>(nameof(StrokeThickness), 16d);

    /// <summary>预警阈值（占量程比例 0~1）。</summary>
    public static readonly StyledProperty<double> WarnFractionProperty =
        AvaloniaProperty.Register<GaugeDial, double>(nameof(WarnFraction), 0.82);

    /// <summary>报警阈值（占量程比例 0~1）。</summary>
    public static readonly StyledProperty<double> DangerFractionProperty =
        AvaloniaProperty.Register<GaugeDial, double>(nameof(DangerFraction), 0.94);

    public static readonly StyledProperty<bool> ShowTicksProperty =
        AvaloniaProperty.Register<GaugeDial, bool>(nameof(ShowTicks), true);

    public static readonly StyledProperty<bool> CompactProperty =
        AvaloniaProperty.Register<GaugeDial, bool>(nameof(Compact));

    static GaugeDial()
    {
        AffectsRender<GaugeDial>(ValueProperty, MinimumProperty, MaximumProperty, TitleProperty,
            UnitProperty, AccentBrushProperty, TrackBrushProperty, StrokeThicknessProperty,
            WarnFractionProperty, DangerFractionProperty, ShowTicksProperty, CompactProperty);
    }

    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double Minimum { get => GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public string Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Unit { get => GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public IBrush AccentBrush { get => GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public IBrush TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public double StrokeThickness { get => GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }
    public double WarnFraction { get => GetValue(WarnFractionProperty); set => SetValue(WarnFractionProperty, value); }
    public double DangerFraction { get => GetValue(DangerFractionProperty); set => SetValue(DangerFractionProperty, value); }
    public bool ShowTicks { get => GetValue(ShowTicksProperty); set => SetValue(ShowTicksProperty, value); }
    public bool Compact { get => GetValue(CompactProperty); set => SetValue(CompactProperty, value); }

    private const double StartAngle = 135;
    private const double SweepAngle = 270;

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 30 || h <= 30) return;

        var compact = Compact;
        var thickness = compact ? Math.Min(StrokeThickness, 11) : StrokeThickness;
        var cx = w / 2;
        var cy = h / 2 + (compact ? 2 : 4);
        var radius = Math.Min(w, h) / 2 - thickness / 2 - (compact ? 4 : 8);

        var range = Maximum - Minimum;
        var pct = range <= 0 ? 0 : Math.Clamp((Value - Minimum) / range, 0, 1);

        // 背景弧
        DrawArc(context, cx, cy, radius, StartAngle, StartAngle + SweepAngle, thickness, TrackBrush);

        // 刻度
        if (ShowTicks)
        {
            var tickPen = new Pen(new SolidColorBrush(Color.Parse("#3A5568")), 1.2);
            var step = compact ? 2 : 1;
            var tickRadius = radius - thickness / 2 - 7;
            for (var i = 0; i <= 10; i++)
            {
                if (i % step != 0) continue;
                var a = StartAngle + SweepAngle * i / 10;
                var p1 = PointOn(cx, cy, tickRadius, a);
                var p2 = PointOn(cx, cy, tickRadius - (i % 5 == 0 ? 6 : 3.5), a);
                context.DrawLine(tickPen, p1, p2);
            }
        }

        // 数值弧
        if (pct > 0.004)
        {
            var brush = pct >= DangerFraction
                ? new SolidColorBrush(Color.Parse("#ED4C5C"))
                : pct >= WarnFraction
                    ? new SolidColorBrush(Color.Parse("#F5A623"))
                    : AccentBrush;

            DrawArc(context, cx, cy, radius, StartAngle, StartAngle + SweepAngle * pct, thickness, brush);
        }

        // 量程端点标注
        var dimBrush = new SolidColorBrush(Color.Parse("#5C7484"));
        var minLabel = Minimum.ToString("0.#", CultureInfo.InvariantCulture);
        var maxLabel = Maximum.ToString("0.#", CultureInfo.InvariantCulture);
        var small = compact ? 9.5 : 11.5;

        DrawText(context, minLabel, small, dimBrush,
            cx + (radius + thickness / 2 + 8) * Math.Cos(StartAngle * Math.PI / 180) - 11,
            cy + (radius + thickness / 2 + 8) * Math.Sin(StartAngle * Math.PI / 180) - 7, 22);

        DrawText(context, maxLabel, small, dimBrush,
            cx + (radius + thickness / 2 + 8) * Math.Cos((StartAngle + SweepAngle) * Math.PI / 180) - 11,
            cy + (radius + thickness / 2 + 8) * Math.Sin((StartAngle + SweepAngle) * Math.PI / 180) - 7, 22);

        // 数值 / 单位 / 标题
        var valueSize = compact ? 20 : 34;
        var unitSize = compact ? 10.5 : 13;
        var valueText = Value.ToString("0.0#", CultureInfo.InvariantCulture);

        DrawText(context, valueText, valueSize, new SolidColorBrush(Color.Parse("#E9F5FC")),
            0, cy - valueSize * 0.72, w, FontWeight.SemiBold);

        if (!string.IsNullOrEmpty(Unit))
        {
            DrawText(context, Unit, unitSize, new SolidColorBrush(Color.Parse("#90A9B9")),
                0, cy + valueSize * 0.52, w);
        }

        if (!string.IsNullOrEmpty(Title))
        {
            DrawText(context, Title, compact ? 11 : 13.5, new SolidColorBrush(Color.Parse("#90A9B9")),
                0, cy + radius * 0.62 + (compact ? 2 : 6), w, FontWeight.SemiBold);
        }
    }

    private static Point PointOn(double cx, double cy, double r, double angleDeg)
    {
        var a = angleDeg * Math.PI / 180;
        return new Point(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
    }

    private static void DrawArc(DrawingContext context, double cx, double cy, double r,
        double from, double to, double thickness, IBrush brush)
    {
        if (to <= from) return;

        var start = PointOn(cx, cy, r, from);
        var end = PointOn(cx, cy, r, to);

        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(start, false);
            ctx.ArcTo(end, new Size(r, r), 0, to - from > 180, SweepDirection.Clockwise);
        }

        var pen = new Pen(brush, thickness, lineCap: PenLineCap.Round);
        context.DrawGeometry(null, pen, geo);
    }

    private static void DrawText(DrawingContext context, string text, double size, IBrush brush,
        double x, double y, double maxWidth, FontWeight weight = default)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default, FontStyle.Normal, weight == default ? FontWeight.Normal : weight),
            size, brush)
        {
            MaxTextWidth = maxWidth,
            TextAlignment = TextAlignment.Center
        };
        context.DrawText(ft, new Point(x, y));
    }
}
