using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;

namespace rockDrill.Models;

/// <summary>臂架机构参数（双臂凿岩台车，等效为回转 + 俯仰 + 伸缩 + 推进 + 凿岩补偿）。</summary>
public sealed class BoomSpec
{
    public string Name { get; init; } = "左臂";

    /// <summary>基座在断面坐标中的横向位置 / m。</summary>
    public double BaseX { get; init; }

    /// <summary>基座在断面坐标中的高度 / m。</summary>
    public double BaseZ { get; init; } = 1.27;

    /// <summary>基座到掌子面的距离（机身退距）/ m。</summary>
    public double WorkingDistance { get; init; } = 8.0;

    /// <summary>基本臂长（伸缩、推进全收时）/ m。</summary>
    public double BoomLength { get; init; } = 7.38;

    public double MaxStretch { get; init; } = 2.40;
    public double MaxFeed { get; init; } = 1.80;

    public double MaxYawDeg { get; init; } = 45.0;
    public double MinPitchDeg { get; init; } = -40.0;
    public double MaxPitchDeg { get; init; } = 58.0;

    /// <summary>凿岩补偿角上限（钻杆轴线与臂架轴线夹角）/ °。</summary>
    public double MaxCompDeg { get; init; } = 55.0;

    /// <summary>臂架包络半径 / m。</summary>
    public double BoomRadius { get; init; } = 0.35;

    /// <summary>臂架到断面轮廓的安全间隙 / m。</summary>
    public double Clearance { get; init; } = 0.25;

    public static BoomSpec Left(double workingDistance) => new()
    {
        Name = "左臂",
        BaseX = -1.16,
        BaseZ = 1.27,
        WorkingDistance = workingDistance
    };

    public static BoomSpec Right(double workingDistance) => new()
    {
        Name = "右臂",
        BaseX = 1.16,
        BaseZ = 1.27,
        WorkingDistance = workingDistance
    };
}

/// <summary>一组关节解（逆解结果）：回转、俯仰、伸缩、推进、凿岩补偿。</summary>
public readonly record struct BoomPose(double YawDeg, double PitchDeg, double Stretch, double Feed, double CompDeg);

/// <summary>单个炮孔的解算结果（逆解 + 正解校验 + 防碰撞）。</summary>
public sealed class HoleSolution
{
    public bool Reachable { get; init; }
    public string Error { get; init; } = string.Empty;
    public BoomPose Pose { get; init; }

    /// <summary>正解残差 / m（正解端点与孔口位置的偏差）。</summary>
    public double Residual { get; init; }

    public List<string> Collisions { get; init; } = new();

    public bool HasCollision => Collisions.Count > 0;
    public string CollisionText => Collisions.Count == 0 ? "无干涉" : string.Join("；", Collisions);

    public string PoseText => Reachable
        ? $"回转 {Pose.YawDeg:F1}° / 俯仰 {Pose.PitchDeg:F1}° / 伸缩 {Pose.Stretch:F2} m / 推进 {Pose.Feed:F2} m / 补偿 {Pose.CompDeg:F1}°"
        : Error;
}

/// <summary>
/// 单臂运动学与防碰撞。
/// 机构简化为：竖直回转轴（θ1）+ 臂架俯仰（θ2）+ 大臂伸缩（d3）+ 凿岩推进（d4，与臂架同轴），
/// 末端点为钻头位置；凿岩补偿角由腕部关节吸收臂架轴线与孔向的夹角。
/// 正解：P = B + u(θ1,θ2)·(L0 + d3 + d4)；逆解为其解析反解。
/// </summary>
public sealed class BoomSolver
{
    private const double Deg2Rad = Math.PI / 180.0;
    private const double Rad2Deg = 180.0 / Math.PI;

    private readonly TunnelSection _section;

    public BoomSolver(BoomSpec spec, TunnelSection section)
    {
        Spec = spec;
        _section = section;
    }

    public BoomSpec Spec { get; }

    /// <summary>臂架轴线单位向量（x 横向、y 沿孔轴、z 竖向）。</summary>
    private static (double X, double Y, double Z) Axis(BoomPose pose)
    {
        var yaw = pose.YawDeg * Deg2Rad;
        var pitch = pose.PitchDeg * Deg2Rad;
        return (Math.Sin(yaw) * Math.Cos(pitch), Math.Cos(yaw) * Math.Cos(pitch), Math.Sin(pitch));
    }

    /// <summary>正解：由关节值求钻头位置（掌子面坐标，Y = 0 为掌子面）。</summary>
    public (double X, double Y, double Z) Forward(BoomPose pose)
    {
        var (ux, uy, uz) = Axis(pose);
        var len = Spec.BoomLength + pose.Stretch + pose.Feed;

        return (Spec.BaseX + ux * len,
                -Spec.WorkingDistance + uy * len,
                Spec.BaseZ + uz * len);
    }

    /// <summary>逆解：由孔口位置与孔向求关节值。</summary>
    public bool TryInverse(HolePoint hole, out BoomPose pose, out string error)
    {
        pose = default;
        error = string.Empty;

        var bx = Spec.BaseX;
        var by = -Spec.WorkingDistance;
        var bz = Spec.BaseZ;

        var dx = hole.X - bx;
        var dy = 0 - by;
        var dz = hole.Z - bz;
        var len = Math.Sqrt(dx * dx + dy * dy + dz * dz);

        if (len < 1e-6)
        {
            error = "孔位与基座重合";
            return false;
        }

        var yaw = Math.Atan2(dx, dy) * Rad2Deg;
        var pitch = Math.Asin(Math.Clamp(dz / len, -1, 1)) * Rad2Deg;

        var need = len - Spec.BoomLength;
        var stretch = Math.Clamp(need, 0, Spec.MaxStretch);
        var feed = need - stretch;

        var (ux, uy, uz) = (dx / len, dy / len, dz / len);
        var cos = Math.Clamp(ux * hole.DirX + uy * hole.DirY + uz * hole.DirZ, -1, 1);
        var comp = Math.Acos(cos) * Rad2Deg;

        pose = new BoomPose(Math.Round(yaw, 2), Math.Round(pitch, 2),
            Math.Round(stretch, 3), Math.Round(feed, 3), Math.Round(comp, 2));

        if (Math.Abs(yaw) > Spec.MaxYawDeg)
            error = $"回转角超限（{yaw:F1}° / ±{Spec.MaxYawDeg:F0}°）";
        else if (pitch < Spec.MinPitchDeg || pitch > Spec.MaxPitchDeg)
            error = $"俯仰角超限（{pitch:F1}° / {Spec.MinPitchDeg:F0}~{Spec.MaxPitchDeg:F0}°）";
        else if (need < 0)
            error = $"距离过近（需缩回 {-need:F2} m）";
        else if (feed > Spec.MaxFeed)
            error = $"超出臂架行程（缺 {feed - Spec.MaxFeed:F2} m）";
        else if (comp > Spec.MaxCompDeg)
            error = $"凿岩补偿角超限（{comp:F1}° / ≤{Spec.MaxCompDeg:F0}°）";

        return error.Length == 0;
    }

    /// <summary>逆解 + 正解校验 + 防碰撞，返回该孔的完整解算结果。</summary>
    public HoleSolution Solve(HolePoint hole)
    {
        var ok = TryInverse(hole, out var pose, out var error);
        var tip = Forward(pose);
        var residual = Math.Sqrt(
            (tip.X - hole.X) * (tip.X - hole.X) +
            (tip.Y - 0) * (tip.Y - 0) +
            (tip.Z - hole.Z) * (tip.Z - hole.Z));

        var collisions = ok ? CheckCollisions(hole, pose) : new List<string>();

        return new HoleSolution
        {
            Reachable = ok,
            Error = error,
            Pose = pose,
            Residual = Math.Round(residual, 4),
            Collisions = collisions
        };
    }

    /// <summary>防碰撞：孔口越界、臂架扫掠、孔底超挖、凿岩补偿角。</summary>
    public List<string> CheckCollisions(HolePoint hole, BoomPose pose)
    {
        var items = new List<string>();

        // 孔口越界（周边孔位于设计轮廓线上是正常的，只有明显超出才算碰撞）
        if (!_section.Contains(hole.X, hole.Z) && _section.DistanceToOutline(hole.X, hole.Z) > 0.05)
            items.Add("孔口位于断面轮廓外");

        // 臂架扫掠：基座 → 孔口 轴线上的关键点是否越出断面（周边孔允许贴边）
        var basePoint = new Point(Spec.BaseX, Spec.BaseZ);
        var collar = new Point(hole.X, hole.Z);
        var outsideAt = -1.0;

        const int samples = 16;
        for (var i = 1; i < samples; i++)
        {
            var t = (double)i / samples;
            if (t > 0.85) break;

            var px = basePoint.X + (collar.X - basePoint.X) * t;
            var pz = basePoint.Y + (collar.Y - basePoint.Y) * t;

            if (_section.Contains(px, pz)) continue;
            if (_section.DistanceToOutline(px, pz) <= Spec.BoomRadius) continue;

            outsideAt = t;
            break;
        }

        if (outsideAt >= 0)
            items.Add($"臂架在 {outsideAt * 100:F0}% 处越出断面轮廓");

        // 孔底越界（直孔穿出设计轮廓 → 超挖/穿帮）
        var toeX = hole.X + hole.DirX * hole.Depth;
        var toeZ = hole.Z + hole.DirZ * hole.Depth;
        if (hole.TiltDeg < 0.5 && !_section.Contains(toeX, toeZ) && _section.DistanceToOutline(toeX, toeZ) > 0.1)
            items.Add("孔底超出设计轮廓");

        if (pose.CompDeg > Spec.MaxCompDeg)
            items.Add($"凿岩补偿角超限（{pose.CompDeg:F1}°）");

        return items;
    }

    /// <summary>
    /// 双臂交叉防碰撞：比较两条臂架轴线（基座 → 孔口）在断面内的最小间距，
    /// 小于安全距离即认为两臂会互相干涉。
    /// 只有位于断面中线附近「交叉作业带」内的炮孔才参与比较——
    /// 两侧远端的作业区互不相干，不属于双臂干涉。
    /// </summary>
    public static (HolePoint Other, double Distance)? CheckCrossArm(HolePoint hole, BoomSpec spec,
        IEnumerable<HolePoint> otherArmHoles, BoomSpec otherSpec,
        double safeDistance = 1.2, double zoneWidth = 1.6)
    {
        if (Math.Abs(hole.X) > zoneWidth) return null;

        var a1 = new Point(spec.BaseX, spec.BaseZ);
        var a2 = new Point(hole.X, hole.Z);

        HolePoint? nearest = null;
        var min = double.MaxValue;

        foreach (var other in otherArmHoles)
        {
            if (Math.Abs(other.X) > zoneWidth + 0.6) continue;

            var b1 = new Point(otherSpec.BaseX, otherSpec.BaseZ);
            var b2 = new Point(other.X, other.Z);

            var d = SegmentDistance(a1, a2, b1, b2);
            if (d < min)
            {
                min = d;
                nearest = other;
            }
        }

        if (nearest is null || min >= safeDistance) return null;
        return (nearest, min);
    }

    /// <summary>两条线段间的最短距离（断面平面内）。</summary>
    private static double SegmentDistance(Point p1, Point p2, Point q1, Point q2)
    {
        if (SegmentsIntersect(p1, p2, q1, q2)) return 0;

        return Math.Min(
            Math.Min(PointToSegment(p1, q1, q2), PointToSegment(p2, q1, q2)),
            Math.Min(PointToSegment(q1, p1, p2), PointToSegment(q2, p1, p2)));
    }

    private static double PointToSegment(Point p, Point a, Point b)
    {
        var vx = b.X - a.X;
        var vy = b.Y - a.Y;
        var len2 = vx * vx + vy * vy;
        if (len2 < 1e-12) return Distance(p, a);

        var t = Math.Clamp(((p.X - a.X) * vx + (p.Y - a.Y) * vy) / len2, 0, 1);
        return Distance(p, new Point(a.X + t * vx, a.Y + t * vy));
    }

    private static bool SegmentsIntersect(Point p1, Point p2, Point q1, Point q2)
    {
        static double Cross(Point o, Point a, Point b)
            => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);

        var d1 = Cross(q1, q2, p1);
        var d2 = Cross(q1, q2, p2);
        var d3 = Cross(p1, p2, q1);
        var d4 = Cross(p1, p2, q2);

        return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) &&
               ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
    }

    private static double Distance(Point a, Point b)
        => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
}

/// <summary>
/// 钻孔路径规划（对应 CollectData 的 <c>HolePathPlanner</c>）：
/// 按左右臂划分作业区，底板孔先钻，其后周边孔，最后其余炮孔按 0.4 m 分层蛇形推进。
/// </summary>
public static class DrillingPathPlanner
{
    public static (List<HolePoint> Left, List<HolePoint> Right) Split(IReadOnlyList<HolePoint> holes, double? splitX = null)
    {
        if (holes.Count == 0) return (new List<HolePoint>(), new List<HolePoint>());

        var mid = splitX ?? holes.Average(h => h.X);
        var left = holes.Where(h => h.X < mid).ToList();
        var right = holes.Where(h => h.X >= mid).ToList();
        return (left, right);
    }

    public static List<HolePoint> Order(IReadOnlyList<HolePoint> holes, bool isLeftArm)
    {
        if (holes.Count == 0) return new List<HolePoint>();

        var result = new List<HolePoint>();

        // ① 底板孔：沿断面横向单向推进
        result.AddRange(holes.Where(h => h.Kind == HoleKind.Floor).OrderBy(h => h.X));

        // ② 周边孔：左臂自上而下，右臂自下而上（避免跨越已钻区域）
        result.AddRange(isLeftArm
            ? holes.Where(h => h.Kind == HoleKind.Perimeter).OrderByDescending(h => h.Z)
            : holes.Where(h => h.Kind == HoleKind.Perimeter).OrderBy(h => h.Z));

        // ③ 其余炮孔：按 0.4 m 分层，逐层蛇形
        var rest = holes.Where(h => h.Kind is HoleKind.Aux or HoleKind.Cut).ToList();
        if (rest.Count > 0)
        {
            var centerX = holes.Average(h => h.X);
            var left = rest.Where(h => h.X <= centerX).ToList();
            var right = rest.Where(h => h.X > centerX).ToList();

            const double layerHeight = 0.4;
            var leftLayers = (isLeftArm
                    ? left.GroupBy(h => Math.Floor(h.Z / layerHeight)).OrderBy(g => g.Key)
                    : left.GroupBy(h => Math.Floor(h.Z / layerHeight)).OrderByDescending(g => g.Key))
                .ToList();
            var rightLayers = (isLeftArm
                    ? right.GroupBy(h => Math.Floor(h.Z / layerHeight)).OrderByDescending(g => g.Key)
                    : right.GroupBy(h => Math.Floor(h.Z / layerHeight)).OrderBy(g => g.Key))
                .ToList();

            var cursorX = result.Count > 0 ? result[^1].X : centerX;
            AppendLayers(leftLayers, result, ref cursorX);
            AppendLayers(rightLayers, result, ref cursorX);
        }

        return result;
    }

    private static void AppendLayers(IEnumerable<IGrouping<double, HolePoint>> layers,
        List<HolePoint> result, ref double cursorX)
    {
        foreach (var layer in layers)
        {
            var ordered = layer.ToList();
            if (ordered.Count > 1)
            {
                var minX = ordered.Min(h => h.X);
                var maxX = ordered.Max(h => h.X);
                var fromRight = Math.Abs(cursorX - maxX) < Math.Abs(cursorX - minX);
                ordered = fromRight
                    ? ordered.OrderByDescending(h => h.X).ToList()
                    : ordered.OrderBy(h => h.X).ToList();
            }

            result.AddRange(ordered);
            cursorX = ordered[^1].X;
        }
    }

    /// <summary>路径总长度 / m。</summary>
    public static double Length(IReadOnlyList<HolePoint> path)
    {
        var sum = 0.0;
        for (var i = 1; i < path.Count; i++)
        {
            var dx = path[i].X - path[i - 1].X;
            var dz = path[i].Z - path[i - 1].Z;
            sum += Math.Sqrt(dx * dx + dz * dz);
        }
        return sum;
    }
}
