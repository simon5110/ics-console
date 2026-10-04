using System;
using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ava.ViewModels;

public partial class DataBaseViewModel : ViewModelBase
{
    /// <summary>左侧工厂-车间-工序树</summary>
    public ObservableCollection<TreeNodeVm> TreeNodes { get; } = new();

    /// <summary>右侧设备台账表格数据</summary>
    public ObservableCollection<EquipmentRowVm> Equipments { get; } = new();

    /// <summary>底部设备全生命周期时间线</summary>
    public ObservableCollection<TimelineStageVm> TimelineStages { get; } = new();

    /// <summary>左侧树搜索关键字</summary>
    [ObservableProperty] private string? _keyword;

    /// <summary>右侧表格搜索关键字</summary>
    [ObservableProperty] private string? _searchKeyword;

    /// <summary>右上角数据库延迟（ms），定时轻微抖动</summary>
    [ObservableProperty] private string _perfMsText = "82ms";

    /// <summary>右上角健康分数</summary>
    [ObservableProperty] private int _perfQps = 100;

    private readonly DispatcherTimer _timer;

    // ===== 顶部工具按钮 / 搜索框图标（24x24 viewBox） =====
    public Geometry PlusIcon { get; } = Geometry.Parse("M19 13H13V19H11V13H5V11H11V5H13V11H19V13Z");
    public Geometry DeleteIcon { get; } = Geometry.Parse("M19 4H15.5L14.5 3H9.5L8.5 4H5V6H19M6 19A2 2 0 0,0 8 21H16A2 2 0 0,0 18 19V7H6V19Z");
    public Geometry RefreshIcon { get; } = Geometry.Parse("M17.65 6.35C16.2 4.9 14.21 4 12 4C7.58 4 4.01 7.58 4.01 12C4.01 16.42 7.58 20 12 20C15.73 20 18.84 17.45 19.73 14H17.65C16.83 16.33 14.61 18 12 18C8.69 18 6 15.31 6 12C6 8.69 8.69 6 12 6C13.66 6 15.14 6.69 16.22 7.78L13 11H20V4L17.65 6.35Z");
    public Geometry ExportIcon { get; } = Geometry.Parse("M5 20H19V22H5V20M12 4L17.25 9.25L15.83 10.67L13 7.83V16H11V7.83L8.17 10.67L6.75 9.25L12 4Z");
    public Geometry SearchIcon { get; } = Geometry.Parse("M9.5 3A6.5 6.5 0 0,1 16 9.5C16 11.11 15.41 12.59 14.44 13.73L14.71 14H15.5L20.5 19L19 20.5L14 15.5V14.71L13.73 14.44C12.59 15.41 11.11 16 9.5 16A6.5 6.5 0 0,1 3 9.5 6.5 6.5 0 0,1 9.5 3M9.5 5C7 5 5 7 5 9.5 5 12 7 14 9.5 14 12 14 14 12 14 9.5 14 7 12 5 9.5 5Z");

    // ===== 时间线图标 =====
    private static readonly Geometry CartIcon = Geometry.Parse("M17 18C15.9 18 15 18.9 15 20S15.9 22 17 22 19 21.1 19 20 18.1 18 17 18M1 2V4H3L6.6 11.59L5.25 14.04C5.09 14.32 5 14.65 5 15C5 16.1 5.9 17 7 17H19V15H7.42C7.28 15 7.17 14.89 7.17 14.75L7.2 14.63L8.1 13H15.55C16.3 13 16.96 12.59 17.3 11.97L20.88 5.5C20.96 5.34 21 5.17 21 5C21 4.45 20.55 4 20 4H5.21L4.27 2H1M7 18C5.9 18 5 18.9 5 20S5.9 22 7 22 9 21.1 9 20 8.1 18 7 18Z");
    private static readonly Geometry WrenchIcon = Geometry.Parse("M22.7 19.3L14.8 11.4C15.6 9.7 15.9 7.7 15.4 5.8C14.7 3 12.2 .8 9.2 .2C8.5 .1 7.9 0 7.2 0C6.2 0 5.2 .2 4.3 .6L8.5 4.8L6.4 6.9L2 2.5C.7 4.3 .2 6.6 .7 8.9C1.2 11.9 3.4 14.4 6.2 15.1C8.1 15.6 10.1 15.3 11.8 14.5L19.7 22.4C20.1 22.8 20.7 22.8 21.1 22.4L22.7 20.8C23.1 20.4 23.1 19.7 22.7 19.3Z");
    private static readonly Geometry GearIcon = Geometry.Parse("M12 15.5A3.5 3.5 0 0,1 8.5 12A3.5 3.5 0 0,1 12 8.5A3.5 3.5 0 0,1 15.5 12 3.5 3.5 0 0,1 12 15.5M19.43 12.97C19.46 12.65 19.5 12.33 19.5 12C19.5 11.68 19.46 11.34 19.43 11.03L21.54 9.37C21.73 9.22 21.78 8.95 21.66 8.73L19.66 5.27C19.54 5.05 19.27 4.97 19.05 5.05L16.56 6.05C16.04 5.66 15.5 5.32 14.87 5.07L14.5 2.42C14.46 2.18 14.25 2 14 2H10C9.75 2 9.54 2.18 9.5 2.42L9.13 5.07C8.5 5.32 7.96 5.66 7.44 6.05L4.95 5.05C4.73 4.97 4.46 5.05 4.34 5.27L2.34 8.73C2.21 8.95 2.27 9.22 2.46 9.37L4.56 11.03C4.54 11.34 4.5 11.68 4.5 12C4.5 12.32 4.54 12.66 4.56 12.97L2.46 14.63C2.27 14.78 2.21 15.05 2.34 15.27L4.34 18.73C4.46 18.95 4.73 19.03 4.95 18.95L7.44 17.94C7.96 18.34 8.5 18.68 9.13 18.93L9.5 21.58C9.54 21.82 9.75 22 10 22H14C14.25 22 14.46 21.82 14.5 21.58L14.87 18.93C15.5 18.67 16.04 18.34 16.56 17.94L19.05 18.95C19.27 19.03 19.54 18.95 19.66 18.73L21.66 15.27C21.78 15.05 21.73 14.78 21.54 14.63L19.43 12.97Z");
    private static readonly Geometry ArrowUpIcon = Geometry.Parse("M13 20H11V8L5.5 13.5L4.08 12.08L12 4.16L19.92 12.08L18.5 13.5L13 8V20Z");
    private static readonly Geometry WarningIcon = Geometry.Parse("M13 14H11V9H13M13 18H11V16H13M1 21H23L12 2L1 21Z");
    private static readonly Geometry TrashIcon = Geometry.Parse("M19 4H15.5L14.5 3H9.5L8.5 4H5V6H19M6 19A2 2 0 0,0 8 21H16A2 2 0 0,0 18 19V7H6V19Z");

    /// <summary>树节点文件夹图标（工厂/车间/工序共用）</summary>
    private static readonly Geometry FolderIcon = Geometry.Parse("M10 4H4C2.89 4 2 4.89 2 6V18A2 2 0 0,0 4 20H20A2 2 0 0,0 22 18V8C22 6.89 21.1 6 20 6H12L10 4Z");

    public DataBaseViewModel()
    {
        InitTreeNodes();
        InitEquipments();
        InitTimelineStages();

        // 1.5s 抖动数据库延迟，模拟实时状态
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        Console.WriteLine($"DataBase页面执行了，HashCode:{this.GetHashCode()}");
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        PerfMsText = $"{78 + Random.Shared.Next(0, 12)}ms";
    }

    private void InitTreeNodes()
    {
        // 工厂1：车间1折叠；车间2展开，含工序1、工序2
        var factory1 = new TreeNodeVm("工厂1", FolderIcon, expanded: true)
        {
            Children =
            {
                new TreeNodeVm("车间1", FolderIcon)
                {
                    Children = { new("工序1", FolderIcon), new("工序2", FolderIcon) }
                },
                new TreeNodeVm("车间2", FolderIcon, expanded: true)
                {
                    Children = { new("工序1", FolderIcon), new("工序2", FolderIcon) }
                }
            }
        };

        // 工厂2：车间1～车间5，全部折叠
        var factory2 = new TreeNodeVm("工厂2", FolderIcon, expanded: true);
        for (var w = 1; w <= 5; w++)
        {
            var workshop = new TreeNodeVm($"车间{w}", FolderIcon)
            {
                Children = { new("工序1", FolderIcon), new("工序2", FolderIcon) }
            };
            factory2.Children.Add(workshop);
        }

        TreeNodes.Add(factory1);
        TreeNodes.Add(factory2);

        // 工厂3～工厂9：折叠
        for (var f = 3; f <= 9; f++)
        {
            var factory = new TreeNodeVm($"工厂{f}", FolderIcon)
            {
                Children = { new($"车间1", FolderIcon), new($"车间2", FolderIcon) }
            };
            TreeNodes.Add(factory);
        }
    }

    private void InitEquipments()
    {
        // 设计图中 016～021 的类型循环：类型C/A/B/B/A/C
        var typeCycle = new[] { "类型C", "类型A", "类型B", "类型B", "类型A", "类型C" };

        for (var i = 16; i <= 30; i++)
        {
            var row = new EquipmentRowVm
            {
                Index = i.ToString("D3"),
                Code = $"E{i:D3}",
                Name = $"设备{i}",
                Type = typeCycle[(i - 16) % typeCycle.Length],
                Address = $"地址{i}",
                Manufacturer = $"生产商{i}",
                Quantity = i * 100
            };

            // 017 行默认勾选并蓝底高亮
            if (i == 17)
                row.IsSelected = true;

            Equipments.Add(row);
        }
    }

    private void InitTimelineStages()
    {
        var stages = new (string Name, Geometry Icon)[]
        {
            ("采购", CartIcon),
            ("安装", WrenchIcon),
            ("运行", RefreshIcon),
            ("保养", GearIcon),
            ("升级", ArrowUpIcon),
            ("故障", WarningIcon),
            ("报废", TrashIcon),
        };

        for (var i = 0; i < stages.Length; i++)
        {
            TimelineStages.Add(new TimelineStageVm
            {
                Name = stages[i].Name,
                Icon = stages[i].Icon,
                ShowSeparator = i < stages.Length - 1
            });
        }
    }
}

/// <summary>左侧树节点（工厂 / 车间 / 工序三级共用）</summary>
public partial class TreeNodeVm : ViewModelBase
{
    public string Name { get; set; } = string.Empty;
    public Geometry? Icon { get; set; }
    public ObservableCollection<TreeNodeVm> Children { get; } = new();

    /// <summary>节点是否展开（TreeViewItem 通过样式双向绑定）</summary>
    [ObservableProperty] private bool _isExpanded;

    public TreeNodeVm() { }

    public TreeNodeVm(string name, Geometry? icon, bool expanded = false)
    {
        Name = name;
        Icon = icon;
        IsExpanded = expanded;
    }
}

/// <summary>设备台账表格行</summary>
public partial class EquipmentRowVm : ViewModelBase
{
    /// <summary>序号（三位补零字符串，如 016）</summary>
    public string Index { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Manufacturer { get; set; } = string.Empty;
    public int Quantity { get; set; }

    /// <summary>勾选状态，同步驱动整行蓝底高亮</summary>
    [ObservableProperty] private bool _isSelected;

    [ObservableProperty] private IBrush _rowBrush = Brushes.Transparent;

    partial void OnIsSelectedChanged(bool value)
    {
        RowBrush = value ? Brush.Parse("#E8F1FF") : Brushes.Transparent;
    }
}

/// <summary>底部全生命周期时间线阶段</summary>
public partial class TimelineStageVm : ViewModelBase
{
    public string Name { get; set; } = string.Empty;
    public Geometry? Icon { get; set; }

    /// <summary>是否在右侧显示 › 分隔符（最后一项不显示）</summary>
    public bool ShowSeparator { get; set; }
}
