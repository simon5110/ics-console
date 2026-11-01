using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace rockDrill.ViewModels;

/// <summary>
/// 主框架 ViewModel：顶部状态栏 + 底部页面导航。
/// </summary>
public partial class MainViewModel : ViewModelBase
{
    [ObservableProperty] private string _currentDate = string.Empty;
    [ObservableProperty] private string _currentTime = string.Empty;

    /// <summary>当前工序：调平/定位/钻孔/收工</summary>
    [ObservableProperty] private string _workStage = "自动钻孔";

    [ObservableProperty] private int _alarmCount = 3;

    [ObservableProperty] private string _canStatus = "CAN 正常";
    [ObservableProperty] private bool _canOk = true;

    [ObservableProperty] private string _hydStatus = "液压正常";
    [ObservableProperty] private bool _hydOk = true;

    [ObservableProperty] private string _networkStatus = "4G · 信号良好";
    [ObservableProperty] private string _batteryStatus = "电量 86%";

    [ObservableProperty] private NavItemVm? _selectedNav;
    [ObservableProperty] private object? _currentPage;

    private readonly DispatcherTimer _clock;
    private readonly DispatcherTimer _simulate;
    private readonly Dictionary<string, ViewModelBase> _pageCache = new();

    public MainViewModel()
    {
        CurrentDate = DateTime.Now.ToString("yyyy-MM-dd");
        CurrentTime = DateTime.Now.ToString("HH:mm:ss");

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clock.Tick += (_, _) =>
        {
            CurrentTime = DateTime.Now.ToString("HH:mm:ss");
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            if (CurrentDate != today) CurrentDate = today;
        };
        _clock.Start();

        // 模拟整车状态刷新
        var rnd = new Random();
        _simulate = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _simulate.Tick += (_, _) =>
        {
            BatteryStatus = $"电量 {86 - rnd.Next(0, 2)}%";
            AlarmCount = 3;
        };
        _simulate.Start();

        SelectedNav = NavItems[0];

        // 支持 --page=HolePattern 直接打开指定页面（调试/截图用）
        var page = Environment.GetCommandLineArgs()
            .FirstOrDefault(a => a.StartsWith("--page=", StringComparison.OrdinalIgnoreCase));
        if (page is not null)
        {
            var name = page["--page=".Length..];
            var target = NavItems.FirstOrDefault(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (target is not null) SelectedNav = target;
        }
    }

    public ObservableCollection<NavItemVm> NavItems { get; } = new()
    {
        new NavItemVm
        {
            Header = "主页", Name = "Home",
            Icon = "M12 2.2 21.4 10v11.3h-6.6v-6.2h-5.6v6.2H2.6V10z"
        },
        new NavItemVm
        {
            Header = "调平", Name = "Leveling",
            Icon = "M12 2.4a9.6 9.6 0 1 0 0 19.2 9.6 9.6 0 0 0 0-19.2zm0 2.2a7.4 7.4 0 1 1 0 14.8 7.4 7.4 0 0 1 0-14.8zM11.3 0.6h1.4v3.1h-1.4zM11.3 20.3h1.4v3.1h-1.4zM0.6 11.3h3.1v1.4H0.6zM20.3 11.3h3.1v1.4h-3.1zM12 9.6a2.4 2.4 0 1 0 0 4.8 2.4 2.4 0 0 0 0-4.8z"
        },
        new NavItemVm
        {
            Header = "定位", Name = "Positioning",
            Icon = "M12 2a7.6 7.6 0 0 0-7.6 7.6c0 5.4 7.6 12.4 7.6 12.4s7.6-7 7.6-12.4A7.6 7.6 0 0 0 12 2zm0 4.4a3.2 3.2 0 1 1 0 6.4 3.2 3.2 0 0 1 0-6.4z"
        },
        new NavItemVm
        {
            Header = "布孔图", Name = "HolePattern",
            Icon = "M2.6 2.6h8v8h-8zM13.4 2.6h8v8h-8zM2.6 13.4h8v8h-8zM13.4 13.4h8v8h-8z"
        },
        new NavItemVm
        {
            Header = "钻孔", Name = "Drilling",
            Icon = "M11 1.8h2v3.4l3.4 3.4v7.6h-2v3.6h-4.8v-3.6h-2V8.6L11 5.2z"
        },
        new NavItemVm
        {
            Header = "凿岩监控", Name = "Monitor",
            Icon = "M2 18.4h2.4v-7.2H2zM6.8 18.4h2.4V6.8H6.8zM11.6 18.4h2.4V2.6h-2.4zM16.4 18.4H18.8v-8.6h-2.4zM21.2 18.4h2.4v-5h-2.4z"
        },
        new NavItemVm
        {
            Header = "自动控臂", Name = "AutoControl",
            Icon = "M12 2.4a3.6 3.6 0 1 1 0 7.2 3.6 3.6 0 0 1 0-7.2zM10.9 10.4h2.2v6.2h3v2.2H7.9v-2.2h3zM3.6 18.6h16.8v1.8H3.6z"
        },
        new NavItemVm
        {
            Header = "告警", Name = "Alarm",
            Icon = "M12 2.2a6.4 6.4 0 0 0-6.4 6.4v5.2L3.6 17h16.8l-2-3.2V8.6A6.4 6.4 0 0 0 12 2.2zM9.6 18.4h4.8a2.4 2.4 0 0 1-4.8 0z"
        },
        new NavItemVm
        {
            Header = "数据", Name = "DataRecord",
            Icon = "M12 2.6c-4.6 0-8.4 1.4-8.4 3s3.8 3 8.4 3 8.4-1.4 8.4-3-3.8-3-8.4-3zM3.6 9.2v3.2c0 1.6 3.8 3 8.4 3s8.4-1.4 8.4-3V9.2c-1.8 1.2-4.9 1.9-8.4 1.9s-6.6-.7-8.4-1.9zM3.6 15.4v3.2c0 1.6 3.8 3 8.4 3s8.4-1.4 8.4-3v-3.2c-1.8 1.2-4.9 1.9-8.4 1.9s-6.6-.7-8.4-1.9z"
        },
        new NavItemVm
        {
            Header = "扫描", Name = "Scan",
            Icon = "M3 3.6h5.4v2H5v3.4H3zM15.6 3.6H21v5.4h-2V5.6h-3.4zM3 15h2v3.4h3.4v2H3zM19 15h2v5.4h-5.4v-2H19zM4.4 11h15.2v2H4.4z"
        },
        new NavItemVm
        {
            Header = "系统监控", Name = "System",
            Icon = "M2 3.8h20v13.4H2zM8.6 19h6.8v1.8H8.6z"
        },
        new NavItemVm
        {
            Header = "设置", Name = "Settings",
            Icon = "M3 7.3h18v1.7H3zM3 14.9h18v1.7H3zM7.5 4.6a3.1 3.1 0 1 1 0 6.2 3.1 3.1 0 0 1 0-6.2zM16 12.2a3.1 3.1 0 1 1 0 6.2 3.1 3.1 0 0 1 0-6.2z"
        },
        new NavItemVm
        {
            Header = "文件资料", Name = "Files",
            Icon = "M3 5h7l2.2 2.2H21V20H3zM5 7.2V18h14V9.2h-8.8L8.2 7.2z"
        },
        new NavItemVm
        {
            Header = "测试", Name = "Test",
            Icon = "M9 2h6v2h-1v5.4l4.5 8.2A2.8 2.8 0 0 1 16 22H8a2.8 2.8 0 0 1-2.5-4.4L10 9.4V4H9zM12 12l-2.6 4.8h5.2z"
        },
    };

    partial void OnSelectedNavChanged(NavItemVm? value)
    {
        var name = value?.Name;
        if (string.IsNullOrEmpty(name)) return;

        if (!_pageCache.TryGetValue(name, out var vm))
        {
            vm = name switch
            {
                "Home" => new HomeViewModel(),
                "Leveling" => new LevelingViewModel(),
                "Positioning" => new PositioningViewModel(),
                "HolePattern" => new HolePatternViewModel(),
                "Drilling" => new DrillingViewModel(),
                "Monitor" => new MonitorViewModel(),
                "AutoControl" => new AutoArmControlViewModel(),
                "Alarm" => new AlarmViewModel(),
                "DataRecord" => new DataRecordViewModel(),
                "Scan" => new ScanViewModel(),
                "System" => new SystemViewModel(),
                "Settings" => new SettingsViewModel(),
                "Files" => new FilesViewModel(),
                "Test" => new TestViewModel(),
                _ => null
            };
            if (vm is null) return;
            _pageCache[name] = vm;
        }

        CurrentPage = vm;
    }

    [RelayCommand]
    private void GoAlarm()
        => SelectedNav = NavItems.FirstOrDefault(n => n.Name == "Alarm") ?? NavItems[0];
}

public class NavItemVm
{
    public string Header { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>路径图标（24x24 视口）</summary>
    public string Icon { get; set; } = string.Empty;
}
