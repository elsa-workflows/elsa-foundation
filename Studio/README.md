# Elsa Foundation Studio

Elsa Foundation Studio is a modular React studio shell hosted by ASP.NET Core. The repository owns the Studio module protocol, Vite React shell, TypeScript SDK, and sample class-library modules that contribute same-origin ESM assets and endpoints.

The first slice intentionally proves the extension boundary only:

- `Elsa.Studio.Core` defines manifests, diagnostics, and the manifest collection event.
- `Elsa.Studio.Api` exposes `GET /_elsa/studio/modules`.
- `Elsa.Studio.Web` hosts the root-mounted Vite shell.
- `Elsa.Studio.Samples.Dashboard` contributes a frontend-only dashboard module.
- `Elsa.Studio.Samples.WeatherForecast` contributes a frontend route and deterministic sample endpoint.

The shell UX is adapted from [`elsa-workflows/elsa-foundation-designer`](https://github.com/elsa-workflows/elsa-foundation-designer) main at `2a04fdb`, without importing its Next.js runtime or product feature modules.

## Modular features (CShells)

Studio is a modular monolith built on [CShells](https://www.cshells.io/). Each module is a class library that exposes a CShells *feature* (`IShellFeature` / `IWebShellFeature`) instead of being hardwired into the host. The host (`Elsa.Studio.Web`) does not call module-specific registration methods — it enables features per shell from `appsettings.json`:

```json
{
  "CShells": {
    "Shells": {
      "Default": {
        "Features": {
          "StudioApi": {},
          "ConsoleStream": {},
          "DashboardSample": {},
          "WeatherForecastSample": {}
        },
        "Configuration": {
          "WebRouting": { "Path": "" }
        }
      }
    }
  }
}
```

Removing a feature key (or setting the shell up differently) turns the corresponding module — its services, manifest contribution, and endpoints — on or off without code changes. `WebRouting:Path` of `""` mounts the shell's endpoints at the application root.

Studio modules can also declare backend capabilities they require. The manifest endpoint filters modules automatically when those capabilities are not present, so a frontend module can follow a backend CShell feature without a second manual toggle. Capabilities can come from local contributors, host configuration, or a backend capability endpoint:

```json
{
  "CShells": {
    "Shells": {
      "Default": {
        "Features": {
          "StudioApi": {
            "Options": {
              "BackendCapabilitiesUrl": "https://localhost:5001/default/_elsa/capabilities",
              "Modules": {
                "Elsa.Studio.Experimental": "disabled",
                "Elsa.Studio.Diagnostics": "enabled"
              }
            }
          }
        }
      }
    }
  }
}
```

Module override values accept `auto`/omitted, `enabled`/`true`, and `disabled`/`false`. Forced modules with missing backend capabilities are reported as incompatible diagnostics instead of being loaded. The backend capability URL should include the CShell web-routing path when the backend shell is path-routed.

Features ship in:

| Feature name | Library | Contract |
|---|---|---|
| `StudioApi` | `Elsa.Studio.Api` | `IWebShellFeature` |
| `ConsoleStream` | `Elsa.Studio.ConsoleStream` | `IWebShellFeature` |
| `DashboardSample` | `Elsa.Studio.Samples.Dashboard` | `IShellFeature` |
| `WeatherForecastSample` | `Elsa.Studio.Samples.WeatherForecast` | `IWebShellFeature` |

## Build

```bash
pnpm install
pnpm build
dotnet build
dotnet test
```

## Run

```bash
dotnet run --project src/Elsa.Studio.Web/Elsa.Studio.Web.csproj
```

Open:

- `/`
- `/dashboard`
- `/weather`
- `/diagnostics/modules`
- `/_elsa/studio/modules`
- `/_elsa/studio/diagnostics/console-logs/recent`
- `/_elsa/studio/diagnostics/console-logs/sources`
- `/_elsa/studio/diagnostics/console-logs/hub`
- `/_elsa/studio/samples/weather-forecast`
