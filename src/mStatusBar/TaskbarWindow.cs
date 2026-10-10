using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Interop;
using Deskband11Lib.Wpf;
using Deskband11Lib.Core;
using mStatusBar.Core;
namespace mStatusBar;
internal sealed class TaskbarWindow : Window
{
    private static readonly Brush NearbyForeground = new SolidColorBrush(Color.FromRgb(184, 188, 194));
    public TaskbarContentHost Host { get; }
    public TaskbarPlacement Placement { get; }
    private double? centerTargetX;
    internal bool CenterPlacementVerified => Placement == TaskbarPlacement.Center && centerTargetX is { } target && GetWindowRect(new WindowInteropHelper(this).Handle, out var bounds) && GetWindowRect(GetParent(new WindowInteropHelper(this).Handle), out var parent) && Math.Abs(bounds.Left - parent.Left - target) <= 1;
    private readonly TextBlock activity, running, completed;
    private readonly HealthBarVisual healthBar = new();
    private readonly WeatherVisual weatherIcon = new();
    private readonly TextBlock erinnTime;
    private readonly Border environmentRegion;
    private readonly System.Windows.Controls.Button queueButton;
    internal double HealthRatio => healthBar.Ratio;
    internal bool HealthAlertAnimating => healthBar.AlertAnimating;
    internal double HealthAlertOpacity => healthBar.Opacity;
    internal Color HealthFillColor => healthBar.FillColor;
    internal string EnvironmentText => erinnTime.Text;
    internal string WeatherKind => weatherIcon.Kind;
    internal double QueueButtonWidth => queueButton.ActualWidth;
    internal double ContentSpaceWidth => actionRegion.ActualWidth;
    private readonly MarqueeText music;
    private readonly MusicEffectVisual musicEffect = new();
    private readonly Border background;
    private bool musicPlaying;
    private bool nearbyPlaying;
    private AppSettings appearance;
    private StatusSnapshot? latest;
    private readonly DispatcherTimer playbackTimer;
    private readonly TextBlock playbackTime;
    private readonly ProgressBar playbackProgress;
    private readonly Border actionRegion;
    private readonly Grid musicRegion;
    internal string PlaybackText => playbackTime.Text;
    internal double PlaybackProgress => playbackProgress.Value;
    internal bool PlaybackVisible => playbackTime.Visibility == Visibility.Visible;
    internal bool EffectAnimating => musicEffect.IsAnimating;
    internal double EffectPhase => musicEffect.Phase;
    internal MusicEffect SelectedEffect => musicEffect.SelectedEffect;
    internal bool NearbyDisplayed => nearbyPlaying && musicEffect.IsNearby;
    internal bool MusicIsMuted => music.TextBrush == NearbyForeground;
    internal Color BackgroundColor => ((SolidColorBrush)background.Background).Color;
    public string SummaryText => $"{activity.Text}\n진행 {running.Text} · 완료 {completed.Text}\n{music.Text}";
    public bool MusicFlowing => music.IsFlowing;
    public double MusicOffset => music.Offset;
    public bool IsOnRight
    {
        get { var handle = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            return GetWindowRect(handle, out var window) && GetWindowRect(GetParent(handle), out var parent) && window.Right > parent.Left + (parent.Right - parent.Left) / 2 && window.Right - window.Left > 1; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    public string ParentClass { get { var name = new StringBuilder(256); GetClassName(GetParent(new System.Windows.Interop.WindowInteropHelper(this).Handle), name, name.Capacity); return name.ToString(); } }
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);
    public TaskbarWindow(App app)
    {
        appearance = app.Settings;
        Title = "mStatusBar"; Width = 480; Height = 48;
        WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; AllowsTransparency = true;
        // A zero-alpha layered-window pixel passes mouse input through before WPF receives it.
        // Keep a virtually invisible, full-client input backing independently of the chosen background.
        Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        SourceInitialized += (_, _) => HwndSource.FromHwnd(new WindowInteropHelper(this).Handle).AddHook(HostWindowHook);
        ShowInTaskbar = false; FontFamily = new FontFamily("Malgun Gothic");
        var grid = new Grid { Margin = new Thickness(6, 2, 6, 2) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(68) }); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });

        grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition());
        healthBar.HorizontalAlignment = HorizontalAlignment.Left; healthBar.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRowSpan(healthBar, 2); grid.Children.Add(healthBar);
        erinnTime = Label("--:--", Ui.Muted, 11); erinnTime.Margin = new Thickness(3, 0, 0, 0);
        var environmentLine = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        environmentLine.Children.Add(weatherIcon); environmentLine.Children.Add(erinnTime);
        environmentRegion = new Border { Child = environmentLine, Background = Brushes.Transparent, Cursor = Cursors.Hand };
        Grid.SetColumn(environmentRegion, 2); grid.Children.Add(environmentRegion);
        activity = Label("CLI 설정 필요", Ui.Accent, 12);
        playbackTime = Label("", Ui.Muted, 10); playbackTime.Margin = new Thickness(6, 0, 0, 0);
        var actionLine = new Grid(); actionLine.ColumnDefinitions.Add(new ColumnDefinition()); actionLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actionLine.Children.Add(activity); Grid.SetColumn(playbackTime, 1); actionLine.Children.Add(playbackTime);
        playbackProgress = new ProgressBar { Height = 2, Maximum = 1, Foreground = Ui.Accent, Background = new SolidColorBrush(Color.FromArgb(80, 120, 145, 166)), BorderThickness = new Thickness(0), Margin = new Thickness(0, 2, 0, 0), IsHitTestVisible = false };
        var actionContents = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; actionContents.Children.Add(actionLine); actionContents.Children.Add(playbackProgress);
        actionRegion = new Border { Child = actionContents, Background = Brushes.Transparent, Padding = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand }; Grid.SetColumn(actionRegion, 1); grid.Children.Add(actionRegion);
        music = new MarqueeText(12, Ui.Foreground) { Text = "연주 없음 · 클릭하여 악보 선택", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Background = Brushes.Transparent };
        var musicRow = new Grid { Background = Brushes.Transparent };
        musicRegion = musicRow; musicRow.Cursor = Cursors.Hand;
        musicRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); musicRow.ColumnDefinitions.Add(new ColumnDefinition());
        musicEffect.Margin = new Thickness(0, 0, 5, 0); musicRow.Children.Add(musicEffect);
        Grid.SetColumn(music, 1); musicRow.Children.Add(music);
        Grid.SetColumn(musicRow, 1); Grid.SetColumnSpan(musicRow, 2); Grid.SetRow(musicRow, 1); grid.Children.Add(musicRow);
        running = Label("–", Ui.Foreground, 13); completed = Label("–", Ui.Accent, 13);
        running.FontWeight = completed.FontWeight = FontWeights.SemiBold;
        var queueRows = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        foreach (var (title, count) in new[] { ("진행", running), ("완료", completed) })
        {
            var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(Label(title, count == completed ? Ui.Accent : Ui.Muted, 10)); Grid.SetColumn(count, 1); row.Children.Add(count); queueRows.Children.Add(row);
        }
        queueButton = new System.Windows.Controls.Button { Content = queueRows, Height = 38, Margin = new Thickness(3, 0, 3, 0), Padding = new Thickness(7, 0, 3, 0), HorizontalContentAlignment = HorizontalAlignment.Stretch, Background = new SolidColorBrush(Color.FromRgb(36, 51, 65)), ToolTip = "제작 작업 · 진행/완료 목록 열기" };
        System.Windows.Automation.AutomationProperties.SetName(queueButton, "제작 진행 및 완료 목록");
        queueButton.Click += (_, _) => app.OpenDetails();
        Grid.SetColumn(queueButton, 3); Grid.SetRowSpan(queueButton, 2); grid.Children.Add(queueButton);
        var border = new Border { Child = grid, Background = MusicEffectVisual.BackgroundBrush(appearance), CornerRadius = new CornerRadius(6) }; background = border; Content = border;
        // Route the whole surface, including padding and gaps, before child text/controls.
        border.PreviewMouseLeftButtonUp += (_, e) =>
        {
            var point = e.GetPosition(grid);
            var hpWidth = grid.ColumnDefinitions[0].ActualWidth;
            var contentEnd = hpWidth + grid.ColumnDefinitions[1].ActualWidth + grid.ColumnDefinitions[2].ActualWidth;
            var queueEnd = contentEnd + grid.ColumnDefinitions[3].ActualWidth;
            if (point.X < hpWidth || point.X >= contentEnd && point.X < queueEnd)
            {
                app.OpenDetails(); e.Handled = true;
            }
            else if (point.X < contentEnd)
            {
                if (point.Y < grid.RowDefinitions[0].ActualHeight) app.OpenDetails(); else app.OpenMusic();
                e.Handled = true;
            }
        };
        music.ToolTip = "클릭하여 보유 악보 선택";
        var menu = new ContextMenu(); AddMenu(menu, "작업과 음악", app.OpenDetails); AddMenu(menu, "악보 선택", app.OpenMusic); AddMenu(menu, "설정", app.OpenSettings); AddMenu(menu, "종료", () => _ = app.ExitAsync()); border.ContextMenu = menu;
        Placement = app.Settings.Placement;
        var libraryPlacement = Placement == TaskbarPlacement.Center ? TaskbarContentPlacement.BeforeNotificationArea : Enum.Parse<TaskbarContentPlacement>(Placement.ToString());
        Host = new(this, border, new() { PreferredWidth = 480, PreferredHeight = 48, Placement = libraryPlacement, AnimateLayoutChanges = false });
        playbackTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
        playbackTimer.Tick += (_, _) => UpdatePlayback();
        Closed += (_, _) => { playbackTimer.Stop(); Host.Dispose(); };
    }
    private static TextBlock Label(string text, Brush color, double size) => new() { Text = text, Foreground = color, FontSize = size, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPosition { public IntPtr Hwnd, InsertAfter; public int X, Y, Width, Height; public uint Flags; }
    private IntPtr HostWindowHook(IntPtr hwnd, int message, IntPtr wp, IntPtr lp, ref bool handled)
    {
        // Modify the library's position before Windows shows it; retain its measured safe area,
        // width, clipping, DPI, Explorer recovery, and layout refresh instead of racing a timer.
        if (message == 0x0046 && Placement == TaskbarPlacement.Center && Host is { IsAttached: true })
        {
            var position = Marshal.PtrToStructure<WindowPosition>(lp);
            if ((position.Flags & 0x0002) == 0 && (position.Flags & 0x0020) != 0 && position.Width > 0 &&
                GetWindowRect(GetParent(hwnd), out var parent) && double.IsFinite(background.MaxWidth))
            {
                var scale = GetDpiForWindow(hwnd) / 96.0;
                var freeRight = position.X + position.Width;
                var freeLeft = freeRight - Math.Max(position.Width, background.MaxWidth * scale);
                var target = TaskbarCenterPlacement.Left(parent.Right - parent.Left, freeLeft, freeRight, position.Width);
                position.X = (int)Math.Round(target);
                centerTargetX = position.X;
                Marshal.StructureToPtr(position, lp, false);
            }
        }
        return ClientHitTest(hwnd, message, wp, lp, ref handled);
    }
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);
    private static IntPtr ClientHitTest(IntPtr hwnd, int message, IntPtr wp, IntPtr lp, ref bool handled)
    {
        if (message == 0x0084 && GetWindowRect(hwnd, out var rect))
        {
            var packed = lp.ToInt64(); var x = unchecked((short)(packed & 0xffff)); var y = unchecked((short)((packed >> 16) & 0xffff));
            if (x >= rect.Left && x < rect.Right && y >= rect.Top && y < rect.Bottom)
            {
                handled = true; return new IntPtr(1); // HTCLIENT: the whole allocated taskbar surface.
            }
        }
        return IntPtr.Zero;
    }
    private static void AddMenu(ContextMenu menu, string title, Action action) { var item = new MenuItem { Header = title }; item.Click += (_, _) => action(); menu.Items.Add(item); }
    public void Update(StatusSnapshot snapshot)
    {
        latest = snapshot;
        var environment = snapshot.Connected ? snapshot.Environment : null;
        erinnTime.Text = environment?.TimeText ?? "--:--";
        weatherIcon.SetWeather(environment?.Weather);
        environmentRegion.ToolTip = environment is null ? "에린 날씨·시간: " + (snapshot.EnvironmentUnavailableReason ?? snapshot.Message) : $"에린 날씨: {environment.Weather}\n에린 시간: {environment.TimeText}";
        healthBar.SetState(snapshot.Connected ? snapshot.Health : null, snapshot.Connected && snapshot.Activity is { IsDead: true }, snapshot.HealthUnavailableReason ?? snapshot.Message);
        musicPlaying = snapshot.Connected && snapshot.Activity is { Music.IsPlaying: true };
        nearbyPlaying = snapshot.Connected && snapshot.Activity is { Music.IsPlaying: false } && snapshot.NearbyMusic is not null;
        musicEffect.SetState(appearance.PlaybackEffect, musicPlaying || nearbyPlaying, nearbyPlaying);
        music.TextBrush = nearbyPlaying ? NearbyForeground : Ui.Foreground;
        activity.Text = snapshot.Connected && snapshot.Activity is { IsDead: true } ? "쓰러진 상태" : musicPlaying ? "연주 중" : snapshot.Connected ? snapshot.Activity?.Summary ?? "행동 조회 실패" : "연결 안 됨";
        UpdatePlayback();
        if (musicPlaying) playbackTimer.Start(); else playbackTimer.Stop();
        running.Text = snapshot.Connected && snapshot.Queue is { } q ? q.RunningCount.ToString() : "–";
        completed.Text = snapshot.Connected && snapshot.Queue is { } c ? c.CompletedCount.ToString() : "–";
        queueButton.ToolTip = snapshot.Connected && snapshot.Queue is { } workQueue ? "제작 작업 · " + workQueue.Summary + "\n클릭하여 진행/완료 목록 열기" : "제작 작업 조회 대기";
        music.Text = musicPlaying && snapshot.Activity is { } state ? "♫ " + state.Music.Title : nearbyPlaying ? "주변 ♫ " + snapshot.NearbyMusic!.Title : "연주 없음 · 클릭하여 악보 선택";
        ToolTip = $"{snapshot.Message}\n{snapshot.Queue?.Summary}\n" + (snapshot.Activity is { } a ? DisplayTime.Music(a.Music, snapshot.UpdatedAt, DateTimeOffset.Now) : snapshot.Message);
        if (nearbyPlaying) ToolTip += $"\n주변 연주: {snapshot.NearbyMusic!.Title}\n연주자: {snapshot.NearbyMusic.Performer}";
        else if (snapshot.NearbyMusicUnavailableReason is { } reason) ToolTip += "\n주변 연주: " + reason;
    }
    public void ApplyAppearance(AppSettings settings)
    {
        appearance = settings;
        background.Background = MusicEffectVisual.BackgroundBrush(settings);
        musicEffect.SetState(settings.PlaybackEffect, musicPlaying || nearbyPlaying, nearbyPlaying);
        UpdatePlayback();
    }
    private void UpdatePlayback()
    {
        playbackTime.Visibility = playbackProgress.Visibility = musicPlaying ? Visibility.Visible : Visibility.Collapsed;
        if (!musicPlaying || latest?.Activity is not { } state) return;
        var musicState = state.Music; var now = DateTimeOffset.Now;
        playbackTime.Text = DisplayTime.Playback(musicState, latest.UpdatedAt, now, appearance.MusicTimeDisplay);
        playbackProgress.IsIndeterminate = musicState.TotalSeconds <= 0;
        playbackProgress.Value = musicState.TotalSeconds > 0 ? DisplayTime.Elapsed(musicState, latest.UpdatedAt, now) / musicState.TotalSeconds : 0;
    }
    internal Task ClickBlankRegionForSmokeAsync(bool musicArea)
    {
        var region = musicArea ? (FrameworkElement)musicRegion : actionRegion;
        return UiSmokeInput.ClickAsync(region, new Point(region.ActualWidth - 2, region.ActualHeight - 2));
    }
    internal async Task<bool> OpenSettingsViaContextMenuForSmokeAsync()
    {
        await UiSmokeInput.ClickAsync(background, new Point(30, 10), true);
        await Task.Delay(250);
        var menu = background.ContextMenu;
        if (menu is null || !menu.IsOpen) return false;
        var item = menu.Items.OfType<MenuItem>().Single(entry => (string)entry.Header == "설정");
        await UiSmokeInput.ClickAsync(item, new Point(item.ActualWidth / 2, item.ActualHeight / 2));

        await Task.Delay(200);
        return true;
    }
    internal Task ClickQueueForSmokeAsync() => UiSmokeInput.ClickAsync(queueButton, new Point(queueButton.ActualWidth / 2, queueButton.ActualHeight / 2));
    internal Point BlankPointForSmoke(bool musicArea, bool outerEdge)
    {
        var region = musicArea ? (FrameworkElement)musicRegion : actionRegion;
        var point = new Point(outerEdge ? 2 : region.ActualWidth - 2, region.ActualHeight - 2);
        return region.TranslatePoint(point, this);
    }
    public void ShowFallback() { Width = 480; Height = 48; Topmost = true; Left = Math.Max(0, SystemParameters.WorkArea.Right - Width - 12); Top = Math.Max(0, SystemParameters.WorkArea.Bottom - Height - 12); Show(); }
}

