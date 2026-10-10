using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using mStatusBar.Core;
namespace mStatusBar;
internal sealed class DetailsWindow : FlyoutWindow
{
    private readonly App app;
    private readonly StackPanel panel = new() { Margin = new Thickness(24) };
    private readonly StackPanel header = new();
    private readonly Border musicArea = new();
    private readonly StackPanel worksArea = new();
    private string? musicKey;
    private MarqueeText? titleView;
    public bool TitleFlowing => titleView?.IsFlowing ?? false;
    private readonly DispatcherTimer timer;
    private TextBlock? playback;
    private ProgressBar? progress;
    private StatusSnapshot? snapshot;
    public DetailsWindow(App app)
    {
        this.app = app; Ui.Style(this, "mStatusBar · 작업과 음악", 640, 650);
        panel.Children.Add(header); panel.Children.Add(musicArea); panel.Children.Add(worksArea);
        Content = new ScrollViewer { Content = panel, Background = Ui.Background, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => UpdatePlayback(), Dispatcher);
        Closed += (_, _) => timer.Stop(); Update(app.Snapshot);
    }
    public void Update(StatusSnapshot? value)
    {
        snapshot = value; header.Children.Clear(); worksArea.Children.Clear();
        header.Children.Add(Ui.Text("작업과 음악", 24, Ui.Accent));
        if (snapshot is null) { header.Children.Add(Ui.Text("첫 상태 조회를 기다립니다.")); return; }
        header.Children.Add(Ui.Text(snapshot.Connected ? snapshot.Activity?.Summary ?? snapshot.Message : snapshot.Message, 14, Ui.Muted));
        foreach (var error in snapshot.Errors) header.Children.Add(Ui.Text(error, 13, Ui.Muted));
        if (!snapshot.Connected) { musicArea.Child = null; musicKey = null; return; }
        if (snapshot.Activity is { Music.IsPlaying: true } state) AddMusic(state.Music);
        else { musicArea.Child = null; musicKey = null; }
        if (app.ActionMessage.Length > 0) worksArea.Children.Add(Ui.Text(app.ActionMessage, 14, Ui.Accent));
        if (snapshot.Queue is null) { worksArea.Children.Add(Ui.Text("작업 큐 조회 실패")); return; }
        AddWorks("◷ 진행 중", snapshot.Queue.Works.Where(w => !w.Completed && w.State == "InProgress").ToArray(), false);
        AddWorks("✓ 수령 가능", snapshot.Queue.Works.Where(w => w.Completed).ToArray(), true);
        worksArea.Children.Add(Ui.Text($"마지막 조회 {snapshot.UpdatedAt:HH:mm:ss}", 11, Ui.Muted));
    }
    private void AddMusic(MusicState music)
    {
        var key = $"{music.Title}\n{music.Instrument}\n{music.TotalSeconds}\n{music.IsLoop}";
        if (key == musicKey) { UpdatePlayback(); return; }
        musicKey = key;
        var content = new StackPanel(); content.Children.Add(Ui.Text("♫  NOW PLAYING", 11, Ui.Accent));
        var title = new MarqueeText(27, Ui.Foreground) { Text = music.Title, Margin = new Thickness(0, 0, 0, 12), Background = Brushes.Transparent };
        titleView = title;
        title.MouseLeftButtonUp += (_, _) => app.OpenMusic(); title.ToolTip = "클릭하여 보유 악보 선택"; content.Children.Add(title);
        content.Children.Add(Ui.Text($"{music.Instrument}   ·   {(music.IsLoop ? "↻ 반복 연주" : "연주 중")}", 13, Ui.Muted));
        progress = new ProgressBar { Height = 5, Maximum = music.TotalSeconds > 0 ? music.TotalSeconds : 1, Foreground = Ui.Accent, Background = new SolidColorBrush(Color.FromRgb(55, 64, 91)), BorderThickness = new Thickness(0) }; content.Children.Add(progress);
        playback = Ui.Text("", 16); playback.FontFamily = new FontFamily("Consolas"); playback.Margin = new Thickness(0, 12, 0, 0); content.Children.Add(playback);
        musicArea.Child = new Border { Child = content, Padding = new Thickness(22), CornerRadius = new CornerRadius(14), Margin = new Thickness(0, 6, 0, 22), Background = new LinearGradientBrush(Color.FromRgb(38, 45, 78), Color.FromRgb(24, 58, 57), 30), BorderBrush = new SolidColorBrush(Color.FromRgb(63, 83, 97)), BorderThickness = new Thickness(1) };
        UpdatePlayback();
    }
    private void UpdatePlayback()
    {
        if (snapshot?.Activity is not { Music.IsPlaying: true } state || playback is null || progress is null) return;
        var music = state.Music; var elapsed = music.ElapsedSeconds + Math.Max(0, (DateTimeOffset.Now - snapshot.UpdatedAt).TotalSeconds);
        if (music.TotalSeconds > 0) elapsed = music.IsLoop ? elapsed % music.TotalSeconds : Math.Min(elapsed, music.TotalSeconds);
        playback.Text = $"{DisplayTime.Duration(elapsed)}  /  {(music.TotalSeconds > 0 ? DisplayTime.Duration(music.TotalSeconds) : "--:--")}";
        progress.IsIndeterminate = music.TotalSeconds <= 0; progress.Value = elapsed;
    }
    private void AddWorks(string title, IReadOnlyList<WorkState> works, bool completed)
    {
        worksArea.Children.Add(Ui.Text($"{title}   {works.Count}", 18, completed ? Ui.Accent : Ui.Foreground));
        if (works.Count == 0) { worksArea.Children.Add(Ui.Text(completed ? "수령할 완료품이 없습니다." : "진행 중인 작업이 없습니다.", 13, Ui.Muted)); return; }
        foreach (var work in works)
        {
            var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var text = new StackPanel(); var name = Ui.Text(work.Name, 16); name.FontWeight = FontWeights.SemiBold; name.Margin = new Thickness(0, 0, 0, 4); text.Children.Add(name);
            var meta = Ui.Text(work.Facility + (completed ? " · 완료" : $" · 남은 시간 {DisplayTime.Duration(work.RemainingSeconds)}"), 12, Ui.Muted); meta.Margin = new Thickness(0); text.Children.Add(meta); row.Children.Add(text);
            if (completed)
            {
                var button = Ui.Button(app.IsActing ? "처리 중…" : "수령하기 →", async (_, _) => await app.CollectWorkAsync(work)); button.IsEnabled = !app.IsActing;
                button.VerticalAlignment = VerticalAlignment.Center; button.ToolTip = $"{work.Facility}로 이동해 이 시설의 완료품을 모두 수령합니다."; Grid.SetColumn(button, 1); row.Children.Add(button);
            }
            worksArea.Children.Add(new Border { Child = row, Padding = new Thickness(16, 13, 16, 13), CornerRadius = new CornerRadius(9), Background = new SolidColorBrush(completed ? Color.FromRgb(29, 52, 48) : Color.FromRgb(32, 42, 57)), Margin = new Thickness(0, 0, 0, 9) });
        }
    }
}
