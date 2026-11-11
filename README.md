# PixelTrace

An offline .NET 10 / Avalonia 12 desktop workbench for local image-forensics experiments. Uses **Insiyab.Avalonia 0.3.2** and OpenCvSharp 4.13. No models, Python, services, accounts or telemetry in the application.

## Run on Windows x64

Open **`artifacts/win-x64-v1.2.0/PixelTrace.exe`**. Keep the entire publish folder together. The publish is self-contained; a .NET installation is not required. The full OpenCV runtime uses the Windows Media Foundation OS feature; Windows N/Server installations must have that feature enabled.

1. **Open image** (Ctrl+O) opens an Insiyab dialog. Pick/drop/paste an image using the library's FileUpload, choose the method and block/ELA settings, then **Analyze**. Cancel preserves the current image and results.
2. The result appears over one image. **Show original**, beside the filename, shows the source while held; release to restore the overlay.
3. **Reanalyze** (F5) opens the same dialog with the current file locked. It reuses the retained pixels. A single method runs independently; **All signals** runs all applicable methods. Switch already-computed results instantly from the side panel.
4. The side panel holds sensitivity, opacity and zoom. Endpoint labels explain stronger/subtler differences and original/overlay visibility. **Default** resets only sensitivity (1×) and opacity (60%).
5. **Save result** (Ctrl+S) appears at the end of the toolbar after analysis starts and enables when the result is ready. It exports full-resolution PNG without overwriting the source.

The language pill switches English/Arabic. English controls sit on the right; Arabic controls sit on the left, while image pixels remain unmirrored. Insiyab's **DayNight** control provides the animated light/dark switch. The compact brand pill matches the native caption-pill height. Scroll to zoom; drag to pan. With the viewer focused, 0 fits, 1 shows actual size, +/- zoom and arrow keys pan. Holding the original preserves pan/zoom and cached analysis; Space/Enter works with the comparison button focused.

The **Color guide** stays visible below the image: purple means lower local differences, teal moderate differences, and yellow higher differences. Hovering the image marks the inspected block on that scale using the current sensitivity. Masked regions remain unchanged; intensity is not an AI probability or a verdict on authenticity.

## Build and verify

Requires the .NET 10 SDK. The first restore downloads dependencies; running the built application is entirely offline.

```powershell
dotnet restore PixelTrace.slnx
dotnet build PixelTrace.slnx -c Release --no-restore
dotnet test PixelTrace.slnx -c Release
dotnet publish src/PixelTrace.Desktop/PixelTrace.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishReadyToRun=true -o artifacts/win-x64-v1.2.0
dotnet run --project src/PixelTrace.Desktop
```

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

## Source layout

`src/PixelTrace.Core` owns loading, independent signal calculations, normalization, combination and export. `src/PixelTrace.Desktop` contains view models, the Insiyab window, the inline image canvas and its color legend. `tests/PixelTrace.Tests` contains focused engine tests; `tests/PixelTrace.Desktop.Tests` covers UI lifetimes, rendering, momentary comparison and navigation. Windows is the tested runtime target. Core and UI use portable APIs; a Linux/macOS build needs the corresponding OpenCvSharp native runtime package and platform validation.

The UI follows the [Insiyab documentation](https://github.com/Mohammad-Diab/insiyab-ui/tree/avalonia-v0.3.2/packages/avalonia) and [gallery examples](https://github.com/Mohammad-Diab/insiyab-ui/tree/avalonia-v0.3.2/packages/avalonia/gallery/Insiyab.Avalonia.Gallery). It uses the official theme, toolbar, buttons, fields, progress, surfaces, typography and icons. Insiyab's supplied picture viewer is modal and its zoom canvas is internal; PixelTrace's small inline canvas provides single-image inspection. The library is consumed from NuGet and is unmodified.
