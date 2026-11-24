# PixelTrace v1.0.0

An offline image-forensics workbench for Windows x64, with .NET 10 runtime code included through Native AOT. No .NET installation is required.

| Download | Extracted size | Download size |
| --- | ---: | ---: |
| `PixelTrace-win-x64-v1.0.0.zip` | about 46 MiB | about 19 MiB |
| `PixelTrace-win-x64-v1.0.0.7z` | about 46 MiB | about 13 MiB |

Both archives contain the same standalone application. Extract either archive completely and run `PixelTrace.exe`. Keep all files together. SHA-256 checksum files are provided.

## Features

- Five local analysis methods: noise, frequency, JPEG compression, texture/edges and ELA, plus a combined overlay.
- JPEG, PNG, BMP, TIFF and WebP input, with full-resolution PNG export.
- A single image canvas with zoom/pan, a color guide and press-and-hold **Show original**.
- Analysis summaries with up to three clickable review regions and the checks responding within them.
- Arabic and English interfaces, mirrored control placement and light/dark themes.
- Image selection and settings in one dialog; reanalysis reuses the current image.
- Local processing without accounts, telemetry, services or uploaded images.

## Compact distribution

The stack is .NET 10, Avalonia 12.1.3, Insiyab.Avalonia 0.3.2 and OpenCvSharp/OpenCV 4.13. The 5.54 MiB image-only native runtime is built from pinned, hash-verified upstream sources. Unused video/ML modules, FFmpeg and debug symbols are excluded. Optional IPP/OpenCL acceleration and extra CPU-dispatch variants are disabled to prioritize distribution size; performance can vary by hardware.

The source is MIT licensed. Font and third-party notices are included under `licenses`.

## Validation and interpretation

All 59 automated tests pass against the reduced native runtime. Windows checks cover file selection, analysis, result switching, settings, PNG export, language/theme changes and normal shutdown. The standalone executable does not load an installed CoreCLR runtime.

Analysis findings are review cues, not proof of editing or an authenticity verdict. Lighting, focus and compression can also cause differences; the absence of a response does not prove authenticity. JPEG compression and ELA checks require JPEG input.
