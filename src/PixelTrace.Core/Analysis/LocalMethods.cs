namespace PixelTrace.Core.Analysis;

// Each method is independent: it selects its own feature, confidence mask and numerical floor.
internal abstract class LocalMethod
{
    public abstract AnalysisMethod Kind { get; }
    protected abstract float Feature(BlockFeatures f);
    protected abstract float Validity(BlockFeatures f);
    protected abstract float Floor { get; }
    protected abstract string Description { get; }

    public ScoreMap Calculate(FeatureField field, CancellationToken token)
    {
        int count = field.Cells.Length;
        var features = field.Cells.Select(Feature).ToArray();
        var validity = field.Cells.Select(Validity).ToArray();
        var raw = new float[count];
        // Peers in coarse texture/luminance bins supplement the neighborhood, including larger patches.
        var pools = Enumerable.Range(0, count).Where(i => validity[i] > .1f)
            .GroupBy(i => Bin(field.Cells[i])).ToDictionary(g => g.Key, g =>
            {
                float center = ScoreMath.Median(g.Select(i => features[i]));
                return (Center: center, Scale: Math.Max(Floor, 1.4826f * ScoreMath.Median(g.Select(i => Math.Abs(features[i] - center)))), Count: g.Count());
            });
        for (int y = 0; y < field.Rows; y++)
        {
            token.ThrowIfCancellationRequested();
            for (int x = 0; x < field.Columns; x++)
            {
                int i = y * field.Columns + x;
                if (validity[i] <= 0) continue;
                var neighbors = new List<float>(48); var current = field.Cells[i];
                for (int ny = Math.Max(0, y - 3); ny <= Math.Min(field.Rows - 1, y + 3); ny++)
                    for (int nx = Math.Max(0, x - 3); nx <= Math.Min(field.Columns - 1, x + 3); nx++)
                    {
                        int j = ny * field.Columns + nx; var candidate = field.Cells[j];
                        if (j != i && validity[j] > .1f && Math.Abs(candidate.TextureLevel - current.TextureLevel) < 1 && Math.Abs(candidate.Luminance - current.Luminance) < 55)
                            neighbors.Add(features[j]);
                    }
                double local = 0, global = 0;
                bool hasLocal = neighbors.Count >= 4;
                if (hasLocal)
                {
                    float med = ScoreMath.Median(neighbors);
                    float scale = Math.Max(Floor, 1.4826f * ScoreMath.Median(neighbors.Select(v => Math.Abs(v - med))));
                    local = Math.Abs(features[i] - med) / scale;
                }
                bool hasGlobal = pools.TryGetValue(Bin(current), out var pool) && pool.Count >= 8;
                if (hasGlobal) global = Math.Abs(features[i] - pool.Center) / pool.Scale;
                if (!hasLocal && !hasGlobal) { validity[i] = 0; continue; }
                raw[i] = (float)Math.Min(20, hasLocal && hasGlobal ? .7 * local + .3 * global : hasLocal ? local : global);
            }
        }
        return new(Kind, field.Columns, field.Rows, field.BlockSize, raw, ScoreMath.Normalize(raw, validity), validity, Description);
    }

    private static int Bin(BlockFeatures f) => Math.Clamp((int)(f.TextureLevel / 1.1f), 0, 5) * 4 + Math.Clamp((int)f.Luminance / 64, 0, 3);
}

internal sealed class NoiseMethod : LocalMethod
{
    public override AnalysisMethod Kind => AnalysisMethod.Noise;
    protected override float Feature(BlockFeatures f) => f.Noise;
    protected override float Validity(BlockFeatures f) => f.NoiseValidity;
    protected override float Floor => .22f;
    protected override string Description => "Robust high-pass residual noise compared with regions of similar luminance and texture. Clipped pixels and strong gradients are excluded; near-zero noise has low validity. Denoising, resizing and natural illumination can also change noise.";
}

internal sealed class FrequencyMethod : LocalMethod
{
    public override AnalysisMethod Kind => AnalysisMethod.Frequency;
    protected override float Feature(BlockFeatures f) => f.Frequency;
    protected override float Validity(BlockFeatures f) => f.FrequencyValidity;
    protected override float Floor => .32f;
    protected override string Description => "Local DCT high-to-mid frequency energy, compared against nearby and texture-matched regions. Low-information blocks are masked. Fine natural patterns, focus and resampling can create the same response.";
}

internal sealed class CompressionMethod(int? quality) : LocalMethod
{
    public override AnalysisMethod Kind => AnalysisMethod.Compression;
    protected override float Feature(BlockFeatures f) => f.Compression;
    protected override float Validity(BlockFeatures f) => f.CompressionValidity * (quality >= 97 ? .35f : 1);
    protected override float Floor => quality < 60 ? .4f : .24f;
    protected override string Description => "JPEG 8px boundary-to-interior discontinuity ratios on the encoded raster (before EXIF rotation), with edge suppression. " +
        (quality is { } q ? $"Approximate last-save quality {q}; very high quality weakens this check. " : "Nonstandard quality tables. ") +
        "Repeated compression, crops and resampling can shift or erase the grid; this does not reconstruct compression history.";
}

internal sealed class TextureMethod : LocalMethod
{
    public override AnalysisMethod Kind => AnalysisMethod.Texture;
    protected override float Feature(BlockFeatures f) => f.Texture;
    protected override float Validity(BlockFeatures f) => f.TextureValidity;
    protected override float Floor => .2f;
    protected override string Description => "Relationships between local contrast, gradient energy, edge density and directional coherence. Strong coherent edges and smooth blocks are downweighted. Material boundaries and depth of field can still respond naturally.";
}

internal sealed class ElaMethod(int quality) : LocalMethod
{
    public override AnalysisMethod Kind => AnalysisMethod.Ela;
    protected override float Feature(BlockFeatures f) => f.Ela;
    protected override float Validity(BlockFeatures f) => f.FrequencyValidity * .8f;
    protected override float Floor => .14f;
    protected override string Description => $"JPEG recompression error at quality {quality}, adjusted for texture and compared regionally. A heuristic only: edges, chroma subsampling, encoder differences and previous saves affect ELA. A bright region is not proof of editing. ELA has a low default weight in the combined map.";
}
