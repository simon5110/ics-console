using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using System;
using System.Globalization;

namespace ava.MyControls;

public class CircleProgress : Control
{
    #region AvaloniaProperty
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<CircleProgress, double>(nameof(Value), 0);

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<CircleProgress, double>(nameof(Minimum), 0);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<CircleProgress, double>(nameof(Maximum), 100);

    public static readonly StyledProperty<double> StrokeThicknessProperty =
        AvaloniaProperty.Register<CircleProgress, double>(nameof(StrokeThickness), 8);

    public static readonly StyledProperty<bool> ShowProgressTextProperty =
        AvaloniaProperty.Register<CircleProgress, bool>(nameof(ShowProgressText), true);

    public static readonly StyledProperty<IBrush> ForegroundProperty =
        AvaloniaProperty.Register<CircleProgress, IBrush>(nameof(Foreground), Brushes.DodgerBlue);

    public static readonly StyledProperty<IBrush> BackgroundRingProperty =
        AvaloniaProperty.Register<CircleProgress, IBrush>(nameof(BackgroundRing), Brushes.LightGray);

    public static readonly StyledProperty<IBrush> TextColorProperty =
        AvaloniaProperty.Register<CircleProgress, IBrush>(nameof(TextColor), Brushes.Black);

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double StrokeThickness
    {
        get => GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public bool ShowProgressText
    {
        get => GetValue(ShowProgressTextProperty);
        set => SetValue(ShowProgressTextProperty, value);
    }

    public IBrush Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public IBrush BackgroundRing
    {
        get => GetValue(BackgroundRingProperty);
        set => SetValue(BackgroundRingProperty, value);
    }

    public IBrush TextColor
    {
        get => GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }
    #endregion

    static CircleProgress()
    {
        AffectsRender<CircleProgress>(
            ValueProperty, MinimumProperty, MaximumProperty, StrokeThicknessProperty,
            ShowProgressTextProperty, ForegroundProperty, BackgroundRingProperty, TextColorProperty);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var rect = new Rect(Bounds.Size);
        var center = rect.Center;
        double padding = StrokeThickness / 2;
        double radius = Math.Min(rect.Width, rect.Height) / 2 - padding;

        // 背景圆环
        var bgPen = new Pen(BackgroundRing, StrokeThickness);
        context.DrawEllipse(null, bgPen, center, radius, radius);

        double range = Maximum - Minimum;
        double percent = range <= 0 ? 0 : Math.Clamp((Value - Minimum) / range, 0, 1);

        if (percent > 0)
        {
            double startAngle = -Math.PI / 2; // 顶部起始
            double sweepAngle = percent * 2 * Math.PI;
            var pen = new Pen(Foreground, StrokeThickness);

            var geometry = new StreamGeometry();
            using var geoCtx = geometry.Open();
            geoCtx.BeginFigure(
                new Point(
                    center.X + radius * Math.Cos(startAngle),
                    center.Y + radius * Math.Sin(startAngle)),
                false);

            geoCtx.ArcTo(
                new Point(
                    center.X + radius * Math.Cos(startAngle + sweepAngle),
                    center.Y + radius * Math.Sin(startAngle + sweepAngle)),
                new Size(radius, radius),
                0,
                sweepAngle > Math.PI, // >180°自动大圆弧
                SweepDirection.Clockwise);

            context.DrawGeometry(null, pen, geometry);
        }

        // 绘制中间百分比文字【最终修复 Avalonia11 版本】
        if (ShowProgressText)
        {
            var textStr = $"{Value:F0}%";
            var formatted = new FormattedText(
                textStr,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                Typeface.Default,
                18,
                TextColor)
            {
                MaxTextWidth = Bounds.Width,
                TextAlignment = TextAlignment.Center
            };

            var textPos = new Point(0, center.Y - formatted.Height / 2);
            context.DrawText(formatted, textPos);
        }
    }
}