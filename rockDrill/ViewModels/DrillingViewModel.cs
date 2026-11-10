using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using rockDrill.Models;

namespace rockDrill.ViewModels;

/// <summary>视图切换按钮（正视图 / 侧视图 / 俯视图 / 三维视图）。</summary>
public sealed class ViewModeOptionVm
{
    public ViewModeOptionVm(HoleViewMode mode) => Mode = mode;

    public HoleViewMode Mode { get; }
    public string Header => Mode.Display();
    public string Hint => Mode.AxisHint();
}

/// <summary>单臂钻孔状态（现场 HMI 顶部“左臂 / 右臂”条：设置孔深、当前孔深、姿态读数）。</summary>
public partial class DrillArmVm : ObservableObject
{
    private static readonly IBrush LeftAccent = new SolidColorBrush(Color.Parse("#23A9F0"));
    private static readonly IBrush RightAccent = new SolidColorBrush(Color.Parse("#F5A623"));

    public DrillArmVm(int index)
    {
        Index = index;
        Name = index == 0 ? "左臂" : "右臂";
        Accent = index == 0 ? LeftAccent : RightAccent;
    }

    public int Index { get; }
    public string Name { get; }
    public IBrush Accent { get; }

    /// <summary>指令主题前缀：左臂 L_ / 右臂 R_（与自动控臂页一致）。</summary>
    public string CommandPrefix => Index == 0 ? "L_" : "R_";

    /// <summary>本臂已规划的孔（按钻孔顺序）。</summary>
    public ObservableCollection<HoleVm> Planned { get; } = new();

    /// <summary>待执行队列。</summary>
    public Queue<HoleVm> Queue { get; } = new();

    [ObservableProperty] private double _planDepth = 4.00;
    [ObservableProperty] private double _currentDepth;
    [ObservableProperty] private double _depthPercent;
    [ObservableProperty] private string _depthText = "0.00";
    [ObservableProperty] private HoleVm? _currentHole;
    [ObservableProperty] private bool _isDrilling;
    [ObservableProperty] private int _doneCount;
    [ObservableProperty] private string _stateText = "待机";

    /// <summary>末端坐标 / 翻转 / 俯仰 / 补偿（四行，画布左上 / 右上角显示）。</summary>
    [ObservableProperty] private string _poseText = "末端坐标(0.00,-8.00,0.00)\n翻转:0.0°\n俯仰:0.0°\n补偿:0.0°";

    public int PlannedCount => Planned.Count;

    /// <summary>"设置孔深:4.00m"（现场 HMI 右上角的提示语）。</summary>
    public string PlanDepthText => $"设置孔深:{PlanDepth:F2}m";

    /// <summary>孔深 "− / +" 按钮命令（参数为增量字符串）。</summary>
    public IRelayCommand<string>? StepDepthCommand { get; set; }

    partial void OnCurrentDepthChanged(double value)
    {
        DepthText = value.ToString("F2");
        DepthPercent = PlanDepth <= 0 ? 0 : Math.Clamp(value / PlanDepth * 100, 0, 100);
    }

    partial void OnPlanDepthChanged(double value)
    {
        OnPropertyChanged(nameof(PlanDepthText));
        DepthPercent = value <= 0 ? 0 : Math.Clamp(CurrentDepth / value * 100, 0, 100);
    }
}

/// <summary>参数表一行（左臂 / 右臂两列数值）。</summary>
public partial class DrillParamRowVm : ObservableObject
{
    public DrillParamRowVm(string label, string unit)
    {
        Label = label;
        Unit = unit;
    }

    public string Label { get; }
    public string Unit { get; }

    [ObservableProperty] private string _leftText = "0.00";
    [ObservableProperty] private string _rightText = "0.00";
}

/// <summary>弹出面板中的只读信息行。</summary>
public sealed class InfoRowVm
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
}

/// <summary>
/// 钻孔控制页（对齐 doc/现场测试视频.mp4 的现场 HMI）：
/// 四视图掌子面图 + 双臂作业条 + 参数表 + 规划 / 执行 / 凿岩按钮。
/// 规划与解算复用布孔图的 <see cref="DrillingPathPlanner"/>、<see cref="BoomSolver"/>，
/// 钻进推进（进尺、完成、切换下一孔）由本地模拟定时器驱动。
/// </summary>
public partial class DrillingViewModel : ViewModelBase
{
    private static readonly IBrush PendingFill = new SolidColorBrush(Color.Parse("#DCE8F0"));
    private static readonly IBrush PendingStroke = new SolidColorBrush(Color.Parse("#0C1922"));
    private static readonly IBrush DoneFill = new SolidColorBrush(Color.Parse("#123F2C"));
    private static readonly IBrush DoneStroke = new SolidColorBrush(Color.Parse("#2AD07C"));
    private static readonly IBrush DoingFill = new SolidColorBrush(Color.Parse("#4A3612"));
    private static readonly IBrush DoingStroke = new SolidColorBrush(Color.Parse("#F5A623"));
    private static readonly IBrush MarkBrush = new SolidColorBrush(Color.Parse("#F5D24A"));

    private readonly Random _rnd = new();
    private readonly DispatcherTimer _timer;

    private List<HolePoint> _points = new();
    private BoomSolver _leftSolver = null!;
    private BoomSolver _rightSolver = null!;

    [ObservableProperty] private TunnelSection _section = TunnelSection.CreateArch(12.40, 3.00);
    [ObservableProperty] private int _viewModeIndex;
    [ObservableProperty] private int _selectedArmIndex;
    [ObservableProperty] private int _canvasRevision;
    [ObservableProperty] private string _datasetText = "内置半圆拱断面";
    [ObservableProperty] private string _statusText = "正在载入布孔数据…";
    [ObservableProperty] private bool _isManualPlanning;
    [ObservableProperty] private bool _isHidden;
    [ObservableProperty] private bool _isAutoDrilling;
    [ObservableProperty] private string _autoDrillText = "自动凿岩";
    [ObservableProperty] private HoleVm? _selectedHole;
    [ObservableProperty] private HoleVm? _currentHole;
    [ObservableProperty] private string _selectedHoleText = "未选中孔";
    [ObservableProperty] private string? _activePanel;
    [ObservableProperty] private string _panelTitle = string.Empty;
    [ObservableProperty] private int _drillModeIndex;
    [ObservableProperty] private string _leftInfoText = "左臂\n末端坐标(0.00,-8.00,0.00)";
    [ObservableProperty] private string _rightInfoText = "右臂\n末端坐标(0.00,-8.00,0.00)";

    public ObservableCollection<ViewModeOptionVm> Views { get; } = new();
    public ObservableCollection<DrillArmVm> Arms { get; } = new();
    public ObservableCollection<HoleVm> Holes { get; } = new();

    /// <summary>画布实际显示（“隐藏”会收起对侧臂的规划孔）。</summary>
    public ObservableCollection<HoleVm> DisplayHoles { get; } = new();

    /// <summary>右下角单臂局部图的孔集。</summary>
    public ObservableCollection<HoleVm> LocalHoles { get; } = new();

    public ObservableCollection<ArmMarkVm> ArmMarks { get; } = new();
    public ObservableCollection<DrillParamRowVm> ParamRows { get; } = new();
    public ObservableCollection<InfoRowVm> PanelRows { get; } = new();
    public ObservableCollection<string> DrillModes { get; } = new() { "单臂作业", "双臂交替", "双臂并行" };

    public DrillingViewModel()
    {
        foreach (var mode in HoleViewModeInfo.All)
            Views.Add(new ViewModeOptionVm(mode));

        Arms.Add(new DrillArmVm(0));
        Arms.Add(new DrillArmVm(1));

        // 每臂自己的孔深微调命令（顶部臂条上的 − / +）
        foreach (var arm in Arms)
        {
            var target = arm;
            arm.StepDepthCommand = new RelayCommand<string>(delta =>
            {
                var step = double.TryParse(delta, out var v) ? v : 0;
                target.PlanDepth = Math.Clamp(Math.Round(target.PlanDepth + step, 2), 1.0, 6.0);
                StatusText = $"{target.Name}设置孔深：{target.PlanDepth:F2} m。";
            });
        }

        ParamRows.Add(new DrillParamRowVm("冲击压力", "bar"));
        ParamRows.Add(new DrillParamRowVm("推进压力", "bar"));
        ParamRows.Add(new DrillParamRowVm("旋转压力", "bar"));
        ParamRows.Add(new DrillParamRowVm("钻进深度", "mm"));
        ParamRows.Add(new DrillParamRowVm("水流量", "L/min"));
        ParamRows.Add(new DrillParamRowVm("油雾压力", "bar"));

        LoadScene();
        AutoPlan();
        RefreshDisplayHoles();
        RefreshLocalHoles();
        UpdateInfoTexts();

        ApplyCommandLineOverrides();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(130) };
        _timer.Tick += (_, _) => OnTick();
        _timer.Start();
    }

    // ===================== 派生属性 =====================

    /// <summary>
    /// 调试 / 演示用命令行开关：
    /// <c>--view=0..3</c> 选择视图，<c>--arm=0|1</c> 选择臂，<c>--autodrill</c> 启动自动凿岩。
    /// </summary>
    private void ApplyCommandLineOverrides()
    {
        foreach (var arg in Environment.GetCommandLineArgs())
        {
            if (arg.StartsWith("--view=", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(arg["--view=".Length..], out var view))
            {
                ViewModeIndex = Math.Clamp(view, 0, 3);
            }
            else if (arg.StartsWith("--arm=", StringComparison.OrdinalIgnoreCase) &&
                     int.TryParse(arg["--arm=".Length..], out var arm))
            {
                SelectedArmIndex = Math.Clamp(arm, 0, 1);
            }
            else if (arg.Equals("--autodrill", StringComparison.OrdinalIgnoreCase))
            {
                ToggleAutoDrill();
            }
        }
    }

    public HoleViewMode ViewMode => HoleViewModeInfo.All[Math.Clamp(ViewModeIndex, 0, 3)];

    public DrillArmVm SelectedArm => Arms[Math.Clamp(SelectedArmIndex, 0, Arms.Count - 1)];

    public string SelectedViewText => ViewMode.Display();

    public string ClearArmText => "清除" + SelectedArm.Name;

    public string ExecuteArmText => SelectedArm.Name + "执行";

    public bool IsPanelOpen => ActivePanel is not null;

    public bool IsModePanel => ActivePanel == "mode";

    public string HideText => IsHidden ? "显示" : "隐藏";

    public string ManualPlanText => IsManualPlanning ? "退出手动规划" : "手动规划";

    public string PlannedText => $"{SelectedArm.Name}已规划孔数：{SelectedArm.PlannedCount}";

    public string LocalCaption => SelectedArmIndex == 0 ? "左" : "右";

    /// <summary>已钻孔数（双臂合计）。</summary>
    public int DoneTotal => Holes.Count(h => h.StateText == "已完成");

    public string DoneProgressText => $"{DoneTotal} / {Holes.Count} 孔";

    public double DonePercent => Holes.Count == 0 ? 0 : (double)DoneTotal / Holes.Count * 100;

    partial void OnViewModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(ViewMode));
        OnPropertyChanged(nameof(SelectedViewText));
        CanvasRevision++;
    }

    partial void OnSelectedArmIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedArm));
        OnPropertyChanged(nameof(ClearArmText));
        OnPropertyChanged(nameof(ExecuteArmText));
        OnPropertyChanged(nameof(LocalCaption));
        OnPropertyChanged(nameof(PlannedText));
        CurrentHole = SelectedArm.CurrentHole;
        RefreshLocalHoles();
        RefreshDisplayHoles();
        UpdateInfoTexts();
    }

    partial void OnIsHiddenChanged(bool value)
    {
        OnPropertyChanged(nameof(HideText));
        RefreshDisplayHoles();
    }

    partial void OnIsManualPlanningChanged(bool value)
        => OnPropertyChanged(nameof(ManualPlanText));

    partial void OnActivePanelChanged(string? value)
    {
        OnPropertyChanged(nameof(IsPanelOpen));
        OnPropertyChanged(nameof(IsModePanel));
        if (value is not null) BuildPanelRows();
    }

    partial void OnDrillModeIndexChanged(int value)
    {
        if (DrillModes.Count == 0) return;
        StatusText = $"钻孔模式：{DrillModes[Math.Clamp(value, 0, DrillModes.Count - 1)]}";
    }

    partial void OnIsAutoDrillingChanged(bool value)
        => AutoDrillText = value ? "停止凿岩" : "自动凿岩";

    partial void OnSelectedHoleChanged(HoleVm? value)
    {
        if (value is null)
        {
            SelectedHoleText = "未选中孔";
            return;
        }

        SelectedHoleText = $"No.{value.No} · {value.Type} · X {value.X:F2} / Z {value.Y:F2} m";
        if (!IsManualPlanning) return;

        if (value.StateText == "已完成")
        {
            StatusText = $"No.{value.No} 已完成，无需再规划。";
            return;
        }

        var arm = SelectedArm;
        if (arm.Planned.Contains(value))
        {
            StatusText = $"No.{value.No} 已在 {arm.Name} 的规划队列中。";
            return;
        }

        value.ArmIndex = arm.Index;
        value.BoomText = arm.Name;
        value.PathOrder = arm.Planned.Count + 1;
        arm.Planned.Add(value);
        arm.Queue.Enqueue(value);

        RefreshLocalHoles();
        RefreshDisplayHoles();
        NotifyPlanning();
        StatusText = $"手动规划：No.{value.No} 已加入 {arm.Name}（共 {arm.Planned.Count} 孔）。";
    }

    private void NotifyPlanning()
    {
        OnPropertyChanged(nameof(DoneTotal));
        OnPropertyChanged(nameof(DoneProgressText));
        OnPropertyChanged(nameof(DonePercent));
        OnPropertyChanged(nameof(PlannedText));
        CanvasRevision++;
    }

    /// <summary>“隐藏 / 显示”按钮：收起对侧臂的规划孔。</summary>
    [RelayCommand]
    private void ToggleHide() => IsHidden = !IsHidden;

    // ===================== 数据载入 =====================

    private void LoadScene()
    {
        try
        {
            var datasets = HoleDatasetLibrary.Discover();
            var info = datasets.FirstOrDefault(d => d.Name.Contains("炮孔布置"))
                       ?? datasets.FirstOrDefault();

            if (info is not null)
            {
                var dataset = HoleDatasetLibrary.Load(info);
                _points = dataset.Holes.ToList();
                Section = dataset.Section;
                DatasetText = info.Name;
            }
        }
        catch (Exception ex)
        {
            StatusText = "载入布孔数据失败：" + ex.Message;
        }

        if (_points.Count == 0)
        {
            Section = TunnelSection.CreateArch(12.40, 3.00);
            DatasetText = "内置半圆拱断面";
        }

        _leftSolver = new BoomSolver(BoomSpec.Left(8.0), Section);
        _rightSolver = new BoomSolver(BoomSpec.Right(8.0), Section);

        RebuildHoles();

        StatusText = _points.Count > 0
            ? $"{DatasetText}：{Holes.Count} 个炮孔，断面 {Section.Span:F2} × {Section.Height:F2} m。"
            : "未找到布孔 CSV，使用内置半圆拱示例断面。";
    }

    private void RebuildHoles()
    {
        Holes.Clear();

        for (var i = 0; i < _points.Count; i++)
        {
            var p = _points[i];
            Holes.Add(new HoleVm
            {
                Index = i,
                No = p.No,
                Type = p.KindText,
                X = p.X,
                Y = p.Z,
                ToeX = p.X + p.DirX * p.Depth,
                ToeY = p.Z + p.DirZ * p.Depth,
                Depth = p.Depth,
                AxialY = 0,
                DirX = p.DirX,
                DirY = p.DirY,
                DirZ = p.DirZ,
                Diameter = p.DiameterMm,
                IsRelief = p.Kind == HoleKind.Cut && p.DiameterMm > 60,
                TiltDeg = p.TiltDeg,
                Fill = PendingFill,
                Stroke = PendingStroke,
                StrokeThickness = 1,
                StateText = "待钻",
                StateBrush = new SolidColorBrush(Color.Parse("#5C7484")),
                DepthText = "0.00",
                AngleText = (90 - p.TiltDeg).ToString("F1"),
                BoomText = p.X < 0 ? "左臂" : "右臂"
            });
        }
    }

    // ===================== 显示集合 =====================

    private void RefreshDisplayHoles()
    {
        DisplayHoles.Clear();

        foreach (var hole in Holes)
        {
            // “隐藏”：收起对侧臂尚未完成的规划孔，只保留当前臂与本臂孔位
            if (IsHidden && hole.ArmIndex >= 0 && hole.ArmIndex != SelectedArmIndex &&
                hole.StateText != "已完成")
                continue;

            DisplayHoles.Add(hole);
        }

        CanvasRevision++;
    }

    private void RefreshLocalHoles()
    {
        LocalHoles.Clear();

        var arm = SelectedArm;
        foreach (var hole in Holes)
        {
            if (hole.ArmIndex == arm.Index) LocalHoles.Add(hole);
        }

        if (LocalHoles.Count == 0)
        {
            // 尚未规划：按左右侧粗略分区，保证局部图有内容
            foreach (var hole in Holes)
            {
                if (arm.Index == 0 ? hole.X < 0 : hole.X >= 0) LocalHoles.Add(hole);
            }
        }

        CanvasRevision++;
    }

    private void UpdateMarks()
    {
        ArmMarks.Clear();

        foreach (var arm in Arms)
        {
            var hole = arm.CurrentHole;
            if (hole is null) continue;

            ArmMarks.Add(new ArmMarkVm
            {
                Label = arm.Name,
                X = hole.X,
                Y = hole.Y,
                Axial = hole.AxialY,
                Brush = MarkBrush
            });
        }

        CanvasRevision++;
    }

    private void UpdateInfoTexts()
    {
        LeftInfoText = "左臂\n" + Arms[0].PoseText;
        RightInfoText = "右臂\n" + Arms[1].PoseText;
    }

    // ===================== 规划 =====================

    [RelayCommand]
    private void AutoPlan()
    {
        if (Holes.Count == 0) return;

        var (left, right) = DrillingPathPlanner.Split(_points);
        var leftOrder = DrillingPathPlanner.Order(left, true);
        var rightOrder = DrillingPathPlanner.Order(right, false);

        foreach (var arm in Arms)
        {
            arm.Planned.Clear();
            arm.Queue.Clear();
        }

        var order = 0;

        void Add(DrillArmVm arm, IEnumerable<HolePoint> points)
        {
            foreach (var point in points)
            {
                var index = _points.IndexOf(point);
                if (index < 0 || index >= Holes.Count) continue;

                var hole = Holes[index];
                if (hole.StateText == "已完成") continue;

                hole.ArmIndex = arm.Index;
                hole.BoomText = arm.Name;
                hole.PathOrder = ++order;

                arm.Planned.Add(hole);
                arm.Queue.Enqueue(hole);
            }
        }

        Add(Arms[0], leftOrder);
        Add(Arms[1], rightOrder);

        RefreshDisplayHoles();
        RefreshLocalHoles();
        NotifyPlanning();

        StatusText = $"自动规划完成：左臂 {Arms[0].Planned.Count} 孔 / 右臂 {Arms[1].Planned.Count} 孔。";
    }

    [RelayCommand]
    private void ManualPlan()
    {
        IsManualPlanning = !IsManualPlanning;
        StatusText = IsManualPlanning
            ? "手动规划：在图上点选炮孔，按顺序加入当前臂的规划队列。"
            : "已退出手动规划。";
    }

    [RelayCommand]
    private void ClearArm()
    {
        var arm = SelectedArm;

        foreach (var hole in arm.Planned)
        {
            hole.ArmIndex = -1;
            hole.PathOrder = 0;
        }

        arm.Planned.Clear();
        arm.Queue.Clear();
        arm.CurrentHole = null;
        arm.CurrentDepth = 0;
        arm.IsDrilling = false;
        arm.StateText = "待机";

        RefreshLocalHoles();
        RefreshDisplayHoles();
        NotifyPlanning();
        StatusText = $"{arm.Name}规划已清除。";
    }

    [RelayCommand]
    private void ClearLastHole()
    {
        var last = Holes.LastOrDefault(h => h.StateText == "已完成");
        if (last is null)
        {
            StatusText = "没有已完成的孔可撤销。";
            return;
        }

        last.StateText = "待钻";
        last.Fill = PendingFill;
        last.Stroke = PendingStroke;
        last.StrokeThickness = 1;
        last.DepthText = "0.00";

        var arm = Arms[Math.Clamp(last.ArmIndex, 0, 1)];
        arm.DoneCount = Holes.Count(h => h.StateText == "已完成" && h.ArmIndex == arm.Index);
        arm.Planned.Add(last);
        arm.Queue.Enqueue(last);

        RefreshDisplayHoles();
        RefreshLocalHoles();
        NotifyPlanning();
        StatusText = $"已清除 No.{last.No} 的完成状态，重新排入 {arm.Name}。";
    }

    [RelayCommand]
    private void ClearDoneHoles()
    {
        foreach (var hole in Holes)
        {
            if (hole.StateText != "已完成") continue;
            hole.StateText = "待钻";
            hole.Fill = PendingFill;
            hole.Stroke = PendingStroke;
            hole.StrokeThickness = 1;
            hole.DepthText = "0.00";
        }

        foreach (var arm in Arms) arm.DoneCount = 0;

        AutoPlan();
        StatusText = "已清除全部已钻完成孔，并重新生成规划。";
    }

    // ===================== 执行 =====================

    [RelayCommand]
    private void ExecuteArm()
    {
        var arm = SelectedArm;

        if (arm.CurrentHole is null && !TryTakeNext(arm))
        {
            StatusText = $"{arm.Name}没有待钻孔，请先自动规划或手动规划。";
            return;
        }

        IsAutoDrilling = false;
        arm.IsDrilling = true;
        arm.StateText = "钻进中";
        StatusText = $"{arm.Name}开始执行：No.{arm.CurrentHole?.No}。";
        UpdateMarks();
        UpdateInfoTexts();
    }

    [RelayCommand]
    private void StopArm()
    {
        foreach (var arm in Arms)
        {
            arm.IsDrilling = false;
            arm.StateText = "已停止";
        }

        IsAutoDrilling = false;
        StatusText = "全部臂架已停止。";
    }

    [RelayCommand]
    private void ToggleAutoDrill()
    {
        IsAutoDrilling = !IsAutoDrilling;

        if (IsAutoDrilling)
        {
            foreach (var arm in Arms)
            {
                if (arm.CurrentHole is null) TryTakeNext(arm);
                arm.IsDrilling = arm.CurrentHole is not null;
                arm.StateText = arm.IsDrilling ? "自动凿岩" : "待机";
            }

            StatusText = $"自动凿岩已启动（{DrillModes[Math.Clamp(DrillModeIndex, 0, DrillModes.Count - 1)]}）。";
        }
        else
        {
            foreach (var arm in Arms)
            {
                arm.IsDrilling = false;
                arm.StateText = "待机";
            }

            StatusText = "自动凿岩已停止。";
        }

        UpdateMarks();
        UpdateInfoTexts();
    }

    [RelayCommand]
    private void EmergencyStop()
    {
        foreach (var arm in Arms)
        {
            arm.IsDrilling = false;
            arm.StateText = "急停";

            if (arm.CurrentHole is { } hole)
            {
                hole.StateText = "待钻";
                hole.Fill = PendingFill;
                hole.Stroke = PendingStroke;
                hole.StrokeThickness = 1;
                hole.DepthText = "0.00";
            }

            arm.CurrentHole = null;
            arm.CurrentDepth = 0;
        }

        IsAutoDrilling = false;
        CurrentHole = null;
        UpdateMarks();
        RefreshDisplayHoles();
        RefreshLocalHoles();
        NotifyPlanning();
        StatusText = "急停已触发：所有进给停止、当前孔复位。";
    }

    // ===================== 钻孔推进 =====================

    private bool TryTakeNext(DrillArmVm arm)
    {
        while (arm.Queue.Count > 0)
        {
            var hole = arm.Queue.Dequeue();
            if (hole.StateText == "已完成") continue;

            hole.StateText = "钻进中";
            hole.Fill = DoingFill;
            hole.Stroke = DoingStroke;
            hole.StrokeThickness = 2;

            arm.CurrentHole = hole;
            arm.CurrentDepth = 0;
            arm.StateText = arm.IsDrilling ? "钻进中" : "已就位";

            CurrentHole = hole;
            UpdatePose(arm, hole);
            UpdateMarks();
            UpdateInfoTexts();
            NotifyPlanning();
            return true;
        }

        return false;
    }

    private void OnTick()
    {
        foreach (var arm in Arms)
        {
            if (!arm.IsDrilling) continue;

            if (arm.CurrentHole is null && !TryTakeNext(arm))
            {
                arm.IsDrilling = false;
                arm.StateText = "规划完成";
                continue;
            }

            var hole = arm.CurrentHole;
            if (hole is null) continue;

            var step = 0.045 + _rnd.NextDouble() * 0.02;
            arm.CurrentDepth = Math.Min(arm.PlanDepth, arm.CurrentDepth + step);
            hole.DepthText = arm.CurrentDepth.ToString("F2");

            UpdatePose(arm, hole);

            if (arm.CurrentDepth >= arm.PlanDepth - 1e-6)
                CompleteHole(arm, hole);
        }

        UpdateParamTable();
        UpdateInfoTexts();
    }

    private void CompleteHole(DrillArmVm arm, HoleVm hole)
    {
        hole.StateText = "已完成";
        hole.Fill = DoneFill;
        hole.Stroke = DoneStroke;
        hole.StrokeThickness = 1;
        hole.DepthText = arm.PlanDepth.ToString("F2");

        arm.DoneCount = Holes.Count(h => h.StateText == "已完成" && h.ArmIndex == arm.Index);
        arm.CurrentHole = null;
        arm.CurrentDepth = 0;
        arm.StateText = "已到位";

        if (!TryTakeNext(arm))
        {
            arm.IsDrilling = false;
            arm.StateText = "规划完成";
            StatusText = $"{arm.Name}规划孔已全部完成（{arm.DoneCount} 孔）。";
        }

        RefreshDisplayHoles();
        RefreshLocalHoles();
        NotifyPlanning();
    }

    private void UpdatePose(DrillArmVm arm, HoleVm hole)
    {
        if (hole.Index < 0 || hole.Index >= _points.Count) return;

        var solver = arm.Index == 0 ? _leftSolver : _rightSolver;
        var solution = solver.Solve(_points[hole.Index]);
        var tip = solver.Forward(solution.Pose);

        arm.PoseText =
            $"末端坐标({tip.X:F2},{tip.Y:F2},{tip.Z:F2})\n" +
            $"翻转:{solution.Pose.YawDeg:F1}°\n" +
            $"俯仰:{solution.Pose.PitchDeg:F1}°\n" +
            $"补偿:{solution.Pose.CompDeg:F1}°";
    }

    private void UpdateParamTable()
    {
        var drilling = Arms.Any(a => a.IsDrilling);

        double Walk(double min, double max, double span)
            => drilling ? Math.Round(Math.Clamp((min + max) / 2 + (_rnd.NextDouble() - 0.5) * (max - min), min, max), 2) : 0.00;

        ParamRows[0].LeftText = Walk(60, 180, 8).ToString("F2");
        ParamRows[0].RightText = Walk(60, 180, 8).ToString("F2");
        ParamRows[1].LeftText = Walk(40, 120, 6).ToString("F2");
        ParamRows[1].RightText = Walk(40, 120, 6).ToString("F2");
        ParamRows[2].LeftText = Walk(50, 140, 7).ToString("F2");
        ParamRows[2].RightText = Walk(50, 140, 7).ToString("F2");
        ParamRows[3].LeftText = (Arms[0].CurrentDepth * 1000).ToString("F0");
        ParamRows[3].RightText = (Arms[1].CurrentDepth * 1000).ToString("F0");
        ParamRows[4].LeftText = Walk(20, 70, 4).ToString("F2");
        ParamRows[4].RightText = Walk(20, 70, 4).ToString("F2");
        ParamRows[5].LeftText = Walk(1.5, 4.5, 0.3).ToString("F2");
        ParamRows[5].RightText = Walk(1.5, 4.5, 0.3).ToString("F2");
    }

    // ===================== 弹出面板 =====================

    [RelayCommand]
    private void OpenPanel(string key)
    {
        if (ActivePanel == key)
        {
            ClosePanel();
            return;
        }

        ActivePanel = key;
        PanelTitle = key switch
        {
            "machine" => "钻机参数",
            "mode" => "钻孔模式",
            _ => "常用数据"
        };
    }

    [RelayCommand]
    private void ClosePanel()
    {
        ActivePanel = null;
        PanelTitle = string.Empty;
    }

    private void BuildPanelRows()
    {
        PanelRows.Clear();

        switch (ActivePanel)
        {
            case "machine":
                PanelRows.Add(new InfoRowVm { Label = "钻具型号", Value = "T38-R32", Note = "#07" });
                PanelRows.Add(new InfoRowVm { Label = "钎头直径", Value = "φ45 mm", Note = "周边孔 φ45" });
                PanelRows.Add(new InfoRowVm { Label = "最大孔深", Value = "5.20 m", Note = "当前设置 " + SelectedArm.PlanDepth.ToString("F2") + " m" });
                PanelRows.Add(new InfoRowVm { Label = "推进行程", Value = "1.80 m", Note = "分两级推进" });
                PanelRows.Add(new InfoRowVm { Label = "冲击压力上限", Value = "220 bar", Note = "超限自动降速" });
                PanelRows.Add(new InfoRowVm { Label = "旋转压力上限", Value = "160 bar", Note = "波动 ±1.5 bar 提示夹钎" });
                PanelRows.Add(new InfoRowVm { Label = "开孔方式", Value = "低冲击开孔", Note = "0.3 m 后转全参数" });
                break;

            case "mode":
                PanelRows.Add(new InfoRowVm { Label = "当前模式", Value = DrillModes[Math.Clamp(DrillModeIndex, 0, DrillModes.Count - 1)] });
                PanelRows.Add(new InfoRowVm { Label = "左臂规划孔数", Value = Arms[0].Planned.Count.ToString() });
                PanelRows.Add(new InfoRowVm { Label = "右臂规划孔数", Value = Arms[1].Planned.Count.ToString() });
                PanelRows.Add(new InfoRowVm { Label = "设置孔深", Value = SelectedArm.PlanDepth.ToString("F2") + " m" });
                PanelRows.Add(new InfoRowVm { Label = "自动凿岩", Value = IsAutoDrilling ? "运行中" : "未启动" });
                break;

            default:
                PanelRows.Add(new InfoRowVm { Label = "当前作业臂", Value = SelectedArm.Name });
                PanelRows.Add(new InfoRowVm { Label = "当前孔", Value = SelectedArm.CurrentHole is { } h ? "No." + h.No : "--" });
                PanelRows.Add(new InfoRowVm { Label = "已钻孔数", Value = DoneProgressText });
                PanelRows.Add(new InfoRowVm { Label = "循环进度", Value = DonePercent.ToString("F1") + " %" });
                PanelRows.Add(new InfoRowVm { Label = "布孔数据", Value = DatasetText });
                PanelRows.Add(new InfoRowVm { Label = "断面", Value = $"{Section.Span:F2} × {Section.Height:F2} m" });
                PanelRows.Add(new InfoRowVm { Label = "当前视图", Value = SelectedViewText });
                break;
        }
    }

    /// <summary>界面上的孔深“− / +”按钮：参数为增量（如 "0.25" / "-0.25"）。</summary>
    [RelayCommand]
    private void StepPlanDepth(string delta)
    {
        var arm = SelectedArm;
        var v = double.TryParse(delta, out var d) ? d : 0;
        arm.PlanDepth = Math.Clamp(Math.Round(arm.PlanDepth + v, 2), 1.0, 6.0);
        StatusText = $"{arm.Name}设置孔深：{arm.PlanDepth:F2} m。";
        if (ActivePanel is not null) BuildPanelRows();
    }
}
