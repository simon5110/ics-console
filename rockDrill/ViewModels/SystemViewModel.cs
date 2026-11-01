using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace rockDrill.ViewModels;

/// <summary>系统监控：通讯拓扑、传感器与 IO 信号。</summary>
public partial class SystemViewModel : ViewModelBase
{
    private readonly List<SignalVm> _mainSignals = new();
    private readonly List<SignalVm> _leftSignals = new();
    private readonly List<SignalVm> _rightSignals = new();
    private readonly List<SignalVm> _engineSignals = new();

    [ObservableProperty] private int _selectedNetTab;
    [ObservableProperty] private int _selectedSignalTab;
    [ObservableProperty] private ObservableCollection<SignalVm> _signalItems = new();
    [ObservableProperty] private string _signalGroupTitle = "主柜信号";

    public ObservableCollection<string> NetTabs { get; } = new() { "通讯总览", "左臂传感器", "右臂传感器" };
    public ObservableCollection<string> SignalTabs { get; } = new() { "主柜信号", "左臂信号", "右臂信号", "发动机" };

    public ObservableCollection<SignalVm> CanStats { get; } = new();

    public SystemViewModel()
    {
        _mainSignals.AddRange(new[]
        {
            Sig("主泵压力", "21.4", "MPa", "正常"),
            Sig("系统电压", "24.2", "V", "正常"),
            Sig("液压油温", "58.2", "°C", "正常"),
            Sig("回油滤压差", "0.18", "MPa", "正常"),
            Sig("冷却风扇转速", "1450", "r/min", "正常"),
            Sig("空压机压力", "0.65", "MPa", "正常"),
            Sig("照明回路", "ON", "", "正常"),
            Sig("警示灯", "ON", "", "正常"),
            Sig("支腿到位 L/R", "1 / 1", "", "正常"),
            Sig("水阀反馈", "1", "", "正常"),
            Sig("燃油液位", "68", "%", "正常"),
        });

        _leftSignals.AddRange(new[]
        {
            Sig("臂架回转角度", "-12.4", "°", "正常"),
            Sig("臂架俯仰角度", "3.8", "°", "正常"),
            Sig("直臂伸缩位移", "1240", "mm", "正常"),
            Sig("凿岩补偿位移", "480", "mm", "正常"),
            Sig("左倾角传感器 X", "0.42", "°", "正常"),
            Sig("左倾角传感器 Y", "-0.15", "°", "正常"),
            Sig("冲击压力", "14.3", "MPa", "正常"),
            Sig("回转压力", "8.2", "MPa", "正常"),
            Sig("推进压力", "6.1", "MPa", "正常"),
        });

        _rightSignals.AddRange(new[]
        {
            Sig("臂架回转角度", "-9.6", "°", "正常"),
            Sig("臂架俯仰角度", "6.2", "°", "正常"),
            Sig("直臂伸缩位移", "1080", "mm", "正常"),
            Sig("凿岩补偿位移", "362", "mm", "正常"),
            Sig("右倾角传感器 X", "0.28", "°", "正常"),
            Sig("右倾角传感器 Y", "0.06", "°", "正常"),
            Sig("冲击压力", "13.9", "MPa", "正常"),
            Sig("回转压力", "7.8", "MPa", "正常"),
            Sig("推进压力", "5.6", "MPa", "偏低"),
        });

        _engineSignals.AddRange(new[]
        {
            Sig("发动机转速", "1850", "r/min", "正常"),
            Sig("冷却水温", "82", "°C", "正常"),
            Sig("机油压力", "0.42", "MPa", "正常"),
            Sig("燃油液位", "68", "%", "正常"),
            Sig("电瓶电压", "27.8", "V", "正常"),
            Sig("尾气温度", "312", "°C", "正常"),
            Sig("工作小时", "4268", "h", "正常"),
        });

        CanStats.Add(new SignalVm { Name = "CAN0 负载", ValueText = "32 %", State = "正常" });
        CanStats.Add(new SignalVm { Name = "CAN1 负载", ValueText = "12 %", State = "正常" });
        CanStats.Add(new SignalVm { Name = "CAN2 负载", ValueText = "8 %", State = "正常" });
        CanStats.Add(new SignalVm { Name = "错误帧", ValueText = "0 帧", State = "正常" });
        CanStats.Add(new SignalVm { Name = "总线帧率", ValueText = "512 fps", State = "正常" });

        SignalItems = new ObservableCollection<SignalVm>(_mainSignals);
    }

    private static SignalVm Sig(string name, string value, string unit, string state)
        => new()
        {
            Name = name,
            ValueText = value,
            Unit = unit,
            State = state,
            Brush = state == "正常"
                ? new SolidColorBrush(Color.Parse("#2AD07C"))
                : new SolidColorBrush(Color.Parse("#F5A623"))
        };

    partial void OnSelectedSignalTabChanged(int value)
    {
        switch (value)
        {
            case 1:
                SignalGroupTitle = "左臂信号";
                SignalItems = new ObservableCollection<SignalVm>(_leftSignals);
                break;
            case 2:
                SignalGroupTitle = "右臂信号";
                SignalItems = new ObservableCollection<SignalVm>(_rightSignals);
                break;
            case 3:
                SignalGroupTitle = "发动机";
                SignalItems = new ObservableCollection<SignalVm>(_engineSignals);
                break;
            default:
                SignalGroupTitle = "主柜信号";
                SignalItems = new ObservableCollection<SignalVm>(_mainSignals);
                break;
        }
    }
}
