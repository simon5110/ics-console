using System;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace rockDrill.ViewModels;

/// <summary>一条钻孔历史记录。</summary>
public class DataRowVm
{
    public string Id { get; init; } = string.Empty;
    public string Time { get; init; } = string.Empty;
    public string Chainage { get; init; } = string.Empty;
    public string DisplayState { get; init; } = "正常";
    public string ValveState { get; init; } = "正常";
    public string OilTemp { get; init; } = "0";
    public string OilLevel { get; init; } = "0";
    public string LeftImpact { get; init; } = "0";
    public string LeftBack { get; init; } = "0";
    public string LeftRotate { get; init; } = "0";
    public string LeftFeed { get; init; } = "0";
    public string RightImpact { get; init; } = "0";
    public string LeftDepth { get; init; } = "0";
}

/// <summary>数据管理：施工参数录入 + 钻孔历史记录。</summary>
public partial class DataRecordViewModel : ViewModelBase
{
    private readonly Random _rnd = new();

    [ObservableProperty] private string _siteCode = string.Empty;
    [ObservableProperty] private string _startChainageNo = string.Empty;
    [ObservableProperty] private string _startChainageValue = string.Empty;
    [ObservableProperty] private string _idCard = string.Empty;
    [ObservableProperty] private string _machineNo = "DW2-2024-0071";
    [ObservableProperty] private string _workType = "开挖钻孔作业";
    [ObservableProperty] private string _holeDiameter = string.Empty;

    [ObservableProperty] private string _chainageInput = string.Empty;
    [ObservableProperty] private string _currentChainageText = "未设置";
    [ObservableProperty] private string _hintText = "录入工点信息后点击「确认」，记录将绑定当前施工循环。";

    public ObservableCollection<DataRowVm> Rows { get; } = new();

    public string TotalText => "共 99 335 条记录";
    public string PageText => "第 1 / 1987 页";

    public DataRecordViewModel()
    {
        LoadRows();
    }

    private void LoadRows()
    {
        Rows.Clear();
        var t = new DateTime(2026, 10, 31, 9, 29, 40);

        for (var i = 0; i < 40; i++)
        {
            var time = t.AddSeconds(-i * 37);
            Rows.Add(new DataRowVm
            {
                Id = (381245 - i).ToString(),
                Time = time.ToString("yyyy-MM-dd HH:mm:ss"),
                Chainage = $"K2+{186 + i * 0.003:F3}",
                DisplayState = "正常",
                ValveState = "正常",
                OilTemp = $"{52 + _rnd.Next(0, 8)}",
                OilLevel = $"{68 + _rnd.Next(0, 8)}",
                LeftImpact = $"{138 + _rnd.Next(0, 12)}",
                LeftBack = $"{22 + _rnd.Next(0, 5)}",
                LeftRotate = $"{76 + _rnd.Next(0, 10)}",
                LeftFeed = $"{58 + _rnd.Next(0, 8)}",
                RightImpact = $"{134 + _rnd.Next(0, 12)}",
                LeftDepth = $"{3500 + _rnd.Next(0, 40)}"
            });
        }
    }

    [RelayCommand]
    private void Refresh() => LoadRows();

    [RelayCommand]
    private void ConfirmSite()
    {
        HintText = string.IsNullOrWhiteSpace(SiteCode)
            ? "工点编码不能为空，请先录入工点信息。"
            : $"工点 {SiteCode} 信息已确认，机号 {MachineNo} 已绑定本循环。";
    }

    [RelayCommand]
    private void SetChainage()
    {
        CurrentChainageText = string.IsNullOrWhiteSpace(ChainageInput) ? "未设置" : ChainageInput;
        HintText = $"当前里程号已设置为 {CurrentChainageText}。";
    }
}
