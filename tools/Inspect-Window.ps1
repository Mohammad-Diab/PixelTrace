param([int]$ProcessId, [string]$CapturePath)
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class PixelTraceWindow {
    public delegate bool EnumProc(IntPtr hwnd, IntPtr lparam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr lparam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint process);
    [DllImport("user32.dll")] public static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    public struct RECT { public int Left, Top, Right, Bottom; }
}
'@
$script:handles = @()
[PixelTraceWindow]::EnumWindows({ param($handle, $unused)
    [uint32]$windowProcess = 0
    [void][PixelTraceWindow]::GetWindowThreadProcessId($handle, [ref]$windowProcess)
    if ($windowProcess -eq $ProcessId) {
        $title = New-Object System.Text.StringBuilder 512
        [void][PixelTraceWindow]::GetWindowText($handle, $title, 512)
        $script:handles += [pscustomobject]@{Handle=$handle; Title=$title.ToString(); Visible=[PixelTraceWindow]::IsWindowVisible($handle)}
    }
    return $true
}, [IntPtr]::Zero) | Out-Null
$script:handles | Format-Table
$target = $script:handles | Where-Object { $_.Title -like 'PixelTrace*' } | Select-Object -First 1
if ($null -ne $target) {
    $element = [System.Windows.Automation.AutomationElement]::FromHandle($target.Handle)
    $all = $element.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($item in $all) { if ($item.Current.Name) { "{0}: {1}" -f $item.Current.ControlType.ProgrammaticName, $item.Current.Name } }
    if ($CapturePath) {
        $rect = New-Object PixelTraceWindow+RECT
        [void][PixelTraceWindow]::GetWindowRect($target.Handle, [ref]$rect)
        $bitmap = New-Object System.Drawing.Bitmap ($rect.Right - $rect.Left), ($rect.Bottom - $rect.Top)
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        $hdc = $graphics.GetHdc()
        try { [void][PixelTraceWindow]::PrintWindow($target.Handle, $hdc, 2) } finally { $graphics.ReleaseHdc($hdc); $graphics.Dispose() }
        try { $bitmap.Save($CapturePath, [Drawing.Imaging.ImageFormat]::Png) } finally { $bitmap.Dispose() }
    }
}
