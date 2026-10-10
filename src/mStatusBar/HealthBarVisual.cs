using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using mStatusBar.Core;

namespace mStatusBar;

internal sealed class HealthBarVisual : FrameworkElement
{
    private static readonly Brush Track = new SolidColorBrush(Color.FromRgb(62, 42, 57));
    private static readonly Brush Pink = new SolidColorBrush(Color.FromRgb(244, 133, 180));
    private static readonly Brush Red = new SolidColorBrush(Color.FromRgb(255, 67, 84));
    private HealthState? health;
    private bool dead;
    internal double Ratio => health?.Ratio ?? 0;
    internal bool AlertAnimating { get; private set; }
    internal Color FillColor => ((SolidColorBrush)(dead ? Red : Pink)).Color;
    public HealthBarVisual()
    {
        Width = 8; Height = 34;
        IsVisibleChanged += (_, _) => UpdateAlert();
        Loaded += (_, _) => UpdateAlert();
        Unloaded += (_, _) => StopAlert();
    }
    public void SetState(HealthState? value, bool isDead, string? reason)
    {
        health = value; dead = isDead;
        ToolTip = isDead ? "캐릭터 사망" : value is null ? "HP: " + (reason ?? "조회 대기") : $"HP {value.Current:N0} / {value.Maximum:N0} ({value.Ratio:P0})";
        System.Windows.Automation.AutomationProperties.SetName(this, ToolTip.ToString());
        InvalidateVisual(); UpdateAlert();
    }
    private void UpdateAlert()
    {
        if (!dead || !IsVisible || !IsLoaded) { StopAlert(); return; }
        if (AlertAnimating) return;
        AlertAnimating = true;
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(550))
        { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
    }
    private void StopAlert()
    {
        if (!AlertAnimating) return;
        BeginAnimation(OpacityProperty, null); Opacity = 1; AlertAnimating = false;
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var rect = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRoundedRectangle(Track, null, rect, 3, 3);
        var ratio = dead ? 1 : Ratio;
        if (ratio <= 0) return;
        dc.PushClip(new RectangleGeometry(rect, 3, 3));
        dc.DrawRectangle(dead ? Red : Pink, null, new Rect(0, ActualHeight * (1 - ratio), ActualWidth, ActualHeight * ratio));
        dc.Pop();
    }
}
