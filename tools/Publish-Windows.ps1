param([string]$OutputDirectory = 'artifacts/win-x64-v1.3.0')
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $projectRoot
try {
    dotnet restore PixelTrace.slnx
    if ($LASTEXITCODE) { throw 'Restore failed' }
    dotnet build PixelTrace.slnx -c Release --no-restore
    if ($LASTEXITCODE) { throw 'Build failed' }
    dotnet test PixelTrace.slnx -c Release --no-build --no-restore
    if ($LASTEXITCODE) { throw 'Tests failed' }
    dotnet publish src/PixelTrace.Desktop/PixelTrace.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o $OutputDirectory
    if ($LASTEXITCODE) { throw 'Publish failed' }
    foreach ($name in @('PixelTrace.exe', 'OpenCvSharpExtern.dll', 'OpenCvSharp.dll', 'opencv_videoio_ffmpeg4130_64.dll', 'coreclr.dll', 'hostfxr.dll')) {
        $path = Join-Path $OutputDirectory $name
        if (!(Test-Path -LiteralPath $path)) { throw "Missing publish dependency: $name" }
    }
    Copy-Item -LiteralPath 'README.md', 'VERIFICATION.md', 'THIRD-PARTY-NOTICES.md' -Destination $OutputDirectory
    Write-Output "Windows x64 publish is ready: $OutputDirectory/PixelTrace.exe"
} finally { Pop-Location }
