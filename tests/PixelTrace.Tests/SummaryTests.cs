using PixelTrace.Core.Analysis;
using Xunit;

namespace PixelTrace.Tests;

public sealed class SummaryTests
{
    [Fact]
    public void GroupsAdjacentResponsesAndAttributesOnlyTheMethodsThatResponded()
    {
        var noise = Map(AnalysisMethod.Noise, 5, 5, (0, .9f), (1, .8f), (24, .9f));
        var frequency = Map(AnalysisMethod.Frequency, 5, 5, (0, .7f), (4, .9f));
        var texture = Map(AnalysisMethod.Texture, 5, 5, (20, .8f));
        var original = noise.Scores.ToArray();
        var report = AnalysisSummary.Create(Result(noise, frequency, texture));
        Assert.Equal(4, report.RegionCount); Assert.Equal(3, report.Regions.Count);
        var first = report.Regions[0];
        Assert.Equal((0, 0, 64, 32, 2), (first.X, first.Y, first.Width, first.Height, first.Blocks));
        Assert.Equal(new[] { AnalysisMethod.Noise, AnalysisMethod.Frequency }, first.Methods);
        Assert.Equal(AnalysisMethod.Noise, first.PreferredMethod);
        Assert.Equal(original, noise.Scores); Assert.False(report.LimitedCoverage);
    }

    [Fact]
    public void RanksTheAggregateResponseBeforeTheCountOfThresholdCrossings()
    {
        var noise = Map(AnalysisMethod.Noise, 4, 4, (15, 1));
        var frequency = Map(AnalysisMethod.Frequency, 4, 4, (0, .66f), (15, .6f));
        var compression = Map(AnalysisMethod.Compression, 4, 4, (0, .66f));
        var report = AnalysisSummary.Create(Result(noise, frequency, compression));
        Assert.Equal(2, report.RegionCount);
        Assert.Equal((96, 96), (report.Regions[0].X, report.Regions[0].Y));
        Assert.Equal(new[] { AnalysisMethod.Noise }, report.Regions[0].Methods);
        Assert.Equal(2, report.Regions[1].Methods.Count);
    }

    [Fact]
    public void DoesNotCountCombinedOrUnavailableMapsAsIndependentChecks()
    {
        var noise = Map(AnalysisMethod.Noise, 4, 4);
        var combined = Map(AnalysisMethod.Combined, 4, 4, (0, 1));
        var unavailable = Map(AnalysisMethod.Ela, 4, 4, (0, 1)) with { Available = false };
        var report = AnalysisSummary.Create(Result(noise, combined, unavailable));
        Assert.Equal(0, report.RegionCount); Assert.Empty(report.Regions);
        Assert.Equal(new[] { AnalysisMethod.Noise }, report.Methods); Assert.False(report.LimitedCoverage);
        Assert.True(AnalysisSummary.Create(Result(combined, unavailable)).LimitedCoverage);
    }

    [Fact]
    public void IgnoresMaskedAndNonfiniteValuesAndDistinguishesLimitedData()
    {
        var map = Map(AnalysisMethod.Noise, 4, 4, (0, 1), (1, float.NaN), (2, float.PositiveInfinity), (3, .9f));
        map.Validity[0] = 0; map.Validity[3] = float.NaN;
        var report = AnalysisSummary.Create(Result(map));
        Assert.Empty(report.Regions); Assert.False(report.LimitedCoverage);
        Array.Fill(map.Validity, 0); map.Validity[5] = 1;
        Assert.True(AnalysisSummary.Create(Result(map)).LimitedCoverage);
    }

    [Theory]
    [InlineData(1, 64, 32, 6, 13)]
    [InlineData(2, 0, 32, 6, 13)]
    [InlineData(3, 0, 0, 6, 13)]
    [InlineData(4, 64, 0, 6, 13)]
    [InlineData(5, 32, 64, 13, 6)]
    [InlineData(6, 0, 64, 13, 6)]
    [InlineData(7, 0, 0, 13, 6)]
    [InlineData(8, 32, 0, 13, 6)]
    public void RegionBoundsRespectPartialEdgeTilesAndEveryExifOrientation(int orientation, int x, int y, int width, int height)
    {
        var result = Result(Map(AnalysisMethod.Noise, 3, 2, (5, .8f))) with { RasterWidth = 70, RasterHeight = 45 };
        var region = Assert.Single(AnalysisSummary.Create(result, orientation).Regions);
        Assert.Equal((x, y, width, height), (region.X, region.Y, region.Width, region.Height));
    }

    [Fact]
    public void UsesAFixedReviewThresholdWithoutDependingOnRawScores()
    {
        var map = Map(AnalysisMethod.Noise, 4, 4, (0, AnalysisSummary.ReviewThreshold - .001f), (15, AnalysisSummary.ReviewThreshold));
        Array.Fill(map.RawScores, 1000);
        var region = Assert.Single(AnalysisSummary.Create(Result(map)).Regions);
        Assert.Equal((96, 96, 1), (region.X, region.Y, region.Blocks));
    }

    [Fact]
    public void HonorsCancellationAndRejectsMismatchedGrids()
    {
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var result = Result(Map(AnalysisMethod.Noise, 4, 4));
        Assert.Throws<OperationCanceledException>(() => AnalysisSummary.Create(result, token: cancellation.Token));
        Assert.Throws<ArgumentException>(() => AnalysisSummary.Create(Result(Map(AnalysisMethod.Noise, 4, 4), Map(AnalysisMethod.Texture, 3, 4))));
    }

    private static ScoreMap Map(AnalysisMethod method, int cols, int rows, params (int Index, float Value)[] responses)
    {
        var scores = new float[cols * rows];
        foreach (var (i, value) in responses) scores[i] = value;
        return new(method, cols, rows, 32, scores.ToArray(), scores, Enumerable.Repeat(1f, scores.Length).ToArray(), "fixture");
    }
    private static AnalysisResult Result(params ScoreMap[] maps) => new(maps.ToDictionary(m => m.Method),
        maps[0].Columns * 32, maps[0].Rows * 32, 32, 90, TimeSpan.Zero, "fixture");
}
