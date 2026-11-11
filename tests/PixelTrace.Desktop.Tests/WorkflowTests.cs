using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using OpenCvSharp;
using PixelTrace.Core.Analysis;
using PixelTrace.Desktop;
using PixelTrace.Desktop.Controls;
using PixelTrace.Desktop.ViewModels;
using Xunit;
using Window = Avalonia.Controls.Window;

[assembly: AvaloniaTestApplication(typeof(PixelTrace.Desktop.Tests.TestSetup))]

namespace PixelTrace.Desktop.Tests;

public static class TestSetup
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class WorkflowTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "PixelTrace-desktop-tests-" + Guid.NewGuid().ToString("N"));
    public WorkflowTests() => Directory.CreateDirectory(_folder);
    public void Dispose() => Directory.Delete(_folder, true);

    [AvaloniaFact]
    public async Task ReplaceAnalyzedImagesAndSwitchUnsupportedMapsWithoutUsingDisposedBitmaps()
    {
        var window = new MainWindow(); window.Show();
        var model = (MainViewModel)window.DataContext!;
        try
        {
            for (int round = 0; round < 3; round++)
            {
                string path = Fixture(round % 2 == 0 ? ".jpg" : ".png", 257 + round, 181 + round);
                await model.OpenAsync(path);
                Assert.False(model.HasError, model.Error); Assert.True(model.HasImage); Assert.False(model.HasResults);
                Assert.Equal(257 + round, model.Original!.PixelSize.Width);
                Frame();
                await model.AnalyzeAsync(); await Rendered(model);
                Assert.False(model.HasError, model.Error);
                foreach (var method in model.Methods)
                {
                    model.SelectedMethod = method; await Rendered(model); Frame();
                    Assert.False(model.HasError, model.Error);
                    bool unavailable = round % 2 == 1 && method.Kind is AnalysisMethod.Compression or AnalysisMethod.Ela;
                    Assert.Equal(unavailable, model.MethodUnavailable);
                    if (unavailable) Assert.False(model.CanSave);
                }
            }
        }
        finally { await Close(window); }
    }

    [AvaloniaFact]
    public async Task RapidVisualizationChangesReuseRawScoresAndReleasePreviousRenders()
    {
        var window = new MainWindow(); window.Show(); var model = (MainViewModel)window.DataContext!;
        try
        {
            await model.OpenAsync(Fixture(".jpg", 641, 481)); await model.AnalyzeAsync(); await Rendered(model);
            model.SelectedMethod = model.Methods.Single(m => m.Kind == AnalysisMethod.Noise); await Rendered(model);
            var raw = model.CurrentMap!.RawScores; var original = model.Original;
            for (int i = 0; i < 20; i++) { model.Opacity = i / 20.0; model.Sensitivity = .5 + i / 10.0; }
            await Rendered(model); Frame();
            Assert.Same(raw, model.CurrentMap.RawScores); Assert.Same(original, model.Original);
            Assert.False(model.HasError, model.Error); Assert.False(model.IsRendering); Assert.True(model.CanSave);
            string result = Path.Combine(_folder, "ui-result.png"); await model.ExportAsync(result);
            Assert.True(File.Exists(result)); Assert.False(model.HasError, model.Error);
        }
        finally { await Close(window); }
    }

    [AvaloniaFact]
    public async Task ClosingDuringAnalysisOrRenderingDetachesTheViewersBeforeReleasingImages()
    {
        for (int mode = 0; mode < 3; mode++)
        {
            var window = new MainWindow(); window.Show(); var model = (MainViewModel)window.DataContext!;
            var left = window.FindControl<ImageViewport>("OriginalViewer")!;
            var right = window.FindControl<ImageViewport>("ResultViewer")!;
            bool closed = false; window.Closed += (_, _) => closed = true;
            try
            {
                await model.OpenAsync(Fixture(".jpg", 1025, 769));
                var analysis = model.AnalyzeAsync();
                if (mode > 0) await analysis;
                if (mode == 2) await Rendered(model);
                await Close(window); await analysis;
                Assert.Null(left.Source); Assert.Null(right.Source);
                Assert.Null(model.Original); Assert.Null(model.Result);
                model.InspectPixel(10, 10); // Late native pointer events must not access a disposed Mat.
                Assert.False(model.CanOpen); Assert.False(model.CanAnalyze); Assert.False(model.CanSave);
            }
            finally { if (!closed) await Close(window); }
        }
    }

    [AvaloniaFact]
    public async Task InvalidReplacementPreservesTheLoadedImageAndZoomStaysSynchronized()
    {
        var window = new MainWindow(); window.Show(); var model = (MainViewModel)window.DataContext!;
        try
        {
            await model.OpenAsync(Fixture(".png", 801, 601)); Frame();
            var original = model.Original;
            await model.OpenAsync(Path.Combine(_folder, "missing.png"));
            Assert.True(model.HasError); Assert.Same(original, model.Original); Assert.True(model.CanAnalyze);
            model.DismissError();
            var left = window.FindControl<ImageViewport>("OriginalViewer")!;
            var right = window.FindControl<ImageViewport>("ResultViewer")!;
            left.ActualSize(); left.Step(1); Frame();
            Assert.Equal(left.State.Zoom, right.State.Zoom); Assert.Equal(left.State.Center, right.State.Center);
            right.Step(-1); Assert.Equal(left.State.Zoom, right.State.Zoom);
            var center = left.TranslatePoint(new Avalonia.Point(left.Bounds.Width / 2, left.Bounds.Height / 2), window)!.Value;
            window.MouseMove(center); window.MouseWheel(center, new Vector(0, 1)); Frame();
            Assert.Equal(left.State.Zoom, right.State.Zoom);
            var previousCenter = left.State.Center;
            window.MouseDown(center, MouseButton.Left);
            window.MouseMove(center + new Vector(50, 30));
            window.MouseUp(center + new Vector(50, 30), MouseButton.Left); Frame();
            Assert.NotEqual(previousCenter, left.State.Center); Assert.Equal(left.State.Center, right.State.Center);
            left.Fit(); window.Width = 1200; Frame();
            Assert.True(left.State.Fitted); Assert.True(right.State.Fitted); Assert.Equal(left.State.Center, right.State.Center);
        }
        finally { await Close(window); }
    }

    private string Fixture(string extension, int width, int height)
    {
        string path = Path.Combine(_folder, Guid.NewGuid().ToString("N") + extension);
        using var image = new Mat(height, width, MatType.CV_8UC3);
        Cv2.Randu(image, new Scalar(100, 110, 120), new Scalar(160, 170, 180));
        Cv2.ImEncode(extension, image, out var bytes); File.WriteAllBytes(path, bytes); return path;
    }
    private static async Task Rendered(MainViewModel model)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (model.IsRendering) { await Task.Delay(20, timeout.Token); Frame(); }
    }
    private static void Frame() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); }
    private static async Task Close(Window window)
    {
        var closed = new TaskCompletionSource(); window.Closed += (_, _) => closed.TrySetResult();
        window.Close(); await closed.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }
}
