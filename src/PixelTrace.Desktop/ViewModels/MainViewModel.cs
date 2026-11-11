using Avalonia.Media.Imaging;
using PixelTrace.Core.Analysis;
using PixelTrace.Core.Imaging;
using PixelTrace.Desktop.Imaging;

namespace PixelTrace.Desktop.ViewModels;

public sealed record MethodChoice(AnalysisMethod Kind, string Name) { public override string ToString() => Name; }
public sealed class MethodWeight(AnalysisMethod method, string name) : ObservableObject
{
    private double _value = ScoreMath.DefaultWeight(method);
    public AnalysisMethod Method => method;
    public string Name => name;
    public double Value { get => _value; set => Set(ref _value, Math.Clamp(value, 0, 2)); }
}

public sealed class MainViewModel : ObservableObject
{
    private ImageDocument? _image;
    private AnalysisResult? _analysis;
    private WriteableBitmap? _original, _result;
    private CancellationTokenSource? _work, _renderCancellation;
    private Task _renderTask = Task.CompletedTask;
    private Task _workTask = Task.CompletedTask;
    private int _renderVersion;
    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private bool _busy, _rendering, _closing;
    private double _progress, _opacity = .6, _sensitivity = 1;
    private int _blockSize = 32, _elaQuality = 90;
    private string _status = "Open an image to begin.", _error = "", _pixel = "Wheel to zoom · drag to pan · 0 fit · 1 actual size";
    private MethodChoice _selected;

    public MainViewModel()
    {
        _selected = Methods[0];
        foreach (var weight in Weights) weight.PropertyChanged += (_, _) => Recombine();
    }

    public MethodChoice[] Methods { get; } = [new(AnalysisMethod.Combined, "Combined heatmap"), new(AnalysisMethod.Noise, "Noise consistency"),
        new(AnalysisMethod.Frequency, "Frequency analysis"), new(AnalysisMethod.Compression, "Compression artifacts"),
        new(AnalysisMethod.Texture, "Texture & edges"), new(AnalysisMethod.Ela, "Error level analysis")];
    public MethodWeight[] Weights { get; } = [new(AnalysisMethod.Noise, "Noise"), new(AnalysisMethod.Frequency, "Frequency"),
        new(AnalysisMethod.Compression, "Compression"), new(AnalysisMethod.Texture, "Texture & edges"), new(AnalysisMethod.Ela, "ELA")];
    public int[] BlockSizes { get; } = [16, 32, 64, 128];
    public int[] ElaQualities { get; } = [75, 85, 90, 95, 100];
    public WriteableBitmap? Original => _original;
    public WriteableBitmap? Result => _result ?? _original;
    public bool HasImage => _image is not null;
    public bool HasResults => _analysis is not null;
    public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) NotifyState(); } }
    public bool IsRendering { get => _rendering; private set { if (Set(ref _rendering, value)) Changed(nameof(CanSave)); } }
    public bool CanOpen => !IsBusy && !_closing;
    public bool CanAnalyze => HasImage && !IsBusy && !_closing;
    public bool CanSave => HasResults && !IsBusy && !IsRendering && CurrentMap is { Available: true, ValidBlocks: > 0 } && !_closing;
    public bool CanAdjust => HasResults && !IsBusy && !_closing;
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string Error { get => _error; private set { if (Set(ref _error, value)) Changed(nameof(HasError)); } }
    public bool HasError => Error.Length > 0;
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public string PixelReadout { get => _pixel; private set => Set(ref _pixel, value); }
    public string FileName => _image is null ? "No image loaded" : Path.GetFileName(_image.Path);
    public string FileDetails => _image is null ? "JPEG · PNG · BMP · TIFF · WebP" : $"{_image.Width:N0} × {_image.Height:N0} px  ·  {_image.Header.Format}";
    public string SourceNote => string.IsNullOrWhiteSpace(_image?.DecodeNote) ? "The original file is never modified." : _image.DecodeNote;
    public string MethodTitle => SelectedMethod.Name;
    public string MethodDescription => CurrentMap?.Description ?? "Choose an image and run the five independent analyses. Inspect each map, then compare their combined response.";
    public string AnalysisNote => _analysis?.Note ?? "For visual investigation. Heatmap intensity is not a probability of AI generation or a verdict on authenticity.";
    public string AnalysisDetails => _analysis is null ? "Ready for local analysis" : $"{_analysis.BlockSize}px blocks · ELA Q{_analysis.ElaQuality} · {_analysis.Elapsed.TotalSeconds:0.0}s";
    public string Coverage => CurrentMap is { } map ? !map.Available ? "JPEG input required" : map.ValidBlocks == 0 ? "Insufficient regional evidence" : $"{map.ValidBlocks:N0} / {map.Scores.Length:N0} blocks with usable evidence" : "No analysis yet";
    public bool MethodUnavailable => CurrentMap is { Available: false };
    public int BlockSize { get => _blockSize; set { if (Set(ref _blockSize, value)) SettingsChanged(); } }
    public int ElaQuality { get => _elaQuality; set { if (Set(ref _elaQuality, value)) SettingsChanged(); } }
    public double Opacity { get => _opacity; set { if (Set(ref _opacity, value)) QueueRender(); } }
    public double Sensitivity { get => _sensitivity; set { if (Set(ref _sensitivity, value)) QueueRender(); } }
    public MethodChoice SelectedMethod
    {
        get => _selected;
        set { if (value is not null && Set(ref _selected, value)) { NotifyResult(); QueueRender(); } }
    }
    public ScoreMap? CurrentMap => _analysis is not null && _analysis.Maps.TryGetValue(SelectedMethod.Kind, out var map) ? map : null;

    public Task OpenAsync(string path) => RunWorkAsync(async token =>
    {
        Status = "Decoding image…";
        await StopRenderAsync();
        ImageDocument? next = null; WriteableBitmap? bitmap = null;
        try
        {
            (next, bitmap) = await Task.Run(() =>
            {
                var doc = ImageDocument.Load(path, token);
                try
                {
                    using var oriented = ImageDocument.Orient(doc.Pixels, doc.Header.Orientation);
                    return (doc, BitmapFactory.Create(oriented));
                }
                catch { doc.Dispose(); throw; }
            }, token);
            token.ThrowIfCancellationRequested();
            var oldImage = _image; var oldOriginal = _original; var oldResult = _result;
            _image = next; _original = bitmap; _result = null; _analysis = null;
            next = null; bitmap = null;
            NotifyImage();
            oldImage?.Dispose(); oldOriginal?.Dispose(); oldResult?.Dispose();
            Status = "Image ready. Click Analyze to inspect local inconsistencies.";
        }
        finally { next?.Dispose(); bitmap?.Dispose(); }
    });

    public Task AnalyzeAsync() => !CanAnalyze ? Task.CompletedTask : RunWorkAsync(async token =>
    {
        Status = "Starting analysis…";
        var options = new AnalysisOptions(BlockSize, ElaQuality);
        var reporter = new Progress<AnalysisProgress>(p =>
        {
            if (!token.IsCancellationRequested && IsBusy) { Progress = Math.Max(Progress, p.Value); Status = p.Message; }
        });
        var result = await Task.Run(() => new ForensicAnalyzer().Analyze(_image!, options, reporter, token), token);
        token.ThrowIfCancellationRequested();
        _analysis = result;
        Recombine(); NotifyResult();
        Status = $"Analysis complete · {result.Elapsed.TotalSeconds:0.0}s. Switch maps to compare evidence.";
        QueueRender();
    });

    public Task ExportAsync(string path) => !CanSave ? Task.CompletedTask : RunWorkAsync(async token =>
    {
        Status = "Rendering and exporting full-resolution PNG…";
        var settings = new VisualizationSettings(Opacity, Sensitivity); var map = CurrentMap!;
        await Task.Run(() => HeatmapRenderer.ExportPng(path, _image!, map, settings, token), token);
        Status = $"Saved {Path.GetFileName(path)} · {_image!.Width:N0} × {_image.Height:N0} px"; Progress = 100;
    });

    private Task RunWorkAsync(Func<CancellationToken, Task> action)
    {
        if (IsBusy || _closing) return Task.CompletedTask;
        _workTask = Run(); return _workTask;
        async Task Run()
        {
            _work?.Dispose(); _work = new();
            IsBusy = true; Error = ""; Progress = 0;
            try { await action(_work.Token); }
            catch (OperationCanceledException) { Status = "Cancelled. Any previous image and completed results are retained."; }
            catch (Exception e) { Error = FriendlyError(e); Status = "Operation could not be completed."; }
            finally { IsBusy = false; }
        }
    }

    public void Cancel() { _work?.Cancel(); _renderCancellation?.Cancel(); Status = "Cancelling…"; }
    public void DismissError() => Error = "";
    public void ReportError(Exception error)
    {
        Error = FriendlyError(error); Status = "Operation could not be completed.";
    }

    private void QueueRender()
    {
        if (_closing || _analysis is null || _image is null) return;
        _renderCancellation?.Cancel();
        _renderCancellation?.Dispose();
        _renderCancellation = new();
        IsRendering = true;
        _renderTask = RenderAsync(_renderCancellation.Token, ++_renderVersion);
    }

    private async Task RenderAsync(CancellationToken token, int version)
    {
        WriteableBitmap? bitmap = null;
        try
        {
            await Task.Delay(90, token);
            await _renderGate.WaitAsync(token);
            try
            {
                token.ThrowIfCancellationRequested();
                var image = _image!; var map = CurrentMap!;
                if (!map.Available)
                {
                    var previous = _result; _result = null; Changed(nameof(Result)); previous?.Dispose(); return;
                }
                var settings = new VisualizationSettings(Opacity, Sensitivity);
                bitmap = await Task.Run(() =>
                {
                    using var rendered = HeatmapRenderer.Render(image, map, settings, token);
                    token.ThrowIfCancellationRequested();
                    return BitmapFactory.Create(rendered);
                }, token);
                token.ThrowIfCancellationRequested();
                var old = _result; _result = bitmap; bitmap = null; Changed(nameof(Result)); old?.Dispose();
            }
            finally { _renderGate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Error = FriendlyError(e); }
        finally { bitmap?.Dispose(); if (version == _renderVersion) IsRendering = false; }
    }

    private async Task StopRenderAsync()
    {
        _renderCancellation?.Cancel();
        await _renderTask;
        // Earlier cancelled renders can still be leaving a non-interruptible native call.
        await _renderGate.WaitAsync(); _renderGate.Release();
    }

    private void Recombine()
    {
        if (_analysis is null) return;
        var maps = _analysis.Maps.ToDictionary(kv => kv.Key, kv => kv.Value);
        maps[AnalysisMethod.Combined] = ScoreMath.Combine(maps.Values, Weights.ToDictionary(w => w.Method, w => w.Value));
        _analysis = _analysis with { Maps = maps };
        NotifyResult();
        if (SelectedMethod.Kind == AnalysisMethod.Combined) QueueRender();
    }

    public void InspectPixel(int x, int y)
    {
        if (_image is null) return;
        int width = _image.Pixels.Width, height = _image.Pixels.Height;
        (int rx, int ry) = _image.Header.Orientation switch
        {
            2 => (width - 1 - x, y), 3 => (width - 1 - x, height - 1 - y), 4 => (x, height - 1 - y),
            5 => (y, x), 6 => (y, height - 1 - x), 7 => (width - 1 - y, height - 1 - x), 8 => (width - 1 - y, x), _ => (x, y)
        };
        string text = $"x {x:N0} · y {y:N0}";
        if (CurrentMap is { Available: true } map && rx >= 0 && ry >= 0 && rx < width && ry < height)
        {
            int i = ry / map.BlockSize * map.Columns + rx / map.BlockSize;
            text += map.Validity[i] > .05f ? $" · block intensity {map.Scores[i]:0.000} (0–1 scale, not probability)" : " · masked: insufficient evidence";
        }
        PixelReadout = text;
    }

    public async Task ShutdownAsync()
    {
        _closing = true; _work?.Cancel(); _renderCancellation?.Cancel();
        await _workTask; await StopRenderAsync();
        var image = _image; var original = _original; var result = _result;
        _image = null; _original = null; _result = null;
        // Unbind both viewers while the bitmaps are still alive. Native close/resize/pointer events
        // can otherwise inspect a bitmap or Mat whose native resources have already been released.
        Changed(nameof(Original)); Changed(nameof(Result));
        image?.Dispose(); original?.Dispose(); result?.Dispose();
        _work?.Dispose(); _renderCancellation?.Dispose(); _renderGate.Dispose();
    }

    private void SettingsChanged() { if (HasResults) Status = "Block size / ELA quality changed. Click Analyze to apply; the displayed result keeps its previous settings."; }
    private void NotifyImage()
    {
        foreach (var n in new[] { nameof(Original), nameof(Result), nameof(HasImage), nameof(FileName), nameof(FileDetails), nameof(SourceNote) }) Changed(n);
        NotifyResult(); NotifyState();
    }
    private void NotifyResult()
    {
        foreach (var n in new[] { nameof(HasResults), nameof(MethodTitle), nameof(MethodDescription), nameof(AnalysisNote), nameof(AnalysisDetails), nameof(Coverage), nameof(MethodUnavailable) }) Changed(n);
        NotifyState();
    }
    private void NotifyState() { Changed(nameof(CanOpen)); Changed(nameof(CanAnalyze)); Changed(nameof(CanSave)); Changed(nameof(CanAdjust)); }
    private static string FriendlyError(Exception e) => e is OutOfMemoryException ? "Not enough memory. Try a smaller copy of the image." :
        e is DllNotFoundException or TypeInitializationException ? "OpenCV could not start. Keep the complete Windows publish folder, including OpenCvSharpExtern.dll. Windows N/Server also requires the Media Foundation OS feature. " + e.GetBaseException().Message : e.Message;
}
