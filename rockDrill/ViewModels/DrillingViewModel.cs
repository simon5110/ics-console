using System;
using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace rockDrill.ViewModels;

/// <summary>钻孔工步。</summary>
public partial class StepVm : ObservableObject
{
    [ObservableProperty] private int _index;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private IBrush _brush = Brushes.DimGray;
    [ObservableProperty] private string _stateText = "等待";
    [ObservableProperty] private bool _isActive;
}

/// <summary>钻孔控制页。</summary>
public partial class DrillingViewModel : ViewModelBase
{
    private readonly Random _rnd = new();
    private readonly DispatcherTimer _timer;

    [ObservableProperty] private int _selectedBoomIndex;
    [ObservableProperty] private string _holeNoText = "No.62";
    [ObservableProperty] private string _holeTypeText = "周边孔";
    [ObservableProperty] private string _depthText = "1.84";
    [ObservableProperty] private string _planDepthText = "3.50";
    [ObservableProperty] private double _depthPercent = 52.6;
    [ObservableProperty] private string _elapsedText = "01:12";
    [ObservableProperty] private string _remainText = "01:16";
    [ObservableProperty] private string _autoStateText = "自动钻孔运行中";
    [ObservableProperty] private bool _isRunning = true;
    [ObservableProperty] private string _runButtonText = "暂停钻孔";

    private double _depth = 1.84;

    public ObservableCollection<string> BoomOptions { get; } = new() { "左臂 BOOM-A", "右臂 BOOM-B" };
    public ObservableCollection<StepVm> Steps { get; } = new();
    public ObservableCollection<AxisVm> Axes { get; } = new();
    public ObservableCollection<MetricVm> Params { get; } = new();
    public ObservableCollection<SignalVm> Interlocks { get; } = new();
    public ObservableCollection<DrillRecordVm> RecentHoles { get; } = new();

    public DrillingViewModel()
    {
        var stepNames = new[] { "孔位定位", "对孔", "开孔", "钻入", "清孔", "退钎" };
        for (var i = 0; i < stepNames.Length; i++)
        {
            var done = i < 3;
            var active = i == 3;
            Steps.Add(new StepVm
            {
                Index = i + 1,
                Name = stepNames[i],
                Brush = done ? new SolidColorBrush(Color.Parse("#2AD07C"))
                    : active ? new SolidColorBrush(Color.Parse("#F5A623"))
                    : new SolidColorBrush(Color.Parse("#233642")),
                StateText = done ? "完成" : active ? "进行" : "等待",
                IsActive = active
            });
        }

        Axes.Add(new AxisVm { Label = "臂架回转", ValueText = "-12.4°", Percent = 42, Brush = Brushes.DeepSkyBlue });
        Axes.Add(new AxisVm { Label = "臂架俯仰", ValueText = "3.8°", Percent = 31, Brush = Brushes.MediumPurple });
        Axes.Add(new AxisVm { Label = "直臂伸缩", ValueText = "1240 mm", Percent = 55, Brush = Brushes.MediumTurquoise });
        Axes.Add(new AxisVm { Label = "凿岩补偿", ValueText = "480 mm", Percent = 58, Brush = Brushes.LimeGreen });
        Axes.Add(new AxisVm { Label = "凿岩翻转", ValueText = "0.0°", Percent = 50, Brush = Brushes.Goldenrod });
        Axes.Add(new AxisVm { Label = "凿岩偏摆", ValueText = "-2.1°", Percent = 47, Brush = Brushes.CornflowerBlue });

        Params.Add(new MetricVm { Label = "冲击压力", Value = 14.6, Unit = "MPa", Maximum = 25, Target = 0.72, Brush = Brushes.DeepSkyBlue });
        Params.Add(new MetricVm { Label = "回转压力", Value = 8.4, Unit = "MPa", Maximum = 20, Target = 0.5, Brush = Brushes.MediumPurple });
        Params.Add(new MetricVm { Label = "推进压力", Value = 6.2, Unit = "MPa", Maximum = 16, Target = 0.55, Brush = Brushes.MediumTurquoise });
        Params.Add(new MetricVm { Label = "冲洗水流量", Value = 42, Unit = "L/min", Maximum = 80, Brush = Brushes.DeepSkyBlue });
        Params.Add(new MetricVm { Label = "回转速度", Value = 96, Unit = "r/min", Maximum = 150, Brush = Brushes.LimeGreen });
        Params.Add(new MetricVm { Label = "钻具推进速度", Value = 0.42, Unit = "m/min", Maximum = 1.2, Brush = Brushes.Goldenrod });

        Interlocks.Add(new SignalVm { Name = "防碰撞联锁", ValueText = "已生效", State = "正常" });
        Interlocks.Add(new SignalVm { Name = "夹钎保护", ValueText = "已生效", State = "正常" });
        Interlocks.Add(new SignalVm { Name = "水压联锁", ValueText = "1.9 MPa", State = "正常" });
        Interlocks.Add(new SignalVm { Name = "支腿支撑", ValueText = "左/右 到位", State = "正常" });
        Interlocks.Add(new SignalVm { Name = "激光定位", ValueText = "偏差 8 mm", State = "正常" });
        Interlocks.Add(new SignalVm { Name = "急停回路", ValueText = "已复位", State = "正常" });

        RecentHoles.Add(new DrillRecordVm { No = "No.61", Depth = "3.50 m", Duration = "01:04", Result = "合格", Brush = Brushes.LimeGreen });
        RecentHoles.Add(new DrillRecordVm { No = "No.60", Depth = "3.52 m", Duration = "01:11", Result = "合格", Brush = Brushes.LimeGreen });
        RecentHoles.Add(new DrillRecordVm { No = "No.59", Depth = "3.55 m", Duration = "01:16", Result = "超深 5 cm", Brush = Brushes.Goldenrod });

        foreach (var m in Params) m.Display = m.Value.ToString("F2");

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _timer.Tick += (_, _) => Simulate();
        _timer.Start();
    }

    private void Simulate()
    {
        if (!IsRunning) return;

        foreach (var m in Params)
        {
            var step = (m.Maximum - m.Minimum) * 0.01;
            m.Value = Math.Round(Math.Clamp(m.Value + (_rnd.NextDouble() - 0.5) * step,
                m.Minimum + step, m.Maximum - step), 2);
            m.Display = m.Value.ToString("F2");
        }

        _depth = Math.Min(3.5, _depth + 0.02 + _rnd.NextDouble() * 0.02);
        DepthText = _depth.ToString("F2");
        DepthPercent = _depth / 3.5 * 100;

        var elapsed = TimeSpan.FromSeconds(72 + _rnd.Next(0, 2));
        ElapsedText = elapsed.ToString(@"mm\:ss");
        var remain = TimeSpan.FromSeconds(Math.Max(4, (int)((3.5 - _depth) / 0.42 * 60)));
        RemainText = remain.ToString(@"mm\:ss");
    }

    [RelayCommand]
    private void ToggleRun()
    {
        IsRunning = !IsRunning;
        AutoStateText = IsRunning ? "自动钻孔运行中" : "已暂停";
        RunButtonText = IsRunning ? "暂停钻孔" : "启动钻孔";
        var active = Steps[3];
        active.Brush = IsRunning ? new SolidColorBrush(Color.Parse("#F5A623")) : new SolidColorBrush(Color.Parse("#5C7484"));
        active.StateText = IsRunning ? "进行" : "暂停";
    }

    [RelayCommand]
    private void EmergencyStop()
    {
        IsRunning = false;
        AutoStateText = "急停已触发";
        RunButtonText = "启动钻孔";
        foreach (var s in Steps)
        {
            s.Brush = new SolidColorBrush(Color.Parse("#233642"));
            s.StateText = "等待";
        }
    }
}
