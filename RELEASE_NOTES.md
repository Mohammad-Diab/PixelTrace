# PixelTrace v1.3.1

Smaller Windows packages with the same .NET 10, Avalonia 12, Insiyab 0.3.2 and OpenCvSharp/OpenCV 4.13 stack.

| Download | Extracted | ZIP | Requirement |
| --- | ---: | ---: | --- |
| `PixelTrace-win-x64-v1.3.1.zip` | about 51 MiB | about 20 MiB | No .NET installation |
| `PixelTrace-win-x64-v1.3.1-framework-dependent.zip` | about 38 MiB | about 15 MiB | .NET 10 x64 Runtime |

- Removed about 100 MiB of vendor debug symbols from distributions.
- Built a 5.54 MiB image-only OpenCV runtime from pinned, hash-verified upstream sources. JPEG, PNG, BMP, TIFF, WebP and all five analysis methods remain available.
- Removed unused video, ML, FFmpeg, IPP/OpenCL acceleration and extra CPU-dispatch variants. Size-oriented compilation can affect performance on some hardware.
- Added a Native AOT portable build and a smaller framework-dependent alternative; neither ships ReadyToRun assemblies.
- Kept Arabic/English UI support, themes, analysis summaries and full-resolution PNG export. Documentation, comments and release notes are English.
- Included MIT, font and third-party notices and ZIP SHA-256 checksums.

Extract the selected ZIP completely and run `PixelTrace.exe` inside its folder. Keep all files together. The framework-dependent package requires the [.NET 10 x64 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0/runtime).

All 59 automated tests passed against the reduced native runtime. Native Windows checks exercised analysis, result selection, settings, PNG export, themes/language and normal shutdown. Analysis findings are review cues, not proof of editing or an authenticity verdict.
