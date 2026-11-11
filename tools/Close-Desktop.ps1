param([int]$ProcessId)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$process = [Diagnostics.Process]::GetProcessById($ProcessId)
$processHandle = $process.Handle
$condition = New-Object System.Windows.Automation.PropertyCondition ([System.Windows.Automation.AutomationElement]::ProcessIdProperty), $ProcessId
$window = $null
$deadline = [DateTime]::UtcNow.AddSeconds(15)
do {
    if ($process.HasExited) { throw "PixelTrace exited before its window was ready: $($process.ExitCode)" }
    $windows = [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $condition)
    foreach ($candidate in $windows) { if ($candidate.Current.Name -like 'PixelTrace*') { $window = $candidate; break } }
    if (!$window) { Start-Sleep -Milliseconds 150 }
} while (!$window -and [DateTime]::UtcNow -lt $deadline)
if (!$window) { throw 'PixelTrace main window was not found.' }
$window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
if (!$process.WaitForExit(15000)) { throw 'PixelTrace did not exit after closing its main window.' }
Write-Output "PixelTrace exit code: $($process.ExitCode)"
if ($process.ExitCode -ne 0) { throw "PixelTrace exited with error $($process.ExitCode)" }
