using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace mStatusBar;
internal sealed class MarqueeText : Grid
{
    private readonly Canvas canvas = new();
    private readonly TextBlock first;
    private readonly TextBlock second;
    private readonly TranslateTransform movement = new();
    private string text = "";
    public bool IsFlowing => movement.HasAnimatedProperties;
    public double Offset => movement.X;
    public Brush TextBrush { get => first.Foreground; set => first.Foreground = second.Foreground = value; }
    public string Text { get => text; set { if (text == value) return; text = value; first.Text = second.Text = value; ArrangeFlow(); } }
    public MarqueeText(double size, Brush foreground)
    {
        Height = size * 1.5; ClipToBounds = true;
        first = new TextBlock { FontSize = size, Foreground = foreground }; second = new TextBlock { FontSize = size, Foreground = foreground };
        canvas.RenderTransform = movement; canvas.Children.Add(first); canvas.Children.Add(second); Children.Add(canvas);
        SizeChanged += (_, _) => ArrangeFlow(); Loaded += (_, _) => ArrangeFlow();
        Unloaded += (_, _) => movement.BeginAnimation(TranslateTransform.XProperty, null);
    }
    private void ArrangeFlow()
    {
        movement.BeginAnimation(TranslateTransform.XProperty, null); movement.X = 0;
        first.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var width = first.DesiredSize.Width;
        second.Visibility = width > ActualWidth && ActualWidth > 0 ? Visibility.Visible : Visibility.Hidden;
        if (second.Visibility != Visibility.Visible) return;
        var distance = width + 42; Canvas.SetLeft(second, distance);
        movement.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, -distance, TimeSpan.FromSeconds(distance / 30)) { RepeatBehavior = RepeatBehavior.Forever });
    }
}
