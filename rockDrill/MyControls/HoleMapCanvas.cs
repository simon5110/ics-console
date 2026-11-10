using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Media;
using rockDrill.Models;
using rockDrill.ViewModels;

namespace rockDrill.MyControls;

/// <summary>
/// 掌子面炮孔布置图。
/// 断面轮廓来自 <see cref="TunnelSection"/>（内置半圆拱或 CSV 轮廓线），
/// 轮廓与炮孔的绘制方式参考 CollectData 的 <c>FormCollect</c>：轮廓逐段绘制、
/// 炮孔按孔口/孔底两点绘制连线与孔圈、世界坐标经统一缩放后映射到屏幕坐标。
/// 另外绘制钻孔路径（左右臂）、臂架基座与选中孔的臂架连线。
/// </summary>
public class HoleMapCanvas : Control
{
    public static readonly StyledProperty<TunnelSection?> SectionProperty =
        AvaloniaProperty.Register<HoleMapCanvas, TunnelSection?>(nameof(Section));

    public static readonly StyledProperty<IEnumerable<HoleVm>?> HolesProperty =
        AvaloniaProperty.Register<HoleMapCanvas, IEnumerable<HoleVm>?>(nameof(Holes));

    public static readonly StyledProperty<HoleVm?> SelectedHoleProperty =
        AvaloniaProperty.Register<HoleMapCanvas, HoleVm?>(nameof(SelectedHole), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<IEnumerable<HolePathVm>?> PathsProperty =
        AvaloniaProperty.Register<HoleMapCanvas, IEnumerable<HolePathVm>?>(nameof(Paths));

    public static readonly StyledProperty<IEnumerable<BoomBaseVm>?> BoomsProperty =
        AvaloniaProperty.Register<HoleMapCanvas, IEnumerable<BoomBaseVm>?>(nameof(Booms));

    public static readonly StyledProperty<int> RevisionProperty =
        AvaloniaProperty.Register<HoleMapCanvas, int>(nameof(Revision));

    public static readonly StyledProperty<IBrush> OutlineBrushProperty =
        AvaloniaProperty.Register<HoleMapCanvas, IBrush>(nameof(OutlineBrush),
            new SolidColorBrush(Color.Parse("#2E4A60")));

    public static readonly StyledProperty<IBrush> GuideBrushProperty =
        AvaloniaProperty.Register<HoleMapCanvas, IBrush>(nameof(GuideBrush),
            new SolidColorBrush(Color.Parse("#1B2C38")));

    private static readonly IBrush DimBrush = new SolidColorBrush(Color.Parse("#5C7484"));
    private static readonly IBrush HoleTextBrush = new SolidColorBrush(Color.Parse("#D6E6F1"));
    private static readonly IBrush FaceFillBrush = new SolidColorBrush(Color.Parse("#0A1319"));
    private static readonly IBrush FaceEdgeBrush = new SolidColorBrush(Color.Parse("#0E1A22"));
    private static readonly IBrush AccentBrush = new SolidColorBrush(Color.Parse("#23A9F0"));
    private static readonly IBrush DoneHaloBrush = new SolidColorBrush(Color.Parse("#122AD07C"));
    private static readonly IBrush ToeBrush = new SolidColorBrush(Color.Parse("#6F93AB"));
    private static readonly IBrush AlarmBrush = new SolidColorBrush(Color.Parse("#ED4C5C"));

    static HoleMapCanvas()
    {
        AffectsRender<HoleMapCanvas>(SectionProperty, HolesProperty, SelectedHoleProperty,
            PathsProperty, BoomsProperty, RevisionProperty, OutlineBrushProperty, GuideBrushProperty);
    }

    public TunnelSection? Section { get => GetValue(SectionProperty); set => SetValue(SectionProperty, value); }
    public IEnumerable<HoleVm>? Holes { get => GetValue(HolesProperty); set => SetValue(HolesProperty, value); }
    public HoleVm? SelectedHole { get => GetValue(SelectedHoleProperty); set => SetValue(SelectedHoleProperty, value); }
    public IEnumerable<HolePathVm>? Paths { get => GetValue(PathsProperty); set => SetValue(PathsProperty, value); }
    public IEnumerable<BoomBaseVm>? Booms { get => GetValue(BoomsProperty); set => SetValue(BoomsProperty, value); }

    /// <summary>解算/规划版本号（变化时强制重绘）。</summary>
    public int Revision { get => GetValue(RevisionProperty); set => SetValue(RevisionProperty, value); }

    public IBrush OutlineBrush { get => GetValue(OutlineBrushProperty); set => SetValue(OutlineBrushProperty, value); }
    public IBrush GuideBrush { get => GetValue(GuideBrushProperty); set => SetValue(GuideBrushProperty, value); }

    public override void Render(DrawingContext context)
    {
        var section = Section;
        var layout = GetLayout(section);
        if (section is null || layout is null) return;

        var (scale, offsetX, offsetY) = layout.Value;

        double ToScreenX(double x) => offsetX + x * scale;
        double ToScreenY(double y) => offsetY - y * scale;

        // ===== 断面轮廓 =====
        var polyline = section.ToPolyline();
        var outline = new StreamGeometry();
        using (var ctx = outline.Open())
        {
            ctx.BeginFigure(new Point(ToScreenX(polyline[0].X), ToScreenY(polyline[0].Y)), true);
            for (var i = 1; i < polyline.Count; i++)
                ctx.LineTo(new Point(ToScreenX(polyline[i].X), ToScreenY(polyline[i].Y)));
            ctx.EndFigure(true);
        }

        // 断面内部填充 + 轮廓暗带 + 轮廓亮线
        context.DrawGeometry(FaceFillBrush, null, outline);
        context.DrawGeometry(null, new Pen(FaceEdgeBrush, 10), outline);
        context.DrawGeometry(null, new Pen(OutlineBrush, 2), outline);

        // ===== 参考线：隧道中线、拱脚线 =====
        var guidePen = new Pen(GuideBrush, 1) { DashStyle = new DashStyle(new double[] { 7, 7 }, 0) };
        context.DrawLine(guidePen, new Point(ToScreenX(0), ToScreenY(section.MinY)),
                                     new Point(ToScreenX(0), ToScreenY(section.MaxY)));
        if (section.IsStandardArch)
            context.DrawLine(guidePen, new Point(ToScreenX(section.MinX), ToScreenY(section.WallHeight)),
                                         new Point(ToScreenX(section.MaxX), ToScreenY(section.WallHeight)));

        // ===== 尺寸标注 =====
        var leftmostWorld = section.MinX;
        if (Holes is not null)
        {
            foreach (var hole in Holes)
            {
                if (hole.X < leftmostWorld) leftmostWorld = hole.X;
            }
        }

        DrawSpanDimension(context, section, ToScreenX, ToScreenY);
        DrawHeightDimension(context, section, ToScreenX, ToScreenY, leftmostWorld);

        // ===== 钻孔路径 / 臂架 =====
        var paths = Paths;
        if (paths is not null)
        {
            foreach (var path in paths)
                DrawPath(context, path, ToScreenX, ToScreenY);
        }

        // ===== 炮孔 =====
        var holes = Holes;
        if (holes is not null)
        {
            var toePen = new Pen(ToeBrush, 1);
            var numberText = new Typeface(new FontFamily("Consolas, Menlo, monospace"), FontStyle.Normal, FontWeight.SemiBold);
            var alarmPen = new Pen(AlarmBrush, 1.2) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };

            // 孔圈与编号随画布缩放，画布越大点位越清晰（下限保证小窗仍可辨认）
            var holeRadius = Math.Clamp(scale * 0.16, 5.5, 12.5);
            var holeFontSize = Math.Clamp(scale * 0.17, 7.5, 12.5);
            var labelWidth = holeRadius * 4.8;
            var toeRadius = Math.Max(2.5, holeRadius * 0.42);

            foreach (var hole in holes)
            {
                var collar = new Point(ToScreenX(hole.X), ToScreenY(hole.Y));
                var toe = new Point(ToScreenX(hole.ToeX), ToScreenY(hole.ToeY));

                // 斜孔：孔口 → 孔底投影的连线（FormCollect 中的 DrawLine + 小孔圈）
                if (hole.TiltDeg > 0.01)
                {
                    context.DrawLine(toePen, collar, toe);
                    context.DrawEllipse(hole.Fill, new Pen(hole.Stroke, 1), toe, toeRadius, toeRadius);
                }

                var radius = hole.IsRelief ? holeRadius * 1.35 : holeRadius;

                if (hole.StateText == "已完成")
                    context.DrawEllipse(DoneHaloBrush, null, collar, radius + holeRadius * 0.55, radius + holeRadius * 0.55);

                context.DrawEllipse(hole.Fill, new Pen(hole.Stroke, hole.StrokeThickness), collar, radius, radius);

                if (hole.IsRelief)
                {
                    var reliefPen = new Pen(hole.Stroke, 1) { DashStyle = new DashStyle(new double[] { 2, 2 }, 0) };
                    context.DrawEllipse(null, reliefPen, collar, radius - holeRadius * 0.45, radius - holeRadius * 0.45);
                }

                // 防碰撞：干涉孔加红色虚线圈
                if (hole.HasCollision || hole.CollisionText.Length > 0)
                    context.DrawEllipse(null, alarmPen, collar, radius + holeRadius * 0.4, radius + holeRadius * 0.4);

                var text = new FormattedText(hole.NoText, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    numberText, holeFontSize, HoleTextBrush)
                {
                    MaxTextWidth = labelWidth,
                    TextAlignment = TextAlignment.Center
                };
                context.DrawText(text, new Point(collar.X - labelWidth / 2, collar.Y - text.Height / 2));
            }
        }

        // ===== 臂架基座 =====
        var booms = Booms;
        if (booms is not null)
        {
            var boomRadius = Math.Clamp(scale * 0.09, 4, 8);

            foreach (var boom in booms)
            {
                var p = new Point(ToScreenX(boom.X), ToScreenY(boom.Z));
                context.DrawEllipse(boom.Brush, new Pen(FaceFillBrush, 1.2), p, boomRadius, boomRadius);

                var label = new FormattedText(boom.Label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface("Microsoft YaHei, sans-serif"), Math.Clamp(scale * 0.15, 9, 13), boom.Brush);
                context.DrawText(label, new Point(p.X + boomRadius + 2, p.Y - label.Height / 2));
            }
        }

        // ===== 当前孔高亮 =====
        var selected = SelectedHole;
        if (selected is null) return;

        var c = new Point(ToScreenX(selected.X), ToScreenY(selected.Y));
        var outer = Math.Clamp(scale * 0.26, 10, 20);

        context.DrawEllipse(null, new Pen(AccentBrush, 1.8), c, outer + 4, outer + 4);
        context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.Parse("#4023A9F0")), 1), c, outer + 10, outer + 10);

        var crossPen = new Pen(new SolidColorBrush(Color.Parse("#A623A9F0")), 1);
        context.DrawLine(crossPen, new Point(c.X - outer - 12, c.Y), new Point(c.X - outer - 5, c.Y));
        context.DrawLine(crossPen, new Point(c.X + outer + 5, c.Y), new Point(c.X + outer + 12, c.Y));
        context.DrawLine(crossPen, new Point(c.X, c.Y - outer - 12), new Point(c.X, c.Y - outer - 5));
        context.DrawLine(crossPen, new Point(c.X, c.Y + outer + 5), new Point(c.X, c.Y + outer + 12));
    }

    /// <summary>
    /// 计算世界坐标 → 屏幕坐标映射。包围盒取“断面 ∪ 全部炮孔（孔口/孔底）∪ 路径 ∪ 臂架”，
    /// 内容整体居中并按可用空间自动缩放，保证所有点位都完整落在图内。
    /// </summary>
    private (double Scale, double OffsetX, double OffsetY)? GetLayout(TunnelSection? section)
    {
        if (section is null) return null;

        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w < 80 || h < 80) return null;

        const double leftGutter = 50;    // 左侧净高标注
        const double rightMargin = 14;
        const double topMargin = 10;
        const double bottomGutter = 36;  // 底部跨度标注

        var boxX = leftGutter;
        var boxY = topMargin;
        var boxW = w - leftGutter - rightMargin;
        var boxH = h - topMargin - bottomGutter;
        if (boxW < 40 || boxH < 40) return null;

        // ===== 世界坐标包围盒 =====
        var minX = section.MinX;
        var maxX = section.MaxX;
        var minY = section.MinY;
        var maxY = section.MaxY;

        void Include(double x, double y)
        {
            if (!double.IsFinite(x) || !double.IsFinite(y)) return;
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }

        if (Holes is not null)
        {
            foreach (var hole in Holes)
            {
                Include(hole.X, hole.Y);
                if (hole.TiltDeg > 0.01) Include(hole.ToeX, hole.ToeY);
            }
        }

        if (Paths is not null)
        {
            foreach (var path in Paths)
            {
                foreach (var p in path.Points)
                    Include(p.X, p.Y);
            }
        }

        if (Booms is not null)
        {
            foreach (var boom in Booms)
                Include(boom.X, boom.Z);
        }

        const double pad = 0.4;   // 世界坐标边距 / m：给孔圈与编号留出空隙

        // 水平以断面中心对称留边（保证隧道中线居中），竖向按实际包围盒紧贴
        var cx = (section.MinX + section.MaxX) / 2;
        var halfW = Math.Max(0.2, Math.Max(cx - minX, maxX - cx)) + pad;
        var cy = (minY + maxY) / 2;
        var halfH = Math.Max(0.2, (maxY - minY) / 2) + pad;

        var scale = Math.Min(boxW / (halfW * 2), boxH / (halfH * 2));
        if (scale <= 0) return null;

        return (scale,
                boxX + boxW / 2 - cx * scale,
                boxY + boxH / 2 + cy * scale);
    }

    /// <summary>点击炮孔切换选中孔（供逆解 / 正解 / 防碰撞详情使用）。</summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var section = Section;
        var holes = Holes;
        var layout = GetLayout(section);
        if (section is null || holes is null || layout is null) return;

        var (scale, offsetX, offsetY) = layout.Value;
        var pos = e.GetPosition(this);

        var wx = (pos.X - offsetX) / scale;
        var wy = (offsetY - pos.Y) / scale;

        HoleVm? nearest = null;
        var bestDistance = double.MaxValue;

        foreach (var hole in holes)
        {
            var dx = hole.X - wx;
            var dy = hole.Y - wy;
            var d = dx * dx + dy * dy;
            if (d < bestDistance)
            {
                bestDistance = d;
                nearest = hole;
            }
        }

        if (nearest is null) return;

        var pixelDistance = Math.Sqrt(bestDistance) * scale;
        if (pixelDistance > 16) return;

        SelectedHole = nearest;
        e.Handled = true;
    }

    /// <summary>绘制一条连线（钻孔路径带箭头，臂架连线为虚线）。</summary>
    private static void DrawPath(DrawingContext context, HolePathVm path,
        Func<double, double> sx, Func<double, double> sy)
    {
        if (path.Points.Count < 2) return;

        var pen = new Pen(path.Brush, path.Thickness, lineCap: PenLineCap.Round);
        if (path.Dashed) pen.DashStyle = new DashStyle(new double[] { 5, 5 }, 0);

        for (var i = 1; i < path.Points.Count; i++)
        {
            var a = new Point(sx(path.Points[i - 1].X), sy(path.Points[i - 1].Y));
            var b = new Point(sx(path.Points[i].X), sy(path.Points[i].Y));
            context.DrawLine(pen, a, b);

            if (!path.ShowArrows || i % 2 != 0) continue;

            // 方向箭头
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 8) continue;

            var ux = dx / len;
            var uy = dy / len;
            var tip = new Point(a.X + ux * (len * 0.5), a.Y + uy * (len * 0.5));
            var left = new Point(tip.X - ux * 7 - uy * 3.5, tip.Y - uy * 7 + ux * 3.5);
            var right = new Point(tip.X - ux * 7 + uy * 3.5, tip.Y - uy * 7 - ux * 3.5);
            context.DrawLine(pen, tip, left);
            context.DrawLine(pen, tip, right);
        }
    }

    /// <summary>断面跨度标注（底板下方）。</summary>
    private void DrawSpanDimension(DrawingContext context, TunnelSection section,
        Func<double, double> sx, Func<double, double> sy)
    {
        var y = sy(section.MinY) + 18;
        var x1 = sx(section.MinX);
        var x2 = sx(section.MaxX);
        var pen = new Pen(DimBrush, 1);

        context.DrawLine(pen, new Point(x1, y), new Point(x2, y));
        context.DrawLine(pen, new Point(x1, y - 5), new Point(x1, y + 5));
        context.DrawLine(pen, new Point(x2, y - 5), new Point(x2, y + 5));

        var text = new FormattedText($"{section.Span:F2} m", CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface("Consolas, Menlo, monospace"), 11, DimBrush);
        context.DrawText(text, new Point((x1 + x2) / 2 - text.Width / 2, y + 4));
    }

    /// <summary>断面净高标注（左侧，避开超出轮廓的周边孔）。</summary>
    private void DrawHeightDimension(DrawingContext context, TunnelSection section,
        Func<double, double> sx, Func<double, double> sy, double leftmostWorld)
    {
        var x = Math.Max(24, sx(Math.Min(leftmostWorld, section.MinX)) - 16);
        var yBottom = sy(section.MinY);
        var yTop = sy(section.MaxY);
        var pen = new Pen(DimBrush, 1);

        context.DrawLine(pen, new Point(x, yBottom), new Point(x, yTop));
        context.DrawLine(pen, new Point(x - 5, yBottom), new Point(x + 5, yBottom));
        context.DrawLine(pen, new Point(x - 5, yTop), new Point(x + 5, yTop));

        var text = new FormattedText($"{section.Height:F2} m", CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight, new Typeface("Consolas, Menlo, monospace"), 11, DimBrush);
        var left = Math.Max(2, x - text.Width - 6);
        context.DrawText(text, new Point(left, (yBottom + yTop) / 2 - text.Height / 2));
    }
}
