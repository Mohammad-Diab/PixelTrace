param([int]$ProcessId, [string]$InputImage, [string]$OutputImage, [switch]$CloseAtEnd, [switch]$CloseDuringAnalysis)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Drawing
if (Test-Path -LiteralPath $OutputImage) { throw 'Choose a new output path for this verification run.' }
$sourceHashBefore = (Get-FileHash -LiteralPath $InputImage -Algorithm SHA256).Hash
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class PixelTraceFileDialog {
    public delegate bool EnumProc(IntPtr hwnd, IntPtr data);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr data);
    [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr hwnd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr hwnd, StringBuilder name, int size);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wparam, string text);
    [DllImport("user32.dll", EntryPoint="SendMessageW")] static extern IntPtr SendMessageValue(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam);
    public static void Choose(IntPtr dialog, string path) {
        IntPtr combo = IntPtr.Zero, edit = IntPtr.Zero;
        EnumChildWindows(dialog, (h, p) => {
            var name = new StringBuilder(128); GetClassName(h, name, 128);
            if (GetDlgCtrlID(h) == 1148 && name.ToString() == "ComboBoxEx32") combo = h;
            if (GetDlgCtrlID(h) == 1148 && name.ToString() == "Edit") edit = h;
            return true;
        }, IntPtr.Zero);
        if (combo != IntPtr.Zero) EnumChildWindows(combo, (h, p) => {
            var name = new StringBuilder(128); GetClassName(h, name, 128);
            if (name.ToString() == "Edit") edit = h; return true;
        }, IntPtr.Zero);
        if (edit == IntPtr.Zero) EnumChildWindows(dialog, (h, p) => {
            var name = new StringBuilder(128); GetClassName(h, name, 128);
            if (GetDlgCtrlID(h) == 1001 && name.ToString() == "Edit") edit = h; return true;
        }, IntPtr.Zero);
        if (edit == IntPtr.Zero) {
            var found = new StringBuilder();
            EnumChildWindows(dialog, (h, p) => { var name = new StringBuilder(128); GetClassName(h, name, 128); found.Append(h + ":" + GetDlgCtrlID(h) + ":" + name + "; "); return true; }, IntPtr.Zero);
            throw new Exception("Native file name edit not found. Dialog=" + dialog + "; " + found);
        }
        SendMessageValue(edit, 0xB1, IntPtr.Zero, (IntPtr)(-1));
        SendMessage(edit, 0xC2, (IntPtr)1, path);
        PostMessage(dialog, 0x111, (IntPtr)1, IntPtr.Zero);
    }
}
'@
$scope = [System.Windows.Automation.TreeScope]::Descendants
$names = [System.Windows.Automation.AutomationElement]::NameProperty
$processes = [System.Windows.Automation.AutomationElement]::ProcessIdProperty
$types = [System.Windows.Automation.AutomationElement]::ControlTypeProperty
$desktop = [System.Windows.Automation.AutomationElement]::RootElement
$processCondition = New-Object System.Windows.Automation.PropertyCondition $processes, $ProcessId
function Find-Element($root, $name, $kind) {
    $nameCondition = New-Object System.Windows.Automation.PropertyCondition $names, $name
    if ($kind) {
        $typeCondition = New-Object System.Windows.Automation.PropertyCondition $types, $kind
        $condition = New-Object System.Windows.Automation.AndCondition $nameCondition, $typeCondition
    } else { $condition = $nameCondition }
    return $root.FindFirst($scope, $condition)
}
function Wait-For($test, $label) {
    $limit = [DateTime]::UtcNow.AddSeconds(25)
    do { $value = & $test; if ($value) { return $value }; Start-Sleep -Milliseconds 150 } while ([DateTime]::UtcNow -lt $limit)
    throw "Timed out: $label"
}
function Invoke-Button($root, $name) {
    $button = Find-Element $root $name ([System.Windows.Automation.ControlType]::Button)
    if (!$button) { throw "Button not found: $name" }
    $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
}
function Windows { return $desktop.FindAll([System.Windows.Automation.TreeScope]::Children, $processCondition) }
function Verify-Export {
    if ((Get-FileHash -LiteralPath $InputImage -Algorithm SHA256).Hash -ne $sourceHashBefore) { throw 'The original file changed.' }
    $source = [Drawing.Image]::FromFile($InputImage)
    $result = [Drawing.Image]::FromFile($OutputImage)
    try {
        $expectedWidth = $source.Width; $expectedHeight = $source.Height
        if ($source.PropertyIdList -contains 274) {
            $orientation = [BitConverter]::ToUInt16($source.GetPropertyItem(274).Value, 0)
            if ($orientation -ge 5) { $expectedWidth = $source.Height; $expectedHeight = $source.Width }
        }
        if ($result.Width -ne $expectedWidth -or $result.Height -ne $expectedHeight) { throw 'Export dimensions changed.' }
        if ($result.RawFormat.Guid -ne [Drawing.Imaging.ImageFormat]::Png.Guid) { throw 'Export is not PNG.' }
        Write-Output "Verified PNG $($result.Width)x$($result.Height); original SHA256 unchanged."
    } finally { $source.Dispose(); $result.Dispose() }
}
$window = Wait-For {
    foreach ($item in (Windows)) { if ($item.Current.Name -like 'PixelTrace*') { return $item } }
} 'main window'
$pendingSave = Find-Element $window 'Save forensic visualization' ([System.Windows.Automation.ControlType]::Window)
if ($pendingSave) {
    [PixelTraceFileDialog]::Choose([IntPtr]$pendingSave.Current.NativeWindowHandle, $OutputImage)
    [void](Wait-For { Test-Path -LiteralPath $OutputImage } 'PNG export')
    Write-Output "Saved through native picker: $OutputImage"
    Verify-Export
    if ($CloseAtEnd) { & (Join-Path $PSScriptRoot 'Close-Desktop.ps1') -ProcessId $ProcessId }
    exit 0
}
$alreadyOpen = Find-Element $window 'Open image for inspection' ([System.Windows.Automation.ControlType]::Window)
if (!$alreadyOpen) { Invoke-Button $window 'Open Image' }
Write-Output 'Open Image invoked.'
$dialog = Wait-For {
    Find-Element $window 'Open image for inspection' ([System.Windows.Automation.ControlType]::Window)
} 'open file dialog'
[PixelTraceFileDialog]::Choose([IntPtr]$dialog.Current.NativeWindowHandle, $InputImage)
[void](Wait-For { Find-Element $window 'Image ready. Click Analyze to inspect local inconsistencies.' $null } 'image decode')
Write-Output 'Image loaded through the native picker.'
Invoke-Button $window 'Analyze'
if ($CloseDuringAnalysis) {
    & (Join-Path $PSScriptRoot 'Close-Desktop.ps1') -ProcessId $ProcessId
    exit 0
}
[void](Wait-For {
    $save = Find-Element $window 'Save Result' ([System.Windows.Automation.ControlType]::Button)
    if ($save -and $save.Current.IsEnabled) { return $save }
} 'analysis and heatmap rendering')
Write-Output 'All five analyses completed; Save Result enabled.'
$methodBox = Find-Element $window 'Analysis method' ([System.Windows.Automation.ControlType]::ComboBox)
foreach ($method in @('Noise consistency', 'Frequency analysis', 'Compression artifacts', 'Texture & edges', 'Error level analysis', 'Combined heatmap')) {
    $methodBox.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $item = Wait-For { Find-Element $window $method ([System.Windows.Automation.ControlType]::ListItem) } "method item $method"
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $methodBox.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
    if ([IO.Path]::GetExtension($InputImage) -notmatch '^\.jpe?g$' -and $method -in @('Compression artifacts', 'Error level analysis')) {
        [void](Wait-For { Find-Element $window 'JPEG input required' $null } 'non-JPEG method qualification')
        continue
    }
    [void](Wait-For {
        $save = Find-Element $window 'Save Result' ([System.Windows.Automation.ControlType]::Button)
        if ($save -and $save.Current.IsEnabled) { return $save }
    } "render $method")
}
Write-Output 'Switched through every individual map and back to the combined map.'
Invoke-Button $window '1:1'
Invoke-Button $window 'Zoom in'
Invoke-Button $window 'Fit'
$slider = Find-Element $window 'Heatmap opacity' ([System.Windows.Automation.ControlType]::Slider)
$slider.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue(0.72)
$sensitivity = Find-Element $window 'Heatmap sensitivity' ([System.Windows.Automation.ControlType]::Slider)
$sensitivity.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue(1.4)
Write-Output 'Fit, actual size, zoom, opacity and sensitivity exercised.'
Start-Sleep -Milliseconds 750
Invoke-Button $window 'Save Result'
$saveDialog = Wait-For {
    Find-Element $window 'Save forensic visualization' ([System.Windows.Automation.ControlType]::Window)
} 'save file dialog'
[PixelTraceFileDialog]::Choose([IntPtr]$saveDialog.Current.NativeWindowHandle, $OutputImage)
[void](Wait-For { Test-Path -LiteralPath $OutputImage } 'PNG export')
Write-Output "Saved through native picker: $OutputImage"
Verify-Export
if ($CloseAtEnd) { & (Join-Path $PSScriptRoot 'Close-Desktop.ps1') -ProcessId $ProcessId }
