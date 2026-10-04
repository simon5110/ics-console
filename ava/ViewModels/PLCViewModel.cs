using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace ava.ViewModels;

public partial class PLCViewModel : ViewModelBase
{
    // ===== 左侧：串口配置下拉框选项 =====
    public List<string> ComPorts { get; } = new() { "COM1", "COM2", "COM3", "COM4", "COM5" };
    public List<string> BaudRates { get; } = new() { "9600", "19200", "38400", "57600", "115200" };
    public List<string> DataBits { get; } = new() { "5", "6", "7", "8" };
    public List<string> ParityBits { get; } = new() { "无", "奇校验", "偶校验" };
    public List<string> StopBits { get; } = new() { "1", "1.5", "2" };

    [ObservableProperty] private string? _selectedComPort;
    [ObservableProperty] private string? _selectedBaudRate;
    [ObservableProperty] private string? _selectedDataBit;
    [ObservableProperty] private string? _selectedParityBit;
    [ObservableProperty] private string? _selectedStopBit;

    // ===== 左侧：能源数值输入 =====
    public ObservableCollection<EnergyInput> EnergyInputs { get; } = new()
    {
        new EnergyInput { Label = "电能" },
        new EnergyInput { Label = "水能" },
        new EnergyInput { Label = "天然气" },
        new EnergyInput { Label = "生物燃气" },
        new EnergyInput { Label = "石油" },
        new EnergyInput { Label = "太阳能" },
        new EnergyInput { Label = "风能" },
    };

    // ===== 右侧：8 个能源卡片（图标用矢量 Geometry） =====
    public ObservableCollection<EnergyCard> EnergyCards { get; } = new()
    {
        new EnergyCard { Name = "今日电力耗量", TodayValue = 60, YesterdayDelta = 80, MonthValue = 457, LastMonthValue = 451 },
        new EnergyCard { Name = "今日水力耗量", TodayValue = 71, YesterdayDelta = 86, MonthValue = 454, LastMonthValue = 857 },
        new EnergyCard { Name = "今日天然气耗量", TodayValue = 64, YesterdayDelta = 75, MonthValue = 157, LastMonthValue = 458 },
        new EnergyCard { Name = "今日生物燃气耗量", TodayValue = 88, YesterdayDelta = 66, MonthValue = 545, LastMonthValue = 454 },
        new EnergyCard { Name = "今日石油耗量", TodayValue = 75, YesterdayDelta = 65, MonthValue = 885, LastMonthValue = 254 },
        new EnergyCard { Name = "今日太阳能耗量", TodayValue = 45, YesterdayDelta = 65, MonthValue = 457, LastMonthValue = 452 },
        new EnergyCard { Name = "今日风能耗量", TodayValue = 85, YesterdayDelta = 67, MonthValue = 456, LastMonthValue = 454 },
        new EnergyCard { Name = "今日氢气耗量", TodayValue = 78, YesterdayDelta = 44, MonthValue = 654, LastMonthValue = 874 },
    };

    // ===== 右侧：耗量对比图 =====
    public ISeries[] Series { get; set; }
    public Axis[] XAxes { get; set; }
    public Axis[] YAxes { get; set; }

    public PLCViewModel()
    {
        InitIcons();
        InitChart();
    }

    // 8 个 Material Design 图标 path（24x24 viewBox），顺序与 EnergyCards 对应
    private static readonly string[] IconPaths =
    {
        "M11 15H6L13 1V9H18L11 23V15Z", // 闪电 - 电力
        "M12,20A6,6 0 0,1 6,14C6,10 12,3.25 12,3.25C12,3.25 18,10 18,14A6,6 0 0,1 12,20Z", // 水滴 - 水力
        "M17.66 11.2C17.43 10.9 17.15 10.64 16.89 10.38C16.22 9.78 15.46 9.35 14.82 8.72C13.33 7.26 13 4.85 13.95 3C13 3.23 12.17 3.75 11.46 4.32C8.87 6.4 7.85 10.07 9.07 13.22C9.11 13.32 9.15 13.42 9.15 13.55C9.15 13.77 9 13.97 8.8 14.05C8.57 14.15 8.33 14.09 8.14 13.93C8.08 13.88 8.04 13.83 8 13.76C6.87 12.33 6.69 10.28 7.45 8.64C5.78 10 4.87 12.3 5 14.47C5.06 14.97 5.12 15.47 5.29 15.97C5.43 16.57 5.7 17.17 6 17.7C7.08 19.43 8.95 20.67 10.96 20.92C13.1 21.19 15.39 20.8 17.03 19.32C18.86 17.66 19.5 15 18.56 12.72L18.43 12.46C18.22 12 17.66 11.2 17.66 11.2M14.5 17.5C14.22 17.74 13.76 18 13.4 18.1C12.28 18.5 11.16 17.94 10.5 17.28C11.69 17 12.4 16.12 12.61 15.23C12.78 14.43 12.46 13.77 12.33 13C12.21 12.26 12.23 11.63 12.5 10.94C12.69 11.32 12.89 11.7 13.13 12C13.9 13 15.11 13.44 15.37 14.8C15.41 14.94 15.43 15.08 15.43 15.23C15.46 16.05 15.1 16.95 14.5 17.5H14.5Z", // 火焰 - 天然气
        "M17,8C8,10 5.9,16.17 3.82,21.34L5.71,22L6.66,19.7C7.14,19.87 7.64,20 8,20C19,20 22,3 22,3C21,5 14,5.25 9,6.25C4,7.25 2,11.5 2,13.5C2,15.5 3.75,17.25 3.75,17.25C7,8 17,8 17,8Z", // 叶子 - 生物燃气
        "M3,2H6C6.28,2 6.53,2.11 6.71,2.29L8.79,4.38L9.59,3.59C10,3.2 10.5,3 11,3H17C17.5,3 18,3.2 18.41,3.59L19.41,4.59C19.8,5 20,5.5 20,6V19A2,2 0 0,1 18,21H8A2,2 0 0,1 6,19V13L6,12V8C6,7.5 6.2,7 6.59,6.59L7.38,5.79L5.59,4H3V2M11,5V7H17V5H11M11.41,11L9.41,9H8V10.41L10,12.41V15.59L8,17.59V19H9.41L11.41,17H14.59L16.59,19H18V17.59L16,15.59V12.41L18,10.41V9H16.59L14.59,11H11.41M12,13H14V15H12V13Z", // 加油机 - 石油
        "M3.55 19.09L4.96 20.5L6.76 18.71L5.34 17.29M12 6C8.69 6 6 8.69 6 12S8.69 18 12 18 18 15.31 18 12C18 8.68 15.31 6 12 6M20 13H23V11H20M17.24 18.71L19.04 20.5L20.45 19.09L18.66 17.29M20.45 5L19.04 3.6L17.24 5.39L18.66 6.81M13 1H11V4H13M6.76 5.39L4.96 3.6L3.55 5L5.34 6.81L6.76 5.39M1 13H4V11H1M13 20H11V23H13", // 太阳 - 太阳能
        "M13.33,11.67L16.21,14.58C17.62,13.16 16.21,11.75 16.21,11.75L14.72,10.24C14.9,9.86 15,9.44 15,9C15,7.95 14.46,7.03 13.64,6.5L15,2.11C13.09,1.53 12.5,3.44 12.5,3.44L11.69,6.03C10.46,6.16 9.46,7 9.13,8.18L4.67,9.63C5.31,11.53 7.2,10.9 7.2,10.9L9.27,10.23C9.61,10.97 10.23,11.54 11,11.82V19C11,19 9,19 9,21C9,21.5 9,21.81 9,22H15V21C15,21 15,19 13,19V11.82C13.12,11.78 13.23,11.72 13.33,11.67M10.5,9A1.5,1.5 0 0,1 12,7.5A1.5,1.5 0 0,1 13.5,9A1.5,1.5 0 0,1 12,10.5A1.5,1.5 0 0,1 10.5,9Z", // 风车 - 风能
        "M12,11A1,1 0 0,1 13,12A1,1 0 0,1 12,13A1,1 0 0,1 11,12A1,1 0 0,1 12,11M4.22,4.22C5.65,2.79 8.75,3.43 12,5.56C15.25,3.43 18.35,2.79 19.78,4.22C21.21,5.65 20.57,8.75 18.44,12C20.57,15.25 21.21,18.35 19.78,19.78C18.35,21.21 15.25,20.57 12,18.44C8.75,20.57 5.65,21.21 4.22,19.78C2.79,18.35 3.43,15.25 5.56,12C3.43,8.75 2.79,5.65 4.22,4.22M15.54,8.46C16.15,9.08 16.71,9.71 17.23,10.34C18.61,8.21 19.11,6.38 18.36,5.64C17.62,4.89 15.79,5.39 13.66,6.77C14.29,7.29 14.92,7.85 15.54,8.46M8.46,15.54C7.85,14.92 7.29,14.29 6.77,13.66C5.39,15.79 4.89,17.62 5.64,18.36C6.38,19.11 8.21,18.61 10.34,17.23C9.71,16.71 9.08,16.15 8.46,15.54M5.64,5.64C4.89,6.38 5.39,8.21 6.77,10.34C7.29,9.71 7.85,9.08 8.46,8.46C9.08,7.85 9.71,7.29 10.34,6.77C8.21,5.39 6.38,4.89 5.64,5.64M9.88,14.12C10.58,14.82 11.3,15.46 12,16.03C12.7,15.46 13.42,14.82 14.12,14.12C14.82,13.42 15.46,12.7 16.03,12C15.46,11.3 14.82,10.58 14.12,9.88C13.42,9.18 12.7,8.54 12,7.97C11.3,8.54 10.58,9.18 9.88,9.88C9.18,10.58 8.54,11.3 7.97,12C8.54,12.7 9.18,13.42 9.88,14.12M18.36,18.36C19.11,17.62 18.61,15.79 17.23,13.66C16.71,14.29 16.15,14.92 15.54,15.54C14.92,16.15 14.29,16.71 13.66,17.23C15.79,18.61 17.62,19.11 18.36,18.36Z", // 原子 - 氢气
    };

    private void InitIcons()
    {
        for (var i = 0; i < EnergyCards.Count; i++)
            EnergyCards[i].IconGeometry = Geometry.Parse(IconPaths[i]);
    }

    private void InitChart()
    {
        // 数据（从设计图读取），顺序：电能、水能、天然气、生物燃气、石油、太阳能、风能、氢气
        double[] blue = { 62, 77, 77, 45, 16, 100, 16, 17 };
        double[] red = { 44, 35, 24, 13, 16, 43, 54, 55 };
        double[] orange = { 85, 86, 77, 97, 77, 78, 68, 89 };
        double[] green = { 82, 86, 89, 67, 78, 88, 98, 79 };

        // 柱子渐变（上实下淡）：构造函数第3、4参数为渐变起止点（相对坐标）
        var blueGradient = new LinearGradientPaint(
            new SKColor(48, 109, 230),
            new SKColor(48, 109, 230, 30),
            new SKPoint(0.5f, 0),
            new SKPoint(0.5f, 1));
        var redGradient = new LinearGradientPaint(
            new SKColor(235, 80, 80),
            new SKColor(235, 80, 80, 30),
            new SKPoint(0.5f, 0),
            new SKPoint(0.5f, 1));

        Series = new ISeries[]
        {
            new ColumnSeries<double>
            {
                Name = "今日", Values = blue, Fill = blueGradient, MaxBarWidth = 14
            },
            new ColumnSeries<double>
            {
                Name = "昨日", Values = red, Fill = redGradient, MaxBarWidth = 14
            },
            new LineSeries<double>
            {
                Name = "趋势一", Values = orange,
                Stroke = new SolidColorPaint(new SKColor(245, 166, 35)) { StrokeThickness = 2 },
                Fill = null,
                GeometrySize = 7,
                GeometryFill = new SolidColorPaint(SKColors.White),
                GeometryStroke = new SolidColorPaint(new SKColor(245, 166, 35)) { StrokeThickness = 2 },
                LineSmoothness = 0.5
            },
            new LineSeries<double>
            {
                Name = "趋势二", Values = green,
                Stroke = new SolidColorPaint(new SKColor(60, 170, 75)) { StrokeThickness = 2 },
                Fill = null,
                GeometrySize = 7,
                GeometryFill = new SolidColorPaint(SKColors.White),
                GeometryStroke = new SolidColorPaint(new SKColor(60, 170, 75)) { StrokeThickness = 2 },
                LineSmoothness = 0.5
            }
        };

        XAxes = new[]
        {
            new Axis
            {
                Labels = new[] { "电能", "水能", "天然气", "生物燃气", "石油", "太阳能", "风能", "氢气" },
                LabelsPaint = new SolidColorPaint(new SKColor(102, 102, 102)),
                TextSize = 12
            }
        };

        YAxes = new[]
        {
            new Axis
            {
                MinLimit = 0, MaxLimit = 100,
                LabelsPaint = new SolidColorPaint(new SKColor(102, 102, 102)),
                TextSize = 12,
                SeparatorsPaint = new SolidColorPaint(new SKColor(235, 235, 235)) { StrokeThickness = 1 }
            }
        };
    }
}

/// <summary>左侧能源数值输入项</summary>
public partial class EnergyInput : ViewModelBase
{
    public string Label { get; set; } = string.Empty;

    [ObservableProperty] private string _inputValue = string.Empty;
}

/// <summary>右侧能源卡片</summary>
public partial class EnergyCard : ViewModelBase
{
    public string Name { get; set; } = string.Empty;
    public int TodayValue { get; set; }
    public int YesterdayDelta { get; set; }
    public int MonthValue { get; set; }
    public int LastMonthValue { get; set; }

    /// <summary>矢量图标（24x24 坐标系）</summary>
    public Geometry? IconGeometry { get; set; }
}
