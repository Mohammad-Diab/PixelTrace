using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PixelTrace.Desktop.Controls;
using PixelTrace.Desktop.Diagnostics;
using PixelTrace.Desktop.ViewModels;

namespace PixelTrace.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _model = new();
    private bool _shutdown, _closing;

    public MainWindow()
    {
        InitializeComponent(); DataContext = _model;
        Viewer.ViewChanged += _ => ViewZoom.Text = Viewer.ZoomText;
        Viewer.PixelHovered += _model.InspectPixel;
        Viewer.PointerExited += (_, _) => _model.ClearInspection();
        CompareButton.AddHandler(PointerPressedEvent, ComparePressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        CompareButton.AddHandler(PointerReleasedEvent, (_, _) => _model.IsShowingOriginal = false, RoutingStrategies.Tunnel, handledEventsToo: true);
        CompareButton.PointerCaptureLost += (_, _) => _model.IsShowingOriginal = false;
        CompareButton.LostFocus += (_, _) => _model.IsShowingOriginal = false;
        CompareButton.AddHandler(KeyDownEvent, (_, e) => { if (e.Key is Key.Space or Key.Enter) _model.IsShowingOriginal = true; }, RoutingStrategies.Tunnel, handledEventsToo: true);
        CompareButton.AddHandler(KeyUpEvent, (_, e) => { if (e.Key is Key.Space or Key.Enter) _model.IsShowingOriginal = false; }, RoutingStrategies.Tunnel, handledEventsToo: true);
        Deactivated += (_, _) => _model.IsShowingOriginal = false;
        Closing += OnClosing;
    }

    private void ComparePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Pointer.Type == PointerType.Touch || e.GetCurrentPoint(CompareButton).Properties.IsLeftButtonPressed)
            _model.IsShowingOriginal = true;
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
                await _model.OpenAsync(path); Viewer.Fit();
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
    private void FitClick(object? sender, RoutedEventArgs e) => Viewer.Fit();
    private void ActualClick(object? sender, RoutedEventArgs e) => Viewer.ActualSize();
    private void ZoomOutClick(object? sender, RoutedEventArgs e) => Viewer.Step(-1);
    private void ZoomInClick(object? sender, RoutedEventArgs e) => Viewer.Step(1);
    private void DismissErrorClick(object? sender, RoutedEventArgs e) => _model.DismissError();

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_shutdown) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        _model.IsShowingOriginal = false;
        try { await _model.ShutdownAsync(); }
        catch (Exception error) { CrashLog.Write(error, "window cleanup"); }
        _shutdown = true;
        // Let the first Closing event finish before requesting the final native close.
        Dispatcher.UIThread.Post(Close);
    }
}
