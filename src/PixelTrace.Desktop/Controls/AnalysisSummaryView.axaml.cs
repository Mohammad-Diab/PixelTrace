using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using PixelTrace.Desktop.ViewModels;

namespace PixelTrace.Desktop.Controls;

public sealed partial class AnalysisSummaryView : UserControl
{
    public event Action<Rect>? RegionSelected;
    public AnalysisSummaryView() => InitializeComponent();
    private void RegionClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel { CanAdjust: true } model || sender is not Button { Tag: SummaryRegionItem item }) return;
        model.SelectSummaryRegion(item);
        if (model.ReviewBounds is { } bounds) RegionSelected?.Invoke(bounds);
    }
    private void ClearClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel model) model.ClearReviewRegion();
    }
}
