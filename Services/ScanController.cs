using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Ports;
using System.Text;
using UpperMachine.Models;

namespace UpperMachine.Services;

public sealed class ScanController : IDisposable
{
    private sealed class SensorMeasurement
    {
        private readonly double[] _channels =
        {
            double.NaN,
            double.NaN,
            double.NaN,
            double.NaN,
        };

        public double Channel1 => _channels[0];

        public double Channel2 => _channels[1];

        public double Channel3 => _channels[2];

        public double Channel4 => _channels[3];

        public bool HasAnyChannel =>
            IsValidVoltage(Channel1) ||
            IsValidVoltage(Channel2) ||
            IsValidVoltage(Channel3) ||
            IsValidVoltage(Channel4);

        public void SetChannel(int channelNumber, double voltage)
        {
            if (channelNumber < 1 || channelNumber > 4)
            {
                return;
            }

            _channels[channelNumber - 1] = voltage;
        }
    }

    private enum GrblState
    {
        Run,
        Idle,
        Alarm,
        Hold,
        Jog,
        Door,
        Check,
        Home,
        Sleep,
        Unknown,
    }

    private enum CompleteState
    {
        No,
        Yes,
    }

    private enum WorkState
    {
        Wait,
        Idle,
        Scan,
        Run,
        WorkFinished,
        AllFinished,
        Exit,
    }

    private sealed class ScanSettings
    {
        public int NextPosition { get; set; }

        public int TotalPoints { get; set; }

        public int XPoints { get; set; }

        public int YPoints { get; set; }

        public int Columns => XPoints + 1;

        public int Rows => YPoints + 1;
    }

    private readonly SerialLineBuffer _controlBuffer = new();
    private readonly SerialLineBuffer _dataBuffer = new();
    private readonly object _portLock = new();

    private CancellationTokenSource? _cts;
    private SerialPort? _controlPort;
    private SerialPort? _dataPort;
    private PointData[] _dataArray = Array.Empty<PointData>();
    private ScanParameter? _scanParameter;
    private ScanSettings? _scanSettings;
    private CompleteState _scanCompleteState;
    private WorkState _workState;
    private bool _disposed;

    private const double VoltageUpperLimit = 10.0;
    private const double VoltageLowerLimit = 0.0;
    private const int MultiMeasureTimes = 3;
    private const int ScanRetryTimes = 2;

    public ScanController()
    {
        _controlBuffer.LineReceived += line => Log($"[控制 RX] {line}");
        _dataBuffer.LineReceived += line => Log($"[传感 RX] {line}");
    }

    public bool IsScanRunning { get; private set; }

    public bool HasResumePoint =>
        _scanSettings is not null &&
        _scanSettings.NextPosition > 0 &&
        _scanSettings.NextPosition < _scanSettings.TotalPoints &&
        _dataArray.Length == _scanSettings.TotalPoints;

    public bool IsControlReady()
    {
        lock (_portLock)
        {
            return _controlPort?.IsOpen == true;
        }
    }

    public string HoldCommand { get; set; } = "$J=G90 Z0.0 F500";

    public int HoldIntervalMs { get; set; } = 100;

    public string DropCommand { get; set; } = "$J=G91Z0F1500";

    public string RaiseCommand { get; set; } = "$J=G90Z-10F1500";

    public string FilterAlgorithm { get; set; } = ScanParameter.AverageFilter;

    public IReadOnlyList<PointData> DataArray => _dataArray;

    public event EventHandler<string>? LogReceived;

    public event EventHandler<string>? StateChanged;

    public event EventHandler<PathPreparedEventArgs>? PathPrepared;

    public event EventHandler<PointScannedEventArgs>? PointScanned;

    public event EventHandler<ScanCompletedEventArgs>? ScanCompleted;

    public void AttachPorts(SerialPort controlPort, SerialPort dataPort)
    {
        lock (_portLock)
        {
            DetachPortEvents();

            _controlPort = controlPort;
            _dataPort = dataPort;

            _controlPort.DataReceived += OnControlPortDataReceived;
            _dataPort.DataReceived += OnDataPortDataReceived;
        }
    }

    public void DetachPorts()
    {
        lock (_portLock)
        {
            DetachPortEvents();
            _controlPort = null;
            _dataPort = null;
        }
    }

    public void SendControlCommand(string command)
    {
        SerialPort port = EnsureControlPort();
        SendCode(port, command, "控制");
    }

    public void SendDataCommand(string command)
    {
        SerialPort port = EnsureDataPort();
        SendCode(port, command, "传感");
    }

    public void StartScan(ScanParameter scanParameter)
    {
        if (IsScanRunning)
        {
            throw new InvalidOperationException("扫描任务已在运行。");
        }

        SerialPort controlPort = EnsureControlPort();
        SerialPort dataPort = EnsureDataPort();

        _scanParameter = scanParameter;
        CreatePath(scanParameter);

        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        IsScanRunning = true;
        SetState("准备扫描");

        Task.Run(async () =>
        {
            bool cancelled = false;
            string message = "扫描完成";

            try
            {
                await ExecuteAsync(controlPort, dataPort, scanParameter, token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                message = "扫描已停止";
            }
            catch (Exception ex)
            {
                message = $"扫描异常: {ex.Message}";
                Log($"[异常] {ex}");
            }
            finally
            {
                IsScanRunning = false;
                SetState(cancelled ? "已停止" : "空闲");
                ScanCompleted?.Invoke(this, new ScanCompletedEventArgs(cancelled, message));
            }
        });
    }

    public void ResumeScan()
    {
        if (IsScanRunning)
        {
            throw new InvalidOperationException("扫描任务正在运行中。");
        }

        if (_scanParameter is null || _scanSettings is null || _dataArray.Length == 0)
        {
            throw new InvalidOperationException("当前没有可继续的扫描任务。");
        }

        if (!HasResumePoint)
        {
            throw new InvalidOperationException("当前没有可继续的断点进度。");
        }

        SerialPort controlPort = EnsureControlPort();
        SerialPort dataPort = EnsureDataPort();

        _cts = new CancellationTokenSource();
        CancellationToken token = _cts.Token;
        IsScanRunning = true;
        SetState("继续扫描中");
        Log($"[扫描] 从第 {_scanSettings.NextPosition + 1} 个点继续。");

        Task.Run(async () =>
        {
            bool cancelled = false;
            string message = "扫描完成";

            try
            {
                await ExecuteAsync(controlPort, dataPort, _scanParameter, token);
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
                message = "扫描已暂停。";
            }
            catch (Exception ex)
            {
                message = $"扫描异常: {ex.Message}";
                Log($"[异常] {ex}");
            }
            finally
            {
                IsScanRunning = false;
                SetState(cancelled ? "已暂停" : "已就绪");
                ScanCompleted?.Invoke(this, new ScanCompletedEventArgs(cancelled, message));
            }
        });
    }

    public void StopScan()
    {
        _workState = WorkState.Exit;

        try
        {
            _cts?.Cancel();
        }
        catch
        {
        }
    }

    public string SaveSimplePointData(string? rootDirectory = null)
    {
        if (_scanSettings is null || _dataArray.Length == 0)
        {
            throw new InvalidOperationException("当前没有可保存的数据。");
        }

        string saveDir = Path.Combine(rootDirectory ?? AppContext.BaseDirectory, "ScanData");
        Directory.CreateDirectory(saveDir);
        string fileName = $"ScanData_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        string fullPath = Path.Combine(saveDir, fileName);

        using StreamWriter writer = new(fullPath, false, Encoding.UTF8);
        writer.WriteLine("Order,X,Y,Voltage");

        foreach (PointData point in _dataArray)
        {
            writer.WriteLine(
                $"{point.Order}," +
                $"{point.X.ToString("F2", CultureInfo.InvariantCulture)}," +
                $"{point.Y.ToString("F2", CultureInfo.InvariantCulture)}," +
                $"{point.Voltage.ToString("F4", CultureInfo.InvariantCulture)}");
        }

        Log($"[保存] {fullPath}");
        return fullPath;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopScan();
        DetachPorts();
        _cts?.Dispose();
    }

    private void DetachPortEvents()
    {
        if (_controlPort is not null)
        {
            _controlPort.DataReceived -= OnControlPortDataReceived;
        }

        if (_dataPort is not null)
        {
            _dataPort.DataReceived -= OnDataPortDataReceived;
        }
    }

    private SerialPort EnsureControlPort()
    {
        if (_controlPort is null || !_controlPort.IsOpen)
        {
            throw new InvalidOperationException("控制串口未连接。");
        }

        return _controlPort;
    }

    private SerialPort EnsureDataPort()
    {
        if (_dataPort is null || !_dataPort.IsOpen)
        {
            throw new InvalidOperationException("传感串口未连接。");
        }

        return _dataPort;
    }

    private void OnControlPortDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        HandlePortDataReceived(sender, _controlBuffer);
    }

    private void OnDataPortDataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        HandlePortDataReceived(sender, _dataBuffer);
    }

    private void HandlePortDataReceived(object sender, SerialLineBuffer buffer)
    {
        if (sender is not SerialPort port || !port.IsOpen)
        {
            return;
        }

        try
        {
            int bytesToRead = port.BytesToRead;
            if (bytesToRead <= 0)
            {
                return;
            }

            byte[] data = new byte[bytesToRead];
            int read = port.Read(data, 0, bytesToRead);
            if (read <= 0)
            {
                return;
            }

            string received = port.Encoding.GetString(data, 0, read);
            buffer.Append(received);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"串口接收异常: {ex.Message}");
        }
    }

    private void SendCode(SerialPort port, string code, string channelName)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }

        if (!port.IsOpen)
        {
            throw new InvalidOperationException("串口已断开，已保留扫描进度，请重新连接后点击“继续扫描”。");
        }

        byte[] data = port.Encoding.GetBytes(code + "\r\n");
        port.Write(data, 0, data.Length);
        Log($"[{channelName} TX] {code}");
    }

    private void CreatePath(ScanParameter scanParameter)
    {
        int xPoints = (int)(scanParameter.MaxLimitX / scanParameter.MinStep);
        int yPoints = (int)(scanParameter.MaxLimitY / scanParameter.MinStep);
        long theoreticalTotal = ((long)xPoints + 1L) * ((long)yPoints + 1L);

        if (theoreticalTotal > int.MaxValue)
        {
            throw new InvalidOperationException("点位数量过大，请减小范围或增大步长。");
        }

        bool isCircle = scanParameter.Shape.Contains("圆", StringComparison.OrdinalIgnoreCase) ||
                        scanParameter.Shape.Contains("Circle", StringComparison.OrdinalIgnoreCase);
        double minX = isCircle ? -scanParameter.MaxLimitX / 2.0 : 0;
        double minY = isCircle ? -scanParameter.MaxLimitY / 2.0 : 0;
        double centerX = isCircle ? 0 : scanParameter.MaxLimitX / 2.0;
        double centerY = isCircle ? 0 : scanParameter.MaxLimitY / 2.0;
        double radiusSquared = scanParameter.Radius * scanParameter.Radius;

        PointData[] tempArray = new PointData[theoreticalTotal];
        int index = 0;

        for (int y = 0; y <= yPoints; y++)
        {
            double currentY = minY + scanParameter.MinStep * y;
            IEnumerable<int> xRange = y % 2 == 0
                ? Enumerable.Range(0, xPoints + 1)
                : Enumerable.Range(0, xPoints + 1).Reverse();

            foreach (int x in xRange)
            {
                double currentX = minX + scanParameter.MinStep * x;
                bool shouldAdd = true;

                if (isCircle)
                {
                    double dx = currentX - centerX;
                    double dy = currentY - centerY;
                    shouldAdd = dx * dx + dy * dy <= radiusSquared;
                }

                if (!shouldAdd)
                {
                    continue;
                }

                tempArray[index] = new PointData
                {
                    Order = index,
                    X = currentX,
                    Y = currentY,
                    Voltage = double.NaN,
                };
                index++;
            }
        }

        if (index < tempArray.Length)
        {
            Array.Resize(ref tempArray, index);
        }

        _dataArray = tempArray;
        _scanSettings = new ScanSettings
        {
            NextPosition = 0,
            TotalPoints = _dataArray.Length,
            XPoints = xPoints,
            YPoints = yPoints,
        };

        PathPrepared?.Invoke(
            this,
            new PathPreparedEventArgs(
                _dataArray.ToArray(),
                new ScanPlanInfo
                {
                    TotalPoints = _dataArray.Length,
                    Columns = xPoints + 1,
                    Rows = yPoints + 1,
                    Step = scanParameter.MinStep,
                    MinX = minX,
                    MinY = minY,
                    MaxX = scanParameter.MaxLimitX,
                    MaxY = scanParameter.MaxLimitY,
                }));

        Log($"[路径] 已生成 {_dataArray.Length} 个扫描点");
    }

    private async Task<GrblState> IsRunState(CancellationToken ct)
    {
        _controlBuffer.ClearPendingLines();
        SendCode(EnsureControlPort(), "?", "控制");

        string[] response = await WaitForLinesAsync(_controlBuffer, 250, ct);
        if (response.Length == 0)
        {
            return GrblState.Unknown;
        }

        foreach (string line in response)
        {
            if (line.Contains("Run", StringComparison.OrdinalIgnoreCase))
            {
                return GrblState.Run;
            }

            if (line.Contains("Idle", StringComparison.OrdinalIgnoreCase))
            {
                return GrblState.Idle;
            }

            if (line.Contains("Alarm", StringComparison.OrdinalIgnoreCase))
            {
                return GrblState.Alarm;
            }

            if (line.Contains("Hold", StringComparison.OrdinalIgnoreCase))
            {
                return GrblState.Hold;
            }

            if (line.Contains("Jog", StringComparison.OrdinalIgnoreCase))
            {
                return GrblState.Jog;
            }

            if (line.Contains("Door", StringComparison.OrdinalIgnoreCase))
            {
                return GrblState.Door;
            }

            if (line.Contains("Check", StringComparison.OrdinalIgnoreCase))
            {
                return GrblState.Check;
            }

            if (line.Contains("Home", StringComparison.OrdinalIgnoreCase))
            {
                return GrblState.Home;
            }

            if (line.Contains("Sleep", StringComparison.OrdinalIgnoreCase))
            {
                return GrblState.Sleep;
            }
        }

        return GrblState.Unknown;
    }

    private async Task<string[]> WaitForLinesAsync(SerialLineBuffer buffer, int timeoutMs, CancellationToken ct)
    {
        int elapsed = 0;
        while (elapsed < timeoutMs)
        {
            string[] lines = buffer.DrainAvailableLines();
            if (lines.Length > 0)
            {
                return lines;
            }

            await Task.Delay(10, ct);
            elapsed += 10;
        }

        return Array.Empty<string>();
    }

    private async Task MoveToNextPoint(ScanSettings settings, SerialPort controlPort, ScanParameter scanParameter, CancellationToken ct)
    {
        if (settings.NextPosition >= settings.TotalPoints)
        {
            return;
        }

        if (settings.NextPosition > 0 && !string.IsNullOrWhiteSpace(RaiseCommand))
        {
            SendCode(controlPort, RaiseCommand, "控制");
            await Task.Delay(280, ct);
        }

        PointData point = _dataArray[settings.NextPosition];
        string command =
            $"$J=G90 X{point.X.ToString(CultureInfo.InvariantCulture)} " +
            $"Y{point.Y.ToString(CultureInfo.InvariantCulture)} " +
            $"F{scanParameter.Speed.ToString(CultureInfo.InvariantCulture)}";
        SendCode(controlPort, command, "控制");
        await Task.Delay(800, ct);
    }

    private async Task Scan(int presentPosition, SerialPort controlPort, SerialPort dataPort, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(DropCommand))
        {
            SendCode(controlPort, DropCommand, "Control");
            await Task.Delay(280, ct);
        }

        double finalVoltage = -1.0;
        bool isDataValid = false;
        int retryCount = 0;

        while (!isDataValid && retryCount < ScanRetryTimes)
        {
            try
            {
                List<double> filteredVoltages = new();
                List<double> channel1Voltages = new();
                List<double> channel2Voltages = new();
                List<double> channel3Voltages = new();
                List<double> channel4Voltages = new();

                for (int i = 0; i < MultiMeasureTimes; i++)
                {
                    SensorMeasurement measurement = await SingleMeasure(dataPort, ct);
                    CollectVoltage(channel1Voltages, measurement.Channel1);
                    CollectVoltage(channel2Voltages, measurement.Channel2);
                    CollectVoltage(channel3Voltages, measurement.Channel3);
                    CollectVoltage(channel4Voltages, measurement.Channel4);
                    CollectVoltage(filteredVoltages, measurement.Channel1);

                    if (string.Equals(_scanParameter?.Channel2Mode, ScanParameter.ChannelModeAuxFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        CollectVoltage(filteredVoltages, measurement.Channel2);
                    }

                    if (string.Equals(_scanParameter?.Channel3Mode, ScanParameter.ChannelModeAuxFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        CollectVoltage(filteredVoltages, measurement.Channel3);
                    }

                    if (string.Equals(_scanParameter?.Channel4Mode, ScanParameter.ChannelModeAuxFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        CollectVoltage(filteredVoltages, measurement.Channel4);
                    }

                    await Task.Delay(30, ct);
                }

                if (filteredVoltages.Count > 0)
                {
                    finalVoltage = ApplyFilter(filteredVoltages);
                    isDataValid = true;

                    PointData point = _dataArray[presentPosition];
                    point.Channel1Voltage = AverageOrNaN(channel1Voltages);
                    point.Channel2Voltage = AverageOrNaN(channel2Voltages);
                    point.Channel3Voltage = AverageOrNaN(channel3Voltages);
                    point.Channel4Voltage = AverageOrNaN(channel4Voltages);
                    point.Voltage = finalVoltage;
                    _dataArray[presentPosition] = point;
                }
                else
                {
                    retryCount++;
                    await Task.Delay(20, ct);
                }
            }
            catch (Exception ex)
            {
                Log($"[Measure] point {presentPosition}: {ex.Message}");
                retryCount++;
                await Task.Delay(20, ct);
            }
        }

        _scanCompleteState = isDataValid ? CompleteState.Yes : CompleteState.No;
    }

    private async Task<SensorMeasurement> SingleMeasure(SerialPort dataPort, CancellationToken ct)
    {
        _dataBuffer.ClearPendingLines();
        SendCode(dataPort, "AT+AIREAD", "Sensor");

        string[] response = await WaitForLinesAsync(_dataBuffer, 180, ct);
        return ParseMeasurement(response);
    }

    private double ApplyFilter(IReadOnlyList<double> voltages)
    {
        if (voltages.Count == 0)
        {
            return -1.0;
        }

        if (string.Equals(FilterAlgorithm, ScanParameter.MedianFilter, StringComparison.OrdinalIgnoreCase))
        {
            List<double> sorted = voltages.OrderBy(v => v).ToList();
            int middle = sorted.Count / 2;
            return sorted.Count % 2 == 0
                ? (sorted[middle - 1] + sorted[middle]) / 2.0
                : sorted[middle];
        }

        if (string.Equals(FilterAlgorithm, ScanParameter.TrimmedMeanFilter, StringComparison.OrdinalIgnoreCase))
        {
            if (voltages.Count >= 5)
            {
                List<double> sorted = voltages.OrderBy(v => v).ToList();
                return sorted.Skip(1).Take(sorted.Count - 2).Average();
            }

            return voltages.Average();
        }

        return voltages.Average();
    }

    public double ParseVoltageData(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return -1.0;
        }

        try
        {
            string[] parts = input.Split(',');
            if (parts.Length >= 2 &&
                double.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double milliVolt))
            {
                double voltage = milliVolt / 1000.0;
                return IsValidVoltage(voltage) ? voltage : -1.0;
            }

            if (parts.Length >= 2 &&
                double.TryParse(parts[1].Trim(), out milliVolt))
            {
                double voltage = milliVolt / 1000.0;
                return IsValidVoltage(voltage) ? voltage : -1.0;
            }
        }
        catch
        {
        }

        return -1.0;
    }

    private SensorMeasurement ParseMeasurement(IEnumerable<string> response)
    {
        SensorMeasurement measurement = new();

        foreach (string line in response)
        {
            string cleanLine = line.Trim();
            if (!cleanLine.StartsWith("ch", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int colonIndex = cleanLine.IndexOf(':');
            if (colonIndex <= 2)
            {
                continue;
            }

            string channelText = cleanLine.Substring(2, colonIndex - 2);
            if (!int.TryParse(channelText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int channelNumber))
            {
                continue;
            }

            double voltage = ParseVoltageData(cleanLine.Substring(colonIndex + 1));
            measurement.SetChannel(channelNumber, voltage);
        }

        return measurement;
    }

    private static void CollectVoltage(ICollection<double> target, double voltage)
    {
        if (IsValidVoltage(voltage))
        {
            target.Add(voltage);
        }
    }

    private static bool IsValidVoltage(double voltage)
    {
        return !double.IsNaN(voltage) && voltage >= VoltageLowerLimit && voltage <= VoltageUpperLimit;
    }

    private static double AverageOrNaN(IReadOnlyList<double> voltages)
    {
        return voltages.Count > 0 ? voltages.Average() : double.NaN;
    }

    private async Task ExecuteAsync(
        SerialPort controlPort,
        SerialPort dataPort,
        ScanParameter scanParameter,
        CancellationToken ct)
    {
        if (_scanSettings is null)
        {
            throw new InvalidOperationException("扫描路径尚未准备。");
        }

        SendCode(controlPort, "G92 X0 Y0 Z0", "控制");
        _workState = WorkState.Run;
        _scanCompleteState = CompleteState.No;
        SetState("扫描中");

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            if (_scanSettings.NextPosition == 0)
            {
                _workState = WorkState.Scan;
            }

            switch (_workState)
            {
                case WorkState.Wait:
                    int delayTime = _scanSettings.NextPosition % Math.Max(1, _scanSettings.Columns) == 1
                        ? (int)(scanParameter.MaxLimitX / scanParameter.Speed * 1000) + 200
                        : (int)(scanParameter.MinStep / scanParameter.Speed * 1000);

                    await Task.Delay(Math.Max(delayTime, 60), ct);

                    GrblState state = await IsRunState(ct);
                    if (state != GrblState.Idle)
                    {
                        await Task.Delay(25, ct);
                    }
                    else
                    {
                        await Task.Delay(200, ct);
                        _workState = WorkState.Scan;
                    }
                    break;

                case WorkState.Idle:
                    await Task.Delay(50, ct);
                    break;

                case WorkState.Run:
                    await MoveToNextPoint(_scanSettings, controlPort, scanParameter, ct);
                    _workState = WorkState.Wait;
                    break;

                case WorkState.Scan:
                    await Task.Delay(250, ct);
                    if (_scanCompleteState == CompleteState.No)
                    {
                        int presentPosition = _scanSettings.NextPosition;
                        await Scan(presentPosition, controlPort, dataPort, ct);
                    }
                    else
                    {
                        _scanCompleteState = CompleteState.No;
                        _scanSettings.NextPosition++;
                        int completedIndex = _scanSettings.NextPosition - 1;
                        PointScanned?.Invoke(this, new PointScannedEventArgs(completedIndex, _dataArray[completedIndex]));
                        _workState = WorkState.Run;
                    }

                    if (_scanSettings.NextPosition >= _scanSettings.TotalPoints)
                    {
                        _workState = WorkState.AllFinished;
                    }
                    break;

                case WorkState.AllFinished:
                    _scanSettings.NextPosition = 0;
                    Log("[扫描] 数据扫描完成");
                    return;

                case WorkState.Exit:
                    Log("[扫描] 已暂停，当前进度已保留。");
                    ct.ThrowIfCancellationRequested();
                    return;

                case WorkState.WorkFinished:
                    _workState = WorkState.AllFinished;
                    break;
            }
        }
    }

    
    public static IEnumerable<PointData> BuildAuxiliaryPoints(IEnumerable<PointData> points, ScanParameter parameter)
    {
        foreach (PointData point in points)
        {
            yield return point;

            if (string.Equals(parameter.Channel2Mode, ScanParameter.ChannelModeAuxScan, StringComparison.OrdinalIgnoreCase) && IsValidVoltage(point.Channel2Voltage))
            {
                yield return CreateAuxiliaryPoint(
                    point,
                    parameter.Channel2OffsetX,
                    parameter.Channel2OffsetY,
                    point.Channel2Voltage,
                    1002);
            }

            if (string.Equals(parameter.Channel3Mode, ScanParameter.ChannelModeAuxScan, StringComparison.OrdinalIgnoreCase) && IsValidVoltage(point.Channel3Voltage))
            {
                yield return CreateAuxiliaryPoint(
                    point,
                    parameter.Channel3OffsetX,
                    parameter.Channel3OffsetY,
                    point.Channel3Voltage,
                    2003);
            }

            if (string.Equals(parameter.Channel4Mode, ScanParameter.ChannelModeAuxScan, StringComparison.OrdinalIgnoreCase) && IsValidVoltage(point.Channel4Voltage))
            {
                yield return CreateAuxiliaryPoint(
                    point,
                    parameter.Channel4OffsetX,
                    parameter.Channel4OffsetY,
                    point.Channel4Voltage,
                    3004);
            }
        }
    }

    private static PointData CreateAuxiliaryPoint(PointData point, double offsetX, double offsetY, double voltage, int orderOffset)
    {
        return new PointData
        {
            Order = point.Order + orderOffset,
            X = point.X + offsetX,
            Y = point.Y + offsetY,
            Voltage = voltage,
            Channel1Voltage = point.Channel1Voltage,
            Channel2Voltage = point.Channel2Voltage,
            Channel3Voltage = point.Channel3Voltage,
            Channel4Voltage = point.Channel4Voltage,
        };
    }private void Log(string message)
    {
        LogReceived?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] {message}");
    }

    private void SetState(string state)
    {
        StateChanged?.Invoke(this, state);
    }
}

public sealed class PathPreparedEventArgs : EventArgs
{
    public PathPreparedEventArgs(IReadOnlyList<PointData> points, ScanPlanInfo plan)
    {
        Points = points;
        Plan = plan;
    }

    public IReadOnlyList<PointData> Points { get; }

    public ScanPlanInfo Plan { get; }
}

public sealed class PointScannedEventArgs : EventArgs
{
    public PointScannedEventArgs(int index, PointData point)
    {
        Index = index;
        Point = point;
    }

    public int Index { get; }

    public PointData Point { get; }
}

public sealed class ScanCompletedEventArgs : EventArgs
{
    public ScanCompletedEventArgs(bool cancelled, string message)
    {
        Cancelled = cancelled;
        Message = message;
    }

    public bool Cancelled { get; }

    public string Message { get; }
}
