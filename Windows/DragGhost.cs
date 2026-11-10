using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace QuickPanel.Windows;

/// <summary>
/// v1.19：拖拽幽灵窗口 —— 拖动时跟随光标显示半透明图标预览。
/// 用 WS_EX_TRANSPARENT 让鼠标事件穿透，不干扰 DragDrop。
/// </summary>
public sealed class DragGhost : IDisposable
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    private Window? _window;
    private DispatcherTimer? _timer;
    private readonly double _size;

    public DragGhost(ImageSource? icon, string name, double size = 64)
    {
        _size = size;

        var content = new Border
        {
            Width = size,
            Height = size,
            CornerRadius = new CornerRadius(size * 0.22),
            Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            Child = new Image
            {
                Source = icon,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(size * 0.13),
            },
        };
        RenderOptions.SetBitmapScalingMode(content, BitmapScalingMode.HighQuality);

        // 柔和外发光，让幽灵在任意背景上都有轮廓
        content.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            Color = Colors.Black,
            BlurRadius = 14,
            ShadowDepth = 3,
            Opacity = 0.55,
        };

        _window = new Window
        {
            WindowStyle = WindowStyle.None,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            IsHitTestVisible = false,
            Content = content,
            Title = name,
        };

        _window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(_window).Handle;
            var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE,
                ex | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        };

        _window.Show();

        // 入场动画：从小放大 + 淡入
        _window.Opacity = 0.0;
        var scale = new ScaleTransform(0.75, 0.75);
        content.RenderTransform = scale;
        content.RenderTransformOrigin = new Point(0.5, 0.5);

        var ease = new System.Windows.Media.Animation.CubicEase
        {
            EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut,
        };
        _window.BeginAnimation(UIElement.OpacityProperty,
            new System.Windows.Media.Animation.DoubleAnimation(0.92, TimeSpan.FromMilliseconds(140))
            { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new System.Windows.Media.Animation.DoubleAnimation(1.0, TimeSpan.FromMilliseconds(180))
            { EasingFunction = ease });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty,
            new System.Windows.Media.Animation.DoubleAnimation(1.0, TimeSpan.FromMilliseconds(180))
            { EasingFunction = ease });

        // 跟随光标（30ms ≈ 33fps，足够顺滑且不吃 CPU）
        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16),
        };
        _timer.Tick += (_, _) => FollowCursor();
        _timer.Start();
        FollowCursor();
    }

    private void FollowCursor()
    {
        if (_window is null) return;
        if (!GetCursorPos(out var p)) return;

        // 用 WPF 的 DPI 换算，避免高分屏偏移
        var source = PresentationSource.FromVisual(_window);
        var scale = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var pt = scale.Transform(new Point(p.X, p.Y));

        _window.Left = pt.X - _window.ActualWidth / 2;
        _window.Top = pt.Y - _window.ActualHeight / 2;
    }

    public void Dispose()
    {
        _timer?.Stop();
        _timer = null;
        try { _window?.Close(); } catch { /* 忽略 */ }
        _window = null;
    }
}
