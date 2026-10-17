# Mark2

Mark2 is a web application for mark recognition.

The solution (`Mark2.slnx`) contains the app project in `Mark2/`. It is a Blazor WebAssembly app built with [MudBlazor](https://mudblazor.com/), and runs entirely in the browser. It is deployed to GitHub Pages.

## Requirements

- [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0) (currently RC2)
- `wasm-tools` workload for Release builds (AOT compilation)

```sh
% dotnet workload install wasm-tools
```

## Testing locally

```sh
% dotnet run --project Mark2
```

The dev server listens on http://localhost:5130 (https://localhost:7202 with the `https` profile).

## Build

```sh
% dotnet build                    # build all projects in the solution
% dotnet publish Mark2 -c Release -p GHPages=true -p GHPagesBase=/
```

ImageSharp 4 requires a Six Labors license key at build time. Set it in the `SixLaborsLicenseKey` environment variable, or put it in a local `sixlabors.lic` file at the repository root (ignored by git).

```sh
% SixLaborsLicenseKey=... dotnet publish Mark2 -c Release -p GHPages=true -p GHPagesBase=/
```

## Deployment

Pushing to `main` runs `.github/workflows/github-pages.yml`, which publishes the app and deploys it to GitHub Pages. The license key is read from the `SIXLABORS_LICENSE_KEY` repository secret.

## Sandbox (sbx)

`sbxenv.yaml` defines a Docker sandbox with the .NET SDK installed (see `sbx/dotnet/spec.yaml`).

```sh
% sbx env run .                          # run Claude Code in the sandbox
% sbx env run --env-arg agent=codex .    # or another coding agent
```

Inside the sandbox, bind the dev server to all interfaces so it is reachable from the host on port 5130:

```sh
% dotnet run --project Mark2 --urls http://0.0.0.0:5130
```
