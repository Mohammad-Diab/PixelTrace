using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using Avalonia.Interactivity;
using Insiyab.Avalonia.Controls;
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
                    if (model.AvailableMethods.Contains(method))
                        Assert.Equal(method, window.FindControl<ComboBox>("ResultSelector")!.SelectedItem);
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
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Purple · Low");
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "Yellow · High");
        }
        finally { await Close(window); }
    }

    [AvaloniaFact]
    public async Task SetupCancelPreservesResultsAndReanalysisLocksTheSource()
    {
        var window = new MainWindow(); window.Show(); var model = (MainViewModel)window.DataContext!;
        try
        {
            string source = Fixture(".jpg", 513, 385);
            await model.OpenAsync(source); await model.AnalyzeAsync(); await Rendered(model);
            var original = model.Original; var scores = model.CurrentMap!.RawScores; var result = model.Result; var summary = model.Summary;
            var opening = window.ShowAnalysisSetupAsync(window.FindControl<Control>("OpenButton")!, false); Frame();
            var dialog = window.ActiveAnalysisDialog!; var draft = (AnalysisDraft)dialog.DataContext!;
            string replacement = Fixture(".png", 321, 241);
            ((AnalysisSetupView)dialog.Content!).FindControl<FileUpload>("ImageFile")!.AddFiles([
                new FileEntry(Path.GetFileName(replacement), new FileInfo(replacement).Length, () => Task.FromResult<Stream>(File.OpenRead(replacement)))]);
            Assert.True(draft.CanSubmit); draft.BlockSize = 64;
            dialog.Close(false); await opening; Frame();
            Assert.Same(original, model.Original); Assert.Same(result, model.Result); Assert.Same(scores, model.CurrentMap.RawScores); Assert.Equal(32, model.BlockSize);
            Assert.Same(summary, model.Summary);

            var reanalyzing = window.ShowAnalysisSetupAsync(window.FindControl<Control>("ReanalyzeButton")!, true); Frame();
            dialog = window.ActiveAnalysisDialog!; draft = (AnalysisDraft)dialog.DataContext!;
            var fileField = ((AnalysisSetupView)dialog.Content!).FindControl<FileUpload>("ImageFile")!;
            Assert.False(fileField.IsEnabled); Assert.Equal(Path.GetFileName(source), fileField.Files.Single().Name);
            File.Delete(source); // Reanalysis uses retained pixels, even if the disk file is no longer available.
            draft.BlockSize = 64; draft.Method = draft.Methods.Single(m => m.Kind == AnalysisMethod.Noise);
            dialog.Close(true); await reanalyzing; await Rendered(model);
            Assert.False(model.HasError, model.Error); Assert.Same(original, model.Original);
            Assert.Equal(64, model.BlockSize); Assert.Equal(AnalysisMethod.Noise, model.AvailableMethods.Single().Kind);
            Assert.NotSame(scores, model.CurrentMap!.RawScores);
            var raw = model.CurrentMap.RawScores;
            model.Sensitivity = 2; model.Opacity = .1; model.ResetDisplay(); await Rendered(model);
            Assert.Equal(1, model.Sensitivity); Assert.Equal(.6, model.Opacity); Assert.Same(raw, model.CurrentMap.RawScores);
        }
        finally { await Close(window); }
    }

    [AvaloniaFact]
    public async Task ArabicMirrorsOnlyTheLayoutAndLibraryDayNightChangesTheTheme()
    {
        var window = new MainWindow(); window.Show(); var model = (MainViewModel)window.DataContext!; Frame();
        try
        {
            var panel = window.FindControl<Border>("DisplayPanel")!; var image = window.FindControl<Border>("ImagePanel")!;
            Assert.True(panel.TranslatePoint(default, window)!.Value.X > image.TranslatePoint(default, window)!.Value.X);
            var displayTitle = window.FindControl<TextBlock>("DisplayTitle")!;
            var defaults = window.FindControl<Button>("DefaultButton")!;
            Assert.False(PhysicalBounds(displayTitle, window).Intersects(PhysicalBounds(defaults, window)));
            var pill = window.FindControl<Border>("BrandPill")!; var captions = window.FindControl<CaptionButtons>("WindowCaptions")!;
            // Native captions hide on the headless backend; their documented shared height remains available.
            Assert.Equal((double)window.FindResource("InsAppbarH")!, pill.Bounds.Height, 1);
            Assert.True(window.FindControl<TextBlock>("AppName")!.FontSize <= 15);
            await model.OpenAsync(Fixture(".png", 241, 191)); await model.AnalyzeAsync(); await Rendered(model);
            model.ToggleLanguage(); Frame();
            Assert.Equal(FlowDirection.RightToLeft, window.FlowDirection);
            Assert.Equal("فتح صورة", window.FindControl<ToolbarButton>("OpenButton")!.Label);
            Assert.True(panel.TranslatePoint(default, window)!.Value.X < image.TranslatePoint(default, window)!.Value.X);
            Assert.False(PhysicalBounds(displayTitle, window).Intersects(PhysicalBounds(defaults, window)));
            Assert.Equal(FlowDirection.LeftToRight, window.FindControl<ImageViewport>("Viewer")!.FlowDirection);
            Assert.Equal(model.SelectedMethod, window.FindControl<ComboBox>("ResultSelector")!.SelectedItem);
            var theme = window.FindControl<DayNight>("ThemeButton")!;
            var at = theme.TranslatePoint(new Avalonia.Point(theme.Bounds.Width / 2, theme.Bounds.Height / 2), window)!.Value;
            window.MouseMove(at); window.MouseDown(at, MouseButton.Left); window.MouseUp(at, MouseButton.Left);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (window.ActualThemeVariant != ThemeVariant.Dark) { await Task.Delay(20, timeout.Token); Frame(); }
            Assert.True(theme.IsChecked);
        }
        finally { if (model.IsArabic) model.ToggleLanguage(); ThemeToggle.Apply(ThemeMode.Light); await Close(window); }
    }

    [AvaloniaFact]
    public async Task NewAnalysisDialogCommitsTheStreamImageOnlyWhenSubmitted()
    {
        var window = new MainWindow(); window.Show(); var model = (MainViewModel)window.DataContext!;
        try
        {
            Assert.False(model.ShowSave); Assert.False(window.FindControl<ToolbarButton>("SaveButton")!.IsVisible);
            var task = window.ShowAnalysisSetupAsync(window.FindControl<Control>("OpenButton")!, false); Frame();
            var dialog = window.ActiveAnalysisDialog!; var draft = (AnalysisDraft)dialog.DataContext!;
            Assert.False(draft.CanSubmit);
            string source = Fixture(".png", 333, 251);
            ((AnalysisSetupView)dialog.Content!).FindControl<FileUpload>("ImageFile")!.AddFiles([
                new FileEntry("memory.png", new FileInfo(source).Length, () => Task.FromResult<Stream>(File.OpenRead(source)))]);
            Assert.False(model.HasImage); Assert.True(draft.CanSubmit);
            draft.Method = draft.Methods.First(m => m.Kind == AnalysisMethod.Ela); Assert.False(draft.CanSubmit);
            draft.Method = draft.Methods.First(m => m.Kind == AnalysisMethod.Combined); Assert.True(draft.CanSubmit);
            var submit = ((DialogFoot)dialog.Foot!).Children.OfType<Button>().Last();
            Assert.True(submit.IsEnabled); submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await task; await Rendered(model); Frame();
            Assert.False(model.HasError, model.Error); Assert.True(model.ShowSave); Assert.True(model.CanSave);
            Assert.Equal("memory.png", model.FileName); Assert.Equal(333, model.Original!.PixelSize.Width);
            Assert.DoesNotContain(model.AvailableMethods, m => m.Kind is AnalysisMethod.Ela or AnalysisMethod.Compression);
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

    [AvaloniaFact]
    public async Task SummaryLocalizesAndNavigatesWithoutChangingWithDisplaySettings()
    {
        var window = new MainWindow(); window.Show(); var model = (MainViewModel)window.DataContext!;
        try
        {
            Assert.False(window.FindControl<AnalysisSummaryView>("SummaryPanel")!.IsVisible);
            string path = SummaryFixture();
            await model.OpenAsync(path); await model.AnalyzeAsync(); await Rendered(model); Frame();
            var summary = model.Summary!;
            Assert.NotNull(summary); Assert.False(summary.LimitedCoverage); Assert.True(summary.RegionCount > 0);
            Assert.Equal(5, summary.Methods.Count); Assert.DoesNotContain(AnalysisMethod.Combined, summary.Methods);
            Assert.True(window.FindControl<AnalysisSummaryView>("SummaryPanel")!.IsVisible);
            Assert.Contains("Regions to review", model.SummaryStatus);
            var button = window.GetVisualDescendants().OfType<Button>().First(b => b.Tag is SummaryRegionItem);
            var item = (SummaryRegionItem)button.Tag!;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Rendered(model); Frame();
            var viewer = window.FindControl<ImageViewport>("Viewer")!;
            Assert.Equal(item.Region.PreferredMethod, model.SelectedMethod.Kind);
            Assert.Equal(new Avalonia.Rect(item.Region.X, item.Region.Y, item.Region.Width, item.Region.Height), viewer.ReviewBounds);
            Assert.False(viewer.State.Fitted);
            var state = viewer.State; var bounds = viewer.ReviewBounds; var raw = model.CurrentMap!.RawScores;
            model.IsShowingOriginal = true; Frame(); Assert.Null(viewer.ReviewBounds); Assert.Equal(state, viewer.State);
            model.IsShowingOriginal = false; Frame(); Assert.Equal(bounds, viewer.ReviewBounds); Assert.Equal(state, viewer.State);
            model.Opacity = .2; model.Sensitivity = 2.3; await Rendered(model); Frame();
            Assert.Same(summary, model.Summary); Assert.Same(raw, model.CurrentMap.RawScores);
            model.ToggleLanguage(); Frame();
            Assert.Equal("مناطق تستحق المراجعة", model.SummaryStatus);
            Assert.Contains(model.Text["SummaryLimit"], window.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text));
            Assert.Equal(bounds, viewer.ReviewBounds); Assert.Equal(state, viewer.State);
            Assert.Equal(FlowDirection.LeftToRight, viewer.FlowDirection);
            model.ClearReviewRegion(); Frame(); Assert.Null(viewer.ReviewBounds);
            await model.OpenAsync(Fixture(".png", 200, 160)); Frame();
            Assert.Null(model.Summary); Assert.Empty(model.SummaryRegions);
            Assert.False(window.FindControl<AnalysisSummaryView>("SummaryPanel")!.IsVisible);
        }
        finally { if (model.IsArabic) model.ToggleLanguage(); await Close(window); }
    }

    [AvaloniaFact]
    public async Task SummaryExplainsLimitedDataAndSingleMethodScope()
    {
        var window = new MainWindow(); window.Show(); var model = (MainViewModel)window.DataContext!;
        try
        {
            using var image = new Mat(128, 128, MatType.CV_8UC3, new Scalar(128, 128, 128));
            string path = Path.Combine(_folder, "flat.png"); Cv2.ImWrite(path, image);
            await model.OpenAsync(path); await model.AnalyzeAsync(); await Rendered(model); Frame();
            Assert.True(model.Summary!.LimitedCoverage); Assert.Empty(model.SummaryRegions);
            Assert.Equal("Limited information", model.SummaryStatus); Assert.Contains("JPEG checks unavailable", model.SummaryScope);
            var draft = model.CreateDraft(true); draft.Method = draft.Methods.Single(m => m.Kind == AnalysisMethod.Noise);
            await model.ExecuteDraftAsync(draft); await Rendered(model); Frame();
            Assert.Single(model.Summary!.Methods); Assert.Contains("One check only", model.SummaryScope);
            Assert.DoesNotContain("JPEG checks unavailable", model.SummaryScope);
        }
        finally { await Close(window); }
    }

    private string SummaryFixture()
    {
        using var image = new Mat(480, 640, MatType.CV_8UC3); var random = new Random(76);
        for (int y = 0; y < 480; y++) for (int x = 0; x < 640; x++)
        {
            double wave = 15 * Math.Sin(x * .035) + 10 * Math.Cos(y * .043);
            double noise = Math.Sqrt(-2 * Math.Log(Math.Max(1e-10, random.NextDouble()))) * Math.Cos(2 * Math.PI * random.NextDouble())
                * (x is >= 256 and < 352 && y is >= 160 and < 288 ? 9 : 2);
            byte Channel(double basis) => (byte)Math.Clamp(basis + wave + noise, 0, 255);
            image.Set(y, x, new Vec3b(Channel(100 + y * .025), Channel(122 + x * .025), Channel(150 + x * .02)));
        }
        string path = Path.Combine(_folder, "summary.jpg");
        Cv2.ImWrite(path, image, new ImageEncodingParam(ImwriteFlags.JpegQuality, 94)); return path;
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
    private static Avalonia.Rect PhysicalBounds(Control control, Window window)
    {
        var a = control.TranslatePoint(default, window)!.Value;
        var b = control.TranslatePoint(new Avalonia.Point(control.Bounds.Width, control.Bounds.Height), window)!.Value;
        return new Avalonia.Rect(new Avalonia.Point(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)), new Avalonia.Point(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
    }
    private static async Task Close(Window window)
    {
        var closed = new TaskCompletionSource(); window.Closed += (_, _) => closed.TrySetResult();
        window.Close(); await closed.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }
}
