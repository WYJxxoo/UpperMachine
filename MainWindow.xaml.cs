using System.Collections.ObjectModel;
using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using UpperMachine.Models;
using UpperMachine.Services;
using UpperMachine.ViewModels;

namespace UpperMachine;

public partial class MainWindow : Window
{
    private const double SurfaceHeightUnit = 8.0;
    private const double SurfaceVoltageMax = 10.0;
    private const int MaxLogLines = 2000;
    private const int SurfaceColorBuckets = 48;

    private readonly ObservableCollection<PointRowViewModel> _pointRows = new();
    private readonly ObservableCollection<ScanParameterPreset> _scanPresets = new();
    private readonly ScanController _scanController = new();
    private readonly ScanPresetStore _presetStore = new();
    private readonly Model3DGroup _surfaceScene = new();
    private readonly Model3DGroup _surfaceDataGroup = new();
    private ProbeControlWindow? _probeControlWindow;

    private SerialPort? _controlPort;
    private SerialPort? _dataPort;
    private ScanPlanInfo? _scanPlan;
    private double[,]? _heatmapData;
    private DateTime _lastPlotRefresh = DateTime.MinValue;
    private DateTime _lastGridScroll = DateTime.MinValue;
    private DateTime _lastSurfaceRebuild = DateTime.MinValue;
    private ScaleTransform3D? _surfaceHeightScale;
    private ScanParameter? _lastScanParameter;
    private bool _analysisButtonAdded;
    private ScanParameter CurrentRenderParameter => _lastScanParameter ?? ReadScanParameterFromUi();

    public MainWindow()
    {
        InitializeComponent();
        NormalizeUiText();

        PointsDataGrid.ItemsSource = _pointRows;
        SourceInitialized += (_, _) => EnsureWindowFitsWorkArea();
        InitializeThreeDScene();
        ConfigureUiDefaults();
        ConfigureControllerEvents();
        EnsureAnalysisEntryPoint();
        RenderVisuals(force: true);
        UpdateSurfaceTransform();

        Closing += MainWindow_Closing;
    }

    private void NormalizeUiText()
    {
        Title = "GRBL 扫描上位机";
        CurrentPageTitleTextBlock.Text = "设备与参数";
        CurrentPageHintTextBlock.Text = "先连接控制串口和传感串口，再设置扫描范围、步长和设备命令。";

        SetLeadingText(StateTextBlock, "当前状态");
        StateTextBlock.Text = "未连接";
        DeviceStatusTextBlock.Text = "请先连接控制串口和传感串口。";

        SetNavigationItemText(0, "设备与参数", "连接设备并配置扫描参数");
        SetNavigationItemText(1, "扫描监控与图形", "查看扫描进度、热力图和 3D 视图");
        SetNavigationItemText(2, "数据与日志", "查看采样点、电压结果和串口日志");

        SetLeadingText(CurrentPageTitleTextBlock, "设备与参数");
        SetTopRightSummary("当前预设参数", "未选择");
        SetBottomSidebarSummary("当前模式", "自动扫描与结果分析");

        SetCardTitle(ControlPortComboBox, "串口连接");
        SetLabelFor(ControlPortComboBox, "控制串口");
        SetLabelFor(ControlBaudComboBox, "控制波特率");
        SetLabelFor(DataPortComboBox, "传感串口");
        SetLabelFor(DataBaudComboBox, "传感波特率");
        SetButtonsInNearestWrapPanel(ControlPortComboBox, "刷新串口", "连接设备", "断开连接");

        SetCardTitle(RaiseCommandTextBox, "控制与指令");
        SetLabelFor(RaiseCommandTextBox, "抬笔命令");
        SetLabelFor(DropCommandTextBox, "落笔命令");
        SetLabelFor(HoldCommandTextBox, "保持命令");
        SetLabelFor(RawCommandTextBox, "手动指令");
        SetButtonInSameGrid(RawCommandTextBox, "发送");
        SetLastButtonInBorder(RaiseCommandTextBox, "打开控制面板");

        SetCardTitle(MaxXTextBox, "扫描参数设置");
        SetLabelFor(MaxXTextBox, "X 范围 (mm)");
        SetLabelFor(MaxYTextBox, "Y 范围 (mm)");
        SetLabelFor(StepXTextBox, "X 步长 (mm)");
        SetLabelFor(StepYTextBox, "Y 步长 (mm)");
        SetLabelFor(SpeedTextBox, "速度 (mm/min)");
        SetLabelFor(ShapeComboBox, "扫描形状");
        SetLabelFor(RadiusTextBox, "半径 (mm)");
        SetLabelFor(FilterAlgorithmComboBox, "滤波算法");
        ReplaceComboItems(ShapeComboBox, "矩形", "圆形");
        ReplaceComboItems(FilterAlgorithmComboBox, ScanParameter.AverageFilter, ScanParameter.MedianFilter, ScanParameter.TrimmedMeanFilter);
        SetLabelFor(Channel2ModeComboBox, "CH2 模式");
        SetLabelFor(Channel3ModeComboBox, "CH3 模式");
        SetLabelFor(Channel4ModeComboBox, "CH4 模式");
        ReplaceComboItems(Channel2ModeComboBox, "关闭", "辅助扫描", "辅助滤波");
        ReplaceComboItems(Channel3ModeComboBox, "关闭", "辅助扫描", "辅助滤波");
        ReplaceComboItems(Channel4ModeComboBox, "关闭", "辅助扫描", "辅助滤波");
        SetLabelFor(Channel2OffsetXTextBox, "CH2 X 增量");
        SetLabelFor(Channel2OffsetYTextBox, "CH2 Y 增量");
        SetLabelFor(Channel3OffsetXTextBox, "CH3 X 增量");
        SetLabelFor(Channel3OffsetYTextBox, "CH3 Y 增量");
        SetLabelFor(Channel4OffsetXTextBox, "CH4 X 增量");
        SetLabelFor(Channel4OffsetYTextBox, "CH4 Y 增量");
        SetButtonsInNearestWrapPanel(MaxXTextBox, "开始扫描", "停止扫描", "继续扫描", "返回原点", "导出 CSV");

        SetCardTitle(PresetNameTextBox, "预设参数");
        SetDescriptionInCard(PresetNameTextBox, "保存、加载或直接使用常用扫描参数。", 1);
        SetLabelFor(PresetNameTextBox, "预设名称");
        PresetNameTextBox.Text = "默认参数组";
        SetLabelFor(PresetComboBox, "已保存预设");
        SetButtonsInNearestWrapPanel(PresetNameTextBox, "保存预设", "加载预设", "使用预设扫描", "删除预设");

        SetCardTitle(ProgressTextBlock, "扫描进度", useLeadingText: true);
        SetCardTitle(LastVoltageTextBlock, "当前电压", useLeadingText: true);
        SetCardTitle(LastPointTextBlock, "当前位置", useLeadingText: true);
        SetCardTitle(HeatmapPlot, "扫描热力图");
        SetCardTitle(SurfaceViewport, "3D 扫描视图");
        SetCardTitle(LogTextBox, "串口日志");
        SetCardTitle(PointsDataGrid, "采样点明细");

        if (PointsDataGrid.Columns.Count >= 4)
        {
            PointsDataGrid.Columns[0].Header = "序号";
            PointsDataGrid.Columns[1].Header = "X";
            PointsDataGrid.Columns[2].Header = "Y";
            PointsDataGrid.Columns[3].Header = "电压";
        }
    }

    private void SetTopRightSummary(string title, string value)
    {
        Border? summaryBorder = FindVisualChildren<Border>(this)
            .FirstOrDefault(border => border.Child is StackPanel panel && panel.Children.OfType<TextBlock>().Count() >= 2);
        if (summaryBorder?.Child is not StackPanel panel)
        {
            return;
        }

        TextBlock[] texts = panel.Children.OfType<TextBlock>().ToArray();
        if (texts.Length >= 2)
        {
            texts[0].Text = title;
            texts[1].Text = value;
        }
    }

    private void SetBottomSidebarSummary(string title, string value)
    {
        Border? sidebarFooter = FindVisualChildren<Border>(this)
            .FirstOrDefault(border => border.CornerRadius.TopLeft == 8 && border.Child is StackPanel panel && panel.Children.OfType<TextBlock>().Count() == 2 && panel.Children.OfType<TextBlock>().Last().Text.Contains("数据与日志", StringComparison.Ordinal));
        if (sidebarFooter?.Child is not StackPanel panel)
        {
            return;
        }

        TextBlock[] texts = panel.Children.OfType<TextBlock>().ToArray();
        texts[0].Text = title;
        texts[1].Text = value;
    }

    private void SetNavigationItemText(int index, string title, string hint)
    {
        if (NavigationListBox.Items[index] is not ListBoxItem item || item.Content is not StackPanel panel)
        {
            return;
        }

        TextBlock[] texts = panel.Children.OfType<TextBlock>().ToArray();
        if (texts.Length >= 2)
        {
            texts[0].Text = title;
            texts[1].Text = hint;
        }
    }

    private void SetLeadingText(FrameworkElement element, string text)
    {
        if (element.Parent is not Panel panel)
        {
            return;
        }

        int index = panel.Children.IndexOf(element);
        if (index > 0 && panel.Children[index - 1] is TextBlock textBlock)
        {
            textBlock.Text = text;
        }
    }

    private void SetLabelFor(FrameworkElement element, string text)
    {
        if (element.Parent is not Panel panel)
        {
            return;
        }

        Label? label = panel.Children.OfType<Label>().FirstOrDefault();
        if (label is not null)
        {
            label.Content = text;
        }
    }

    private void SetButtonInSameGrid(FrameworkElement element, string text)
    {
        Grid? grid = FindAncestor<Grid>(element);
        Button? button = grid?.Children.OfType<Button>().FirstOrDefault();
        if (button is not null)
        {
            button.Content = text;
        }
    }

    private void SetLastButtonInBorder(FrameworkElement element, string text)
    {
        Border? border = FindAncestor<Border>(element);
        Button? button = FindVisualChildren<Button>(border).LastOrDefault();
        if (button is not null)
        {
            button.Content = text;
        }
    }

    private void SetButtonsInNearestWrapPanel(FrameworkElement element, params string[] texts)
    {
        Border? border = FindAncestor<Border>(element);
        WrapPanel? wrapPanel = FindVisualChildren<WrapPanel>(border).LastOrDefault();
        if (wrapPanel is null)
        {
            return;
        }

        Button[] buttons = wrapPanel.Children.OfType<Button>().ToArray();
        for (int i = 0; i < buttons.Length && i < texts.Length; i++)
        {
            buttons[i].Content = texts[i];
        }
    }

    private void SetCardTitle(FrameworkElement element, string text, bool useLeadingText = false)
    {
        if (useLeadingText)
        {
            SetLeadingText(element, text);
            return;
        }

        Border? border = FindAncestor<Border>(element);
        TextBlock? title = FindVisualChildren<TextBlock>(border).FirstOrDefault();
        if (title is not null)
        {
            title.Text = text;
        }
    }

    private void SetDescriptionInCard(FrameworkElement element, string text, int textBlockIndex)
    {
        Border? border = FindAncestor<Border>(element);
        TextBlock[] texts = FindVisualChildren<TextBlock>(border).ToArray();
        if (texts.Length > textBlockIndex)
        {
            texts[textBlockIndex].Text = text;
        }
    }

    private void ReplaceComboItems(ComboBox comboBox, params string[] items)
    {
        object? selectedTag = (comboBox.SelectedItem as ComboBoxItem)?.Tag;
        comboBox.Items.Clear();
        for (int i = 0; i < items.Length; i++)
        {
            ComboBoxItem item = new() { Content = items[i] };
            if (comboBox == Channel2ModeComboBox || comboBox == Channel3ModeComboBox || comboBox == Channel4ModeComboBox)
            {
                item.Tag = i switch
                {
                    0 => ScanParameter.ChannelModeOff,
                    1 => ScanParameter.ChannelModeAuxScan,
                    _ => ScanParameter.ChannelModeAuxFilter,
                };
            }

            comboBox.Items.Add(item);
            if ((selectedTag is null && i == 0) || Equals(item.Tag, selectedTag))
            {
                comboBox.SelectedItem = item;
            }
        }

        if (comboBox.SelectedIndex < 0 && comboBox.Items.Count > 0)
        {
            comboBox.SelectedIndex = 0;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            child = VisualTreeHelper.GetParent(child);
            if (child is T target)
            {
                return target;
            }
        }

        return null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject? root) where T : DependencyObject
    {
        if (root is null)
        {
            yield break;
        }

        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
            {
                yield return typed;
            }

            foreach (T descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
    private void EnsureWindowFitsWorkArea()
    {
        Rect workArea = SystemParameters.WorkArea;
        double maxWidth = Math.Max(960, workArea.Width - 24);
        double maxHeight = Math.Max(680, workArea.Height - 24);

        double minWidth = MinWidth;
        double minHeight = MinHeight;

        Width = Math.Max(minWidth, Math.Min(Width, maxWidth));
        Height = Math.Max(minHeight, Math.Min(Height, maxHeight));

        Left = workArea.Left + Math.Max(0, (workArea.Width - Width) / 2);
        Top = workArea.Top + Math.Max(0, (workArea.Height - Height) / 2);
    }

    private void ConfigureUiDefaults()
    {
        ControlBaudComboBox.ItemsSource = new[] { "115200", "57600", "38400", "19200", "9600" };
        DataBaudComboBox.ItemsSource = new[] { "115200", "57600", "38400", "19200", "9600" };
        PresetComboBox.ItemsSource = _scanPresets;
        ControlBaudComboBox.SelectedIndex = 0;
        DataBaudComboBox.SelectedItem = "9600";
        RefreshPortList();
        LoadPresetList();
        NavigationListBox.SelectedIndex = 0;
        AppendLog("界面已加载，请先连接控制串口和传感串口。");
    }

    private void EnsureAnalysisEntryPoint()
    {
        if (_analysisButtonAdded ||
            DataPage.Children.Count == 0 ||
            DataPage.Children[0] is not Border headerBorder ||
            headerBorder.Child is not Grid headerGrid)
        {
            return;
        }

        Button? saveButton = headerGrid.Children.OfType<Button>().FirstOrDefault();
        if (saveButton is null)
        {
            return;
        }

        headerGrid.Children.Remove(saveButton);

        WrapPanel actionPanel = new()
        {
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        Grid.SetColumn(actionPanel, 1);

        Button analysisButton = new()
        {
            Content = "AI 分析",
            Style = (Style)FindResource("ActionButtonStyle"),
        };
        analysisButton.Click += OpenAnalysisButton_Click;

        saveButton.Content = "导出 CSV";
        actionPanel.Children.Add(analysisButton);
        actionPanel.Children.Add(saveButton);
        headerGrid.Children.Add(actionPanel);

        _analysisButtonAdded = true;
    }

    private void OpenProbeControlWindowButton_Click(object sender, RoutedEventArgs e)
    {
        if (_probeControlWindow is null || !_probeControlWindow.IsVisible)
        {
            _probeControlWindow = new ProbeControlWindow(_scanController)
            {
                Owner = this,
            };
            _probeControlWindow.Closed += (_, _) => _probeControlWindow = null;
            _probeControlWindow.Show();
            return;
        }

        _probeControlWindow.Activate();
    }

    private void ConfigureControllerEvents()
    {
        _scanController.LogReceived += (_, message) =>
            Dispatcher.InvokeAsync(() => AppendLog(message), DispatcherPriority.Background);

        _scanController.StateChanged += (_, state) =>
            Dispatcher.InvokeAsync(() => StateTextBlock.Text = state);

        _scanController.PathPrepared += (_, args) =>
            Dispatcher.InvokeAsync(() => PreparePointRows(args));

        _scanController.PointScanned += (_, args) =>
            Dispatcher.InvokeAsync(() => ApplyScannedPoint(args));

        _scanController.ScanCompleted += (_, args) =>
            Dispatcher.InvokeAsync(() =>
            {
                AppendLog(args.Message);
                RenderVisuals(force: true);
                RebuildSurface();
                SurfaceViewport.ZoomExtents(0);
                if (!args.Cancelled)
                {
                    MessageBox.Show(args.Message, "扫描完成", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            });
    }

    private void InitializeThreeDScene()
    {
        _surfaceHeightScale = new ScaleTransform3D(1, HeightScaleSlider.Value, 1);
        _surfaceDataGroup.Transform = _surfaceHeightScale;

        _surfaceScene.Children.Add(new AmbientLight(Color.FromRgb(90, 100, 110)));
        _surfaceScene.Children.Add(new DirectionalLight(Color.FromRgb(255, 244, 224), new Vector3D(-1, -1.6, -1.2)));
        _surfaceScene.Children.Add(new DirectionalLight(Color.FromRgb(120, 186, 255), new Vector3D(1, -0.7, 0.3)));
        _surfaceScene.Children.Add(_surfaceDataGroup);

        ModelVisual3D sceneVisual = new()
        {
            Content = _surfaceScene,
        };

        SurfaceViewport.Children.Clear();
        SurfaceViewport.Children.Add(sceneVisual);
    }

    private void RefreshPortList()
    {
        string[] ports = SerialPort.GetPortNames().OrderBy(name => name).ToArray();
        string? currentControl = ControlPortComboBox.SelectedItem as string;
        string? currentData = DataPortComboBox.SelectedItem as string;

        ControlPortComboBox.ItemsSource = ports;
        DataPortComboBox.ItemsSource = ports.ToArray();

        if (!string.IsNullOrWhiteSpace(currentControl) && ports.Contains(currentControl))
        {
            ControlPortComboBox.SelectedItem = currentControl;
        }
        else if (ports.Length > 0)
        {
            ControlPortComboBox.SelectedIndex = 0;
        }

        if (!string.IsNullOrWhiteSpace(currentData) && ports.Contains(currentData))
        {
            DataPortComboBox.SelectedItem = currentData;
        }
        else if (ports.Length > 1)
        {
            DataPortComboBox.SelectedIndex = 1;
        }
        else if (ports.Length == 1)
        {
            DataPortComboBox.SelectedIndex = 0;
        }
    }

    private void PreparePointRows(PathPreparedEventArgs args)
    {
        _scanPlan = args.Plan;
        _heatmapData = new double[_scanPlan.Rows, _scanPlan.Columns];

        for (int row = 0; row < _scanPlan.Rows; row++)
        {
            for (int col = 0; col < _scanPlan.Columns; col++)
            {
                _heatmapData[row, col] = double.NaN;
            }
        }

        _pointRows.Clear();
        foreach (PointData point in args.Points)
        {
            _pointRows.Add(new PointRowViewModel
            {
                Order = point.Order,
                X = point.X,
                Y = point.Y,
                Voltage = point.Voltage,
                Channel1Voltage = point.Channel1Voltage,
                Channel2Voltage = point.Channel2Voltage,
                Channel3Voltage = point.Channel3Voltage,
                Channel4Voltage = point.Channel4Voltage,
            });
        }

        ProgressTextBlock.Text = $"0 / {_pointRows.Count}";
        LastVoltageTextBlock.Text = "-- V";
        LastPointTextBlock.Text = "(0.00, 0.00)";
        ResetSurface();
        SurfaceViewport.ZoomExtents(0);
        RenderVisuals(force: true);
    }

    private void ApplyScannedPoint(PointScannedEventArgs args)
    {
        if (args.Index < 0 || args.Index >= _pointRows.Count)
        {
            return;
        }

        PointRowViewModel row = _pointRows[args.Index];
        row.Voltage = args.Point.Voltage;
        row.Channel1Voltage = args.Point.Channel1Voltage;
        row.Channel2Voltage = args.Point.Channel2Voltage;
        row.Channel3Voltage = args.Point.Channel3Voltage;
        row.Channel4Voltage = args.Point.Channel4Voltage;
        UpdateHeatmapCells(row);

        // 3D 曲面采用节流重建：单网格合并后重建开销已大幅降低，
        // 仍按固定间隔刷新，既保留实时感，又避免每个点都触发完整网格重建。
        if (DateTime.Now - _lastSurfaceRebuild >= TimeSpan.FromMilliseconds(500))
        {
            _lastSurfaceRebuild = DateTime.Now;
            RebuildSurface();
        }

        ProgressTextBlock.Text = $"{args.Index + 1} / {_pointRows.Count}";
        LastVoltageTextBlock.Text = args.Point.Voltage >= 0 ? $"{args.Point.Voltage:F4} V" : "-- V";
        LastPointTextBlock.Text = $"({args.Point.X:F2}, {args.Point.Y:F2})";

        if (DateTime.Now - _lastGridScroll >= TimeSpan.FromMilliseconds(300))
        {
            _lastGridScroll = DateTime.Now;
            PointsDataGrid.ScrollIntoView(row);
        }

        RenderVisuals(force: false);
    }

    private void RenderVisuals(bool force)
    {
        if (!force && DateTime.Now - _lastPlotRefresh < TimeSpan.FromMilliseconds(140))
        {
            return;
        }

        _lastPlotRefresh = DateTime.Now;
        RenderHeatmap();
    }

    private void RenderHeatmap()
    {
        HeatmapPlot.Plot.Clear();

        if (_heatmapData is not null && _scanPlan is not null)
        {
            bool hasValidValue = _heatmapData
                .Cast<double>()
                .Any(value => !double.IsNaN(value) && value >= 0);

            if (hasValidValue)
            {
                var heatmap = HeatmapPlot.Plot.Add.Heatmap(_heatmapData);
                heatmap.CellAlignment = ScottPlot.Alignment.LowerLeft;
                heatmap.Rectangle = new ScottPlot.CoordinateRect(
                    _scanPlan.MinX - _scanPlan.StepX / 2.0,
                    _scanPlan.MinX + _scanPlan.MaxX + _scanPlan.StepX / 2.0,
                    _scanPlan.MinY - _scanPlan.StepY / 2.0,
                    _scanPlan.MinY + _scanPlan.MaxY + _scanPlan.StepY / 2.0);
                heatmap.Colormap = new ScottPlot.Colormaps.Turbo();
            }

            HeatmapPlot.Plot.Axes.SetLimits(
                _scanPlan.MinX - _scanPlan.StepX / 2.0,
                _scanPlan.MinX + _scanPlan.MaxX + _scanPlan.StepX / 2.0,
                _scanPlan.MinY - _scanPlan.StepY / 2.0,
                _scanPlan.MinY + _scanPlan.MaxY + _scanPlan.StepY / 2.0);
            HeatmapPlot.Plot.Axes.SquareUnits();
        }

        HeatmapPlot.Plot.Title("Scan Heatmap");
        HeatmapPlot.Plot.XLabel("X");
        HeatmapPlot.Plot.YLabel("Y");
        HeatmapPlot.Refresh();
    }

    private void ResetSurface()
    {
        _surfaceDataGroup.Children.Clear();
        if (_scanPlan is null)
        {
            return;
        }

        (double spanX, double spanZ) = GetSurfaceSpan();
        _surfaceDataGroup.Children.Add(CreateBasePlateModel(spanX, spanZ));
    }

    private void RebuildSurface()
    {
        if (_scanPlan is null)
        {
            return;
        }

        ResetSurface();

        double markerSize = Math.Max(Math.Min(_scanPlan.StepX, _scanPlan.StepY) * 0.28, 0.8);
        Dictionary<Color, MeshGeometry3D> buckets = new();
        ScanParameter parameter = CurrentRenderParameter;

        foreach (PointRowViewModel row in _pointRows)
        {
            AddMarkerToBuckets(buckets, row.X, row.Y, row.Voltage, markerSize);

            if (string.Equals(parameter.Channel2Mode, ScanParameter.ChannelModeAuxScan, StringComparison.OrdinalIgnoreCase))
            {
                AddMarkerToBuckets(buckets, row.X + parameter.Channel2OffsetX, row.Y + parameter.Channel2OffsetY, row.Channel2Voltage, markerSize);
            }

            if (string.Equals(parameter.Channel3Mode, ScanParameter.ChannelModeAuxScan, StringComparison.OrdinalIgnoreCase))
            {
                AddMarkerToBuckets(buckets, row.X + parameter.Channel3OffsetX, row.Y + parameter.Channel3OffsetY, row.Channel3Voltage, markerSize);
            }

            if (string.Equals(parameter.Channel4Mode, ScanParameter.ChannelModeAuxScan, StringComparison.OrdinalIgnoreCase))
            {
                AddMarkerToBuckets(buckets, row.X + parameter.Channel4OffsetX, row.Y + parameter.Channel4OffsetY, row.Channel4Voltage, markerSize);
            }
        }

        foreach ((Color color, MeshGeometry3D mesh) in buckets)
        {
            if (mesh.Positions.Count == 0)
            {
                continue;
            }

            _surfaceDataGroup.Children.Add(CreateColoredModel(mesh, color));
        }
    }

    private void AddMarkerToBuckets(Dictionary<Color, MeshGeometry3D> buckets, double x, double y, double voltage, double markerSize)
    {
        if (double.IsNaN(voltage) || voltage < 0)
        {
            return;
        }

        Color color = QuantizeSurfaceColor(voltage / SurfaceVoltageMax);
        if (!buckets.TryGetValue(color, out MeshGeometry3D? mesh))
        {
            mesh = new MeshGeometry3D();
            buckets.Add(color, mesh);
        }

        double centeredX = x - (_scanPlan.MinX + _scanPlan.MaxX / 2.0);
        double centeredZ = y - (_scanPlan.MinY + _scanPlan.MaxY / 2.0);
        double height = NormalizeVoltage(voltage) * SurfaceHeightUnit;
        AddCubeToMesh(mesh, centeredX, centeredZ, height, markerSize);
    }

    private void UpdateSurfaceTransform()
    {
        if (_surfaceHeightScale is null)
        {
            return;
        }

        _surfaceHeightScale.ScaleY = HeightScaleSlider.Value;
    }

    private (double SpanX, double SpanZ) GetSurfaceSpan()
    {
        double spanX = Math.Max(_scanPlan?.MaxX ?? 100, 60);
        double spanZ = Math.Max(_scanPlan?.MaxY ?? 80, 60);
        return (spanX, spanZ);
    }

    private double GetRawGridVoltage(int row, int column)
    {
        if (_heatmapData is null)
        {
            return double.NaN;
        }

        return _heatmapData[row, column];
    }

    private void UpdateHeatmapCells(PointRowViewModel row)
    {
        if (_scanPlan is null || _heatmapData is null || _scanPlan.StepX <= 0 || _scanPlan.StepY <= 0)
        {
            return;
        }

        SetHeatmapCell(row.X, row.Y, row.Voltage);

        ScanParameter parameter = CurrentRenderParameter;
        if (string.Equals(parameter.Channel2Mode, ScanParameter.ChannelModeAuxScan, StringComparison.OrdinalIgnoreCase))
        {
            SetHeatmapCell(row.X + parameter.Channel2OffsetX, row.Y + parameter.Channel2OffsetY, row.Channel2Voltage);
        }

        if (string.Equals(parameter.Channel3Mode, ScanParameter.ChannelModeAuxScan, StringComparison.OrdinalIgnoreCase))
        {
            SetHeatmapCell(row.X + parameter.Channel3OffsetX, row.Y + parameter.Channel3OffsetY, row.Channel3Voltage);
        }

        if (string.Equals(parameter.Channel4Mode, ScanParameter.ChannelModeAuxScan, StringComparison.OrdinalIgnoreCase))
        {
            SetHeatmapCell(row.X + parameter.Channel4OffsetX, row.Y + parameter.Channel4OffsetY, row.Channel4Voltage);
        }
    }

    private void SetHeatmapCell(double x, double y, double voltage)
    {
        if (_scanPlan is null || _heatmapData is null || _scanPlan.StepX <= 0 || _scanPlan.StepY <= 0)
        {
            return;
        }

        int column = (int)Math.Round((x - _scanPlan.MinX) / _scanPlan.StepX);
        int row = (int)Math.Round((y - _scanPlan.MinY) / _scanPlan.StepY);

        if (row >= 0 && row < _heatmapData.GetLength(0) &&
            column >= 0 && column < _heatmapData.GetLength(1))
        {
            _heatmapData[row, column] = voltage;
        }
    }

    private static double AverageValidValues(params double[] values)
    {
        double[] valid = values.Where(value => !double.IsNaN(value) && value >= 0).ToArray();
        return valid.Length == 0 ? -1 : valid.Average();
    }

    private static double NormalizeVoltage(double value)
    {
        return double.IsNaN(value) || value < 0 ? 0 : value;
    }

    private static void AddCubeToMesh(MeshGeometry3D mesh, double centerX, double centerZ, double height, double size)
    {
        double half = size / 2.0;
        double baseY = -1.1;
        double topY = Math.Max(baseY + size * 0.7, height);

        Point3D p000 = new(centerX - half, baseY, centerZ - half);
        Point3D p100 = new(centerX + half, baseY, centerZ - half);
        Point3D p110 = new(centerX + half, baseY, centerZ + half);
        Point3D p010 = new(centerX - half, baseY, centerZ + half);
        Point3D p001 = new(centerX - half, topY, centerZ - half);
        Point3D p101 = new(centerX + half, topY, centerZ - half);
        Point3D p111 = new(centerX + half, topY, centerZ + half);
        Point3D p011 = new(centerX - half, topY, centerZ + half);

        AddQuad(mesh, p001, p101, p111, p011);
        AddQuad(mesh, p000, p100, p101, p001);
        AddQuad(mesh, p100, p110, p111, p101);
        AddQuad(mesh, p110, p010, p011, p111);
        AddQuad(mesh, p010, p000, p001, p011);
    }

    private static GeometryModel3D CreateColoredModel(MeshGeometry3D mesh, Color color)
    {
        SolidColorBrush diffuseBrush = new(color);
        SolidColorBrush specularBrush = new(Color.FromArgb(180, 255, 255, 255));

        MaterialGroup material = new();
        material.Children.Add(new DiffuseMaterial(diffuseBrush));
        material.Children.Add(new SpecularMaterial(specularBrush, 36));

        return new GeometryModel3D
        {
            Geometry = mesh,
            Material = material,
            BackMaterial = material,
        };
    }

    private static void AddQuad(MeshGeometry3D mesh, Point3D a, Point3D b, Point3D c, Point3D d)
    {
        int start = mesh.Positions.Count;
        mesh.Positions.Add(a);
        mesh.Positions.Add(b);
        mesh.Positions.Add(c);
        mesh.Positions.Add(a);
        mesh.Positions.Add(c);
        mesh.Positions.Add(d);

        mesh.TriangleIndices.Add(start + 0);
        mesh.TriangleIndices.Add(start + 1);
        mesh.TriangleIndices.Add(start + 2);
        mesh.TriangleIndices.Add(start + 3);
        mesh.TriangleIndices.Add(start + 4);
        mesh.TriangleIndices.Add(start + 5);

        Vector3D normal1 = CalculateNormal(a, b, c);
        Vector3D normal2 = CalculateNormal(a, c, d);
        mesh.Normals.Add(normal1);
        mesh.Normals.Add(normal1);
        mesh.Normals.Add(normal1);
        mesh.Normals.Add(normal2);
        mesh.Normals.Add(normal2);
        mesh.Normals.Add(normal2);
    }

    private GeometryModel3D CreateBasePlateModel(double spanX, double spanZ)
    {
        double halfX = spanX / 2.0;
        double halfZ = spanZ / 2.0;
        Point3D p00 = new(-halfX, -1.4, -halfZ);
        Point3D p10 = new(halfX, -1.4, -halfZ);
        Point3D p11 = new(halfX, -1.4, halfZ);
        Point3D p01 = new(-halfX, -1.4, halfZ);
        return CreateCellModel(p00, p10, p11, p01, Color.FromRgb(32, 43, 54), 0.72);
    }

    private GeometryModel3D CreateCellModel(Point3D p00, Point3D p10, Point3D p11, Point3D p01, Color color, double opacity = 1.0)
    {
        MeshGeometry3D mesh = new()
        {
            Positions = new Point3DCollection { p00, p10, p11, p00, p11, p01 },
            TriangleIndices = new Int32Collection { 0, 1, 2, 3, 4, 5 },
        };

        Vector3D normal1 = CalculateNormal(p00, p10, p11);
        Vector3D normal2 = CalculateNormal(p00, p11, p01);
        mesh.Normals = new Vector3DCollection
        {
            normal1, normal1, normal1,
            normal2, normal2, normal2,
        };

        SolidColorBrush diffuseBrush = new(Color.FromArgb((byte)(255 * opacity), color.R, color.G, color.B));
        SolidColorBrush specularBrush = new(Color.FromArgb((byte)(160 * opacity), 255, 255, 255));

        MaterialGroup material = new();
        material.Children.Add(new DiffuseMaterial(diffuseBrush));
        material.Children.Add(new SpecularMaterial(specularBrush, 28));

        return new GeometryModel3D
        {
            Geometry = mesh,
            Material = material,
            BackMaterial = material,
        };
    }

    private static Vector3D CalculateNormal(Point3D a, Point3D b, Point3D c)
    {
        Vector3D u = b - a;
        Vector3D v = c - a;
        Vector3D normal = Vector3D.CrossProduct(u, v);
        if (normal.LengthSquared < 0.0001)
        {
            return new Vector3D(0, 1, 0);
        }

        normal.Normalize();
        return normal;
    }

    private static Color QuantizeSurfaceColor(double ratio)
    {
        ratio = Math.Clamp(ratio, 0, 1);
        int bucket = (int)Math.Round(ratio * (SurfaceColorBuckets - 1));
        return InterpolateSurfaceColor(bucket / (double)(SurfaceColorBuckets - 1));
    }

    private static Color InterpolateSurfaceColor(double ratio)
    {
        ratio = Math.Clamp(ratio, 0, 1);
        Color[] palette =
        {
            Color.FromRgb(46, 85, 164),
            Color.FromRgb(63, 167, 214),
            Color.FromRgb(86, 205, 168),
            Color.FromRgb(238, 202, 91),
            Color.FromRgb(227, 109, 88),
        };

        double scaled = ratio * (palette.Length - 1);
        int index = Math.Min((int)scaled, palette.Length - 2);
        double local = scaled - index;

        Color start = palette[index];
        Color end = palette[index + 1];

        byte r = (byte)(start.R + (end.R - start.R) * local);
        byte g = (byte)(start.G + (end.G - start.G) * local);
        byte b = (byte)(start.B + (end.B - start.B) * local);
        return Color.FromRgb(r, g, b);
    }

    private static SerialPort BuildSerialPort(string portName, int baudRate)
    {
        return new SerialPort(portName, baudRate)
        {
            Encoding = Encoding.ASCII,
            NewLine = "\r\n",
            ReadTimeout = 300,
            WriteTimeout = 300,
        };
    }

    private bool EnsureConnected()
    {
        if (_controlPort?.IsOpen == true && _dataPort?.IsOpen == true)
        {
            return true;
        }

        MessageBox.Show("请先连接控制串口和传感串口。", "未连接", MessageBoxButton.OK, MessageBoxImage.Warning);
        return false;
    }

    private static double ParseDouble(string input, string fieldName)
    {
        if (double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out double invariantValue))
        {
            return invariantValue;
        }

        if (double.TryParse(input, out double localValue))
        {
            return localValue;
        }

        throw new InvalidOperationException($"{fieldName} 不是有效数字。");
    }

    private static int ParseInt(string input, string fieldName)
    {
        if (int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture, out int invariantValue))
        {
            return invariantValue;
        }

        if (int.TryParse(input, out int localValue))
        {
            return localValue;
        }

        throw new InvalidOperationException($"{fieldName} 不是有效整数。");
    }

    private ScanParameter ReadScanParameterFromUi()
    {
        string filterAlgorithm = (FilterAlgorithmComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString()
            ?? ScanParameter.AverageFilter;
        string shape = (ShapeComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Rectangle";
        return new ScanParameter
        {
            MaxLimitX = ParseDouble(MaxXTextBox.Text, "X range"),
            MaxLimitY = ParseDouble(MaxYTextBox.Text, "Y range"),
            StepX = ParseInt(StepXTextBox.Text, "X step"),
            StepY = ParseInt(StepYTextBox.Text, "Y step"),
            Speed = ParseDouble(SpeedTextBox.Text, "Speed"),
            Shape = shape,
            Radius = ParseDouble(RadiusTextBox.Text, "Radius"),
            FilterAlgorithm = filterAlgorithm,
            Channel2Mode = ReadAuxChannelMode(Channel2ModeComboBox),
            Channel3Mode = ReadAuxChannelMode(Channel3ModeComboBox),
            Channel4Mode = ReadAuxChannelMode(Channel4ModeComboBox),
            Channel2OffsetX = ParseDouble(Channel2OffsetXTextBox.Text, "CH2 X offset"),
            Channel2OffsetY = ParseDouble(Channel2OffsetYTextBox.Text, "CH2 Y offset"),
            Channel3OffsetX = ParseDouble(Channel3OffsetXTextBox.Text, "CH3 X offset"),
            Channel3OffsetY = ParseDouble(Channel3OffsetYTextBox.Text, "CH3 Y offset"),
            Channel4OffsetX = ParseDouble(Channel4OffsetXTextBox.Text, "CH4 X offset"),
            Channel4OffsetY = ParseDouble(Channel4OffsetYTextBox.Text, "CH4 Y offset"),
        };
    }

    private void ApplyScanParameterToUi(ScanParameter parameter)
    {
        MaxXTextBox.Text = parameter.MaxLimitX.ToString(CultureInfo.InvariantCulture);
        MaxYTextBox.Text = parameter.MaxLimitY.ToString(CultureInfo.InvariantCulture);
        StepXTextBox.Text = parameter.StepX.ToString(CultureInfo.InvariantCulture);
        StepYTextBox.Text = parameter.StepY.ToString(CultureInfo.InvariantCulture);
        SpeedTextBox.Text = parameter.Speed.ToString(CultureInfo.InvariantCulture);
        RadiusTextBox.Text = parameter.Radius.ToString(CultureInfo.InvariantCulture);
        string filterAlgorithm = string.IsNullOrWhiteSpace(parameter.FilterAlgorithm)
            ? ScanParameter.AverageFilter
            : parameter.FilterAlgorithm;

        foreach (ComboBoxItem item in FilterAlgorithmComboBox.Items)
        {
            if (string.Equals(item.Content?.ToString(), filterAlgorithm, StringComparison.OrdinalIgnoreCase))
            {
                FilterAlgorithmComboBox.SelectedItem = item;
                break;
            }
        }

        string targetShape = parameter.Shape.Contains("Circle", StringComparison.OrdinalIgnoreCase)
            ? "Circle"
            : "Rectangle";

        foreach (ComboBoxItem item in ShapeComboBox.Items)
        {
            if (string.Equals(item.Content?.ToString(), targetShape, StringComparison.OrdinalIgnoreCase))
            {
                ShapeComboBox.SelectedItem = item;
                break;
            }
        }

        SetAuxChannelMode(Channel2ModeComboBox, parameter.Channel2Mode);
        SetAuxChannelMode(Channel3ModeComboBox, parameter.Channel3Mode);
        SetAuxChannelMode(Channel4ModeComboBox, parameter.Channel4Mode);
        Channel2OffsetXTextBox.Text = parameter.Channel2OffsetX.ToString(CultureInfo.InvariantCulture);
        Channel2OffsetYTextBox.Text = parameter.Channel2OffsetY.ToString(CultureInfo.InvariantCulture);
        Channel3OffsetXTextBox.Text = parameter.Channel3OffsetX.ToString(CultureInfo.InvariantCulture);
        Channel3OffsetYTextBox.Text = parameter.Channel3OffsetY.ToString(CultureInfo.InvariantCulture);
        Channel4OffsetXTextBox.Text = parameter.Channel4OffsetX.ToString(CultureInfo.InvariantCulture);
        Channel4OffsetYTextBox.Text = parameter.Channel4OffsetY.ToString(CultureInfo.InvariantCulture);
    }

    private ScanParameter CloneScanParameter(ScanParameter parameter)
    {
        return new ScanParameter
        {
            MaxLimitX = parameter.MaxLimitX,
            MaxLimitY = parameter.MaxLimitY,
            StepX = parameter.StepX,
            StepY = parameter.StepY,
            Speed = parameter.Speed,
            Shape = parameter.Shape,
            Radius = parameter.Radius,
            FilterAlgorithm = parameter.FilterAlgorithm,
            Channel2Mode = parameter.Channel2Mode,
            Channel3Mode = parameter.Channel3Mode,
            Channel4Mode = parameter.Channel4Mode,
            Channel2OffsetX = parameter.Channel2OffsetX,
            Channel2OffsetY = parameter.Channel2OffsetY,
            Channel3OffsetX = parameter.Channel3OffsetX,
            Channel3OffsetY = parameter.Channel3OffsetY,
            Channel4OffsetX = parameter.Channel4OffsetX,
            Channel4OffsetY = parameter.Channel4OffsetY,
        };
    }

    private static string ReadAuxChannelMode(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is string tag &&
            !string.IsNullOrWhiteSpace(tag))
        {
            return tag;
        }

        return ScanParameter.ChannelModeOff;
    }

    private static void SetAuxChannelMode(ComboBox comboBox, string? mode)
    {
        string targetMode = string.IsNullOrWhiteSpace(mode) ? ScanParameter.ChannelModeOff : mode;

        foreach (ComboBoxItem item in comboBox.Items)
        {
            if (string.Equals(item.Tag?.ToString(), targetMode, StringComparison.OrdinalIgnoreCase))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }

        if (comboBox.Items.Count > 0)
        {
            comboBox.SelectedIndex = 0;
        }
    }
private void LoadPresetList(string? selectPresetName = null)
    {
        try
        {
            _scanPresets.Clear();

            foreach (ScanParameterPreset preset in _presetStore.LoadPresets())
            {
                _scanPresets.Add(preset);
            }

            if (_scanPresets.Count == 0)
            {
                PresetComboBox.SelectedIndex = -1;
                return;
            }

            ScanParameterPreset? targetPreset = null;
            if (!string.IsNullOrWhiteSpace(selectPresetName))
            {
                targetPreset = _scanPresets.FirstOrDefault(item =>
                    string.Equals(item.Name, selectPresetName, StringComparison.CurrentCultureIgnoreCase));
            }

            PresetComboBox.SelectedItem = targetPreset ?? _scanPresets[0];
        }
        catch (Exception ex)
        {
            _scanPresets.Clear();
            PresetComboBox.SelectedIndex = -1;
            AppendLog($"读取预设参数组失败: {ex.Message}");
        }
    }

    private ScanParameterPreset? GetSelectedPreset()
    {
        return PresetComboBox.SelectedItem as ScanParameterPreset;
    }

    private ScanParameterPreset BuildPresetFromUi(string presetName)
    {
        return new ScanParameterPreset
        {
            Name = presetName,
            UpdatedAt = DateTime.Now,
            Parameter = CloneScanParameter(ReadScanParameterFromUi()),
        };
    }

    private bool TryLoadSelectedPresetIntoUi(out ScanParameterPreset? preset)
    {
        preset = GetSelectedPreset();
        if (preset is null)
        {
            MessageBox.Show("请先选择一个已保存的参数组。", "未选择参数组", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        ApplyScanParameterToUi(preset.Parameter);
        PresetNameTextBox.Text = preset.Name;
        AppendLog($"已加载预设参数组：{preset.Name}");
        return true;
    }

    private void StartScan(ScanParameter scanParameter)
    {
        _scanController.RaiseCommand = RaiseCommandTextBox.Text.Trim();
        _scanController.DropCommand = DropCommandTextBox.Text.Trim();
        _scanController.HoldCommand = HoldCommandTextBox.Text.Trim();
        _scanController.FilterAlgorithm = scanParameter.FilterAlgorithm;
        _lastScanParameter = CloneScanParameter(scanParameter);
        _scanController.StartScan(scanParameter);
        NavigationListBox.SelectedIndex = 1;
        AppendLog("扫描任务已启动。");
    }

    private void AppendLog(string message)
    {
        LogTextBox.AppendText(message + Environment.NewLine);

        if (LogTextBox.LineCount > MaxLogLines)
        {
            int firstKeptLine = LogTextBox.LineCount - MaxLogLines;
            int trimIndex = LogTextBox.GetCharacterIndexFromLineIndex(firstKeptLine);
            if (trimIndex > 0)
            {
                LogTextBox.Text = LogTextBox.Text[trimIndex..];
            }
        }

        LogTextBox.ScrollToEnd();
    }

    private void NavigationListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavigationListBox.SelectedItem is not ListBoxItem selectedItem ||
            selectedItem.Tag is not string pageKey)
        {
            return;
        }

        SetActivePage(pageKey);
    }

    private void SetActivePage(string pageKey)
    {
        SetupPage.Visibility = pageKey == "Setup" ? Visibility.Visible : Visibility.Collapsed;
        MonitorPage.Visibility = pageKey == "Monitor" ? Visibility.Visible : Visibility.Collapsed;
        DataPage.Visibility = pageKey == "Data" ? Visibility.Visible : Visibility.Collapsed;

        switch (pageKey)
        {
            case "Monitor":
                CurrentPageTitleTextBlock.Text = "扫描监控与图形";
                CurrentPageHintTextBlock.Text = "查看扫描进度、热力图和 3D 散点结果。";
                break;
            case "Data":
                CurrentPageTitleTextBlock.Text = "数据与日志";
                CurrentPageHintTextBlock.Text = "查看点位明细、扫描日志，并从这里导出 CSV。";
                break;
            default:
                CurrentPageTitleTextBlock.Text = "设备与参数";
                CurrentPageHintTextBlock.Text = "先连接控制串口和传感串口，再设置扫描范围、步长和设备命令。";
                break;
        }
    }

    private void SurfaceControl_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        UpdateSurfaceTransform();
    }

    private void SavePresetButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string presetName = PresetNameTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(presetName))
            {
                MessageBox.Show("请先输入参数组名称。", "名称为空", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool exists = _scanPresets.Any(item =>
                string.Equals(item.Name, presetName, StringComparison.CurrentCultureIgnoreCase));

            if (exists)
            {
                MessageBoxResult overwrite = MessageBox.Show(
                    $"参数组“{presetName}”已存在，是否覆盖？",
                    "纭瑕嗙洊",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (overwrite != MessageBoxResult.Yes)
                {
                    return;
                }
            }

            _presetStore.SavePreset(BuildPresetFromUi(presetName));
            LoadPresetList(presetName);
            AppendLog($"已保存预设参数组：{presetName}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "保存失败", MessageBoxButton.OK, MessageBoxImage.Error);
            AppendLog($"保存预设参数组失败: {ex.Message}");
        }
    }

    private void LoadPresetButton_Click(object sender, RoutedEventArgs e)
    {
        TryLoadSelectedPresetIntoUi(out _);
    }

    private void UsePresetScanButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!EnsureConnected())
            {
                return;
            }

            if (!TryLoadSelectedPresetIntoUi(out ScanParameterPreset? preset) || preset is null)
            {
                return;
            }

            StartScan(CloneScanParameter(preset.Parameter));
            AppendLog($"已使用预设参数组开始扫描：{preset.Name}");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            AppendLog($"使用预设参数组扫描失败: {ex.Message}");
        }
    }

    private void DeletePresetButton_Click(object sender, RoutedEventArgs e)
    {
        ScanParameterPreset? preset = GetSelectedPreset();
        if (preset is null)
        {
            MessageBox.Show("请先选择要删除的参数组。", "未选择参数组", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        MessageBoxResult result = MessageBox.Show(
            $"确定删除参数组“{preset.Name}”吗？",
            "删除参数组",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _presetStore.DeletePreset(preset.Name);
        LoadPresetList();
        PresetNameTextBox.Text = string.Empty;
        AppendLog($"已删除预设参数组：{preset.Name}");
    }

    private void RefreshPortsButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshPortList();
        AppendLog("串口列表已刷新。");
    }

    private void ConnectButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string? controlPortName = ControlPortComboBox.SelectedItem as string;
            string? dataPortName = DataPortComboBox.SelectedItem as string;

            if (string.IsNullOrWhiteSpace(controlPortName) || string.IsNullOrWhiteSpace(dataPortName))
            {
                throw new InvalidOperationException("请选择控制串口和传感串口。");
            }

            if (string.Equals(controlPortName, dataPortName, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("控制串口和传感串口不能是同一个端口。");
            }

            DisconnectInternal();

            int controlBaud = ParseInt(ControlBaudComboBox.Text, "控制波特率");
            int dataBaud = ParseInt(DataBaudComboBox.Text, "传感波特率");

            _controlPort = BuildSerialPort(controlPortName, controlBaud);
            _dataPort = BuildSerialPort(dataPortName, dataBaud);
            _controlPort.Open();
            _dataPort.Open();

            _scanController.AttachPorts(_controlPort, _dataPort);

            DeviceStatusTextBlock.Text = $"控制 {controlPortName} / 传感 {dataPortName} 已连接";
            AppendLog($"已连接控制串口 {controlPortName}，波特率 {controlBaud}。");
            AppendLog($"已连接传感串口 {dataPortName}，波特率 {dataBaud}。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "连接失败", MessageBoxButton.OK, MessageBoxImage.Error);
            AppendLog($"连接失败: {ex.Message}");
            DisconnectInternal();
        }
    }

    private void DisconnectButton_Click(object sender, RoutedEventArgs e)
    {
        DisconnectInternal();
        AppendLog("串口已断开。");
    }

    private void StartScanButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!EnsureConnected())
            {
                return;
            }

            StartScan(ReadScanParameterFromUi());
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            AppendLog($"启动扫描失败: {ex.Message}");
        }
    }

    private void StopScanButton_Click(object sender, RoutedEventArgs e)
    {
        _scanController.StopScan();
        AppendLog("已请求停止扫描。");
    }


    private void ResumeScanButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!EnsureConnected())
            {
                return;
            }

            _scanController.RaiseCommand = RaiseCommandTextBox.Text.Trim();
            _scanController.DropCommand = DropCommandTextBox.Text.Trim();
            _scanController.HoldCommand = HoldCommandTextBox.Text.Trim();

            if (_lastScanParameter is not null)
            {
                _scanController.FilterAlgorithm = _lastScanParameter.FilterAlgorithm;
            }

            _scanController.ResumeScan();
            NavigationListBox.SelectedIndex = 1;
            AppendLog("已从断点继续扫描。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "继续扫描失败", MessageBoxButton.OK, MessageBoxImage.Error);
            AppendLog($"继续扫描失败: {ex.Message}");
        }
    }
    private void ReturnToOriginButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!EnsureConnected())
            {
                return;
            }

            string raiseCommand = RaiseCommandTextBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(raiseCommand))
            {
                _scanController.SendControlCommand(raiseCommand);
            }

            string speedText = ParseDouble(SpeedTextBox.Text, "绉诲姩閫熷害").ToString("0.###", CultureInfo.InvariantCulture);
            _scanController.SendControlCommand($"$J=G90 X0 Y0 F{speedText}");
            AppendLog("已发送返回原点命令。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "返回原点失败", MessageBoxButton.OK, MessageBoxImage.Error);
            AppendLog($"返回原点失败: {ex.Message}");
        }
    }

    private void OpenAnalysisButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            IReadOnlyList<PointData> points = GetCurrentAnalysisPoints();
            if (points.Count == 0)
            {
                MessageBox.Show("当前没有可分析的数据。", "AI 数据分析", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DataAnalysisWindow window = new(points, _scanPlan, _lastScanParameter)
            {
                Owner = this,
            };
            window.Show();
            AppendLog("AI 数据分析窗口已打开。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "AI 数据分析", MessageBoxButton.OK, MessageBoxImage.Error);
            AppendLog($"AI 数据分析打开失败: {ex.Message}");
        }
    }

    private IReadOnlyList<PointData> GetCurrentAnalysisPoints()
    {
        IReadOnlyList<PointData> controllerPoints = _scanController.DataArray;
        if (controllerPoints.Count > 0)
        {
            return controllerPoints.ToArray();
        }

        return _pointRows
            .Select(row => new PointData
            {
                Order = row.Order,
                X = row.X,
                Y = row.Y,
                Voltage = row.Voltage,
            })
            .ToArray();
    }

    private void SaveCsvButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string savedPath = _scanController.SaveSimplePointData();
            AppendLog($"CSV 已导出到: {savedPath}");
            MessageBox.Show(savedPath, "导出成功", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "导出失败", MessageBoxButton.OK, MessageBoxImage.Error);
            AppendLog($"导出失败: {ex.Message}");
        }
    }

    private void SendRawCommandButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!EnsureConnected())
            {
                return;
            }

            string command = RawCommandTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(command))
            {
                return;
            }

            _scanController.SendControlCommand(command);
        }
        catch (Exception ex)
        {
            AppendLog($"手动命令发送失败: {ex.Message}");
        }
    }

    private void SendLogCommandButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!EnsureConnected())
            {
                return;
            }

            string command = LogCommandTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(command))
            {
                return;
            }

            if (SendTargetComboBox.SelectedIndex == 1)
            {
                _scanController.SendDataCommand(command);
            }
            else
            {
                _scanController.SendControlCommand(command);
            }
        }
        catch (Exception ex)
        {
            AppendLog($"串口发送失败: {ex.Message}");
        }
    }

    private void ClearLogButton_Click(object sender, RoutedEventArgs e)
    {
        LogTextBox.Clear();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        DisconnectInternal();
        _scanController.Dispose();
    }

    private void DisconnectInternal()
    {
        try
        {
            _scanController.StopScan();
            _scanController.DetachPorts();
        }
        catch
        {
        }

        ClosePort(ref _controlPort);
        ClosePort(ref _dataPort);
        DeviceStatusTextBlock.Text = "未连接串口";
    }

    private static void ClosePort(ref SerialPort? port)
    {
        if (port is null)
        {
            return;
        }

        try
        {
            if (port.IsOpen)
            {
                port.Close();
            }
        }
        catch
        {
        }
        finally
        {
            port.Dispose();
            port = null;
        }
    }
}
