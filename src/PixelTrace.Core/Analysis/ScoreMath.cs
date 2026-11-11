namespace PixelTrace.Core.Analysis;

public static class ScoreMath
{
    public static float Median(IEnumerable<float> values)
    {
        var sorted = values.Where(float.IsFinite).Order().ToArray();
        if (sorted.Length == 0) return 0;
        int n = sorted.Length;
        return n % 2 == 0 ? (sorted[n / 2 - 1] + sorted[n / 2]) * .5f : sorted[n / 2];
    }

    public static float[] Normalize(float[] raw, float[] validity)
    {
        if (raw.Length != validity.Length) throw new ArgumentException("Score and validity lengths differ.");
        var good = raw.Where((v, i) => float.IsFinite(v) && validity[i] > .05f).Order().ToArray();
        var output = new float[raw.Length];
        if (good.Length < 2 || good[^1] - good[0] < .025f) return output;
        float center = Median(good), mad = Median(good.Select(v => Math.Abs(v - center)));
        // Absolute floor prevents a nearly uniform image from being stretched into a dramatic heatmap.
        float upper = Math.Max(3, Math.Max(center + 3 * 1.4826f * mad, good[(int)((good.Length - 1) * .95)]));
        for (int i = 0; i < output.Length; i++)
            if (validity[i] > 0 && float.IsFinite(raw[i])) output[i] = Math.Clamp((raw[i] - center * .35f) / upper, 0, 1);
        return output;
    }

    public static ScoreMap Combine(IEnumerable<ScoreMap> source, IReadOnlyDictionary<AnalysisMethod, double>? weights = null)
    {
        var maps = source.Where(m => m.Available && m.Method != AnalysisMethod.Combined).ToArray();
        if (maps.Length == 0) throw new ArgumentException("No compatible analysis maps.");
        var first = maps[0]; int count = first.Scores.Length;
        if (maps.Any(m => m.Columns != first.Columns || m.Rows != first.Rows || m.BlockSize != first.BlockSize || m.Scores.Length != count || m.Validity.Length != count))
            throw new ArgumentException("Analysis grids must match.");
        var scores = new float[count]; var valid = new float[count];
        var active = maps.Select(m => (Map: m, Weight: weights is not null && weights.TryGetValue(m.Method, out var w) ? w : DefaultWeight(m.Method)))
            .Select(m => (m.Map, Weight: double.IsFinite(m.Weight) ? Math.Clamp(m.Weight, 0, 10) : 0)).ToArray();
        double possible = active.Sum(m => m.Weight);
        for (int i = 0; i < count; i++)
        {
            double sum = 0, divisor = 0;
            foreach (var (map, weight) in active)
            {
                if (!float.IsFinite(map.Scores[i]) || !float.IsFinite(map.Validity[i])) continue;
                double v = Math.Clamp(map.Validity[i], 0, 1) * weight;
                sum += Math.Clamp(map.Scores[i], 0, 1) * v; divisor += v;
            }
            if (divisor > 1e-9) scores[i] = (float)(sum / divisor);
            valid[i] = possible > 0 ? (float)Math.Clamp(divisor / possible, 0, 1) : 0;
        }
        return new(AnalysisMethod.Combined, first.Columns, first.Rows, first.BlockSize, (float[])scores.Clone(), scores, valid,
            "Weighted, robustly normalized local inconsistencies. Neutral regions may have insufficient evidence. Intensity is not an authenticity or AI probability.");
    }

    public static double DefaultWeight(AnalysisMethod method) => method switch
    {
        AnalysisMethod.Noise => 1,
        AnalysisMethod.Frequency => 1,
        AnalysisMethod.Texture => .8,
        AnalysisMethod.Compression => .7,
        AnalysisMethod.Ela => .35,
        _ => 0
    };
}
