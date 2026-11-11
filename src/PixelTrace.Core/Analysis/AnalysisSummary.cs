namespace PixelTrace.Core.Analysis;

public sealed record ReviewRegion(int X, int Y, int Width, int Height, int Blocks,
    IReadOnlyList<AnalysisMethod> Methods, AnalysisMethod PreferredMethod);

/// <summary>Descriptive review cues, never an authenticity classification or probability.</summary>
public sealed record AnalysisSummary(int RegionCount, bool LimitedCoverage,
    IReadOnlyList<AnalysisMethod> Methods, IReadOnlyList<ReviewRegion> Regions)
{
    // A fixed display-independent review threshold on normalized local responses.
    // It is a navigation heuristic, not a calibrated manipulation threshold.
    public const float ReviewThreshold = .65f;

    public static AnalysisSummary Create(AnalysisResult result, int orientation = 1, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var maps = result.Maps.Values.Where(m => m.Available && m.Method != AnalysisMethod.Combined).OrderBy(m => m.Method).ToArray();
        if (maps.Length == 0) return new(0, true, [], []);
        int cols = maps[0].Columns, rows = maps[0].Rows, count = cols * rows;
        var valid = new bool[count]; var signals = new int[count]; var peaks = new float[count];
        var preferred = new AnalysisMethod[count];
        foreach (var map in maps)
        {
            if (map.Columns != cols || map.Rows != rows || map.Scores.Length != count || map.Validity.Length != count)
                throw new ArgumentException("Analysis grids must match.", nameof(result));
            for (int i = 0; i < count; i++)
            {
                if ((i & 1023) == 0) token.ThrowIfCancellationRequested();
                if (!float.IsFinite(map.Validity[i]) || map.Validity[i] <= .05f || !float.IsFinite(map.Scores[i])) continue;
                valid[i] = true;
                if (map.Scores[i] < ReviewThreshold) continue;
                signals[i]++;
                if (map.Scores[i] > peaks[i]) { peaks[i] = map.Scores[i]; preferred[i] = map.Method; }
            }
        }
        long validArea = 0;
        for (int i = 0; i < count; i++)
            if (valid[i]) validArea += (long)Math.Min(result.BlockSize, result.RasterWidth - i % cols * result.BlockSize)
                * Math.Min(result.BlockSize, result.RasterHeight - i / cols * result.BlockSize);
        bool limited = valid.Count(v => v) < 4 || validArea < (long)result.RasterWidth * result.RasterHeight / 4
            || Math.Min(result.RasterWidth, result.RasterHeight) < 64;
        // Rank navigation cues by the same weighted response as the combined display,
        // so correlated high responses do not automatically outrank a stronger aggregate cue.
        var aggregate = ScoreMath.Combine(maps).Scores;
        var visited = new bool[count]; var queue = new Queue<int>();
        var regions = new List<(ReviewRegion Region, float AggregatePeak, int Overlap, float Peak)>();
        for (int seed = 0; seed < count; seed++)
        {
            token.ThrowIfCancellationRequested();
            if (signals[seed] == 0 || visited[seed]) continue;
            visited[seed] = true; queue.Enqueue(seed);
            int left = cols, top = rows, right = 0, bottom = 0, blocks = 0, overlap = 0, strongest = seed;
            float aggregatePeak = 0;
            var methods = new HashSet<AnalysisMethod>();
            while (queue.TryDequeue(out int i))
            {
                if ((blocks & 1023) == 0) token.ThrowIfCancellationRequested();
                int x = i % cols, y = i / cols; blocks++;
                left = Math.Min(left, x); right = Math.Max(right, x + 1); top = Math.Min(top, y); bottom = Math.Max(bottom, y + 1);
                overlap = Math.Max(overlap, signals[i]);
                aggregatePeak = Math.Max(aggregatePeak, aggregate[i]);
                if (peaks[i] > peaks[strongest]) strongest = i;
                foreach (var map in maps)
                    if (float.IsFinite(map.Validity[i]) && map.Validity[i] > .05f && float.IsFinite(map.Scores[i]) && map.Scores[i] >= ReviewThreshold)
                        methods.Add(map.Method);
                for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= cols || ny >= rows) continue;
                    int next = ny * cols + nx;
                    if (visited[next] || signals[next] == 0) continue;
                    visited[next] = true; queue.Enqueue(next);
                }
            }
            int x0 = left * result.BlockSize, y0 = top * result.BlockSize;
            int x1 = Math.Min(right * result.BlockSize, result.RasterWidth), y1 = Math.Min(bottom * result.BlockSize, result.RasterHeight);
            var box = Orient(x0, y0, x1, y1, result.RasterWidth, result.RasterHeight, orientation);
            regions.Add((new(box.X, box.Y, box.Width, box.Height, blocks, methods.Order().ToArray(), preferred[strongest]), aggregatePeak, overlap, peaks[strongest]));
        }
        return new(regions.Count, limited, maps.Select(m => m.Method).ToArray(),
            regions.OrderByDescending(r => r.AggregatePeak).ThenByDescending(r => r.Overlap).ThenByDescending(r => r.Region.Blocks).ThenByDescending(r => r.Peak)
                .Take(3).Select(r => r.Region).ToArray());
    }

    private static (int X, int Y, int Width, int Height) Orient(int x0, int y0, int x1, int y1, int w, int h, int orientation) => orientation switch
    {
        2 => (w - x1, y0, x1 - x0, y1 - y0),
        3 => (w - x1, h - y1, x1 - x0, y1 - y0),
        4 => (x0, h - y1, x1 - x0, y1 - y0),
        5 => (y0, x0, y1 - y0, x1 - x0),
        6 => (h - y1, x0, y1 - y0, x1 - x0),
        7 => (h - y1, w - x1, y1 - y0, x1 - x0),
        8 => (y0, w - x1, y1 - y0, x1 - x0),
        _ => (x0, y0, x1 - x0, y1 - y0)
    };
}
