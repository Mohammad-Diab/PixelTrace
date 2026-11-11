namespace PixelTrace.Core.Analysis;

public enum AnalysisMethod { Combined, Noise, Frequency, Compression, Texture, Ela }

public sealed record AnalysisOptions(int BlockSize = 32, int ElaQuality = 90)
{
    public void Validate()
    {
        if (BlockSize is not (16 or 32 or 64 or 128)) throw new ArgumentOutOfRangeException(nameof(BlockSize));
        if (ElaQuality is < 50 or > 100) throw new ArgumentOutOfRangeException(nameof(ElaQuality));
    }
}

public sealed record ScoreMap(AnalysisMethod Method, int Columns, int Rows, int BlockSize,
    float[] RawScores, float[] Scores, float[] Validity, string Description, bool Available = true)
{
    public int ValidBlocks => Validity.Count(v => v > .05f);
}

public sealed record AnalysisResult(IReadOnlyDictionary<AnalysisMethod, ScoreMap> Maps,
    int RasterWidth, int RasterHeight, int BlockSize, int ElaQuality, TimeSpan Elapsed, string Note)
{
    public AnalysisSummary? Summary { get; init; }
}

public sealed record AnalysisProgress(double Value, string Message);
