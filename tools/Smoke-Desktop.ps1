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
if (!(Find-Element $window 'Analyze image' $null)) { Invoke-Button $window 'Open image' }
[void](Wait-For { Find-Element $window 'Analyze image' $null } 'analysis setup')
$fileField = Find-Element $window 'Choose an image' $null
if ($fileField) { $fileField.SetFocus() }
[void][PixelTraceFileDialog]::PostMessage([IntPtr]$window.Current.NativeWindowHandle, 0x100, [IntPtr]13, [IntPtr]0)
[void][PixelTraceFileDialog]::PostMessage([IntPtr]$window.Current.NativeWindowHandle, 0x101, [IntPtr]13, [IntPtr]0)
$picker = Wait-For { Find-Element $window 'Open' ([System.Windows.Automation.ControlType]::Window) } 'native file picker'
[PixelTraceFileDialog]::Choose([IntPtr]$picker.Current.NativeWindowHandle, $InputImage)
[void](Wait-For {
    $analyze = Find-Element $window 'Analyze' ([System.Windows.Automation.ControlType]::Button)
    if ($analyze -and $analyze.Current.IsEnabled) { return $analyze }
} 'selected file')
Write-Output 'File selected through Insiyab FileUpload.'
Invoke-Button $window 'Analyze'
if ($CloseDuringAnalysis) {
    & (Join-Path $PSScriptRoot 'Close-Desktop.ps1') -ProcessId $ProcessId
    exit 0
}
[void](Wait-For {
    $save = Find-Element $window 'Save result' ([System.Windows.Automation.ControlType]::Button)
    if ($save -and $save.Current.IsEnabled) { return $save }
} 'analysis and visualization')
Write-Output 'Analysis completed; trailing Save result enabled.'
$resultBox = Find-Element $window 'Result' ([System.Windows.Automation.ControlType]::ComboBox)
$maps = @('Noise','Frequency','Texture & edges','Combined')
if ([IO.Path]::GetExtension($InputImage) -match '^\.jpe?g$') { $maps = @('Noise','Frequency','Compression','Texture & edges','ELA','Combined') }
foreach ($map in $maps) {
    $resultBox.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $item = Wait-For { Find-Element $window $map ([System.Windows.Automation.ControlType]::ListItem) } "result $map"
    $item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    $resultBox.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
    [void](Wait-For {
        $save = Find-Element $window 'Save result' ([System.Windows.Automation.ControlType]::Button)
        if ($save -and $save.Current.IsEnabled) { return $save }
    } "render $map")
}
Invoke-Button $window 'Reanalyze'
[void](Wait-For { Find-Element $window 'Current image (locked)' $null } 'locked reanalysis source')
$lockedFile = Find-Element $window 'Choose an image' $null
if ($lockedFile -and $lockedFile.Current.IsEnabled) { throw 'Reanalysis must lock the input image.' }
Invoke-Button $window 'Cancel'
[void](Wait-For {
    $save = Find-Element $window 'Save result' ([System.Windows.Automation.ControlType]::Button)
    if ($save -and $save.Current.IsEnabled) { return $save }
} 'cancelled setup retains result')
Write-Output 'Result switching and locked-source reanalysis/cancel verified.'
Invoke-Button $window '1:1'
Invoke-Button $window 'Zoom in'
Invoke-Button $window 'Fit'
$opacity = Find-Element $window 'Opacity' ([System.Windows.Automation.ControlType]::Slider)
$sensitivity = Find-Element $window 'Sensitivity' ([System.Windows.Automation.ControlType]::Slider)
$opacity.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue(0.72)
$sensitivity.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).SetValue(1.4)
Invoke-Button $window 'Default'
if ([Math]::Abs($opacity.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).Current.Value - 0.6) -gt 0.001) { throw 'Default opacity failed' }
if ([Math]::Abs($sensitivity.GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern).Current.Value - 1) -gt 0.001) { throw 'Default sensitivity failed' }
Start-Sleep -Milliseconds 500
Invoke-Button $window 'Save result'
$saveDialog = Wait-For { Find-Element $window 'Save result' ([System.Windows.Automation.ControlType]::Window) } 'save picker'
[PixelTraceFileDialog]::Choose([IntPtr]$saveDialog.Current.NativeWindowHandle, $OutputImage)
[void](Wait-For { Test-Path -LiteralPath $OutputImage } 'PNG export')
Verify-Export
if ($CloseAtEnd) { & (Join-Path $PSScriptRoot 'Close-Desktop.ps1') -ProcessId $ProcessId }
