using System.Diagnostics;
using OpenCvSharp;
using PixelTrace.Core.Imaging;

namespace PixelTrace.Core.Analysis;

/// <summary>Traditional block statistics at native resolution; no classifier, model or network access.</summary>
public sealed class ForensicAnalyzer
{
    public AnalysisResult Analyze(ImageDocument image, AnalysisOptions options,
        IProgress<AnalysisProgress>? progress = null, CancellationToken token = default,
        IReadOnlyCollection<AnalysisMethod>? methods = null)
    {
        options.Validate(); token.ThrowIfCancellationRequested();
        var watch = Stopwatch.StartNew();
        var selected = (methods ?? [AnalysisMethod.Noise, AnalysisMethod.Frequency, AnalysisMethod.Compression, AnalysisMethod.Texture, AnalysisMethod.Ela])
            .Where(m => m != AnalysisMethod.Combined).Distinct().ToHashSet();
        if (selected.Count == 0) throw new ArgumentException("Choose at least one independent method.", nameof(methods));
        int block = options.BlockSize, width = image.Pixels.Width, height = image.Pixels.Height;
        while ((long)((width + block - 1) / block) * ((height + block - 1) / block) > 65536) block *= 2;
        int cols = (width + block - 1) / block, rows = (height + block - 1) / block;
        var field = new FeatureField(cols, rows, block);
        using var gray = new Mat();
        progress?.Report(new(2, "Preparing luminance and local statistics…"));
        Cv2.CvtColor(image.Pixels, gray, ColorConversionCodes.BGR2GRAY);
        var parallel = new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount - 1, 1, 8) };
        int finished = 0;
        Parallel.For(0, rows, parallel, y =>
        {
            for (int x = 0; x < cols; x++)
            {
                token.ThrowIfCancellationRequested();
                field.Cells[y * cols + x] = Extract(gray, x * block, y * block, block, selected.Contains(AnalysisMethod.Frequency));
            }
            int done = Interlocked.Increment(ref finished);
            if (done % Math.Max(1, rows / 30) == 0) progress?.Report(new(5 + 55.0 * done / rows, "Measuring noise, frequencies, texture and block boundaries…"));
        });
        var maps = new Dictionary<AnalysisMethod, ScoreMap>();
        var calculators = new LocalMethod[] { new NoiseMethod(), new FrequencyMethod(), new CompressionMethod(image.Header.JpegQuality), new TextureMethod(), new ElaMethod(options.ElaQuality) };
        foreach (var method in calculators.Where(m => selected.Contains(m.Kind)))
        {
            token.ThrowIfCancellationRequested();
            progress?.Report(new(62 + 30.0 * maps.Count / selected.Count, $"Comparing {method.Kind.ToString().ToLowerInvariant()} across regions…"));
            bool jpegOnly = method.Kind is AnalysisMethod.Compression or AnalysisMethod.Ela;
            if (jpegOnly && image.Header.Format != "JPEG")
            {
                maps.Add(method.Kind, new(method.Kind, cols, rows, block, new float[cols * rows], new float[cols * rows], new float[cols * rows],
                    "Unavailable for this input. JPEG compression and ELA require an original JPEG; converting a lossless image to JPEG would introduce new artifacts.", false));
                continue;
            }
            if (method.Kind == AnalysisMethod.Ela) MeasureEla(image.Pixels, field, options.ElaQuality, parallel, token);
            maps.Add(method.Kind, method.Calculate(field, token));
        }
        token.ThrowIfCancellationRequested();
        maps.Add(AnalysisMethod.Combined, ScoreMath.Combine(maps.Values));
        string note = "Local statistical differences are clues for inspection, not proof of editing or synthetic origin.";
        if (block != options.BlockSize) note += $" Block size increased to {block}px to keep the analysis grid within 65,536 cells.";
        if (image.Width < 64 || image.Height < 64) note += " Small images offer very little regional evidence.";
        if (image.Header.Format == "JPEG") note += image.Header.JpegQuality is { } q
            ? $" Approximate last JPEG quality: {q} (IJG table match). Prior compression history cannot be recovered."
            : " Nonstandard JPEG quantization tables; compression quality and prior history are unknown.";
        watch.Stop(); progress?.Report(new(100, "Analysis complete"));
        return new(maps, image.Pixels.Width, image.Pixels.Height, block, options.ElaQuality, watch.Elapsed, note);
    }

    private static unsafe BlockFeatures Extract(Mat gray, int x, int y, int block, bool frequency)
    {
        int w = Math.Min(block, gray.Width - x), h = Math.Min(block, gray.Height - y);
        if (w < 8 || h < 8) return default;
        using var roi = new Mat(gray, new Rect(x, y, w, h));
        Cv2.MeanStdDev(roi, out var mean, out var std);
        using var smooth = new Mat();
        using var gx = new Mat(); using var gy = new Mat();
        // A small halo makes tile borders irrelevant to the derivative/noise estimates.
        int left = Math.Max(0, x - 2), top = Math.Max(0, y - 2);
        using var halo = new Mat(gray, new Rect(left, top, Math.Min(gray.Width, x + w + 2) - left, Math.Min(gray.Height, y + h + 2) - top));
        Cv2.GaussianBlur(halo, smooth, new Size(3, 3), .8, borderType: BorderTypes.Reflect101);
        Cv2.Sobel(halo, gx, MatType.CV_32F, 1, 0, 3, .125, borderType: BorderTypes.Reflect101);
        Cv2.Sobel(halo, gy, MatType.CV_32F, 0, 1, 3, .125, borderType: BorderTypes.Reflect101);
        double grad = 0, edge = 0, xx = 0, yy = 0, xy = 0, clip = 0;
        var residual = new List<float>(w * h / 2);
        double boundary = 0, inside = 0; int nb = 0, ni = 0;
        for (int j = 1; j < h - 1; j++)
        {
            byte* p = (byte*)gray.Ptr(y + j), s = (byte*)smooth.Ptr(y - top + j);
            float* dx = (float*)gx.Ptr(y - top + j), dy = (float*)gy.Ptr(y - top + j);
            for (int i = 1; i < w - 1; i++)
            {
                int px = x + i, hx = x - left + i;
                float a = dx[hx], b = dy[hx]; double g = Math.Sqrt(a * a + b * b);
                grad += g; xx += a * a; yy += b * b; xy += a * b;
                if (g > 12) edge++;
                if (p[px] < 5 || p[px] > 250) clip++;
                // Estimate noise only away from strong texture and clipped intensities.
                if (g < 8 && p[px] is > 8 and < 247) residual.Add(Math.Abs(p[px] - s[hx]));
                if (g < 22)
                {
                    double hd = Math.Abs(p[px] - p[px - 1]);
                    double vd = Math.Abs(p[px] - ((byte*)gray.Ptr(y + j - 1))[px]);
                    if (px % 8 == 0) { boundary += hd; nb++; } else if (px % 8 == 4) { inside += hd; ni++; }
                    if ((y + j) % 8 == 0) { boundary += vd; nb++; } else if ((y + j) % 8 == 4) { inside += vd; ni++; }
                }
            }
        }
        int n = Math.Max(1, (w - 2) * (h - 2));
        double gradient = grad / n;
        double coherence = Math.Sqrt((xx - yy) * (xx - yy) + 4 * xy * xy) / (xx + yy + 1);
        float signal = (float)Math.Clamp(1 - clip / n, 0, 1);
        float noise = ScoreMath.Median(residual) * 1.4826f;
        float noiseValid = signal * (float)Math.Clamp(residual.Count / (n * .4), 0, 1) * Math.Clamp(noise / .8f, 0, 1);
        double freq = 0;
        if (frequency && std.Val0 > 1)
        {
            using var f = new Mat(); using var padded = new Mat(); using var dct = new Mat();
            roi.ConvertTo(f, MatType.CV_32F);
            Cv2.CopyMakeBorder(f, padded, 0, block - h, 0, block - w, BorderTypes.Reflect101);
            Cv2.Dct(padded, dct);
            double high = 0, mid = 0;
            for (int v = 0; v < block; v++)
            {
                float* row = (float*)dct.Ptr(v);
                for (int u = 0; u < block; u++)
                {
                    double energy = row[u] * row[u];
                    if (u + v >= block * .9) high += energy;
                    else if (u + v >= block * .25) mid += energy;
                }
            }
            freq = Math.Log((high + block) / (mid + block));
        }
        return new(
            (float)mean.Val0, (float)Math.Log(1 + gradient),
            (float)Math.Log(.35 + noise / Math.Sqrt(.25 + mean.Val0 / 128)), noiseValid,
            (float)freq, signal * (float)Math.Clamp((std.Val0 - 1) / 8, 0, 1) * (float)(1 - .75 * coherence),
            (float)Math.Log((boundary / Math.Max(1, nb) + .5) / (inside / Math.Max(1, ni) + .5)),
            nb >= 8 && ni >= 8 && inside / ni > .15 ? signal * (float)Math.Clamp(1 - gradient / 45, 0, 1) : 0,
            (float)(Math.Log((gradient + 1) / (std.Val0 + 2)) + .4 * edge / n + .35 * coherence),
            signal * (float)Math.Clamp((std.Val0 - 1) / 6, 0, 1) * (float)Math.Clamp(1 - coherence * .75, .2, 1), 0);
    }

    private static void MeasureEla(Mat source, FeatureField field, int quality, ParallelOptions parallel, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Cv2.ImEncode(".jpg", source, out var bytes, new ImageEncodingParam(ImwriteFlags.JpegQuality, quality));
        if (bytes.Length == 0) throw new InvalidOperationException("JPEG recompression failed.");
        token.ThrowIfCancellationRequested();
        using var recompressed = Cv2.ImDecode(bytes, ImreadModes.Color | ImreadModes.IgnoreOrientation);
        if (recompressed.Empty()) throw new InvalidOperationException("JPEG recompression could not be decoded.");
        Parallel.For(0, field.Rows, parallel, y =>
        {
            using var difference = new Mat();
            for (int x = 0; x < field.Columns; x++)
            {
                token.ThrowIfCancellationRequested();
                int px = x * field.BlockSize, py = y * field.BlockSize;
                var rect = new Rect(px, py, Math.Min(field.BlockSize, source.Width - px), Math.Min(field.BlockSize, source.Height - py));
                using var a = new Mat(source, rect); using var b = new Mat(recompressed, rect);
                Cv2.Absdiff(a, b, difference);
                var m = Cv2.Mean(difference); var f = field.Cells[y * field.Columns + x];
                // Texture predicts some recompression error; normalize it before comparing regions.
                float ela = (float)Math.Log(1 + (m.Val0 + m.Val1 + m.Val2) / (3 * (1 + .35 * Math.Exp(f.TextureLevel))));
                field.Cells[y * field.Columns + x] = f with { Ela = ela };
            }
        });
    }
}

internal readonly record struct BlockFeatures(float Luminance, float TextureLevel,
    float Noise, float NoiseValidity, float Frequency, float FrequencyValidity,
    float Compression, float CompressionValidity, float Texture, float TextureValidity, float Ela);

internal sealed record FeatureField(int Columns, int Rows, int BlockSize)
{
    public BlockFeatures[] Cells { get; } = new BlockFeatures[Columns * Rows];
}
