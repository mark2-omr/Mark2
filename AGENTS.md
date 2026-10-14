# AGENTS.md

Guidance for coding agents working in this repository.

## Overview

Mark2 is a mark recognition (OMR) web app. It is a single-project Blazor WebAssembly app (`Mark2.csproj`) that runs entirely in the browser and is deployed to GitHub Pages. Users load a layout (from a file or from `https://repository.mark2.org/<surveyId>.json`) and scanned images, run recognition, and download the results as a spreadsheet.

## Tech stack

- .NET 11 (RC2) / Blazor WebAssembly, AOT + SIMD in Release builds
- MudBlazor 9 for UI
- SixLabors.ImageSharp 4 for image processing
- NPOI for spreadsheet output (SkiaSharp is referenced only so that AOT can compile NPOI.OOXML)
- ONNX Runtime Web (`wwwroot/js/ort/`) for handwritten digit recognition via JS interop (`runOnnxRuntime`)

## Layout

| Path | Contents |
| --- | --- |
| `Program.cs` | Host setup (localization, MudBlazor services) |
| `Pages/Index.razor` | The main (and only) page: UI and workflow |
| `Pages/Index.resx`, `Pages/Index.ja.resx` | UI strings (English / Japanese) |
| `Models/Survey.cs` | Layout loading, recognition across images, spreadsheet export |
| `Models/Item.cs` | Per-image processing: square detection, interpolation, mark / digit recognition, log images |
| `Models/*.cs` | Layout data model (`Page`, `Question`, `Area`, `Square`, `Repository`, ...) |
| `Layout/MainLayout.razor` | App layout |
| `wwwroot/` | Static assets, `index.html` (startup loading screen), service worker, JS |
| `.github/workflows/github-pages.yml` | Publish and deploy to GitHub Pages on push to `main` |
| `sbxenv.yaml`, `sbx/dotnet/spec.yaml` | Docker sandbox (sbx) environment with the .NET SDK |

## Commands

```sh
dotnet build                                  # build (Debug)
dotnet run                                    # dev server on http://localhost:5130
dotnet run --urls http://0.0.0.0:5130         # inside the sbx sandbox (reachable from host)
dotnet publish -c Release -p GHPages=true -p GHPagesBase=/   # production build (needs wasm-tools)
```

There is no test project. Verify changes with `dotnet build` and, for UI or recognition changes, by running the app.

Release builds use AOT compilation and are slow; prefer Debug builds while iterating.

### AOT build runs out of memory

In an environment with 8 GB of memory (e.g. the sbx sandbox), the Release publish fails at the final link step:

```
emcc : error : '.../wasm-opt --strip-target-features --post-emscripten -O2 ...' failed (received SIGKILL (-9))
error MSB3073: The command "emcc ..." exited with code 1.
```

`wasm-opt -O2` on `dotnet.native.wasm` needs about 7 GB by itself and is killed by the OOM killer (`dmesg` shows `Out of memory: Killed process ... (wasm-opt)`). The preceding AOT step (`*.dll.bc -> *.dll.o`, parallel clang) peaks lower, so limiting build parallelism does not help.

Before running a Release publish, check how much memory is available and choose the build method accordingly:

```sh
free -g                                   # total memory of the machine / sandbox
cat /sys/fs/cgroup/memory.max 2>/dev/null # container memory limit, if any ("max" = no limit)
```

| Available memory | Build method |
| --- | --- |
| Plenty (roughly 12 GB or more) | The default command (`-O2` link, same as CI) |
| About 8 GB | Add `-p:EmccLinkOptimizationFlag=-O1` |
| Less than about 7 GB | Even `-O1` is likely to fail; give the machine or sandbox more memory |

```sh
# About 8 GB: lower the link optimization level
dotnet publish -c Release -p GHPages=true -p GHPagesBase=/ -p:EmccLinkOptimizationFlag=-O1
```

Measured in an 8 GB sbx sandbox (no swap, 10 CPUs): the default build was killed at a peak of about 7.9 GB (`wasm-opt` alone used up to about 7.5 GB), while `-O1` completed at a peak of about 6.5 GB in about 2.5 minutes. The 12 GB threshold for the default build is an estimate, not a measurement; if the default build is killed, retry with `-O1`.

`-O1` is only for verifying the build locally. Do not add it to `Mark2.csproj` or the workflow: GitHub Actions runners have enough memory for the default `-O2`, which produces the deployed build.

## Conventions

- Add user-facing strings to both `Pages/Index.resx` and `Pages/Index.ja.resx`, and use them through `IStringLocalizer` (`Loc["Key"]`).
- Use ImageSharp 4 APIs (e.g. `Color.ParseHex(...).ToPixel<Rgba32>()`; `Rgba32.ParseHex` no longer exists). Encode debug log images as RGB PNG so overlays keep their color on grayscale scans.
- ImageSharp 4 needs a Six Labors license key at build time: the `SixLaborsLicenseKey` environment variable (CI uses the `SIXLABORS_LICENSE_KEY` secret) or a local `sixlabors.lic`. Never commit the key or `sixlabors.lic`.
- Keep the code style of the surrounding code (nullable enabled, implicit usings, collection expressions).

## Git and PRs

- Do not commit directly to `main`; create a branch and open a PR.
- Commit messages are short, imperative English sentences (e.g. `Update MudBlazor from 8.15.0 to 9.11.0`), with details in the body when useful.
- Pushing to `main` deploys to production (GitHub Pages).
