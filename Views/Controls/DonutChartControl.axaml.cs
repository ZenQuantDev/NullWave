using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.Shapes;

namespace NullWave.Views.Controls;

public partial class DonutChartControl : UserControl
{
    public static readonly StyledProperty<Dictionary<string, double>?> DataProperty =
        AvaloniaProperty.Register<DonutChartControl, Dictionary<string, double>?>(nameof(Data));

    public Dictionary<string, double>? Data { get => GetValue(DataProperty); set => SetValue(DataProperty, value); }
    
    public string DominantMood => Data?.OrderByDescending(x => x.Value).FirstOrDefault().Key ?? "-";

    static DonutChartControl()
    {
        DataProperty.Changed.AddClassHandler<DonutChartControl>((x, e) => x.Draw());
    }

    public DonutChartControl() 
    { 
        InitializeComponent();
        this.DataContext = this; // Bind DominantMood to self
    }

    private void Draw()
    {
        ChartCanvas.Children.Clear();
        if (Data == null || Data.Count == 0) return;

        var center = new Point(130, 130);
        double outerR = 100, innerR = 65;
        double startAngle = -90;

        foreach (var (mood, value) in Data)
        {
            if (value <= 0) continue;
            double sweep = value * 360;
            var geo = BuildArcSegmentGeometry(center, outerR, innerR, startAngle, sweep);
            var path = new Path { Data = geo, Fill = new SolidColorBrush(ColorForMood(mood)) };
            ChartCanvas.Children.Add(path);
            startAngle += sweep;
        }
    }

    private static Color ColorForMood(string mood) => mood switch
    {
        "Energetic" => Color.Parse("#F59E0B"), // Amber
        "Melancholy" => Color.Parse("#3B82F6"), // Blue
        "Chill" => Color.Parse("#14B8A6"),      // Teal
        "Dark" => Color.Parse("#8B5CF6"),       // Purple
        "Happy" => Color.Parse("#EC4899"),      // Pink
        "Focus" => Color.Parse("#10B981"),      // Green
        _ => Colors.Gray
    };

        private static PathGeometry BuildArcSegmentGeometry(Point center, double outerR, double innerR, double startAngleDeg, double sweepDeg)
    {
        if (sweepDeg >= 360) sweepDeg = 359.999;
        if (sweepDeg <= 0) return new PathGeometry();

        double startRad = startAngleDeg * Math.PI / 180.0;
        double endRad = (startAngleDeg + sweepDeg) * Math.PI / 180.0;
        bool isLargeArc = sweepDeg > 180.0;

        Point outerStart = new(center.X + outerR * Math.Cos(startRad), center.Y + outerR * Math.Sin(startRad));
        Point outerEnd = new(center.X + outerR * Math.Cos(endRad), center.Y + outerR * Math.Sin(endRad));
        Point innerStart = new(center.X + innerR * Math.Cos(startRad), center.Y + innerR * Math.Sin(startRad));
        Point innerEnd = new(center.X + innerR * Math.Cos(endRad), center.Y + innerR * Math.Sin(endRad));

        // Explicit property assignment prevents CS8602 nullable warnings
        var figure = new PathFigure 
        { 
            StartPoint = outerStart, 
            IsClosed = true, 
            IsFilled = true 
        };
        
        figure.Segments = new PathSegments
        {
            new ArcSegment { Point = outerEnd, Size = new Size(outerR, outerR), IsLargeArc = isLargeArc, SweepDirection = SweepDirection.Clockwise },
            new LineSegment { Point = innerEnd },
            new ArcSegment { Point = innerStart, Size = new Size(innerR, innerR), IsLargeArc = isLargeArc, SweepDirection = SweepDirection.CounterClockwise },
            new LineSegment { Point = outerStart }
        };

        var geo = new PathGeometry();
        geo.Figures = new PathFigures { figure };
        
        return geo;
    }
}