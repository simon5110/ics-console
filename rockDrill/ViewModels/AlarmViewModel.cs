using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace rockDrill.ViewModels;

/// <summary>告警中心。</summary>
public partial class AlarmViewModel : ViewModelBase
{
    [ObservableProperty] private AlarmVm? _selectedAlarm;
    [ObservableProperty] private string _summaryText = "当前 10 条告警 · 3 条未确认";

    public ObservableCollection<AlarmVm> Alarms { get; } = new();
    public ObservableCollection<AlarmVm> History { get; } = new();

    public AlarmViewModel()
    {
        Alarms.Add(new AlarmVm
        {
            Time = "08:42:15", Code = "A-2103", Message = "右臂推进压力短时低于下限",
            Source = "右臂凿岩机", Level = "一般", State = "未确认",
            Brush = new SolidColorBrush(Color.Parse("#F5A623")),
            Advice = "检查推进比例阀给定值与推进梁导轨阻力；确认钻杆是否卡滞，必要时手动退钎后重新对孔。"
        });
        Alarms.Add(new AlarmVm
        {
            Time = "08:37:02", Code = "A-1874", Message = "左臂回转压力波动超限",
            Source = "左臂液压回路", Level = "严重", State = "未确认",
            Brush = new SolidColorBrush(Color.Parse("#ED4C5C")),
            Advice = "检查回转马达溢流阀、回转压力传感器接线；若伴随冲击压力下降，停机检查钎尾磨损。"
        });
        Alarms.Add(new AlarmVm
        {
            Time = "08:21:44", Code = "A-0912", Message = "液压油温接近报警阈值",
            Source = "液压系统", Level = "一般", State = "未确认",
            Brush = new SolidColorBrush(Color.Parse("#F5A623")),
            Advice = "确认液压油散热器风扇运转正常、散热片无粉尘堵塞；油温超过 75 ℃ 请降低负载。"
        });
        Alarms.Add(new AlarmVm
        {
            Time = "08:12:31", Code = "A-3007", Message = "左臂夹钎保护动作",
            Source = "左臂凿岩机", Level = "紧急", State = "已确认",
            Brush = new SolidColorBrush(Color.Parse("#FF4D4F")),
            Advice = "已自动停止推进并反转退出。检查孔内是否塌孔、钎杆是否弯曲，确认后方可继续钻孔。",
            IsConfirmed = true
        });
        Alarms.Add(new AlarmVm
        {
            Time = "07:58:09", Code = "A-0451", Message = "右操作台急停回路断开",
            Source = "右操作台", Level = "严重", State = "已确认",
            Brush = new SolidColorBrush(Color.Parse("#ED4C5C")),
            Advice = "确认急停按钮已复位、电缆插头锁紧；复位后在系统监控页检查 DI 状态。",
            IsConfirmed = true
        });
        Alarms.Add(new AlarmVm
        {
            Time = "07:47:52", Code = "A-5120", Message = "数显表通讯超时（3 次）",
            Source = "CAN1 网络", Level = "一般", State = "已确认",
            Brush = new SolidColorBrush(Color.Parse("#F5A623")),
            Advice = "检查数显表终端电阻与 CAN 线束屏蔽接地；通讯恢复后告警自动复位。",
            IsConfirmed = true
        });
        Alarms.Add(new AlarmVm
        {
            Time = "07:35:20", Code = "A-6621", Message = "左臂倾角传感器零点漂移",
            Source = "左臂传感器", Level = "提示", State = "已确认",
            Brush = new SolidColorBrush(Color.Parse("#23A9F0")),
            Advice = "在设置-传感器标定中对左臂倾角传感器执行零点标定（需臂架收回至参考位置）。",
            IsConfirmed = true
        });
        Alarms.Add(new AlarmVm
        {
            Time = "07:20:11", Code = "A-0042", Message = "发动机水温偏高",
            Source = "发动机", Level = "提示", State = "已确认",
            Brush = new SolidColorBrush(Color.Parse("#23A9F0")),
            Advice = "检查冷却液液位与散热器通风；短时高水温可在怠速下冷却 5 分钟。",
            IsConfirmed = true
        });
        Alarms.Add(new AlarmVm
        {
            Time = "07:04:38", Code = "A-7712", Message = "冲洗水流量低",
            Source = "水路系统", Level = "一般", State = "已确认",
            Brush = new SolidColorBrush(Color.Parse("#F5A623")),
            Advice = "检查水泵压力、水阀开度与滤网堵塞情况，流量低于 25 L/min 禁止开孔。",
            IsConfirmed = true
        });
        Alarms.Add(new AlarmVm
        {
            Time = "06:52:03", Code = "A-2001", Message = "布孔图与实测掌子面偏差 46 mm",
            Source = "扫描系统", Level = "提示", State = "已确认",
            Brush = new SolidColorBrush(Color.Parse("#23A9F0")),
            Advice = "偏差在允许范围内（≤50 mm），建议复核周边孔外插角后继续。",
            IsConfirmed = true
        });

        foreach (var a in Alarms) History.Add(a);

        SelectedAlarm = Alarms[1];
    }

    public int UrgentCount => Count("紧急");
    public int SeriousCount => Count("严重");
    public int NormalCount => Count("一般");
    public int InfoCount => Count("提示");

    private int Count(string level)
    {
        var n = 0;
        foreach (var a in Alarms)
            if (a.Level == level) n++;
        return n;
    }

    [RelayCommand]
    private void ConfirmSelected()
    {
        if (SelectedAlarm is null) return;
        SelectedAlarm.State = "已确认";
        SelectedAlarm.IsConfirmed = true;
        SummaryText = "当前 10 条告警 · 2 条未确认";
    }

    [RelayCommand]
    private void ClearAll()
    {
        foreach (var a in Alarms)
        {
            a.State = "已确认";
            a.IsConfirmed = true;
        }
        SummaryText = "当前 10 条告警 · 全部已确认";
    }
}
