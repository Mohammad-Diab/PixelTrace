using OpenCvSharp;
using PixelTrace.Core.Analysis;
using PixelTrace.Core.Imaging;

string output = Path.GetFullPath(args.Length > 0 ? args[0] : "artifacts/verification/samples");
Directory.CreateDirectory(output);
using var raster = new Mat(800, 1280, MatType.CV_8UC3);
var random = new Random(76);
for (int y = 0; y < 800; y++)
    for (int x = 0; x < 1280; x++)
    {
        double wave = 15 * Math.Sin(x * .035) + 10 * Math.Cos(y * .043);
        bool patch = x is >= 512 and < 704 && y is >= 288 and < 512;
        double noise = Math.Sqrt(-2 * Math.Log(Math.Max(1e-10, random.NextDouble()))) * Math.Cos(2 * Math.PI * random.NextDouble()) * (patch ? 9 : 2);
        byte Channel(double basis) => (byte)Math.Clamp(basis + wave + noise, 0, 255);
        raster.Set(y, x, new Vec3b(Channel(100 + y * .025), Channel(122 + x * .025), Channel(150 + x * .02)));
    }
Cv2.Rectangle(raster, new Rect(100, 130, 230, 480), new Scalar(95, 72, 44), 4, LineTypes.AntiAlias);
Cv2.Circle(raster, new Point(1000, 400), 180, new Scalar(136, 170, 198), 3, LineTypes.AntiAlias);
Cv2.PutText(raster, "PIXELTRACE / SIGNAL TEST", new Point(72, 70), HersheyFonts.HersheySimplex, 1, new Scalar(220, 230, 238), 2, LineTypes.AntiAlias);
Cv2.ImEncode(".jpg", raster, out var jpg, new ImageEncodingParam(ImwriteFlags.JpegQuality, 94));
File.WriteAllBytes(Path.Combine(output, "signal-test.jpg"), jpg);
Cv2.ImEncode(".png", raster, out var png); File.WriteAllBytes(Path.Combine(output, "signal-test.png"), png);
using var image = ImageDocument.Load(Path.Combine(output, "signal-test.jpg"));
var result = new ForensicAnalyzer().Analyze(image, new());
foreach (var (method, map) in result.Maps)
{
    HeatmapRenderer.ExportPng(Path.Combine(output, method.ToString().ToLowerInvariant() + ".png"), image, map, new(.65, 1.25));
    Console.WriteLine($"{method}: {map.ValidBlocks}/{map.Scores.Length} usable blocks; max {map.Scores.Max():F3}");
}
Console.WriteLine($"OpenCV {Cv2.GetVersionString()}; all six visualizations exported at {image.Width}x{image.Height}.");
File.WriteAllText(Path.Combine(output, "fixture.txt"), "Procedural signal fixture, not a photograph. A 192x224px patch at encoded x=512,y=288 has increased noise (sigma 9 versus 2). Shapes and lettering are natural controls in this fixture. This verifies response and localization, not forensic accuracy on real photographs.");
