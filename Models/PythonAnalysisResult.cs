namespace UpperMachine.Models;

/// <summary>
/// analysis_engine.py 输出的 JSON 结果，供数据处理页交互式渲染。
/// 网格中的缺失/无效值在 JSON 里为 null，映射到 double?；渲染时再转回 double.NaN。
/// </summary>
public sealed class PythonAnalysisResult
{
    public string? Algo { get; set; }
    public AnalysisCounts? Counts { get; set; }
    public List<AnalysisPoint>? Points { get; set; }
    public AnalysisGrid? Grid { get; set; }
    public AnalysisElectricField? ElectricField { get; set; }
    public AnalysisLaplacian? Laplacian { get; set; }
    public double? VoltageMean { get; set; }
    public AnalysisPinn? Pinn { get; set; }
}

public sealed class AnalysisCounts
{
    public int Normal { get; set; }
    public int Electrode { get; set; }
    public int Anomaly { get; set; }
}

public sealed class AnalysisPoint
{
    public double X { get; set; }
    public double Y { get; set; }
    public double V { get; set; }
    public string? Category { get; set; }
}

public sealed class AnalysisGrid
{
    public double[]? X { get; set; }
    public double[]? Y { get; set; }
    public double?[][]? Cleaned { get; set; }
    public double?[][]? Laplace { get; set; }
}

public sealed class AnalysisElectricField
{
    public double[]? X { get; set; }
    public double[]? Y { get; set; }
    public double[]? Ex { get; set; }
    public double[]? Ey { get; set; }
}

public sealed class AnalysisLaplacian
{
    public double[]? Values { get; set; }
    public double? Mean { get; set; }
    public double? Std { get; set; }
    public double? Min { get; set; }
    public double? Max { get; set; }
    public int Samples { get; set; }
}

public sealed class AnalysisPinn
{
    public double[]? X { get; set; }
    public double[]? Y { get; set; }
    public double?[][]? Predicted { get; set; }
    public double?[][]? True { get; set; }
    public double Rmse { get; set; }
    public double Mae { get; set; }
}
