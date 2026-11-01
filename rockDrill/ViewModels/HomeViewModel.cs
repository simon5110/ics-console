using System;
using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace rockDrill.ViewModels;

/// <summary>主页：作业总览</summary>
public partial class HomeViewModel : ViewModelBase
{
    private readonly Random _rnd = new();
    private readonly DispatcherTimer _timer;

    [ObservableProperty] private string _shiftText = "早班 08:00 - 16:00";
    [ObservableProperty] private string _operatorText = "操作员 张伟 · 班组 A";
    [ObservableProperty] private string _cycleText = "第 12 循环";
    [ObservableProperty] private string _faceText = "掌子面 K2+186.5 · 左线";

    [ObservableProperty] private int _doneHoles = 61;
    [ObservableProperty] private int _totalHoles = 96;
    [ObservableProperty] private double _roundProgress = 63.5;
    [ObservableProperty] private string _footageText = "2.10 / 3.50 m";
    [ObservableProperty] private string _remainText = "预计剩余 32 min";

    [ObservableProperty] private string _scanStateText = "扫描完成";
    [ObservableProperty] private string _calibStateText = "标定有效";
    [ObservableProperty] private string _autoStateText = "自动钻孔中";

    public ObservableCollection<BoomVm> Booms { get; } = new();
    public ObservableCollection<MetricVm> Metrics { get; } = new();
    public ObservableCollection<DeviceVm> Devices { get; } = new();
    public ObservableCollection<AlarmVm> RecentAlarms { get; } = new();

    public HomeViewModel()
    {
        Booms.Add(new BoomVm
        {
            Name = "左臂", SubName = "BOOM-A", State = "自动钻孔", StateBrush = Brushes.LimeGreen,
            HoleText = "No.62", DepthText = "1.84 m", TotalText = "38.6 m",
            Axes =
            {
                new AxisVm { Label = "回转角度", ValueText = "12.4°", Percent = 42, Brush = Brushes.DeepSkyBlue },
                new AxisVm { Label = "俯仰角度", ValueText = "3.8°", Percent = 31, Brush = Brushes.MediumPurple },
                new AxisVm { Label = "补偿行程", ValueText = "480 mm", Percent = 58, Brush = Brushes.MediumTurquoise },
            }
        });

        Booms.Add(new BoomVm
        {
            Name = "右臂", SubName = "BOOM-B", State = "退钎中", StateBrush = Brushes.Orange,
            HoleText = "No.61", DepthText = "3.50 m", TotalText = "36.2 m",
            Axes =
            {
                new AxisVm { Label = "回转角度", ValueText = "-9.6°", Percent = 38, Brush = Brushes.DeepSkyBlue },
                new AxisVm { Label = "俯仰角度", ValueText = "6.2°", Percent = 44, Brush = Brushes.MediumPurple },
                new AxisVm { Label = "补偿行程", ValueText = "362 mm", Percent = 45, Brush = Brushes.MediumTurquoise },
            }
        });

        Metrics.Add(new MetricVm { Label = "冲击压力", Value = 14.6, Unit = "MPa", Maximum = 25, Minimum = 0, Target = 0.72, Brush = Brushes.DeepSkyBlue });
        Metrics.Add(new MetricVm { Label = "回转压力", Value = 8.4, Unit = "MPa", Maximum = 20, Minimum = 0, Target = 0.5, Brush = Brushes.MediumPurple });
        Metrics.Add(new MetricVm { Label = "推进压力", Value = 6.2, Unit = "MPa", Maximum = 16, Minimum = 0, Target = 0.55, Brush = Brushes.MediumTurquoise });
        Metrics.Add(new MetricVm { Label = "冲洗水压", Value = 1.9, Unit = "MPa", Maximum = 4, Minimum = 0, Brush = Brushes.DeepSkyBlue });
        Metrics.Add(new MetricVm { Label = "回转转速", Value = 96, Unit = "r/min", Maximum = 150, Minimum = 0, Brush = Brushes.LimeGreen });
        Metrics.Add(new MetricVm { Label = "推进速度", Value = 0.42, Unit = "m/min", Maximum = 1.2, Minimum = 0, Brush = Brushes.Goldenrod });

        Devices.Add(new DeviceVm { Name = "发动机转速", ValueText = "1850", Unit = "r/min", Percent = 72 });
        Devices.Add(new DeviceVm { Name = "冷却水温", ValueText = "82", Unit = "°C", Percent = 55 });
        Devices.Add(new DeviceVm { Name = "液压油温", ValueText = "58", Unit = "°C", Percent = 48 });
        Devices.Add(new DeviceVm { Name = "主泵压力", ValueText = "21.4", Unit = "MPa", Percent = 78 });
        Devices.Add(new DeviceVm { Name = "系统电压", ValueText = "24.2", Unit = "V", Percent = 84 });

        RecentAlarms.Add(new AlarmVm
        {
            Time = "08:42:15", Code = "A-2103", Message = "右臂推进压力短时低于下限",
            Source = "右臂凿岩机", Level = "一般", Brush = Brushes.Goldenrod
        });
        RecentAlarms.Add(new AlarmVm
        {
            Time = "08:37:02", Code = "A-1874", Message = "左臂回转压力波动超限",
            Source = "左臂液压回路", Level = "严重", Brush = Brushes.OrangeRed
        });
        RecentAlarms.Add(new AlarmVm
        {
            Time = "08:21:44", Code = "A-0912", Message = "液压油温接近报警阈值",
            Source = "液压系统", Level = "一般", Brush = Brushes.Goldenrod
        });

        // 实时数值抖动，模拟现场采集
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _timer.Tick += (_, _) => Simulate();
        _timer.Start();
    }

    private void Simulate()
    {
        foreach (var m in Metrics)
        {
            var step = (m.Maximum - m.Minimum) * 0.012;
            var next = m.Value + (_rnd.NextDouble() - 0.5) * step;
            m.Value = Math.Round(Math.Clamp(next, m.Minimum + step, m.Maximum - step), 2);
            m.Display = m.Value.ToString("F2");
        }

        if (Booms.Count > 0)
        {
            var b = Booms[0];
            var depth = 1.84 + _rnd.NextDouble() * 0.06;
            b.DepthText = $"{depth:F2} m";
        }
    }
}
