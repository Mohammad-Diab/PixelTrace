param(
    [string]$OutputDirectory = 'artifacts/native/win-x64',
    [string]$BuildDirectory = (Join-Path $env:TEMP 'PixelTrace-native-4.13.0'),
    [int]$Parallelism = 6
)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$output = [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (!(Test-Path -LiteralPath $vswhere)) { throw 'Install Visual Studio C++ build tools and CMake.' }
$vs = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (!$vs) { throw 'The Visual Studio C++ toolchain is required.' }
$cmake = Join-Path $vs 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
if (!(Test-Path -LiteralPath $cmake)) { $cmake = (Get-Command cmake -ErrorAction Stop).Source }
$generator = if ((& $vswhere -latest -products '*' -property installationVersion) -like '18.*') { 'Visual Studio 18 2026' } else { 'Visual Studio 17 2022' }
New-Item -ItemType Directory -Path $BuildDirectory,$output -Force | Out-Null
$archives = @(
    @{Name='opencv-4.13.0'; Url='https://codeload.github.com/opencv/opencv/zip/refs/tags/4.13.0'; Hash='431CB489D275DA26526581C6735D7D7C0B00F190FE940B3217D386B77AEF52C7'},
    @{Name='opencvsharp-b161e7e'; Url='https://codeload.github.com/shimat/opencvsharp/zip/b161e7e012f5101f6d5dc68a835c59db6cc88b18'; Hash='3827D4B2EC9FDE0D3DF9120B388AFAEFEC17C7A03C1509B0EF6570D17C3FA4D4'}
)
foreach ($archive in $archives) {
    $zip = Join-Path $BuildDirectory ($archive.Name + '.zip')
    if (!(Test-Path -LiteralPath $zip)) { Invoke-WebRequest -UseBasicParsing -Uri $archive.Url -OutFile $zip }
    if ((Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash -ne $archive.Hash) { throw "Source archive checksum mismatch: $($archive.Name)" }
    $expanded = Join-Path $BuildDirectory $archive.Name
    if (!(Test-Path -LiteralPath $expanded)) { Expand-Archive -LiteralPath $zip -DestinationPath $expanded }
}
$opencv = Join-Path $BuildDirectory 'opencv-4.13.0/opencv-4.13.0'
$wrapper = Join-Path $BuildDirectory 'opencvsharp-b161e7e/opencvsharp-b161e7e012f5101f6d5dc68a835c59db6cc88b18'
$opencvBuild = Join-Path $BuildDirectory 'build-opencv'
$wrapperBuild = Join-Path $BuildDirectory 'build-wrapper'
$install = Join-Path $BuildDirectory 'install'
$configure = @(
    '-S',$opencv,'-B',$opencvBuild,'-G',$generator,'-A','x64',
    "-DCMAKE_INSTALL_PREFIX=$install",'-DBUILD_LIST=core,imgproc,imgcodecs','-DBUILD_SHARED_LIBS=OFF',
    '-DBUILD_WITH_STATIC_CRT=ON','-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded',
    '-DCMAKE_C_FLAGS_RELEASE=/O1 /Ob2 /DNDEBUG /Gy /Gw','-DCMAKE_CXX_FLAGS_RELEASE=/O1 /Ob2 /DNDEBUG /Gy /Gw',
    '-DENABLE_LTO=ON','-DCPU_BASELINE=SSE2','-DCPU_DISPATCH=',
    '-DBUILD_TESTS=OFF','-DBUILD_PERF_TESTS=OFF','-DBUILD_EXAMPLES=OFF','-DBUILD_opencv_apps=OFF',
    '-DBUILD_JAVA=OFF','-DBUILD_opencv_python2=OFF','-DBUILD_opencv_python3=OFF',
    '-DWITH_IPP=OFF','-DWITH_ITT=OFF','-DWITH_OPENCL=OFF','-DWITH_FFMPEG=OFF','-DWITH_MSMF=OFF',
    '-DWITH_DSHOW=OFF','-DWITH_OPENEXR=OFF','-DWITH_OPENJPEG=OFF','-DWITH_JASPER=OFF',
    '-DWITH_AVIF=OFF','-DWITH_GDAL=OFF','-DWITH_GDCM=OFF','-DWITH_LAPACK=OFF','-DWITH_EIGEN=OFF',
    '-DWITH_PROTOBUF=OFF','-DBUILD_PROTOBUF=OFF','-DWITH_ADE=OFF',
    '-DWITH_JPEG=ON','-DWITH_PNG=ON','-DWITH_TIFF=ON','-DWITH_WEBP=ON',
    '-DBUILD_JPEG=ON','-DBUILD_PNG=ON','-DBUILD_TIFF=ON','-DBUILD_WEBP=ON','-DBUILD_ZLIB=ON'
)
& $cmake @configure
if ($LASTEXITCODE) { throw 'OpenCV configuration failed.' }
& $cmake --build $opencvBuild --config Release --target INSTALL --parallel $Parallelism
if ($LASTEXITCODE) { throw 'OpenCV native build failed.' }
$opencvConfig = Get-ChildItem -LiteralPath $install -Filter OpenCVConfig.cmake -Recurse | Where-Object { $_.DirectoryName -match 'staticlib' } | Select-Object -First 1
if (!$opencvConfig) { throw 'Static OpenCV configuration was not generated.' }
$wrapperConfigure = @('-S',(Join-Path $wrapper 'src'),'-B',$wrapperBuild,'-G',$generator,'-A','x64',
    "-DOpenCV_DIR=$($opencvConfig.DirectoryName)",'-DCMAKE_POLICY_DEFAULT_CMP0091=NEW',
    '-DCMAKE_CXX_FLAGS_RELEASE=/O1 /Ob2 /DNDEBUG /Gy /Gw /GL','-DCMAKE_SHARED_LINKER_FLAGS_RELEASE=/LTCG /OPT:REF /OPT:ICF /INCREMENTAL:NO')
foreach ($feature in @('CONTRIB','STITCHING','CALIB3D','VIDEO','FEATURES2D','FLANN','DNN','ML','OBJDETECT','PHOTO','BARCODE','HIGHGUI','VIDEOIO','INSTALL_TO_TEST')) {
    $wrapperConfigure += "-DNO_$feature=ON"
}
& $cmake @wrapperConfigure
if ($LASTEXITCODE) { throw 'OpenCvSharp wrapper configuration failed.' }
& $cmake --build $wrapperBuild --config Release --parallel $Parallelism
if ($LASTEXITCODE) { throw 'OpenCvSharp wrapper build failed.' }
$native = Get-ChildItem -LiteralPath $wrapperBuild -Filter OpenCvSharpExtern.dll -Recurse | Select-Object -First 1
if (!$native) { throw 'Native library was not generated.' }
Copy-Item -LiteralPath $native.FullName -Destination (Join-Path $output 'OpenCvSharpExtern.dll')
$licenses = Join-Path $output 'licenses'
New-Item -ItemType Directory -Path $licenses -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $opencv 'LICENSE') -Destination (Join-Path $licenses 'OpenCV-LICENSE.txt')
Copy-Item -LiteralPath (Join-Path $wrapper 'LICENSE') -Destination (Join-Path $licenses 'OpenCvSharp-LICENSE.txt')
foreach ($component in @('zlib','libjpeg-turbo','libpng','libtiff','libwebp')) {
    $componentSource = Join-Path $opencv "3rdparty/$component"
    $componentDestination = Join-Path $licenses $component
    New-Item -ItemType Directory -Path $componentDestination -Force | Out-Null
    Get-ChildItem -LiteralPath $componentSource -File | Where-Object { $_.Name -match '(?i)license|copying|copyright|notice|readme' } | Copy-Item -Destination $componentDestination
}
[ordered]@{
    OpenCV='4.13.0'; OpenCvSharpCommit='b161e7e012f5101f6d5dc68a835c59db6cc88b18'
    SourceArchives=$archives; Modules=@('core','imgproc','imgcodecs'); Codecs=@('JPEG','PNG','TIFF','WebP','BMP')
    IPP=$false; OpenCL=$false; CpuDispatch=$false; StaticCRT=$true
    ConfigureArguments=$configure; WrapperArguments=$wrapperConfigure
    SHA256=(Get-FileHash -LiteralPath (Join-Path $output 'OpenCvSharpExtern.dll')).Hash
    Bytes=$native.Length
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $output 'native-build.json') -Encoding UTF8
Write-Output "Image-only OpenCV runtime ready: $output"
