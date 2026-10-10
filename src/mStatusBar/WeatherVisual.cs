using System.Windows;
using System.Windows.Media;

namespace mStatusBar;

// Draw weather locally so icons stay legible without emoji fonts or external assets.
internal sealed class WeatherVisual : FrameworkElement
{
    private string kind = "unknown";
    internal string Kind => kind;
    public WeatherVisual() { Width = 18; Height = 18; }
    public void SetWeather(string? weather)
    {
        var value = weather?.Trim().ToLowerInvariant() ?? "";
        kind = value switch
        {
            "sunny" or "clear" or "fair" or "맑음" => "sun",
            "cloudy" or "overcast" or "흐림" => "cloud",
            "partlycloudy" or "partly cloudy" => "partly",
            "rain" or "rainy" or "raining" or "비" => "rain",
            "snow" or "snowy" or "snowing" or "눈" => "snow",
            "thunder" or "thunderstorm" or "storm" or "stormy" => "storm",
            "fog" or "foggy" or "mist" or "misty" => "fog",
            _ => "unknown"
        };
        ToolTip = string.IsNullOrWhiteSpace(weather) ? "날씨 조회 대기" : "에린 날씨: " + weather;
        System.Windows.Automation.AutomationProperties.SetName(this, ToolTip.ToString());
        InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var light = new SolidColorBrush(Color.FromRgb(182, 203, 218));
        var gold = new SolidColorBrush(Color.FromRgb(255, 209, 105));
        var blue = new SolidColorBrush(Color.FromRgb(123, 194, 245));
        if (kind is "sun" or "partly")
        {
            var center = kind == "sun" ? new Point(9, 9) : new Point(6, 6);
            var radius = kind == "sun" ? 3.3 : 2.5;
            dc.DrawEllipse(gold, null, center, radius, radius);
            for (var i = 0; i < 8; i++)
            {
                var angle = Math.PI * i / 4;
                dc.DrawLine(new Pen(gold, 1.1), new Point(center.X + Math.Cos(angle) * (radius + 1.5), center.Y + Math.Sin(angle) * (radius + 1.5)),
                    new Point(center.X + Math.Cos(angle) * (radius + 3), center.Y + Math.Sin(angle) * (radius + 3)));
            }
        }
        if (kind is "cloud" or "partly" or "rain" or "snow" or "storm")
        {
            dc.DrawEllipse(light, null, new Point(5, 9), 3, 3);
            dc.DrawEllipse(light, null, new Point(9, 7), 3.8, 3.8);
            dc.DrawEllipse(light, null, new Point(13, 9), 3, 3);
            dc.DrawRoundedRectangle(light, null, new Rect(4, 9, 10, 3), 1, 1);
        }
        if (kind == "rain")
            foreach (var x in new[] { 5.0, 9, 13 }) dc.DrawLine(new Pen(blue, 1.4), new Point(x, 13), new Point(x - 1, 16));
        if (kind == "snow")
            foreach (var x in new[] { 5.0, 10, 14 }) dc.DrawEllipse(Brushes.White, null, new Point(x, 15), 1, 1);
        if (kind == "storm")
        {
            var bolt = Geometry.Parse("M 10,11 L 7,15 L 10,15 L 8,18 L 14,13 L 10,13 Z");
            dc.DrawGeometry(gold, null, bolt);
        }
        if (kind == "fog")
            foreach (var y in new[] { 5.0, 9, 13 }) dc.DrawLine(new Pen(light, 1.6), new Point(3, y), new Point(15, y));
        if (kind == "unknown")
        {
            dc.DrawEllipse(null, new Pen(light, 1.2), new Point(9, 9), 6, 6);
            dc.DrawLine(new Pen(light, 1.5), new Point(6, 9), new Point(12, 9));
        }
    }
}
