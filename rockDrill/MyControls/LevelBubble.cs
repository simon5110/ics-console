using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace rockDrill.MyControls;

/// <summary>
/// 设备调平指示器：同心圆 + 十字/斜向基准线 + 倾角气泡，对应现场「设备调平系统」界面。
/// </summary>
public class LevelBubble : Control
{
    public static readonly StyledProperty<double> TiltXProperty =
        AvaloniaProperty.Register<LevelBubble, double>(nameof(TiltX));

    public static readonly StyledProperty<double> TiltYProperty =
        AvaloniaProperty.Register<LevelBubble, double>(nameof(TiltY));

    public static readonly StyledProperty<double> MaxTiltProperty =
        AvaloniaProperty.Register<LevelBubble, double>(nameof(MaxTilt), 8d);

    /// <summary>允许偏差（度），用于判定气泡是否居中。</summary>
    public static readonly StyledProperty<double> ToleranceProperty =
        AvaloniaProperty.Register<LevelBubble, double>(nameof(Tolerance), 0.2d);

    public static readonly StyledProperty<IBrush> RingBrushProperty =
        AvaloniaProperty.Register<LevelBubble, IBrush>(nameof(RingBrush),
            new SolidColorBrush(Color.Parse("#3A5568")));

    public static readonly StyledProperty<IBrush> AxisBrushProperty =
        AvaloniaProperty.Register<LevelBubble, IBrush>(nameof(AxisBrush),
            new SolidColorBrush(Color.Parse("#5A7688")));

    static LevelBubble()
    {
        AffectsRender<LevelBubble>(TiltXProperty, TiltYProperty, MaxTiltProperty,
            ToleranceProperty, RingBrushProperty, AxisBrushProperty);
    }

    public double TiltX { get => GetValue(TiltXProperty); set => SetValue(TiltXProperty, value); }
    public double TiltY { get => GetValue(TiltYProperty); set => SetValue(TiltYProperty, value); }
    public double MaxTilt { get => GetValue(MaxTiltProperty); set => SetValue(MaxTiltProperty, value); }
    public double Tolerance { get => GetValue(ToleranceProperty); set => SetValue(ToleranceProperty, value); }
    public IBrush RingBrush { get => GetValue(RingBrushProperty); set => SetValue(RingBrushProperty, value); }
    public IBrush AxisBrush { get => GetValue(AxisBrushProperty); set => SetValue(AxisBrushProperty, value); }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 20 || h <= 20) return;

        var center = new Point(w / 2, h / 2);
        var radius = Math.Min(w, h) / 2 - 34;

        var ringPen = new Pen(RingBrush, 1);
        var glowPen = new Pen(new SolidColorBrush(Color.Parse("#3323A9F0")), 1.4);

        // 同心圆
        for (var i = 1; i <= 6; i++)
        {
            var r = radius * i / 6;
            context.DrawEllipse(null, i % 3 == 0 ? glowPen : ringPen, center, r, r);
        }

        // 十字与对角线
        var axisPen = new Pen(AxisBrush, 1);
        context.DrawLine(axisPen, new Point(center.X - radius, center.Y), new Point(center.X + radius, center.Y));
        context.DrawLine(axisPen, new Point(center.X, center.Y - radius), new Point(center.X, center.Y + radius));

        var diag = radius * Math.Sqrt(2) / 2;
        context.DrawLine(ringPen, new Point(center.X - diag, center.Y - diag), new Point(center.X + diag, center.Y + diag));
        context.DrawLine(ringPen, new Point(center.X - diag, center.Y + diag), new Point(center.X + diag, center.Y - diag));

        // 允许范围圈
        var tolPen = new Pen(new SolidColorBrush(Color.Parse("#402AD07C")), 1)
        {
            DashStyle = new DashStyle(new double[] { 4, 4 }, 0)
        };
        var tolRadius = radius * Math.Clamp(Tolerance / Math.Max(MaxTilt, 0.001), 0.05, 1);
        context.DrawEllipse(null, tolPen, center, tolRadius, tolRadius);

        // 角度标注
        var labelBrush = new SolidColorBrush(Color.Parse("#90A9B9"));
        DrawLabel(context, $"-{MaxTilt:0}°", new Point(center.X - radius - 30, center.Y - 9), labelBrush);
        DrawLabel(context, $"{MaxTilt:0}°", new Point(center.X + radius + 8, center.Y - 9), labelBrush);

        // 气泡
        var nx = Math.Clamp(TiltX / MaxTilt, -1, 1);
        var ny = Math.Clamp(TiltY / MaxTilt, -1, 1);
        var dot = new Point(center.X + nx * radius * 0.94, center.Y - ny * radius * 0.94);

        var level = Math.Abs(TiltX) <= Tolerance && Math.Abs(TiltY) <= Tolerance;
        var dotBrush = level
            ? new SolidColorBrush(Color.Parse("#8CFF3D"))
            : new SolidColorBrush(Color.Parse("#F5A623"));

        context.DrawEllipse(new SolidColorBrush(Color.FromArgb(60, 140, 255, 61)), null, dot, 14, 14);
        context.DrawEllipse(dotBrush, new Pen(new SolidColorBrush(Color.Parse("#DFFFB0")), 1.2), dot, 6, 6);

        // 中心点
        context.DrawEllipse(new SolidColorBrush(Color.Parse("#2FE04B")), null, center, 2.4, 2.4);
    }

    private static void DrawLabel(DrawingContext context, string text, Point origin, IBrush brush)
    {
        var ft = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            Typeface.Default, 13, brush);
        context.DrawText(ft, origin);
    }
}
