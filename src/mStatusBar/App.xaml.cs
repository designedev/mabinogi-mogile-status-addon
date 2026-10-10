using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Text.Json;
using mStatusBar.Core;
using Forms = System.Windows.Forms;

namespace mStatusBar;

public partial class App : System.Windows.Application
{
    public SettingsStore Store { get; private set; } = null!;
    public AppSettings Settings { get; private set; } = new();
    public ReadOnlyCliClient Client { get; private set; } = null!;
    public StatusSnapshot? Snapshot { get; private set; }
    private PollingService poller = null!;
    private TaskbarWindow? taskbar;
    private SettingsWindow? settingsWindow;
    private DetailsWindow? detailsWindow;
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private Mutex? singleton;
    private readonly SemaphoreSlim recreation = new(1, 1);
    private bool exiting;
    private string? attachError;
    private string? smokeDirectory;
    private int settingsGeneration;
    private CollectionService collection = null!;
    private MusicService musicService = null!;
    private MusicWindow? musicWindow;
    private FlyoutWindow? activeFlyout;
    private Task? actionTask;
    private IStartupRegistration startup = null!;
    internal string? StartupWarning { get; private set; }
    public bool IsActing => actionTask is { IsCompleted: false };
    public string ActionMessage { get; private set; } = "";

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var directory = Environment.GetEnvironmentVariable("MSTATUSBAR_SETTINGS_DIR") ?? SettingsStore.DefaultDirectory;
        // Test runs can use an isolated settings directory without touching the user's settings.
        var identity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(directory).ToUpperInvariant())));
        singleton = new Mutex(true, "Local\\mStatusBar-" + identity, out var created);
        if (!created) { System.Windows.MessageBox.Show("mStatusBar가 이미 실행 중입니다. 트레이 아이콘에서 설정을 열어 주세요."); Shutdown(); return; }
        var smokeIndex = Array.IndexOf(e.Args, "--smoke-output");
        if (smokeIndex >= 0 && smokeIndex + 1 < e.Args.Length) smokeDirectory = Path.GetFullPath(e.Args[smokeIndex + 1]);
        Store = new(directory); Settings = Store.Load();
        startup = smokeDirectory is not null || Environment.GetEnvironmentVariable("MSTATUSBAR_SETTINGS_DIR") is not null ? new MockStartupRegistration() : new WindowsStartupRegistration();
        if (Settings.StartWithWindows)
        {
            try { startup.Apply(true); }
            catch (Exception ex) { StartupWarning = "자동 실행 등록 실패: " + ex.Message; }
        }
        var process = new CliProcess();
        Client = new(process, Path.Combine(directory, "capabilities"));
        collection = new(process, process); musicService = new(process, process);
        poller = new(Client);
        poller.Updated += snapshot =>
        {
            var generation = settingsGeneration;
            Dispatcher.InvokeAsync(() =>
            {
                if (exiting || generation != settingsGeneration) return;
                Snapshot = snapshot; taskbar?.Update(snapshot); detailsWindow?.Update(snapshot);
                if (tray is not null) tray.Text = "mStatusBar · " + (snapshot.Connected ? "게임 연결됨" : "연결 안 됨");
            });
        };
        CreateTray();
        DispatcherUnhandledException += (_, args) =>
        {
            args.Handled = true;
            System.Windows.MessageBox.Show(args.Exception.Message, "mStatusBar 오류");
        };
        await poller.ConfigureAsync(Settings);
        await RecreateTaskbarAsync();
        if (Settings.CliPath.Length == 0 || !File.Exists(Settings.CliPath) || Store.LoadWarning is not null || StartupWarning is not null) OpenSettings();
        if (smokeDirectory is not null) _ = SaveSmokeAsync(smokeDirectory);
    }

    private void CreateTray()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("상태 상세", null, (_, _) => Dispatcher.Invoke(OpenDetails));
        menu.Items.Add("설정", null, (_, _) => Dispatcher.Invoke(OpenSettings));
        menu.Items.Add("작업 표시줄 다시 연결", null, async (_, _) => await RecreateTaskbarAsync());
        menu.Items.Add("종료", null, async (_, _) => await ExitAsync());
        using var iconStream = GetResourceStream(new Uri("pack://application:,,,/Assets/mStatusBar.ico")).Stream;
        using var sourceIcon = new System.Drawing.Icon(iconStream);
        trayIcon = (System.Drawing.Icon)sourceIcon.Clone();
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = "mStatusBar", ContextMenuStrip = menu, Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(OpenDetails);
    }

    private async Task RecreateTaskbarAsync()
    {
        if (!await recreation.WaitAsync(0)) return;
        try
        {
            if (exiting) return;
            activeFlyout?.Hide(); activeFlyout = null;
            var old = taskbar; taskbar = null;
            old?.Host.Dispose(); old?.Close();
            var window = new TaskbarWindow(this); taskbar = window;
            window.Host.TaskbarWindowRecreationRequired += (_, _) => _ = Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(1000);
                await RecreateTaskbarAsync();
            });
            if (Snapshot is not null) window.Update(Snapshot);
            try
            {
                await window.Host.AttachWhenLayoutReadyAsync().WaitAsync(TimeSpan.FromSeconds(20));
                if (exiting) return;
                attachError = null; window.Show();
            }
            catch (Exception ex)
            {
                window.Host.Dispose();
                if (exiting) return;
                attachError = ex.Message; window.ShowFallback();
                tray?.ShowBalloonTip(5000, "mStatusBar", "작업 표시줄에 연결하지 못해 작은 상태 창을 표시합니다. 트레이에서 다시 연결할 수 있습니다.", Forms.ToolTipIcon.Info);
            }
        }
        finally { recreation.Release(); }
    }

    public async Task ApplySettingsAsync(AppSettings settings)
    {
        if (IsActing) throw new InvalidOperationException("진행 중인 작업이 끝난 후 설정을 변경해 주세요.");
        settings.Validate();
        var placementChanged = Settings.Placement != settings.Placement;
        var restoreSettings = settingsWindow?.IsVisible == true;
        if (Settings.StartWithWindows != settings.StartWithWindows || StartupWarning is not null)
        {
            startup.Apply(settings.StartWithWindows);
            try { Store.Save(settings); }
            catch { startup.Apply(Settings.StartWithWindows); throw; }
        }
        else Store.Save(settings);
        StartupWarning = null;
        settingsGeneration++;
        Settings = settings;
        taskbar?.ApplyAppearance(settings);
        Snapshot = StatusSnapshot.Unavailable("설정 적용 중…", TimeSpan.FromSeconds(settings.PollIntervalSeconds));
        taskbar?.Update(Snapshot); detailsWindow?.Update(Snapshot);
        await poller.ConfigureAsync(settings);
        if (placementChanged)
        {
            await RecreateTaskbarAsync();
            if (restoreSettings && settingsWindow is not null) ShowFlyout(settingsWindow, false);
        }
    }
    public void OpenSettings()
    {
        if (exiting) return;
        if (settingsWindow is null)
        {
            settingsWindow = new(this); settingsWindow.Closed += (_, _) => settingsWindow = null;
        }
        ShowFlyout(settingsWindow);
    }
    public void OpenDetails()
    {
        if (exiting) return;
        if (detailsWindow is null)
        {
            detailsWindow = new(this); detailsWindow.Closed += (_, _) => detailsWindow = null;
        }
        ShowFlyout(detailsWindow);
    }
    public async Task ExitAsync()
    {
        if (IsActing) { ActionMessage = "진행 중인 작업이 끝나면 종료합니다."; detailsWindow?.Update(Snapshot); await actionTask!; }
        if (exiting) return; exiting = true;
        if (poller is not null) await poller.DisposeAsync();
        taskbar?.Host.Dispose(); taskbar?.Close();
        tray?.Dispose(); tray = null;
        Shutdown();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        tray?.Dispose(); trayIcon?.Dispose(); singleton?.Dispose();
        base.OnExit(e);
    }
    public void OpenMusic()
    {
        if (exiting) return;
        if (musicWindow is null) { musicWindow = new(this); musicWindow.Closed += (_, _) => musicWindow = null; }
        ShowFlyout(musicWindow);
        if (musicWindow.IsVisible) _ = musicWindow.LoadAsync();
    }
    private void ShowFlyout(FlyoutWindow window, bool toggle = true)
    {
        if (activeFlyout == window && window.IsVisible && toggle) { window.Hide(); activeFlyout = null; return; }
        activeFlyout?.Hide(); activeFlyout = window;
        window.DismissOnDeactivate = smokeDirectory is null;
        window.Present(new System.Windows.Interop.WindowInteropHelper(taskbar!).Handle);
    }
    private Task<bool> ConfirmCollectionAsync(CollectionConfirmation info)
    {
        using var hold = activeFlyout?.SuspendDismissal();
        var message = $"{info.Facility}의 완료품을 모두 수령합니다.\n{info.Description}\n{info.Note}";
        var answer = activeFlyout is { IsVisible: true } owner ? System.Windows.MessageBox.Show(owner, message, "수령 확인", MessageBoxButton.OKCancel, MessageBoxImage.Question) :
            System.Windows.MessageBox.Show(message, "수령 확인", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        return Task.FromResult(answer == MessageBoxResult.OK);
    }
    public Task CollectWorkAsync(WorkState work) => RunActionAsync($"{work.Facility}로 이동해 완료품을 수령하고 있습니다…", () => collection.CollectAsync(Settings.CliPath, work, ConfirmCollectionAsync));
    public Task PlayMusicAsync(string title) => RunActionAsync($"「{title}」 연주를 시작합니다…", () => musicService.PlayAsync(Settings.CliPath, title));
    private async Task RunActionAsync(string message, Func<Task<string>> run)
    {
        if (IsActing || exiting) return;
        ActionMessage = message; actionTask = ExecuteActionAsync(run);
        detailsWindow?.Update(Snapshot); musicWindow?.UpdateState();
        await actionTask;
        detailsWindow?.Update(Snapshot); musicWindow?.UpdateState();
    }
    private async Task ExecuteActionAsync(Func<Task<string>> run)
    {
        try { ActionMessage = await run(); }
        catch (Exception ex) { ActionMessage = "작업: " + ex.Message; }
        finally { if (!exiting) await poller.ConfigureAsync(Settings); }
    }

    private async Task SaveSmokeAsync(string directory)
    {
        try
        {
            await Task.Delay(6000);
            var firstParentClass = taskbar?.ParentClass;
            await RecreateTaskbarAsync(); await Task.Delay(1000);
            Directory.CreateDirectory(directory);
            SaveImage(taskbar!, Path.Combine(directory, "taskbar.png"));
            var contextSettingsAccessible = await taskbar!.OpenSettingsViaContextMenuForSmokeAsync();
            contextSettingsAccessible &= settingsWindow?.IsVisible == true;
            if (settingsWindow is null) throw new InvalidOperationException("실제 우클릭 입력으로 애드온 설정 메뉴를 열지 못했습니다.");
            await settingsWindow!.TestConnectionAsync(); await Task.Delay(200);
            SaveImage(settingsWindow, Path.Combine(directory, "settings.png"));
            var connectionTestSucceeded = settingsWindow.ConnectionTestSucceeded;
            await settingsWindow.TestInvalidFileForSmokeAsync();
            SaveImage(settingsWindow, Path.Combine(directory, "settings-failure.png"));
            var connectionFailureShown = !settingsWindow.ConnectionTestSucceeded;
            var settingsFits = settingsWindow.FitsWorkArea;
            var timeSettingApplied = true;
            foreach (var mode in Enum.GetValues<PlaybackTimeMode>())
            {
                timeSettingApplied &= await settingsWindow.SetPlaybackTimeForSmokeAsync(mode, true);
                for (var attempt = 0; attempt < 30 && Snapshot?.Activity is not { Music.IsPlaying: true }; attempt++) await Task.Delay(100);
                timeSettingApplied &= taskbar!.PlaybackText.StartsWith(mode == PlaybackTimeMode.Elapsed ? "재생 " : "남은 ", StringComparison.Ordinal) && taskbar.PlaybackVisible && taskbar.PlaybackProgress > 0 && taskbar.PlaybackProgress <= 1;
                timeSettingApplied &= startup is MockStartupRegistration { Enabled: true };
                SaveImage(taskbar, Path.Combine(directory, $"playback-{mode}.png"));
            }
            timeSettingApplied &= await settingsWindow.SetPlaybackTimeForSmokeAsync(PlaybackTimeMode.Elapsed, false);
            timeSettingApplied &= startup is MockStartupRegistration { Enabled: false };
            var placementApplied = true;
            var centerPlacementApplied = false;
            var placementPositions = new Dictionary<string, double>();
            foreach (var placementOption in Enum.GetValues<TaskbarPlacement>().Reverse())
            {
                placementApplied &= await settingsWindow.SetPlacementForSmokeAsync(placementOption);
                await Task.Delay(700);
                placementApplied &= taskbar!.Host.IsAttached && taskbar.Placement.ToString() == placementOption.ToString() && settingsWindow.FitsWorkArea && settingsWindow.IsVisible;
                placementPositions[placementOption.ToString()] = settingsWindow.ActualScreenBounds.Left;
                if (placementOption == TaskbarPlacement.Center)
                {
                    centerPlacementApplied = taskbar!.CenterPlacementVerified;
                    taskbar.Host.RefreshLayout(); await Task.Delay(700);
                    centerPlacementApplied &= taskbar.CenterPlacementVerified;
                    settingsWindow.ShowPlacementForSmoke(); await Task.Delay(100);
                    SaveImage(taskbar, Path.Combine(directory, "taskbar-center.png"));
                    SaveImage(settingsWindow, Path.Combine(directory, "settings-center.png"));
                }
            }
            var repeatedPresentationStable = true;
            settingsWindow.ShowPlacementForSmoke(); await Task.Delay(150);
            SaveImage(settingsWindow, Path.Combine(directory, "settings-placement.png"));
            var appearanceApplied = true;
            var effectsAnimate = true;
            var effectsStop = true;
            foreach (var selectedEffect in Enum.GetValues<MusicEffect>())
            {
                appearanceApplied &= await settingsWindow.SetAppearanceForSmokeAsync(selectedEffect, "#182B46", 45);
                // Await the first fresh snapshot after applying settings before sampling motion.
                for (var attempt = 0; attempt < 30 && Snapshot?.Activity is not { Music.IsPlaying: true }; attempt++) await Task.Delay(100);
                appearanceApplied &= taskbar!.SelectedEffect == selectedEffect && taskbar.BackgroundColor == Color.FromArgb(140, 24, 43, 70);
                var phase = taskbar.EffectPhase; await Task.Delay(180);
                if (selectedEffect != MusicEffect.None) effectsAnimate &= taskbar.EffectAnimating && taskbar.EffectPhase > phase;
                else effectsStop &= !taskbar.EffectAnimating;
                SaveImage(taskbar, Path.Combine(directory, $"effect-{selectedEffect}.png"));
                taskbar.Update(StatusSnapshot.Unavailable("정지 검증", TimeSpan.FromSeconds(2)));
                effectsStop &= !taskbar.EffectAnimating;
                if (Snapshot is not null) taskbar.Update(Snapshot);
            }
            foreach (var transparent in new[] { 0, 100 })
            {
                appearanceApplied &= await settingsWindow.SetAppearanceForSmokeAsync(MusicEffect.Equalizer, "#182B46", transparent);
                appearanceApplied &= taskbar!.BackgroundColor.A == (transparent == 0 ? 255 : 0);
            }
            appearanceApplied &= await settingsWindow.SetAppearanceForSmokeAsync(MusicEffect.Equalizer, "#171D28", 8);
            settingsWindow.ShowPlacementForSmoke(); await Task.Delay(150);
            SaveImage(settingsWindow, Path.Combine(directory, "settings-appearance.png"));
            for (var repeat = 0; repeat < 3; repeat++)
            {
                settingsWindow.Hide(); ShowFlyout(settingsWindow, false);
                var previousOpacity = 0.0;
                for (var frame = 0; frame < 9; frame++)
                {
                    await Task.Delay(25);
                    var opacity = settingsWindow.PresentationOpacity;
                    repeatedPresentationStable &= settingsWindow.IsVisible && opacity + 0.001 >= previousOpacity;
                    previousOpacity = opacity;
                }
                repeatedPresentationStable &= previousOpacity >= 0.99 && settingsWindow.HiddenBeforeShow;
            }
            settingsWindow.DismissOnDeactivate = true;
            var external = new Window { Width = 1, Height = 1, Left = settingsWindow.Left, Top = settingsWindow.Top,
                Opacity = 0, WindowStyle = WindowStyle.None, ShowInTaskbar = false, Topmost = true };
            bool modalHoldProtected;
            using (settingsWindow.SuspendDismissal())
            {
                external.Show(); external.Activate(); await Task.Delay(150);
                settingsWindow.DismissIfInactive(false);
                modalHoldProtected = settingsWindow.IsVisible && settingsWindow.DismissalSuspended;
            }
            external.Activate(); await Task.Delay(150); settingsWindow.DismissIfInactive(false);
            var outsideDismissed = !settingsWindow.IsVisible; external.Close();
            OpenSettings();
            var escape = new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,
                PresentationSource.FromVisual(settingsWindow), Environment.TickCount, System.Windows.Input.Key.Escape)
                { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent };
            settingsWindow.RaiseEvent(escape);
            var escapeDismissed = !settingsWindow.IsVisible;
            OpenSettings(); OpenSettings(); var toggleDismissed = !settingsWindow.IsVisible;
            OpenDetails(); await Task.Delay(1000);
            SaveImage(detailsWindow!, Path.Combine(directory, "details.png"));
            var detailTitleFlowing = detailsWindow!.TitleFlowing;
            var detailsFits = detailsWindow.FitsWorkArea;
            var blankClicksAtOpacity = new Dictionary<string, bool>();
            foreach (var transparent in new[] { 0, 50, 100 })
            {
                taskbar!.ApplyAppearance(Settings with { BackgroundTransparencyPercent = transparent });
                await Task.Delay(100);
                var clicksWork = true;
                foreach (var (point, expectMusic) in new[] { (taskbar.BlankPointForSmoke(false, true), false), (taskbar.BlankPointForSmoke(false, false), false), (taskbar.BlankPointForSmoke(true, true), true), (taskbar.BlankPointForSmoke(true, false), true) })
                {
                    activeFlyout?.Hide(); activeFlyout = null;
                    await UiSmokeInput.ClickAsync(taskbar, point);
                    clicksWork &= expectMusic ? musicWindow?.IsVisible == true : detailsWindow.IsVisible;
                }
                blankClicksAtOpacity[transparent.ToString()] = clicksWork;
            }
            taskbar!.ApplyAppearance(Settings);
            await Task.Delay(100);
            activeFlyout?.Hide(); activeFlyout = null;
            detailsWindow.Hide(); activeFlyout = null;
            await taskbar!.ClickBlankRegionForSmokeAsync(false);
            var regionClicksOpen = detailsWindow.IsVisible;
            await taskbar.ClickBlankRegionForSmokeAsync(true);
            regionClicksOpen &= musicWindow?.IsVisible == true && !detailsWindow.IsVisible;
            await taskbar.ClickBlankRegionForSmokeAsync(true);
            regionClicksOpen &= musicWindow?.IsVisible == false;
            await taskbar.ClickBlankRegionForSmokeAsync(true);
            regionClicksOpen &= musicWindow?.IsVisible == true;
            // Test one physical outside click on a non-activating, app-owned test window.
            var outsideTarget = new Window { Width = 36, Height = 36, Left = SystemParameters.WorkArea.Left + 20, Top = SystemParameters.WorkArea.Top + 20,
                ShowInTaskbar = false, ShowActivated = false, Topmost = true, WindowStyle = WindowStyle.None, Background = Brushes.DimGray };
            outsideTarget.SourceInitialized += (_, _) => System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(outsideTarget).Handle).AddHook(
                (IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled) => { if (msg == 0x0021) { handled = true; return new IntPtr(3); } return IntPtr.Zero; });
            outsideTarget.Show();
            if (musicWindow is null) throw new InvalidOperationException($"클릭 검증 실패: context={contextSettingsAccessible}, center={centerPlacementApplied}, regions={regionClicksOpen}, opacity={JsonSerializer.Serialize(blankClicksAtOpacity)}");
            musicWindow.DismissOnDeactivate = true;
            var outsideHookInstalled = musicWindow.OutsideClickMonitorActive;
            await UiSmokeInput.ClickAsync(outsideTarget, new Point(18, 18));
            var singleOutsideClickCloses = !musicWindow.IsVisible && !musicWindow.OutsideClickMonitorActive;
            outsideTarget.Close();
            OpenMusic(); await musicWindow!.LoadAsync(); await Task.Delay(300);
            var flowOffsetBefore = taskbar?.MusicOffset; await Task.Delay(1000);
            SaveImage(musicWindow, Path.Combine(directory, "music.png"));
            var musicScrollWorks = await musicWindow.CheckScrollingForSmokeAsync();
            SaveImage(musicWindow, Path.Combine(directory, "music-scrolled.png"));
            var singleFlyout = Windows.OfType<FlyoutWindow>().Count(window => window.IsVisible) == 1;
            var flowOffsetAfter = taskbar?.MusicOffset;
            // Freeze polling to sample the surrounding-music transition without another snapshot racing it.
            await poller.DisposeAsync();
            var ownState = Snapshot!;
            var weatherHealthDisplayed = ownState.Environment is not null && ownState.Health is not null && taskbar!.EnvironmentText == ownState.Environment.TimeText && taskbar.HealthRatio == ownState.Health.Ratio && taskbar.WeatherKind == "sun";
            var settingsSpaceReclaimed = taskbar!.ContentSpaceWidth >= 300;
            var queueUnified = taskbar!.QueueButtonWidth <= 84 && taskbar.QueueButtonWidth > 0 && Math.Abs(taskbar.ActualWidth - 480) < 1;
            activeFlyout?.Hide(); activeFlyout = null;
            await taskbar.ClickQueueForSmokeAsync();
            var queueClickOpens = detailsWindow.IsVisible;
            activeFlyout?.Hide(); activeFlyout = null;
            var weatherIconsSupported = true;
            foreach (var (weather, expected) in new[] { ("Sunny", "sun"), ("Cloudy", "cloud"), ("Rainy", "rain"), ("Snowy", "snow"), ("Thunderstorm", "storm"), ("Foggy", "fog"), ("PartlyCloudy", "partly"), ("FutureWeather", "unknown") })
            {
                taskbar.Update(ownState with { Environment = new EnvironmentState(weather, "10:44") });
                weatherIconsSupported &= taskbar.WeatherKind == expected && taskbar.EnvironmentText == "10:44";
                SaveImage(taskbar, Path.Combine(directory, $"weather-{expected}.png"));
            }
            var alive = ownState with { Health = new HealthState(25, 100), Activity = ownState.Activity! with { IsDead = false } };
            taskbar.Update(alive);
            var healthRatioApplied = taskbar.HealthRatio == 0.25 && !taskbar.HealthAlertAnimating && taskbar.HealthFillColor == Color.FromRgb(244, 133, 180);
            SaveImage(taskbar, Path.Combine(directory, "health-low.png"));
            taskbar.Update(alive with { Health = new HealthState(0, 100) });
            healthRatioApplied &= taskbar.HealthRatio == 0 && !taskbar.HealthAlertAnimating;
            var deadState = alive with { Health = new HealthState(0, 100), Activity = alive.Activity! with { IsDead = true } };
            taskbar.Update(deadState); await Task.Delay(100);
            var deathOpacity = taskbar.HealthAlertOpacity;
            taskbar.Update(deadState); await Task.Delay(300);
            var healthAlertFlashes = taskbar.HealthAlertAnimating && taskbar.HealthFillColor == Color.FromRgb(255, 67, 84) && Math.Abs(deathOpacity - taskbar.HealthAlertOpacity) > 0.05;
            SaveImage(taskbar, Path.Combine(directory, "health-dead.png"));
            taskbar.Update(alive);
            var healthRevived = !taskbar.HealthAlertAnimating && taskbar.HealthAlertOpacity == 1 && taskbar.HealthFillColor == Color.FromRgb(244, 133, 180);
            taskbar.Update(ownState with { Environment = null, Health = null, EnvironmentUnavailableReason = "모의 조회 실패", HealthUnavailableReason = "모의 조회 실패" });
            var statusFieldsCleared = taskbar.EnvironmentText == "--:--" && taskbar.WeatherKind == "unknown" && taskbar.HealthRatio == 0 && !taskbar.HealthAlertAnimating;
            SaveImage(taskbar, Path.Combine(directory, "status-unavailable.png"));
            taskbar.Update(deadState);
            taskbar.Update(StatusSnapshot.Unavailable("연결 끊김 검증", TimeSpan.FromSeconds(2)));
            statusFieldsCleared &= taskbar.EnvironmentText == "--:--" && taskbar.HealthRatio == 0 && !taskbar.HealthAlertAnimating;
            taskbar.Update(ownState);
            var nearbyResponse = await Client.QueryNearbyAsync(Settings.CliPath, default);
            var nearby = NearbyPerformance.ParseNearest(nearbyResponse!.Value)!;
            var surroundings = ownState with { Activity = ownState.Activity! with { Music = ownState.Activity.Music with { IsPlaying = false } }, NearbyMusic = nearby };
            taskbar!.ApplyAppearance(Settings with { PlaybackEffect = MusicEffect.None });
            taskbar.Update(surroundings);
            var nearbyPhase = taskbar.EffectPhase; await Task.Delay(200);
            var nearbyDisplayed = taskbar.NearbyDisplayed && taskbar.MusicIsMuted && taskbar.EffectAnimating && taskbar.EffectPhase > nearbyPhase && taskbar.SummaryText.Contains(nearby.Title);
            SaveImage(taskbar, Path.Combine(directory, "nearby-music.png"));
            taskbar.ApplyAppearance(Settings); taskbar.Update(ownState with { NearbyMusic = nearby });
            var ownMusicPrioritized = !taskbar.NearbyDisplayed && !taskbar.MusicIsMuted && taskbar.EffectAnimating;
            taskbar.Update(surroundings with { NearbyMusic = null });
            var nearbyCleared = !taskbar.NearbyDisplayed && !taskbar.EffectAnimating && !taskbar.SummaryText.Contains(nearby.Title);
            taskbar.Update(StatusSnapshot.Unavailable("연결 끊김 검증", TimeSpan.FromSeconds(2)));
            nearbyCleared &= !taskbar.EffectAnimating;
            nearbyCleared &= !taskbar.PlaybackVisible;
            taskbar.Update(ownState);
            File.WriteAllText(Path.Combine(directory, "diagnostics.json"), JsonSerializer.Serialize(new
            {
                attached = taskbar?.Host.IsAttached, firstParentClass, parentClass = taskbar?.ParentClass, attachError, summary = taskbar?.SummaryText,
                connected = Snapshot?.Connected, errors = Snapshot?.Errors,
                interval = Settings.PollIntervalSeconds, placement = Settings.Placement.ToString(), width = taskbar?.ActualWidth, height = taskbar?.ActualHeight,
                onRight = taskbar?.IsOnRight, connectionTestSucceeded, connectionFailureShown,
                musicTitles = musicWindow.Titles, musicFlowing = taskbar?.MusicFlowing, detailTitleFlowing,
                flowOffsetBefore, flowOffsetAfter,
                singleFlyout, outsideDismissed, escapeDismissed, toggleDismissed, modalHoldProtected,
                flyoutsFit = settingsFits && detailsFits && musicWindow.FitsWorkArea,
                noFlyoutTaskbarButtons = Windows.OfType<FlyoutWindow>().All(window => !window.ShowInTaskbar && window.WindowStyle == WindowStyle.None),
                musicScrollbarVisible = musicWindow.HasScrollbar, musicScrollWorks,
                flyoutsPreparedHidden = Windows.OfType<FlyoutWindow>().All(window => window.HiddenBeforeShow),
                repeatedPresentationStable, placementApplied, placementPositions, centerPlacementApplied, contextSettingsAccessible, settingsSpaceReclaimed,
                appearanceApplied, effectsAnimate, effectsStop,
                nearbyDisplayed, ownMusicPrioritized, nearbyCleared,
                timeSettingApplied, regionClicksOpen, outsideHookInstalled, singleOutsideClickCloses,
                weatherHealthDisplayed, weatherIconsSupported, healthRatioApplied, healthAlertFlashes, healthRevived, statusFieldsCleared, queueUnified, queueClickOpens,
                blankClicksAtOpacity
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Directory.CreateDirectory(directory); File.WriteAllText(Path.Combine(directory, "smoke-error.txt"), ex.ToString()); }
        finally { await ExitAsync(); }
    }
    private static void SaveImage(Window window, string path)
    {
        window.UpdateLayout();
        var visual = window is FlyoutWindow ? window : (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(visual.ActualWidth)), Math.Max(1, (int)Math.Ceiling(visual.ActualHeight)), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
}






