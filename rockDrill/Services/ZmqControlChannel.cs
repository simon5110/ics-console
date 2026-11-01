using System;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;
using NetMQ;
using NetMQ.Sockets;

namespace rockDrill.Services;

/// <summary>
/// 控制指令发布通道（ZeroMQ PUB）：向控制端发送 "主题 + 开度" 两帧消息。
/// 主题格式为 <c>L_/R_ + 动作名</c>，例如 <c>L_Boom_Down</c>，载荷为 0–100 的开度字符串。
/// </summary>
public sealed class ZmqCommandPublisher : IDisposable
{
    /// <summary>控制端发布地址。</summary>
    public const string Address = "tcp://192.168.1.200:5555";

    private readonly PublisherSocket _socket;
    private bool _disposed;

    public ZmqCommandPublisher()
    {
        _socket = new PublisherSocket();
        _socket.Options.Linger = TimeSpan.FromMilliseconds(200);
        _socket.Connect(Address);
    }

    /// <summary>发布一条指令（两帧：主题 / 开度）。</summary>
    public void Send(string topic, int opening)
    {
        if (_disposed) return;
        _socket.SendMoreFrame(topic).SendFrame(opening.ToString(CultureInfo.InvariantCulture));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _socket.Dispose();
        }
        catch
        {
            // 退出阶段忽略关闭异常
        }
    }
}

/// <summary>
/// 传感器订阅通道（ZeroMQ SUB）：订阅 <c>sensor.</c> 主题，缓存左右臂各 8 路反馈值并记录更新时间。
/// 主题格式：<c>sensor.左臂直臂俯仰编码器</c> / <c>sensor.右臂凿岩偏摆编码器</c>。
/// </summary>
public sealed class ZmqSensorFeed : IDisposable
{
    /// <summary>传感器订阅地址。</summary>
    public const string Address = "tcp://192.168.1.200:5556";

    private const int ArmCount = 2;
    private const int ChannelCount = 8;

    private static readonly string[] SensorNames =
    {
        "直臂偏摆编码器",
        "直臂俯仰编码器",
        "直臂伸缩拉绳传感器",
        "凿岩翻转编码器",
        "凿岩偏摆编码器",
        "凿岩俯仰编码器",
        "凿岩补偿拉绳传感器",
        "凿岩推进编码器"
    };

    private readonly float[] _values = new float[ArmCount * ChannelCount];
    private readonly DateTime[] _updatedAt = new DateTime[ArmCount * ChannelCount];
    private readonly object _lock = new();
    private readonly CancellationTokenSource _cts = new();

    private Thread? _thread;
    private long _receivedCount;
    private DateTime _lastReceiveAt;
    private string _lastTopic = string.Empty;
    private string? _lastError;

    /// <summary>已接收帧数。</summary>
    public long ReceivedCount
    {
        get
        {
            lock (_lock) return _receivedCount;
        }
    }

    /// <summary>最近一次收到数据的时间。</summary>
    public DateTime LastReceiveAt
    {
        get
        {
            lock (_lock) return _lastReceiveAt;
        }
    }

    /// <summary>最近一次收到数据的主题。</summary>
    public string LastTopic
    {
        get
        {
            lock (_lock) return _lastTopic;
        }
    }

    /// <summary>最近一次错误（成功接收后自动清空）。</summary>
    public string? LastError
    {
        get
        {
            lock (_lock) return _lastError;
        }
    }

    /// <summary>启动后台订阅线程。</summary>
    public void Start()
    {
        if (_thread != null) return;
        _thread = new Thread(Loop)
        {
            IsBackground = true,
            Name = "zmq-sensor-subscriber"
        };
        _thread.Start();
    }

    /// <summary>读取指定臂 / 通道的读数，超过 <paramref name="maxAge"/> 未更新视为无效。</summary>
    public bool TryGet(int armIndex, int channelIndex, TimeSpan maxAge, out float value)
    {
        lock (_lock)
        {
            value = 0;
            if (armIndex < 0 || armIndex >= ArmCount || channelIndex < 0 || channelIndex >= ChannelCount)
                return false;

            int index = armIndex * ChannelCount + channelIndex;
            DateTime updatedAt = _updatedAt[index];
            if (updatedAt == default || DateTime.UtcNow - updatedAt > maxAge)
                return false;

            value = _values[index];
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    private void Loop()
    {
        try
        {
            using var subscriber = new SubscriberSocket();
            subscriber.Options.Linger = TimeSpan.Zero;
            subscriber.Connect(Address);
            subscriber.Subscribe("sensor.");

            while (!_cts.IsCancellationRequested)
            {
                var message = new NetMQMessage();
                if (!subscriber.TryReceiveMultipartMessage(TimeSpan.FromMilliseconds(100), ref message, 2))
                    continue;

                if (message.FrameCount != 2)
                    continue;

                string topic = message[0].ConvertToString(Encoding.UTF8);
                string payload = message[1].ConvertToString(Encoding.UTF8);

                if (!TryMapTopic(topic, out int armIndex, out int channelIndex))
                {
                    lock (_lock)
                        _lastError = "收到未识别的传感器主题：" + topic;
                    continue;
                }

                if (!TryParseValue(payload, out float value))
                {
                    lock (_lock)
                        _lastError = "传感器数据格式无效：" + topic + " " + payload;
                    continue;
                }

                lock (_lock)
                {
                    int index = armIndex * ChannelCount + channelIndex;
                    _values[index] = value;
                    _updatedAt[index] = DateTime.UtcNow;
                    _receivedCount++;
                    _lastReceiveAt = _updatedAt[index];
                    _lastTopic = topic;
                    _lastError = null;
                }
            }
        }
        catch (Exception ex)
        {
            lock (_lock)
                _lastError = "ZMQ 订阅失败：" + ex.Message;
        }
    }

    private static bool TryMapTopic(string topic, out int armIndex, out int channelIndex)
    {
        armIndex = -1;
        channelIndex = -1;

        for (int arm = 0; arm < ArmCount; arm++)
        {
            string prefix = arm == 0 ? "sensor.左臂" : "sensor.右臂";
            if (!topic.StartsWith(prefix, StringComparison.Ordinal))
                continue;

            string sensorName = topic.Substring(prefix.Length);
            channelIndex = Array.IndexOf(SensorNames, sensorName);
            if (channelIndex < 0)
                return false;

            armIndex = arm;
            return true;
        }

        return false;
    }

    private static bool TryParseValue(string payload, out float value)
    {
        if (float.TryParse(payload, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
            !float.IsNaN(value) && !float.IsInfinity(value))
            return true;

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("parsed", out var parsed) &&
                parsed.ValueKind == JsonValueKind.Number)
            {
                value = parsed.GetSingle();
                return !float.IsNaN(value) && !float.IsInfinity(value);
            }
        }
        catch (JsonException)
        {
            // 忽略格式错误，交由调用方上报
        }

        value = 0;
        return false;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try
        {
            _thread?.Join(TimeSpan.FromMilliseconds(500));
        }
        catch
        {
            // 退出阶段忽略
        }

        _cts.Dispose();
    }
}
