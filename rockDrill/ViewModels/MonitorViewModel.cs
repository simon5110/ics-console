using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using rockDrill.MyControls;

namespace rockDrill.ViewModels;

/// <summary>监测通道条目。</summary>
public partial class ChannelVm : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _valueText = string.Empty;
    [ObservableProperty] private IBrush _brush = Brushes.DeepSkyBlue;
    [ObservableProperty] private bool _isSelected;
}

/// <summary>凿岩监控：多通道趋势 + 流程变量。</summary>
public partial class MonitorViewModel : ViewModelBase
{
    private const int Points = 90;
    private readonly Random _rnd = new();
    private readonly DispatcherTimer _timer;

    private readonly List<double> _percussion = new();
    private readonly List<double> _rotation = new();
    private readonly List<double> _feed = new();

    [ObservableProperty] private int _tick;
    [ObservableProperty] private string _holeNoText = "No.62";
    [ObservableProperty] private string _stepText = "钻入";
    [ObservableProperty] private string _depthText = "1.84 m";
    [ObservableProperty] private string _peakText = "15.9 MPa";
    [ObservableProperty] private string _avgText = "14.3 MPa";
    [ObservableProperty] private string _waveText = "±0.62 MPa";
    [ObservableProperty] private string _drillTimeText = "03:42";

    public ObservableCollection<TrendSeries> Series { get; } = new();
    public ObservableCollection<ChannelVm> Channels { get; } = new();
    public ObservableCollection<MetricVm> FlowVars { get; } = new();
    public ObservableCollection<MetricVm> MainGauges { get; } = new();
    public ObservableCollection<MetricVm> SubGauges { get; } = new();

    public MonitorViewModel()
    {
        // 预置一段历史曲线
        for (var i = 0; i < Points; i++)
        {
            var t = i / (double)Points;
            _percussion.Add(14.3 + Math.Sin(t * Math.PI * 3) * 0.7 + (_rnd.NextDouble() - 0.5) * 0.4);
            _rotation.Add(8.2 + Math.Sin(t * Math.PI * 2.2 + 1) * 0.9 + (_rnd.NextDouble() - 0.5) * 0.5);
            _feed.Add(6.1 + Math.Sin(t * Math.PI * 1.6 + 2) * 0.55 + (_rnd.NextDouble() - 0.5) * 0.35);
        }

        Series.Add(new TrendSeries
        {
            Name = "冲击压力",
            Stroke = new SolidColorBrush(Color.Parse("#23A9F0")),
            Values = _percussion
        });
        Series.Add(new TrendSeries
        {
            Name = "回转压力",
            Stroke = new SolidColorBrush(Color.Parse("#9B8CFF")),
            Values = _rotation
        });
        Series.Add(new TrendSeries
        {
            Name = "推进压力",
            Stroke = new SolidColorBrush(Color.Parse("#2EE6D6")),
            Values = _feed
        });

        Channels.Add(new ChannelVm { Name = "左臂 · 冲击压力", ValueText = "14.3 MPa", Brush = new SolidColorBrush(Color.Parse("#23A9F0")), IsSelected = true });
        Channels.Add(new ChannelVm { Name = "左臂 · 回转压力", ValueText = "8.2 MPa", Brush = new SolidColorBrush(Color.Parse("#9B8CFF")) });
        Channels.Add(new ChannelVm { Name = "左臂 · 推进压力", ValueText = "6.1 MPa", Brush = new SolidColorBrush(Color.Parse("#2EE6D6")) });
        Channels.Add(new ChannelVm { Name = "右臂 · 冲击压力", ValueText = "13.9 MPa", Brush = new SolidColorBrush(Color.Parse("#2AD07C")) });
        Channels.Add(new ChannelVm { Name = "右臂 · 回转压力", ValueText = "7.8 MPa", Brush = new SolidColorBrush(Color.Parse("#F5A623")) });
        Channels.Add(new ChannelVm { Name = "冲洗水压力", ValueText = "1.9 MPa", Brush = new SolidColorBrush(Color.Parse("#ED4C5C")) });

        FlowVars.Add(new MetricVm { Label = "冲击压力", Value = 14.3, Unit = "MPa", Maximum = 25, Brush = Brushes.DeepSkyBlue });
        FlowVars.Add(new MetricVm { Label = "回转压力", Value = 8.2, Unit = "MPa", Maximum = 20, Brush = Brushes.MediumPurple });
        FlowVars.Add(new MetricVm { Label = "推进压力", Value = 6.1, Unit = "MPa", Maximum = 16, Brush = Brushes.MediumTurquoise });
        FlowVars.Add(new MetricVm { Label = "冲洗水流量", Value = 42, Unit = "L/min", Maximum = 80, Brush = Brushes.DeepSkyBlue });
        foreach (var m in FlowVars) m.Display = m.Value.ToString("F1");

        // 主仪表：冲击 / 回转 / 推进压力
        MainGauges.Add(new MetricVm { Label = "冲击压力", Value = 14.3, Unit = "MPa", Maximum = 25, Brush = new SolidColorBrush(Color.Parse("#23A9F0")) });
        MainGauges.Add(new MetricVm { Label = "回转压力", Value = 8.2, Unit = "MPa", Maximum = 20, Brush = new SolidColorBrush(Color.Parse("#9B8CFF")) });
        MainGauges.Add(new MetricVm { Label = "推进压力", Value = 6.1, Unit = "MPa", Maximum = 16, Brush = new SolidColorBrush(Color.Parse("#2EE6D6")) });

        // 副仪表：转速 / 推进速度 / 水压 / 主泵压力
        SubGauges.Add(new MetricVm { Label = "回转转速", Value = 96, Unit = "r/min", Maximum = 150, Brush = new SolidColorBrush(Color.Parse("#2AD07C")) });
        SubGauges.Add(new MetricVm { Label = "推进速度", Value = 0.42, Unit = "m/min", Maximum = 1.2, Brush = new SolidColorBrush(Color.Parse("#F5A623")) });
        SubGauges.Add(new MetricVm { Label = "冲洗水压", Value = 1.9, Unit = "MPa", Maximum = 4, Brush = new SolidColorBrush(Color.Parse("#23A9F0")) });
        SubGauges.Add(new MetricVm { Label = "主泵压力", Value = 21.4, Unit = "MPa", Maximum = 30, Brush = new SolidColorBrush(Color.Parse("#9B8CFF")) });

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Simulate();
        _timer.Start();
    }

    private void Simulate()
    {
        Push(_percussion, 14.3, 0.7);
        Push(_rotation, 8.2, 0.6);
        Push(_feed, 6.1, 0.4);

        Channels[0].ValueText = $"{_percussion[^1]:F1} MPa";
        Channels[1].ValueText = $"{_rotation[^1]:F1} MPa";
        Channels[2].ValueText = $"{_feed[^1]:F1} MPa";

        FlowVars[0].Value = Math.Round(_percussion[^1], 1);
        FlowVars[0].Display = FlowVars[0].Value.ToString("F1");
        FlowVars[1].Value = Math.Round(_rotation[^1], 1);
        FlowVars[1].Display = FlowVars[1].Value.ToString("F1");
        FlowVars[2].Value = Math.Round(_feed[^1], 1);
        FlowVars[2].Display = FlowVars[2].Value.ToString("F1");

        MainGauges[0].Value = Math.Round(_percussion[^1], 1);
        MainGauges[1].Value = Math.Round(_rotation[^1], 1);
        MainGauges[2].Value = Math.Round(_feed[^1], 1);

        SubGauges[0].Value = Math.Round(96 + (_rnd.NextDouble() - 0.5) * 6, 0);
        SubGauges[1].Value = Math.Round(0.42 + (_rnd.NextDouble() - 0.5) * 0.06, 2);
        SubGauges[2].Value = Math.Round(1.9 + (_rnd.NextDouble() - 0.5) * 0.12, 2);
        SubGauges[3].Value = Math.Round(21.4 + (_rnd.NextDouble() - 0.5) * 0.6, 1);

        PeakText = $"{_percussion.Max() + 1.1:F1} MPa";
        AvgText = $"{_percussion.Average():F1} MPa";

        Tick++; // 触发趋势图重绘
    }

    private void Push(List<double> data, double center, double amp)
    {
        data.RemoveAt(0);
        data.Add(center + (_rnd.NextDouble() - 0.5) * amp * 2);
    }
}
