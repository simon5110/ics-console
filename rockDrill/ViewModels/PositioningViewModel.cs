using System;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using rockDrill.Models;

namespace rockDrill.ViewModels;

/// <summary>定位页臂架示意图中的一条臂（基座 → 大臂 → 推进梁 → 钻头）。</summary>
public sealed class ArmSchematicVm
{
    public string Name { get; init; } = string.Empty;
    public string Prefix { get; init; } = string.Empty;

    public double BaseX { get; init; }
    public double BaseZ { get; init; }

    public double ElbowX { get; init; }
    public double ElbowY { get; init; }
    public double ElbowZ { get; init; }

    public double TipX { get; init; }
    public double TipY { get; init; }
    public double TipZ { get; init; }

    public IBrush Brush { get; init; } = Brushes.LimeGreen;
    public IBrush BeamBrush { get; init; } = Brushes.LightGreen;
    public bool IsSelected { get; init; }
}

/// <summary>只读信息行（标定 / 隧道线 / 全站仪状态面板用）。</summary>
public sealed class InfoLineVm
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
}

/// <summary>
/// 定位页（对齐 doc/现场测试视频.mp4 的现场 HMI）：
/// 左列「标定 / 隧道线 / 返回」，右列「全站仪 / 快速定位 / 确认」，
/// 中间在「全站仪定位」（左右后视棱镜坐标 + 重置/定位）与
/// 「臂架定位」（台车里程 / 掌子面里程 + 左臂右臂 + 臂架示意图）之间切换。
/// </summary>
public partial class PositioningViewModel : ViewModelBase
{
    private readonly Random _rnd = new();

    // ===================== 模式 =====================

    [ObservableProperty] private int _windowMode;          // 0 = 全站仪定位，1 = 臂架定位
    [ObservableProperty] private int _selectedArmIndex;    // 臂架定位中选中的臂

    // ===================== 后视棱镜 =====================

    [ObservableProperty] private string _leftX = "-1.633";
    [ObservableProperty] private string _leftY = "14.706";
    [ObservableProperty] private string _leftZ = "-1.387";
    [ObservableProperty] private string _rightX = "0.295";
    [ObservableProperty] private string _rightY = "14.982";
    [ObservableProperty] private string _rightZ = "0.016";

    // ===================== 定位结果 =====================

    [ObservableProperty] private string _statusText = "未定位";
    [ObservableProperty] private bool _isLocated;
    [ObservableProperty] private string _deviationText = "— —";
    [ObservableProperty] private string _lastTimeText = "— —";
    [ObservableProperty] private string _hintText = "输入左右后视棱镜坐标后点击「定位」，或先「快速定位」完成臂架粗定位。";

    // ===================== 臂架定位 =====================

    [ObservableProperty] private string _trolleyChainage = "K89+037";
    [ObservableProperty] private string _faceChainage = "K89+041.50";
    [ObservableProperty] private double _faceDistance = 4.50;
    [ObservableProperty] private string _selectedArmText = "右臂";
    [ObservableProperty] private string _armOffsetText = "— —";

    // ===================== 隧道线 =====================

    [ObservableProperty] private string _lineName = "左线";
    [ObservableProperty] private string _chainageText = "K89 + 037";
    [ObservableProperty] private string _azimuthText = "327° 18′ 24″";
    [ObservableProperty] private string _slopeText = "-1.2 %";
    [ObservableProperty] private string _sectionText = "12.40 × 9.20 m";
    [ObservableProperty] private string _advanceText = "3.50 m";

    // ===================== 面板 =====================

    [ObservableProperty] private string? _activePanel;
    [ObservableProperty] private string _panelTitle = string.Empty;

    // 标定面板输入
    [ObservableProperty] private string _calibPrismConst = "-30.0";
    [ObservableProperty] private string _calibKnownDistance = "25.000";
    [ObservableProperty] private string _calibMeasuredDistance = "25.032";
    [ObservableProperty] private string _calibResultText = "— —";

    // 隧道线面板输入
    [ObservableProperty] private string _editLineName = "左线";
    [ObservableProperty] private string _editChainage = "K89 + 037";
    [ObservableProperty] private string _editAzimuth = "327.31";
    [ObservableProperty] private string _editSlope = "-1.2";
    [ObservableProperty] private string _editSpan = "12.40";
    [ObservableProperty] private string _editHeight = "9.20";

    public ObservableCollection<ArmSchematicVm> Arms { get; } = new();
    public ObservableCollection<InfoLineVm> PanelLines { get; } = new();
    public ObservableCollection<string> ArmOptions { get; } = new() { "左臂", "右臂" };

    [ObservableProperty] private int _canvasRevision;
    [ObservableProperty] private TunnelSection _section = TunnelSection.CreateArch(12.40, 3.00);

    public PositioningViewModel()
    {
        RebuildArms();
        FaceChainage = FormatChainage(TrolleyChainage, FaceDistance);
        ApplyCommandLineOverrides();
    }

    /// <summary>调试 / 演示开关：<c>--armview</c> 打开臂架定位，<c>--panel=calib|line|instrument</c> 打开面板。</summary>
    private void ApplyCommandLineOverrides()
    {
        foreach (var arg in Environment.GetCommandLineArgs())
        {
            if (arg.Equals("--armview", StringComparison.OrdinalIgnoreCase))
            {
                WindowMode = 1;
            }
            else if (arg.StartsWith("--panel=", StringComparison.OrdinalIgnoreCase))
            {
                switch (arg["--panel=".Length..].ToLowerInvariant())
                {
                    case "calib":
                        OpenCalibration();
                        break;
                    case "line":
                        OpenTunnelLine();
                        break;
                    case "instrument":
                        OpenInstrument();
                        break;
                }
            }
        }
    }

    // ===================== 派生属性 =====================

    public bool IsStationView => WindowMode == 0;

    public bool IsArmView => WindowMode == 1;

    public string TitleText => WindowMode == 0 ? "全站仪定位" : "臂架定位";

    public bool IsPanelOpen => ActivePanel is not null;

    public bool IsCalibPanel => ActivePanel == "calib";

    public bool IsLinePanel => ActivePanel == "line";

    public bool IsInstrumentPanel => ActivePanel == "instrument";

    public double SchematicDepth => Math.Clamp(FaceDistance, 2.5, 9.0);

    partial void OnWindowModeChanged(int value)
    {
        OnPropertyChanged(nameof(IsStationView));
        OnPropertyChanged(nameof(IsArmView));
        OnPropertyChanged(nameof(TitleText));
        if (value == 1) RebuildArms();
    }

    partial void OnSelectedArmIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedArmText));
        RebuildArms();
    }

    partial void OnFaceDistanceChanged(double value)
    {
        OnPropertyChanged(nameof(SchematicDepth));
        FaceChainage = FormatChainage(TrolleyChainage, value);
        RebuildArms();
    }

    partial void OnTrolleyChainageChanged(string value)
        => FaceChainage = FormatChainage(value, FaceDistance);

    partial void OnActivePanelChanged(string? value)
    {
        OnPropertyChanged(nameof(IsPanelOpen));
        OnPropertyChanged(nameof(IsCalibPanel));
        OnPropertyChanged(nameof(IsLinePanel));
        OnPropertyChanged(nameof(IsInstrumentPanel));
        if (value is not null) BuildPanelLines();
    }

    partial void OnCalibMeasuredDistanceChanged(string value) => ComputeCalibration();

    partial void OnCalibKnownDistanceChanged(string value) => ComputeCalibration();

    /// <summary>台车里程 + 距离 → 掌子面里程（如 K89 + 037 加 4.5 m → K89+41.50）。</summary>
    private static string FormatChainage(string trolley, double distance)
    {
        var plus = trolley.IndexOf('+');
        if (plus <= 0) return "— —";

        var kmPart = trolley[..plus];
        var mPart = trolley[(plus + 1)..];

        var kmDigits = new string(Array.FindAll(kmPart.ToCharArray(), char.IsDigit));
        if (!int.TryParse(kmDigits, out var km)) return "— —";

        if (!double.TryParse(mPart.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var meter))
            return "— —";

        var total = km * 1000 + meter + distance;
        var kilo = (int)Math.Floor(total / 1000);
        var rest = total - kilo * 1000;

        var letters = new string(Array.FindAll(kmPart.ToCharArray(), char.IsLetter));
        if (letters.Length == 0) letters = "K";

        return $"{letters}{kilo}+{rest:F2}";
    }

    // ===================== 臂架示意图数据 =====================

    private void RebuildArms()
    {
        Arms.Clear();

        var distance = SchematicDepth;
        Section = TunnelSection.CreateArch(12.40, 3.00);

        for (var i = 0; i < 2; i++)
        {
            var spec = i == 0 ? BoomSpec.Left(distance) : BoomSpec.Right(distance);
            var solver = new BoomSolver(spec, Section);

            var targetX = i == 0 ? -1.80 : 1.80;
            var target = new HolePoint
            {
                No = i + 1,
                X = targetX,
                Z = 5.40,
                Depth = 3.6,
                DirY = 1
            };

            var solution = solver.Solve(target);
            var pose = solution.Reachable ? solution.Pose : new BoomPose(i == 0 ? 12 : -12, 18, 1.2, 0.8, 0);

            var yaw = pose.YawDeg * Math.PI / 180.0;
            var pitch = pose.PitchDeg * Math.PI / 180.0;

            var ux = Math.Sin(yaw) * Math.Cos(pitch);
            var uy = Math.Cos(yaw) * Math.Cos(pitch);
            var uz = Math.Sin(pitch);

            var bx = spec.BaseX;
            var by = -distance;
            var bz = spec.BaseZ;

            var elbowLen = spec.BoomLength * 0.62;
            var ex = bx + ux * elbowLen;
            var ey = by + uy * elbowLen;
            var ez = bz + uz * elbowLen;

            // 推进梁对准掌子面上的目标孔口（轴向 y = 0）
            var tx = targetX;
            var ty = 0.0;
            var tz = 5.40;

            var selected = i == SelectedArmIndex;

            Arms.Add(new ArmSchematicVm
            {
                Name = i == 0 ? "左臂" : "右臂",
                Prefix = i == 0 ? "L_" : "R_",
                BaseX = bx,
                BaseZ = bz,
                ElbowX = ex,
                ElbowY = ey,
                ElbowZ = ez,
                TipX = tx,
                TipY = ty,
                TipZ = tz,
                Brush = selected
                    ? new SolidColorBrush(Color.Parse(i == 0 ? "#2AD07C" : "#23A9F0"))
                    : new SolidColorBrush(Color.Parse(i == 0 ? "#1C5C46" : "#1B4A63")),
                BeamBrush = new SolidColorBrush(Color.Parse(selected ? "#9BF2C6" : "#4E7F96")),
                IsSelected = selected
            });
        }

        CanvasRevision++;
    }

    // ===================== 命令：左列 =====================

    /// <summary>标定：棱镜常数与测距校核。</summary>
    [RelayCommand]
    private void OpenCalibration()
    {
        ActivePanel = "calib";
        PanelTitle = "棱镜标定";
        ComputeCalibration();
    }

    /// <summary>隧道线：线路参数设置。</summary>
    [RelayCommand]
    private void OpenTunnelLine()
    {
        ActivePanel = "line";
        PanelTitle = "隧道线设置";

        EditLineName = LineName;
        EditChainage = ChainageText;
        EditAzimuth = "327.31";
        EditSlope = "-1.2";
        EditSpan = "12.40";
        EditHeight = "9.20";
    }

    /// <summary>返回：臂架定位时退回全站仪定位，否则提示已回到上一个工序。</summary>
    [RelayCommand]
    private void Back()
    {
        if (WindowMode == 1)
        {
            WindowMode = 0;
            StatusText = "已返回全站仪定位";
            HintText = "可重新输入后视棱镜坐标并执行定位。";
            return;
        }

        StatusText = IsLocated ? "定位结果已保持" : "未定位";
        HintText = "已返回上一个工序；再次定位前请确认棱镜坐标。";
    }

    // ===================== 命令：右列 =====================

    /// <summary>全站仪：仪器状态面板。</summary>
    [RelayCommand]
    private void OpenInstrument()
    {
        ActivePanel = "instrument";
        PanelTitle = "全站仪状态";
    }

    /// <summary>快速定位：切换到臂架定位并模拟一次粗定位（测距 → 掌子面里程）。</summary>
    [RelayCommand]
    private void QuickLocate()
    {
        WindowMode = 1;

        FaceDistance = Math.Round(4.0 + _rnd.NextDouble() * 1.6, 2);
        FaceChainage = FormatChainage(TrolleyChainage, FaceDistance);
        ArmOffsetText = $"{( _rnd.NextDouble() * 2 - 1 ) * 0.06:+0.000;-0.000} m";
        StatusText = "快速定位完成";
        IsLocated = true;
        DeviationText = $"{_rnd.Next(4, 12)} mm";
        LastTimeText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        HintText = "快速定位已用激光测距得到掌子面里程，请确认台车里程后执行臂架「定位」。";
    }

    /// <summary>确认：接受当前定位结果。</summary>
    [RelayCommand]
    private void Confirm()
    {
        if (!IsLocated)
        {
            StatusText = "尚未定位";
            HintText = "请先执行「定位」或「快速定位」，确认操作已取消。";
            return;
        }

        LastTimeText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        StatusText = WindowMode == 1 ? "臂架定位已确认" : "定位已确认";
        HintText = "结果已写入当前工序，可进入扫描 / 布孔。";
    }

    // ===================== 命令：中间区 =====================

    [RelayCommand]
    private void Reset()
    {
        if (WindowMode == 1)
        {
            FaceDistance = 4.50;
            FaceChainage = FormatChainage(TrolleyChainage, FaceDistance);
            ArmOffsetText = "— —";
            StatusText = "已重置距离";
            HintText = "掌子面距离已恢复默认值，可重新定位。";
            RebuildArms();
            return;
        }

        LeftX = "-1.633";
        LeftY = "14.706";
        LeftZ = "-1.387";
        RightX = "0.295";
        RightY = "14.982";
        RightZ = "0.016";

        StatusText = "未定位";
        IsLocated = false;
        DeviationText = "— —";
        HintText = "坐标已重置为上次标定值，请重新执行定位。";

        if (ActivePanel is not null) BuildPanelLines();
    }

    /// <summary>定位：全站仪两点后方交会 / 臂架与掌子面关系解算。</summary>
    [RelayCommand]
    private void Locate()
    {
        if (WindowMode == 1)
        {
            ArmOffsetText = $"{( _rnd.NextDouble() * 2 - 1 ) * 0.04:+0.000;-0.000} m";
            StatusText = "臂架定位完成";
            IsLocated = true;
            DeviationText = $"{_rnd.Next(1, 5)} mm";
            LastTimeText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            HintText = $"{SelectedArmText}与掌子面关系已解算，偏差在允许范围内。";
            return;
        }

        StatusText = "已定位";
        IsLocated = true;
        DeviationText = $"{_rnd.Next(1, 6)} mm";
        LastTimeText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        HintText = "两点后方交会完成，残差在允许范围内，可进入臂架定位或扫描。";

        if (ActivePanel is not null) BuildPanelLines();
    }

    [RelayCommand]
    private void SelectArm(string side)
    {
        SelectedArmIndex = string.Equals(side, "R", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        StatusText = $"{SelectedArmText}已选中";
    }

    [RelayCommand]
    private void ClosePanel()
    {
        ActivePanel = null;
        PanelTitle = string.Empty;
    }

    // ===================== 标定 / 隧道线 =====================

    private void ComputeCalibration()
    {
        if (!double.TryParse(CalibKnownDistance, out var known) ||
            !double.TryParse(CalibMeasuredDistance, out var measured) || known <= 0)
        {
            CalibResultText = "— —";
            return;
        }

        var delta = (measured - known) * 1000.0;   // mm
        CalibResultText = $"Δ {delta:+0.0;-0.0} mm　建议棱镜常数 {( -delta):+0.0;-0.0} mm";
    }

    [RelayCommand]
    private void ApplyCalibration()
    {
        ComputeCalibration();
        HintText = $"棱镜常数已更新：{CalibPrismConst} mm。";
        StatusText = "标定已应用";
        ClosePanel();
    }

    [RelayCommand]
    private void ApplyTunnelLine()
    {
        LineName = EditLineName;
        ChainageText = EditChainage;
        AzimuthText = $"{EditAzimuth}°";
        SlopeText = $"{EditSlope} %";
        SectionText = $"{EditSpan} × {EditHeight} m";

        if (double.TryParse(EditSpan, out var span) && double.TryParse(EditHeight, out var height) && span > 2)
        {
            Section = TunnelSection.CreateArch(span, Math.Max(1.0, height - span / 2));
            RebuildArms();
        }

        TrolleyChainage = EditChainage.Replace(" ", string.Empty);
        FaceChainage = FormatChainage(TrolleyChainage, FaceDistance);

        HintText = $"隧道线已更新：{LineName} {ChainageText}，方位 {AzimuthText}，坡度 {SlopeText}。";
        StatusText = "隧道线已保存";
        ClosePanel();
    }

    private void BuildPanelLines()
    {
        PanelLines.Clear();

        switch (ActivePanel)
        {
            case "calib":
                PanelLines.Add(new InfoLineVm { Label = "棱镜型号", Value = "ATP1 圆棱镜", Note = "常数 -30 mm" });
                PanelLines.Add(new InfoLineVm { Label = "已知距离", Value = CalibKnownDistance + " m", Note = "钢尺量距" });
                PanelLines.Add(new InfoLineVm { Label = "实测距离", Value = CalibMeasuredDistance + " m", Note = "全站仪测距" });
                PanelLines.Add(new InfoLineVm { Label = "计算结果", Value = CalibResultText });
                break;

            case "line":
                PanelLines.Add(new InfoLineVm { Label = "线路", Value = LineName });
                PanelLines.Add(new InfoLineVm { Label = "当前里程", Value = ChainageText });
                PanelLines.Add(new InfoLineVm { Label = "中线方位", Value = AzimuthText });
                PanelLines.Add(new InfoLineVm { Label = "线路坡度", Value = SlopeText });
                PanelLines.Add(new InfoLineVm { Label = "断面尺寸", Value = SectionText });
                PanelLines.Add(new InfoLineVm { Label = "循环进尺", Value = AdvanceText });
                break;

            default:
                PanelLines.Add(new InfoLineVm { Label = "仪器型号", Value = "徕卡 TS16", Note = "1″ 级" });
                PanelLines.Add(new InfoLineVm { Label = "通讯", Value = "正常", Note = "RS232 / 蓝牙" });
                PanelLines.Add(new InfoLineVm { Label = "电量", Value = "86 %", Note = "约 6 h" });
                PanelLines.Add(new InfoLineVm { Label = "测距模式", Value = "棱镜 ±1 mm" });
                PanelLines.Add(new InfoLineVm { Label = "棱镜常数", Value = CalibPrismConst + " mm" });
                PanelLines.Add(new InfoLineVm { Label = "最后测量", Value = LastTimeText });
                break;
        }
    }
}
