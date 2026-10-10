using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using mStatusBar.Core;
namespace mStatusBar;
internal sealed class MusicWindow : FlyoutWindow
{
    private readonly App app;
    private readonly StackPanel list = new();
    private readonly TextBlock message, caption;
    private readonly ScrollViewer scroll;
    private readonly System.Windows.Controls.Button refresh;
    private readonly CancellationTokenSource cancellation = new();
    private IReadOnlyList<string> titles = [];
    private Task? loading;
    private string? rowsKey;
    public IReadOnlyList<string> Titles => titles;
    public bool HasScrollbar => scroll.ComputedVerticalScrollBarVisibility == Visibility.Visible;
    internal async Task<bool> CheckScrollingForSmokeAsync()
    {
        scroll.PageDown();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        return scroll.VerticalOffset > 0;
    }
    public MusicWindow(App app)
    {
        this.app = app; Ui.Style(this, "mStatusBar · 보유 악보", 480, 580);
        var panel = new DockPanel { Margin = new Thickness(24), Background = Ui.Background, LastChildFill = true };
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var top = new Grid(); top.ColumnDefinitions.Add(new ColumnDefinition()); top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = Ui.Text("보유 악보", 23); heading.FontWeight = FontWeights.SemiBold; heading.Margin = new Thickness(0); top.Children.Add(heading);
        refresh = Ui.Button("↻", async (_, _) => await LoadAsync()); refresh.Width = 36; refresh.Height = 34; refresh.Padding = new Thickness(0); refresh.Margin = new Thickness(0); refresh.FontSize = 19; refresh.ToolTip = "악보 목록 새로고침";
        Grid.SetColumn(refresh, 1); top.Children.Add(refresh); header.Children.Add(top);
        caption = Ui.Text("가나다순 · 선택하면 바로 연주합니다", 12, Ui.Muted); caption.Margin = new Thickness(0, 8, 0, 0); header.Children.Add(caption);
        message = Ui.Text("악보 목록을 불러옵니다…", 12, Ui.Accent); message.Margin = new Thickness(0, 10, 0, 0); header.Children.Add(message);
        DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        scroll = new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 8, 0), CanContentScroll = false };
        panel.Children.Add(scroll); Content = new Border { Background = Ui.Background, Child = panel };
        Closed += (_, _) => cancellation.Cancel();
    }
    public Task LoadAsync()
    {
        if (loading is { IsCompleted: false }) return loading;
        loading = LoadCoreAsync(); return loading;
    }
    private async Task LoadCoreAsync()
    {
        refresh.IsEnabled = false; message.Text = "악보 목록을 불러옵니다…";
        try
        {
            var path = app.Settings.CliPath;
            await app.Client.CheckStatusAsync(path, cancellation.Token);
            var data = await app.Client.QueryAsync(path, "get_music_scores", cancellation.Token);
            if (cancellation.IsCancellationRequested || path != app.Settings.CliPath) return;
            titles = MusicCatalog.Titles(data); UpdateState();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { message.Text = ex.Message; }
        finally { if (!cancellation.IsCancellationRequested) refresh.IsEnabled = true; }
    }
    public void UpdateState()
    {
        caption.Text = $"가나다순 · {titles.Count}개의 악보 · 선택하면 바로 연주합니다";
        message.Text = app.ActionMessage.Length > 0 ? app.ActionMessage : titles.Count == 0 ? "보유 악보가 없습니다." : "";
        message.Visibility = message.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        var playing = app.Snapshot?.Activity?.Music is { IsPlaying: true } music ? music.Title : "";
        var key = string.Join("\n", titles) + "\n" + app.IsActing + "\n" + playing;
        if (rowsKey == key) return; rowsKey = key;
        var offset = scroll.VerticalOffset; list.Children.Clear();
        foreach (var title in titles)
        {
            var current = title == playing;
            var button = Ui.Button("", async (_, _) => await app.PlayMusicAsync(title));
            button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch; button.Height = 62;
            button.Padding = new Thickness(12, 10, 12, 10); button.Margin = new Thickness(0, 0, 0, 7); button.IsEnabled = !app.IsActing;
            button.Background = new SolidColorBrush(current ? Color.FromRgb(28, 61, 54) : Color.FromRgb(31, 41, 56));
            button.BorderBrush = new SolidColorBrush(current ? Color.FromRgb(69, 131, 111) : Color.FromRgb(47, 61, 77)); button.ToolTip = title;
            var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) }); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            var glyph = new TextBlock { Text = current ? "♫" : "♪", Foreground = current ? Ui.Accent : Ui.Muted, FontSize = 18, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(glyph);
            var name = new TextBlock { Text = title, Foreground = Ui.Foreground, FontSize = 14, FontWeight = current ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(name, 1); row.Children.Add(name);
            var play = new TextBlock { Text = current ? "●" : "▶", FontSize = 11, Foreground = Ui.Accent, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(play, 2); row.Children.Add(play); button.Content = row; list.Children.Add(button);
        }
        Dispatcher.InvokeAsync(() => scroll.ScrollToVerticalOffset(offset), System.Windows.Threading.DispatcherPriority.Loaded);
    }
}
