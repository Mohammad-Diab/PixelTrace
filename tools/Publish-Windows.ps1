param(
    [string]$OutputDirectory = '',
    [ValidateSet('Portable','FrameworkDependent')][string]$Mode = 'Portable',
    [switch]$SkipNativeBuild,
    [switch]$SkipTests
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Push-Location $projectRoot
try {
    [xml]$project = Get-Content -LiteralPath 'src/PixelTrace.Desktop/PixelTrace.Desktop.csproj'
    $version = $project.Project.PropertyGroup.Version
    $sevenZipCommand = Get-Command 7z -ErrorAction SilentlyContinue | Select-Object -First 1
    $sevenZip = if ($sevenZipCommand) { $sevenZipCommand.Source } else { Join-Path $env:ProgramFiles '7-Zip/7z.exe' }
    if (!(Test-Path -LiteralPath $sevenZip)) { throw 'Install 7-Zip to create the release archive.' }
    $suffix = if ($Mode -eq 'FrameworkDependent') { '-framework-dependent' } else { '' }
    if (!$OutputDirectory) { $OutputDirectory = "artifacts/win-x64-v$version$suffix" }
    $output = [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
    if ((Test-Path -LiteralPath $output) -and (Get-ChildItem -LiteralPath $output -Force | Select-Object -First 1)) {
        throw 'Use an empty output directory to avoid carrying old libraries into the package.'
    }
    $nativeDirectory = Join-Path $projectRoot 'artifacts/native/win-x64'
    $native = Join-Path $nativeDirectory 'OpenCvSharpExtern.dll'
    if (!$SkipNativeBuild) { & (Join-Path $PSScriptRoot 'Build-NativeOpenCv.ps1') }
    if (!(Test-Path -LiteralPath $native)) { throw 'Build the image-only runtime with tools/Build-NativeOpenCv.ps1 first.' }
    $nativeMetadata = Get-Content -LiteralPath (Join-Path $nativeDirectory 'native-build.json') -Raw | ConvertFrom-Json
    if ((Get-FileHash -LiteralPath $native).Hash -ne $nativeMetadata.SHA256) { throw 'The native library does not match its build manifest.' }
    $nativeProperty = "-p:PixelTraceNativeRuntime=$native"
    if (!$SkipTests) {
        dotnet test PixelTrace.slnx -c Release -r win-x64 $nativeProperty
        if ($LASTEXITCODE) { throw 'Tests failed.' }
    }
    $profile = if ($Mode -eq 'FrameworkDependent') { 'WindowsFrameworkDependent' } else { 'WindowsNative' }
    dotnet publish src/PixelTrace.Desktop/PixelTrace.Desktop.csproj "-p:PublishProfile=$profile" $nativeProperty -o $output
    if ($LASTEXITCODE) { throw 'Publish failed.' }
    foreach ($name in @('PixelTrace.exe','OpenCvSharpExtern.dll','libSkiaSharp.dll','libHarfBuzzSharp.dll','av_libglesv2.dll','LICENSE')) {
        if (!(Test-Path -LiteralPath (Join-Path $output $name))) { throw "Missing publish dependency: $name" }
    }
    if (Get-ChildItem -LiteralPath $output -Recurse -File -Filter '*.pdb' | Select-Object -First 1) { throw 'Debug symbols must not be distributed.' }
    if (Get-ChildItem -LiteralPath $output -Recurse -File -Filter '*ffmpeg*' | Select-Object -First 1) { throw 'An unused video library was included.' }
    if ($Mode -eq 'Portable' -and (Test-Path -LiteralPath (Join-Path $output 'coreclr.dll'))) { throw 'The portable package must use Native AOT.' }
    Copy-Item -LiteralPath 'README.md','RELEASE_NOTES.md','VERIFICATION.md','THIRD-PARTY-NOTICES.md' -Destination $output
    if ($Mode -eq 'Portable') {
        $startHere = "PixelTrace $version - Windows x64 portable`n`nRun PixelTrace.exe. The .NET 10 runtime code is included through Native AOT.`nNo .NET installation is required. Keep all extracted files together.`n`nLicense notices are under licenses.`nAnalysis findings are review cues, not an authenticity verdict.`n"
        [IO.File]::WriteAllText((Join-Path $output 'START_HERE.txt'),$startHere,(New-Object Text.UTF8Encoding($false)))
    }
    $licenses = Join-Path $output 'licenses'
    New-Item -ItemType Directory -Path $licenses -Force | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $nativeDirectory 'licenses') | Copy-Item -Destination $licenses -Recurse
    $assets = Get-Content -LiteralPath 'src/PixelTrace.Desktop/obj/project.assets.json' -Raw | ConvertFrom-Json
    $nugetRoot = $assets.packageFolders.PSObject.Properties.Name | Select-Object -First 1
    foreach ($library in $assets.libraries.PSObject.Properties) {
        if ($library.Value.type -ne 'package') { continue }
        $package = Join-Path $nugetRoot $library.Value.path
        $notices = @(Get-ChildItem -LiteralPath $package -File | Where-Object { $_.Name -match '(?i)license|notice|copyright' })
        if (!$notices.Count) { continue }
        $packageDestination = Join-Path $licenses ($library.Name.Replace('/','-'))
        New-Item -ItemType Directory -Path $packageDestination -Force | Out-Null
        $notices | Copy-Item -Destination $packageDestination
    }
    Copy-Item -LiteralPath (Join-Path $nugetRoot 'insiyab.avalonia/0.3.2/fonts') -Destination (Join-Path $licenses 'fonts') -Recurse
    if ($Mode -eq 'Portable') {
        $runtime = Get-ChildItem -LiteralPath (Join-Path $nugetRoot 'microsoft.netcore.app.runtime.win-x64') -Directory |
            Where-Object { $_.Name -match '^10\.0\.\d+$' } | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
        Copy-Item -LiteralPath (Join-Path $runtime.FullName 'LICENSE.TXT') -Destination (Join-Path $licenses 'DOTNET-LICENSE.txt')
        Copy-Item -LiteralPath (Join-Path $runtime.FullName 'THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $licenses 'DOTNET-THIRD-PARTY-NOTICES.txt')
    }
    $nativeMetadata | Select-Object OpenCV,OpenCvSharpCommit,SourceArchives,Modules,Codecs,IPP,OpenCL,CpuDispatch,StaticCRT,SHA256,Bytes |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'native-build.json') -Encoding UTF8
    $archiveBase = Join-Path $projectRoot "artifacts/PixelTrace-win-x64-v$version$suffix"
    $archive = "$archiveBase.7z"
    & $sevenZip a -t7z -mx=9 -m0=LZMA2 -md=64m -mmt=2 -bsp0 -bso0 $archive $output
    if ($LASTEXITCODE) { throw '7z compression failed.' }
    & $sevenZip t -bsp0 -bso0 $archive
    if ($LASTEXITCODE) { throw 'Archive verification failed.' }
    $hash = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksum = "$hash  $([IO.Path]::GetFileName($archive))`n"
    [IO.File]::WriteAllText("$archive.sha256",$checksum,(New-Object Text.UTF8Encoding($false)))
    [IO.File]::WriteAllText("$archiveBase-SHA256SUMS.txt",$checksum,(New-Object Text.UTF8Encoding($false)))
    $files = Get-ChildItem -LiteralPath $output -Recurse -File
    [pscustomobject]@{ Mode=$Mode; Folder=$output; Files=$files.Count; Bytes=($files | Measure-Object Length -Sum).Sum; ArchiveBytes=(Get-Item -LiteralPath $archive).Length; SHA256=$hash } |
        ConvertTo-Json | Set-Content -LiteralPath "$archive.size.json" -Encoding UTF8
    Write-Output "Windows x64 package ready: $archive"
} finally { Pop-Location }
