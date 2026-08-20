using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using UpperMachine.Models;

namespace UpperMachine.Services;

/// <summary>
/// 通过子进程调用 Python 分析引擎（PythonAnalysis/analysis_engine.py），
/// 把扫描网格写成临时 CSV，读取引擎产出的 JSON 结果。
/// </summary>
public sealed class PythonAnalysisService
{
    private static readonly string[] PythonCandidates =
    {
        Path.Combine("D:", "Anaconda", "python.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Anaconda3", "python.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "miniconda3", "python.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "anaconda3", "python.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "miniconda3", "python.exe"),
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static string FindPythonExecutable()
    {
        foreach (string candidate in PythonCandidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "python";
    }

    public static string GetEngineScriptPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "PythonAnalysis", "analysis_engine.py");
    }

    /// <summary>
    /// 运行分析引擎。xValues/yValues 为网格坐标，values 为电压网格（缺失为 double.NaN）。
    /// </summary>
    public async Task<PythonAnalysisResult> RunAsync(
        double[] xValues,
        double[] yValues,
        double[,] values,
        string algo,
        CancellationToken cancellationToken = default)
    {
        string python = FindPythonExecutable();
        string script = GetEngineScriptPath();
        if (!File.Exists(script))
        {
            throw new InvalidOperationException($"未找到 Python 分析引擎脚本：{script}");
        }

        string tempRoot = Path.GetTempPath();
        string csvPath = Path.Combine(tempRoot, $"uppermachine_{Guid.NewGuid():N}.csv");
        string jsonPath = Path.Combine(tempRoot, $"uppermachine_{Guid.NewGuid():N}.json");

        try
        {
            WriteCsv(csvPath, xValues, yValues, values);

            string arguments = $"\"{script}\" --csv \"{csvPath}\" --json \"{jsonPath}\" --algo {algo}";
            (string stdout, string stderr, int exitCode) = await RunProcessAsync(python, arguments, cancellationToken);

            if (exitCode != 0)
            {
                string message = ExtractErrorMessage(stderr) ?? $"Python 分析引擎退出码 {exitCode}";
                throw new InvalidOperationException(message);
            }

            if (!File.Exists(jsonPath))
            {
                throw new InvalidOperationException("Python 分析引擎未生成结果文件。");
            }

            string json = await File.ReadAllTextAsync(jsonPath, Encoding.UTF8, cancellationToken);
            PythonAnalysisResult? result = JsonSerializer.Deserialize<PythonAnalysisResult>(json, JsonOptions);
            return result ?? throw new InvalidOperationException("Python 分析结果为空。");
        }
        finally
        {
            TryDelete(csvPath);
            TryDelete(jsonPath);
        }
    }

    private static void WriteCsv(string csvPath, double[] xValues, double[] yValues, double[,] values)
    {
        StringBuilder builder = new();
        builder.AppendLine("X坐标,Y坐标,电压值");

        int rows = values.GetLength(0);
        int columns = values.GetLength(1);
        for (int row = 0; row < rows; row++)
        {
            for (int column = 0; column < columns; column++)
            {
                double voltage = values[row, column];
                if (double.IsNaN(voltage))
                {
                    continue;
                }

                double x = xValues[column];
                double y = yValues[row];
                builder.Append(x.ToString(CultureInfo.InvariantCulture));
                builder.Append(',');
                builder.Append(y.ToString(CultureInfo.InvariantCulture));
                builder.Append(',');
                builder.AppendLine(voltage.ToString(CultureInfo.InvariantCulture));
            }
        }

        File.WriteAllText(csvPath, builder.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static async Task<(string Stdout, string Stderr, int ExitCode)> RunProcessAsync(
        string fileName,
        string arguments,
        CancellationToken cancellationToken)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            },
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"无法启动 Python：{fileName}");
        }

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        string stdout = await stdoutTask;
        string stderr = await stderrTask;
        return (stdout, stderr, process.ExitCode);
    }

    private static string? ExtractErrorMessage(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr))
        {
            return null;
        }

        const string marker = "__ERROR__:";
        string? marked = stderr
            .Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith(marker, StringComparison.Ordinal));
        if (marked is not null)
        {
            return marked[marker.Length..].Trim();
        }

        return stderr.Trim();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
