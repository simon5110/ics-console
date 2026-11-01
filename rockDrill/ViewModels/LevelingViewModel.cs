using System;
using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace rockDrill.ViewModels;

/// <summary>支腿状态。</summary>
public partial class LegVm : ObservableObject
{
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _stateText = "支撑到位";
    [ObservableProperty] private IBrush _brush = Brushes.LimeGreen;
}

/// <summary>设备调平：倾角仪 + 调平控制。</summary>
public partial class LevelingViewModel : ViewModelBase
{
    private readonly Random _rnd = new();
    private readonly DispatcherTimer _timer;

    [ObservableProperty] private double _tiltX = -0.01;
    [ObservableProperty] private double _tiltY;
    [ObservableProperty] private string _rollText = "-0.01°";
    [ObservableProperty] private string _pitchText = "0.00°";
    [ObservableProperty] private string _stateText = "已调平";
    [ObservableProperty] private bool _isLeveled = true;
    [ObservableProperty] private string _modeText = "自动调平模式";
    [ObservableProperty] private string _lastLevelText = "2026-10-31 07:48";
    [ObservableProperty] private string _actionHint = "车体水平度满足钻孔条件，可进入定位工序。";

    public ObservableCollection<LegVm> Legs { get; } = new();

    public LevelingViewModel()
    {
        Legs.Add(new LegVm { Name = "左前支腿" });
        Legs.Add(new LegVm { Name = "右前支腿" });
        Legs.Add(new LegVm { Name = "左后支腿" });
        Legs.Add(new LegVm { Name = "右后支腿" });

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _timer.Tick += (_, _) => Simulate();
        _timer.Start();
    }

    private void Simulate()
    {
        TiltX = Math.Round(-0.01 + (_rnd.NextDouble() - 0.5) * 0.04, 2);
        TiltY = Math.Round((_rnd.NextDouble() - 0.5) * 0.03, 2);
        RollText = $"{TiltX:F2}°";
        PitchText = $"{TiltY:F2}°";
        IsLeveled = Math.Abs(TiltX) <= 0.2 && Math.Abs(TiltY) <= 0.2;
        StateText = IsLeveled ? "已调平" : "需微调";
        ActionHint = IsLeveled
            ? "车体水平度满足钻孔条件，可进入定位工序。"
            : "倾角超过 ±0.20°，请检查支腿支撑或执行自动调平。";
    }

    [RelayCommand]
    private void StartLeveling()
    {
        ModeText = "自动调平中";
        StateText = "调平中";
        ActionHint = "正在驱动四条支腿补偿，左右倾角与前后倾角将收敛至 ±0.05°。";
    }

    [RelayCommand]
    private void StopLeveling()
    {
        ModeText = "手动模式";
        StateText = "已暂停";
        ActionHint = "调平已暂停，可手动点动支腿后重新执行自动调平。";
    }
}
