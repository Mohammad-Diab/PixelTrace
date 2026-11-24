# PixelTrace

An offline .NET 10 / Avalonia 12 desktop workbench for local image-forensics experiments. Uses **Insiyab.Avalonia 0.3.2** and OpenCvSharp 4.13. No models, Python, services, accounts or telemetry in the application.

## Run on Windows x64

Download the portable Windows build from [PixelTrace v1.0.0](https://github.com/Mohammad-Diab/PixelTrace/releases/tag/v1.0.0), extract it completely, and run `PixelTrace.exe`. Keep the extracted files together. The .NET 10 runtime code is built into the executable through Native AOT; no .NET installation is required.

| Download | Extracted size | Download size |
| --- | ---: | ---: |
| [Windows x64 ZIP](https://github.com/Mohammad-Diab/PixelTrace/releases/download/v1.0.0/PixelTrace-win-x64-v1.0.0.zip) | about 46 MiB | about 19 MiB |
| [Windows x64 7z](https://github.com/Mohammad-Diab/PixelTrace/releases/download/v1.0.0/PixelTrace-win-x64-v1.0.0.7z) | about 46 MiB | about 13 MiB |

Both archives contain the same standalone application: .NET 10, Avalonia 12.1.3, Insiyab.Avalonia 0.3.2 and OpenCvSharp 4.13.0.20260627. OpenCV 4.13.0 is built from pinned upstream sources with only `core`, `imgproc` and `imgcodecs`. JPEG, PNG, BMP, TIFF and WebP are supported. Video/ML modules, FFmpeg, PDBs, unused CPU-dispatch variants and IPP/OpenCL acceleration are not shipped. The image-only native library uses a static C runtime and has no Media Foundation dependency. Disabling optional acceleration prioritizes package size; performance can vary by image and hardware.

For local builds, the executable is `artifacts/win-x64-v1.0.0/PixelTrace.exe`. The release assets include SHA-256 checksums. ZIP and 7z refer to archive formats, not different application variants.

1. **Open image** (Ctrl+O) opens an Insiyab dialog. Pick/drop/paste an image using the library's FileUpload, choose the method and block/ELA settings, then **Analyze**. Cancel preserves the current image and results.
2. The result appears over one image. **Show original**, beside the filename, shows the source while held; release to restore the overlay.
3. **Reanalyze** (F5) opens the same dialog with the current file locked. It reuses the retained pixels. A single method runs independently; **All signals** runs all applicable methods. Switch already-computed results instantly from the side panel.
4. The side panel holds sensitivity, opacity and zoom. Endpoint labels explain stronger/subtler differences and original/overlay visibility. **Default** resets only sensitivity (1×) and opacity (60%).
5. **Save result** (Ctrl+S) appears at the end of the toolbar after analysis starts and enables when the result is ready. It exports full-resolution PNG without overwriting the source.

The language pill switches English/Arabic. English controls sit on the right; Arabic controls sit on the left, while image pixels remain unmirrored. Insiyab's **DayNight** control provides the animated light/dark switch. The compact brand pill matches the native caption-pill height. Scroll to zoom; drag to pan. With the viewer focused, 0 fits, 1 shows actual size, +/- zoom and arrow keys pan. Holding the original preserves pan/zoom and cached analysis; Space/Enter works with the comparison button focused.

The **Color guide** stays visible below the image: purple means lower local differences, teal moderate differences, and yellow higher differences. Hovering the image marks the inspected block on that scale using the current sensitivity. Masked regions remain unchanged; intensity is not an AI probability or a verdict on authenticity.

The bilingual **Analysis summary** below the image explains whether there are regions to review, no prominent differences, or limited usable information. It lists up to three regions and the checks that responded within each one. Selecting a region frames and zooms to it and displays its strongest responding method. Show original hides the frame while held; Clear selection removes it. The summary also discloses single-method scope and unavailable JPEG checks. Lighting, focus and compression can produce differences; neither a response nor its absence certifies authenticity.

The summary is computed once from the independent maps, excluding Combined and unavailable checks. Valid blocks with normalized response at least 0.65 are grouped by eight-neighbor connectivity. Groups are ranked by their peak validity-weighted combined response; ties use the number of checks responding in the same block, block count and individual peak response. These checks may be correlated. Region bounds follow the displayed EXIF orientation. Fewer than four usable blocks, under one-quarter usable pixel coverage, or a side below 64px produces the limited-information qualification. These are fixed navigation heuristics, not calibrated detection thresholds. Sensitivity, opacity and result selection do not change the report. PNG export remains the analysis overlay without the selection frame or summary text.

## Build and verify

Requires a .NET SDK that targets .NET 10. The first restore downloads dependencies; running the application is entirely offline. Ordinary development builds use the official OpenCvSharp Slim runtime at the same pinned version.

```powershell
dotnet restore PixelTrace.slnx
dotnet build PixelTrace.slnx -c Release --no-restore
dotnet test PixelTrace.slnx -c Release
dotnet run --project src/PixelTrace.Desktop
```

Build the compact distribution with Visual Studio C++ build tools and CMake installed:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Publish-Windows.ps1
```

The script verifies the upstream source archive hashes, builds the image-only OpenCV runtime, tests against that exact DLL and publishes to an empty directory using the `WindowsNative` profile. It produces a ZIP and SHA-256 file; when 7-Zip is installed, it uses maximum standard Deflate for ZIP and also produces a smaller 7z archive with its checksum. Native build intermediates are cached under the system temporary directory. Use `-SkipNativeBuild` to reuse the verified native output. `-SkipTests` is available for packaging an already-tested commit. `-OutputDirectory` selects a fresh destination. A framework-dependent profile remains available for development, but the published release is the standalone Native AOT build.

Release notes, documentation and code comments are written in English. Arabic text is retained for the application's localization and the tests that exercise it or Unicode filenames.

The source files tracked by Git occupy about 230 KiB. `bin`, `obj`, local build experiments and verification evidence are generated files excluded from Git and from the downloadable application packages.

Generate deterministic signal fixtures and all six exported maps:

```powershell
dotnet run --project tools/PixelTrace.Verify -- artifacts/verification/samples
```

`tools/Smoke-Desktop.ps1` exercises the actual Windows app through UI Automation and native file dialogs; pass its process ID, an absolute input path and a new absolute PNG output path. `tools/Inspect-Window.ps1` captures only the application's window. These scripts require an interactive Windows desktop. See [VERIFICATION.md](VERIFICATION.md) for the checks performed on this build.

If an unexpected failure occurs, its managed exception and stack are recorded locally in `%LOCALAPPDATA%/PixelTrace/Logs/crash.log`. No diagnostics are uploaded. `tools/Close-Desktop.ps1` verifies normal window closure and a zero process exit code; the smoke script accepts `-CloseAtEnd` or `-CloseDuringAnalysis` for lifecycle checks.

## Signals and interpretation

| Method | Local measurement |
| --- | --- |
| Noise | Robust high-pass residual magnitude away from strong gradients/clipped pixels, adjusted for luminance; near-zero noise loses validity. |
| Frequency | DCT high-to-mid frequency energy. Low contrast and coherent edges lose validity. |
| Compression | JPEG 8px boundary-to-interior discontinuity ratios; strong edges are suppressed. An IJG luminance-table match estimates last-save quality when possible. |
| Texture & edges | Relationships between contrast, gradient energy, edge density and directional coherence. |
| ELA | JPEG recompression residual at the selected quality, adjusted for texture and compared regionally. |

Each method compares nearby regions with compatible texture and luminance, supplemented by robust texture/luminance peer statistics. Median/MAD scales and absolute floors prevent tiny numerical differences from being amplified. Raw anomaly scores, normalized maps and validity masks are separate from rendering. Compatible normalized maps are combined with configurable validity-weighted contributions; ELA has a low default weight. Bilinear interpolation uses actual block centers, including partial edge blocks. Masked regions retain the source.

**Intensity is a relative statistical response, not a probability of manipulation or AI generation.** Lighting, materials, focus, denoising, natural edges, resampling, JPEG subsampling and earlier saves can cause responses. Uniformly processed or large context-matched edits may leave little signal. ELA is a heuristic. The program does not reconstruct compression history or certify authenticity.

## Image handling and limits

- JPEG, PNG, BMP, classic TIFF (first page) and WebP through OpenCV. Compression and ELA are explicitly unavailable for non-JPEG inputs.
- JPEG/TIFF EXIF orientation is applied consistently to display and export. Analysis runs on the encoded raster to preserve the JPEG block grid. PNG/WebP orientation metadata and animated frames are not supported.
- 16-bit integer input is mapped to 8-bit. Transparency is composited on neutral gray and disclosed. Export contains decoded RGB pixels; original ICC profiles and metadata are not retained.
- No analysis downsampling: exported dimensions equal the oriented original. Default blocks are 32px; 16/32/64/128 are selectable. The effective size increases if needed to cap the grid at 65,536 cells. Partial blocks smaller than 8px are masked.
- Files above 256 MB, images above 40 megapixels, or a side above 32,000px are rejected before native decoding. A large image still needs several hundred MB of memory for full-resolution originals, results and native intermediates.
- Expensive work runs off the UI thread; independent tile rows run in bounded parallel loops. Cancellation is checked between regions/stages. A native JPEG/PNG codec call must finish before cancellation can take effect. Export writes a temporary file and replaces the destination only after successful completion.

## License

PixelTrace is licensed under the [MIT License](LICENSE), copyright (c) 2026 Mohammad Diab. Third-party components retain their own licenses; see [Third-party notices](THIRD-PARTY-NOTICES.md).

## Source layout

`src/PixelTrace.Core` owns loading, independent signal calculations, normalization, combination and export. `src/PixelTrace.Desktop` contains view models, the Insiyab window, the inline image canvas and its color legend. `tests/PixelTrace.Tests` contains focused engine tests; `tests/PixelTrace.Desktop.Tests` covers UI lifetimes, rendering, momentary comparison and navigation. Windows is the tested runtime target. Core and UI use portable APIs; a Linux/macOS build needs the corresponding OpenCvSharp native runtime package and platform validation.

The UI follows the [Insiyab documentation](https://github.com/Mohammad-Diab/insiyab-ui/tree/avalonia-v0.3.2/packages/avalonia) and [gallery examples](https://github.com/Mohammad-Diab/insiyab-ui/tree/avalonia-v0.3.2/packages/avalonia/gallery/Insiyab.Avalonia.Gallery). It uses the official theme, toolbar, buttons, fields, progress, surfaces, typography and icons. Insiyab's supplied picture viewer is modal and its zoom canvas is internal; PixelTrace's small inline canvas provides single-image inspection. The library is consumed from NuGet and is unmodified.
