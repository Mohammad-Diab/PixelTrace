# Build verification — 24 November 2026

## 1.3.1 compact Windows packages

The stack remains .NET 10, Avalonia 12.1.3, Insiyab.Avalonia 0.3.2, OpenCvSharp 4.13.0.20260627 and OpenCV 4.13.0. Builds were performed with .NET SDK 11.0.100 targeting .NET 10 and the 10.0.15 Native AOT compiler, using Visual Studio 2026 C++ tools.

| Distribution | Extracted size | ZIP size | .NET installation |
| --- | ---: | ---: | --- |
| Previous 1.3.0 self-contained ReadyToRun | 321.36 MiB | 117.18 MiB | Not required |
| Compact Native AOT portable | about 51 MiB | about 20 MiB | Not required |
| Compact framework-dependent | about 38 MiB | about 15 MiB | .NET 10 x64 Runtime required |

The Git-tracked source was 217 KiB before this change. The old publish included 100 MiB of native vendor PDBs and 93 MiB of OpenCV/FFmpeg binaries. Compact publish profiles exclude PDB assets, including native NuGet symbols that the SDK symbol flag alone did not remove. The Native AOT profile also avoids shipping a separate CoreCLR and ReadyToRun assemblies.

The image-only OpenCvSharpExtern is 5.54 MiB. It was compiled from the hash-verified OpenCV 4.13.0 source archive and OpenCvSharp commit `b161e7e012f5101f6d5dc68a835c59db6cc88b18`, enabling only core/imgproc/imgcodecs and JPEG/PNG/TIFF/WebP/BMP support. IPP, OpenCL, extra CPU-dispatch variants, video and ML modules are disabled. This prioritizes distribution size over optional acceleration. The exact build is recorded in `native-build.json`; the tested DLL SHA-256 is `F29C0DDF9F7944C817BD0E6922AE32BFF61F579D566ED39C0A1A19976C2C5F19`. PE dependency inspection lists only KERNEL32.dll and ole32.dll, with static CRT and no Media Foundation/FFmpeg dependency.

All **59 existing tests passed against this exact DLL** (48 core, 11 UI), including all five input formats, orientations, partial blocks, analysis, immutable rendering/export and lifecycle cases. Test output DLL hashes match the native build. Managed and Native AOT builds completed without compiler or trim/AOT warnings. The dialog's sole reflection binding was replaced by a property-change subscription, detached when the dialog closes, so the Analyze button remains compatible with trimming and AOT.

The actual Native AOT executable passed the Windows file-picker, JPEG analysis, result switching, reanalysis/Cancel, defaults and PNG export workflow. It loaded the image-only DLL from its own publish folder, with no CoreCLR loaded. Arabic/dark switching, localized summaries and unmirrored image content were captured, then the app closed with exit code 0. The framework-dependent executable separately passed the PNG workflow with JPEG-only checks omitted, retained 1280 x 800 export dimensions and the original SHA-256, and closed normally. Both stderr logs were empty. Native/source archive hashes, build logs, package size JSON, TRX results and screenshots are under `artifacts/verification/v1.3.1` and `artifacts/size-audit`. No forensic accuracy benchmark or exhaustive hardware-performance equivalence is claimed.

Documentation and release notes are English. Arabic remains only in UI localization and localization/Unicode tests. The existing v1.3.0 GitHub release body was corrected to English as well.

## Earlier validation (11 November 2026)

Verified on Windows x64 with .NET SDK 10.0.404 / runtime 10.0.15, Avalonia 12.1.3, Insiyab.Avalonia 0.3.2 and OpenCV/OpenCvSharp 4.13.

- NuGet restore and Release solution build succeeded. **Zero compiler warnings or errors.**
- **59 automated tests passed:** 48 core cases and 11 Avalonia/Skia UI workflow cases.
- Core checks cover empty/corrupted/unsupported files; pre-decode size guards; JPEG, PNG, BMP, TIFF and WebP; small images; 16-bit and alpha handling; all eight JPEG EXIF orientations and known orientation pixel ordering; TIFF orientation; Unicode paths; local noise/frequency changes; flat-image safeguards; robust normalization; validity-weighted combination; rendering immutability; PNG dimensions/pixels; source overwrite protection; and cancellation.
- A **4104 × 4104 (~16.8 MP)** JPEG was analyzed and exported at unchanged dimensions. Selecting 16px blocks correctly increased the effective block size to 32px to bound the analysis grid.
- Headless UI tests use actual Insiyab styles, Skia bitmaps and the real window/view model. They exercise repeated JPEG/PNG replacement, all method selections, non-JPEG qualification, rapid visualization changes with cached raw scores, invalid-file recovery, PNG export, wheel zoom, pointer drag and resize/fit. They verify the light theme, one viewer, the subtitle inside the brand pill, the visible color guide, and momentary mouse/keyboard comparison without losing zoom or recalculating scores. A shutdown regression test closes during analysis, pending rendering and completed rendering, and verifies the viewer detaches and late pointer callbacks are safe.
- The **published Windows executable was launched on the interactive desktop**. Windows UI Automation exercised native Open/Save dialogs, analysis, all six maps, fit/actual-size/zoom controls, opacity and sensitivity, and PNG export. PNG and JPEG were opened sequentially in the same session; JPEG-only methods were explicitly unavailable for PNG. Exported files were 1280 × 800 PNGs and original SHA-256 hashes were unchanged.
- The publish is self-contained and includes `PixelTrace.exe`, `coreclr.dll`, `hostfxr.dll`, `OpenCvSharp.dll`, **`OpenCvSharpExtern.dll`** and `opencv_videoio_ffmpeg4130_64.dll`. The OpenCV DLL is PE AMD64 (`8664`) and its SHA-256 matches the native library exercised by tests: `41E6A418637B7992814A668272E3197306F75C01B151DDB24AC2DE183FB9C3F3`. Its PE import table references Windows OS libraries, including Media Foundation; the imported-library list is saved with the verification evidence.

Evidence is under `artifacts/verification`: final test TRX files, a captured application window, procedural fixtures, six signal maps and native-publish metadata. The fixture's deliberately noisier patch is described in its `fixture.txt`; these are engineering checks, not an accuracy benchmark.

Not verified: Linux/macOS execution, installation on another Windows machine, unusual codec variants, sustained use at the 40 MP safety limit, or forensic accuracy on a real-photo corpus. No scientific AI/manipulation probabilities are claimed. Native codec calls are not interruptible halfway through; cancellation takes effect at the next checkpoint.

## 1.0.1 lifecycle follow-up

An application-error dialog was reported from an earlier agent test session; the user had not run the app. Its original managed stack was unavailable, so the exact exception behind that dialog is unconfirmed. Version 1.0.1 detaches both image bindings before disposing their native resources, defers final window closure until the first Closing event returns, catches native picker failures, and records unexpected exceptions locally with their stack and app version. Fatal exceptions are logged without being marked handled.

`tools/Verify-Windows-Lifecycle.ps1` tests the actual published executable and records normal process exit codes in `artifacts/verification/crash-fix/lifecycle.json`, along with stderr and UI regression test results. These checks use normal window closure instead of terminating test processes.

## 1.1.0 simplified interface

The application now defaults to light mode, with a single image canvas and a momentary **Hold for original** button. The original is shown before analysis; completed overlays use the same canvas. Mouse release outside the button, keyboard release, focus loss and deactivation restore the overlay. Comparison keeps the pan/zoom position and cached analysis scores. Main controls are method, sensitivity and opacity; block size, ELA quality and explanations are tucked into **Options**. The source and results panels, repeated Open/Fit controls and large explanation/sidebar panels were removed.

The persistent color guide identifies purple as lower, teal as moderate and yellow as higher local differences. A hover marker applies the renderer's sensitivity curve to the inspected block score. The app name and description share one title-bar pill, with the description directly below the name. The core algorithms and export resolution are unchanged.

The new publish is in `artifacts/win-x64-v1.1.0` so an already-running older version can keep its files open. The native Windows workflow verified all six maps and PNG export, then captured the original while Space was held and the overlay after release. Normal window closure returned exit code 0, stderr was empty and no crash log was produced. Captures and exports are under `artifacts/verification/light-ui`.

## 1.3.0 descriptive analysis summary

A concise Arabic/English summary now appears below the image after successful analysis. It distinguishes regions to review, no prominent differences and limited information. Up to three regions name the methods responding within them; selecting one switches to its strongest responding method and frames/zooms to the oriented bounds. The frame disappears during Show original and can be cleared. Single-method scope, JPEG-only exclusions, natural causes of differences and the limits of both positive and negative findings remain visible. Display settings do not recalculate the summary. No manipulation probability or authenticity verdict is generated.

Fourteen new core cases verify grouping and method attribution, aggregate ranking before threshold-crossing counts, omission of Combined/unavailable checks, validity/nonfinite masking, limited coverage, fixed response threshold, raw-score immutability, cancellation/grid validation, partial edge blocks and all eight EXIF region orientations. Two new UI cases exercise actual procedural-image analysis, region-button navigation, original comparison, cached summary under display changes, Arabic translation, image replacement, low-detail inputs and independent-method scope. Cancel rollback also verifies summary identity is preserved. All 59 solution tests passed; the final build/test runs had no compiler warnings or errors.

The published Windows executable passed native file selection, analysis, all six result selections, locked reanalysis/Cancel, display resets and PNG export at 1280 × 800 with an unchanged source SHA-256. The procedural fixture's central noise patch appears as the first review region; clicking it selected Noise, framed the patch and zoomed from fit to 205%. Arabic/dark switching preserved the selection, and Clear selection/Fit restored the overview. Captures are in `artifacts/verification/v1.3`. Normal closure returned exit code 0 and stderr was empty. These checks validate behavior and localization, not forensic accuracy on real photographs.

## 1.2.1 upload text clipping

The upload field now uses the font's natural line height and removes the picker text's negative bottom margin. This preserves Arabic letter tops and diacritics without replacing Insiyab's FileUpload. All 9 desktop workflow tests passed again. The published application was opened in Arabic and English on Windows, both upload dialogs were captured in `artifacts/verification/v1.2.1`, and normal closure returned exit code 0.

## 1.2.0 analysis setup and bilingual layout

The toolbar now opens an Insiyab Dialog containing its FileUpload, method selection and analysis settings. Reanalyze locks the current file and reuses decoded pixels; Cancel leaves applied settings, the image and completed results intact. Image selection and expensive analysis commit as one operation. The trailing Save result button appears on analysis start and stays disabled until a completed result is ready. Stream-backed and clipboard images use the same guarded decoder as files.

The instant display controls are vertical on the right in English and on the left in Arabic. Endpoint labels and numeric values clarify sensitivity/opacity; Default restores only those display values. Show original sits beside the filename. Insiyab DayNight supplies the theme animation; the brand pill uses the library's caption-height token and a smaller name. UI labels, methods, dialog actions, status and library words switch language together; the image and color scale keep physical left-to-right orientation.

UI regressions cover cancel rollback, locked-source reanalysis after the disk file is removed, independent method execution, display-only defaults, stream input through the actual FileUpload, real dialog submit actions, Arabic layout and method selection, theme switching, and label/button separation. Core tests additionally check byte-input decoding and input immutability.

The published 1.2.0 executable was tested on the actual Windows desktop: Insiyab FileUpload opened the native chooser, all computed maps were switched, reanalysis displayed the locked file, Cancel retained the result, Default restored both display values, and PNG export retained 1280 × 800 dimensions and the original file hash. The built-in DayNight was operated through keyboard input, then the interface was switched to Arabic. Captures confirm the left-side Arabic controls, translated library captions, dark theme, preserved result selection and unmirrored image lettering. Normal closure returned exit code 0; stderr was empty and no crash log was created. Evidence is in `artifacts/verification/v1.2`.
