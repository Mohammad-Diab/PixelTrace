using System.Globalization;
using Avalonia;
using PixelTrace.Core.Analysis;

namespace PixelTrace.Desktop.ViewModels;

public sealed record SummaryRegionItem(string Title, string Detail, string BoundsLabel, ReviewRegion Region);

public sealed partial class MainViewModel
{
    private ReviewRegion? _reviewRegion;
    public AnalysisSummary? Summary => _analysis?.Summary;
    public SummaryRegionItem[] SummaryRegions { get; private set; } = [];
    public string SummaryStatus => Text[Summary is { LimitedCoverage: true } ? "SummaryLimited"
        : Summary is { RegionCount: > 0 } ? "SummaryFound" : "SummaryQuiet"];
    public string SummaryDescription => Summary is not { } report ? "" : report.LimitedCoverage ? Text["SummaryLimitedInfo"]
        : report.RegionCount > 0 ? string.Format(CultureInfo.InvariantCulture, Text["SummaryFoundInfo"], report.Regions.Count, report.RegionCount)
        : Text["SummaryQuietInfo"];
    public string SummaryScope => Summary is not { } report ? "" : string.Format(CultureInfo.InvariantCulture, Text["SummaryScope"], report.Methods.Count)
        + (report.Methods.Count == 1 ? " · " + Text["SummarySingle"] : "")
        + (_analysis!.Maps.Values.Any(m => !m.Available) ? " · " + Text["SummaryJpeg"] : "");
    public Rect? ReviewBounds => !IsShowingOriginal && _reviewRegion is { } r ? new(r.X, r.Y, r.Width, r.Height) : null;
    public bool HasReviewRegion => _reviewRegion is not null;

    public void SelectSummaryRegion(SummaryRegionItem item)
    {
        if (!CanAdjust || !SummaryRegions.Contains(item)) return;
        _reviewRegion = item.Region;
        SelectedMethod = Methods.First(m => m.Kind == item.Region.PreferredMethod);
        SyncSelectedResult();
        Changed(nameof(ReviewBounds)); Changed(nameof(HasReviewRegion));
    }
    public void ClearReviewRegion()
    {
        _reviewRegion = null; Changed(nameof(ReviewBounds)); Changed(nameof(HasReviewRegion));
    }
    private void RefreshSummary(bool clearSelection = true)
    {
        if (clearSelection) ClearReviewRegion();
        SummaryRegions = Summary?.Regions.Select((r, i) => new SummaryRegionItem(
            $"{i + 1} · {RegionPosition(r)}", string.Join(IsArabic ? "، " : ", ", r.Methods.Select(m => Text[m.ToString()])),
            $"x {r.X} · y {r.Y} · {r.Width} × {r.Height} px", r)).ToArray() ?? [];
        foreach (string name in new[] { nameof(Summary), nameof(SummaryRegions), nameof(SummaryStatus), nameof(SummaryDescription), nameof(SummaryScope) }) Changed(name);
    }
    private string RegionPosition(ReviewRegion r)
    {
        if (_image is null) return "";
        if (r.Width >= _image.Width * .75 && r.Height >= _image.Height * .75) return Text["RegionAcross"];
        int col = Math.Clamp((int)((r.X + r.Width / 2.0) * 3 / _image.Width), 0, 2);
        int row = Math.Clamp((int)((r.Y + r.Height / 2.0) * 3 / _image.Height), 0, 2);
        return Text[$"Region{row}{col}"];
    }
}
