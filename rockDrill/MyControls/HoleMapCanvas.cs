using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using rockDrill.ViewModels;

namespace rockDrill.MyControls;

/// <summary>
/// 掌子面炮孔布置图：断面轮廓 + 炮孔点位 + 选中孔高亮。
/// 内部使用 800×560 的设计坐标，外部可用 Viewbox 缩放。
/// </summary>
public class HoleMapCanvas : Control
{
    public static readonly StyledProperty<IEnumerable<HoleVm>?> HolesProperty =
        AvaloniaProperty.Register<HoleMapCanvas, IEnumerable<HoleVm>?>(nameof(Holes));

    public static readonly StyledProperty<HoleVm?> SelectedHoleProperty =
        AvaloniaProperty.Register<HoleMapCanvas, HoleVm?>(nameof(SelectedHole));

    public static readonly StyledProperty<IBrush> OutlineBrushProperty =
        AvaloniaProperty.Register<HoleMapCanvas, IBrush>(nameof(OutlineBrush),
            new SolidColorBrush(Color.Parse("#2E4A60")));

    public static readonly StyledProperty<IBrush> GuideBrushProperty =
        AvaloniaProperty.Register<HoleMapCanvas, IBrush>(nameof(GuideBrush),
            new SolidColorBrush(Color.Parse("#1B2C38")));

    private const double MapWidth = 800;
    private const double MapHeight = 560;

    static HoleMapCanvas()
    {
        AffectsRender<HoleMapCanvas>(HolesProperty, SelectedHoleProperty, OutlineBrushProperty, GuideBrushProperty);
    }

    public IEnumerable<HoleVm>? Holes { get => GetValue(HolesProperty); set => SetValue(HolesProperty, value); }
    public HoleVm? SelectedHole { get => GetValue(SelectedHoleProperty); set => SetValue(SelectedHoleProperty, value); }
    public IBrush OutlineBrush { get => GetValue(OutlineBrushProperty); set => SetValue(OutlineBrushProperty, value); }
    public IBrush GuideBrush { get => GetValue(GuideBrushProperty); set => SetValue(GuideBrushProperty, value); }

    public override void Render(DrawingContext context)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        if (w <= 4 || h <= 4) return;

        var scale = Math.Min(w / MapWidth, h / MapHeight);
        var offsetX = (w - MapWidth * scale) / 2;
        var offsetY = (h - MapHeight * scale) / 2;

        using var _ = context.PushTransform(Matrix.CreateScale(scale, scale) *
                                            Matrix.CreateTranslation(offsetX / scale, offsetY / scale));

        // ===== 断面轮廓（拱形）=====
        var outline = new StreamGeometry();
        using (var ctx = outline.Open())
        {
            ctx.BeginFigure(new Point(40, 530), false);
            ctx.LineTo(new Point(40, 130));
            ctx.ArcTo(new Point(140, 30), new Size(100, 100), 0, false, SweepDirection.Clockwise);
            ctx.LineTo(new Point(660, 30));
            ctx.ArcTo(new Point(760, 130), new Size(100, 100), 0, false, SweepDirection.Clockwise);
            ctx.LineTo(new Point(760, 530));
            ctx.EndFigure(false);
        }
        // 先画内侧阴影带，再画轮廓亮线
        context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.Parse("#0E1A22")), 8), outline);
        context.DrawGeometry(null, new Pen(OutlineBrush, 1.6), outline);

        // ===== 中线与水平基准 =====
        var guidePen = new Pen(GuideBrush, 1)
        {
            DashStyle = new DashStyle(new double[] { 6, 6 }, 0)
        };
        context.DrawLine(guidePen, new Point(400, 30), new Point(400, 530));
        context.DrawLine(guidePen, new Point(40, 280), new Point(760, 280));

        // ===== 炮孔 =====
        var holes = Holes;
        if (holes is null) return;

        foreach (var hole in holes)
        {
            var radius = hole.Size / 2;
            var center = new Point(hole.CenterX, hole.CenterY);

            // 已完成孔淡淡的扩散环
            if (hole.StateText == "已完成")
            {
                context.DrawEllipse(new SolidColorBrush(Color.Parse("#0F2AD07C")), null, center, radius + 4, radius + 4);
            }

            context.DrawEllipse(hole.Fill, new Pen(hole.Stroke, hole.StrokeThickness), center, radius, radius);

            var text = new FormattedText(hole.NoText, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Consolas"), 10, new SolidColorBrush(Color.Parse("#C9DEEC")))
            {
                MaxTextWidth = radius * 2 + 6,
                TextAlignment = TextAlignment.Center
            };
            context.DrawText(text, new Point(center.X - radius - 3, center.Y - text.Height / 2));
        }

        // ===== 当前孔高亮 =====
        var selected = SelectedHole;
        if (selected is not null)
        {
            var c = new Point(selected.CenterX, selected.CenterY);
            var accent = new SolidColorBrush(Color.Parse("#23A9F0"));
            context.DrawEllipse(null, new Pen(accent, 1.6), c, 18, 18);
            context.DrawEllipse(null, new Pen(new SolidColorBrush(Color.Parse("#4023A9F0")), 1), c, 24, 24);

            var crossPen = new Pen(new SolidColorBrush(Color.Parse("#8023A9F0")), 1);
            context.DrawLine(crossPen, new Point(c.X - 26, c.Y), new Point(c.X - 20, c.Y));
            context.DrawLine(crossPen, new Point(c.X + 20, c.Y), new Point(c.X + 26, c.Y));
            context.DrawLine(crossPen, new Point(c.X, c.Y - 26), new Point(c.X, c.Y - 20));
            context.DrawLine(crossPen, new Point(c.X, c.Y + 20), new Point(c.X, c.Y + 26));
        }
    }
}
