using System;
using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace ava.ViewModels;

public partial class OSViewModel : ViewModelBase
{
    /// <summary>顶部6个设备状态卡</summary>
    public ObservableCollection<EquipmentCard> EquipmentCards { get; } = new();

    /// <summary>中部3个KPI面板</summary>
    public ObservableCollection<KpiItem> KpiItems { get; } = new();

    /// <summary>底部设备台账</summary>
    public ObservableCollection<LedgerRow> LedgerRows { get; } = new();

    private readonly DispatcherTimer _timer;

    // 6个设备图标路径（24x24 viewBox），顺序：开料机/CPU/文件/缆线/硬盘/数据库
    private static readonly Bitmap[] IconPaths =
    {
        // // 开料机 - 剪刀
        // "M9.64 7.64C10.07 7.21 10.66 6.93 11.32 6.93C12.65 6.93 13.72 8 13.72 9.33C13.72 10.66 12.65 11.73 11.32 11.73C10.66 11.73 10.07 11.45 9.64 11.02L4.66 14.41L3.53 12.46L8.5 9.07L3.53 5.68L4.66 3.73L9.64 7.64M21 17.5C21 18.88 19.88 20 18.5 20C17.12 20 16 18.88 16 17.5C16 16.12 17.12 15 18.5 15C19.88 15 21 16.12 21 17.5M21 6.5C21 7.88 19.88 9 18.5 9C17.12 9 16 7.88 16 6.5C16 5.12 17.12 4 18.5 4C19.88 4 21 5.12 21 6.5Z",
        // // CPU - 芯片
        // "M6,2H18V4H6V2M7,4H17V6H7V4M6,6H18V18H6V6M8,8V16H10V8H8M11,8V16H13V8H11M14,8V16H16V8H14M5,18H19V20H5V18Z",
        // // 文件
        // "M14,2H6A2,2 0 0,0 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2M18,20H6V4H13V9H18V20Z",
        // // 缆线 - USB
        // "M12 2L9 5H11V11H7V8L3 12L7 16V13H11V19H9L12 22L15 19H13V13H17V16L21 12L17 8V11H13V5H15L12 2Z",
        // // 硬盘
        // "M5,4V20H19V4H5M12,6A2,2 0 0,1 14,8A2,2 0 0,1 12,10A2,2 0 0,1 10,8A2,2 0 0,1 12,6M12,14A4,4 0 0,1 16,18H8A4,4 0 0,1 12,14Z",
        // // 数据库
        // "M12,3C7.58,3 4,4.79 4,7C4,9.21 7.58,11 12,11C16.42,11 20,9.21 20,7C20,4.79 16.42,3 12,3M4,9V12C4,14.21 7.58,16 12,16C16.42,16 20,14.21 20,12V9C20,11.21 16.42,13 12,13C7.58,13 4,11.21 4,9M4,14V17C4,19.21 7.58,21 12,21C16.42,21 20,19.21 20,17V14C20,16.21 16.42,18 12,18C7.58,18 4,16.21 4,14Z",
        
        new(AssetLoader.Open(new Uri("avares://ava/Assets/Images/设备1.png"))),
        new(AssetLoader.Open(new Uri("avares://ava/Assets/Images/设备2.png"))),
        new(AssetLoader.Open(new Uri("avares://ava/Assets/Images/设备3.png"))),
        new(AssetLoader.Open(new Uri("avares://ava/Assets/Images/设备4.png"))),
        new(AssetLoader.Open(new Uri("avares://ava/Assets/Images/设备5.png"))),
        new(AssetLoader.Open(new Uri("avares://ava/Assets/Images/设备6.png"))),
        
    };

    public OSViewModel()
    {
        InitEquipmentCards();
        InitKpiItems();
        InitLedgerRows();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        Console.WriteLine($"OS页面执行了，HashCode:{this.GetHashCode()}");
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        // 抖动 KPI 数值
        foreach (var kpi in KpiItems)
        {
            kpi.Value = Math.Round(kpi.BaseValue + Random.Shared.NextDouble() * 2 - 1, 1);
            kpi.ValueText = $"{kpi.Value:F1}%";
        }

        // 抖动 6#数据库的性能指标
        if (EquipmentCards.Count > 5 && EquipmentCards[5].ShowPerf)
        {
            EquipmentCards[5].PerfMs = 80 + Random.Shared.Next(0, 15);
            EquipmentCards[5].PerfQps = 95 + Random.Shared.Next(0, 15);
        }

        // 抖动台账寿命
        foreach (var row in LedgerRows)
        {
            if (row.Lifetime < 100)
                row.Lifetime = Math.Clamp(row.BaseLifetime + Random.Shared.Next(-2, 3), 0, 100);
        }
    }

    private void InitEquipmentCards()
    {
        var defs = new (string Name, string Status, string Color)[]
        {
            ("1#开料机", "Stop",  "#FA8C16"),
            ("2#CPU",   "Run",   "#52C41A"),
            ("3#文件",   "Alarm", "#F5222D"),
            ("4#缆线",   "Run",   "#52C41A"),
            ("5#硬盘",   "Alarm", "#F5222D"),
            ("6#数据库",  "Run",   "#52C41A"),
        };

        for (var i = 0; i < defs.Length; i++)
        {
            var d = defs[i];
            var card = new EquipmentCard
            {
                Name = d.Name,
                Location = "广州工厂",
                StatusText = d.Status,
                StatusBrush = Brush.Parse(d.Color),
                IconGeometry = IconPaths[i],
            };
            if (i == 5) // 6#数据库额外显示性能指标
            {
                card.ShowPerf = true;
                card.PerfMs = 85;
                card.PerfQps = 100;
            }
            EquipmentCards.Add(card);
        }
    }

    private void InitKpiItems()
    {
        // OEE - 上升绿线
        KpiItems.Add(new KpiItem
        {
            Title = "OEE",
            SubTitle = "设备综合效率",
            BaseValue = 82,
            Value = 82,
            ValueText = "82%",
            TrendText = "Ascend +10%",
            TrendBrush = Brush.Parse("#52C41A"),
            SparkSeries = CreateSparkSeries(new SKColor(82, 196, 26), GenerateSpark(ascend: true))
        });

        // 能耗 - 下降红线
        KpiItems.Add(new KpiItem
        {
            Title = "能耗",
            SubTitle = "Energy",
            BaseValue = 82,
            Value = 82,
            ValueText = "82%",
            TrendText = "Ascend +10%",
            TrendBrush = Brush.Parse("#52C41A"),
            SparkSeries = CreateSparkSeries(new SKColor(235, 80, 80), GenerateSpark(descend: true))
        });

        // 产量 - 波浪橙线
        KpiItems.Add(new KpiItem
        {
            Title = "产量",
            SubTitle = "Output",
            BaseValue = 82,
            Value = 82,
            ValueText = "82%",
            TrendText = "Descend -10%",
            TrendBrush = Brush.Parse("#F5222D"),
            SparkSeries = CreateSparkSeries(new SKColor(250, 140, 22), GenerateSpark(wave: true))
        });
    }

    private static ISeries[] CreateSparkSeries(SKColor color, double[] values)
    {
        return new ISeries[]
        {
            new LineSeries<double>
            {
                Values = values,
                Stroke = new SolidColorPaint(color) { StrokeThickness = 2 },
                Fill = null,
                GeometrySize = 0,
                LineSmoothness = 0.5
            }
        };
    }

    private static double[] GenerateSpark(bool ascend = false, bool descend = false, bool wave = false)
    {
        var data = new double[20];
        for (var i = 0; i < 20; i++)
        {
            if (wave)
                data[i] = 50 + 25 * Math.Sin(i * 0.6) + Random.Shared.NextDouble() * 5;
            else if (ascend)
                data[i] = 25 + i * 2.5 + Random.Shared.NextDouble() * 5;
            else
                data[i] = 75 - i * 2.5 + Random.Shared.NextDouble() * 5;
        }
        return data;
    }

    private void InitLedgerRows()
    {
        var defs = new (string Name, string Category, string Location, string Status, bool Enabled, double Lifetime)[]
        {
            ("开料机", "生产设备", "开料车间", "启用", true,  50),
            ("CPU",   "办公设备", "办公室",   "报废", false, 100),
            ("文件",   "办公设备", "办公室",   "报废", false, 100),
            ("缆线",   "辅助设备", "仓库",     "启用", true,  60),
            ("硬盘",   "办公设备", "办公室",   "启用", true,  71),
            ("数据库",  "软件设备", "办公室",   "启用", true,  88),
        };

        for (var i = 0; i < defs.Length; i++)
        {
            var d = defs[i];
            LedgerRows.Add(new LedgerRow
            {
                Index = i + 1,
                Name = d.Name,
                Category = d.Category,
                Location = d.Location,
                Date = "2024-10-11",
                StatusText = d.Status,
                IsEnabled = d.Enabled,
                StatusBrush = Brush.Parse(d.Enabled ? "#52C41A" : "#F5222D"),
                Lifetime = d.Lifetime,
                BaseLifetime = d.Lifetime,
                IconGeometry = IconPaths[i]
            });
        }
    }
}

/// <summary>设备状态卡（顶部6个）</summary>
public partial class EquipmentCard : ViewModelBase
{
    public string Name { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public Bitmap? IconGeometry { get; set; }
    public IBrush StatusBrush { get; set; } = null!;
    public string StatusText { get; set; } = string.Empty;
    public bool ShowPerf { get; set; }

    [ObservableProperty] private double _perfMs;
    [ObservableProperty] private double _perfQps;
}

/// <summary>KPI 面板（中部3个，含 sparkline 曲线）</summary>
public partial class KpiItem : ViewModelBase
{
    public string Title { get; set; } = string.Empty;
    public string SubTitle { get; set; } = string.Empty;
    public string TrendText { get; set; } = string.Empty;
    public IBrush TrendBrush { get; set; } = null!;
    public double BaseValue { get; set; }
    public ISeries[] SparkSeries { get; set; } = null!;

    // sparkline 隐藏轴（每实例独立，避免共享冲突）
    public Axis[] SparkXAxes { get; } = new[] { new Axis { IsVisible = false } };
    public Axis[] SparkYAxes { get; } = new[] { new Axis { IsVisible = false } };

    [ObservableProperty] private double _value;
    [ObservableProperty] private string _valueText = string.Empty;
}

/// <summary>设备台账行（底部表格）</summary>
public partial class LedgerRow : ViewModelBase
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    public Bitmap? IconGeometry { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string StatusText { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public IBrush StatusBrush { get; set; } = null!;
    public double BaseLifetime { get; set; }

    [ObservableProperty] private double _lifetime;
}
