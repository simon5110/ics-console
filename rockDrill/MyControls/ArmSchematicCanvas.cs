using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using rockDrill.Models;
using rockDrill.ViewModels;

namespace rockDrill.MyControls;

/// <summary>
/// 定位页的臂架示意图（对齐现场 HMI 的「臂架定位」画面）：
/// 以等轴测方式画出断面沿进深方向的箱体、掌子面黄色作业带，
/// 以及左右臂的基座 → 大臂 → 推进梁 → 钻头折线，选中臂高亮。
/// </summary>
public class ArmSchematicCanvas : Control
{
    public static readonly StyledProperty<TunnelSection?> SectionProperty =
        AvaloniaProperty.Register<ArmSchematicCanvas, TunnelSection?>(nameof(Section));

    public static readonly StyledProperty<double> DepthProperty =
        AvaloniaProperty.Register<ArmSchematicCanvas, double>(nameof(Depth), 4.5);

    public static readonly StyledProperty<IEnumerable<ArmSchematicVm>?> ArmsProperty =
        AvaloniaProperty.Register<ArmSchematicCanvas, IEnumerable<ArmSchematicVm>?>(nameof(Arms));

    public static readonly StyledProperty<int> RevisionProperty =
        AvaloniaProperty.Register<ArmSchematicCanvas, int>(nameof(Revision));

    private static readonly IBrush BackdropBrush = new SolidColorBrush(Color.Parse("#060C11"));
    private static readonly IBrush GridBrush = new SolidColorBrush(Color.Parse("#0F1D27"));
    private static readonly IBrush OutlineBrush = new SolidColorBrush(Color.Parse("#3E6B86"));
    private static readonly IBrush DimOutlineBrush = new SolidColorBrush(Color.Parse("#254A5F"));
    private static readonly IBrush FaceFillBrush = new SolidColorBrush(Color.Parse("#0B1620"));
    private static readonly IBrush BandFillBrush = new SolidColorBrush(Color.Parse("#662F3A1B"));
    private static readonly IBrush BandStrokeBrush = new SolidColorBrush(Color.Parse("#D8C24A"));
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#8FA6B8"));

    static ArmSchematicCanvas()
    {
        AffectsRender<ArmSchematicCanvas>(SectionProperty, DepthProperty, ArmsProperty, RevisionProperty);
    }

    public TunnelSection? Section { get => GetValue(SectionProperty); set => SetValue(SectionProperty, value); }
    public double Depth { get => GetValue(DepthProperty); set => SetValue(DepthProperty, value); }
    public IEnumerable<ArmSchematicVm>? Arms { get => GetValue(ArmsProperty); set => SetValue(ArmsProperty, value); }
    public int Revision { get => GetValue(RevisionProperty); set => SetValue(RevisionProperty, value); }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(BackdropBrush, new Rect(Bounds.Size));

        if (Bounds.Width < 60 || Bounds.Height < 60) return;
        if (Section is not { } section) return;

        var depth = Math.Clamp(Depth, 1.0, 12.0);
        var layout = GetLayout(section, depth);
        if (layout is null) return;

        var (scale, ox, oy) = layout.Value;

        Point P(double x, double z, double y)
        {
            var p = Project(x, z, y);
            return new Point(ox + p.X * scale, oy - p.Y * scale);
        }

        DrawGrid(context, scale, ox, oy);
        DrawTunnel(context, section, depth, P);
        DrawArms(context, P, scale);

        var farLabel = Format("掌子面", 11, TextBrush);
        context.DrawText(farLabel, new Point(Bounds.Width - farLabel.Width - 12, 8));

        var nearLabel = Format("台车", 11, TextBrush);
        context.DrawText(nearLabel, new Point(12, Bounds.Height - nearLabel.Height - 8));
    }

    /// <summary>等轴测投影：进深越大（越靠近台车）越往左下展开。</summary>
    private static Point Project(double x, double z, double y) => new(x + y * 0.55, z + y * 0.34);

    private (double Scale, double OffsetX, double OffsetY)? GetLayout(TunnelSection section, double depth)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;

        var minX = double.MaxValue;
        var maxX = double.MinValue;
        var minY = double.MaxValue;
        var maxY = double.MinValue;

        void Include(Point p)
        {
            if (!double.IsFinite(p.X) || !double.IsFinite(p.Y)) return;
            if (p.X < minX) minX = p.X;
            if (p.X > maxX) maxX = p.X;
            if (p.Y < minY) minY = p.Y;
            if (p.Y > maxY) maxY = p.Y;
        }

        foreach (var p in section.ToPolyline())
        {
            Include(Project(p.X, p.Y, 0));
            Include(Project(p.X, p.Y, -depth));
        }

        if (Arms is not null)
        {
            foreach (var arm in Arms)
            {
                Include(Project(arm.BaseX, arm.BaseZ, -depth));
                Include(Project(arm.ElbowX, arm.ElbowZ, arm.ElbowY));
                Include(Project(arm.TipX, arm.TipZ, arm.TipY));
            }
        }

        if (minX > maxX) return null;

        const double pad = 0.5;
        minX -= pad; maxX += pad; minY -= pad; maxY += pad;

        var scale = Math.Min((w - 24) / Math.Max(0.5, maxX - minX), (h - 24) / Math.Max(0.5, maxY - minY));
        if (scale <= 0 || !double.IsFinite(scale)) return null;

        return (scale, w / 2 - (minX + maxX) / 2 * scale, h / 2 + (minY + maxY) / 2 * scale);
    }

    private void DrawGrid(DrawingContext context, double scale, double ox, double oy)
    {
        var step = 1.0;
        while (step * scale < 16) step *= 2;
        var stepPx = step * scale;
        var pen = new Pen(GridBrush, 1);

        var startX = ox - Math.Ceiling(ox / stepPx) * stepPx;
        if (startX < 0) startX += stepPx;
        for (var x = startX; x < Bounds.Width; x += stepPx)
            context.DrawLine(pen, new Point(x, 0), new Point(x, Bounds.Height));

        var startY = oy - Math.Floor(oy / stepPx) * stepPx;
        if (startY > Bounds.Height) startY -= stepPx;
        for (var y = startY; y < Bounds.Height; y += stepPx)
            context.DrawLine(pen, new Point(0, y), new Point(Bounds.Width, y));
    }

    private void DrawTunnel(DrawingContext context, TunnelSection section, double depth,
        Func<double, double, double, Point> P)
    {
        var polyline = section.ToPolyline();

        var near = polyline.Select(p => P(p.X, p.Y, -depth)).ToList();
        var far = polyline.Select(p => P(p.X, p.Y, 0)).ToList();

        var solid = new Pen(OutlineBrush, 1.4);
        var dashed = new Pen(DimOutlineBrush, 1) { DashStyle = new DashStyle(new double[] { 5, 5 }, 0) };

        // 台车端（近端）断面：实线 + 填充
        DrawPolygon(context, near, solid, FaceFillBrush);

        // 掌子面（远端）断面：虚线
        DrawPolygon(context, far, dashed, null);

        // 四条棱线
        foreach (var idx in new[] { 0, polyline.Count / 4, polyline.Count / 2, polyline.Count * 3 / 4 })
            context.DrawLine(dashed, near[idx], far[idx]);

        // 掌子面黄色作业带
        var z1 = section.MinY + section.Height * 0.48;
        var z2 = section.MinY + section.Height * 0.70;
        var band = new List<Point>
        {
            P(section.MinX, z1, 0),
            P(section.MaxX, z1, 0),
            P(section.MaxX, z2, 0),
            P(section.MinX, z2, 0)
        };
        DrawPolygon(context, band, new Pen(BandStrokeBrush, 1.2), BandFillBrush);
    }

    private void DrawArms(DrawingContext context, Func<double, double, double, Point> P, double scale)
    {
        var arms = Arms;
        if (arms is null) return;

        foreach (var arm in arms)
        {
            var basePt = P(arm.BaseX, arm.BaseZ, -Math.Clamp(Depth, 1.0, 12.0));
            var tip = P(arm.TipX, arm.TipZ, arm.TipY);

            // 二维折臂：基座 → 大臂 → 推进梁 → 钻头（中点法向偏移，保证弯曲方向朝外）
            var dx = tip.X - basePt.X;
            var dy = tip.Y - basePt.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 1) len = 1;

            var mid = new Point((basePt.X + tip.X) / 2, (basePt.Y + tip.Y) / 2);
            var nx = -dy / len;
            var ny = dx / len;
            var bend = len * 0.22;

            var elbow = new Point(mid.X + nx * bend, mid.Y + ny * bend);
            if ((elbow.X - mid.X) * arm.BaseX < 0)
                elbow = new Point(mid.X - nx * bend, mid.Y - ny * bend);

            var boomPen = new Pen(arm.Brush, arm.IsSelected ? 7 : 5, lineCap: PenLineCap.Round);
            var beamPen = new Pen(arm.BeamBrush, arm.IsSelected ? 4.5 : 3.2, lineCap: PenLineCap.Round);

            if (arm.IsSelected)
            {
                context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#332AD07C")), 15,
                    lineCap: PenLineCap.Round), basePt, elbow);
            }

            context.DrawLine(boomPen, basePt, elbow);
            context.DrawLine(beamPen, elbow, tip);

            // 基座
            context.DrawEllipse(arm.Brush, new Pen(BackdropBrush, 1.5), basePt,
                Math.Clamp(scale * 0.16, 4, 9), Math.Clamp(scale * 0.16, 4, 9));

            // 肘关节
            context.DrawEllipse(BackdropBrush, new Pen(arm.Brush, 2), elbow,
                Math.Clamp(scale * 0.1, 2.5, 6), Math.Clamp(scale * 0.1, 2.5, 6));

            // 钻头
            context.DrawEllipse(arm.BeamBrush, null, tip, Math.Clamp(scale * 0.09, 2.5, 6),
                Math.Clamp(scale * 0.09, 2.5, 6));

            var label = Format(arm.Name, 11.5, arm.Brush);
            context.DrawText(label, new Point(basePt.X + 10, basePt.Y + 4));
        }
    }

    private static void DrawPolygon(DrawingContext context, IReadOnlyList<Point> points, Pen pen, IBrush? fill)
    {
        if (points.Count < 2) return;

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(points[0], true);
            for (var i = 1; i < points.Count; i++)
                ctx.LineTo(points[i]);
            ctx.EndFigure(true);
        }

        context.DrawGeometry(fill, pen, geometry);
    }

    private static FormattedText Format(string text, double size, IBrush brush)
        => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
               new Typeface("Microsoft YaHei, sans-serif"), size, brush);
}
