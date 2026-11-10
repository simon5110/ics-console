using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;

namespace rockDrill.Models;

/// <summary>炮孔用途分类（与现场布孔图的四类一致）。</summary>
public enum HoleKind
{
    Cut,
    Aux,
    Perimeter,
    Floor
}

/// <summary>CSV 中的一条炮孔记录。坐标为掌子面坐标：X 横向、Z 竖向、Y 沿孔轴（向里为正）。</summary>
public sealed class HolePoint
{
    public int No { get; init; }
    public string RawType { get; init; } = string.Empty;
    public HoleKind Kind { get; init; }

    public double X { get; init; }
    public double Z { get; init; }
    public double Depth { get; init; }

    /// <summary>孔向单位向量（面内投影 DirX / DirZ，沿孔轴 DirY）。</summary>
    public double DirX { get; init; }
    public double DirY { get; init; } = 1;
    public double DirZ { get; init; }

    /// <summary>孔向相对掌子面法线的倾角 / °。</summary>
    public double TiltDeg { get; init; }

    public double DiameterMm { get; init; } = 45;

    public string KindText => Kind switch
    {
        HoleKind.Cut => "掏槽孔",
        HoleKind.Aux => "辅助孔",
        HoleKind.Perimeter => "周边孔",
        _ => "底板孔"
    };
}

/// <summary>可加载的布孔数据集（doc/Gradder 下的一个目录或一组文件）。</summary>
public sealed class HoleDatasetInfo
{
    public string Name { get; init; } = string.Empty;
    public string Folder { get; init; } = string.Empty;
    public string HoleFile { get; init; } = string.Empty;
    public string? ContourFile { get; init; }

    public override string ToString() => Name;
}

/// <summary>已解析的布孔数据集：断面轮廓 + 炮孔点位。</summary>
public sealed class HoleDataset
{
    public required HoleDatasetInfo Info { get; init; }
    public required TunnelSection Section { get; init; }
    public required IReadOnlyList<HolePoint> Holes { get; init; }
    public string SectionSourceText { get; init; } = "按孔位估算";
    public int SkippedRows { get; init; }
}

/// <summary>
/// 布孔 CSV 读取器。支持 doc/Gradder 下两种现场数据格式：
/// 1) <c>ManyHoles.csv</c>：序号,类型,startX,startY,startZ,endX,endY,endZ,孔深,外偏角,角度,回转角度
///    （类型 S/D/V/V1/V2/C1/C2/TopC1/H，机身导出格式）；
/// 2) <c>*炮孔布置*.csv</c>：同一列布局，类型为 REFERENCE_POINT/BORDER/BORDER_PADDING/AID_TOP/AID_SPREADER/BORDER_BOTTOM/CUT。
/// 轮廓来自同目录的 <c>Arcs.csv</c>（直线/顺时针圆弧段）或 <c>*轮廓线*.csv</c>（点 + 半径）。
/// </summary>
public static class HoleDatasetLibrary
{
    private static readonly Dictionary<string, HoleKind> KindMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["BORDER"] = HoleKind.Perimeter,
        ["C1"] = HoleKind.Perimeter,
        ["周边孔"] = HoleKind.Perimeter,
        ["BORDER_BOTTOM"] = HoleKind.Floor,
        ["D"] = HoleKind.Floor,
        ["底板孔"] = HoleKind.Floor,
        ["CUT"] = HoleKind.Cut,
        ["S"] = HoleKind.Cut,
        ["掏槽孔"] = HoleKind.Cut,
    };

    /// <summary>定位 doc/Gradder 目录（从程序目录向上查找，或使用环境变量）。</summary>
    public static string? FindRoot()
    {
        var fromEnv = Environment.GetEnvironmentVariable("ROCKDRILL_GRADDER_DIR");
        if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv)) return fromEnv;

        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var dir = new DirectoryInfo(start);
            for (var i = 0; i < 8 && dir is not null; i++)
            {
                var candidate = Path.Combine(dir.FullName, "doc", "Gradder");
                if (Directory.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }
        }

        return null;
    }

    /// <summary>扫描可用的布孔数据集。</summary>
    public static List<HoleDatasetInfo> Discover(string? root = null)
    {
        root ??= FindRoot();
        var result = new List<HoleDatasetInfo>();
        if (root is null || !Directory.Exists(root)) return result;

        var folders = Directory.GetDirectories(root).Append(root);
        foreach (var folder in folders)
        {
            var many = Path.Combine(folder, "ManyHoles.csv");
            if (File.Exists(many))
            {
                result.Add(new HoleDatasetInfo
                {
                    Name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar)),
                    Folder = folder,
                    HoleFile = many,
                    ContourFile = FindContourFile(folder)
                });
                continue;
            }

            var layout = Directory.EnumerateFiles(folder, "*炮孔布置*.csv").FirstOrDefault();
            if (layout is null) continue;

            result.Add(new HoleDatasetInfo
            {
                Name = Path.GetFileNameWithoutExtension(layout),
                Folder = folder,
                HoleFile = layout,
                ContourFile = FindContourFile(folder)
            });
        }

        return result.OrderBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static string? FindContourFile(string folder)
    {
        var arcs = Path.Combine(folder, "Arcs.csv");
        if (File.Exists(arcs)) return arcs;

        return Directory.EnumerateFiles(folder, "*轮廓线*.csv").FirstOrDefault();
    }

    /// <summary>加载数据集：解析炮孔 + 轮廓，并统一归一化到「底面 y=0、断面中心 x=0」。</summary>
    public static HoleDataset Load(HoleDatasetInfo info)
    {
        var rows = ReadRows(info.HoleFile);
        var holes = new List<HolePoint>();
        var skipped = 0;

        foreach (var row in rows)
        {
            if (row.Length < 8) { skipped++; continue; }

            var rawType = row[1].Trim();
            if (rawType.Equals("REFERENCE_POINT", StringComparison.OrdinalIgnoreCase))
            {
                skipped++;
                continue;
            }

            if (!TryNum(row[2], out var sx) || !TryNum(row[4], out var sz)) { skipped++; continue; }

            var ex = TryNum(row[5], out var v5) ? v5 : sx;
            var ey = TryNum(row[6], out var v6) ? v6 : 3.4;
            var ez = TryNum(row[7], out var v7) ? v7 : sz;

            var depth = row.Length > 8 && TryNum(row[8], out var d) && d > 0.01
                ? d
                : Math.Sqrt((ex - sx) * (ex - sx) + (ey - 0) * (ey - 0) + (ez - sz) * (ez - sz));

            var (dx, dy, dz) = Normalize(ex - sx, ey - 0, ez - sz);
            if (dy < 0) { dx = -dx; dy = -dy; dz = -dz; }

            var tilt = Math.Acos(Math.Clamp(Math.Abs(dy), -1, 1)) * 180.0 / Math.PI;

            holes.Add(new HolePoint
            {
                No = row.Length > 0 && int.TryParse(row[0].Trim(), out var no) ? no : holes.Count + 1,
                RawType = rawType,
                Kind = KindMap.TryGetValue(rawType, out var kind) ? kind : HoleKind.Aux,
                X = sx,
                Z = sz,
                Depth = Math.Round(depth, 3),
                DirX = dx,
                DirY = dy,
                DirZ = dz,
                TiltDeg = Math.Round(tilt, 2)
            });
        }

        var contour = info.ContourFile is null ? null : TryLoadContour(info.ContourFile);

        if (contour is not null && contour.Count >= 3 && holes.Count > 0)
        {
            // 轮廓与炮孔统一归一化（同一平移量，保持相对关系）
            var minX = contour.Min(p => p.X);
            var maxX = contour.Max(p => p.X);
            var minY = contour.Min(p => p.Y);
            var cx = (minX + maxX) / 2;

            var shifted = contour.Select(p => new Point(p.X - cx, p.Y - minY)).ToList();

            TunnelSection? section = null;
            try
            {
                section = TunnelSection.FromPolygon(shifted);
            }
            catch
            {
                section = null;   // 轮廓数据退化时回退到「按孔位估算」
            }

            if (section is not null)
            {
                var shiftedHoles = holes.Select(h => Shift(h, cx, minY)).ToList();

                // 部分数据集（如 00-39孔）的孔位使用「钻机局部坐标」而非轮廓坐标，
                // 此时先把孔位对齐到断面中线 / 底板，再判断是否需要包络
                var alignedHoles = AlignPatternFrame(section, shiftedHoles);

                // 设计轮廓可能小于实际布孔范围（加宽 / 超挖布孔），贴合到包住全部孔位
                var (fitted, fittedHoles, expanded) = EncloseAllHoles(section, alignedHoles);

                return new HoleDataset
                {
                    Info = info,
                    Section = fitted,
                    Holes = fittedHoles,
                    SectionSourceText = (Path.GetFileName(info.ContourFile!) ?? "轮廓文件")
                                        + (expanded ? " · 轮廓已按孔位贴合" : string.Empty),
                    SkippedRows = skipped
                };
            }
        }

        // 无轮廓文件：按孔位范围估算一个半圆拱断面
        if (holes.Count == 0)
        {
            return new HoleDataset
            {
                Info = info,
                Section = TunnelSection.CreateArch(12.40, 3.00),
                Holes = holes,
                SectionSourceText = "空数据集（默认断面）",
                SkippedRows = skipped
            };
        }

        var hMinX = holes.Min(h => h.X);
        var hMaxX = holes.Max(h => h.X);
        var hMinZ = holes.Min(h => h.Z);
        var hMaxZ = holes.Max(h => h.Z);

        var span = Math.Max(2.0, hMaxX - hMinX + 1.6);
        var height = Math.Max(2.0, hMaxZ - hMinZ + 1.0);
        var wall = Math.Max(0.4, height - span / 2);

        var arch = TunnelSection.CreateArch(Math.Round(span, 2), Math.Round(wall, 2));
        var centerX = (hMinX + hMaxX) / 2;
        var floorZ = hMinZ - 0.45;

        // 估算的拱形也可能漏掉角落处的孔（边角超挖），同样外扩到包住全部孔位
        var (archFitted, archHoles, archExpanded) = EncloseAllHoles(
            arch, holes.Select(h => Shift(h, centerX, floorZ)).ToList());

        return new HoleDataset
        {
            Info = info,
            Section = archFitted,
            Holes = archHoles,
            SectionSourceText = "按孔位估算（无轮廓文件）"
                                + (archExpanded ? " · 轮廓已按孔位贴合" : string.Empty),
            SkippedRows = skipped
        };
    }

    /// <summary>
    /// 当布孔范围明显小于断面（部分炮孔 / 钻机局部坐标），把布孔图对齐到断面中线与底板：
    /// 水平居中、竖向坐落在底板上方。整面布孔（占断面 60% 以上）保持原始相对位置。
    /// </summary>
    private static List<HolePoint> AlignPatternFrame(TunnelSection section, List<HolePoint> holes)
    {
        if (holes.Count == 0) return holes;

        var hMinX = holes.Min(h => h.X);
        var hMaxX = holes.Max(h => h.X);
        var hMinY = holes.Min(h => h.Z);
        var hMaxY = holes.Max(h => h.Z);

        var offsetX = hMaxX - hMinX < section.Span * 0.6
            ? (section.MinX + section.MaxX) / 2 - (hMinX + hMaxX) / 2
            : 0.0;

        var offsetY = hMaxY - hMinY < section.Height * 0.6
            ? section.MinY + 0.15 - hMinY
            : 0.0;

        if (Math.Abs(offsetX) < 1e-6 && Math.Abs(offsetY) < 1e-6) return holes;

        return holes.Select(h => Shift(h, -offsetX, -offsetY)).ToList();
    }

    /// <summary>
    /// 若存在落在轮廓之外的炮孔（加宽 / 超挖布孔），把轮廓重新贴合到炮孔包络上：
    /// 以「孔位包围盒 + 小余量」为目标框做横 / 竖缩放（半圆拱则按目标框重建），
    /// 保证周边孔紧贴轮廓、底板孔略高于轮廓底边。改造后轮廓重新归一化到
    /// 「底面 y = 0、中线 x = 0」，炮孔同步平移，孔距与相对位置不变。
    /// </summary>
    private static (TunnelSection Section, List<HolePoint> Holes, bool Adjusted) EncloseAllHoles(
        TunnelSection section, List<HolePoint> holes)
    {
        if (holes.Count == 0) return (section, holes, false);

        bool AllInside(TunnelSection s)
        {
            foreach (var h in holes)
            {
                if (!s.Contains(h.X, h.Z)) return false;
            }
            return true;
        }

        // 设计轮廓已经包住全部孔位时保持原样
        if (AllInside(section)) return (section, holes, false);

        var hMinX = holes.Min(h => h.X);
        var hMaxX = holes.Max(h => h.X);
        var hMinY = holes.Min(h => h.Z);
        var hMaxY = holes.Max(h => h.Z);

        var cxS = (section.MinX + section.MaxX) / 2;
        var cyS = (section.MinY + section.MaxY) / 2;

        const double startMargin = 0.3;
        var sizeHint = Math.Max(hMaxX - hMinX, hMaxY - hMinY);
        var margin = Math.Max(startMargin, sizeHint * 0.03);

        TunnelSection? fitted = null;

        for (var attempt = 0; attempt < 12; attempt++)
        {
            // 目标框 = 设计轮廓 ∪（孔位包围盒 + 余量）：只放大不缩小，
            // 保证断面不会被压成孔位包络那么大。
            var tMinX = Math.Min(section.MinX, hMinX - margin);
            var tMaxX = Math.Max(section.MaxX, hMaxX + margin);
            var tMinY = Math.Min(section.MinY, hMinY - margin);
            var tMaxY = Math.Max(section.MaxY, hMaxY + margin);
            var tCx = (tMinX + tMaxX) / 2;
            var tCy = (tMinY + tMaxY) / 2;

            TunnelSection candidate;

            if (section.IsStandardArch)
            {
                // 半圆拱：直接按目标框重建，保持圆弧形状
                var span = Math.Max(1.0, tMaxX - tMinX);
                var height = Math.Max(1.0, tMaxY - tMinY);
                var wall = Math.Max(0.3, height - span / 2);
                candidate = TunnelSection.CreateArch(span, wall).Translate(-tCx, -tMinY);
            }
            else
            {
                var kx = (tMaxX - tMinX) / Math.Max(0.1, section.Span);
                var ky = (tMaxY - tMinY) / Math.Max(0.1, section.Height);
                candidate = section.ScaleAbout(kx, ky, cxS, cyS)
                                    .Translate(cxS - tCx, cyS - tCy);
            }

            fitted = candidate;
            if (AllInside(candidate)) break;

            margin += 0.12;   // 形状与孔位包络不吻合时逐步放大目标框
        }

        if (fitted is null || !AllInside(fitted)) return (section, holes, false);

        // 重新归一化：中线 x = 0、底面 y = 0（纯平移，保持形状与孔距）
        var newCenterX = (fitted.MinX + fitted.MaxX) / 2;
        var newMinY = fitted.MinY;
        var normalized = fitted.Translate(newCenterX, newMinY);
        var shiftedHoles = holes.Select(h => Shift(h, newCenterX, newMinY)).ToList();

        return (normalized, shiftedHoles, true);
    }

    private static HolePoint Shift(HolePoint h, double dx, double dy) => new()
    {
        No = h.No,
        RawType = h.RawType,
        Kind = h.Kind,
        X = h.X - dx,
        Z = h.Z - dy,
        Depth = h.Depth,
        DirX = h.DirX,
        DirY = h.DirY,
        DirZ = h.DirZ,
        TiltDeg = h.TiltDeg,
        DiameterMm = h.DiameterMm
    };

    private static List<string[]> ReadRows(string path)
    {
        var rows = new List<string[]>();
        foreach (var line in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            var cells = line.Trim().TrimStart('\uFEFF').Split(',');
            // 跳过表头（首列不是数字）
            if (!TryNum(cells[0], out _)) continue;

            rows.Add(cells);
        }
        return rows;
    }

    private static bool TryNum(string? text, out double value)
        => double.TryParse((text ?? string.Empty).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static (double X, double Y, double Z) Normalize(double x, double y, double z)
    {
        var len = Math.Sqrt(x * x + y * y + z * z);
        return len < 1e-9 ? (0, 1, 0) : (x / len, y / len, z / len);
    }

    /// <summary>读取轮廓文件并离散为闭合折线。解析失败返回 null。</summary>
    private static List<Point>? TryLoadContour(string path)
    {
        try
        {
            var name = Path.GetFileName(path);
            var isArcFile = name.Contains("Arcs", StringComparison.OrdinalIgnoreCase);

            var segments = isArcFile ? ParseArcFile(path) : ParseProfileFile(path);
            if (segments.Count < 2) return null;

            // Arcs.csv 里可能混有钻孔线段，只保留端点相连的最大线段簇作为轮廓
            var loop = LargestConnectedComponent(segments);
            if (loop.Count < 3) return null;

            var chain = Chain(loop);
            return chain.Count >= 3 ? chain : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Arcs.csv：类型(直线/顺时针/逆时针), x1, y1, x2, y2, R, ...</summary>
    private static List<SectionSegment> ParseArcFile(string path)
    {
        var segments = new List<SectionSegment>();
        foreach (var line in File.ReadAllLines(path))
        {
            var cells = line.Trim().Split(',');
            if (cells.Length < 6) continue;

            if (!TryNum(cells[1], out var x1) || !TryNum(cells[2], out var y1) ||
                !TryNum(cells[3], out var x2) || !TryNum(cells[4], out var y2))
                continue;

            TryNum(cells[5], out var r);

            // 用半径判定直线 / 圆弧：文件可能是 GBK 编码，直接匹配「直线」不一定成立
            var isArc = Math.Abs(r) > 1e-6;
            segments.Add(new SectionSegment(isArc ? 1 : 0, x1, y1, x2, y2, Math.Abs(r)));
        }
        return segments;
    }

    /// <summary>轮廓线.csv：startX, startY, startR —— 逐点连成段，半径取自起始行（0 = 直线）。</summary>
    private static List<SectionSegment> ParseProfileFile(string path)
    {
        var points = new List<(double X, double Y, double R)>();
        foreach (var line in File.ReadAllLines(path))
        {
            var cells = line.Trim().Split(',');
            if (cells.Length < 3) continue;
            if (!TryNum(cells[0], out var x) || !TryNum(cells[1], out var y)) continue;
            TryNum(cells[2], out var r);
            points.Add((x, y, Math.Abs(r)));
        }

        var segments = new List<SectionSegment>();
        for (var i = 0; i < points.Count; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Count];
            var isArc = a.R > 1e-6 && Math.Abs(b.X - a.X) + Math.Abs(b.Y - a.Y) > 1e-6;
            segments.Add(new SectionSegment(isArc ? 1 : 0, a.X, a.Y, b.X, b.Y, a.R));
        }
        return segments;
    }

    /// <summary>把各段离散后按端点首尾相接，组成闭合折线（不再保留精确圆弧）。</summary>
    private static List<Point> Chain(List<SectionSegment> segments)
    {        var parts = segments.Select(s => Flatten(s)).ToList();
        var chain = new List<Point>(parts[0]);
        var used = new HashSet<int> { 0 };
        var start = chain[0];

        while (used.Count < parts.Count)
        {
            var current = chain[^1];
            if (Distance(current, start) < 0.02 && used.Count > 2) break;

            var found = -1;
            var reverse = false;
            for (var i = 0; i < parts.Count; i++)
            {
                if (used.Contains(i)) continue;
                if (Distance(parts[i][0], current) < 0.02) { found = i; break; }
                if (Distance(parts[i][^1], current) < 0.02) { found = i; reverse = true; break; }
            }

            if (found < 0) break;

            used.Add(found);
            var part = new List<Point>(parts[found]);
            if (reverse) part.Reverse();
            chain.AddRange(part.Skip(1));
        }

        if (chain.Count > 3 && Distance(chain[^1], chain[0]) < 0.02) chain.RemoveAt(chain.Count - 1);
        return chain;
    }

    /// <summary>取端点相连的最大线段簇（轮廓通常由多段首尾相接组成，孤立的钻孔线段会被排除）。</summary>
    private static List<SectionSegment> LargestConnectedComponent(IReadOnlyList<SectionSegment> segments)
    {
        var n = segments.Count;
        var parent = new int[n];
        for (var i = 0; i < n; i++) parent[i] = i;

        int Find(int i) => parent[i] == i ? i : (parent[i] = Find(parent[i]));
        void Union(int a, int b)
        {
            a = Find(a);
            b = Find(b);
            if (a != b) parent[b] = a;
        }

        static bool Touch(double x1, double y1, double x2, double y2)
            => Math.Abs(x1 - x2) < 0.05 && Math.Abs(y1 - y2) < 0.05;

        for (var i = 0; i < n; i++)
        {
            for (var j = i + 1; j < n; j++)
            {
                var a = segments[i];
                var b = segments[j];
                if (Touch(a.X1, a.Y1, b.X1, b.Y1) || Touch(a.X1, a.Y1, b.X2, b.Y2) ||
                    Touch(a.X2, a.Y2, b.X1, b.Y1) || Touch(a.X2, a.Y2, b.X2, b.Y2))
                    Union(i, j);
            }
        }

        var best = Enumerable.Range(0, n)
            .GroupBy(Find)
            .OrderByDescending(g => g.Count())
            .First();

        return best.Select(i => segments[i]).ToList();
    }

    private static List<Point> Flatten(SectionSegment seg, int arcSegments = 24)
    {
        if (!seg.IsArc) return new List<Point> { new(seg.X1, seg.Y1), new(seg.X2, seg.Y2) };

        var (cx, cy) = seg.ArcCenter;
        var (th1, th2) = seg.ArcAngles;

        var points = new List<Point>(arcSegments + 1);
        for (var i = 0; i <= arcSegments; i++)
        {
            var th = th1 + (th2 - th1) * i / arcSegments;
            points.Add(new Point(cx + seg.Radius * Math.Cos(th), cy + seg.Radius * Math.Sin(th)));
        }
        return points;
    }

    private static double Distance(Point a, Point b)
        => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}
