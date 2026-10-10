using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using mStatusBar.Core;
using TextBox = System.Windows.Controls.TextBox;
namespace mStatusBar;
internal sealed class SettingsWindow : FlyoutWindow
{
    private readonly TextBox path, interval;
    private readonly TextBlock testTitle, testDetail, saveMessage;
    private readonly System.Windows.Controls.Button testButton, browseButton, saveButton;
    private readonly ProgressBar testProgress;
    private readonly ScrollViewer contentScroll;
    private readonly List<System.Windows.Controls.RadioButton> placementOptions = new();
    private readonly List<System.Windows.Controls.RadioButton> effectOptions = new();
    private readonly List<System.Windows.Controls.RadioButton> timeOptions = new();
    private readonly System.Windows.Controls.CheckBox startWithWindows;
    private readonly TextBox backgroundColor;
    private readonly Slider transparency;
    private readonly MusicEffectVisual effectPreview = new();
    private readonly Border appearancePreview = new();
    private readonly List<System.Windows.Controls.Button> colorButtons = new();
    private readonly App app;
    private readonly CancellationTokenSource cancellation = new();
    public bool ConnectionTestSucceeded { get; private set; }
    public SettingsWindow(App app)
    {
        this.app = app; Ui.Style(this, "mStatusBar · 연결 설정", 680, 700);
        var panel = new StackPanel { Margin = new Thickness(28) };
        panel.Children.Add(Ui.Text("환경 설정", 27, Ui.Foreground));
        panel.Children.Add(Ui.Text("게임 연결과 작업 표시줄의 위치·모양을 설정하세요.", 14, Ui.Muted));
        var guidance = Ui.Text("환경 설정 > 게임 > 마비노기 모바일 AI 커넥터 기능을 켜야 정상 동작합니다", 14, Ui.Accent); guidance.Margin = new Thickness(0);
        panel.Children.Add(new Border { Child = guidance, Background = new SolidColorBrush(Color.FromRgb(25, 54, 50)), CornerRadius = new CornerRadius(10), Padding = new Thickness(16), Margin = new Thickness(0, 6, 0, 18) });
        var file = new StackPanel(); file.Children.Add(Ui.Text("01   CLI 실행 파일", 17));
        file.Children.Add(Ui.Text("게임 폴더의 MabinogiMobile_CLI.exe를 선택하세요.", 12, Ui.Muted));
        path = new TextBox { Text = app.Settings.CliPath, MinHeight = 40, Padding = new Thickness(10), IsReadOnly = true, ToolTip = app.Settings.CliPath };
        file.Children.Add(path);
        var fileRow = Ui.Row(); browseButton = Ui.Button("파일 찾아보기…", Browse); fileRow.Children.Add(browseButton);
        testButton = Ui.Button("커넥터 연결 테스트", async (_, _) => await TestConnectionAsync()); fileRow.Children.Add(testButton); file.Children.Add(fileRow);
        testTitle = Ui.Text("●  아직 연결을 확인하지 않았습니다", 16, Ui.Muted);
        testDetail = Ui.Text("게임이 꺼져 있어도 파일 경로를 저장할 수 있습니다.", 12, Ui.Muted);
        testProgress = new ProgressBar { Height = 3, IsIndeterminate = true, Visibility = Visibility.Collapsed, Foreground = Ui.Accent, Margin = new Thickness(0, 0, 0, 10) };
        file.Children.Add(testProgress); file.Children.Add(testTitle); file.Children.Add(testDetail); panel.Children.Add(Card(file));
        var timing = new StackPanel(); timing.Children.Add(Ui.Text("02   상태 조회 주기", 17));
        var timingRow = Ui.Row(); interval = new TextBox { Text = app.Settings.PollIntervalSeconds.ToString(CultureInfo.InvariantCulture), Width = 56, Height = 32, Padding = new Thickness(8, 4, 8, 4), FontSize = 14, VerticalContentAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center, MaxLength = 2 };
        timingRow.Children.Add(interval); timingRow.Children.Add(new TextBlock { Text = "초마다 조회", Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) }); timing.Children.Add(timingRow);
        timing.Children.Add(Ui.Text("기본 2초 · 1~60초\n이전 조회가 끝난 뒤 기다리며, 연결 실패 시 재시도 간격을 늘립니다.", 12, Ui.Muted)); panel.Children.Add(Card(timing));
        var position = new StackPanel(); position.Children.Add(Ui.Text("03   작업 표시줄 위치", 17));
        foreach (var (value, label) in new[] {
            (TaskbarPlacement.BeforeNotificationArea, "오른쪽 · 알림 영역 앞 (기본)"),
            (TaskbarPlacement.Center, "가운데 · 중앙에 가깝게"),
            (TaskbarPlacement.LeftEdge, "왼쪽 끝 · 위젯 옆"),
            (TaskbarPlacement.BeforeStartButton, "시작 버튼 바로 앞"),
            (TaskbarPlacement.Auto, "자동 · 여유 공간이 넓은 쪽") })
        {
            var option = new System.Windows.Controls.RadioButton { Content = label, Tag = value, GroupName = "Placement", IsChecked = value == app.Settings.Placement, Margin = new Thickness(0, 3, 0, 3), Padding = new Thickness(10, 8, 10, 8), FontSize = 13 };
            placementOptions.Add(option); position.Children.Add(option);
        }
        position.Children.Add(Ui.Text("가운데는 시작 버튼·앱 아이콘과 겹치지 않는 범위에서 중앙에 가깝게 배치합니다. Windows가 왼쪽 정렬이면 왼쪽 끝·시작 버튼 앞은 오른쪽으로 대체됩니다. 공간이 부족하면 폭이 줄어듭니다.", 12, Ui.Muted)); panel.Children.Add(Card(position));
        var effects = new StackPanel(); effects.Children.Add(Ui.Text("04   음악 재생 효과", 17));
        var choices = new WrapPanel();
        foreach (var (value, label) in new[] { (MusicEffect.Equalizer, "이퀄라이저"), (MusicEffect.Wave, "물결"), (MusicEffect.Pulse, "숨쉬는 점"), (MusicEffect.Orbit, "궤도"), (MusicEffect.None, "효과 없음") })
        {
            var option = new System.Windows.Controls.RadioButton { Content = label, Tag = value, GroupName = "MusicEffect", IsChecked = value == app.Settings.PlaybackEffect, Margin = new Thickness(0, 3, 8, 3), Padding = new Thickness(10, 8, 10, 8), FontSize = 13 };
            option.Checked += (_, _) => UpdateAppearancePreview();
            effectOptions.Add(option); choices.Children.Add(option);
        }
        effects.Children.Add(choices);
        effects.Children.Add(Ui.Text("연주 중 곡명 옆에서 은은하게 움직입니다. 재생 상태를 표현하는 장식 효과이며 실제 음량 분석은 아닙니다.", 12, Ui.Muted));
        effects.Children.Add(Ui.Text("행동 영역의 시간 표시", 13, Ui.Muted));
        var timeRow = new WrapPanel();
        foreach (var (value, label) in new[] { (PlaybackTimeMode.Elapsed, "재생 시간"), (PlaybackTimeMode.Remaining, "남은 시간") })
        {
            var option = new System.Windows.Controls.RadioButton { Content = label, Tag = value, GroupName = "PlaybackTime", IsChecked = value == app.Settings.MusicTimeDisplay, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 8, 10, 8), FontSize = 13 };
            timeOptions.Add(option); timeRow.Children.Add(option);
        }
        effects.Children.Add(timeRow);
        panel.Children.Add(Card(effects));
        var colors = new StackPanel(); colors.Children.Add(Ui.Text("05   애드온 배경", 17));
        backgroundColor = new TextBox { Text = app.Settings.BackgroundColor, Width = 104, Height = 32, Padding = new Thickness(8, 4, 8, 4), FontSize = 13, MaxLength = 7, VerticalContentAlignment = VerticalAlignment.Center };
        var palette = new WrapPanel();
        foreach (var color in new[] { "#171D28", "#102E2A", "#182B46", "#30243E", "#3C272A", "#343434" })
        {
            var swatch = new System.Windows.Controls.Button { Width = 32, Height = 28, Margin = new Thickness(0, 0, 8, 8), Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)), BorderBrush = Ui.Muted, BorderThickness = new Thickness(1), ToolTip = color };
            swatch.Click += (_, _) => backgroundColor.Text = color; colorButtons.Add(swatch); palette.Children.Add(swatch);
        }
        colors.Children.Add(palette);
        var colorRow = Ui.Row(); colorRow.Children.Add(new TextBlock { Text = "직접 입력", Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        colorRow.Children.Add(backgroundColor); colors.Children.Add(colorRow);
        var opacityRow = Ui.Row();
        opacityRow.Children.Add(new TextBlock { Text = "배경 투명도", Foreground = Ui.Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        transparency = new Slider { Minimum = 0, Maximum = 100, Value = app.Settings.BackgroundTransparencyPercent, TickFrequency = 1, IsSnapToTickEnabled = true, Width = 220, Height = 28, VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Accent, Style = (Style)FindResource("AppearanceSlider"), IsMoveToPointEnabled = true, SmallChange = 1, LargeChange = 10, ToolTip = "배경 투명도 0~100% · 방향키로 1%씩 조정" };
        var percent = new TextBlock { Foreground = Ui.Foreground, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        void UpdatePercent() => percent.Text = $"{transparency.Value:0}%";
        transparency.ValueChanged += (_, _) => { UpdatePercent(); UpdateAppearancePreview(); }; UpdatePercent();
        opacityRow.Children.Add(transparency); opacityRow.Children.Add(percent); colors.Children.Add(opacityRow);
        colors.Children.Add(Ui.Text("0%는 불투명, 100%는 배경 없음입니다. 글자와 버튼은 선명하게 유지됩니다.", 12, Ui.Muted));
        var previewRow = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        effectPreview.Margin = new Thickness(0, 0, 8, 0); previewRow.Children.Add(effectPreview);
        previewRow.Children.Add(new TextBlock { Text = "♫ 음악 재생 미리보기", FontSize = 12, Foreground = Ui.Foreground, VerticalAlignment = VerticalAlignment.Center });
        appearancePreview.Child = previewRow; appearancePreview.CornerRadius = new CornerRadius(6); appearancePreview.Padding = new Thickness(12); appearancePreview.Margin = new Thickness(0, 4, 0, 0); colors.Children.Add(appearancePreview);
        backgroundColor.TextChanged += (_, _) => UpdateAppearancePreview();
        panel.Children.Add(Card(colors)); UpdateAppearancePreview();
        var startupCard = new StackPanel(); startupCard.Children.Add(Ui.Text("06   Windows 자동 실행", 17));
        startWithWindows = new System.Windows.Controls.CheckBox { Content = "Windows가 시작될 때 항상 실행", IsChecked = app.Settings.StartWithWindows, Padding = new Thickness(10, 8, 10, 8), FontSize = 13, Foreground = Ui.Foreground };
        startupCard.Children.Add(startWithWindows);
        startupCard.Children.Add(Ui.Text("로그인하면 애드온을 실행합니다. 게임이 아직 준비되지 않았다면 커넥터 연결을 재시도하고, 연결되면 자동으로 상태 표시를 시작합니다.", 12, Ui.Muted)); panel.Children.Add(Card(startupCard));
        saveMessage = Ui.Text(app.StartupWarning ?? app.Store.LoadWarning ?? "", 13, Ui.Accent);
        var footer = Ui.Row(); saveButton = Ui.Button("저장 및 적용", Save); saveButton.Background = Ui.Accent; saveButton.Foreground = Ui.Background; footer.Children.Add(saveButton);
        footer.Children.Add(Ui.Button("닫기", (_, _) => Hide()));
        var footerPanel = new StackPanel { Margin = new Thickness(28, 6, 28, 14) }; footerPanel.Children.Add(saveMessage); footerPanel.Children.Add(footer);
        var layout = new DockPanel { Background = Ui.Background }; DockPanel.SetDock(footerPanel, Dock.Bottom); layout.Children.Add(footerPanel);
        contentScroll = new ScrollViewer { Content = panel, Background = Ui.Background, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        layout.Children.Add(contentScroll); Content = layout;
        Closed += (_, _) => cancellation.Cancel();
    }
    private static Border Card(UIElement child) => new() { Child = child, Background = new SolidColorBrush(Color.FromRgb(31, 41, 56)), BorderBrush = new SolidColorBrush(Color.FromRgb(49, 64, 81)), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(20), Margin = new Thickness(0, 0, 0, 18) };
    private void Busy(bool busy) { testButton.IsEnabled = browseButton.IsEnabled = saveButton.IsEnabled = interval.IsEnabled = backgroundColor.IsEnabled = transparency.IsEnabled = startWithWindows.IsEnabled = !busy; foreach (var option in placementOptions.Concat(effectOptions).Concat(timeOptions)) option.IsEnabled = !busy; foreach (var button in colorButtons) button.IsEnabled = !busy; }
    private MusicEffect SelectedEffect => (MusicEffect)(effectOptions.FirstOrDefault(option => option.IsChecked == true)?.Tag ?? MusicEffect.Equalizer);
    private void UpdateAppearancePreview()
    {
        effectPreview.SetState(SelectedEffect, true);
        if (backgroundColor is null || transparency is null) return;
        var settings = app.Settings with { BackgroundColor = backgroundColor.Text.Trim(), BackgroundTransparencyPercent = (int)Math.Round(transparency.Value) };
        try { settings.Validate(); appearancePreview.Background = MusicEffectVisual.BackgroundBrush(settings); }
        catch (ArgumentException) { }
    }
    private void Browse(object sender, RoutedEventArgs e)
    {
        using var hold = SuspendDismissal();
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "MabinogiMobile_CLI.exe 선택", CheckFileExists = true, Multiselect = false, Filter = "CLI 실행 파일 (*.exe)|*.exe", FileName = "MabinogiMobile_CLI.exe" };
        if (dialog.ShowDialog(this) != true) return;
        path.Text = dialog.FileName; path.ToolTip = path.Text; ConnectionTestSucceeded = false;
        testTitle.Text = "●  파일 선택 완료"; testTitle.Foreground = Ui.Muted; testDetail.Text = "커넥터 연결 테스트를 눌러 게임 연결을 확인하세요.";
    }
    public async Task TestConnectionAsync()
    {
        if (!testButton.IsEnabled) return;
        Busy(true); ConnectionTestSucceeded = false; testButton.Content = "연결 확인 중…"; testProgress.Visibility = Visibility.Visible;
        testTitle.Text = "●  커넥터 응답을 기다리고 있습니다"; testTitle.Foreground = Ui.Accent; testDetail.Text = "CLI를 실행해 게임 연결 상태를 확인합니다. 최대 30초 기다립니다.";
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token); limit.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            if (!File.Exists(path.Text)) throw new ArgumentException("존재하는 CLI 실행 파일을 먼저 선택해 주세요.");
            await app.Client.CheckStatusAsync(path.Text, limit.Token);
            ConnectionTestSucceeded = true; testTitle.Text = "✓  커넥터 연결 성공"; testTitle.Foreground = Ui.Accent;
            testDetail.Text = $"CLI 응답 정상 · 게임 연결됨\n확인 시각 {DateTime.Now:HH:mm:ss}";
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { TestFailed("30초 동안 응답이 없습니다. 파일 경로와 게임 실행 상태를 확인해 주세요."); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { TestFailed(ex.Message); }
        finally { if (!cancellation.IsCancellationRequested) { Busy(false); testButton.Content = "다시 연결 테스트"; testProgress.Visibility = Visibility.Collapsed; } }
    }
    private void TestFailed(string message) { testTitle.Text = "✕  커넥터 연결 실패"; testTitle.Foreground = new SolidColorBrush(Color.FromRgb(255, 154, 151)); testDetail.Text = message + $"\n확인 시각 {DateTime.Now:HH:mm:ss}"; }
    internal async Task TestInvalidFileForSmokeAsync()
    {
        var originalPath = path.Text;
        try { path.Text = ""; await TestConnectionAsync(); }
        finally { path.Text = originalPath; }
    }
    private async void Save(object sender, RoutedEventArgs e)
        => await SaveSettingsAsync();
    internal async Task<bool> SetPlacementForSmokeAsync(TaskbarPlacement placement)
    {
        placementOptions.Single(option => (TaskbarPlacement)option.Tag == placement).IsChecked = true;
        await SaveSettingsAsync();
        return app.Settings.Placement == placement && app.Store.Load().Placement == placement;
    }
    internal void ShowPlacementForSmoke() { placementOptions[0].BringIntoView(); placementOptions.Last().BringIntoView(); }
    internal async Task<bool> SetPlaybackTimeForSmokeAsync(PlaybackTimeMode mode, bool automatic)
    {
        timeOptions.Single(option => (PlaybackTimeMode)option.Tag == mode).IsChecked = true;
        startWithWindows.IsChecked = automatic;
        await SaveSettingsAsync();
        var saved = app.Store.Load(); return saved.MusicTimeDisplay == mode && saved.StartWithWindows == automatic;
    }
    internal async Task<bool> SetAppearanceForSmokeAsync(MusicEffect effect, string color, int transparent)
    {
        effectOptions.Single(option => (MusicEffect)option.Tag == effect).IsChecked = true;
        backgroundColor.Text = color; transparency.Value = transparent;
        await SaveSettingsAsync();
        var saved = app.Store.Load();
        return saved.PlaybackEffect == effect && saved.BackgroundColor == color && saved.BackgroundTransparencyPercent == transparent;
    }
    private async Task SaveSettingsAsync()
    {
        Busy(true);
        try { if (!File.Exists(path.Text)) throw new ArgumentException("존재하는 CLI 실행 파일을 선택해 주세요."); if (!int.TryParse(interval.Text, out var seconds)) throw new ArgumentException("조회 주기는 정수로 입력해 주세요."); await app.ApplySettingsAsync(new() { CliPath = path.Text, PollIntervalSeconds = seconds, Placement = (TaskbarPlacement)placementOptions.Single(option => option.IsChecked == true).Tag, PlaybackEffect = SelectedEffect, BackgroundColor = backgroundColor.Text.Trim().ToUpperInvariant(), BackgroundTransparencyPercent = (int)Math.Round(transparency.Value), MusicTimeDisplay = (PlaybackTimeMode)timeOptions.Single(option => option.IsChecked == true).Tag, StartWithWindows = startWithWindows.IsChecked == true }); saveMessage.Text = "✓ 설정을 저장하고 적용했습니다."; }
        catch (Exception ex) { saveMessage.Text = ex.Message; }
        finally { if (!cancellation.IsCancellationRequested) Busy(false); }
    }
}


