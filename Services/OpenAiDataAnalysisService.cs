using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using UpperMachine.Models;

namespace UpperMachine.Services;

public sealed class OpenAiDataAnalysisService
{
    private static readonly HttpClient HttpClient = new(new SocketsHttpHandler
    {
        AutomaticDecompression = System.Net.DecompressionMethods.All,
        ConnectTimeout = TimeSpan.FromSeconds(30),
    })
    {
        Timeout = TimeSpan.FromSeconds(300),
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private const string DefaultUrl = "https://api.deepseek.com/responses";
    private const string DefaultModel = "deepseek-v4-flash";
    private const string SystemInstruction = "你是经验丰富的数据分析助手。请基于扫描数据做中文分析，先结论后证据，不要编造未出现的数值。";

    public string BuildSummaryText(
        IReadOnlyList<PointData> points,
        ScanPlanInfo? plan,
        ScanParameter? parameter)
    {
        AnalysisStats stats = BuildStats(points);
        StringBuilder sb = new();

        sb.AppendLine($"样本数: {stats.TotalCount}");
        sb.AppendLine($"有效值: {stats.ValidCount}");
        sb.AppendLine($"无效值: {stats.InvalidCount}");

        if (plan is not null)
        {
            sb.AppendLine($"网格: {plan.Rows} x {plan.Columns}");
            sb.AppendLine($"步长: X {plan.StepX}, Y {plan.StepY}");
            sb.AppendLine($"范围: X 0..{plan.MaxX.ToString("F2", CultureInfo.InvariantCulture)}, Y 0..{plan.MaxY.ToString("F2", CultureInfo.InvariantCulture)}");
        }

        if (parameter is not null)
        {
            sb.AppendLine($"形状: {parameter.Shape}");
            sb.AppendLine($"半径: {parameter.Radius.ToString("F2", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"速度: {parameter.Speed.ToString("F2", CultureInfo.InvariantCulture)}");
        }

        if (stats.ValidCount > 0)
        {
            sb.AppendLine($"最小值: {FormatPointValue(stats.MinPoint!.Value)}");
            sb.AppendLine($"最大值: {FormatPointValue(stats.MaxPoint!.Value)}");
            sb.AppendLine($"平均值: {stats.Mean.ToString("F4", CultureInfo.InvariantCulture)} V");
            sb.AppendLine($"中位数: {stats.Median.ToString("F4", CultureInfo.InvariantCulture)} V");
            sb.AppendLine($"标准差: {stats.StandardDeviation.ToString("F4", CultureInfo.InvariantCulture)} V");
        }

        return sb.ToString().TrimEnd();
    }

    public async Task<string> AnalyzeAsync(
        string apiKey,
        string url,
        string model,
        IReadOnlyList<PointData> points,
        ScanPlanInfo? plan,
        ScanParameter? parameter,
        string userInstruction,
        CancellationToken cancellationToken = default)
    {
        string prompt = BuildPrompt(points, plan, parameter, userInstruction);
        string resolvedModel = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
        string endpoint = string.IsNullOrWhiteSpace(url) ? DefaultUrl : url.Trim();

        // 兼容两种接口：/responses 使用 input+instructions，其余按 chat/completions 使用 messages。
        object payload = endpoint.Contains("/responses", StringComparison.OrdinalIgnoreCase)
            ? new
            {
                model = resolvedModel,
                instructions = SystemInstruction,
                input = prompt,
            }
            : new
            {
                model = resolvedModel,
                messages = new object[]
                {
                    new { role = "system", content = SystemInstruction },
                    new { role = "user", content = prompt },
                },
            };
        using HttpRequestMessage request = new(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        using HttpResponseMessage response = await SendAsync(request, cancellationToken);
        string responseBody = await ReadBodyAsync(response, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(ExtractErrorMessage(responseBody, response.StatusCode));
        }

        string text = ExtractResponseText(responseBody);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("模型返回为空。");
        }

        return text.Trim();
    }

    public string BuildPrompt(
        IReadOnlyList<PointData> points,
        ScanPlanInfo? plan,
        ScanParameter? parameter,
        string userInstruction)
    {
        AnalysisStats stats = BuildStats(points);
        StringBuilder sb = new();

        sb.AppendLine("你是经验丰富的数据分析助手。");
        sb.AppendLine("请基于下面的上位机扫描数据，用中文输出简洁但有判断力的分析。");
        sb.AppendLine("要求：");
        sb.AppendLine("1. 先给结论，再给证据。");
        sb.AppendLine("2. 重点关注整体趋势、异常点、峰谷分布、边界变化和可能的工艺/机械含义。");
        sb.AppendLine("3. 如果数据不足或存在缺失，请明确指出。");
        sb.AppendLine("4. 不要编造未出现在数据中的数值。");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(userInstruction))
        {
            sb.AppendLine("用户额外要求：");
            sb.AppendLine(userInstruction.Trim());
            sb.AppendLine();
        }

        sb.AppendLine("数据概览：");
        sb.AppendLine(BuildSummaryText(points, plan, parameter));
        sb.AppendLine();
        sb.AppendLine("原始数据：");
        sb.AppendLine("Order,X,Y,Voltage");

        foreach (PointData point in points)
        {
            sb.AppendLine(
                $"{point.Order}," +
                $"{point.X.ToString("F2", CultureInfo.InvariantCulture)}," +
                $"{point.Y.ToString("F2", CultureInfo.InvariantCulture)}," +
                $"{FormatVoltage(point.Voltage)}");
        }

        return sb.ToString();
    }

    private static AnalysisStats BuildStats(IReadOnlyList<PointData> points)
    {
        if (points.Count == 0)
        {
            return AnalysisStats.Empty;
        }

        List<PointData> validPoints = new();
        foreach (PointData point in points)
        {
            if (!double.IsNaN(point.Voltage) && point.Voltage >= 0)
            {
                validPoints.Add(point);
            }
        }

        if (validPoints.Count == 0)
        {
            return new AnalysisStats(points.Count, 0, points.Count, null, null, double.NaN, double.NaN, double.NaN, double.NaN);
        }

        PointData minPoint = validPoints[0];
        PointData maxPoint = validPoints[0];
        List<double> voltages = new(validPoints.Count);

        foreach (PointData point in validPoints)
        {
            voltages.Add(point.Voltage);

            if (point.Voltage < minPoint.Voltage)
            {
                minPoint = point;
            }

            if (point.Voltage > maxPoint.Voltage)
            {
                maxPoint = point;
            }
        }

        double mean = voltages.Average();
        double median = CalculateMedian(voltages);
        double variance = voltages.Sum(value => Math.Pow(value - mean, 2)) / voltages.Count;
        double standardDeviation = Math.Sqrt(variance);

        return new AnalysisStats(
            points.Count,
            validPoints.Count,
            points.Count - validPoints.Count,
            minPoint,
            maxPoint,
            mean,
            median,
            standardDeviation,
            maxPoint.Voltage - minPoint.Voltage);
    }

    private static double CalculateMedian(List<double> values)
    {
        values.Sort();
        if (values.Count == 0)
        {
            return double.NaN;
        }

        int middle = values.Count / 2;
        if (values.Count % 2 == 1)
        {
            return values[middle];
        }

        return (values[middle - 1] + values[middle]) / 2.0;
    }

    private static string FormatPointValue(PointData point)
    {
        return $"{point.Voltage.ToString("F4", CultureInfo.InvariantCulture)} V @ ({point.X.ToString("F2", CultureInfo.InvariantCulture)}, {point.Y.ToString("F2", CultureInfo.InvariantCulture)})";
    }

    private static string FormatVoltage(double voltage)
    {
        return double.IsNaN(voltage) || voltage < 0
            ? "NA"
            : voltage.ToString("F4", CultureInfo.InvariantCulture);
    }

    private static string ExtractResponseText(string responseBody)
    {
        using JsonDocument document = JsonDocument.Parse(responseBody);
        JsonElement root = document.RootElement;

        if (root.TryGetProperty("output_text", out JsonElement outputText) &&
            outputText.ValueKind == JsonValueKind.String)
        {
            return outputText.GetString() ?? string.Empty;
        }

        if (TryExtractFromOutput(root, out string? text))
        {
            return text ?? string.Empty;
        }

        if (TryExtractFromChatCompletions(root, out text))
        {
            return text ?? string.Empty;
        }

        return string.Empty;
    }

    private static bool TryExtractFromOutput(JsonElement root, out string? text)
    {
        text = null;

        if (!root.TryGetProperty("output", out JsonElement output) ||
            output.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        StringBuilder builder = new();
        foreach (JsonElement outputItem in output.EnumerateArray())
        {
            if (!outputItem.TryGetProperty("content", out JsonElement content) ||
                content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (JsonElement contentItem in content.EnumerateArray())
            {
                if (contentItem.TryGetProperty("text", out JsonElement textElement) &&
                    textElement.ValueKind == JsonValueKind.String)
                {
                    string? value = textElement.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        builder.AppendLine(value);
                    }
                    continue;
                }

                if (contentItem.TryGetProperty("value", out JsonElement valueElement) &&
                    valueElement.ValueKind == JsonValueKind.String)
                {
                    string? value = valueElement.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        builder.AppendLine(value);
                    }
                }
            }
        }

        if (builder.Length == 0)
        {
            return false;
        }

        text = builder.ToString().Trim();
        return true;
    }

    private static bool TryExtractFromChatCompletions(JsonElement root, out string? text)
    {
        text = null;

        if (!root.TryGetProperty("choices", out JsonElement choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0)
        {
            return false;
        }

        JsonElement firstChoice = choices[0];
        if (!firstChoice.TryGetProperty("message", out JsonElement message))
        {
            return false;
        }

        if (message.TryGetProperty("content", out JsonElement content))
        {
            if (content.ValueKind == JsonValueKind.String)
            {
                text = content.GetString();
                return !string.IsNullOrWhiteSpace(text);
            }

            if (content.ValueKind == JsonValueKind.Array)
            {
                StringBuilder builder = new();
                foreach (JsonElement contentItem in content.EnumerateArray())
                {
                    if (contentItem.TryGetProperty("text", out JsonElement textElement) &&
                        textElement.ValueKind == JsonValueKind.String)
                    {
                        string? value = textElement.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            builder.AppendLine(value);
                        }
                    }
                }

                if (builder.Length > 0)
                {
                    text = builder.ToString().Trim();
                    return true;
                }
            }
        }

        return false;
    }

    private static string GetInnermostMessage(Exception ex)
    {
        Exception current = ex;
        while (current.InnerException is not null)
        {
            current = current.InnerException;
        }

        return current.Message;
    }

    private static async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await HttpClient.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.IO.IOException or TaskCanceledException)
        {
            throw new InvalidOperationException($"网络请求失败：{GetInnermostMessage(ex)}", ex);
        }
    }

    private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or System.IO.IOException or TaskCanceledException)
        {
            throw new InvalidOperationException($"读取响应失败：{GetInnermostMessage(ex)}", ex);
        }
    }

    private static string ExtractErrorMessage(string responseBody, HttpStatusCode statusCode)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(responseBody);
            JsonElement root = document.RootElement;
            if (root.TryGetProperty("error", out JsonElement error) &&
                error.TryGetProperty("message", out JsonElement message) &&
                message.ValueKind == JsonValueKind.String)
            {
                string? errorMessage = message.GetString();
                if (!string.IsNullOrWhiteSpace(errorMessage))
                {
                    return $"OpenAI 请求失败 ({(int)statusCode}): {errorMessage}";
                }
            }
        }
        catch
        {
        }

        return $"OpenAI 请求失败 ({(int)statusCode})";
    }

    private sealed record AnalysisStats(
        int TotalCount,
        int ValidCount,
        int InvalidCount,
        PointData? MinPoint,
        PointData? MaxPoint,
        double Mean,
        double Median,
        double StandardDeviation,
        double Range)
    {
        public static AnalysisStats Empty { get; } = new(0, 0, 0, null, null, double.NaN, double.NaN, double.NaN, double.NaN);
    }
}
