using System;
using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace ava.ViewModels;

public partial class RealTimeViewModel : ViewModelBase
{
    // ===== 产量统计（面积折线图） =====
    public ISeries[] OutputSeries { get; }
    public Axis[] OutputXAxes { get; }
    public Axis[] OutputYAxes { get; }

    // ===== 订单状态（环形图） =====
    public ISeries[] OrderSeries { get; }

    /// <summary>订单完成率（环形图中心文字）</summary>
    [ObservableProperty] private int _orderCompleteRate = 90;

    /// <summary>右上角延迟徽标文字</summary>
    [ObservableProperty] private string _perfMsText = "102ms";

    /// <summary>右上角健康分</summary>
    [ObservableProperty] private int _perfScore = 100;

    // ===== 技能发展（雷达图，切换组员时整体替换） =====
    [ObservableProperty] private ISeries[] _skillSeries = null!;
    public Axis[] SkillAngleAxes { get; }
    public PolarAxis[] SkillRadiusAxes { get; }

    /// <summary>雷达图下方 A~E 明细</summary>
    public ObservableCollection<SkillLabelVm> SkillLabels { get; } = new();

    /// <summary>选择组员下拉项与选中索引（第0项=默认/选择组员）</summary>
    public ObservableCollection<string> MemberOptions { get; } = new() { "选择组员", "张三", "李四", "王五" };
    [ObservableProperty] private int _selectedMemberIndex;

    // ===== 产量对比（渐变柱状图，切换类型时整体替换） =====
    [ObservableProperty] private ISeries[] _compareSeries = null!;
    public Axis[] CompareXAxes { get; }
    public Axis[] CompareYAxes { get; }

    /// <summary>选择类型下拉项</summary>
    public ObservableCollection<string> TypeOptions { get; } = new() { "选择类型", "日产量", "周产量", "月产量" };
    [ObservableProperty] private int _selectedTypeIndex;

    // ===== 班组 TOP3（切换区域时重新填充） =====
    public ObservableCollection<TopTeamVm> TopTeams { get; } = new();

    /// <summary>底部三个班级奖章</summary>
    public ObservableCollection<string> ClassBadges { get; } = new() { "1班", "2班", "3班" };

    /// <summary>选择区域下拉项</summary>
    public ObservableCollection<string> RegionOptions { get; } = new() { "选择区域", "华东区", "华南区", "华北区" };
    [ObservableProperty] private int _selectedRegionIndex;

    private readonly DispatcherTimer _timer;

    public RealTimeViewModel()
    {
        OutputSeries = CreateOutputSeries();
        OutputXAxes = new[] { new Axis { IsVisible = false } };
        OutputYAxes = CreateOutputYAxes();

        OrderSeries = CreateOrderSeries();

        SkillAngleAxes = CreateSkillAngleAxes();
        SkillRadiusAxes = CreateSkillRadiusAxes();
        ApplyMemberData(0);

        CompareXAxes = CreateCompareXAxes();
        CompareYAxes = CreateCompareYAxes();
        ApplyCompareData(0);

        ApplyRegionData(0);

        // 1.5s 抖动延迟徽标
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        Console.WriteLine($"RealTime页面执行了，HashCode:{this.GetHashCode()}");
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        PerfMsText = $"{96 + Random.Shared.Next(0, 14)}ms";
    }

    // ===== 下拉联动：选择组员 → 重建雷达图与明细 =====
    partial void OnSelectedMemberIndexChanged(int value) => ApplyMemberData(value);

    private void ApplyMemberData(int index)
    {
        // 各组员 A/B/C/D/E 五维成绩（第0组为设计图默认数据）
        var dataSets = new double[][]
        {
            new[] { 90.0, 80, 70, 90, 90 },
            new[] { 85.0, 70, 92, 75, 80 },
            new[] { 70.0, 92, 85, 80, 95 },
            new[] { 95.0, 60, 75, 88, 70 },
        };
        var values = dataSets[Math.Clamp(index, 0, dataSets.Length - 1)];

        var red = new SKColor(235, 92, 92);
        SkillSeries = new ISeries[]
        {
            new PolarLineSeries<double>
            {
                Values = values,
                Stroke = new SolidColorPaint(red) { StrokeThickness = 2 },
                Fill = new SolidColorPaint(red.WithAlpha(45)),
                GeometrySize = 0,
                IsClosed = true
            }
        };

        SkillLabels.Clear();
        var names = new[] { "A", "B", "C", "D", "E" };
        for (var i = 0; i < names.Length; i++)
            SkillLabels.Add(new SkillLabelVm { Name = names[i], Value = (int)values[i] });
    }

    // ===== 下拉联动：选择类型 → 重建柱状图 =====
    partial void OnSelectedTypeIndexChanged(int value) => ApplyCompareData(value);

    private void ApplyCompareData(int index)
    {
        // 不同产量类型下 1~6 号产线的数值（第0组为设计图默认数据）
        var dataSets = new double[][]
        {
            new[] { 80.0, 86, 96, 89, 92, 97 },
            new[] { 62.0, 72, 68, 85, 78, 90 },
            new[] { 80.0, 86, 96, 89, 92, 97 },
            new[] { 55.0, 62, 70, 68, 75, 82 },
        };
        var values = dataSets[Math.Clamp(index, 0, dataSets.Length - 1)];

        var topGreen = new SKColor(30, 90, 35);
        var bottomRed = new SKColor(230, 35, 42);

        CompareSeries = new ISeries[]
        {
            new ColumnSeries<double>
            {
                Values = values,
                // 柱体自上而下 深绿→红
                Fill = new LinearGradientPaint(
                    topGreen, bottomRed,
                    new SKPoint(0.5f, 0), new SKPoint(0.5f, 1)),
                MaxBarWidth = 20,
                Padding = 12,
                IsHoverable = false
            }
        };
    }

    // ===== 下拉联动：选择区域 → 重建 TOP3 排行 =====
    partial void OnSelectedRegionIndexChanged(int value) => ApplyRegionData(value);

    private void ApplyRegionData(int index)
    {
        // 各区域班组排行（第0组为设计图默认数据）
        var defs = new (string Name, string Code, int Points, string MedalColor)[][]
        {
            new[]
            {
                ("JLinking设备组", "JL1", 90, "#F5C242"),
                ("JLinking生产甲班", "JL3", 87, "#B8BCC4"),
                ("JLinking生产乙班", "JL2", 80, "#D4925A"),
            },
            new[]
            {
                ("JLinking华东一组", "HD1", 95, "#F5C242"),
                ("JLinking华东二组", "HD2", 88, "#B8BCC4"),
                ("JLinking华东三组", "HD3", 76, "#D4925A"),
            },
            new[]
            {
                ("JLinking华南一组", "HN1", 92, "#F5C242"),
                ("JLinking华南二组", "HN2", 85, "#B8BCC4"),
                ("JLinking华南三组", "HN3", 81, "#D4925A"),
            },
            new[]
            {
                ("JLinking华北一组", "HB1", 89, "#F5C242"),
                ("JLinking华北二组", "HB2", 84, "#B8BCC4"),
                ("JLinking华北三组", "HB3", 72, "#D4925A"),
            },
        };
        var dataSet = defs[Math.Clamp(index, 0, defs.Length - 1)];

        TopTeams.Clear();
        for (var i = 0; i < dataSet.Length; i++)
        {
            var d = dataSet[i];
            TopTeams.Add(new TopTeamVm
            {
                Rank = i + 1,
                Name = d.Name,
                Code = d.Code,
                Points = d.Points,
                MedalBrush = Brush.Parse(d.MedalColor)
            });
        }
    }

    // ===== 产量统计面积图 =====
    private static ISeries[] CreateOutputSeries()
    {
        // 设计图曲线：前段 440~500 高位波动，中段深跌至 220，后段回升至 360
        double[] values =
        {
            455, 442, 458, 482, 450, 470, 485, 448, 500, 415,
            432, 348, 425, 330, 368, 315, 220, 372, 455, 482,
            425, 322, 345, 362, 365
        };

        var green = new SKColor(82, 196, 26);

        return new ISeries[]
        {
            new LineSeries<double>
            {
                Values = values,
                Stroke = new SolidColorPaint(green) { StrokeThickness = 2.5f },
                // 浅绿面积填充（自上而下渐变）
                Fill = new LinearGradientPaint(
                    green.WithAlpha(90), green.WithAlpha(18),
                    new SKPoint(0.5f, 0), new SKPoint(0.5f, 1)),
                GeometrySize = 5,
                GeometryFill = new SolidColorPaint(green),
                GeometryStroke = new SolidColorPaint(green),
                LineSmoothness = 0.45
            }
        };
    }

    private static Axis[] CreateOutputYAxes()
    {
        return new[]
        {
            new Axis
            {
                MinLimit = 0,
                MaxLimit = 500,
                LabelsPaint = new SolidColorPaint(new SKColor(120, 120, 120)),
                TextSize = 11,
                SeparatorsPaint = new SolidColorPaint(new SKColor(238, 238, 238)) { StrokeThickness = 1 },
                Labeler = v => ((int)v).ToString()
            }
        };
    }

    // ===== 订单状态环形图（已完成 90% 绿 / 未完成 10% 红） =====
    private static ISeries[] CreateOrderSeries()
    {
        return new ISeries[]
        {
            new PieSeries<double>
            {
                Name = "已完成",
                Values = new double[] { 90 },
                Fill = new SolidColorPaint(new SKColor(46, 125, 50)),
                RelativeInnerRadius = 0.62,
                IsHoverable = false
            },
            new PieSeries<double>
            {
                Name = "未完成",
                Values = new double[] { 10 },
                Fill = new SolidColorPaint(new SKColor(245, 34, 45)),
                RelativeInnerRadius = 0.62,
                IsHoverable = false
            }
        };
    }

    // ===== 雷达图坐标轴（固定，不随组员切换） =====
    private static Axis[] CreateSkillAngleAxes()
    {
        var webGreen = new SKColor(130, 195, 135);
        return new[]
        {
            new Axis
            {
                Labels = new[] { "A", "B", "C", "D", "E" },
                LabelsPaint = new SolidColorPaint(new SKColor(46, 125, 50)),
                TextSize = 14,
                SeparatorsPaint = new SolidColorPaint(webGreen.WithAlpha(160)) { StrokeThickness = 1 }
            }
        };
    }

    private static PolarAxis[] CreateSkillRadiusAxes()
    {
        var webGreen = new SKColor(130, 195, 135);
        return new[]
        {
            new PolarAxis
            {
                MinLimit = 0,
                MaxLimit = 100,
                // 只显示同心多边形网格，不显示刻度数字
                LabelsPaint = new SolidColorPaint(SKColors.Transparent),
                TextSize = 0,
                SeparatorsPaint = new SolidColorPaint(webGreen.WithAlpha(160)) { StrokeThickness = 1 }
            }
        };
    }

    // ===== 柱状图坐标轴（固定） =====
    private static Axis[] CreateCompareXAxes()
    {
        return new[]
        {
            new Axis
            {
                Labels = new[] { "1", "2", "3", "4", "5", "6" },
                LabelsPaint = new SolidColorPaint(new SKColor(96, 98, 102)),
                TextSize = 12,
                SeparatorsPaint = new SolidColorPaint(SKColors.Transparent)
            }
        };
    }

    private static Axis[] CreateCompareYAxes()
    {
        return new[]
        {
            new Axis
            {
                MinLimit = 0,
                MaxLimit = 110,
                IsVisible = false
            }
        };
    }
}

/// <summary>雷达图底部技能明细（A: 90）</summary>
public class SkillLabelVm
{
    public string Name { get; set; } = string.Empty;
    public int Value { get; set; }
    public string Display => $"{Name}: {Value}";
}

/// <summary>班组 TOP3 排行行</summary>
public class TopTeamVm
{
    public int Rank { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int Points { get; set; }
    public IBrush MedalBrush { get; set; } = null!;
}
