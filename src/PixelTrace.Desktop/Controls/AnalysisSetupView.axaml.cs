using Avalonia.Controls;
using PixelTrace.Desktop.ViewModels;

namespace PixelTrace.Desktop.Controls;

public sealed partial class AnalysisSetupView : UserControl
{
    public AnalysisSetupView() => InitializeComponent();
    public AnalysisSetupView(AnalysisDraft draft) : this()
    {
        DataContext = draft;
        if (draft.File is { } file) ImageFile.SetFiles([file]);
        ImageFile.FilesChanged += (_, _) => { if (draft.CanChooseFile) draft.File = ImageFile.Files.FirstOrDefault(); };
    }
}
