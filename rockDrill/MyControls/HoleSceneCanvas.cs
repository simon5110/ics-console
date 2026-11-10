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
/// 钻孔页的掌子面作业图，对齐现场 HMI（doc/现场测试视频.mp4）：
/// 支持 <b>正视图 / 侧视图 / 俯视图 / 三维视图</b> 四种投影切换，
/// 绘制断面轮廓与参考网格、全部炮孔（含孔号、孔底投影）、双臂当前钻孔的钻杆与靶圈，
/// 以及臂架当前位置准星（黄色十字）和左右臂姿态读数。
/// </summary>
public class HoleSceneCanvas : Control
{
    // ===================== 依赖属性 =====================

    public static readonly StyledProperty<TunnelSection?> SectionProperty =
        AvaloniaProperty.Register<HoleSceneCanvas, TunnelSection?>(nameof(Section));

    public static readonly StyledProperty<IEnumerable<HoleVm>?> HolesProperty =
        AvaloniaProperty.Register<HoleSceneCanvas, IEnumerable<HoleVm>?>(nameof(Holes));

    /// <summary>正在钻进的孔（画钻杆 + 绿色靶圈）。</summary>
    public static readonly StyledProperty<HoleVm?> CurrentHoleProperty =
        AvaloniaProperty.Register<HoleSceneCanvas, HoleVm?>(nameof(CurrentHole));

    /// <summary>点击选中的孔（手动规划用，双向绑定）。</summary>
    public static readonly StyledProperty<HoleVm?> SelectedHoleProperty =
        AvaloniaProperty.Register<HoleSceneCanvas, HoleVm?>(nameof(SelectedHole), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<HoleViewMode> ViewModeProperty =
        AvaloniaProperty.Register<HoleSceneCanvas, HoleViewMode>(nameof(ViewMode));

    /// <summary>臂架当前位置标记（黄色准星）。</summary>
    public static readonly StyledProperty<IEnumerable<ArmMarkVm>?> ArmMarksProperty =
        AvaloniaProperty.Register<HoleSceneCanvas, IEnumerable<ArmMarkVm>?>(nameof(ArmMarks));

    /// <summary>左上角姿态读数（左臂：末端坐标 / 翻转 / 俯仰 / 补偿）。</summary>
    public static readonly StyledProperty<string?> LeftInfoTextProperty =
        AvaloniaProperty.Register<HoleSceneCanvas, string?>(nameof(LeftInfoText));

    /// <summary>右上角姿态读数（右臂）。</summary>
    public static readonly StyledProperty<string?> RightInfoTextProperty =
        AvaloniaProperty.Register<HoleSceneCanvas, string?>(nameof(RightInfoText));

    /// <summary>左上角大字角标（单臂局部图上的“左”/“右”）。</summary>
    public static readonly StyledProperty<string?> CaptionProperty =
        AvaloniaProperty.Register<HoleSceneCanvas, string?>(nameof(Caption));

    public static readonly StyledProperty<bool> ShowDimensionsProperty =
        AvaloniaProperty.Register<HoleSceneCanvas, bool>(nameof(ShowDimensions), true);

    public static readonly StyledProperty<bool> ShowHoleLabelsProperty =
        AvaloniaProperty.Register<HoleSceneCanvas, bool>(nameof(ShowHoleLabels), true);

    public static readonly StyledProperty<bool> ShowGridProperty =
        AvaloniaProperty.Register<HoleSceneCanvas, bool>(nameof(ShowGrid), true);

    /// <summary>数据版本号（变化时强制重绘）。</summary>
    public static readonly StyledProperty<int> RevisionProperty =
        AvaloniaProperty.Register<HoleSceneCanvas, int>(nameof(Revision));

    // ===================== 画笔 =====================

    private static readonly IBrush BackdropBrush = new SolidColorBrush(Color.Parse("#060C11"));
    private static readonly IBrush GridBrush = new SolidColorBrush(Color.Parse("#0F1D27"));
    private static readonly IBrush GridAxisBrush = new SolidColorBrush(Color.Parse("#17303F"));
    private static readonly IBrush OutlineBrushStatic = new SolidColorBrush(Color.Parse("#3E6B86"));
    private static readonly IBrush GuideBrushStatic = new SolidColorBrush(Color.Parse("#1B2C38"));
    private static readonly IBrush FaceFillBrush = new SolidColorBrush(Color.Parse("#0A1319"));
    private static readonly IBrush DimBrush = new SolidColorBrush(Color.Parse("#5C7484"));
    private static readonly IBrush LightTextBrush = new SolidColorBrush(Color.Parse("#D6E6F1"));
    private static readonly IBrush DarkTextBrush = new SolidColorBrush(Color.Parse("#0A1319"));
    private static readonly IBrush TargetBrush = new SolidColorBrush(Color.Parse("#2AD07C"));
    private static readonly IBrush TargetFillBrush = new SolidColorBrush(Color.Parse("#CC2AD07C"));
    private static readonly IBrush RodBrush = new SolidColorBrush(Color.Parse("#2EE6D6"));
    private static readonly IBrush RodGlowBrush = new SolidColorBrush(Color.Parse("#552EE6D6"));
    private static readonly IBrush MarkBrush = new SolidColorBrush(Color.Parse("#F5D24A"));
    private static readonly IBrush InfoBrush = new SolidColorBrush(Color.Parse("#F0D98C"));
    private static readonly IBrush CaptionBrush = new SolidColorBrush(Color.Parse("#2AD07C"));

    private static readonly Typeface MonoFace = new("Consolas, Menlo, monospace");
    private static readonly Typeface UiFace = new("Microsoft YaHei, sans-serif");

    // 本次渲染使用的世界 → 屏幕变换（Render 内刷新），避免每个孔重复求布局
    private double _scale = 1;
    private double _offsetX;
    private double _offsetY;

    static HoleSceneCanvas()
    {
        AffectsRender<HoleSceneCanvas>(SectionProperty, HolesProperty, CurrentHoleProperty,
            SelectedHoleProperty, ViewModeProperty, ArmMarksProperty, LeftInfoTextProperty,
            RightInfoTextProperty, CaptionProperty, ShowDimensionsProperty,
            ShowHoleLabelsProperty, ShowGridProperty, RevisionProperty);
    }

    public TunnelSection? Section { get => GetValue(SectionProperty); set => SetValue(SectionProperty, value); }
    public IEnumerable<HoleVm>? Holes { get => GetValue(HolesProperty); set => SetValue(HolesProperty, value); }
    public HoleVm? CurrentHole { get => GetValue(CurrentHoleProperty); set => SetValue(CurrentHoleProperty, value); }
    public HoleVm? SelectedHole { get => GetValue(SelectedHoleProperty); set => SetValue(SelectedHoleProperty, value); }
    public HoleViewMode ViewMode { get => GetValue(ViewModeProperty); set => SetValue(ViewModeProperty, value); }
    public IEnumerable<ArmMarkVm>? ArmMarks { get => GetValue(ArmMarksProperty); set => SetValue(ArmMarksProperty, value); }
    public string? LeftInfoText { get => GetValue(LeftInfoTextProperty); set => SetValue(LeftInfoTextProperty, value); }
    public string? RightInfoText { get => GetValue(RightInfoTextProperty); set => SetValue(RightInfoTextProperty, value); }
    public string? Caption { get => GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
    public bool ShowDimensions { get => GetValue(ShowDimensionsProperty); set => SetValue(ShowDimensionsProperty, value); }
    public bool ShowHoleLabels { get => GetValue(ShowHoleLabelsProperty); set => SetValue(ShowHoleLabelsProperty, value); }
    public bool ShowGrid { get => GetValue(ShowGridProperty); set => SetValue(ShowGridProperty, value); }
    public int Revision { get => GetValue(RevisionProperty); set => SetValue(RevisionProperty, value); }

    // ===================== 渲染 =====================

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width < 20 || Bounds.Height < 20) return;

        context.FillRectangle(BackdropBrush, new Rect(Bounds.Size));

        if (Section is not { } section) return;
        var layout = GetLayout(section);
        if (layout is null) return;

        var (scale, offsetX, offsetY) = layout.Value;
        _scale = scale;
        _offsetX = offsetX;
        _offsetY = offsetY;
        var axialMax = GetAxialMax();

        if (ShowGrid) DrawGrid(context, scale, offsetX, offsetY);

        DrawOutline(context, section, axialMax, scale, offsetX, offsetY);

        if (ShowDimensions) DrawDimensions(context, section, axialMax, scale, offsetX, offsetY);

        DrawHoles(context, scale, offsetX, offsetY);

        DrawCurrentHole(context, scale, offsetX, offsetY);
        DrawArmMarks(context, scale, offsetX, offsetY);

        DrawAxisHint(context);
        DrawInfoTexts(context);
        DrawCaption(context);
    }

    /// <summary>三维点 → 视图平面坐标（米）。正视图忽略进深，侧视图/俯视图/三维视图带上进深。</summary>
    private Point Project(double x, double z, double axial)
    {
        switch (ViewMode)
        {
            case HoleViewMode.Side:
                return new Point(axial, z);
            case HoleViewMode.Top:
                return new Point(x, axial);
            case HoleViewMode.Iso:
                const double c = 0.866;
                const double s = 0.5;
                return new Point((x - axial) * c, z + (x + axial) * s);
            default:
                return new Point(x, z);
        }
    }

    private Point P(double x, double z, double axial)
    {
        var p = Project(x, z, axial);
        return new Point(_offsetX + p.X * _scale, _offsetY - p.Y * _scale);
    }

    /// <summary>炮孔在当前视图下的孔口屏幕坐标。</summary>
    private Point CollarPoint(HoleVm hole) => P(hole.X, hole.Y, hole.AxialY);

    private Point ToePoint(HoleVm hole)
        => P(hole.X + hole.DirX * hole.Depth,
             hole.Y + hole.DirZ * hole.Depth,
             hole.AxialY + hole.DirY * hole.Depth);

    /// <summary>孔轴方向的最大进深 / m（侧视 / 俯视 / 三维视图的箱体长度）。</summary>
    private double GetAxialMax()
    {
        var max = 0.6;
        if (Holes is not null)
        {
            foreach (var hole in Holes)
            {
                var toe = hole.AxialY + hole.DirY * Math.Max(hole.Depth, 0);
                if (toe > max) max = toe;
            }
        }
        return max;
    }

    // ===================== 布局（世界 → 屏幕） =====================

    private (double Scale, double OffsetX, double OffsetY)? GetLayout(TunnelSection? section)
    {
        if (section is null) return null;

        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w < 60 || h < 60) return null;

        var leftGutter = ShowDimensions && ViewMode == HoleViewMode.Front ? 46.0 : 16.0;
        var bottomGutter = ShowDimensions && ViewMode == HoleViewMode.Front ? 30.0 : 16.0;
        var boxX = leftGutter;
        var boxY = 12.0;
        var boxW = w - leftGutter - 14;
        var boxH = h - boxY - bottomGutter;
        if (boxW < 40 || boxH < 40) return null;

        var axialMax = GetAxialMax();

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

        foreach (var p in OutlinePoints(section, axialMax)) Include(p);

        if (Holes is not null)
        {
            foreach (var hole in Holes)
            {
                Include(Project(hole.X, hole.Y, hole.AxialY));
                if (hole.Depth > 0.01)
                    Include(Project(hole.X + hole.DirX * hole.Depth,
                                    hole.Y + hole.DirZ * hole.Depth,
                                    hole.AxialY + hole.DirY * hole.Depth));
            }
        }

        if (ArmMarks is not null)
        {
            foreach (var mark in ArmMarks)
                Include(Project(mark.X, mark.Y, mark.Axial));
        }

        if (minX > maxX || minY > maxY) return null;

        const double pad = 0.35;          // 世界坐标边距 / m
        minX -= pad; maxX += pad; minY -= pad; maxY += pad;

        var spanX = Math.Max(0.4, maxX - minX);
        var spanY = Math.Max(0.4, maxY - minY);

        var scale = Math.Min(boxW / spanX, boxH / spanY);
        if (scale <= 0 || !double.IsFinite(scale)) return null;

        return (scale,
                boxX + boxW / 2 - (minX + maxX) / 2 * scale,
                boxY + boxH / 2 + (minY + maxY) / 2 * scale);
    }

    /// <summary>断面在当前视图下的轮廓点（用于包围盒与绘制）。</summary>
    private IEnumerable<Point> OutlinePoints(TunnelSection section, double axialMax)
    {
        switch (ViewMode)
        {
            case HoleViewMode.Side:
                yield return Project(0, section.MinY, 0);
                yield return Project(0, section.MaxY, 0);
                yield return Project(0, section.MaxY, axialMax);
                yield return Project(0, section.MinY, axialMax);
                break;

            case HoleViewMode.Top:
                yield return Project(section.MinX, 0, 0);
                yield return Project(section.MaxX, 0, 0);
                yield return Project(section.MaxX, 0, axialMax);
                yield return Project(section.MinX, 0, axialMax);
                break;

            case HoleViewMode.Iso:
                foreach (var p in section.ToPolyline()) yield return Project(p.X, p.Y, 0);
                foreach (var p in section.ToPolyline()) yield return Project(p.X, p.Y, axialMax);
                break;

            default:
                foreach (var p in section.ToPolyline()) yield return Project(p.X, p.Y, 0);
                break;
        }
    }

    // ===================== 绘制：网格 / 轮廓 =====================

    private void DrawGrid(DrawingContext context, double scale, double ox, double oy)
    {
        if (scale <= 0) return;

        // 网格间距按缩放自适应（1 m 起，过密则翻倍），避免小比例时糊成一片
        var step = 1.0;
        while (step * scale < 16) step *= 2;
        var stepPx = step * scale;

        var pen = new Pen(GridBrush, 1);
        var axisPen = new Pen(GridAxisBrush, 1);

        // 世界原点对齐屏幕
        var startX = ox - Math.Ceiling(ox / stepPx) * stepPx;
        if (startX < 0) startX += stepPx;

        for (var x = startX; x < Bounds.Width; x += stepPx)
        {
            var world = (x - ox) / scale;
            var p = Math.Abs(world - Math.Round(world)) < 0.001 ? axisPen : pen;
            context.DrawLine(p, new Point(x, 0), new Point(x, Bounds.Height));
        }

        var startY = oy - Math.Floor(oy / stepPx) * stepPx;
        if (startY > Bounds.Height) startY -= stepPx;

        for (var y = startY; y < Bounds.Height; y += stepPx)
        {
            var world = (oy - y) / scale;
            var p = Math.Abs(world - Math.Round(world)) < 0.001 ? axisPen : pen;
            context.DrawLine(p, new Point(0, y), new Point(Bounds.Width, y));
        }
    }

    private void DrawOutline(DrawingContext context, TunnelSection section, double axialMax,
        double scale, double ox, double oy)
    {
        Point S(Point p) => new(ox + p.X * scale, oy - p.Y * scale);

        var dashPen = new Pen(GuideBrushStatic, 1) { DashStyle = new DashStyle(new double[] { 5, 5 }, 0) };
        var solidPen = new Pen(OutlineBrushStatic, 1.6);

        if (ViewMode == HoleViewMode.Front)
        {
            var polyline = section.ToPolyline();
            var outline = new StreamGeometry();
            using (var ctx = outline.Open())
            {
                ctx.BeginFigure(S(Project(polyline[0].X, polyline[0].Y, 0)), true);
                for (var i = 1; i < polyline.Count; i++)
                    ctx.LineTo(S(Project(polyline[i].X, polyline[i].Y, 0)));
                ctx.EndFigure(true);
            }

            context.DrawGeometry(FaceFillBrush, null, outline);
            context.DrawGeometry(null, solidPen, outline);

            // 隧道中线 / 拱脚线
            context.DrawLine(dashPen,
                S(Project(0, section.MinY, 0)), S(Project(0, section.MaxY, 0)));
            if (section.IsStandardArch)
                context.DrawLine(dashPen,
                    S(Project(section.MinX, section.WallHeight, 0)),
                    S(Project(section.MaxX, section.WallHeight, 0)));
            return;
        }

        // 侧视 / 俯视：断面沿进深方向的矩形包络
        if (ViewMode == HoleViewMode.Iso)
        {
            // 三维视图：掌子面轮廓实线 + 孔底端面虚线 + 四条棱线
            var polyline = section.ToPolyline();
            var face = polyline.Select(p => S(Project(p.X, p.Y, 0))).ToList();
            var back = polyline.Select(p => S(Project(p.X, p.Y, axialMax))).ToList();

            DrawPolygon(context, face, solidPen, FaceFillBrush);
            DrawPolygon(context, back, dashPen, null);

            foreach (var idx in new[] { 0, polyline.Count / 4, polyline.Count / 2, polyline.Count * 3 / 4 })
                context.DrawLine(dashPen, face[idx], back[idx]);
            return;
        }

        var box = OutlinePoints(section, axialMax).Select(S).ToList();
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(box[0], false);
            for (var i = 1; i < box.Count; i++) ctx.LineTo(box[i]);
            ctx.EndFigure(true);
        }
        context.DrawGeometry(null, solidPen, geo);

        if (ViewMode == HoleViewMode.Top)
        {
            // 掌子面位置（进深 0）与隧道中线
            context.DrawLine(dashPen, S(Project(section.MinX, 0, 0)), S(Project(section.MaxX, 0, 0)));
            context.DrawLine(dashPen, S(Project(0, 0, 0)), S(Project(0, 0, axialMax)));
        }
        else
        {
            // 侧视：掌子面立面、底板、拱顶
            context.DrawLine(dashPen, S(Project(0, section.MinY, 0)), S(Project(0, section.MaxY, 0)));
            context.DrawLine(dashPen, S(Project(0, section.MinY, 0)), S(Project(0, section.MinY, axialMax)));
            context.DrawLine(dashPen, S(Project(0, section.MaxY, 0)), S(Project(0, section.MaxY, axialMax)));
        }
    }

    /// <summary>闭合多边形（可选填充）。</summary>
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

    private void DrawDimensions(DrawingContext context, TunnelSection section, double axialMax,
        double scale, double ox, double oy)
    {
        var pen = new Pen(DimBrush, 1);
        Point S(Point p) => new(ox + p.X * scale, oy - p.Y * scale);

        if (ViewMode == HoleViewMode.Front)
        {
            // 跨度标注（底板下方）
            var y = S(Project(0, section.MinY, 0)).Y + 18;
            var x1 = S(Project(section.MinX, 0, 0)).X;
            var x2 = S(Project(section.MaxX, 0, 0)).X;
            context.DrawLine(pen, new Point(x1, y), new Point(x2, y));
            context.DrawLine(pen, new Point(x1, y - 5), new Point(x1, y + 5));
            context.DrawLine(pen, new Point(x2, y - 5), new Point(x2, y + 5));

            var spanText = Format($"{section.Span:F2} m", 11, DimBrush);
            context.DrawText(spanText, new Point((x1 + x2) / 2 - spanText.Width / 2, y + 3));

            // 净高标注（左侧）
            var x = S(Project(section.MinX, 0, 0)).X - 18;
            var yb = S(Project(0, section.MinY, 0)).Y;
            var yt = S(Project(0, section.MaxY, 0)).Y;
            context.DrawLine(pen, new Point(x, yb), new Point(x, yt));
            context.DrawLine(pen, new Point(x - 5, yb), new Point(x + 5, yb));
            context.DrawLine(pen, new Point(x - 5, yt), new Point(x + 5, yt));

            var heightText = Format($"{section.Height:F2} m", 11, DimBrush);
            context.DrawText(heightText, new Point(Math.Max(2, x - heightText.Width - 6),
                (yb + yt) / 2 - heightText.Height / 2));
            return;
        }

        // 侧视 / 俯视 / 三维：标注进深
        var a0 = S(Project(0, 0, 0));
        var a1 = S(Project(0, 0, axialMax));
        var depthLabel = Format($"进深 {axialMax:F2} m", 11, DimBrush);
        var anchor = ViewMode == HoleViewMode.Side ? a1 : a0;
        context.DrawText(depthLabel, new Point(anchor.X - depthLabel.Width / 2, Bounds.Height - 18));
    }

    // ===================== 绘制：炮孔 =====================

    private void DrawHoles(DrawingContext context, double scale, double ox, double oy)
    {
        var holes = Holes;
        if (holes is null) return;

        var radius = Math.Clamp(scale * 0.11, 6.0, 12.0);
        var fontSize = Math.Clamp(radius * 0.9, 7.0, 10.5);
        var toeRadius = Math.Max(1.6, radius * 0.3);

        // 第一遍：斜孔的孔向连线与孔底投影（先画线，避免压住后画的孔圈与编号）
        foreach (var hole in holes)
        {
            if (hole.Depth <= 0.01) continue;

            var collar = CollarPoint(hole);
            var toe = ToePoint(hole);

            var dx = toe.X - collar.X;
            var dy = toe.Y - collar.Y;
            if (Math.Sqrt(dx * dx + dy * dy) <= radius * 1.6) continue;

            context.DrawLine(new Pen(hole.Stroke, 1), collar, toe);
            context.DrawEllipse(hole.Fill, new Pen(hole.Stroke, 1), toe, toeRadius, toeRadius);
        }

        // 第二遍：孔圈、编号、标记
        foreach (var hole in holes)
        {
            var collar = CollarPoint(hole);
            var r = hole.IsRelief ? radius * 1.35 : radius;

            if (hole.StateText == "已完成")
                context.DrawEllipse(new SolidColorBrush(Color.Parse("#332AD07C")), null, collar, r * 1.6, r * 1.6);

            context.DrawEllipse(hole.Fill, new Pen(hole.Stroke, hole.StrokeThickness), collar, r, r);

            if (hole.IsRelief)
            {
                var reliefPen = new Pen(hole.Stroke, 1) { DashStyle = new DashStyle(new double[] { 2, 2 }, 0) };
                context.DrawEllipse(null, reliefPen, collar, r - radius * 0.45, r - radius * 0.45);
            }

            if (!ShowHoleLabels) continue;

            var label = Format(hole.NoText, fontSize, LabelBrushFor(hole.Fill), MonoFace);
            context.DrawText(label, new Point(collar.X - label.Width / 2, collar.Y - label.Height / 2));
        }
    }

    /// <summary>孔号文字颜色：深色孔圈用亮字，亮色孔圈用暗字。</summary>
    private static IBrush LabelBrushFor(IBrush fill)
    {
        if (fill is ISolidColorBrush solid)
        {
            var lum = (0.299 * solid.Color.R + 0.587 * solid.Color.G + 0.114 * solid.Color.B) / 255.0;
            return lum > 0.55 ? DarkTextBrush : LightTextBrush;
        }
        return LightTextBrush;
    }

    private void DrawCurrentHole(DrawingContext context, double scale, double ox, double oy)
    {
        var current = CurrentHole;
        if (current is null) return;

        var collar = CollarPoint(current);
        var toe = ToePoint(current);

        var vx = toe.X - collar.X;
        var vy = toe.Y - collar.Y;
        var len = Math.Sqrt(vx * vx + vy * vy);

        // 正视图里直孔的孔轴投影退化成一点：改用一段横向钻杆示意，便于观察进尺
        if (len < 8)
        {
            var stub = Math.Max(28, scale * 1.4);
            toe = new Point(collar.X + stub, collar.Y);
            vx = stub;
            vy = 0;
            len = stub;
        }

        context.DrawLine(new Pen(RodGlowBrush, 7, lineCap: PenLineCap.Round), collar, toe);
        context.DrawLine(new Pen(RodBrush, 2.4, lineCap: PenLineCap.Round), collar, toe);

        var r = Math.Clamp(scale * 0.15, 8, 17);
        context.DrawEllipse(null, new Pen(TargetBrush, 2), toe, r, r);
        context.DrawEllipse(TargetFillBrush, null, toe, r * 0.32, r * 0.32);

        var crossPen = new Pen(TargetBrush, 1.4);
        context.DrawLine(crossPen, new Point(toe.X - r - 5, toe.Y), new Point(toe.X - r + 1, toe.Y));
        context.DrawLine(crossPen, new Point(toe.X + r - 1, toe.Y), new Point(toe.X + r + 5, toe.Y));
        context.DrawLine(crossPen, new Point(toe.X, toe.Y - r - 5), new Point(toe.X, toe.Y - r + 1));
        context.DrawLine(crossPen, new Point(toe.X, toe.Y + r - 1), new Point(toe.X, toe.Y + r + 5));
    }

    private void DrawArmMarks(DrawingContext context, double scale, double ox, double oy)
    {
        var marks = ArmMarks;
        if (marks is null) return;

        foreach (var mark in marks)
        {
            var p = P(mark.X, mark.Y, mark.Axial);
            var size = Math.Clamp(scale * 0.14, 7, 14);
            var pen = new Pen(mark.Brush, 1.8);

            // 黄色十字准星（现场 HMI 的“臂架当前位置”）
            context.DrawLine(pen, new Point(p.X - size, p.Y), new Point(p.X - size * 0.35, p.Y));
            context.DrawLine(pen, new Point(p.X + size * 0.35, p.Y), new Point(p.X + size, p.Y));
            context.DrawLine(pen, new Point(p.X, p.Y - size), new Point(p.X, p.Y - size * 0.35));
            context.DrawLine(pen, new Point(p.X, p.Y + size * 0.35), new Point(p.X, p.Y + size));

            var diamond = new StreamGeometry();
            using (var ctx = diamond.Open())
            {
                ctx.BeginFigure(new Point(p.X, p.Y - size * 0.5), true);
                ctx.LineTo(new Point(p.X + size * 0.5, p.Y));
                ctx.LineTo(new Point(p.X, p.Y + size * 0.5));
                ctx.LineTo(new Point(p.X - size * 0.5, p.Y));
                ctx.EndFigure(true);
            }
            context.DrawGeometry(null, pen, diamond);
        }
    }

    // ===================== 绘制：文字 =====================

    private void DrawAxisHint(DrawingContext context)
    {
        var text = Format(ViewMode.AxisHint(), 11, DimBrush);
        context.DrawText(text, new Point(Bounds.Width - text.Width - 12, Bounds.Height - text.Height - 6));
    }

    private void DrawInfoTexts(DrawingContext context)
    {
        var left = LeftInfoText;
        if (!string.IsNullOrWhiteSpace(left))
        {
            var text = Format(left, 11.5, InfoBrush, MonoFace);
            context.DrawText(text, new Point(10, 8));
        }

        var right = RightInfoText;
        if (!string.IsNullOrWhiteSpace(right))
        {
            var text = Format(right, 11.5, InfoBrush, MonoFace);
            context.DrawText(text, new Point(Bounds.Width - text.Width - 10, 8));
        }
    }

    private void DrawCaption(DrawingContext context)
    {
        var caption = Caption;
        if (string.IsNullOrWhiteSpace(caption)) return;

        var size = Math.Clamp(Bounds.Height * 0.14, 24, 56);
        var text = Format(caption, size, CaptionBrush, UiFace);
        context.DrawText(text, new Point(14, 10));
    }

    private static FormattedText Format(string text, double size, IBrush brush, Typeface? face = null)
        => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
               face ?? UiFace, size, brush);

    // ===================== 交互 =====================

    /// <summary>点击炮孔：切换选中孔（手动规划 / 查看孔位信息）。</summary>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var holes = Holes;
        if (holes is null || Section is null) return;

        var pos = e.GetPosition(this);

        HoleVm? nearest = null;
        var best = double.MaxValue;

        foreach (var hole in holes)
        {
            var p = CollarPoint(hole);
            var d = (p.X - pos.X) * (p.X - pos.X) + (p.Y - pos.Y) * (p.Y - pos.Y);
            if (d < best)
            {
                best = d;
                nearest = hole;
            }
        }

        if (nearest is null || Math.Sqrt(best) > 22) return;

        SelectedHole = nearest;
        e.Handled = true;
    }
}
