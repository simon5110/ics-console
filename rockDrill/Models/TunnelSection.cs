using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;

namespace rockDrill.Models;

/// <summary>
/// 断面轮廓段。Type = 0 为直线段，Type = 1 为圆弧段。
/// 圆弧用「弦的两个端点 + 半径」描述，与 CollectData 中
/// <c>ContourChecker</c> / <c>FormCollect.DrawContour</c> 的轮廓数据格式一致：
/// <c>{type, x1, z1, x2, z2, R}</c>。
/// 坐标：x 横向（向右为正），y 竖向（向上为正），单位 m。
/// </summary>
public readonly record struct SectionSegment(int Type, double X1, double Y1, double X2, double Y2, double Radius)
{
    public bool IsArc => Type != 0;

    /// <summary>圆弧圆心（取法与 CollectData 的 DrawContour 一致）。</summary>
    public (double X, double Y) ArcCenter
    {
        get
        {
            var mx = (X1 + X2) / 2;
            var my = (Y1 + Y2) / 2;
            var dx = X2 - X1;
            var dy = Y2 - Y1;
            var d = Math.Sqrt(dx * dx + dy * dy);
            if (d < 1e-9 || Radius <= 0) return (mx, my);

            var h = Math.Sqrt(Math.Max(0, Radius * Radius - d * d / 4));
            return (mx + h * dy / d, my - h * dx / d);
        }
    }

    /// <summary>圆弧起止角（顺时针，th1 &gt;= th &gt;= th2）。</summary>
    public (double Th1, double Th2) ArcAngles
    {
        get
        {
            var (cx, cy) = ArcCenter;
            var th1 = Math.Atan2(Y1 - cy, X1 - cx);
            var th2 = Math.Atan2(Y2 - cy, X2 - cx);
            if (th2 > th1) th2 -= 2 * Math.PI;
            return (th1, th2);
        }
    }
}

/// <summary>
/// 隧道掌子面断面轮廓。
/// 支持两种来源：内置半圆拱（两侧直墙 + 拱顶半圆）与外部轮廓线（直线段 + 圆弧段）。
/// 提供轮廓离散、点在断面内判定、到轮廓距离/最近点等几何功能，
/// 对应 CollectData 中 <c>ContourChecker</c> 与 <c>FormCollect</c> 的轮廓处理逻辑。
/// 约定：断面底面 y = 0 附近，x 以断面中心为 0（加载外部轮廓时由 <see cref="FromSegments"/> 统一归一化）。
/// </summary>
public sealed class TunnelSection
{
    private readonly List<SectionSegment> _segments;
    private readonly List<Point> _polyline;

    private TunnelSection(List<SectionSegment> segments, bool isStandardArch, double wallHeight)
    {
        _segments = segments;
        IsStandardArch = isStandardArch;
        WallHeight = wallHeight;
        _polyline = BuildPolyline(segments);
        if (_polyline.Count < 3) throw new ArgumentException("轮廓点不足", nameof(segments));

        MinX = _polyline.Min(p => p.X);
        MaxX = _polyline.Max(p => p.X);
        MinY = _polyline.Min(p => p.Y);
        MaxY = _polyline.Max(p => p.Y);
        Span = MaxX - MinX;
        Height = MaxY - MinY;
        ArchRadius = IsStandardArch ? Span / 2 : Math.Max(0, Height - wallHeight);
    }

    /// <summary>跨度（两帮净宽）/ m。</summary>
    public double Span { get; }

    /// <summary>直墙高度 / m（非标准拱为估算值）。</summary>
    public double WallHeight { get; }

    /// <summary>拱部半径 / m（半圆拱 = 跨度的一半）。</summary>
    public double ArchRadius { get; }

    /// <summary>断面总高 / m。</summary>
    public double Height { get; }

    public double MinX { get; }
    public double MaxX { get; }
    public double MinY { get; }
    public double MaxY { get; }

    /// <summary>是否内置半圆拱（决定是否绘制拱脚线等辅助要素）。</summary>
    public bool IsStandardArch { get; }

    public IReadOnlyList<SectionSegment> Segments => _segments;

    /// <summary>半圆拱断面：两侧直墙 + 拱顶半圆，底面在 y = 0。</summary>
    public static TunnelSection CreateArch(double span, double wallHeight)
    {
        var r = span / 2;
        var segments = new List<SectionSegment>
        {
            new(0, -r, 0, -r, wallHeight, 0),
            new(1, -r, wallHeight, 0, wallHeight + r, r),
            new(1, 0, wallHeight + r, r, wallHeight, r),
            new(0, r, wallHeight, r, 0, 0),
            new(0, r, 0, -r, 0, 0),
        };
        return new TunnelSection(segments, true, wallHeight);
    }

    /// <summary>
    /// 由外部轮廓段（例如 doc/Gradder 下的 Arcs.csv / 轮廓线.csv）创建断面，
    /// 并把坐标归一化到「底面 y = 0、水平方向以断面中心为 0」。
    /// </summary>
    public static TunnelSection FromSegments(IReadOnlyList<SectionSegment> segments)
    {
        if (segments.Count < 2) throw new ArgumentException("轮廓段不足", nameof(segments));

        var probe = BuildPolyline(segments);
        var cx = (probe.Min(p => p.X) + probe.Max(p => p.X)) / 2;
        var fy = probe.Min(p => p.Y);

        var normalized = segments
            .Select(s => new SectionSegment(s.Type, s.X1 - cx, s.Y1 - fy, s.X2 - cx, s.Y2 - fy, s.Radius))
            .ToList();

        var wall = Math.Max(0, normalized.Max(s => Math.Max(s.Y1, s.Y2)) - Math.Abs(probe.Max(p => p.X) - cx));

        return new TunnelSection(normalized, false, Math.Round(wall, 3));
    }

    /// <summary>
    /// 由闭合折线（例如 CSV 轮廓线离散后的点列）创建断面，
    /// 并把坐标归一化到「底面 y = 0、水平方向以断面中心为 0」。
    /// </summary>
    public static TunnelSection FromPolygon(IReadOnlyList<Point> polygon)
    {
        if (polygon.Count < 3) throw new ArgumentException("轮廓点不足", nameof(polygon));

        var cx = (polygon.Min(p => p.X) + polygon.Max(p => p.X)) / 2;
        var fy = polygon.Min(p => p.Y);

        var segments = new List<SectionSegment>(polygon.Count);
        for (var i = 0; i < polygon.Count; i++)
        {
            var a = polygon[i];
            var b = polygon[(i + 1) % polygon.Count];
            if (Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9) continue;
            segments.Add(new SectionSegment(0, a.X - cx, a.Y - fy, b.X - cx, b.Y - fy, 0));
        }

        return new TunnelSection(segments, false, 0);
    }

    /// <summary>把轮廓离散成首尾相接的折线（末尾不重复首点）。</summary>
    private static List<Point> BuildPolyline(IReadOnlyList<SectionSegment> segments, int arcSegmentsPerArc = 36)
    {
        var points = new List<Point>();

        foreach (var seg in segments)
        {
            if (!seg.IsArc)
            {
                if (points.Count == 0) points.Add(new Point(seg.X1, seg.Y1));
                points.Add(new Point(seg.X2, seg.Y2));
                continue;
            }

            var (cx, cy) = seg.ArcCenter;
            var (th1, th2) = seg.ArcAngles;

            if (points.Count == 0) points.Add(new Point(seg.X1, seg.Y1));
            for (var i = 1; i <= arcSegmentsPerArc; i++)
            {
                var th = th1 + (th2 - th1) * i / arcSegmentsPerArc;
                points.Add(new Point(cx + seg.Radius * Math.Cos(th), cy + seg.Radius * Math.Sin(th)));
            }
        }

        return points;
    }

    /// <summary>把轮廓离散成首尾相接的折线（末尾不重复首点）。</summary>
    public List<Point> ToPolyline() => new(_polyline);

    /// <summary>
    /// 以 (<paramref name="cx"/>, <paramref name="cy"/>) 为中心按比例缩放断面，
    /// 圆弧半径同步缩放，形状类型（半圆拱 / 轮廓线）保持不变。
    /// </summary>
    public TunnelSection ScaleAbout(double k, double cx, double cy)
        => ScaleAbout(k, k, cx, cy);

    /// <summary>
    /// 以 (<paramref name="cx"/>, <paramref name="cy"/>) 为中心做横 / 竖不等比缩放，
    /// 用于让轮廓贴合炮孔包络；圆弧半径取两个方向的平均值近似。
    /// </summary>
    public TunnelSection ScaleAbout(double kx, double ky, double cx, double cy)
    {
        var kr = (kx + ky) / 2;

        var segments = _segments.Select(s => new SectionSegment(
            s.Type,
            cx + (s.X1 - cx) * kx, cy + (s.Y1 - cy) * ky,
            cx + (s.X2 - cx) * kx, cy + (s.Y2 - cy) * ky,
            s.Radius * kr)).ToList();

        return new TunnelSection(segments, IsStandardArch, WallHeight * ky);
    }

    /// <summary>把断面整体平移（用于重新归一化到底面 y = 0、中线 x = 0）。</summary>
    public TunnelSection Translate(double dx, double dy)
    {
        var segments = _segments.Select(s => new SectionSegment(
            s.Type, s.X1 - dx, s.Y1 - dy, s.X2 - dx, s.Y2 - dy, s.Radius)).ToList();

        return new TunnelSection(segments, IsStandardArch, WallHeight);
    }

    /// <summary>
    /// 生成向内缩进 <paramref name="inset"/> 米的「环」折线：
    /// 左帮（底 → 拱脚）→ 拱顶半圆 → 右帮（拱脚 → 底）。
    /// 仅用于半圆拱断面；外部轮廓直接使用炮孔数据。
    /// </summary>
    public List<Point> InsetRingPolyline(double inset)
    {
        var r = Math.Max(0.05, ArchRadius - inset);
        var wallTop = WallHeight;
        var bottom = Math.Min(inset, wallTop);

        var points = new List<Point> { new(-r, bottom) };
        if (wallTop > bottom + 1e-6) points.Add(new Point(-r, wallTop));

        const int arcSegments = 40;
        for (var i = 0; i <= arcSegments; i++)
        {
            var th = Math.PI - Math.PI * i / arcSegments;
            var p = new Point(r * Math.Cos(th), wallTop + r * Math.Sin(th));
            if (points.Count > 0 && NearlySame(points[^1], p)) continue;
            points.Add(p);
        }

        if (wallTop > bottom + 1e-6) points.Add(new Point(r, bottom));

        return points;
    }

    /// <summary>点在断面内（射线法，直线段与圆弧段分别处理）。</summary>
    public bool Contains(double px, double py)
    {
        var count = 0;

        foreach (var seg in _segments)
        {
            if (!seg.IsArc)
            {
                double x1 = seg.X1, y1 = seg.Y1, x2 = seg.X2, y2 = seg.Y2;
                if ((y1 <= py && y2 > py) || (y2 <= py && y1 > py))
                {
                    var t = (py - y1) / (y2 - y1);
                    var xIntersect = x1 + t * (x2 - x1);
                    if (xIntersect > px) count++;
                }
                continue;
            }

            var (cx, cy) = seg.ArcCenter;
            var (th1, th2) = seg.ArcAngles;
            var radius = seg.Radius;

            var dyc = py - cy;
            if (Math.Abs(dyc) > radius) continue;

            var dxr = Math.Sqrt(Math.Max(0, radius * radius - dyc * dyc));
            double[] xs = { cx - dxr, cx + dxr };

            foreach (var xIntersect in xs)
            {
                if (xIntersect <= px) continue;
                var thIntersect = Math.Atan2(py - cy, xIntersect - cx);
                if (thIntersect > th1) thIntersect -= 2 * Math.PI;
                if (th1 >= thIntersect && thIntersect >= th2) count++;
            }
        }

        return count % 2 == 1;
    }

    /// <summary>点到轮廓的最短距离 / m。</summary>
    public double DistanceToOutline(double px, double py)
    {
        var min = double.MaxValue;
        foreach (var seg in _segments)
        {
            var p = NearestPoint(px, py, seg);
            var dx = p.X - px;
            var dy = p.Y - py;
            var d = dx * dx + dy * dy;
            if (d < min) min = d;
        }
        return Math.Sqrt(min);
    }

    /// <summary>轮廓上离给定点最近的点。</summary>
    public Point NearestPointOnOutline(double px, double py)
    {
        var best = new Point(px, py);
        var min = double.MaxValue;

        foreach (var seg in _segments)
        {
            var p = NearestPoint(px, py, seg);
            var d = (p.X - px) * (p.X - px) + (p.Y - py) * (p.Y - py);
            if (d < min)
            {
                min = d;
                best = p;
            }
        }

        return best;
    }

    /// <summary>断面上某点的「向外法线」（由孔口指向轮廓外侧）。</summary>
    public (double X, double Y) OutwardDirection(double px, double py)
    {
        var nearest = NearestPointOnOutline(px, py);
        var dx = nearest.X - px;
        var dy = nearest.Y - py;
        var len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-6) return (0, 1);
        return (dx / len, dy / len);
    }

    private static Point NearestPoint(double px, double py, SectionSegment seg)
    {
        if (!seg.IsArc)
        {
            double x1 = seg.X1, y1 = seg.Y1, x2 = seg.X2, y2 = seg.Y2;
            var vx = x2 - x1;
            var vy = y2 - y1;
            var c1 = vx * (px - x1) + vy * (py - y1);
            if (c1 <= 0) return new Point(x1, y1);

            var c2 = vx * vx + vy * vy;
            if (c2 <= c1) return new Point(x2, y2);

            var t = c1 / c2;
            return new Point(x1 + t * vx, y1 + t * vy);
        }

        var (cx, cy) = seg.ArcCenter;
        var (th1, th2) = seg.ArcAngles;

        var th = Math.Atan2(py - cy, px - cx);
        if (th > th1) th -= 2 * Math.PI;

        if (th1 >= th && th >= th2)
            return new Point(cx + seg.Radius * Math.Cos(th), cy + seg.Radius * Math.Sin(th));

        var d1 = (px - seg.X1) * (px - seg.X1) + (py - seg.Y1) * (py - seg.Y1);
        var d2 = (px - seg.X2) * (px - seg.X2) + (py - seg.Y2) * (py - seg.Y2);
        return d1 <= d2 ? new Point(seg.X1, seg.Y1) : new Point(seg.X2, seg.Y2);
    }

    /// <summary>按给定间距沿折线等距取样（含首尾点）。</summary>
    public static List<Point> Resample(IReadOnlyList<Point> path, double spacing)
    {
        var result = new List<Point>();
        if (path.Count == 0) return result;
        if (path.Count == 1 || spacing <= 1e-6)
        {
            result.AddRange(path);
            return result;
        }

        var cumulative = new double[path.Count];
        for (var i = 1; i < path.Count; i++)
        {
            var dx = path[i].X - path[i - 1].X;
            var dy = path[i].Y - path[i - 1].Y;
            cumulative[i] = cumulative[i - 1] + Math.Sqrt(dx * dx + dy * dy);
        }

        var total = cumulative[^1];
        if (total < 1e-6)
        {
            result.Add(path[0]);
            return result;
        }

        var count = Math.Max(2, (int)Math.Round(total / spacing) + 1);
        var step = total / (count - 1);
        var j = 1;

        for (var i = 0; i < count; i++)
        {
            var s = i * step;
            while (j < path.Count - 1 && cumulative[j] < s) j++;

            var segLength = cumulative[j] - cumulative[j - 1];
            var t = segLength < 1e-9 ? 0 : (s - cumulative[j - 1]) / segLength;

            result.Add(new Point(
                path[j - 1].X + (path[j].X - path[j - 1].X) * t,
                path[j - 1].Y + (path[j].Y - path[j - 1].Y) * t));
        }

        return result;
    }

    private static bool NearlySame(Point a, Point b)
        => Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6;
}
