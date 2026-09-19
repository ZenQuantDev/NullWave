using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;

namespace NullWave.Views.Controls;

public partial class RadarChartControl : UserControl
{
    public static readonly StyledProperty<Dictionary<string, double>?> DataProperty =
        AvaloniaProperty.Register<RadarChartControl, Dictionary<string, double>?>(nameof(Data));

    public Dictionary<string, double>? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }

    static RadarChartControl()
    {
        DataProperty.Changed.AddClassHandler<RadarChartControl>((x, e) => x.Draw());
    }

    public RadarChartControl() { InitializeComponent(); }

    private void Draw()
    {
        ChartCanvas.Children.Clear();
        if (Data == null || Data.Count == 0) return;

        var center = new Point(130, 130);
        double radius = 90;
        int n = Data.Count;
        var accentColor = (Application.Current?.Resources["ColorAccent"] as Color?) ?? Colors.DodgerBlue;

        // Web rings
        for (double frac = 0.25; frac <= 1.0; frac += 0.25)
        {
            var pts = new List<Point>();
            for (int i = 0; i < n; i++)
            {
                double angle = -Math.PI / 2 + i * 2 * Math.PI / n;
                pts.Add(new Point(center.X + radius * frac * Math.Cos(angle), center.Y + radius * frac * Math.Sin(angle)));
            }
            ChartCanvas.Children.Add(new Polygon { Points = new Points(pts), Stroke = Brushes.Gray, StrokeThickness = 0.5, Opacity = 0.3 });
        }

        // Data polygon
        var polyPts = new List<Point>();
        int idx = 0;
        foreach (var (label, value) in Data)
        {
            double angle = -Math.PI / 2 + idx * 2 * Math.PI / n;
            double r = radius * Math.Clamp(value, 0, 1);
            polyPts.Add(new Point(center.X + r * Math.Cos(angle), center.Y + r * Math.Sin(angle)));

            var labelText = new TextBlock { Text = label, FontSize = 10, Foreground = Brushes.Gray };
            Canvas.SetLeft(labelText, center.X + (radius + 14) * Math.Cos(angle) - 20);
            Canvas.SetTop(labelText, center.Y + (radius + 14) * Math.Sin(angle) - 6);
            ChartCanvas.Children.Add(labelText);
            idx++;
        }
        ChartCanvas.Children.Add(new Polygon { Points = new Points(polyPts), Fill = new SolidColorBrush(accentColor, 0.35), Stroke = new SolidColorBrush(accentColor), StrokeThickness = 2 });
    }
}
