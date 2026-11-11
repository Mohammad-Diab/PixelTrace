param([string]$PublishDirectory = 'artifacts/win-x64-v1.3.0')
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$evidence = Join-Path $root 'artifacts\verification\crash-fix'
New-Item -ItemType Directory -Path $evidence -Force | Out-Null
$oldLogDirectory = $env:PIXELTRACE_LOG_DIRECTORY
$env:PIXELTRACE_LOG_DIRECTORY = Join-Path $evidence 'diagnostics'
$report = @()
try {
    foreach ($case in @('empty', 'analysis-close', 'full-workflow-close')) {
        $process = Start-Process -FilePath (Join-Path $root "$PublishDirectory\PixelTrace.exe") -WorkingDirectory $root -WindowStyle Hidden -PassThru -RedirectStandardError (Join-Path $evidence "$case-stderr.log") -RedirectStandardOutput (Join-Path $evidence "$case-stdout.log")
        $processHandle = $process.Handle
        Write-Output "Started $case (PID $($process.Id))."
        if ($case -eq 'empty') {
            & (Join-Path $PSScriptRoot 'Close-Desktop.ps1') -ProcessId $process.Id
        } else {
            $smoke = @{
                ProcessId = $process.Id
                InputImage = Join-Path $root 'artifacts\verification\samples\signal-test.jpg'
                OutputImage = Join-Path $evidence ("$case-" + [Guid]::NewGuid().ToString('N') + '.png')
            }
            if ($case -eq 'analysis-close') { $smoke.CloseDuringAnalysis = $true }
            else { $smoke.CloseAtEnd = $true }
            & (Join-Path $PSScriptRoot 'Smoke-Desktop.ps1') @smoke
        }
        if (!$process.WaitForExit(15000) -or $process.ExitCode -ne 0) { throw "$case failed to exit cleanly." }
        $stderr = Get-Content -LiteralPath (Join-Path $evidence "$case-stderr.log") -Raw
        if ($stderr -match 'Unhandled exception') { throw "$case had an unhandled exception." }
        $report += [pscustomobject]@{ Case=$case; ProcessId=$process.Id; ExitCode=$process.ExitCode; UnhandledException=$false }
        $report | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $evidence 'lifecycle.json') -Encoding UTF8
    }
    $crashLog = Join-Path $env:PIXELTRACE_LOG_DIRECTORY 'crash.log'
    if (Test-Path -LiteralPath $crashLog) { throw "A diagnostic error was recorded: $crashLog" }
    Write-Output 'All native lifecycle checks passed; no crash log was produced.'
} finally { $env:PIXELTRACE_LOG_DIRECTORY = $oldLogDirectory }
