namespace UpperMachine.Models;

public sealed class ScanParameter
{
    public const string AverageFilter = "平均值";

    public const string MedianFilter = "中值";

    public const string TrimmedMeanFilter = "去极值平均";

    public const string ChannelModeOff = "Off";

    public const string ChannelModeAuxScan = "AuxScan";

    public const string ChannelModeAuxFilter = "AuxFilter";

    public double MaxLimitX { get; set; }

    public double MaxLimitY { get; set; }

    public int StepX { get; set; }

    public int StepY { get; set; }

    public double Speed { get; set; }

    public string Shape { get; set; } = "Rectangle";

    public double Radius { get; set; }

    public string FilterAlgorithm { get; set; } = AverageFilter;

    public string Channel2Mode { get; set; } = ChannelModeOff;

    public string Channel3Mode { get; set; } = ChannelModeOff;

    public string Channel4Mode { get; set; } = ChannelModeOff;

    public double Channel2OffsetX { get; set; } = 1.0;

    public double Channel2OffsetY { get; set; } = 0.0;

    public double Channel3OffsetX { get; set; } = 2.0;

    public double Channel3OffsetY { get; set; } = 0.0;

    public double Channel4OffsetX { get; set; } = 3.0;

    public double Channel4OffsetY { get; set; } = 0.0;
}
