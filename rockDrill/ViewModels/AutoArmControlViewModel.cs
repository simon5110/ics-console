using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using rockDrill.Services;

namespace rockDrill.ViewModels;

/// <summary>比例阀组定义：A / B 两个相反方向 + 对应的反馈通道号。</summary>
public sealed class ValveDefinition
{
    public ValveDefinition(string nameA, string commandA, string nameB, string commandB, int sensorIndex)
    {
        NameA = nameA;
        CommandA = commandA;
        NameB = nameB;
        CommandB = commandB;
        SensorIndex = sensorIndex;
    }

    public string NameA { get; }
    public string CommandA { get; }
    public string NameB { get; }
    public string CommandB { get; }
    public int SensorIndex { get; }
}

/// <summary>阀门行状态色调。</summary>
public enum ValveStatusTone
{
    Idle,
    Info,
    Ok,
    Warn,
    Bad
}

/// <summary>自动控臂表格中的一行比例阀。</summary>
public partial class ValveRowVm : ObservableObject
{
    private static readonly IBrush IdleBrush = new SolidColorBrush(Color.Parse("#90A9B9"));
    private static readonly IBrush InfoBrush = new SolidColorBrush(Color.Parse("#23A9F0"));
    private static readonly IBrush OkBrush = new SolidColorBrush(Color.Parse("#2AD07C"));
    private static readonly IBrush WarnBrush = new SolidColorBrush(Color.Parse("#F5A623"));
    private static readonly IBrush BadBrush = new SolidColorBrush(Color.Parse("#ED4C5C"));

    public ValveRowVm(ValveDefinition definition,
                      Action<ValveRowVm> start,
                      Action<ValveRowVm> stop,
                      Action<ValveRowVm> pid)
    {
        Definition = definition;
        StartCommand = new RelayCommand(() => start(this));
        StopCommand = new RelayCommand(() => stop(this));
        PidCommand = new RelayCommand(() => pid(this));
    }

    public ValveDefinition Definition { get; }
    public string NameA => Definition.NameA;
    public string NameB => Definition.NameB;

    public IRelayCommand StartCommand { get; }
    public IRelayCommand StopCommand { get; }
    public IRelayCommand PidCommand { get; }

    /// <summary>A 方向开度（0–100）。</summary>
    [ObservableProperty] private decimal? _openA = 0;

    /// <summary>B 方向开度（0–100）。</summary>
    [ObservableProperty] private decimal? _openB = 0;

    /// <summary>目标读数（可留空）。</summary>
    [ObservableProperty] private string _targetText = string.Empty;

    /// <summary>当前读数显示。</summary>
    [ObservableProperty] private string _currentText = "--";

    [ObservableProperty] private string _statusText = "空闲";
    [ObservableProperty] private IBrush _statusBrush = IdleBrush;

    /// <summary>本行是否正在输出。</summary>
    [ObservableProperty] private bool _isRunning;

    [ObservableProperty] private bool _inputsEnabled = true;
    [ObservableProperty] private bool _startEnabled = true;
    [ObservableProperty] private bool _stopEnabled;
    [ObservableProperty] private bool _pidEnabled = true;

    // ===== 运行期状态（不参与绑定） =====
    public double StartValue;
    public DateTime StartedAt;
    public double? TargetValue;
    public int ActiveDirection;
    public int ActiveOpening;

    public void SetStatus(string text, ValveStatusTone tone)
    {
        StatusText = text;
        StatusBrush = tone switch
        {
            ValveStatusTone.Info => InfoBrush,
            ValveStatusTone.Ok => OkBrush,
            ValveStatusTone.Warn => WarnBrush,
            ValveStatusTone.Bad => BadBrush,
            _ => IdleBrush
        };
    }

    /// <summary>取当前方向的开度值。</summary>
    public int OpeningA => (int)(OpenA ?? 0);

    /// <summary>取当前方向的开度值。</summary>
    public int OpeningB => (int)(OpenB ?? 0);

    /// <summary>清空输入与运行状态（不负责发送置零帧）。</summary>
    public void ResetInputs(string status, ValveStatusTone tone)
    {
        IsRunning = false;
        TargetValue = null;
        ActiveOpening = 0;
        OpenA = 0;
        OpenB = 0;
        SetStatus(status, tone);
    }
}

/// <summary>PID 试探 / 位置保持运行期数据。</summary>
internal sealed class PidSession
{
    public required ValveRowVm Row { get; init; }
    public required double Target { get; init; }
    public DateTime StartedAt { get; init; } = DateTime.UtcNow;
    public DateTime NextProbeAt { get; set; }
    public DateTime NextHoldAt { get; set; }
    public double PreviousValue { get; set; }
    public int TrialDirection { get; set; }
    public int ActiveDirection { get; set; } = -1;
    public int Opening { get; set; } = 1;
    public int TrialCount { get; set; }
    public bool Holding { get; set; }
    public double Kp { get; set; }
    public double Ki { get; set; }
    public double Kd { get; set; }
    public double Integral { get; set; }
    public double? PreviousError { get; set; }
    public int IncreasingDirection { get; set; }
    public string PidText { get; set; } = string.Empty;

    public List<KeyValuePair<double, double>> Samples { get; } = new();
}

/// <summary>
/// 自动控臂（ZeroMQ）：7 组比例阀手动开度 / 目标读数运行 / PID 低开度试探与位置保持。
/// 对应旧版 WinForms <c>FormZmqAutoControl</c>，控制逻辑与安全联锁保持一致。
/// </summary>
public partial class AutoArmControlViewModel : ViewModelBase
{
    private const int PublishIntervalMs = 50;
    private const int ZeroRepeat = 3;
    private const double SensorTimeoutSeconds = 0.5;
    private const double Tolerance = 0.05;
    private const int ProbeIntervalMs = 2000;
    private const double ResponseTimeSeconds = 2;
    private const int ProbeTimeoutSeconds = 180;
    private const int HoldIntervalMs = 500;
    private const int ProbeOpenMax = 15;

    private static readonly ValveDefinition[] ValveDefinitions =
    {
        new("大臂俯", "Boom_Down", "大臂仰", "Boom_Up", 1),
        new("大臂左", "Boom_Left", "大臂右", "Boom_Right", 0),
        new("大臂伸", "Boom_Extend", "大臂缩", "Boom_Retract", 2),
        new("推进梁正转", "RockDrill_FWD", "推进梁反转", "RockDrill_REV", 3),
        new("推进梁俯", "RockDrill_Down", "推进梁俯仰上", "RockDrill_Up", 5),
        new("推进梁左", "RockDrill_Left", "推进梁右", "RockDrill_Right", 4),
        new("补偿伸", "Compensate_Extend", "补偿缩", "Compensate_Retract", 6)
    };

    private readonly ZmqCommandPublisher? _publisher;
    private readonly ZmqSensorFeed _sensors = new();
    private readonly DispatcherTimer _sendTimer;
    private readonly DateTime _publishReadyAt = DateTime.UtcNow.AddSeconds(1);

    private PidSession? _pidRun;
    private bool _publisherFailed;
    private bool _shuttingDown;

    [ObservableProperty] private string _connectionText = "连接控制端，等待发布通道就绪…";
    [ObservableProperty] private bool _connectionOk;
    [ObservableProperty] private string _sensorText = "传感器：等待 ZMQ 数据";
    [ObservableProperty] private bool _sensorOk;
    [ObservableProperty] private string _noticeText = string.Empty;
    [ObservableProperty] private IBrush _noticeBrush = new SolidColorBrush(Color.Parse("#F5A623"));
    [ObservableProperty] private bool _hasNotice;
    [ObservableProperty] private int _armIndex;

    public AutoArmControlViewModel()
    {
        foreach (var definition in ValveDefinitions)
            Rows.Add(new ValveRowVm(definition, OnStartRow, OnStopRow, OnStartPid));

        try
        {
            _publisher = new ZmqCommandPublisher();
        }
        catch (Exception ex)
        {
            _publisherFailed = true;
            ConnectionText = "ZeroMQ 初始化失败：" + ex.Message;
        }

        _sensors.Start();

        _sendTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(PublishIntervalMs) };
        _sendTimer.Tick += OnPublishTick;
        _sendTimer.Start();
    }

    public ObservableCollection<ValveRowVm> Rows { get; } = new();

    public bool IsLeftArm => ArmIndex == 0;
    public bool IsRightArm => ArmIndex == 1;
    public string ArmText => ArmIndex == 0 ? "左臂" : "右臂";

    /// <summary>指令主题前缀：左臂 L_ / 右臂 R_。</summary>
    private string CommandPrefix => ArmIndex == 0 ? "L_" : "R_";

    partial void OnArmIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsLeftArm));
        OnPropertyChanged(nameof(IsRightArm));
        OnPropertyChanged(nameof(ArmText));
        Info("已切换到" + ArmText + "，指令主题前缀 " + CommandPrefix + "。");
    }

    partial void OnNoticeTextChanged(string value) => HasNotice = !string.IsNullOrWhiteSpace(value);

    // ===================== 交互命令 =====================

    [RelayCommand]
    private void SelectArm(string side)
    {
        int target = string.Equals(side, "R", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (target == ArmIndex) return;

        if (Rows.Any(r => r.IsRunning) || _pidRun is not null)
            StopAll("切换臂别，已全部置零");

        ArmIndex = target;

        foreach (var row in Rows)
        {
            row.CurrentText = "--";
            row.ResetInputs("空闲", ValveStatusTone.Idle);
        }

        RefreshRowEnableStates();
    }

    [RelayCommand]
    private void EmergencyStop() => StopAll("急停");

    // ===================== 手动启动 / 停止 =====================

    private void OnStartRow(ValveRowVm row)
    {
        if (!ReadyToCommand())
            return;

        if (_pidRun is not null)
        {
            Warn("PID 保持运行期间不能启动其他阀门动作。请先全部急停。");
            return;
        }

        int openA = row.OpeningA;
        int openB = row.OpeningB;

        if (openA > 0 && openB > 0)
        {
            Warn("同一组的两个相反方向不能同时打开。");
            return;
        }

        if (openA == 0 && openB == 0)
        {
            StopRow(row, "阀门为 0");
            return;
        }

        if (!TryReadSensor(row, out float current))
        {
            Warn(row.NameA + "/" + row.NameB + " 的反馈数据不存在或已超过 0.5 秒，不能启动动作。");
            return;
        }

        if (!TryParseOptionalTarget(row.TargetText, out double? target))
        {
            Warn("目标值必须是有效的有限数字。");
            return;
        }

        row.IsRunning = true;
        row.TargetValue = target;
        row.StartValue = current;
        row.StartedAt = DateTime.UtcNow;
        row.ActiveDirection = openA > 0 ? 0 : 1;
        row.ActiveOpening = openA > 0 ? openA : openB;
        row.SetStatus(target.HasValue
            ? "运行至目标 " + target.Value.ToString("G", CultureInfo.InvariantCulture)
            : "手动运行", ValveStatusTone.Info);

        RefreshRowEnableStates();
        Info("已启动 " + row.NameA + "/" + row.NameB + "，开度 " +
             (openA > 0 ? openA : openB) + "%。");
    }

    private void OnStopRow(ValveRowVm row) => StopRow(row, "已停止");

    private void OnStartPid(ValveRowVm row)
    {
        if (!ReadyToCommand())
            return;

        if (_pidRun is not null || Rows.Any(r => r.IsRunning))
        {
            Warn("请先停止所有其他阀门动作，再启动 PID 试探。");
            return;
        }

        if (!TryParseOptionalTarget(row.TargetText, out double? target) || !target.HasValue)
        {
            Warn("请为该阀门输入有效的目标读数。");
            return;
        }

        if (!TryReadSensor(row, out float current))
        {
            Warn(row.NameA + "/" + row.NameB + " 的反馈数据不存在或已超过 0.5 秒，不能启动 PID。");
            return;
        }

        if (Math.Abs(target.Value - current) <= Tolerance)
        {
            Info("目标已在当前读数附近，无法安全试探运动方向。");
            return;
        }

        StopAll("PID 试探准备");

        var run = new PidSession
        {
            Row = row,
            Target = target.Value,
            NextProbeAt = DateTime.UtcNow.AddMilliseconds(ProbeIntervalMs),
            PreviousValue = current,
            TrialDirection = 0,
            Opening = 1
        };
        _pidRun = run;

        row.IsRunning = true;
        row.TargetValue = target;
        row.StartValue = current;
        row.StartedAt = DateTime.UtcNow;
        row.ActiveDirection = 0;
        row.ActiveOpening = 1;
        row.SetStatus("低开度试探中", ValveStatusTone.Warn);

        RefreshRowEnableStates();
        Info("PID 试探已启动：目标 " + target.Value.ToString("G", CultureInfo.InvariantCulture) +
             "，起始开度 1%。");
    }

    // ===================== 发布循环 =====================

    private void OnPublishTick(object? sender, EventArgs e)
    {
        if (_shuttingDown || _publisherFailed)
            return;

        RefreshCurrentReadings();
        RefreshSensorStatus();

        if (DateTime.UtcNow < _publishReadyAt)
        {
            ConnectionText = "ZeroMQ PUB 已连接，等待通道就绪…";
            return;
        }

        ConnectionOk = true;
        ConnectionText = "发布中：" + ZmqCommandPublisher.Address + " | " + ArmText;

        // 1) 安全联锁：运行中的行若反馈超时，立即全部置零
        foreach (var row in Rows.Where(r => r.IsRunning).ToList())
        {
            if (!TryReadSensor(row, out float current))
            {
                StopAll("传感器超过 0.5 秒无更新，全部置零");
                return;
            }

            if (_pidRun is not null && _pidRun.Row == row)
            {
                if (!UpdatePidRun(current, DateTime.UtcNow))
                    return;
            }
            else if (row.TargetValue.HasValue)
            {
                double target = row.TargetValue.Value;
                if (Math.Abs(current - target) <= Tolerance ||
                    (row.StartValue - target) * (current - target) < 0)
                {
                    double elapsed = (DateTime.UtcNow - row.StartedAt).TotalSeconds;
                    StopRow(row, "到位 " + elapsed.ToString("F1", CultureInfo.InvariantCulture) + "s");
                }
            }
        }

        // 2) 发布：先给反方向置零，再给当前方向下发开度
        if (_publisher is null)
            return;

        try
        {
            var zeroCommands = new List<string>();
            var activeCommands = new List<KeyValuePair<string, int>>();

            foreach (var row in Rows.Where(r => r.IsRunning))
            {
                string topicA = CommandPrefix + row.Definition.CommandA;
                string topicB = CommandPrefix + row.Definition.CommandB;
                if (row.ActiveDirection == 0)
                {
                    zeroCommands.Add(topicB);
                    activeCommands.Add(new KeyValuePair<string, int>(topicA, row.ActiveOpening));
                }
                else
                {
                    zeroCommands.Add(topicA);
                    activeCommands.Add(new KeyValuePair<string, int>(topicB, row.ActiveOpening));
                }
            }

            foreach (string topic in zeroCommands)
                _publisher.Send(topic, 0);

            foreach (var command in activeCommands)
                _publisher.Send(command.Key, command.Value);
        }
        catch (Exception ex)
        {
            FailPublisher("ZeroMQ 发布失败：" + ex.Message);
        }
    }

    private void RefreshCurrentReadings()
    {
        foreach (var row in Rows)
        {
            row.CurrentText = TryReadSensor(row, out float current)
                ? current.ToString("F3", CultureInfo.InvariantCulture)
                : "--";
        }
    }

    private void RefreshSensorStatus()
    {
        if (_sensors.LastError is { Length: > 0 } error)
        {
            SensorText = error;
            SensorOk = false;
            return;
        }

        long count = _sensors.ReceivedCount;
        if (count == 0)
        {
            SensorText = "传感器：等待 ZMQ 数据（" + ZmqSensorFeed.Address + "）";
            SensorOk = false;
            return;
        }

        double age = (DateTime.UtcNow - _sensors.LastReceiveAt).TotalSeconds;
        SensorText = "传感器：" + count.ToString(CultureInfo.InvariantCulture) + " 帧 · " +
                     age.ToString("F0", CultureInfo.InvariantCulture) + "s 前";
        SensorOk = age <= 1.0;
    }

    // ===================== PID 试探 / 保持 =====================

    private bool UpdatePidRun(float current, DateTime now)
    {
        PidSession run = _pidRun!;

        if (!run.Holding)
        {
            if ((now - run.StartedAt).TotalSeconds > ProbeTimeoutSeconds)
            {
                StopAll("PID 试探超过 180 秒，已停止");
                return false;
            }

            if (now < run.NextProbeAt)
                return true;

            double moved = current - run.PreviousValue;
            bool movedEnough = Math.Abs(moved) >= Tolerance;
            bool towardTarget = movedEnough && (run.Target - run.PreviousValue) * moved > 0;

            if (run.ActiveDirection < 0)
            {
                if (towardTarget)
                {
                    run.ActiveDirection = run.TrialDirection;
                    run.Row.ActiveDirection = run.ActiveDirection;
                    run.Row.ActiveOpening = run.Opening;
                    run.Samples.Clear();
                    run.Samples.Add(new KeyValuePair<double, double>(0, run.PreviousValue));
                    run.Samples.Add(new KeyValuePair<double, double>(ProbeIntervalMs / 1000.0, current));
                }
                else if (movedEnough)
                {
                    run.TrialDirection = 1 - run.TrialDirection;
                    run.ActiveDirection = run.TrialDirection;
                    run.PreviousValue = current;
                    run.NextProbeAt = now.AddMilliseconds(ProbeIntervalMs);
                    run.Row.ActiveDirection = run.ActiveDirection;
                    run.Row.SetStatus("方向修正，继续试探", ValveStatusTone.Warn);
                    return true;
                }
                else
                {
                    run.TrialDirection = 1 - run.TrialDirection;
                    run.TrialCount++;
                    if (run.TrialCount % 2 == 0)
                    {
                        run.Opening++;
                        if (run.Opening > ProbeOpenMax)
                        {
                            StopAll("试探开度达到 15 仍无有效位移，请检查限位");
                            return false;
                        }
                    }

                    run.PreviousValue = current;
                    run.NextProbeAt = now.AddMilliseconds(ProbeIntervalMs);
                    run.Row.ActiveDirection = run.TrialDirection;
                    run.Row.ActiveOpening = run.Opening;
                    run.Row.SetStatus("开度 " + run.Opening + " 试探 " +
                                      (run.TrialDirection == 0 ? run.Row.NameA : run.Row.NameB),
                        ValveStatusTone.Warn);
                    return true;
                }
            }
            else
            {
                if (movedEnough)
                {
                    double elapsed = run.Samples.Count > 0
                        ? run.Samples[run.Samples.Count - 1].Key + ProbeIntervalMs / 1000.0
                        : 0;
                    run.Samples.Add(new KeyValuePair<double, double>(elapsed, current));
                }
            }

            double error = run.Target - current;
            run.Row.SetStatus(
                "试探到目标 " + run.Target.ToString("G", CultureInfo.InvariantCulture) +
                "，当前 " + current.ToString("F3", CultureInfo.InvariantCulture),
                ValveStatusTone.Info);

            if (Math.Abs(error) <= Tolerance || (run.Target - run.PreviousValue) * error < 0)
            {
                if (!CalculatePid(run))
                {
                    StopAll("试探位移不足，无法计算 PID");
                    return false;
                }

                run.IncreasingDirection = run.Samples[run.Samples.Count - 1].Value > run.Samples[0].Value
                    ? run.ActiveDirection
                    : 1 - run.ActiveDirection;
                run.Holding = true;
                run.NextHoldAt = now;
                run.PreviousError = null;
                run.Row.SetStatus("到达目标，转入 PID 位置保持（" + run.PidText + "）", ValveStatusTone.Ok);
                return UpdatePidRun(current, now);
            }

            if (!movedEnough)
            {
                run.Opening++;
                if (run.Opening > ProbeOpenMax)
                {
                    StopAll("开度达到 15 仍无法继续移动，已停止");
                    return false;
                }

                run.Samples.Clear();
                run.Samples.Add(new KeyValuePair<double, double>(0, current));
            }

            run.PreviousValue = current;
            run.NextProbeAt = now.AddMilliseconds(ProbeIntervalMs);
            run.Row.ActiveDirection = run.ActiveDirection;
            run.Row.ActiveOpening = run.Opening;
            return true;
        }

        // ===== PID 位置保持 =====
        if (now < run.NextHoldAt)
            return true;

        double holdError = run.Target - current;
        if (Math.Abs(holdError) > 2.0)
        {
            StopAll("PID 保持误差超过 2.0，已停止");
            return false;
        }

        if (run.PreviousError.HasValue && run.PreviousError.Value * holdError < 0)
            run.Integral = 0;

        if (Math.Abs(holdError) <= Tolerance)
        {
            run.Row.ActiveOpening = 0;
            run.Row.SetStatus("保持中 " + run.PidText + "，当前 " +
                              current.ToString("F3", CultureInfo.InvariantCulture) + "（无需修正）",
                ValveStatusTone.Ok);
        }
        else
        {
            double dt = HoldIntervalMs / 1000.0;
            double derivative = run.PreviousError.HasValue
                ? (holdError - run.PreviousError.Value) / dt
                : 0;
            double output = run.Kp * holdError +
                            run.Ki * (run.Integral + holdError * dt) +
                            run.Kd * derivative;
            output = holdError > 0 ? output : -output;
            int opening = (int)Math.Round(Math.Min(ProbeOpenMax, Math.Max(1, output)));
            if (opening < ProbeOpenMax)
                run.Integral += holdError * dt;

            run.Row.ActiveDirection = holdError > 0
                ? run.IncreasingDirection
                : 1 - run.IncreasingDirection;
            run.Row.ActiveOpening = opening;
            run.Row.SetStatus("保持中 " + run.PidText + "，误差 " +
                              holdError.ToString("+0.000;-0.000", CultureInfo.InvariantCulture) +
                              "，开度 " + opening, ValveStatusTone.Ok);
        }

        run.PreviousError = holdError;
        run.NextHoldAt = now.AddMilliseconds(HoldIntervalMs);
        return true;
    }

    private static bool CalculatePid(PidSession run)
    {
        if (run.Samples.Count < 2)
            return false;

        double startValue = run.Samples[0].Value;
        KeyValuePair<double, double> end = run.Samples[run.Samples.Count - 1];
        double delta = end.Value - startValue;
        if (Math.Abs(delta) < Tolerance)
            return false;

        double lag = run.Samples
            .FirstOrDefault(sample => Math.Abs(sample.Value - startValue) >= Tolerance).Key;
        if (lag <= 0)
            lag = end.Key;

        double gain = Math.Abs(delta) / Math.Max(end.Key - lag, ProbeIntervalMs / 1000.0) / run.Opening;
        double response = Math.Max(ResponseTimeSeconds, lag);
        if (gain <= 0 || double.IsNaN(gain) || double.IsInfinity(gain))
            return false;

        run.Kp = 1.0 / (gain * response);
        run.Ki = run.Kp / (4.0 * response);
        run.Kd = run.Kp * lag / 2.0;
        if (run.Kp > 8.0)
        {
            double scale = 8.0 / run.Kp;
            run.Kp *= scale;
            run.Ki *= scale;
            run.Kd *= scale;
        }

        run.PidText = string.Format(CultureInfo.InvariantCulture,
            "Kp={0:G3} Ki={1:G3} Kd={2:G3}", run.Kp, run.Ki, run.Kd);
        return true;
    }

    // ===================== 停止 / 失败处理 =====================

    private void StopRow(ValveRowVm row, string status)
    {
        bool pidRow = _pidRun is not null && _pidRun.Row == row;
        if (pidRow)
            _pidRun = null;

        row.ResetInputs(status, ValveStatusTone.Idle);
        RefreshRowEnableStates();
        SendRowZero(row);

        if (pidRow)
            Info("PID 保持已退出。");
    }

    /// <summary>全部停止并重复发布置零帧。</summary>
    public void StopAll(string reason)
    {
        if (_shuttingDown)
            return;

        _pidRun = null;
        foreach (var row in Rows)
            row.ResetInputs(reason, ValveStatusTone.Warn);

        RefreshRowEnableStates();
        SendAllZeros();
    }

    private void RefreshRowEnableStates()
    {
        bool pidLock = _pidRun is not null;

        foreach (var row in Rows)
        {
            if (_publisherFailed)
            {
                row.StartEnabled = false;
                row.StopEnabled = false;
                row.PidEnabled = false;
                row.InputsEnabled = false;
                continue;
            }

            bool editable = !pidLock && !row.IsRunning;
            row.StartEnabled = editable;
            row.PidEnabled = editable;
            row.InputsEnabled = editable;
            row.StopEnabled = row.IsRunning;
        }
    }

    private bool ReadyToCommand()
    {
        if (_publisherFailed || _shuttingDown || DateTime.UtcNow < _publishReadyAt)
        {
            ConnectionText = _publisherFailed
                ? "发布通道错误，已禁止控制"
                : "发布通道正在连接，请稍候…";
            return false;
        }

        return true;
    }

    private bool TryReadSensor(ValveRowVm row, out float value)
        => _sensors.TryGet(ArmIndex, row.Definition.SensorIndex,
            TimeSpan.FromSeconds(SensorTimeoutSeconds), out value);

    private static bool TryParseOptionalTarget(string text, out double? target)
    {
        text = (text ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            target = null;
            return true;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
            double.IsNaN(value) || double.IsInfinity(value))
        {
            target = null;
            return false;
        }

        target = value;
        return true;
    }

    private void SendRowZero(ValveRowVm row)
    {
        if (_publisher is null || _publisherFailed || _shuttingDown || DateTime.UtcNow < _publishReadyAt)
            return;

        try
        {
            for (int repeat = 0; repeat < ZeroRepeat; repeat++)
            {
                _publisher.Send(CommandPrefix + row.Definition.CommandA, 0);
                _publisher.Send(CommandPrefix + row.Definition.CommandB, 0);
            }
        }
        catch (Exception ex)
        {
            FailPublisher("ZeroMQ 置零失败：" + ex.Message);
        }
    }

    private void SendAllZeros()
    {
        if (_publisher is null || _publisherFailed || _shuttingDown || DateTime.UtcNow < _publishReadyAt)
            return;

        try
        {
            for (int repeat = 0; repeat < ZeroRepeat; repeat++)
            {
                foreach (var row in Rows)
                {
                    _publisher.Send(CommandPrefix + row.Definition.CommandA, 0);
                    _publisher.Send(CommandPrefix + row.Definition.CommandB, 0);
                }
            }
        }
        catch (Exception ex)
        {
            FailPublisher("ZeroMQ 急停发布失败：" + ex.Message);
        }
    }

    private void FailPublisher(string message)
    {
        _publisherFailed = true;
        _pidRun = null;
        _sendTimer.Stop();
        ConnectionOk = false;
        ConnectionText = message + "；请立即通过设备侧执行急停";

        foreach (var row in Rows)
        {
            row.ResetInputs("发布失败，请设备侧急停", ValveStatusTone.Bad);
        }

        RefreshRowEnableStates();
    }

    private void Warn(string message)
    {
        NoticeText = message;
        NoticeBrush = new SolidColorBrush(Color.Parse("#F5A623"));
    }

    private void Info(string message)
    {
        NoticeText = message;
        NoticeBrush = new SolidColorBrush(Color.Parse("#23A9F0"));
    }

    /// <summary>页面退出时停止发布并关闭通道。</summary>
    public void Shutdown()
    {
        if (_shuttingDown)
            return;

        StopAll("页面关闭");
        _shuttingDown = true;
        _sendTimer.Stop();
        _sensors.Dispose();
        _publisher?.Dispose();
    }
}
