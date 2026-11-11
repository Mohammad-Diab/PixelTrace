using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PixelTrace.Desktop.Controls;
using PixelTrace.Desktop.Diagnostics;
using PixelTrace.Desktop.ViewModels;

namespace PixelTrace.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _model = new();
    private bool _syncing, _shutdown, _closing;

    public MainWindow()
    {
        InitializeComponent(); DataContext = _model;
        OriginalViewer.ViewChanged += state => Synchronize(OriginalViewer, ResultViewer, state);
        ResultViewer.ViewChanged += state => Synchronize(ResultViewer, OriginalViewer, state);
        OriginalViewer.PixelHovered += _model.InspectPixel;
        ResultViewer.PixelHovered += _model.InspectPixel;
        Closing += OnClosing;
    }

    private void Synchronize(ImageViewport from, ImageViewport to, ViewState state)
    {
        if (_syncing) return;
        _syncing = true;
        try { to.Apply(state); SourceZoom.Text = from.ZoomText; }
        finally { _syncing = false; }
    }

    private async void OpenClick(object? sender, RoutedEventArgs e)
    {
        if (!_model.CanOpen) return;
        try
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Open image for inspection", AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Supported images") { Patterns = ["*.jpg", "*.jpeg", "*.png", "*.bmp", "*.tif", "*.tiff", "*.webp"] }, FilePickerFileTypes.All]
            });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            {
                await _model.OpenAsync(path); OriginalViewer.Fit(); ResultViewer.Fit();
            }
        }
        catch (Exception error) { CrashLog.Write(error, "Open Image"); _model.ReportError(error); }
    }
    private async void SaveClick(object? sender, RoutedEventArgs e)
    {
        if (!_model.CanSave) return;
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save forensic visualization", DefaultExtension = "png", ShowOverwritePrompt = true,
                SuggestedFileName = Path.GetFileNameWithoutExtension(_model.FileName) + "-" + _model.SelectedMethod.Kind.ToString().ToLowerInvariant() + ".png",
                FileTypeChoices = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }]
            });
            if (file?.TryGetLocalPath() is { } path) await _model.ExportAsync(path);
        }
        catch (Exception error) { CrashLog.Write(error, "Save Result"); _model.ReportError(error); }
    }
    private async void AnalyzeClick(object? sender, RoutedEventArgs e) => await _model.AnalyzeAsync();
    private void CancelClick(object? sender, RoutedEventArgs e) => _model.Cancel();
    private void FitClick(object? sender, RoutedEventArgs e) { OriginalViewer.Fit(); ResultViewer.Fit(); }
    private void ActualClick(object? sender, RoutedEventArgs e) => OriginalViewer.ActualSize();
    private void ZoomOutClick(object? sender, RoutedEventArgs e) => OriginalViewer.Step(-1);
    private void ZoomInClick(object? sender, RoutedEventArgs e) => OriginalViewer.Step(1);
    private void DismissErrorClick(object? sender, RoutedEventArgs e) => _model.DismissError();

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_shutdown) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        try { await _model.ShutdownAsync(); }
        catch (Exception error) { CrashLog.Write(error, "window cleanup"); }
        _shutdown = true;
        // Let the first Closing event finish before requesting the final native close.
        Dispatcher.UIThread.Post(Close);
    }
}
