using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using rockDrill.Models;

namespace rockDrill.ViewModels;

/// <summary>单个炮孔（布孔图上的一个点位）。</summary>
public partial class HoleVm : ObservableObject
{
    public int Index { get; init; }
    public int No { get; init; }
    public string NoText => No.ToString();
    public string Type { get; init; } = "辅助孔";

    /// <summary>孔口坐标 / m（原点在断面底面中心，x 右为正，y 上为正）。</summary>
    public double X { get; init; }
    public double Y { get; init; }

    /// <summary>孔底在断面上的投影 / m（斜孔画孔向连线用）。</summary>
    public double ToeX { get; init; }
    public double ToeY { get; init; }

    /// <summary>孔径 / mm。</summary>
    public double Diameter { get; init; } = 45;

    /// <summary>掏槽中心空孔（不装药，孔径更大）。</summary>
    public bool IsRelief { get; init; }

    /// <summary>斜孔倾角 / °。</summary>
    public double TiltDeg { get; init; }

    /// <summary>设计孔深 / m（侧视 / 俯视 / 三维视图沿孔轴投影用）。</summary>
    public double Depth { get; init; }

    /// <summary>孔口沿孔轴（进深）的坐标 / m，掌子面为 0。</summary>
    public double AxialY { get; init; }

    /// <summary>孔向单位向量（横向 / 进深 / 竖向）。</summary>
    public double DirX { get; init; }
    public double DirY { get; init; } = 1;
    public double DirZ { get; init; }

    /// <summary>孔底沿孔轴的坐标 / m。</summary>
    public double ToeAxial => AxialY + DirY * Depth;

    /// <summary>孔底三维坐标（横向 / 竖向 / 进深）。</summary>
    public Point Toe3D => new(X + DirX * Depth, Y + DirZ * Depth);

    [ObservableProperty] private IBrush _fill = new SolidColorBrush(Color.Parse("#22323F"));
    [ObservableProperty] private IBrush _stroke = new SolidColorBrush(Color.Parse("#3A5468"));
    [ObservableProperty] private double _strokeThickness = 1;

    [ObservableProperty] private string _stateText = "待钻";
    [ObservableProperty] private IBrush _stateBrush = new SolidColorBrush(Color.Parse("#5C7484"));
    [ObservableProperty] private bool _isFault;

    public string XText => X.ToString("F2");
    public string YText => Y.ToString("F2");
    public string AngleText { get; init; } = "90.0";

    /// <summary>当前钻进深度 / m（钻孔页随进尺刷新）。</summary>
    [ObservableProperty] private string _depthText = "0.00";

    /// <summary>执行臂架（左臂 / 右臂）。</summary>
    [ObservableProperty] private string _boomText = "左臂";

    /// <summary>所属臂架（0 = 左臂，1 = 右臂，-1 = 未分配）。</summary>
    [ObservableProperty] private int _armIndex = -1;

    /// <summary>运动学解算结果摘要。</summary>
    [ObservableProperty] private string _poseText = "--";

    /// <summary>路径中的钻孔顺序（从 1 开始，0 = 未排入路径）。</summary>
    [ObservableProperty] private int _pathOrder;

    /// <summary>防碰撞结果（为空表示无干涉）。</summary>
    [ObservableProperty] private string _collisionText = string.Empty;

    public bool HasCollision => !string.IsNullOrEmpty(CollisionText);
}

/// <summary>炮孔类型统计。</summary>
public class HoleTypeStatVm
{
    public string Name { get; init; } = string.Empty;
    public string CountText { get; init; } = string.Empty;
    public double Percent { get; init; }
    public IBrush Brush { get; init; } = Brushes.DeepSkyBlue;
}

/// <summary>画布上的一条连线（钻孔路径 / 选中臂架连线）。坐标为单位 m 的断面坐标。</summary>
public sealed class HolePathVm
{
    public string Name { get; init; } = string.Empty;
    public IBrush Brush { get; init; } = Brushes.DeepSkyBlue;
    public IReadOnlyList<Point> Points { get; init; } = Array.Empty<Point>();
    public bool ShowArrows { get; init; } = true;
    public bool Dashed { get; init; }
    public double Thickness { get; init; } = 1.8;
}

/// <summary>画布上的臂架基座标记。</summary>
public sealed class BoomBaseVm
{
    public string Label { get; init; } = string.Empty;
    public double X { get; init; }
    public double Z { get; init; }
    public IBrush Brush { get; init; } = Brushes.DeepSkyBlue;
}

/// <summary>
/// 布孔图：从 doc/Gradder 下的 CSV 载入炮孔与断面轮廓，
/// 并提供逆解、正解、防碰撞与钻孔路径规划。
/// 内置半圆拱断面仅作为无 CSV 数据时的缺省布孔方案。
/// </summary>
public partial class HolePatternViewModel : ViewModelBase
{
    /// <summary>内置断面跨度（两帮净宽）/ m。</summary>
    public const double SpanWidth = 12.40;

    /// <summary>内置断面直墙高度 / m。</summary>
    public const double WallHeight = 3.00;

    /// <summary>内置断面拱部半径 / m（半圆拱：等于跨度的一半）。</summary>
    public const double ArchRadius = SpanWidth / 2;

    /// <summary>内置断面总高 / m。</summary>
    public const double SectionHeight = WallHeight + ArchRadius;

    private static readonly IBrush LeftArmBrush = new SolidColorBrush(Color.Parse("#23A9F0"));
    private static readonly IBrush RightArmBrush = new SolidColorBrush(Color.Parse("#F5A623"));
    private static readonly IBrush BoomLinkBrush = new SolidColorBrush(Color.Parse("#8FA6B8"));

    private List<HolePoint> _points = new();
    private readonly Dictionary<int, HoleSolution> _solutions = new();
    private BoomSolver _leftSolver;
    private BoomSolver _rightSolver;
    private List<HolePoint> _leftPath = new();
    private List<HolePoint> _rightPath = new();

    public ObservableCollection<HoleVm> Holes { get; } = new();
    public ObservableCollection<HoleTypeStatVm> TypeStats { get; } = new();
    public ObservableCollection<HoleDatasetInfo> Datasets { get; } = new();
    public ObservableCollection<HolePathVm> Paths { get; } = new();
    public ObservableCollection<BoomBaseVm> Booms { get; } = new();

    [ObservableProperty] private TunnelSection _section;
    [ObservableProperty] private HoleDatasetInfo? _selectedDataset;
    [ObservableProperty] private HoleVm? _selectedHole;
    [ObservableProperty] private string _datasetText = "内置半圆拱断面";
    [ObservableProperty] private string _sourceText = "内置布孔方案";
    [ObservableProperty] private string _statusText = "选择数据集后点击「载入布孔图」。";
    [ObservableProperty] private string _pathSummaryText = "尚未生成路径";
    [ObservableProperty] private string _kinematicSummaryText = "尚未解算";
    [ObservableProperty] private string _collisionSummaryText = "尚未校验";
    [ObservableProperty] private string _selectedPoseText = "--";
    [ObservableProperty] private string _selectedForwardText = "--";
    [ObservableProperty] private string _selectedCollisionText = "无干涉";
    [ObservableProperty] private bool _selectedHasCollision;
    [ObservableProperty] private int _canvasRevision;

    public HolePatternViewModel()
    {
        _section = TunnelSection.CreateArch(SpanWidth, WallHeight);
        _leftSolver = new BoomSolver(BoomSpec.Left(8.0), _section);
        _rightSolver = new BoomSolver(BoomSpec.Right(8.0), _section);

        LoadDatasets();
        if (SelectedDataset is not null) LoadDataset();
        else BuildBuiltInPattern();
    }

    /// <summary>断面尺寸文本。</summary>
    public string SectionText => $"{Section.Span:F2} × {Section.Height:F2} m";
    public string SpanText => $"{Section.Span:F2} m";
    public string HeightText => $"{Section.Height:F2} m";
    public string ShapeText => Section.IsStandardArch ? "半圆拱" : "轮廓线（CSV）";
    public string ArchRadiusText => Section.IsStandardArch ? $"R{Section.ArchRadius:F2} m" : "—";
    public string WallHeightText => Section.IsStandardArch ? $"{Section.WallHeight:F2} m" : "—";
    public string SectionDescText => $"断面 {Section.Span:F2} m × {Section.Height:F2} m　|　点云偏差 ≤ 32 mm";

    public string AdvanceText => "3.50 m";
    public string DiameterText => "φ45 mm";
    public string PowderText => "118 kg";

    public string TotalText => $"{Holes.Count} 个";
    public string HeaderTagText => $"{Holes.Count} 孔 · {ShapeText}";

    public int DoneCount => Holes.Count(h => h.StateText == "已完成");
    public int FaultCount => Holes.Count(h => h.IsFault);
    public int RemainCount => Holes.Count - DoneCount;
    public double DonePercent => Holes.Count == 0 ? 0 : (double)DoneCount / Holes.Count * 100;

    public string DoneText => $"{DoneCount} / {Holes.Count}";
    public string RemainText => $"{RemainCount} 个";
    public string FaultText => $"{FaultCount} 个";

    public string SelectedNoText => SelectedHole is null ? "--" : $"No.{SelectedHole.No}";
    public string SelectedTypeText => SelectedHole?.Type ?? "--";
    public string SelectedXText => SelectedHole?.XText ?? "--";
    public string SelectedYText => SelectedHole?.YText ?? "--";
    public string SelectedDepthText => SelectedHole?.DepthText ?? "--";
    public string SelectedAngleText => SelectedHole?.AngleText ?? "--";
    public string SelectedBoomText => SelectedHole is null || SelectedHole.ArmIndex < 0
        ? "--"
        : SelectedHole.ArmIndex == 0 ? "左臂" : "右臂";
    public string SelectedStateText => SelectedHole?.StateText ?? "--";
    public IBrush SelectedStateBrush => SelectedHole?.StateBrush ?? Brushes.Gray;
    public IBrush SelectedCollisionBrush => SelectedHasCollision
        ? new SolidColorBrush(Color.Parse("#ED4C5C"))
        : new SolidColorBrush(Color.Parse("#2AD07C"));
    public string SelectedPathText => SelectedHole is null || SelectedHole.PathOrder <= 0
        ? "未排入路径"
        : $"路径 #{SelectedHole.PathOrder}";

    partial void OnSelectedHoleChanged(HoleVm? value)
    {
        OnPropertyChanged(nameof(SelectedNoText));
        OnPropertyChanged(nameof(SelectedTypeText));
        OnPropertyChanged(nameof(SelectedXText));
        OnPropertyChanged(nameof(SelectedYText));
        OnPropertyChanged(nameof(SelectedDepthText));
        OnPropertyChanged(nameof(SelectedAngleText));
        OnPropertyChanged(nameof(SelectedBoomText));
        OnPropertyChanged(nameof(SelectedStateText));
        OnPropertyChanged(nameof(SelectedStateBrush));
        OnPropertyChanged(nameof(SelectedPathText));

        RefreshSelectedSolution();
    }

    partial void OnSelectedHasCollisionChanged(bool value)
    {
        OnPropertyChanged(nameof(SelectedCollisionBrush));
    }

    partial void OnSectionChanged(TunnelSection value)
    {
        OnPropertyChanged(nameof(SectionText));
        OnPropertyChanged(nameof(SpanText));
        OnPropertyChanged(nameof(HeightText));
        OnPropertyChanged(nameof(ShapeText));
        OnPropertyChanged(nameof(ArchRadiusText));
        OnPropertyChanged(nameof(WallHeightText));
        OnPropertyChanged(nameof(SectionDescText));
        OnPropertyChanged(nameof(HeaderTagText));
    }

    // ==================== 数据集 ====================

    private void LoadDatasets()
    {
        Datasets.Clear();
        foreach (var info in HoleDatasetLibrary.Discover())
            Datasets.Add(info);

        SelectedDataset = Datasets.FirstOrDefault(d => d.Name.Contains("炮孔布置"))
                          ?? Datasets.FirstOrDefault();

        // 支持 --dataset=<名称关键字> 直接指定数据集（调试/截图用）
        var arg = Environment.GetCommandLineArgs()
            .FirstOrDefault(a => a.StartsWith("--dataset=", StringComparison.OrdinalIgnoreCase));
        if (arg is not null)
        {
            var keyword = arg["--dataset=".Length..];
            var match = Datasets.FirstOrDefault(d =>
                d.Name.Contains(keyword, StringComparison.CurrentCultureIgnoreCase));
            if (match is not null) SelectedDataset = match;
        }
    }

    [RelayCommand]
    private void LoadDataset()
    {
        var info = SelectedDataset;
        if (info is null)
        {
            StatusText = "未找到 doc/Gradder 下的 CSV 文件，已使用内置布孔方案。";
            BuildBuiltInPattern();
            return;
        }

        try
        {
            var dataset = HoleDatasetLibrary.Load(info);

            _points = dataset.Holes.ToList();
            Section = dataset.Section;
            _leftSolver = new BoomSolver(BoomSpec.Left(8.0), Section);
            _rightSolver = new BoomSolver(BoomSpec.Right(8.0), Section);
            DatasetText = info.Name;
            SourceText = $"{Path.GetFileName(info.HoleFile)} · {dataset.SectionSourceText} · {_points.Count} 孔";

            if (_points.Count == 0)
            {
                StatusText = $"{info.Name} 未解析到有效炮孔。";
                BuildBuiltInPattern();
                return;
            }

            ResetDerived();
            RebuildHoles();
            StatusText = $"已载入 {info.Name}：{_points.Count} 个炮孔，断面 {Section.Span:F2} × {Section.Height:F2} m。";
        }
        catch (Exception ex)
        {
            StatusText = $"载入失败：{ex.Message}";
        }
    }

    private void ResetDerived()
    {
        _solutions.Clear();
        _leftPath = new List<HolePoint>();
        _rightPath = new List<HolePoint>();
        Paths.Clear();
        Booms.Clear();
        PathSummaryText = "尚未生成路径";
        KinematicSummaryText = "尚未解算";
        CollisionSummaryText = "尚未校验";
        SelectedPoseText = "--";
        SelectedForwardText = "--";
        SelectedCollisionText = "无干涉";
        SelectedHasCollision = false;
    }

    // ==================== 炮孔模型 ====================

    private void RebuildHoles()
    {
        Holes.Clear();

        var total = _points.Count;
        var done = (int)Math.Round(total * 0.62);
        var drillingNo = Math.Min(done + 1, total);
        var faultNo = Math.Clamp((int)Math.Round(total * 0.9), drillingNo + 1, total);

        for (var i = 0; i < total; i++)
        {
            var p = _points[i];
            var no = p.No;
            var seq = i + 1;

            string state;
            IBrush fill;
            IBrush stroke;
            IBrush stateBrush;
            double thickness;
            var isFault = false;

            if (seq <= done)
            {
                state = "已完成";
                fill = new SolidColorBrush(Color.Parse("#15402F"));
                stroke = new SolidColorBrush(Color.Parse("#2AD07C"));
                stateBrush = new SolidColorBrush(Color.Parse("#2AD07C"));
                thickness = 1;
            }
            else if (seq == drillingNo)
            {
                state = "钻进中";
                fill = new SolidColorBrush(Color.Parse("#4A3612"));
                stroke = new SolidColorBrush(Color.Parse("#F5A623"));
                stateBrush = new SolidColorBrush(Color.Parse("#F5A623"));
                thickness = 2;
            }
            else if (seq == faultNo)
            {
                state = "异常";
                fill = new SolidColorBrush(Color.Parse("#42151B"));
                stroke = new SolidColorBrush(Color.Parse("#ED4C5C"));
                stateBrush = new SolidColorBrush(Color.Parse("#ED4C5C"));
                thickness = 2;
                isFault = true;
            }
            else
            {
                state = "待钻";
                fill = new SolidColorBrush(Color.Parse("#16242F"));
                stroke = new SolidColorBrush(Color.Parse("#33506A"));
                stateBrush = new SolidColorBrush(Color.Parse("#5C7484"));
                thickness = 1;
            }

            var hole = new HoleVm
            {
                Index = i,
                No = no,
                Type = p.KindText,
                X = p.X,
                Y = p.Z,
                ToeX = p.X + p.DirX * p.Depth,
                ToeY = p.Z + p.DirZ * p.Depth,
                Diameter = p.DiameterMm,
                Depth = p.Depth,
                DirX = p.DirX,
                DirY = p.DirY,
                DirZ = p.DirZ,
                IsRelief = p.Kind == HoleKind.Cut && p.DiameterMm > 60,
                TiltDeg = p.TiltDeg,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = thickness,
                StateText = state,
                StateBrush = stateBrush,
                IsFault = isFault,
                DepthText = state switch
                {
                    "已完成" => p.Depth.ToString("F2"),
                    "钻进中" => (p.Depth * 0.55).ToString("F2"),
                    _ => "0.00"
                },
                AngleText = (90 - p.TiltDeg).ToString("F1"),
                BoomText = p.X < 0 ? "左臂" : "右臂"
            };

            Holes.Add(hole);
            if (seq == drillingNo) SelectedHole = hole;
        }

        SelectedHole ??= Holes.FirstOrDefault();
        BuildStats();
        RefreshCounters();

        // 载入后自动解算一次，让布孔图直接带出路径与运动学结果
        RunPipeline();
    }

    /// <summary>依次执行逆解 → 正解校验 → 防碰撞 → 路径规划。</summary>
    private void RunPipeline()
    {
        if (Holes.Count == 0) return;

        SolveInverse();
        SolveForward();
        CheckCollision();
        BuildPath();
    }

    private void RefreshCounters()
    {
        OnPropertyChanged(nameof(DoneCount));
        OnPropertyChanged(nameof(FaultCount));
        OnPropertyChanged(nameof(RemainCount));
        OnPropertyChanged(nameof(DonePercent));
        OnPropertyChanged(nameof(DoneText));
        OnPropertyChanged(nameof(RemainText));
        OnPropertyChanged(nameof(FaultText));
        OnPropertyChanged(nameof(TotalText));
        OnPropertyChanged(nameof(HeaderTagText));
    }

    private void BuildStats()
    {
        TypeStats.Clear();
        var groups = Holes.GroupBy(h => h.Type).OrderBy(g => TypeRank(g.Key));

        foreach (var g in groups)
        {
            var brush = g.Key switch
            {
                "掏槽孔" => Brushes.DeepSkyBlue,
                "辅助孔" => Brushes.MediumPurple,
                "周边孔" => Brushes.MediumTurquoise,
                _ => Brushes.Goldenrod
            };
            var done = g.Count(h => h.StateText == "已完成");
            TypeStats.Add(new HoleTypeStatVm
            {
                Name = g.Key,
                CountText = $"{done} / {g.Count()}",
                Percent = g.Count() == 0 ? 0 : (double)done / g.Count() * 100,
                Brush = brush
            });
        }
    }

    private static int TypeRank(string type) => type switch
    {
        "掏槽孔" => 0,
        "辅助孔" => 1,
        "周边孔" => 2,
        _ => 3
    };

    // ==================== 内置半圆拱布孔方案 ====================

    private void BuildBuiltInPattern()
    {
        Section = TunnelSection.CreateArch(SpanWidth, WallHeight);
        _leftSolver = new BoomSolver(BoomSpec.Left(8.0), Section);
        _rightSolver = new BoomSolver(BoomSpec.Right(8.0), Section);
        DatasetText = "内置半圆拱断面";
        SourceText = $"内置布孔方案 · {SpanWidth:F2} m × {SectionHeight:F2} m";

        var points = new List<HolePoint>();
        var no = 0;

        void Add(double x, double z, HoleKind kind, double tiltDeg = 0, double diameter = 45)
        {
            var (ox, oz) = Section.OutwardDirection(x, z);
            var tilt = tiltDeg * Math.PI / 180.0;
            var dirX = ox * Math.Sin(tilt);
            var dirZ = oz * Math.Sin(tilt);
            var dirY = Math.Cos(tilt);

            points.Add(new HolePoint
            {
                No = ++no,
                RawType = kind.ToString().ToUpperInvariant(),
                Kind = kind,
                X = x,
                Z = z,
                Depth = kind switch { HoleKind.Cut => 3.60, HoleKind.Aux => 3.40, _ => 3.50 },
                DirX = dirX,
                DirY = dirY,
                DirZ = dirZ,
                TiltDeg = tiltDeg,
                DiameterMm = diameter
            });
        }

        // 直眼掏槽：中心空孔 + 内圈 4 孔 + 外圈 4 孔
        const double cutY = 1.60;
        Add(0, cutY, HoleKind.Cut, 0, 100);
        for (var i = 0; i < 4; i++)
        {
            var a = (45 + i * 90) * Math.PI / 180;
            Add(0.30 * Math.Cos(a), cutY + 0.30 * Math.Sin(a), HoleKind.Cut);
        }
        for (var i = 0; i < 4; i++)
        {
            var a = (i * 90) * Math.PI / 180;
            Add(0.62 * Math.Cos(a), cutY + 0.62 * Math.Sin(a), HoleKind.Cut);
        }

        // 辅助孔：三段内缩环
        foreach (var (inset, spacing) in new[] { (3.15, 1.00), (2.35, 1.00), (1.35, 0.95) })
        {
            foreach (var p in TunnelSection.Resample(Section.InsetRingPolyline(inset), spacing))
                Add(p.X, p.Y, HoleKind.Aux);
        }

        // 周边孔：沿轮廓内缩 0.45 m
        foreach (var p in TunnelSection.Resample(Section.InsetRingPolyline(0.45), 0.75))
            Add(p.X, p.Y, HoleKind.Perimeter, 5.0);

        // 底板孔：沿底板一排
        var half = Section.ArchRadius - 1.0;
        var count = Math.Max(2, (int)Math.Round(2 * half / 0.9) + 1);
        for (var i = 0; i < count; i++)
            Add(-half + 2 * half * i / (count - 1), 0.30, HoleKind.Floor);

        _points = points;
        ResetDerived();
        RebuildHoles();
    }

    // ==================== 双臂分配 ====================

    private void EnsureAssignments()
    {
        var (left, right) = DrillingPathPlanner.Split(_points);
        _leftPath = DrillingPathPlanner.Order(left, true);
        _rightPath = DrillingPathPlanner.Order(right, false);

        foreach (var hole in Holes) hole.ArmIndex = -1;
        var byIndex = Holes.ToDictionary(h => h.Index);

        foreach (var p in _leftPath)
            if (TryFindVm(p, byIndex, out var vm)) vm.ArmIndex = 0;
        foreach (var p in _rightPath)
            if (TryFindVm(p, byIndex, out var vm)) vm.ArmIndex = 1;
    }

    private bool TryFindVm(HolePoint point, IReadOnlyDictionary<int, HoleVm> byIndex, out HoleVm vm)
    {
        var index = _points.IndexOf(point);
        vm = null!;
        return index >= 0 && byIndex.TryGetValue(index, out vm!);
    }

    // ==================== 逆解 ====================

    [RelayCommand]
    private void SolveInverse()
    {
        if (_points.Count == 0) return;

        EnsureAssignments();
        _solutions.Clear();

        var reachable = 0;
        var errors = new List<string>();

        foreach (var hole in Holes)
        {
            var p = _points[hole.Index];
            var solver = hole.ArmIndex == 1 ? _rightSolver : _leftSolver;
            var solution = solver.Solve(p);

            _solutions[hole.Index] = solution;
            hole.PoseText = solution.Reachable ? solution.PoseText : solution.Error;

            if (solution.Reachable) reachable++;
            else if (errors.Count < 3) errors.Add($"No.{hole.No} {solution.Error}");
        }

        KinematicSummaryText = errors.Count == 0
            ? $"逆解完成：{reachable}/{Holes.Count} 孔可达"
            : $"逆解：{reachable}/{Holes.Count} 孔可达（{string.Join("；", errors)}…）";
        StatusText = KinematicSummaryText;

        RefreshSelectedSolution();
        CanvasRevision++;
    }

    // ==================== 正解 ====================

    [RelayCommand]
    private void SolveForward()
    {
        if (_points.Count == 0) return;
        if (_solutions.Count == 0) SolveInverse();

        var maxResidual = 0.0;
        var worst = string.Empty;
        var checkedCount = 0;

        foreach (var hole in Holes)
        {
            if (!_solutions.TryGetValue(hole.Index, out var solution) || !solution.Reachable) continue;

            var solver = hole.ArmIndex == 1 ? _rightSolver : _leftSolver;
            var p = _points[hole.Index];
            var tip = solver.Forward(solution.Pose);

            var residual = Math.Sqrt(
                (tip.X - p.X) * (tip.X - p.X) +
                tip.Y * tip.Y +
                (tip.Z - p.Z) * (tip.Z - p.Z));

            checkedCount++;
            if (residual > maxResidual)
            {
                maxResidual = residual;
                worst = $"No.{hole.No}";
            }
        }

        KinematicSummaryText = checkedCount == 0
            ? "正解：没有可达孔可校验"
            : $"正解校验：{checkedCount} 孔，最大残差 {maxResidual:F4} m{(worst.Length > 0 ? $"（{worst}）" : "")}";
        StatusText = KinematicSummaryText;

        RefreshSelectedSolution();
        CanvasRevision++;
    }

    // ==================== 防碰撞 ====================

    [RelayCommand]
    private void CheckCollision()
    {
        if (_points.Count == 0) return;
        if (_solutions.Count == 0) SolveInverse();

        var blocked = 0;
        var detail = new List<string>();

        foreach (var hole in Holes)
        {
            if (!_solutions.TryGetValue(hole.Index, out var solution)) continue;

            var issues = new List<string>(solution.Collisions);
            if (!solution.Reachable) issues.Add(solution.Error);

            var p = _points[hole.Index];
            var otherPath = hole.ArmIndex == 1 ? _leftPath : _rightPath;
            var otherSpec = hole.ArmIndex == 1 ? _leftSolver.Spec : _rightSolver.Spec;
            var cross = BoomSolver.CheckCrossArm(p, hole.ArmIndex == 1 ? _rightSolver.Spec : _leftSolver.Spec,
                otherPath, otherSpec);
            if (cross is not null)
                issues.Add($"与对侧臂 No.{cross.Value.Other.No} 臂架干涉（间距 {cross.Value.Distance:F2} m）");

            if (issues.Count > 0)
            {
                blocked++;
                hole.CollisionText = string.Join("；", issues);
                if (detail.Count < 3) detail.Add($"No.{hole.No} {hole.CollisionText}");
            }
            else
            {
                hole.CollisionText = string.Empty;
            }
        }

        CollisionSummaryText = blocked == 0
            ? "防碰撞校验：全部通过"
            : $"防碰撞校验：{blocked} 孔需处理（{string.Join("；", detail)}…）";
        StatusText = CollisionSummaryText;

        RefreshSelectedSolution();
        CanvasRevision++;
    }

    // ==================== 路径规划 ====================

    [RelayCommand]
    private void BuildPath()
    {
        if (_points.Count == 0) return;

        EnsureAssignments();
        if (_solutions.Count == 0) SolveInverse();

        Paths.Clear();

        var order = 0;
        foreach (var hole in Holes) hole.PathOrder = 0;

        var leftPoints = new List<Point>();
        foreach (var p in _leftPath)
        {
            var vm = Holes.FirstOrDefault(h => h.Index == _points.IndexOf(p));
            if (vm is null) continue;
            if (_solutions.TryGetValue(vm.Index, out var s) && !s.Reachable) continue;
            vm.PathOrder = ++order;
            leftPoints.Add(new Point(p.X, p.Z));
        }

        var rightPoints = new List<Point>();
        foreach (var p in _rightPath)
        {
            var vm = Holes.FirstOrDefault(h => h.Index == _points.IndexOf(p));
            if (vm is null) continue;
            if (_solutions.TryGetValue(vm.Index, out var s) && !s.Reachable) continue;
            vm.PathOrder = ++order;
            rightPoints.Add(new Point(p.X, p.Z));
        }

        if (leftPoints.Count > 1)
            Paths.Add(new HolePathVm { Name = "左臂路径", Brush = LeftArmBrush, Points = leftPoints });
        if (rightPoints.Count > 1)
            Paths.Add(new HolePathVm { Name = "右臂路径", Brush = RightArmBrush, Points = rightPoints });

        Booms.Clear();
        Booms.Add(new BoomBaseVm { Label = "左臂", X = _leftSolver.Spec.BaseX, Z = _leftSolver.Spec.BaseZ, Brush = LeftArmBrush });
        Booms.Add(new BoomBaseVm { Label = "右臂", X = _rightSolver.Spec.BaseX, Z = _rightSolver.Spec.BaseZ, Brush = RightArmBrush });

        PathSummaryText =
            $"左臂 {leftPoints.Count} 孔 / {DrillingPathPlanner.Length(_leftPath):F1} m　右臂 {rightPoints.Count} 孔 / {DrillingPathPlanner.Length(_rightPath):F1} m";
        StatusText = $"路径已生成：{PathSummaryText}";

        RefreshSelectedSolution();
        OnPropertyChanged(nameof(SelectedPathText));
        CanvasRevision++;
    }

    // ==================== 选中孔信息 ====================

    private void RefreshSelectedSolution()
    {
        // 先移除上一条臂架连线
        for (var i = Paths.Count - 1; i >= 0; i--)
            if (Paths[i].Name == "臂架") Paths.RemoveAt(i);

        RebuildBooms();

        var hole = SelectedHole;
        if (hole is null)
        {
            SelectedPoseText = "--";
            SelectedForwardText = "--";
            SelectedCollisionText = "无干涉";
            SelectedHasCollision = false;
            return;
        }

        if (_solutions.TryGetValue(hole.Index, out var solution))
        {
            SelectedPoseText = solution.Reachable ? solution.PoseText : $"逆解失败：{solution.Error}";

            var solver = hole.ArmIndex == 1 ? _rightSolver : _leftSolver;
            var tip = solver.Forward(solution.Pose);
            SelectedForwardText = solution.Reachable
                ? $"({tip.X:F2}, {tip.Z:F2}) m　残差 {solution.Residual:F4} m"
                : "--";

            SelectedCollisionText = solution.CollisionText;
            SelectedHasCollision = solution.HasCollision || !solution.Reachable;

            // 选中孔的臂架连线（基座 → 孔口）
            if (solution.Reachable)
            {
                Paths.Add(new HolePathVm
                {
                    Name = "臂架",
                    Brush = BoomLinkBrush,
                    Points = new[] { new Point(solver.Spec.BaseX, solver.Spec.BaseZ), new Point(hole.X, hole.Y) },
                    ShowArrows = false,
                    Dashed = true,
                    Thickness = 1.2
                });
            }
        }
        else
        {
            SelectedPoseText = "尚未解算";
            SelectedForwardText = "--";
            SelectedCollisionText = "尚未校验";
            SelectedHasCollision = false;
        }
    }

    private void RebuildBooms()
    {
        Booms.Clear();
        Booms.Add(new BoomBaseVm { Label = "左臂", X = _leftSolver.Spec.BaseX, Z = _leftSolver.Spec.BaseZ, Brush = LeftArmBrush });
        Booms.Add(new BoomBaseVm { Label = "右臂", X = _rightSolver.Spec.BaseX, Z = _rightSolver.Spec.BaseZ, Brush = RightArmBrush });
    }
}
