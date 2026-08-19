using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Devices.Enumeration;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.Capture;

namespace UpperMachine;

public partial class CameraWindow : Window
{
    private MediaCapture? _mediaCapture;
    private CancellationTokenSource? _previewCts;
    private WriteableBitmap? _writeableBitmap;

    public CameraWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadCamerasAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        StopPreview();
        base.OnClosed(e);
    }

    private async Task LoadCamerasAsync()
    {
        try
        {
            var devices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            CameraComboBox.Items.Clear();
            foreach (DeviceInformation device in devices)
            {
                CameraComboBox.Items.Add(new CameraItem(device.Id, device.Name));
            }

            CameraComboBox.SelectedIndex = devices.Count > 0 ? 0 : -1;
            StatusTextBlock.Text = devices.Count > 0
                ? $"已检测到 {devices.Count} 个摄像头，点击“启动”开始预览。"
                : "未检测到摄像头。";
        }
        catch (Exception ex)
        {
            StatusTextBlock.Text = $"枚举摄像头失败：{ex.Message}";
        }
    }

    private async void StartStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mediaCapture is not null)
        {
            StopPreview();
            return;
        }

        await StartPreviewAsync();
    }

    private async Task StartPreviewAsync()
    {
        string deviceId = (CameraComboBox.SelectedItem as CameraItem)?.Id ?? string.Empty;

        try
        {
            StopPreview();

            MediaCapture capture = new();
            MediaCaptureInitializationSettings settings = new()
            {
                StreamingCaptureMode = StreamingCaptureMode.Video,
                VideoDeviceId = deviceId,
                // 用 CPU 内存帧，便于直接拿到 SoftwareBitmap 绘制到 WPF Image。
                MemoryPreference = MediaCaptureMemoryPreference.Cpu,
            };

            await capture.InitializeAsync(settings);
            await capture.StartPreviewAsync();

            _mediaCapture = capture;
            _previewCts = new CancellationTokenSource();
            _ = PreviewLoopAsync(_previewCts.Token);

            StartStopButton.Content = "停止";
            StatusTextBlock.Text = "预览中…";
        }
        catch (Exception ex)
        {
            StopPreview();
            StatusTextBlock.Text = $"启动摄像头失败：{ex.Message}";
        }
    }

    private void StopPreview()
    {
        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = null;

        MediaCapture? capture = _mediaCapture;
        _mediaCapture = null;
        try
        {
            capture?.Dispose();
        }
        catch
        {
            // 忽略释放异常
        }

        StartStopButton.Content = "启动";
    }

    private async Task PreviewLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            MediaCapture? capture = _mediaCapture;
            if (capture is null)
            {
                break;
            }

            try
            {
                using VideoFrame frame = await capture.GetPreviewFrameAsync();
                SoftwareBitmap? source = frame.SoftwareBitmap;
                if (source is null)
                {
                    await Task.Delay(30, ct);
                    continue;
                }

                using SoftwareBitmap converted = SoftwareBitmap.Convert(source, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                int width = converted.PixelWidth;
                int height = converted.PixelHeight;
                byte[] pixels = new byte[4 * width * height];
                converted.CopyToBuffer(pixels.AsBuffer());

                if (_writeableBitmap is null || _writeableBitmap.PixelWidth != width || _writeableBitmap.PixelHeight != height)
                {
                    _writeableBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
                    PreviewImage.Source = _writeableBitmap;
                }

                _writeableBitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // 摄像头断开或读取失败，停止预览循环
                break;
            }

            try
            {
                await Task.Delay(30, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e) => Close();

    private sealed class CameraItem
    {
        public CameraItem(string id, string name)
        {
            Id = id;
            Name = name;
        }

        public string Id { get; }

        public string Name { get; }

        public override string ToString() => Name;
    }
}
