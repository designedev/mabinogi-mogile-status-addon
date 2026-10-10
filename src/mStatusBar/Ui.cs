using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using Orientation = System.Windows.Controls.Orientation;

namespace mStatusBar;

internal static class Ui
{
    public static readonly Brush Background = new SolidColorBrush(Color.FromRgb(23, 29, 40));
    public static readonly Brush Foreground = new SolidColorBrush(Color.FromRgb(234, 241, 250));
    public static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(159, 176, 198));
    public static readonly Brush Accent = new SolidColorBrush(Color.FromRgb(101, 218, 183));
    public static TextBlock Text(string text, double size = 14, Brush? color = null) => new()
    {
        Text = text, FontSize = size, Foreground = color ?? Foreground,
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
    };
    public static Button Button(string text, RoutedEventHandler click)
    {
        var button = new Button { Content = text, Padding = new Thickness(14, 8, 14, 8), Margin = new Thickness(0, 0, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(44, 59, 76)), Foreground = Foreground,
            BorderBrush = new SolidColorBrush(Color.FromRgb(78, 99, 122)) };
        button.Click += click;
        return button;
    }
    public static void Style(Window window, string title, double width, double height)
    {
        window.Title = title; window.Width = width; window.Height = height;
        window.MinWidth = window is FlyoutWindow ? 0 : width; window.MinHeight = window is FlyoutWindow ? 0 : height;
        window.Background = window is FlyoutWindow ? Brushes.Transparent : Background; window.Foreground = Foreground;
        window.FontFamily = new FontFamily("Malgun Gothic");
        window.WindowStartupLocation = window is FlyoutWindow ? WindowStartupLocation.Manual : WindowStartupLocation.CenterScreen;
    }
    public static StackPanel Row() => new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 10) };
}
