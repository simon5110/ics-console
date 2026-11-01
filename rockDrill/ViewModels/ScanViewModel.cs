using System;
using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace rockDrill.ViewModels;

/// <summary>激光雷达扫描 + 数据分析。</summary>
public partial class ScanViewModel : ViewModelBase
{
    private readonly DispatcherTimer _timer;
    private readonly Random _rnd = new();

    [ObservableProperty] private double _progress;
    [ObservableProperty] private string _progressText = "0 %";
    [ObservableProperty] private string _statusText = "未开始";
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private string _pointCountText = "0";
    [ObservableProperty] private string _analysisStateText = "未分析";
    [ObservableProperty] private string _areaText = "— —";
    [ObservableProperty] private string _overCutText = "— —";
    [ObservableProperty] private string _underCutText = "— —";
    [ObservableProperty] private string _flatnessText = "— —";
    [ObservableProperty] private string _hintText = "点击「开始扫描」采集掌子面点云，扫描完成后可执行断面分析。";

    public ObservableCollection<MetricVm> ScanParams { get; } = new();

    public ScanViewModel()
    {
        ScanParams.Add(new MetricVm { Label = "扫描量程", Value = 30, Unit = "m", Maximum = 50, Brush = Avalonia.Media.Brushes.DeepSkyBlue });
        ScanParams.Add(new MetricVm { Label = "角分辨率", Value = 0.25, Unit = "°", Maximum = 1, Brush = Avalonia.Media.Brushes.MediumPurple });
        ScanParams.Add(new MetricVm { Label = "点云密度", Value = 68, Unit = "万点", Maximum = 120, Brush = Avalonia.Media.Brushes.MediumTurquoise });
        foreach (var m in ScanParams) m.Display = m.Value.ToString("0.##");

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _timer.Tick += (_, _) =>
        {
            Progress = Math.Min(100, Progress + 1.6);
            ProgressText = $"{Progress:F0} %";
            StatusText = Progress >= 100 ? "扫描完成" : $"扫描中 {Progress:F0}%";
            PointCountText = ((long)(Progress / 100 * 682_400)).ToString("N0");
            if (Progress >= 100)
            {
                IsScanning = false;
                HasData = true;
                _timer.Stop();
                HintText = "扫描完成，点云已对齐掌子面坐标系，可执行断面分析。";
            }
        };
    }

    [RelayCommand]
    private void StartScan()
    {
        Progress = 0;
        ProgressText = "0 %";
        HasData = false;
        IsScanning = true;
        StatusText = "扫描中 0%";
        AnalysisStateText = "未分析";
        AreaText = OverCutText = UnderCutText = FlatnessText = "— —";
        HintText = "激光雷达正在水平扫描，请勿遮挡扫描视场。";
        _timer.Start();
    }

    [RelayCommand]
    private void StopScan()
    {
        _timer.Stop();
        IsScanning = false;
        StatusText = Progress > 0 ? "已停止" : "未开始";
        HintText = "扫描已停止，可重新开始或导出已采集的部分点云。";
    }

    [RelayCommand]
    private void Analyze()
    {
        if (!HasData)
        {
            HintText = "请先完成扫描后再执行断面分析。";
            return;
        }

        AnalysisStateText = "分析完成";
        AreaText = $"{78 + _rnd.NextDouble() * 6:F1} m²";
        OverCutText = $"{0.08 + _rnd.NextDouble() * 0.1:F2} m";
        UnderCutText = $"{_rnd.NextDouble() * 0.04:F2} m";
        FlatnessText = $"{94 + _rnd.NextDouble() * 3:F1} %";
        HintText = "断面超欠挖在允许范围内，建议按当前布孔图执行钻孔。";
    }
}
