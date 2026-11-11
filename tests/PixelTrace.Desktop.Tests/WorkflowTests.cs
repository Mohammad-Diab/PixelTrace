using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
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
    public async Task ClosingDuringAnalysisOrRenderingDetachesTheViewerBeforeReleasingImages()
    {
        for (int mode = 0; mode < 3; mode++)
        {
            var window = new MainWindow(); window.Show(); var model = (MainViewModel)window.DataContext!;
            var viewer = window.FindControl<ImageViewport>("Viewer")!;
            bool closed = false; window.Closed += (_, _) => closed = true;
            try
            {
                await model.OpenAsync(Fixture(".jpg", 1025, 769));
                var analysis = model.AnalyzeAsync();
                if (mode > 0) await analysis;
                if (mode == 2) await Rendered(model);
                await Close(window); await analysis;
                Assert.Null(viewer.Source);
                Assert.Null(model.Original); Assert.Null(model.Result);
                model.InspectPixel(10, 10); // Late native pointer events must not access a disposed Mat.
                Assert.False(model.CanOpen); Assert.False(model.CanAnalyze); Assert.False(model.CanSave);
            }
            finally { if (!closed) await Close(window); }
        }
    }

    [AvaloniaFact]
    public async Task InvalidReplacementPreservesTheLoadedImageAndViewportNavigation()
    {
        var window = new MainWindow(); window.Show(); var model = (MainViewModel)window.DataContext!;
        try
        {
            await model.OpenAsync(Fixture(".png", 801, 601)); Frame();
            var original = model.Original;
            await model.OpenAsync(Path.Combine(_folder, "missing.png"));
            Assert.True(model.HasError); Assert.Same(original, model.Original); Assert.True(model.CanAnalyze);
            model.DismissError();
            var viewer = window.FindControl<ImageViewport>("Viewer")!;
            viewer.ActualSize(); viewer.Step(1); Frame();
            double previousZoom = viewer.State.Zoom;
            var center = viewer.TranslatePoint(new Avalonia.Point(viewer.Bounds.Width / 2, viewer.Bounds.Height / 2), window)!.Value;
            window.MouseMove(center); window.MouseWheel(center, new Vector(0, 1)); Frame();
            Assert.True(viewer.State.Zoom > previousZoom);
            var previousCenter = viewer.State.Center;
            window.MouseDown(center, MouseButton.Left);
            window.MouseMove(center + new Vector(50, 30));
            window.MouseUp(center + new Vector(50, 30), MouseButton.Left); Frame();
            Assert.NotEqual(previousCenter, viewer.State.Center);
            viewer.Fit(); window.Width = 1100; Frame();
            Assert.True(viewer.State.Fitted);
        }
        finally { await Close(window); }
    }

    [AvaloniaFact]
    public async Task LightWindowHasOneViewerAndTheDescriptionInsideTheBrandPill()
    {
        var window = new MainWindow(); window.Show(); Frame();
        try
        {
            Assert.Equal(ThemeVariant.Light, window.ActualThemeVariant);
            Assert.Single(window.GetVisualDescendants().OfType<ImageViewport>());
            var pill = window.FindControl<Border>("BrandPill")!;
            var name = window.FindControl<TextBlock>("AppName")!;
            var description = window.FindControl<TextBlock>("AppDescription")!;
            Assert.Contains(pill, name.GetLogicalAncestors());
            Assert.Contains(pill, description.GetLogicalAncestors());
            Assert.True(description.TranslatePoint(default, pill)!.Value.Y > name.TranslatePoint(default, pill)!.Value.Y);
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Purple · Lower");
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Yellow · Higher");
        }
        finally { await Close(window); }
    }

    [AvaloniaFact]
    public async Task HoldingOriginalWithMouseOrKeyboardIsMomentaryAndPreservesZoomAndScores()
    {
        var window = new MainWindow(); window.Show(); var model = (MainViewModel)window.DataContext!;
        try
        {
            Assert.False(model.CanCompare);
            await model.OpenAsync(Fixture(".jpg", 801, 601));
            var viewer = window.FindControl<ImageViewport>("Viewer")!;
            Assert.Same(model.Original, viewer.Source); Assert.False(model.CanCompare);
            await model.AnalyzeAsync(); await Rendered(model); Frame();
            var overlay = viewer.Source; var raw = model.CurrentMap!.RawScores;
            Assert.Same(model.Result, overlay); Assert.NotSame(model.Original, overlay);
            var compare = window.FindControl<Button>("CompareButton")!;
            viewer.ActualSize(); viewer.Step(1); Frame();
            var before = viewer.State;
            var press = compare.TranslatePoint(new Avalonia.Point(compare.Bounds.Width / 2, compare.Bounds.Height / 2), window)!.Value;
            window.MouseMove(press); window.MouseDown(press, MouseButton.Left); Frame();
            Assert.True(model.IsShowingOriginal); Assert.Same(model.Original, viewer.Source);
            // Releasing outside the button must still restore the overlay.
            var outside = viewer.TranslatePoint(new Avalonia.Point(20, 20), window)!.Value;
            window.MouseMove(outside); Assert.True(model.IsShowingOriginal);
            window.MouseUp(outside, MouseButton.Left); Frame();
            Assert.False(model.IsShowingOriginal); Assert.Same(overlay, viewer.Source); Assert.Equal(before, viewer.State);
            compare.Focus(); window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " "); Frame();
            Assert.True(model.IsShowingOriginal); Assert.Same(model.Original, viewer.Source);
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.Space, " "); Frame();
            Assert.False(model.IsShowingOriginal); Assert.Same(overlay, viewer.Source);
            Assert.Same(raw, model.CurrentMap.RawScores); Assert.Equal(before, viewer.State);
            model.InspectPixel(200, 200);
            var legend = window.FindControl<HeatmapLegend>("Legend")!;
            Assert.NotNull(legend.Score); Assert.Equal(model.HoverIntensity, legend.Score);
            model.IsShowingOriginal = true; Assert.Null(legend.Score);
            model.IsShowingOriginal = false; Assert.NotNull(legend.Score);
            await model.OpenAsync(Fixture(".png", 801, 601)); Frame();
            Assert.False(model.IsShowingOriginal); Assert.False(model.CanCompare); Assert.Same(model.Original, viewer.Source); Assert.Null(legend.Score);
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
