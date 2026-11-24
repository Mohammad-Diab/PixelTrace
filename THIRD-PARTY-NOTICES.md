# Third-party notices

The following upstream licenses and notices apply to the libraries, assets and native components used by PixelTrace. Release packages include a `licenses` directory in addition to PixelTrace's own MIT `LICENSE`.

- **Insiyab.Avalonia 0.3.2** — Copyright 2026 Mohammad Diab, Apache-2.0. [Source and license](https://github.com/Mohammad-Diab/insiyab-ui). Folder, search and download geometries in `Assets/Icons.axaml` originate in its gallery. Its embedded fonts carry their respective SIL Open Font Licenses in the NuGet package.
- **Avalonia 12.1.3** and its platform/rendering dependencies — MIT and their respective upstream licenses. [Source and license](https://github.com/AvaloniaUI/Avalonia). The Fluent theme package is an internal dependency of Insiyab; PixelTrace initializes InsiyabTheme as documented.
- **OpenCvSharp 4.13.0.20260627** — Copyright Shinta Mori and contributors, Apache-2.0. [Source and license](https://github.com/shimat/opencvsharp).
- **OpenCV 4.13** and native codecs — Apache-2.0 and third-party codec licenses. [OpenCV license](https://github.com/opencv/opencv/blob/4.x/LICENSE), [third-party components](https://github.com/opencv/opencv/tree/4.x/3rdparty).
- Compact Windows releases compile an image-only **OpenCvSharpExtern** from unmodified OpenCV 4.13.0 and OpenCvSharp commit `b161e7e012f5101f6d5dc68a835c59db6cc88b18`, with size-oriented build options. Only `core`, `imgproc` and `imgcodecs` are enabled. The native library includes JPEG, PNG, TIFF, WebP and zlib codecs; their upstream notices are included under `licenses`. `native-build.json` identifies the sources, configuration and binary hash; `tools/Build-NativeOpenCv.ps1` contains the reproducible build procedure. Ordinary development builds use the official `OpenCvSharp4.runtime.win.slim` package at version 4.13.0.20260627.
- The published Windows build does not bundle FFmpeg, video modules, DNN, IPP or OpenCL.
- The portable distribution compiles the **.NET 10 runtime** into the Native AOT executable — MIT and third-party licenses. Its notices are under `licenses/DOTNET-LICENSE.txt` and `licenses/DOTNET-THIRD-PARTY-NOTICES.txt`. No separately installed .NET runtime is required.

Test-only dependencies: xUnit.net (Apache-2.0) and Microsoft.NET.Test.Sdk (MIT).
