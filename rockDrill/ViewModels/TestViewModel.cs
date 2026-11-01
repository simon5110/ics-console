using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace rockDrill.ViewModels;

/// <summary>臂架控制测试（维修模式下的关节点动）。</summary>
public partial class TestViewModel : ViewModelBase
{
    [ObservableProperty] private string _stateText = "控制已停用";
    [ObservableProperty] private bool _isEnabled;
    [ObservableProperty] private string _modeText = "维修模式";
    [ObservableProperty] private string _lastActionText = "— —";

    public ObservableCollection<string> Commands { get; } = new();

    public TestViewModel()
    {
        Add("左臂架回转 左", "左臂架回转 右", "右臂架回转 左", "右臂架回转 右");
        Add("左曲臂俯仰 下", "左曲臂俯仰 上", "右曲臂俯仰 下", "右曲臂俯仰 上");
        Add("左直臂俯仰 下", "左直臂俯仰 上", "右直臂俯仰 下", "右直臂俯仰 上");
        Add("左直臂伸缩 伸", "左直臂伸缩 缩", "右直臂伸缩 伸", "右直臂伸缩 缩");
        Add("左凿岩翻转 正转", "左凿岩翻转 反转", "右凿岩翻转 正转", "右凿岩翻转 反转");
        Add("左凿岩俯仰 下", "左凿岩俯仰 上", "右凿岩俯仰 下", "右凿岩俯仰 上");
        Add("左凿岩偏摆 左", "左凿岩偏摆 右", "右凿岩偏摆 左", "右凿岩偏摆 右");
        Add("左凿岩补偿 前", "左凿岩补偿 后", "右凿岩补偿 前", "右凿岩补偿 后");
        Add("左凿岩推进 前", "左凿岩推进 后", "右凿岩推进 前", "右凿岩推进 后");
    }

    private void Add(params string[] items)
    {
        foreach (var i in items) Commands.Add(i);
    }

    [RelayCommand]
    private void EnableControl()
    {
        IsEnabled = true;
        StateText = "控制已启用";
        LastActionText = "使能输出已接通，可执行单关节点动。";
    }

    [RelayCommand]
    private void DisableControl()
    {
        IsEnabled = false;
        StateText = "控制已停用";
        LastActionText = "使能输出已断开，所有关节动作被禁止。";
    }

    [RelayCommand]
    private void Reset()
    {
        IsEnabled = false;
        StateText = "控制已停用";
        LastActionText = "测试状态已复位，请重新确认现场安全后启用控制。";
    }
}
