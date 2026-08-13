using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using UpperMachine.Models;
using UpperMachine.Services;

namespace UpperMachine;

public partial class DataAnalysisWindow : Window
{
    private readonly OpenAiDataAnalysisService _analysisService = new();
    private readonly IReadOnlyList<PointData> _points;
    private readonly ScanPlanInfo? _scanPlan;
    private readonly ScanParameter? _scanParameter;

    public DataAnalysisWindow(
        IReadOnlyList<PointData> points,
        ScanPlanInfo? scanPlan,
        ScanParameter? scanParameter)
    {
        InitializeComponent();

        _points = points.ToArray();
        _scanPlan = scanPlan;
        _scanParameter = scanParameter;

        SummaryTextBlock.Text = _analysisService.BuildSummaryText(_points, _scanPlan, _scanParameter);
        DatasetInfoTextBlock.Text = $"{_points.Count} 条";

        Loaded += (_, _) => ApiKeyPasswordBox.Focus();
        Closing += (_, _) => ApiKeyPasswordBox.Clear();
    }

    private async void AnalyzeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string apiKey = ApiKeyPasswordBox.Password.Trim();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")?.Trim() ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                MessageBox.Show("请填写 API Key 或设置 OPENAI_API_KEY。", "AI 数据分析", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_points.Count == 0)
            {
                MessageBox.Show("当前没有可分析的数据。", "AI 数据分析", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string model = ModelTextBox.Text.Trim();
            string prompt = PromptTextBox.Text.Trim();

            AnalyzeButton.IsEnabled = false;
            StatusTextBlock.Text = "分析中...";
            ResultTextBox.Text = string.Empty;

            string result = await _analysisService.AnalyzeAsync(
                apiKey,
                model,
                _points,
                _scanPlan,
                _scanParameter,
                prompt);

            ResultTextBox.Text = result;
            StatusTextBlock.Text = "完成";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = "失败";
            MessageBox.Show(ex.Message, "AI 数据分析", MessageBoxButton.OK, MessageBoxImage.Error);
            ResultTextBox.Text = ex.ToString();
        }
        finally
        {
            AnalyzeButton.IsEnabled = true;
        }
    }

    private void CopyResultButton_Click(object sender, RoutedEventArgs e)
    {
        string result = ResultTextBox.Text;
        if (string.IsNullOrWhiteSpace(result))
        {
            return;
        }

        Clipboard.SetText(result);
        StatusTextBlock.Text = "结果已复制";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
