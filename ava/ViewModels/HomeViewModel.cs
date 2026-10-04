using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ava.ViewModels;

public partial class HomeViewModel : ViewModelBase
{
    public ObservableCollection<DeviceItem> DeviceItems { get; set; } = new();

    /// <summary>当前页要显示的报警数据（DataGrid 绑定这个）</summary>
    public ObservableCollection<DeviceRealTimeBj> DeviceRealTimeBjList { get; set; } = new();

    /// <summary>全量报警数据（内存中，分页从中截取）</summary>
    private readonly List<DeviceRealTimeBj> _allData = new();

    [ObservableProperty] private int _currentPage = 1;
    [ObservableProperty] private int _pageSize = 10;
    [ObservableProperty] private int _totalCount;

    // 可选的设备名、报警变量、报警类型
    private readonly string[] _deviceNames = { "1#风机", "2#水泵", "3#空压机", "4#冷却塔", "5#锅炉" };
    private readonly string[] _bjArgs = { "温度", "压力", "电流", "转速", "液位" };
    private readonly string[] _bjTypes = { "超限报警", "低限报警", "波动报警", "通讯中断" };
    private readonly string[] _bjLevels = { "INFO", "WARN", "ALARM" };

    // 图标
    private static readonly Bitmap CpuIcon = new(AssetLoader.Open(new Uri("avares://ava/Assets/Images/CPU.png")));
    private static readonly Bitmap StoreIcon = new(AssetLoader.Open(new Uri("avares://ava/Assets/Images/内存条.png")));
    private static readonly Bitmap CpIcon = new(AssetLoader.Open(new Uri("avares://ava/Assets/Images/硬盘.png")));

    private readonly double _cpuCount = 5;
    private readonly double _storeCount = 32;
    private readonly double _cpCount = 512;

    private readonly DispatcherTimer _timer;

    public HomeViewModel()
    {
        // ===== 顶部三个设备卡片 =====
        RefreshDeviceCards();

        // 每 3 秒刷新顶部卡片数值
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += (_, _) => RefreshDeviceCards();
        _timer.Start();

        // ===== 报警数据：生成 600 条并加载第一页 =====
        GenerateData(600);
        TotalCount = _allData.Count;
        LoadCurrentPage();

        Console.WriteLine($"home页面执行了，HashCode:{this.GetHashCode()}");
    }

    private void RefreshDeviceCards()
    {
        int cpu = Random.Shared.Next(1, 5);
        int store = Random.Shared.Next(1, 32);
        int cp = Random.Shared.Next(1, 512);

        if (DeviceItems.Count == 0)
        {
            DeviceItems.Add(new DeviceItem { DeviceName = "CPU利用率", DeviceImg = CpuIcon });
            DeviceItems.Add(new DeviceItem { DeviceName = "内存利用率", DeviceImg = StoreIcon });
            DeviceItems.Add(new DeviceItem { DeviceName = "磁盘利用率", DeviceImg = CpIcon });
        }

        DeviceItems[0].Value = Math.Round(cpu / _cpuCount * 100, 2);
        DeviceItems[0].DeviceNameSub = $"{cpu}GHz / {_cpuCount}GHz";

        DeviceItems[1].Value = Math.Round(store / _storeCount * 100, 2);
        DeviceItems[1].DeviceNameSub = $"{store}GB / {_storeCount}GB";

        DeviceItems[2].Value = Math.Round(cp / _cpCount * 100, 2);
        DeviceItems[2].DeviceNameSub = $"{cp}GB / {_cpCount}GB";
    }

    /// <summary>随机生成 count 条报警数据到全量列表</summary>
    private void GenerateData(int count)
    {
        _allData.Clear();
        for (int i = 0; i < count; i++)
        {
            _allData.Add(new DeviceRealTimeBj
            {
                Index = i + 1,
                BjDeviceName = _deviceNames[Random.Shared.Next(_deviceNames.Length)],
                BjArg = _bjArgs[Random.Shared.Next(_bjArgs.Length)],
                BjNum = Random.Shared.Next(60, 100),
                BjType = _bjTypes[Random.Shared.Next(_bjTypes.Length)],
                BjYz = 100,
                BjLevel = _bjLevels[Random.Shared.Next(_bjLevels.Length)],
                BjTime = DateTime.Now
                    .AddSeconds(-Random.Shared.Next(0, 86400))
                    .ToString("yyyy/MM/dd H:m:s")
            });
        }
    }

    /// <summary>分页按钮点击：切换到指定页</summary>
    [RelayCommand]
    public void LoadPage(object? currentPage)
    {
        if (currentPage is int page && page >= 1)
            CurrentPage = page;
        LoadCurrentPage();
    }

    /// <summary>每页条数变化时回到第一页并重新加载</summary>
    partial void OnPageSizeChanged(int value)
    {
        CurrentPage = 1;
        LoadCurrentPage();
    }

    /// <summary>从全量数据中截取当前页，填充到绑定集合</summary>
    private void LoadCurrentPage()
    {
        var pageData = _allData
            .Skip((CurrentPage - 1) * PageSize)
            .Take(PageSize);

        DeviceRealTimeBjList.Clear();
        foreach (var item in pageData)
            DeviceRealTimeBjList.Add(item);
    }
}

public partial class DeviceItem : ViewModelBase
{
    public string DeviceName { get; set; } = string.Empty;
    public IImage? DeviceImg { get; set; }

    [ObservableProperty] private string _deviceNameSub = string.Empty;
    [ObservableProperty] public double _value = 0;
}

public class DeviceRealTimeBj
{
    /// <summary>全局序号（分页后连续）</summary>
    public int Index { get; set; }

    public string BjDeviceName { get; set; } = string.Empty;  // 报警设备名字
    public string BjArg { get; set; } = string.Empty;         // 报警变量
    public int BjNum { get; set; }                            // 报警数值 60～100
    public string BjType { get; set; } = string.Empty;        // 报警类型
    public int BjYz { get; set; }                             // 报警阈值 写死100
    public string BjLevel { get; set; } = string.Empty;       // 报警等级 INFO WARN ALARM
    public string BjTime { get; set; } = string.Empty;        // 报警时间
}
