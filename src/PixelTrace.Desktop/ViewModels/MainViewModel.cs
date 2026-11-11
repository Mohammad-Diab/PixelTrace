using System.Globalization;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Insiyab.Avalonia;
using Insiyab.Avalonia.Controls;
using PixelTrace.Core.Analysis;
using PixelTrace.Core.Imaging;
using PixelTrace.Desktop.Imaging;

namespace PixelTrace.Desktop.ViewModels;

public sealed record MethodChoice(AnalysisMethod Kind, string Name) { public override string ToString() => Name; }

public sealed class MainViewModel : ObservableObject
{
    private ImageDocument? _image;
    private FileEntry? _sourceFile;
    private AnalysisResult? _analysis;
    private WriteableBitmap? _original, _result;
    private CancellationTokenSource? _work, _renderCancellation;
    private Task _renderTask = Task.CompletedTask, _workTask = Task.CompletedTask;
    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private int _renderVersion;
    private bool _busy, _rendering, _closing, _showOriginal, _showSave, _arabic;
    private double? _hoverIntensity;
    private double _progress, _opacity = .6, _sensitivity = 1;
    private string _statusKey = "Ready", _error = "", _errorKey = "Failed", _pixel = "";
    private object[] _statusArgs = [];
    private MethodChoice _selected;
    private MethodChoice? _selectedResult;
    private AnalysisMethod _appliedMethod = AnalysisMethod.Combined;
    private MethodChoice[] _availableMethods = [];

    public MainViewModel() { Methods = MakeMethods(); _selected = Methods[0]; }
    public UiStrings Text { get; private set; } = new(false);
    public bool IsArabic => _arabic;
    public FlowDirection Direction => IsArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    public MethodChoice[] Methods { get; private set; }
    public MethodChoice[] AvailableMethods => _availableMethods;
    public int BlockSize { get; set; } = 32;
    public int ElaQuality { get; set; } = 90;
    public WriteableBitmap? Original => _original;
    public WriteableBitmap? Result => _result ?? _original;
    public WriteableBitmap? DisplayImage => IsShowingOriginal ? Original : Result;
    public bool HasImage => _image is not null;
    public bool HasResults => _analysis is not null;
    public bool ShowSave => _showSave;
    public bool CanCompare => _original is not null && _result is not null && CurrentMap is { Available: true } && !_closing;
    public bool IsShowingOriginal
    {
        get => _showOriginal;
        set { if (Set(ref _showOriginal, value && CanCompare)) { Changed(nameof(DisplayImage)); Changed(nameof(HoverIntensity)); } }
    }
    public double? HoverIntensity => IsShowingOriginal ? null : _hoverIntensity;
    public bool IsBusy { get => _busy; private set { if (Set(ref _busy, value)) NotifyState(); } }
    public bool IsRendering { get => _rendering; private set { if (Set(ref _rendering, value)) Changed(nameof(CanSave)); } }
    public bool CanOpen => !IsBusy && !_closing;
    public bool CanAnalyze => HasImage && CanOpen;
    public bool CanSave => HasResults && !IsBusy && !IsRendering && CurrentMap is { Available: true, ValidBlocks: > 0 } && !_closing;
    public bool CanAdjust => HasResults && !IsBusy && !_closing;
    public string Status => string.Format(CultureInfo.InvariantCulture, Text[_statusKey], _statusArgs);
    public string Error => _error;
    public string ErrorText => Text[_errorKey];
    public bool HasError => _error.Length > 0;
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public string PixelReadout => _pixel.Length == 0 ? Text["Navigation"] : _pixel;
    public string FileName => _sourceFile?.Name ?? (_image is null ? Text["NoImage"] : Path.GetFileName(_image.Path));
    public string FileDetails => _image is null ? "" : $"{_image.Width:N0} × {_image.Height:N0} · {_image.Header.Format}";
    public string MethodTitle => SelectedMethod.Name;
    public string MethodDescription => Text[SelectedMethod.Kind + "Info"];
    public bool MethodUnavailable => CurrentMap is { Available: false };
    public string SensitivityValue => Sensitivity.ToString("0.00×", CultureInfo.InvariantCulture);
    public string OpacityValue => Opacity.ToString("0%", CultureInfo.InvariantCulture);
    public double Opacity
    {
        get => _opacity;
        set { if (Set(ref _opacity, Math.Clamp(value, 0, 1))) { Changed(nameof(OpacityValue)); QueueRender(); } }
    }
    public double Sensitivity
    {
        get => _sensitivity;
        set { if (Set(ref _sensitivity, Math.Clamp(value, .4, 2.5))) { Changed(nameof(SensitivityValue)); QueueRender(); } }
    }
    public MethodChoice SelectedMethod
    {
        get => _selected;
        set { if (value is not null && Set(ref _selected, value)) { ClearInspection(); NotifyResult(); QueueRender(); } }
    }
    public MethodChoice? SelectedResult
    {
        get => _selectedResult;
        set { if (Set(ref _selectedResult, value) && value is not null) SelectedMethod = value; }
    }
    public ScoreMap? CurrentMap => _analysis is not null && _analysis.Maps.TryGetValue(SelectedMethod.Kind, out var map) ? map : null;

    private MethodChoice[] MakeMethods() => Enum.GetValues<AnalysisMethod>()
        .Select(m => new MethodChoice(m, Text[m == AnalysisMethod.Combined ? "CombinedResult" : m.ToString()])).ToArray();
    public void ToggleLanguage()
    {
        _arabic = !_arabic; Text = new(IsArabic); InsiyabTheme.SetLanguage(IsArabic ? "ar" : "en");
        var kind = _selected.Kind; Methods = MakeMethods(); _selected = Methods.First(m => m.Kind == kind);
        RefreshMethods();
        SyncSelectedResult();
        foreach (var n in new[] { nameof(Text), nameof(IsArabic), nameof(Direction), nameof(Methods), nameof(AvailableMethods),
            nameof(SelectedMethod), nameof(Status), nameof(ErrorText), nameof(FileName), nameof(MethodTitle), nameof(MethodDescription) }) Changed(n);
        ClearInspection();
    }
    public void ResetDisplay() { Sensitivity = 1; Opacity = .6; }
    public AnalysisDraft CreateDraft(bool reanalysis)
    {
        if (reanalysis && !HasImage) throw new InvalidOperationException("No active image.");
        var methods = Methods.Select(m => m.Kind == AnalysisMethod.Combined ? m with { Name = Text["Combined"] } : m).ToArray();
        return new(Text, methods, reanalysis, reanalysis ? _sourceFile : null,
            reanalysis ? _appliedMethod : AnalysisMethod.Combined, BlockSize, ElaQuality);
    }

    // Kept as an independently testable loading operation; the production dialog commits only after analysis.
    public Task OpenAsync(string path) => RunWorkAsync(async token =>
    {
        SetStatus("Loading"); await StopRenderAsync();
        var file = new FileEntry(Path.GetFileName(path), File.Exists(path) ? new FileInfo(path).Length : 0,
            () => Task.FromResult<Stream>(File.OpenRead(path)));
        var image = await Task.Run(() => ImageDocument.Load(path, token), token);
        try { CommitImage(image, MakeBitmap(image), file, null, AnalysisMethod.Combined); }
        catch { image.Dispose(); throw; }
        SetStatus("ImageReady");
    });

    public Task AnalyzeAsync() => !CanAnalyze ? Task.CompletedTask : RunWorkAsync(token => AnalyzeCurrentAsync(token));
    private async Task AnalyzeCurrentAsync(CancellationToken token)
    {
        BeginAnalysis(); var result = await Calculate(_image!, new(BlockSize, ElaQuality), AnalysisMethod.Combined, token);
        token.ThrowIfCancellationRequested(); _analysis = result; _appliedMethod = AnalysisMethod.Combined;
        NotifyResult(); SetStatus("Done"); QueueRender();
    }

    public Task ExecuteDraftAsync(AnalysisDraft draft)
    {
        if (!draft.CanSubmit) return Task.CompletedTask;
        return RunWorkAsync(async token =>
        {
            BeginAnalysis();
            ImageDocument? next = null; WriteableBitmap? bitmap = null;
            try
            {
                var image = draft.IsReanalysis ? _image! : next = await ReadEntry(draft.File!, token);
                if (draft.Method.Kind is AnalysisMethod.Compression or AnalysisMethod.Ela && image.Header.Format != "JPEG")
                    throw new InvalidDataException(Text["JpegOnly"]);
                var result = await Calculate(image, new(draft.BlockSize, draft.ElaQuality), draft.Method.Kind, token);
                if (next is not null) bitmap = await Task.Run(() => MakeBitmap(next), token);
                token.ThrowIfCancellationRequested(); await StopRenderAsync();
                if (next is not null)
                {
                    CommitImage(next, bitmap!, draft.File!, result, draft.Method.Kind); next = null; bitmap = null;
                }
                else { _analysis = result; _appliedMethod = draft.Method.Kind; _selected = Methods.First(m => m.Kind == draft.Method.Kind); NotifyResult(); }
                BlockSize = draft.BlockSize; ElaQuality = draft.ElaQuality;
                SetStatus("Done"); QueueRender();
            }
            finally { next?.Dispose(); bitmap?.Dispose(); }
        });
    }
    private Task<AnalysisResult> Calculate(ImageDocument image, AnalysisOptions options, AnalysisMethod method, CancellationToken token)
    {
        var progress = new Progress<AnalysisProgress>(p => { if (!token.IsCancellationRequested && IsBusy) Progress = Math.Max(Progress, p.Value); });
        return Task.Run(() => new ForensicAnalyzer().Analyze(image, options, progress, token,
            method == AnalysisMethod.Combined ? null : [method]), token);
    }
    private void BeginAnalysis() { _showSave = true; Changed(nameof(ShowSave)); SetStatus("Analyzing"); }
    private static WriteableBitmap MakeBitmap(ImageDocument image)
    {
        using var oriented = ImageDocument.Orient(image.Pixels, image.Header.Orientation);
        return BitmapFactory.Create(oriented);
    }
    private void CommitImage(ImageDocument image, WriteableBitmap bitmap, FileEntry file, AnalysisResult? result, AnalysisMethod method)
    {
        var oldImage = _image; var oldOriginal = _original; var oldResult = _result;
        _image = image; _original = bitmap; _result = null; _sourceFile = file; _analysis = result; _appliedMethod = method;
        _selected = Methods.First(m => m.Kind == method); IsShowingOriginal = false; ClearInspection();
        foreach (var n in new[] { nameof(Original), nameof(Result), nameof(DisplayImage), nameof(HasImage), nameof(FileName), nameof(FileDetails), nameof(SelectedMethod) }) Changed(n);
        NotifyResult(); oldImage?.Dispose(); oldOriginal?.Dispose(); oldResult?.Dispose();
    }
    private static async Task<ImageDocument> ReadEntry(FileEntry file, CancellationToken token)
    {
        if (file.File?.TryGetLocalPath() is { } path) return await Task.Run(() => ImageDocument.Load(path, token), token);
        if (file.Open is null) throw new InvalidDataException("The image cannot be read.");
        await using var source = await file.Open(); using var bytes = new MemoryStream();
        var buffer = new byte[81920]; int count;
        while ((count = await source.ReadAsync(buffer, token)) > 0)
        {
            if (bytes.Length + count > 256L * 1024 * 1024) throw new InvalidDataException("Image exceeds 256 MB.");
            bytes.Write(buffer, 0, count);
        }
        return await Task.Run(() => ImageDocument.LoadBytes(bytes.ToArray(), file.Name, token), token);
    }

    public Task ExportAsync(string path) => !CanSave ? Task.CompletedTask : RunWorkAsync(async token =>
    {
        SetStatus("Saving"); var settings = new VisualizationSettings(Opacity, Sensitivity); var map = CurrentMap!;
        await Task.Run(() => HeatmapRenderer.ExportPng(path, _image!, map, settings, token), token);
        SetStatus("Saved", Path.GetFileName(path)); Progress = 100;
    });
    private Task RunWorkAsync(Func<CancellationToken, Task> action)
    {
        if (IsBusy || _closing) return Task.CompletedTask;
        _workTask = Run(); return _workTask;
        async Task Run()
        {
            _work?.Dispose(); _work = new(); IsBusy = true; DismissError(); Progress = 0;
            try { await action(_work.Token); }
            catch (OperationCanceledException) { SetStatus("Cancelled"); }
            catch (Exception error) { ReportError(error); }
            finally { IsBusy = false; }
        }
    }
    private void SetStatus(string key, params object[] args) { _statusKey = key; _statusArgs = args; Changed(nameof(Status)); }
    public void Cancel() { _work?.Cancel(); _renderCancellation?.Cancel(); SetStatus("Cancelling"); }
    public void DismissError() { _error = ""; Changed(nameof(Error)); Changed(nameof(HasError)); }
    public void ReportError(Exception error)
    {
        _error = error.Message;
        _errorKey = error switch { FileNotFoundException => "MissingImage", InvalidDataException => "InvalidImage", OutOfMemoryException => "MemoryError", IOException => "FileError", _ => "Failed" };
        Changed(nameof(Error)); Changed(nameof(ErrorText)); Changed(nameof(HasError)); SetStatus("Failed");
    }
    private void QueueRender()
    {
        if (_closing || _analysis is null || _image is null || CurrentMap is null) return;
        _renderCancellation?.Cancel(); _renderCancellation?.Dispose(); _renderCancellation = new();
        IsRendering = true; _renderTask = RenderAsync(_renderCancellation.Token, ++_renderVersion);
    }
    private async Task RenderAsync(CancellationToken token, int version)
    {
        WriteableBitmap? bitmap = null;
        try
        {
            await Task.Delay(90, token); await _renderGate.WaitAsync(token);
            try
            {
                token.ThrowIfCancellationRequested(); var image = _image!; var map = CurrentMap!;
                if (!map.Available) { ReplaceResult(null); return; }
                var settings = new VisualizationSettings(Opacity, Sensitivity);
                bitmap = await Task.Run(() => { using var rendered = HeatmapRenderer.Render(image, map, settings, token); token.ThrowIfCancellationRequested(); return BitmapFactory.Create(rendered); }, token);
                token.ThrowIfCancellationRequested(); ReplaceResult(bitmap); bitmap = null;
            }
            finally { _renderGate.Release(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { ReportError(error); }
        finally { bitmap?.Dispose(); if (version == _renderVersion) IsRendering = false; }
    }
    private async Task StopRenderAsync() { _renderCancellation?.Cancel(); await _renderTask; await _renderGate.WaitAsync(); _renderGate.Release(); }
    private void ReplaceResult(WriteableBitmap? bitmap)
    {
        var old = _result; _result = bitmap; NotifyState(); Changed(nameof(Result)); Changed(nameof(DisplayImage)); old?.Dispose();
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
        _hoverIntensity = null; _pixel = $"x {x:N0} · y {y:N0}";
        if (!IsShowingOriginal && CurrentMap is { Available: true } map && rx >= 0 && ry >= 0 && rx < width && ry < height)
        {
            int i = ry / map.BlockSize * map.Columns + rx / map.BlockSize;
            if (map.Validity[i] > .05f) { _hoverIntensity = map.Scores[i]; _pixel += $" · {Text["Intensity"]} {map.Scores[i]:0.000}"; }
            else _pixel += " · " + Text["Masked"];
        }
        Changed(nameof(HoverIntensity)); Changed(nameof(PixelReadout));
    }
    public void ClearInspection() { _hoverIntensity = null; _pixel = ""; Changed(nameof(HoverIntensity)); Changed(nameof(PixelReadout)); }
    public async Task ShutdownAsync()
    {
        _closing = true; _work?.Cancel(); _renderCancellation?.Cancel(); await _workTask; await StopRenderAsync();
        var image = _image; var original = _original; var result = _result;
        _image = null; _original = null; _result = null; IsShowingOriginal = false; ClearInspection();
        Changed(nameof(Original)); Changed(nameof(Result)); Changed(nameof(DisplayImage));
        image?.Dispose(); original?.Dispose(); result?.Dispose(); _work?.Dispose(); _renderCancellation?.Dispose(); _renderGate.Dispose();
    }
    private void NotifyResult()
    {
        RefreshMethods();
        SyncSelectedResult();
        foreach (var n in new[] { nameof(HasResults), nameof(SelectedMethod), nameof(MethodTitle), nameof(MethodDescription), nameof(MethodUnavailable) }) Changed(n);
        NotifyState();
    }
    private void SyncSelectedResult()
    {
        SelectedResult = _availableMethods.FirstOrDefault(m => m.Kind == _selected.Kind);
    }
    private void RefreshMethods()
    {
        var available = _analysis is null ? [] : Methods.Where(m => _analysis.Maps.TryGetValue(m.Kind, out var map) &&
            map.Available && (_appliedMethod == AnalysisMethod.Combined || m.Kind == _appliedMethod)).ToArray();
        if (_availableMethods.SequenceEqual(available)) return;
        _availableMethods = available; Changed(nameof(AvailableMethods));
    }
    private void NotifyState()
    {
        if (!CanCompare) IsShowingOriginal = false;
        foreach (var n in new[] { nameof(CanOpen), nameof(CanAnalyze), nameof(CanSave), nameof(CanAdjust), nameof(CanCompare) }) Changed(n);
    }
}
