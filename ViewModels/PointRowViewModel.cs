using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace UpperMachine.ViewModels;

public sealed class PointRowViewModel : INotifyPropertyChanged
{
    private double _voltage = double.NaN;
    private double _channel1Voltage = double.NaN;
    private double _channel2Voltage = double.NaN;
    private double _channel3Voltage = double.NaN;
    private double _channel4Voltage = double.NaN;

    public int Order { get; init; }

    public double X { get; init; }

    public double Y { get; init; }

    public double Voltage
    {
        get => _voltage;
        set
        {
            if (Math.Abs(_voltage - value) < 0.000001)
            {
                return;
            }

            _voltage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VoltageDisplay));
            OnPropertyChanged(nameof(HasVoltage));
        }
    }

    public bool HasVoltage => !double.IsNaN(Voltage) && Voltage >= 0;

    public string VoltageDisplay => HasVoltage ? Voltage.ToString("F4") : "--";

    public double Channel1Voltage
    {
        get => _channel1Voltage;
        set => SetChannelVoltage(ref _channel1Voltage, value, nameof(Channel1Voltage), nameof(Channel1VoltageDisplay));
    }

    public double Channel2Voltage
    {
        get => _channel2Voltage;
        set => SetChannelVoltage(ref _channel2Voltage, value, nameof(Channel2Voltage), nameof(Channel2VoltageDisplay));
    }

    public double Channel3Voltage
    {
        get => _channel3Voltage;
        set => SetChannelVoltage(ref _channel3Voltage, value, nameof(Channel3Voltage), nameof(Channel3VoltageDisplay));
    }

    public double Channel4Voltage
    {
        get => _channel4Voltage;
        set => SetChannelVoltage(ref _channel4Voltage, value, nameof(Channel4Voltage), nameof(Channel4VoltageDisplay));
    }

    public string Channel1VoltageDisplay => FormatChannelVoltage(Channel1Voltage);

    public string Channel2VoltageDisplay => FormatChannelVoltage(Channel2Voltage);

    public string Channel3VoltageDisplay => FormatChannelVoltage(Channel3Voltage);

    public string Channel4VoltageDisplay => FormatChannelVoltage(Channel4Voltage);

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetChannelVoltage(ref double field, double value, string propertyName, string displayName)
    {
        if (Math.Abs(field - value) < 0.000001)
        {
            return;
        }

        field = value;
        OnPropertyChanged(propertyName);
        OnPropertyChanged(displayName);
    }

    private static string FormatChannelVoltage(double value)
    {
        return !double.IsNaN(value) && value >= 0 ? value.ToString("F4") : "--";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
