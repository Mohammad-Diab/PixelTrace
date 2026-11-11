using OpenCvSharp;
using PixelTrace.Core.Analysis;

namespace PixelTrace.Core.Imaging;

public sealed record VisualizationSettings(double Opacity = .6, double Sensitivity = 1)
{
    public void Validate()
    {
        if (!double.IsFinite(Opacity) || Opacity is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(Opacity));
        if (!double.IsFinite(Sensitivity) || Sensitivity is < .25 or > 4) throw new ArgumentOutOfRangeException(nameof(Sensitivity));
    }
}

public static class HeatmapRenderer
{
    // Viridis anchor colors, interpolated continuously; increasing brightness means increasing anomaly intensity.
    private static readonly (double R, double G, double B)[] Palette = [(68, 1, 84), (59, 82, 139), (33, 145, 140), (94, 201, 98), (253, 231, 37)];

    public static unsafe Mat Render(ImageDocument image, ScoreMap map, VisualizationSettings settings, CancellationToken token = default)
    {
        settings.Validate();
        int w = image.Pixels.Width, h = image.Pixels.Height;
        if (map.Columns != (w + map.BlockSize - 1) / map.BlockSize || map.Rows != (h + map.BlockSize - 1) / map.BlockSize ||
            map.Scores.Length != map.Columns * map.Rows || map.Validity.Length != map.Scores.Length)
            throw new ArgumentException("The score grid does not match the image.");
        token.ThrowIfCancellationRequested();
        var output = new Mat(h, w, MatType.CV_8UC3);
        try
        {
            var xs = Axis(w, map.Columns, map.BlockSize); var ys = Axis(h, map.Rows, map.BlockSize);
            var lut = Enumerable.Range(0, 1024).Select(i => Color(Math.Pow(i / 1023.0, 1 / settings.Sensitivity))).ToArray();
            Parallel.For(0, h, new ParallelOptions { CancellationToken = token, MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount - 1, 1, 8) }, y =>
            {
                byte* src = (byte*)image.Pixels.Ptr(y), dst = (byte*)output.Ptr(y);
                var ay = ys[y];
                for (int x = 0; x < w; x++)
                {
                    var ax = xs[x];
                    int i00 = ay.Lo * map.Columns + ax.Lo, i10 = ay.Lo * map.Columns + ax.Hi;
                    int i01 = ay.Hi * map.Columns + ax.Lo, i11 = ay.Hi * map.Columns + ax.Hi;
                    double c00 = Good(map.Validity[i00]), c10 = Good(map.Validity[i10]), c01 = Good(map.Validity[i01]), c11 = Good(map.Validity[i11]);
                    double valid = Lerp(Lerp(c00, c10, ax.Mix), Lerp(c01, c11, ax.Mix), ay.Mix);
                    double score = valid > 1e-6 ? Lerp(Lerp(Good(map.Scores[i00]) * c00, Good(map.Scores[i10]) * c10, ax.Mix),
                        Lerp(Good(map.Scores[i01]) * c01, Good(map.Scores[i11]) * c11, ax.Mix), ay.Mix) / valid : 0;
                    var color = lut[Math.Clamp((int)(score * 1023), 0, 1023)];
                    double alpha = map.Available ? settings.Opacity * valid : 0;
                    dst[x * 3] = (byte)Math.Clamp(Math.Round(src[x * 3] * (1 - alpha) + color.B * alpha), 0, 255);
                    dst[x * 3 + 1] = (byte)Math.Clamp(Math.Round(src[x * 3 + 1] * (1 - alpha) + color.G * alpha), 0, 255);
                    dst[x * 3 + 2] = (byte)Math.Clamp(Math.Round(src[x * 3 + 2] * (1 - alpha) + color.R * alpha), 0, 255);
                }
            });
            token.ThrowIfCancellationRequested();
            if (image.Header.Orientation == 1) return output;
            var oriented = ImageDocument.Orient(output, image.Header.Orientation);
            output.Dispose(); return oriented;
        }
        catch { output.Dispose(); throw; }
    }

    public static void ExportPng(string path, ImageDocument image, ScoreMap map, VisualizationSettings settings, CancellationToken token = default)
    {
        string full = Path.GetFullPath(path);
        if (!Path.GetExtension(full).Equals(".png", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Export must use the .png extension.");
        if (string.Equals(full, image.Path, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new InvalidOperationException("Choose a different filename; the original image is never overwritten.");
        string temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var visualization = Render(image, map, settings, token);
            token.ThrowIfCancellationRequested();
            Cv2.ImEncode(".png", visualization, out var bytes, new ImageEncodingParam(ImwriteFlags.PngCompression, 4));
            if (bytes.Length == 0) throw new IOException("PNG encoding failed.");
            token.ThrowIfCancellationRequested();
            using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                const int chunk = 1024 * 1024;
                for (int p = 0; p < bytes.Length; p += chunk) { token.ThrowIfCancellationRequested(); file.Write(bytes, p, Math.Min(chunk, bytes.Length - p)); }
                file.Flush(true);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temp, full, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static double Good(float v) => float.IsFinite(v) ? Math.Clamp(v, 0, 1) : 0;
    private static double Lerp(double a, double b, double t) => a + (b - a) * t;
    private static (double R, double G, double B) Color(double t)
    {
        double p = Math.Clamp(t, 0, 1) * (Palette.Length - 1); int a = Math.Min((int)p, Palette.Length - 2);
        var lo = Palette[a]; var hi = Palette[a + 1]; double mix = p - a;
        return (Lerp(lo.R, hi.R, mix), Lerp(lo.G, hi.G, mix), Lerp(lo.B, hi.B, mix));
    }

    private static (int Lo, int Hi, double Mix)[] Axis(int length, int cells, int block)
    {
        var result = new (int, int, double)[length];
        double Center(int i) => i * block + Math.Min(block, length - i * block) * .5;
        int lo = 0;
        for (int p = 0; p < length; p++)
        {
            double at = p + .5;
            while (lo < cells - 1 && Center(lo + 1) <= at) lo++;
            int hi = Math.Min(lo + 1, cells - 1);
            double mix = hi == lo ? 0 : Math.Clamp((at - Center(lo)) / (Center(hi) - Center(lo)), 0, 1);
            result[p] = (lo, hi, mix);
        }
        return result;
    }
}
