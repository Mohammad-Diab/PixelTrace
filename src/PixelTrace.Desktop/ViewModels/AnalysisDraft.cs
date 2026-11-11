using Insiyab.Avalonia.Controls;
using PixelTrace.Core.Analysis;

namespace PixelTrace.Desktop.ViewModels;

/// <summary>Unapplied settings. Dismissing the dialog never changes the active document or results.</summary>
public sealed class AnalysisDraft : ObservableObject
{
    private FileEntry? _file;
    private MethodChoice _method;
    public UiStrings Text { get; }
    public MethodChoice[] Methods { get; }
    public int[] BlockSizes { get; } = [16, 32, 64, 128];
    public int[] ElaQualities { get; } = [75, 85, 90, 95, 100];
    public bool IsReanalysis { get; }
    public bool CanChooseFile => !IsReanalysis;
    public FileEntry? File
    {
        get => _file;
        set { if (Set(ref _file, value)) { Changed(nameof(CanSubmit)); Changed(nameof(Validation)); } }
    }
    public MethodChoice Method
    {
        get => _method;
        set { if (Set(ref _method, value)) { Changed(nameof(CanSubmit)); Changed(nameof(Validation)); } }
    }
    public int BlockSize { get; set; }
    public int ElaQuality { get; set; }
    public string Validation => File is null ? "" : Method.Kind is AnalysisMethod.Compression or AnalysisMethod.Ela &&
        !new[] { ".jpg", ".jpeg" }.Contains(Path.GetExtension(File.Name), StringComparer.OrdinalIgnoreCase) ? Text["JpegOnly"] : "";
    public bool CanSubmit => File is not null && Validation.Length == 0;

    public AnalysisDraft(UiStrings text, MethodChoice[] methods, bool reanalysis, FileEntry? file,
        AnalysisMethod method, int blockSize, int elaQuality)
    {
        Text = text; Methods = methods; IsReanalysis = reanalysis; _file = file;
        _method = methods.First(m => m.Kind == method); BlockSize = blockSize; ElaQuality = elaQuality;
    }
}
