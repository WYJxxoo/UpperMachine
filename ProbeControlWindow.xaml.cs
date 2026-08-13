using System.Globalization;
using System.Windows;
using UpperMachine.Models;
using UpperMachine.Services;

namespace UpperMachine;

public partial class ProbeControlWindow : Window
{
    private readonly ScanController _scanController;
    private readonly ProbeCommandStore _store = new();
    private ProbeCommandSettings _settings;

    public ProbeControlWindow(ScanController scanController)
    {
        _scanController = scanController;
        _settings = _store.Load();

        InitializeComponent();
        LoadSettingsToUi();
        AttachLogForwarding();
    }

    private void AttachLogForwarding()
    {
        _scanController.LogReceived += OnLogReceived;
    }

    private void DetachLogForwarding()
    {
        _scanController.LogReceived -= OnLogReceived;
    }

    protected override void OnClosed(EventArgs e)
    {
        DetachLogForwarding();
        base.OnClosed(e);
    }

    private void OnLogReceived(object? sender, string message)
    {
        Dispatcher.InvokeAsync(() =>
        {
            LogTextBox.AppendText(message + Environment.NewLine);
            LogTextBox.ScrollToEnd();
        });
    }

    private void LoadSettingsToUi()
    {
        RaiseCommandTextBox.Text = string.IsNullOrWhiteSpace(_settings.RaiseCommand) ? "$J=G91 Z-16 F1000" : _settings.RaiseCommand;
        DropCommandTextBox.Text = string.IsNullOrWhiteSpace(_settings.DropCommand) ? "$J=G91 Z5 F500" : _settings.DropCommand;
        HoldCommandTextBox.Text = _settings.HoldCommand;
        StepTextBox.Text = _settings.Step;
        FeedTextBox.Text = _settings.Feed;
    }

    private ProbeCommandSettings ReadSettingsFromUi()
    {
        return new ProbeCommandSettings
        {
            RaiseCommand = RaiseCommandTextBox.Text.Trim(),
            DropCommand = DropCommandTextBox.Text.Trim(),
            HoldCommand = HoldCommandTextBox.Text.Trim(),
            Step = StepTextBox.Text.Trim(),
            Feed = FeedTextBox.Text.Trim(),
        };
    }

    private bool EnsureConnected()
    {
        if (!_scanController.IsControlReady())
        {
            MessageBox.Show("请先连接控制串口。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    private void SendCommand(string command, string label)
    {
        if (!EnsureConnected())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(command))
        {
            MessageBox.Show($"{label}命令不能为空。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _scanController.SendControlCommand(command);
    }

    private void SendJog(string axis, int direction)
    {
        if (!EnsureConnected())
        {
            return;
        }

        if (!TryReadDouble(StepTextBox.Text, out double step) || step <= 0)
        {
            MessageBox.Show("请输入有效的点动步长。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!TryReadDouble(FeedTextBox.Text, out double feed) || feed <= 0)
        {
            MessageBox.Show("请输入有效的点动速度。", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string stepText = (direction * step).ToString("0.###", CultureInfo.InvariantCulture);
        string feedText = feed.ToString("0.###", CultureInfo.InvariantCulture);
        _scanController.SendControlCommand($"$J=G91 {axis}{stepText} F{feedText}");
    }

    private static bool TryReadDouble(string? value, out double result)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) ||
               double.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out result);
    }

    private void MoveLeftButton_Click(object sender, RoutedEventArgs e) => SendJog("X", -1);

    private void MoveRightButton_Click(object sender, RoutedEventArgs e) => SendJog("X", 1);

    private void MoveDownButton_Click(object sender, RoutedEventArgs e) => SendJog("Y", -1);

    private void MoveUpButton_Click(object sender, RoutedEventArgs e) => SendJog("Y", 1);

    private void VerifyDropButton_Click(object sender, RoutedEventArgs e) => SendCommand(DropCommandTextBox.Text.Trim(), "落笔");

    private void VerifyRaiseButton_Click(object sender, RoutedEventArgs e) => SendCommand(RaiseCommandTextBox.Text.Trim(), "抬笔");

    private void SendHoldButton_Click(object sender, RoutedEventArgs e) => SendCommand(HoldCommandTextBox.Text.Trim(), "保持");

    private void SaveSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        _settings = ReadSettingsFromUi();
        _store.Save(_settings);
        _scanController.RaiseCommand = _settings.RaiseCommand;
        _scanController.DropCommand = _settings.DropCommand;
        _scanController.HoldCommand = _settings.HoldCommand;
        MessageBox.Show("指令已保存。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e) => Close();

}
