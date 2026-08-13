namespace UpperMachine.Models;

public sealed class ScanPlanInfo
{
    public required int TotalPoints { get; init; }

    public required int Columns { get; init; }

    public required int Rows { get; init; }

    public required int Step { get; init; }

    public required double MinX { get; init; }

    public required double MinY { get; init; }

    public required double MaxX { get; init; }

    public required double MaxY { get; init; }
}
