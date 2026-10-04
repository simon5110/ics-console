using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace ava.ViewModels;

public partial class EmbeddedViewModel : ViewModelBase
{
    public ObservableCollection<MotorUnit> Motors { get; } = new();

    private readonly DispatcherTimer _timer;

    public EmbeddedViewModel()
    {
        // 两个电机面板（对应设计图上下两块）
        Motors.Add(new MotorUnit("电机 #1", baseLoad: 4, target: 10, maxSpeed: 50));
        Motors.Add(new MotorUnit("电机 #2", baseLoad: 8, target: 20, maxSpeed: 100));

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _timer.Tick += (_, _) =>
        {
            foreach (var m in Motors) m.Tick();
        };
        _timer.Start();
    }
}

/// <summary>单个电机控制单元</summary>
public partial class MotorUnit : ViewModelBase
{
    public string Name { get; }

    public double MaxSpeed { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _isPowerOn = true;

    [ObservableProperty] private double _loadPercent;
    [ObservableProperty] private double _temperature = 28;
    [ObservableProperty] private double _targetSpeed;
    [ObservableProperty] private double _actualSpeed;
    [ObservableProperty] private int _fineValue = 1;
    [ObservableProperty] private int _selectedModeIndex = 1; // 默认“标准”
    [ObservableProperty] private int _latencyMs = 87;
    [ObservableProperty] private int _fps = 100;

    public string StatusText => IsPowerOn ? "运行" : "停止";

    public ObservableCollection<double> RedValues { get; }
    public ObservableCollection<double> GreenValues { get; }
    public ObservableCollection<string> TimeLabels { get; }

    public ISeries[] Series { get; set; } = null!;
    public Axis[] XAxes { get; set; } = null!;
    public Axis[] YAxes { get; set; } = null!;

    public List<MotorMode> Modes { get; }

    private readonly double _baseLoad;
    private int _phase;

    /// <summary>左轴（实际转速）引用，用于动态伸缩量程上限</summary>
    private Axis? _speedAxis;

    public MotorUnit(string name, double baseLoad, double target, double maxSpeed)
    {
        Name = name;
        _baseLoad = baseLoad;
        MaxSpeed = maxSpeed;
        TargetSpeed = target;
        ActualSpeed = target;
        LoadPercent = baseLoad;

        var now = DateTime.Now;
        // 初始曲线同样围绕「当前档位×当前转速」上下浮动
        var center = FineValue * TargetSpeed;
        var initial = new List<double>(14);
        var seed = center;
        for (var i = 0; i < 14; i++)
        {
            seed = NextPoint(seed, center);
            initial.Add(seed);
        }
        RedValues = new ObservableCollection<double>(initial);
        GreenValues = new ObservableCollection<double>(Enumerable.Repeat(10.0, 14));
        TimeLabels = new ObservableCollection<string>(
            Enumerable.Range(0, 14).Select(i => now.AddSeconds(i).ToString("HH:mm:ss")));

        Modes = new List<MotorMode>
        {
            new("平稳", "M3,12C5,12 7,10 9,10C9,12 11,12 11,12H13C13,12 15,12 15,10C17,10 19,12 21,12V14C19,14 17,12 15,12C15,14 13,14 13,14H11C11,14 9,14 9,12C7,12 5,14 3,14V12M3,18C5,18 7,16 9,16C9,18 11,18 11,18H13C13,18 15,18 15,16C17,16 19,18 21,18V20C19,20 17,18 15,18C15,20 13,20 13,20H11C11,20 9,20 9,18C7,18 5,20 3,20V18Z"),
            new("标准", "M3,3H7V7H3V3M9,3H13V7H9V3M15,3H19V7H15V3M3,9H7V13H3V9M9,9H13V13H9V9M15,9H19V13H15V9M3,15H7V19H3V15M9,15H13V19H9V15M15,15H19V19H15V15Z"),
            new("高效", "M16,6L18.29,8.29L12,14.59L8,10.59L3.29,15.29L4.71,16.71L8,13.41L12,17.41L19.71,9.71L22,12V6H16Z"),
            new("狂暴", "M7,2V13H10V22L17,10H14L17,2H7Z"),
            new("自适应", "M12,2L16,6H13V18H16L12,22L8,18H11V6H8L12,2Z"),
        };

        InitChart();
    }

    /// <summary>
    /// 以「当前档位 × 当前转速」为中心生成下一个采样点：
    /// 40% 惯性向中心回归 + 上下随机浮动（幅度取中心值的 15%，至少 ±4），
    /// 只保证不为负，上限交给左轴动态量程。档位/转速改变后曲线会平滑游走到新中心。
    /// </summary>
    private static double NextPoint(double last, double center)
    {
        var amplitude = Math.Max(center * 0.15, 4);
        var nv = last
                 + (center - last) * 0.4
                 + (Random.Shared.NextDouble() - 0.5) * 2 * amplitude;
        return Math.Max(0, nv);
    }

    /// <summary>
    /// 按「当前档位×转速 峰值」与窗口内实际数据动态调整左轴上限：
    /// 向上对齐到 50 的倍数（最小 50），并带滞回——数据顶到上限才扩容，
    /// 回落到当前量程一半以下才缩容，避免刻度频繁跳变。
    /// </summary>
    private void UpdateSpeedAxisRange(double center)
    {
        if (_speedAxis is null) return;

        var dataMax = 0.0;
        foreach (var v in RedValues)
            if (v > dataMax) dataMax = v;

        // 理论峰值（中心 + 浮动余量），保证下一步冲高时也有空间
        var need = Math.Max(dataMax, center * 1.3 + 4);
        var target = Math.Max(50, Math.Ceiling(need / 50.0) * 50);
        var current = _speedAxis.MaxLimit ?? target;

        if (target > current || target < current * 0.55)
            _speedAxis.MaxLimit = target;
    }

    private void InitChart()
    {
        Series = new ISeries[]
        {
            new LineSeries<double>
            {
                Name = "实际",
                Values = RedValues,
                Stroke = new SolidColorPaint(new SKColor(235, 80, 80)) { StrokeThickness = 2.5f },
                Fill = null,
                GeometrySize = 0,
                LineSmoothness = 0.5,
                ScalesYAt = 0
            },
            new LineSeries<double>
            {
                Name = "目标",
                Values = GreenValues,
                Stroke = new SolidColorPaint(new SKColor(60, 170, 75)) { StrokeThickness = 2 },
                Fill = null,
                GeometrySize = 0,
                LineSmoothness = 0,
                ScalesYAt = 1
            }
        };

        XAxes = new[]
        {
            new Axis
            {
                Labels = TimeLabels,
                LabelsPaint = new SolidColorPaint(new SKColor(120, 120, 120)),
                TextSize = 11,
                SeparatorsPaint = new SolidColorPaint(new SKColor(238, 238, 238)) { StrokeThickness = 1 }
            }
        };

        _speedAxis = new Axis
        {
            // 左轴：实际转速，0 起步，上限随 档位×转速 动态伸缩
            MinLimit = 0,
            Position = AxisPosition.Start,
            LabelsPaint = new SolidColorPaint(new SKColor(120, 120, 120)),
            TextSize = 11,
            SeparatorsPaint = new SolidColorPaint(new SKColor(238, 238, 238)) { StrokeThickness = 1 }
        };

        YAxes = new[]
        {
            _speedAxis,
            // 右轴：0~100（目标 %）
            new Axis
            {
                MinLimit = 0, MaxLimit = 100,
                Position = AxisPosition.End,
                LabelsPaint = new SolidColorPaint(new SKColor(120, 120, 120)),
                TextSize = 11,
                SeparatorsPaint = new SolidColorPaint(new SKColor(245, 245, 245)) { StrokeThickness = 1 }
            }
        };

        // 初始量程按初始档位×转速计算
        UpdateSpeedAxisRange(FineValue * TargetSpeed);
    }

    /// <summary>实时刷新：滚动曲线、抖动指标</summary>
    public void Tick()
    {
        double nv;

        if (IsPowerOn)
        {
            // 运行：红色曲线围绕 当前档位×当前转速 上下浮动
            var center = FineValue * TargetSpeed;
            nv = NextPoint(RedValues[^1], center);
            // 数据中心变化后，左轴量程按需伸缩
            UpdateSpeedAxisRange(center);
        }
        else
        {
            // 停机/急停：曲线按 35% 快速衰减到 0 并贴底保持
            var last = RedValues[^1];
            nv = last <= 0.5 ? 0 : last * 0.35;
            UpdateSpeedAxisRange(0);
        }

        // 滚动红色曲线
        RedValues.RemoveAt(0);
        RedValues.Add(nv);

        // 滚动时间轴
        _phase++;
        TimeLabels.RemoveAt(0);
        TimeLabels.Add(DateTime.Now.AddSeconds(_phase).ToString("HH:mm:ss"));

        if (IsPowerOn)
        {
            // 抖动右上角实时指标
            LatencyMs = Random.Shared.Next(80, 96);
            Fps = Random.Shared.Next(96, 101);
            LoadPercent = Math.Round(Math.Clamp(_baseLoad + (Random.Shared.NextDouble() - 0.5) * 2.5, 0, 100), 0);
            Temperature = Math.Round(28 + (Random.Shared.NextDouble() - 0.5) * 2, 1);

            // 实际速度跟随设定速度（轻微波动）
            ActualSpeed = Math.Round(TargetSpeed + (Random.Shared.NextDouble() - 0.5) * 1.2, 1);
        }
        else
        {
            // 停机：实际速度与负载归零
            ActualSpeed = 0;
            LoadPercent = 0;
        }
    }

    [RelayCommand] private void FinePlus() => FineValue++;
    [RelayCommand] private void FineMinus() => FineValue = Math.Max(0, FineValue - 1);
    [RelayCommand] private void EmergencyStop()
    {
        IsPowerOn = false;
    }
}

/// <summary>运行模式（图标为 24x24 矢量）</summary>
public class MotorMode
{
    public string Name { get; }
    public Geometry Icon { get; }
    public MotorMode(string name, string path)
    {
        Name = name;
        Icon = Geometry.Parse(path);
    }
}
