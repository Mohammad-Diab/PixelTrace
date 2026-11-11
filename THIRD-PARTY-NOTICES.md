# Third-party notices

PixelTrace consumes these unmodified packages. Their original licenses and notices apply to their code, assets and bundled native components.

- **Insiyab.Avalonia 0.3.2** — Copyright 2026 Mohammad Diab, Apache-2.0. [Source and license](https://github.com/Mohammad-Diab/insiyab-ui). Folder, search and download geometries in `Assets/Icons.axaml` originate in its gallery. Its embedded fonts carry their respective SIL Open Font Licenses in the NuGet package.
- **Avalonia 12.1.3** and its platform/rendering dependencies — MIT and their respective upstream licenses. [Source and license](https://github.com/AvaloniaUI/Avalonia). The Fluent theme package is an internal dependency of Insiyab; PixelTrace initializes InsiyabTheme as documented.
- **OpenCvSharp 4.13.0.20260627** — Copyright Shinta Mori and contributors, Apache-2.0. [Source and license](https://github.com/shimat/opencvsharp).
- **OpenCV 4.13** and native codecs — Apache-2.0 and third-party codec licenses. [OpenCV license](https://github.com/opencv/opencv/blob/4.x/LICENSE), [third-party components](https://github.com/opencv/opencv/tree/4.x/3rdparty).
- The full Windows OpenCvSharp runtime includes **FFmpeg** (LGPL 2.1+) and other native dependencies. [FFmpeg license and source](https://ffmpeg.org/legal.html). PixelTrace does not invoke video, machine-learning or DNN functionality.
- The self-contained Windows distribution includes the **.NET 10 runtime** — MIT and third-party licenses. The publish folder includes the runtime's `LICENSE.txt` and `ThirdPartyNotices.txt`.

Test-only dependencies: xUnit.net (Apache-2.0) and Microsoft.NET.Test.Sdk (MIT).
