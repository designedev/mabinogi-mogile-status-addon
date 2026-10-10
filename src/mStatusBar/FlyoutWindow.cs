using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using mStatusBar.Core;

namespace mStatusBar;

internal class FlyoutWindow : Window
{
    private Border? chrome;
    private bool wrapping;
    private int dismissalHolds;
    private ScreenRect anchor;
    private double preferredWidth, preferredHeight;
    private int presentationGeneration;
    private readonly MouseHook mouseHook;
    private IntPtr hookHandle;
    internal bool OutsideClickMonitorActive => hookHandle != IntPtr.Zero;
    public bool HiddenBeforeShow { get; private set; }
    internal double PresentationOpacity => chrome?.Opacity ?? 1;
    public bool DismissOnDeactivate { get; set; } = true;
    public bool DismissalSuspended => dismissalHolds > 0;
    public ScreenRect ScreenBounds { get; private set; }
    public ScreenRect ActualScreenBounds
    {
        get { GetWindowRect(new WindowInteropHelper(this).Handle, out var rect); return new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top); }
    }
    public bool FitsWorkArea { get; private set; }

    public FlyoutWindow()
    {
        mouseHook = OnMouseHook;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false; Topmost = true; AllowsTransparency = true;
        Background = Brushes.Transparent; WindowStartupLocation = WindowStartupLocation.Manual;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && !DismissalSuspended) { Hide(); e.Handled = true; } };
        Deactivated += (_, _) =>
        {
            var generation = presentationGeneration;
            Dispatcher.InvokeAsync(() => { if (generation == presentationGeneration) DismissIfInactive(); }, DispatcherPriority.Background);
        };
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible && hookHandle == IntPtr.Zero) hookHandle = SetWindowsHookEx(14, mouseHook, GetModuleHandle(null), 0);
            else ReleaseMouseHook();
        };
        Closed += (_, _) => ReleaseMouseHook();
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        if (wrapping || newContent is not UIElement element) return;
        wrapping = true;
        Content = null;
        chrome = new Border { Child = element, Background = Ui.Background, CornerRadius = new CornerRadius(15),
            BorderBrush = new SolidColorBrush(Color.FromRgb(63, 78, 98)), BorderThickness = new Thickness(1),
            Padding = new Thickness(2), Margin = new Thickness(10),
            Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 18, ShadowDepth = 3, Opacity = 0.35 } };
        Content = chrome;
        wrapping = false;
    }

    public void Present(IntPtr anchorHandle)
    {
        presentationGeneration++;
        if (preferredWidth == 0) { preferredWidth = Width; preferredHeight = Height; }
        var monitor = MonitorFromWindow(anchorHandle, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info) || !GetWindowRect(anchorHandle, out var source))
            throw new InvalidOperationException("작업 표시줄 위치를 확인하지 못했습니다.");
        anchor = new(source.Left, source.Top, source.Right - source.Left, source.Bottom - source.Top);
        var work = new ScreenRect(info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left, info.Work.Bottom - info.Work.Top);
        var scale = Math.Max(1, GetDpiForWindow(anchorHandle)) / 96.0;
        ScreenBounds = FlyoutPlacement.Calculate(work, anchor, preferredWidth * scale, preferredHeight * scale, 6 * scale);
        MinWidth = MinHeight = 0;
        Width = ScreenBounds.Width / scale; Height = ScreenBounds.Height / scale;
        // Prepare the first frame while hidden. Native positioning must not show or activate
        // the HWND before WPF has laid out the transparent content.
        var movement = new TranslateTransform(0, 12);
        if (chrome is not null)
        {
            chrome.BeginAnimation(OpacityProperty, null);
            chrome.Opacity = 0;
            chrome.RenderTransform = movement;
        }
        var handle = new WindowInteropHelper(this).EnsureHandle();
        SetWindowPos(handle, new IntPtr(-1), (int)ScreenBounds.Left, (int)ScreenBounds.Top, (int)ScreenBounds.Width, (int)ScreenBounds.Height, 0x0010);
        HiddenBeforeShow = !IsWindowVisible(handle);
        Show();
        SetWindowPos(handle, new IntPtr(-1), (int)ScreenBounds.Left, (int)ScreenBounds.Top, (int)ScreenBounds.Width, (int)ScreenBounds.Height, 0x0010);
        UpdateLayout();
        var actual = ActualScreenBounds;
        FitsWorkArea = actual.Left >= work.Left && actual.Top >= work.Top && actual.Right <= work.Right + 1 && actual.Bottom <= work.Bottom + 1;
        Activate();
        if (chrome is not null)
        {
            chrome.Opacity = 1;
            movement.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            chrome.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        }
    }

    internal void DismissIfInactive(bool respectAnchor = true)
    {
        if (!DismissOnDeactivate || DismissalSuspended || !IsVisible || IsActive) return;
        if (respectAnchor && GetCursorPos(out var point) && anchor.Contains(point.X, point.Y)) return;
        Hide();
    }
    private IntPtr OnMouseHook(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && message.ToInt64() is 0x0201 or 0x0204 or 0x0207 or 0x020B)
        {
            var point = Marshal.PtrToStructure<Point>(data);
            ProcessPointerDown(point.X, point.Y);
        }
        return CallNextHookEx(hookHandle, code, message, data);
    }
    internal void ProcessPointerDown(int x, int y)
    {
        if (!DismissOnDeactivate || DismissalSuspended || !IsVisible || ActualScreenBounds.Contains(x, y) || anchor.Contains(x, y)) return;
        var generation = presentationGeneration;
        Dispatcher.InvokeAsync(() =>
        {
            if (generation == presentationGeneration && IsVisible && !DismissalSuspended) Hide();
        }, DispatcherPriority.Input);
    }
    private void ReleaseMouseHook()
    {
        if (hookHandle == IntPtr.Zero) return;
        UnhookWindowsHookEx(hookHandle); hookHandle = IntPtr.Zero;
    }
    public IDisposable SuspendDismissal()
    {
        dismissalHolds++;
        return new DismissalHold(this);
    }
    private sealed class DismissalHold(FlyoutWindow owner) : IDisposable
    {
        private bool disposed;
        public void Dispose() { if (disposed) return; disposed = true; owner.dismissalHolds--; if (owner.IsVisible) owner.Activate(); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public int Flags; }
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    private delegate IntPtr MouseHook(int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int hook, MouseHook callback, IntPtr module, uint thread);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
