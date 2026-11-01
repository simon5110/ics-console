using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace rockDrill.ViewModels;

/// <summary>通用仪表项：压力/转速/行程等模拟量。</summary>
public partial class MetricVm : ObservableObject
{
    [ObservableProperty] private string _label = string.Empty;
    [ObservableProperty] private double _value;
    [ObservableProperty] private string _display = "0.0";
    [ObservableProperty] private string _unit = string.Empty;
    [ObservableProperty] private double _minimum;
    [ObservableProperty] private double _maximum = 100;
    [ObservableProperty] private double _target = -1;
    [ObservableProperty] private IBrush _brush = Brushes.DeepSkyBlue;

    public double Percent => Maximum - Minimum <= 0
        ? 0
        : System.Math.Clamp((Value - Minimum) / (Maximum - Minimum) * 100, 0, 100);

    partial void OnValueChanged(double value)
    {
        OnPropertyChanged(nameof(Percent));
        Display = Unit.Length == 0 ? value.ToString("F1") : value.ToString("F1");
    }
}

/// <summary>臂架姿态轴。</summary>
public partial class AxisVm : ObservableObject
{
    [ObservableProperty] private string _label = string.Empty;
    [ObservableProperty] private string _valueText = "0.0";
    [ObservableProperty] private double _percent;
    [ObservableProperty] private IBrush _brush = Brushes.DeepSkyBlue;
}

/// <summary>单臂状态卡片。</summary>
public partial class BoomVm : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _subName = string.Empty;
    [ObservableProperty] private string _state = "待机";
    [ObservableProperty] private string _stateClass = string.Empty;
    [ObservableProperty] private string _stateBrushKey = "JumboSub";
    [ObservableProperty] private string _holeText = "--";
    [ObservableProperty] private string _depthText = "0.00 m";
    [ObservableProperty] private string _totalText = "0.0 m";
    [ObservableProperty] private IBrush _stateBrush = Brushes.LightSlateGray;

    public System.Collections.ObjectModel.ObservableCollection<AxisVm> Axes { get; } = new();
}

/// <summary>设备/系统状态项。</summary>
public partial class DeviceVm : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _valueText = string.Empty;
    [ObservableProperty] private string _unit = string.Empty;
    [ObservableProperty] private string _state = "正常";
    [ObservableProperty] private bool _isWarning;
    [ObservableProperty] private IBrush _dotBrush = Brushes.LimeGreen;
    [ObservableProperty] private double _percent = 60;
}

/// <summary>报警条目。</summary>
public partial class AlarmVm : ObservableObject
{
    [ObservableProperty] private string _time = string.Empty;
    [ObservableProperty] private string _code = string.Empty;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private string _source = string.Empty;
    [ObservableProperty] private string _level = "一般";
    [ObservableProperty] private string _state = "未确认";
    [ObservableProperty] private IBrush _brush = Brushes.Orange;
    [ObservableProperty] private string _advice = string.Empty;
    [ObservableProperty] private bool _isConfirmed;
}

/// <summary>通讯/IO 信号项。</summary>
public partial class SignalVm : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _valueText = string.Empty;
    [ObservableProperty] private string _unit = string.Empty;
    [ObservableProperty] private string _state = "正常";
    [ObservableProperty] private IBrush _brush = Brushes.LimeGreen;
}
