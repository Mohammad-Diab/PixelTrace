using System.Buffers.Binary;
using System.Security.Cryptography;
using OpenCvSharp;
using PixelTrace.Core.Analysis;
using PixelTrace.Core.Imaging;
using Xunit;

namespace PixelTrace.Tests;

public sealed class CoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "PixelTrace-tests-" + Guid.NewGuid().ToString("N"));
    public CoreTests() => Directory.CreateDirectory(_folder);
    public void Dispose() => Directory.Delete(_folder, true);
    private string FilePath(string name) => Path.Combine(_folder, name);
    private string Write(Mat image, string name, int quality = 92)
    {
        var options = Path.GetExtension(name).Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            ? new[] { new ImageEncodingParam(ImwriteFlags.JpegQuality, quality) } : [];
        Cv2.ImEncode(Path.GetExtension(name), image, out var bytes, options);
        string path = FilePath(name); File.WriteAllBytes(path, bytes); return path;
    }

    [Fact]
    public void EmptyCorruptUnsupportedAndMissingFilesAreRejected()
    {
        File.WriteAllBytes(FilePath("empty.png"), []);
        File.WriteAllBytes(FilePath("corrupt.jpg"), [255, 216, 255, 192, 0, 40, 1]);
        File.WriteAllText(FilePath("pretend.png"), "not an image");
        Assert.Throws<InvalidDataException>(() => ImageDocument.Load(FilePath("empty.png")));
        Assert.Throws<InvalidDataException>(() => ImageDocument.Load(FilePath("corrupt.jpg")));
        Assert.Throws<InvalidDataException>(() => ImageDocument.Load(FilePath("pretend.png")));
        Assert.Throws<FileNotFoundException>(() => ImageDocument.Load(FilePath("missing.png")));
        var invalid = new byte[24]; new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(invalid, 0);
        BinaryPrimitives.WriteInt32BigEndian(invalid.AsSpan(16), 64); BinaryPrimitives.WriteInt32BigEndian(invalid.AsSpan(20), 64);
        File.WriteAllBytes(FilePath("broken.png"), invalid);
        Assert.Throws<InvalidDataException>(() => ImageDocument.Load(FilePath("broken.png")));
    }

    [Fact]
    public void OversizedHeaderIsRejectedBeforeDecode()
    {
        byte[] data = new byte[24]; new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(data, 0);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(16), 50000); BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(20), 50000);
        Assert.Contains("safety limit", Assert.Throws<InvalidDataException>(() => ImageHeader.Read(data)).Message);
    }

    [Theory]
    [InlineData(".png", "PNG")]
    [InlineData(".jpg", "JPEG")]
    [InlineData(".bmp", "BMP")]
    [InlineData(".tiff", "TIFF")]
    [InlineData(".webp", "WebP")]
    public void SupportedFormatsDecodeAndAnalyze(string extension, string format)
    {
        using var fixture = Fixture(123, 91);
        using var image = ImageDocument.Load(Write(fixture, "fixture" + extension));
        Assert.Equal(format, image.Header.Format); Assert.Equal(123, image.Width); Assert.Equal(91, image.Height);
        var result = new ForensicAnalyzer().Analyze(image, new(16));
        Assert.Equal(6, result.Maps.Count);
        Assert.Equal(format == "JPEG", result.Maps[AnalysisMethod.Ela].Available);
        Assert.Equal(format == "JPEG", result.Maps[AnalysisMethod.Compression].Available);
        if (format == "JPEG") Assert.InRange(image.Header.JpegQuality!.Value, 90, 94);
        AssertMaps(result);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(7, 9)]
    [InlineData(17, 15)]
    [InlineData(63, 65)]
    public void SmallImagesProduceFiniteCorrectlySizedMaps(int width, int height)
    {
        using var fixture = Fixture(width, height);
        using var image = ImageDocument.Load(Write(fixture, "tiny.jpg"));
        var result = new ForensicAnalyzer().Analyze(image, new(16));
        AssertMaps(result);
        using var rendered = HeatmapRenderer.Render(image, result.Maps[AnalysisMethod.Combined], new());
        Assert.Equal(width, rendered.Width); Assert.Equal(height, rendered.Height);
    }

    [Fact]
    public void FlatImageDoesNotBecomeAnAutomaticAnomaly()
    {
        using var fixture = new Mat(256, 256, MatType.CV_8UC3, new Scalar(128, 128, 128));
        using var image = ImageDocument.Load(Write(fixture, "flat.png"));
        var result = new ForensicAnalyzer().Analyze(image, new());
        foreach (var map in result.Maps.Values) Assert.All(map.Scores, s => Assert.Equal(0, s));
        Assert.Equal(0, result.Maps[AnalysisMethod.Noise].ValidBlocks);
    }

    [Fact]
    public void LocalNoiseChangeIsSpatiallyLocalized()
    {
        using var fixture = NoisePatch();
        using var image = ImageDocument.Load(Write(fixture, "patch.png"));
        var result = new ForensicAnalyzer().Analyze(image, new(32), methods: [AnalysisMethod.Noise]);
        var map = result.Maps[AnalysisMethod.Noise];
        var patch = new List<float>(); var surround = new List<float>();
        for (int y = 0; y < map.Rows; y++)
            for (int x = 0; x < map.Columns; x++)
                (x is >= 6 and <= 9 && y is >= 6 and <= 9 ? patch : surround).Add(map.Scores[y * map.Columns + x]);
        Assert.True(patch.Average() > surround.Average() + .1, $"patch={patch.Average():F3}, surround={surround.Average():F3}");
        Assert.True(map.Scores.Max() > .4f);
        Assert.Equal(2, result.Maps.Count); // A method can run independently.
    }

    [Fact]
    public void DctRespondsToLocalizedFrequencyChange()
    {
        using var fixture = Fixture(512, 512);
        using (var roi = new Mat(fixture, new Rect(192, 192, 128, 128))) Cv2.GaussianBlur(roi, roi, new Size(15, 15), 4);
        using var image = ImageDocument.Load(Write(fixture, "frequency.png"));
        var result = new ForensicAnalyzer().Analyze(image, new(), methods: [AnalysisMethod.Frequency]);
        var scores = result.Maps[AnalysisMethod.Frequency].Scores;
        Assert.True(scores.Max() > .25f); Assert.True(scores.Distinct().Count() > 5);
    }

    [Fact]
    public void NormalizationIsFiniteRobustAndDoesNotAmplifyTinyRanges()
    {
        var constant = ScoreMath.Normalize([2, 2, 2, float.NaN], [1, 1, 1, 1]);
        Assert.All(constant, s => Assert.Equal(0, s));
        var small = ScoreMath.Normalize([.1f, .101f, .102f], [1, 1, 1]);
        Assert.All(small, s => Assert.Equal(0, s));
        var raw = Enumerable.Repeat(.3f, 99).Append(10).Append(float.PositiveInfinity).ToArray();
        var valid = Enumerable.Repeat(1f, raw.Length).ToArray();
        var output = ScoreMath.Normalize(raw, valid);
        Assert.All(output, s => Assert.InRange(s, 0, 1)); Assert.True(output[99] > .9); Assert.Equal(0, output[100]);
    }

    [Fact]
    public void CombinationHonorsWeightsMasksAndUnavailableMethods()
    {
        var a = Map(AnalysisMethod.Noise, [.2f, .8f], [1, 0]);
        var b = Map(AnalysisMethod.Texture, [.8f, .4f], [1, 1]);
        var off = Map(AnalysisMethod.Compression, [1, 1], [1, 1]) with { Available = false };
        var combined = ScoreMath.Combine([a, b, off], new Dictionary<AnalysisMethod, double> { [AnalysisMethod.Noise] = 3, [AnalysisMethod.Texture] = 1 });
        Assert.Equal(.35f, combined.Scores[0], 5); Assert.Equal(.4f, combined.Scores[1], 5); Assert.Equal(.25f, combined.Validity[1], 5);
        var zero = ScoreMath.Combine([a, b], new Dictionary<AnalysisMethod, double> { [AnalysisMethod.Noise] = 0, [AnalysisMethod.Texture] = double.NaN });
        Assert.All(zero.Validity, v => Assert.Equal(0, v));
        Assert.Throws<ArgumentException>(() => ScoreMath.Combine([a, b with { Columns = 5 }]));
    }

    [Fact]
    public void RenderingIsReversibleAndDoesNotMutateScoresOrSource()
    {
        using var fixture = Fixture(123, 77);
        using var image = ImageDocument.Load(Write(fixture, "original.png"));
        var map = new ForensicAnalyzer().Analyze(image, new(16)).Maps[AnalysisMethod.Combined];
        var before = (float[])map.Scores.Clone();
        using var zero = HeatmapRenderer.Render(image, map, new(0));
        Assert.Equal(0, Cv2.Norm(zero, image.Pixels, NormTypes.INF));
        using var low = HeatmapRenderer.Render(image, map, new(.8, .5));
        using var high = HeatmapRenderer.Render(image, map, new(.8, 2));
        Assert.True(Cv2.Norm(low, high, NormTypes.L1) > 0);
        Assert.Equal(before, map.Scores);
        Assert.Equal(0, Cv2.Norm(fixture, image.Pixels, NormTypes.INF));
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
    public void ExportPreservesOrientedDimensionsAndPixels(int orientation)
    {
        using var fixture = Fixture(123, 77);
        string source = Write(fixture, "oriented.jpg");
        File.WriteAllBytes(source, WithOrientation(File.ReadAllBytes(source), orientation));
        byte[] originalHash = SHA256.HashData(File.ReadAllBytes(source));
        using var image = ImageDocument.Load(source);
        Assert.Equal(orientation, image.Header.Orientation);
        var map = new ForensicAnalyzer().Analyze(image, new(32)).Maps[AnalysisMethod.Combined];
        string output = FilePath("export.png");
        HeatmapRenderer.ExportPng(output, image, map, new(.67, 1.2));
        using var decoded = Cv2.ImRead(output);
        using var expected = HeatmapRenderer.Render(image, map, new(.67, 1.2));
        Assert.Equal(orientation >= 5 ? 77 : 123, decoded.Width);
        Assert.Equal(orientation >= 5 ? 123 : 77, decoded.Height);
        Assert.Equal(0, Cv2.Norm(decoded, expected, NormTypes.INF));
        Assert.Equal(originalHash, SHA256.HashData(File.ReadAllBytes(source)));
        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
    }

    [Fact]
    public void OriginalOverwriteAndIncorrectExportExtensionAreRejected()
    {
        using var fixture = Fixture(64, 64);
        string source = Write(fixture, "source.png");
        using var image = ImageDocument.Load(source);
        var map = new ForensicAnalyzer().Analyze(image, new()).Maps[AnalysisMethod.Combined];
        Assert.Throws<InvalidOperationException>(() => HeatmapRenderer.ExportPng(source, image, map, new()));
        Assert.Throws<ArgumentException>(() => HeatmapRenderer.ExportPng(FilePath("out.jpg"), image, map, new()));
        Assert.Throws<ArgumentOutOfRangeException>(() => HeatmapRenderer.Render(image, map, new(double.NaN)));
    }

    [Fact]
    public void AnalysisAndExportSupportCancellation()
    {
        using var fixture = Fixture(256, 256);
        using var image = ImageDocument.Load(Write(fixture, "input.jpg"));
        var analyzer = new ForensicAnalyzer();
        using var pre = new CancellationTokenSource(); pre.Cancel();
        Assert.Throws<OperationCanceledException>(() => analyzer.Analyze(image, new(), token: pre.Token));
        using var mid = new CancellationTokenSource();
        Assert.ThrowsAny<OperationCanceledException>(() => analyzer.Analyze(image, new(), new InlineProgress(p => { if (p.Value >= 10) mid.Cancel(); }), mid.Token));
        var map = analyzer.Analyze(image, new()).Maps[AnalysisMethod.Combined];
        string output = FilePath("cancelled.png"); File.WriteAllText(output, "previous result");
        Assert.ThrowsAny<OperationCanceledException>(() => HeatmapRenderer.ExportPng(output, image, map, new(), pre.Token));
        Assert.Equal("previous result", File.ReadAllText(output)); Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
    }

    [Fact]
    public void AlphaAndSixteenBitInputsHaveExplicitDecodeSemantics()
    {
        using var alpha = new Mat(64, 64, MatType.CV_8UC4, new Scalar(20, 40, 80, 0));
        using var image = ImageDocument.Load(Write(alpha, "alpha.png"));
        Assert.Contains("Transparency", image.DecodeNote);
        Assert.Equal(new Vec3b(127, 127, 127), image.Pixels.At<Vec3b>(0, 0));
        using var sixteen = new Mat(64, 64, MatType.CV_16UC1, new Scalar(65535));
        using var gray = ImageDocument.Load(Write(sixteen, "sixteen.png"));
        Assert.Contains("16-bit", gray.DecodeNote); Assert.Equal(new Vec3b(255, 255, 255), gray.Pixels.At<Vec3b>(0, 0));
    }

    [Fact]
    public void ExifTransformsFollowKnownPixelOrder()
    {
        using var source = new Mat(2, 3, MatType.CV_8UC1);
        for (int i = 0; i < 6; i++) source.Set(i / 3, i % 3, (byte)(i + 1));
        byte[][] expected = [[1,2,3,4,5,6], [3,2,1,6,5,4], [6,5,4,3,2,1], [4,5,6,1,2,3],
            [1,4,2,5,3,6], [4,1,5,2,6,3], [6,3,5,2,4,1], [3,6,2,5,1,4]];
        for (int orientation = 1; orientation <= 8; orientation++)
        {
            using var oriented = ImageDocument.Orient(source, orientation);
            byte[] actual = new byte[6];
            for (int i = 0; i < 6; i++) actual[i] = oriented.At<byte>(i / oriented.Width, i % oriented.Width);
            Assert.Equal(expected[orientation - 1], actual);
        }
    }

    [Fact]
    public void UnicodeFileNamesLoadAndExport()
    {
        using var fixture = Fixture(103, 83);
        using var image = ImageDocument.Load(Write(fixture, "صورة الاختبار.png"));
        var map = new ForensicAnalyzer().Analyze(image, new()).Maps[AnalysisMethod.Combined];
        string path = FilePath("نتيجة الفحص.png"); HeatmapRenderer.ExportPng(path, image, map, new());
        using var exported = ImageDocument.Load(path);
        Assert.Equal(103, exported.Width); Assert.Equal(83, exported.Height);
    }

    [Fact]
    public void InMemoryImageUsesTheSameDecoderAndDoesNotModifyItsBytes()
    {
        using var fixture = Fixture(113, 79); Cv2.ImEncode(".png", fixture, out var bytes);
        var before = SHA256.HashData(bytes);
        using var image = ImageDocument.LoadBytes(bytes, "clipboard.png");
        Assert.Equal(113, image.Width); Assert.Equal(79, image.Height);
        Assert.Equal(0, Cv2.Norm(fixture, image.Pixels, NormTypes.INF));
        Assert.Equal(before, SHA256.HashData(bytes));
        Assert.Throws<InvalidDataException>(() => ImageDocument.LoadBytes([], "empty.png"));
    }

    [Fact]
    public void LargeRasterCapsTheGridWithoutResizingTheExport()
    {
        // Just above the 65,536-cell threshold for 16px blocks (~16.8 MP).
        using var fixture = new Mat(4104, 4104, MatType.CV_8UC3);
        Cv2.Randu(fixture, new Scalar(110, 110, 110), new Scalar(145, 145, 145));
        using var image = ImageDocument.Load(Write(fixture, "large.jpg"));
        var result = new ForensicAnalyzer().Analyze(image, new(16));
        Assert.Equal(32, result.BlockSize); Assert.Contains("increased", result.Note);
        AssertMaps(result);
        string output = FilePath("large-export.png");
        HeatmapRenderer.ExportPng(output, image, result.Maps[AnalysisMethod.Combined], new());
        var header = ImageHeader.Read(File.ReadAllBytes(output));
        Assert.Equal(4104, header.Width); Assert.Equal(4104, header.Height);
    }

    [Theory]
    [InlineData(3)] [InlineData(6)]
    public void TiffOrientationRetainsTheEncodedRaster(int orientation)
    {
        using var fixture = Fixture(123, 77);
        string path = Write(fixture, "oriented.tiff");
        byte[] data = File.ReadAllBytes(path);
        Assert.Equal((byte)'I', data[0]);
        int offset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
        int count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(offset));
        // Append an IFD, keeping existing strip offsets intact, and add/replace the orientation tag.
        var entries = new List<byte[]>();
        for (int i = 0; i < count; i++)
        {
            var entry = data.AsSpan(offset + 2 + i * 12, 12).ToArray();
            if (BinaryPrimitives.ReadUInt16LittleEndian(entry) != 274) entries.Add(entry);
        }
        byte[] tag = new byte[12]; BinaryPrimitives.WriteUInt16LittleEndian(tag, 274);
        BinaryPrimitives.WriteUInt16LittleEndian(tag.AsSpan(2), 3); BinaryPrimitives.WriteUInt32LittleEndian(tag.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(tag.AsSpan(8), (ushort)orientation); entries.Add(tag);
        entries.Sort((a, b) => BinaryPrimitives.ReadUInt16LittleEndian(a).CompareTo(BinaryPrimitives.ReadUInt16LittleEndian(b)));
        int newOffset = data.Length;
        Array.Resize(ref data, data.Length + 2 + entries.Count * 12 + 4);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(4), newOffset);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(newOffset), (ushort)entries.Count);
        for (int i = 0; i < entries.Count; i++) entries[i].CopyTo(data, newOffset + 2 + i * 12);
        File.WriteAllBytes(path, data);
        using var image = ImageDocument.Load(path);
        Assert.Equal(orientation, image.Header.Orientation);
        Assert.Equal(fixture.Width, image.Pixels.Width); Assert.Equal(fixture.Height, image.Pixels.Height);
        Assert.Equal(0, Cv2.Norm(fixture, image.Pixels, NormTypes.INF));
    }

    private static ScoreMap Map(AnalysisMethod method, float[] scores, float[] valid) => new(method, 2, 1, 32, scores, scores, valid, "test");
    private static void AssertMaps(AnalysisResult result)
    {
        int count = ((result.RasterWidth + result.BlockSize - 1) / result.BlockSize) * ((result.RasterHeight + result.BlockSize - 1) / result.BlockSize);
        foreach (var map in result.Maps.Values)
        {
            Assert.Equal(count, map.Scores.Length); Assert.Equal(count, map.Validity.Length);
            Assert.All(map.RawScores, s => Assert.True(float.IsFinite(s)));
            Assert.All(map.Scores, s => Assert.InRange(s, 0, 1)); Assert.All(map.Validity, s => Assert.InRange(s, 0, 1));
        }
    }

    public static Mat Fixture(int width, int height)
    {
        var mat = new Mat(height, width, MatType.CV_8UC3); var random = new Random(1234);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int l = (int)(120 + 22 * Math.Sin(x * .09) + 18 * Math.Cos(y * .13) + random.Next(-5, 6));
                mat.Set(y, x, new Vec3b((byte)Math.Clamp(l - 12, 0, 255), (byte)Math.Clamp(l + 5, 0, 255), (byte)Math.Clamp(l + 17, 0, 255)));
            }
        return mat;
    }

    public static Mat NoisePatch()
    {
        var mat = new Mat(512, 512, MatType.CV_8UC3); var random = new Random(456);
        for (int y = 0; y < 512; y++)
            for (int x = 0; x < 512; x++)
            {
                double noise = Math.Sqrt(-2 * Math.Log(Math.Max(1e-10, random.NextDouble()))) * Math.Cos(2 * Math.PI * random.NextDouble());
                double sigma = x is >= 192 and < 320 && y is >= 192 and < 320 ? 6.5 : 1.8;
                byte l = (byte)Math.Clamp(128 + 14 * Math.Sin(x * .025) + 14 * Math.Cos(y * .025) + noise * sigma, 0, 255);
                mat.Set(y, x, new Vec3b(l, l, l));
            }
        return mat;
    }

    public static byte[] WithOrientation(byte[] jpeg, int orientation)
    {
        byte[] segment = [0xFF, 0xE1, 0, 34, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'I', (byte)'I', 42, 0, 8, 0, 0, 0, 1, 0, 0x12, 1, 3, 0, 1, 0, 0, 0, (byte)orientation, 0, 0, 0, 0, 0, 0, 0];
        return jpeg[..2].Concat(segment).Concat(jpeg[2..]).ToArray();
    }
    private sealed class InlineProgress(Action<AnalysisProgress> report) : IProgress<AnalysisProgress> { public void Report(AnalysisProgress p) => report(p); }
}
