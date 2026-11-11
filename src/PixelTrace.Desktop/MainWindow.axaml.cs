using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.Data;
using Insiyab.Avalonia.Controls;
using PixelTrace.Desktop.Controls;
using PixelTrace.Desktop.Diagnostics;
using PixelTrace.Desktop.ViewModels;

namespace PixelTrace.Desktop;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _model = new();
    private bool _shutdown, _closing;
    public Dialog? ActiveAnalysisDialog { get; private set; }

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
            await ShowAnalysisSetupAsync((Control?)sender ?? OpenButton, false);
        }
        catch (Exception error) { CrashLog.Write(error, "Open Image"); _model.ReportError(error); }
    }
    private async void ReanalyzeClick(object? sender, RoutedEventArgs e)
    {
        if (!_model.CanAnalyze) return;
        try { await ShowAnalysisSetupAsync((Control?)sender ?? ReanalyzeButton, true); }
        catch (Exception error) { CrashLog.Write(error, "Reanalyze"); _model.ReportError(error); }
    }
    public async Task ShowAnalysisSetupAsync(Control anchor, bool reanalysis)
    {
        if (ActiveAnalysisDialog is not null || !_model.CanOpen) return;
        var draft = _model.CreateDraft(reanalysis);
        var cancel = new Button { Content = draft.Text["Cancel"], Classes = { "secondary" } };
        var analyze = new Button { Content = draft.Text["Analyze"], Classes = { "primary" } };
        Dialog.SetResult(cancel, false); Dialog.SetResult(analyze, true);
        analyze.Bind(IsEnabledProperty, new Binding(nameof(AnalysisDraft.CanSubmit)) { Source = draft });
        var dialog = new Dialog
        {
            Title = draft.Text[reanalysis ? "Reanalyze" : "Setup"], DataContext = draft,
            FlowDirection = _model.Direction, Content = new AnalysisSetupView(draft),
            Foot = new DialogFoot { Children = { cancel, analyze } },
            MinWidth = 420, MaxWidth = 520, ClosedBy = DialogClosedBy.CloseRequest
        };
        ActiveAnalysisDialog = dialog;
        object? result;
        try { result = await dialog.ShowAsync(anchor); }
        finally { ActiveAnalysisDialog = null; }
        if (result is true && draft.CanSubmit && !_closing)
        {
            await _model.ExecuteDraftAsync(draft);
            if (!reanalysis) Viewer.Fit();
        }
    }
    private async void SaveClick(object? sender, RoutedEventArgs e)
    {
        if (!_model.CanSave) return;
        try
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = _model.Text["Save"], DefaultExtension = "png", ShowOverwritePrompt = true,
                SuggestedFileName = Path.GetFileNameWithoutExtension(_model.FileName) + "-" + _model.SelectedMethod.Kind.ToString().ToLowerInvariant() + ".png",
                FileTypeChoices = [new FilePickerFileType("PNG image") { Patterns = ["*.png"] }]
            });
            if (file?.TryGetLocalPath() is { } path) await _model.ExportAsync(path);
        }
        catch (Exception error) { CrashLog.Write(error, "Save Result"); _model.ReportError(error); }
    }
    private void LanguageClick(object? sender, RoutedEventArgs e) { _model.IsShowingOriginal = false; _model.ToggleLanguage(); }
    private void DefaultClick(object? sender, RoutedEventArgs e) => _model.ResetDisplay();
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
        ActiveAnalysisDialog?.Close(false);
        _model.IsShowingOriginal = false;
        try { await _model.ShutdownAsync(); }
        catch (Exception error) { CrashLog.Write(error, "window cleanup"); }
        _shutdown = true;
        // Let the first Closing event finish before requesting the final native close.
        Dispatcher.UIThread.Post(Close);
    }
}
