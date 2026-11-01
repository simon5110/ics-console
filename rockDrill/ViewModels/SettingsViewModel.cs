using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace rockDrill.ViewModels;

/// <summary>参数设置。</summary>
public partial class SettingsViewModel : ViewModelBase
{
    // ---- 凿岩参数 ----
    [ObservableProperty] private string _impactMax = "22.0";
    [ObservableProperty] private string _rotationMax = "15.0";
    [ObservableProperty] private string _feedMax = "12.0";
    [ObservableProperty] private string _waterMin = "1.2";
    [ObservableProperty] private string _retractDelay = "3.5";
    [ObservableProperty] private string _holeTimeout = "240";
    [ObservableProperty] private bool _autoRetract = true;
    [ObservableProperty] private bool _adaptivePressure = true;

    // ---- 臂架限位 ----
    [ObservableProperty] private string _swingLimit = "± 45";
    [ObservableProperty] private string _pitchLimit = "-8 ~ 62";
    [ObservableProperty] private string _telescopeMax = "1800";
    [ObservableProperty] private string _compensateMax = "800";
    [ObservableProperty] private bool _antiCollision = true;
    [ObservableProperty] private bool _slowZone = true;

    // ---- 传感器标定 ----
    [ObservableProperty] private string _leftTiltZero = "0.02";
    [ObservableProperty] private string _rightTiltZero = "-0.01";
    [ObservableProperty] private string _pressureGain = "1.000";
    [ObservableProperty] private string _displacementRange = "2000";
    [ObservableProperty] private string _calibDate = "2026-10-18 09:24";

    // ---- 网络 ----
    [ObservableProperty] private string _localIp = "192.168.1.20";
    [ObservableProperty] private string _gateway = "192.168.1.1";
    [ObservableProperty] private string _mesAddress = "10.20.5.18:8899";
    [ObservableProperty] private string _canBitrate = "500 kbit/s";
    [ObservableProperty] private string _uploadPeriod = "10";
    [ObservableProperty] private bool _cloudSync = true;

    [ObservableProperty] private string _saveHint = "参数最后保存：2026-10-30 18:42";

    public string MachineModel => "DW2-100 双臂凿岩台车";
    public string SerialNo => "DW2-2024-0071";
    public string SoftwareVersion => "ICS 2.4.1 (build 20261026)";
    public string FirmwareVersion => "EC-0832G V1.8.3";
    public string WorkHours => "4 268 h";
    public string LastMaintenance => "2026-09-28 · 500h 保养";

    public void MarkSaved() => SaveHint = "参数已保存 · 生效于下一次循环";

    [RelayCommand]
    private void Save() => MarkSaved();

    [RelayCommand]
    private void RestoreDefaults() => SaveHint = "已恢复出厂参数（待保存）";
}
