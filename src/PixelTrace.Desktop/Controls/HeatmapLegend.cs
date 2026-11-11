using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace PixelTrace.Desktop.Controls;

/// <summary>The heatmap's Viridis scale and hovered-block marker, using the same sensitivity mapping.</summary>
public sealed class HeatmapLegend : Control
{
    public static readonly StyledProperty<double?> ScoreProperty = AvaloniaProperty.Register<HeatmapLegend, double?>(nameof(Score));
    public static readonly StyledProperty<double> SensitivityProperty = AvaloniaProperty.Register<HeatmapLegend, double>(nameof(Sensitivity), 1);
    private static readonly LinearGradientBrush Gradient = new()
    {
        StartPoint = new RelativePoint(0, .5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, .5, RelativeUnit.Relative),
        GradientStops = [new(Color.Parse("#440154"), 0), new(Color.Parse("#3B528B"), .25), new(Color.Parse("#21918C"), .5),
            new(Color.Parse("#5EC962"), .75), new(Color.Parse("#FDE725"), 1)]
    };
    public double? Score { get => GetValue(ScoreProperty); set => SetValue(ScoreProperty, value); }
    public double Sensitivity { get => GetValue(SensitivityProperty); set => SetValue(SensitivityProperty, value); }
    static HeatmapLegend() => AffectsRender<HeatmapLegend>(ScoreProperty, SensitivityProperty);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (Bounds.Width < 1) return;
        double middle = Bounds.Height / 2;
        context.DrawRectangle(Gradient, null, new Rect(0, middle - 5, Bounds.Width, 10), 5, 5);
        if (Score is not { } score || !double.IsFinite(score)) return;
        double sensitivity = double.IsFinite(Sensitivity) ? Math.Clamp(Sensitivity, .25, 4) : 1;
        double x = Math.Clamp(Math.Pow(Math.Clamp(score, 0, 1), 1 / sensitivity) * Bounds.Width, 2, Math.Max(2, Bounds.Width - 2));
        var top = new Point(x, middle - 10); var bottom = new Point(x, middle + 10);
        context.DrawLine(new Pen(Brushes.White, 4), top, bottom);
        context.DrawLine(new Pen(Brushes.Black, 1.5), top, bottom);
    }
}
