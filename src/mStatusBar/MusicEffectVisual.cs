using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using mStatusBar.Core;

namespace mStatusBar;

// A decorative playback indicator. It does not sample audio or represent its spectrum.
internal sealed class MusicEffectVisual : FrameworkElement
{
    private readonly DispatcherTimer timer;
    private readonly Stopwatch clock = new();
    private MusicEffect effect;
    private bool playing;
    private bool nearby;
    internal bool IsNearby => nearby;
    public bool IsAnimating => timer.IsEnabled;
    public double Phase => clock.Elapsed.TotalSeconds;
    public MusicEffect SelectedEffect => effect;
    public MusicEffectVisual()
    {
        Width = 28; Height = 16; IsHitTestVisible = false;
        timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) => InvalidateVisual();
        Loaded += (_, _) => Refresh();
        IsVisibleChanged += (_, _) => Refresh();
        Unloaded += (_, _) => { timer.Stop(); clock.Stop(); };
    }
    public void SetState(MusicEffect selected, bool isPlaying, bool isNearby = false)
    {
        if (effect == selected && playing == isPlaying && nearby == isNearby) return;
        effect = selected; playing = isPlaying; nearby = isNearby; Refresh();
    }
    private void Refresh()
    {
        Visibility = playing && (nearby || effect != MusicEffect.None) ? Visibility.Visible : Visibility.Collapsed;
        if (IsLoaded && IsVisible) { clock.Start(); timer.Start(); }
        else { timer.Stop(); clock.Reset(); }
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (!playing || !nearby && effect == MusicEffect.None) return;
        var t = Phase;
        if (nearby)
        {
            for (var i = 0; i < 3; i++)
            {
                var progress = (t / 2.4 + i / 3.0) % 1;
                var radius = 1.5 + progress * 6;
                var ring = new SolidColorBrush(Color.FromArgb((byte)(145 * (1 - progress)), 188, 203, 214));
                dc.DrawEllipse(null, new Pen(ring, 1.2), new Point(14, 8), radius, radius);
            }
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(130, 188, 203, 214)), null, new Point(14, 8), 1.4, 1.4);
            return;
        }
        var brush = new SolidColorBrush(Color.FromArgb(155, 101, 218, 183));
        var pen = new Pen(brush, 1.5) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        switch (effect)
        {
            case MusicEffect.Equalizer:
                for (var i = 0; i < 5; i++)
                {
                    var height = 3 + 10 * (0.5 + 0.5 * Math.Sin(t * (2.6 + i * 0.3) + i * 1.7));
                    dc.DrawRoundedRectangle(brush, null, new Rect(2 + i * 5, (16 - height) / 2, 3, height), 1.4, 1.4);
                }
                break;
            case MusicEffect.Wave:
                for (var x = 1; x < 27; x++)
                    dc.DrawLine(pen, new Point(x, 8 + Math.Sin(x * 0.42 - t * 3) * 4), new Point(x + 1, 8 + Math.Sin((x + 1) * 0.42 - t * 3) * 4));
                break;
            case MusicEffect.Pulse:
                for (var i = 0; i < 3; i++)
                {
                    var radius = 1.8 + (0.5 + 0.5 * Math.Sin(t * 2.5 - i * 0.8)) * 1.6;
                    dc.DrawEllipse(brush, null, new Point(5 + i * 9, 8), radius, radius);
                }
                break;
            case MusicEffect.Orbit:
                dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(55, 101, 218, 183)), 1), new Point(14, 8), 10, 5);
                for (var i = 0; i < 2; i++)
                    dc.DrawEllipse(brush, null, new Point(14 + Math.Cos(t * 1.8 + i * Math.PI) * 10, 8 + Math.Sin(t * 1.8 + i * Math.PI) * 5), 2, 2);
                break;
        }
    }
    public static SolidColorBrush BackgroundBrush(AppSettings settings)
    {
        var color = (Color)ColorConverter.ConvertFromString(settings.BackgroundColor);
        color.A = (byte)Math.Round(255 * (100 - settings.BackgroundTransparencyPercent) / 100.0);
        return new SolidColorBrush(color);
    }
}
