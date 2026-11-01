using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace rockDrill.ViewModels;

/// <summary>全站仪定位。</summary>
public partial class PositioningViewModel : ViewModelBase
{
    private readonly Random _rnd = new();

    [ObservableProperty] private string _leftX = "-5.083";
    [ObservableProperty] private string _leftY = "13.503";
    [ObservableProperty] private string _leftZ = "0.607";
    [ObservableProperty] private string _rightX = "-2.796";
    [ObservableProperty] private string _rightY = "14.988";
    [ObservableProperty] private string _rightZ = "0.341";

    [ObservableProperty] private string _statusText = "未定位";
    [ObservableProperty] private bool _isLocated;
    [ObservableProperty] private string _deviationText = "— —";
    [ObservableProperty] private string _lastTimeText = "— —";
    [ObservableProperty] private string _hintText = "请输入左右后视棱镜坐标后执行定位。";

    public string ChainageText => "K2 + 186.5";
    public string AzimuthText => "327° 18′ 24″";
    public string SlopeText => "-1.2 %";

    [RelayCommand]
    private void Reset()
    {
        LeftX = "-5.083";
        LeftY = "13.503";
        LeftZ = "0.607";
        RightX = "-2.796";
        RightY = "14.988";
        RightZ = "0.341";
        StatusText = "未定位";
        IsLocated = false;
        DeviationText = "— —";
        HintText = "坐标已重置为上次标定值，请重新执行定位。";
    }

    [RelayCommand]
    private void Locate()
    {
        StatusText = "已定位";
        IsLocated = true;
        DeviationText = $"{_rnd.Next(1, 6)} mm";
        LastTimeText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        HintText = "定位完成，后方交会残差在允许范围内，可进入扫描或布孔工序。";
    }

    [RelayCommand]
    private void QuickLocate()
    {
        StatusText = "快速定位完成";
        IsLocated = true;
        DeviationText = $"{_rnd.Next(4, 12)} mm";
        LastTimeText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        HintText = "快速定位使用上次棱镜坐标，偏差略大，建议复核后进入钻孔。";
    }
}
