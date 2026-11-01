using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace rockDrill.MyControls;

/// <summary>
/// 激光雷达扫描预览：断面轮廓 + 点云 + 扫描进度着色。
/// </summary>
public class PointCloudCanvas : Control
{
    public static readonly StyledProperty<double> ProgressProperty =
        AvaloniaProperty.Register<PointCloudCanvas, double>(nameof(Progress));

    public static readonly StyledProperty<bool> ShowGuideProperty =
        AvaloniaProperty.Register<PointCloudCanvas, bool>(nameof(ShowGuide), true);

    private static readonly List<(double X, double Y, double Z)> Points = BuildPoints();

    static PointCloudCanvas()
    {
        AffectsRender<PointCloudCanvas>(ProgressProperty, ShowGuideProperty);
    }

    public double Progress { get => GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public bool ShowGuide { get => GetValue(ShowGuideProperty); set => SetValue(ShowGuideProperty, value); }

    private static List<(double, double, double)> BuildPoints()
    {
        var rnd = new Random(20261031);
        var list = new List<(double, double, double)>();

        // 掌子面点云：拱形轮廓内散布
        for (var i = 0; i < 620; i++)
        {
            var x = rnd.NextDouble() * 2 - 1;
            var y = rnd.NextDouble();

            // 拱顶收缩
            var limit = y > 0.72 ? 1 - Math.Pow((y - 0.72) / 0.28, 2) * 0.72 : 1;
            if (Math.Abs(x) > limit) continue;

            var z = 0.08 + rnd.NextDouble() * 0.22 + (1 - y) * 0.05;
            list.Add((x, y, z));
        }

        return list;
    }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 20 || h <= 20) return;

        var pad = 18.0;
        var faceW = w - pad * 2;
        var faceH = h - pad * 2;

        // 断面轮廓
        var outline = new StreamGeometry();
        using (var ctx = outline.Open())
        {
            var left = pad;
            var right = pad + faceW;
            var bottom = pad + faceH;
            var arcTop = pad + faceH * 0.30;
            ctx.BeginFigure(new Point(left, bottom), false);
            ctx.LineTo(new Point(left, arcTop));
            ctx.ArcTo(new Point(pad + faceW * 0.5, pad), new Size(faceW * 0.5, faceH * 0.30), 0, false, SweepDirection.Clockwise);
            ctx.ArcTo(new Point(right, arcTop), new Size(faceW * 0.5, faceH * 0.30), 0, false, SweepDirection.Clockwise);
            ctx.LineTo(new Point(right, bottom));
            ctx.EndFigure(false);
        }

        context.DrawGeometry(new SolidColorBrush(Color.Parse("#081016")),
            new Pen(new SolidColorBrush(Color.Parse("#2B4457")), 1.4), outline);

        if (ShowGuide)
        {
            var gridPen = new Pen(new SolidColorBrush(Color.Parse("#12212C")), 1);
            for (var i = 1; i < 6; i++)
            {
                var y = pad + faceH * i / 6;
                context.DrawLine(gridPen, new Point(pad, y), new Point(pad + faceW, y));
            }
            var centerPen = new Pen(new SolidColorBrush(Color.Parse("#1B3446")), 1)
            {
                DashStyle = new DashStyle(new double[] { 5, 5 }, 0)
            };
            context.DrawLine(centerPen, new Point(pad + faceW / 2, pad), new Point(pad + faceW / 2, pad + faceH));
        }

        var visible = (int)(Points.Count * Math.Clamp(Progress / 100.0, 0, 1));
        var brush = new SolidColorBrush(Color.Parse("#2EE6D6"), 0.85);
        var brushDone = new SolidColorBrush(Color.Parse("#23A9F0"), 0.75);

        for (var i = 0; i < visible && i < Points.Count; i++)
        {
            var p = Points[i];
            var px = pad + faceW / 2 + p.X * faceW * 0.46;
            var py = pad + faceH - p.Y * faceH * 0.92;
            var r = 1.2 + p.Z * 2.4;
            context.DrawEllipse(i % 3 == 0 ? brush : brushDone, null, new Point(px, py), r, r);
        }

        if (Progress > 0 && Progress < 100)
        {
            var y = pad + faceH * (1 - Progress / 100.0);
            var sweepPen = new Pen(new SolidColorBrush(Color.Parse("#8023A9F0")), 1.4);
            context.DrawLine(sweepPen, new Point(pad, y), new Point(pad + faceW, y));
        }
    }
}
