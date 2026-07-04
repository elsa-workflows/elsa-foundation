# Elsa Foundation Studio

Elsa Foundation Studio is a modular React studio shell hosted by ASP.NET Core. The repository owns the Studio module protocol, Vite React shell, TypeScript SDK, and sample class-library modules that contribute same-origin ESM assets and endpoints.

The first slice intentionally proves the extension boundary only:

- `Elsa.Studio.Core` defines manifests, diagnostics, and the manifest collection event.
- `Elsa.Studio.Api` exposes `GET /_elsa/studio/modules`.
- `Elsa.Studio.Web` hosts the root-mounted Vite shell.
- `Elsa.Studio.Samples.Dashboard` contributes a frontend-only dashboard module.
- `Elsa.Studio.Samples.WeatherForecast` contributes a frontend route and deterministic sample endpoint.
- `Elsa.Studio.ConsoleStream` demonstrates a backend-scoped console stream module.

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

Backend-targeted Studio modules should use the runtime backend base URL instead of the shell origin. The shell exposes it through `/studio-runtime.js` and the frontend SDK surfaces it as `api.backend.http`. If `Studio:BackendBaseUrl` is unset, the backend client falls back to the shell origin.

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

When `Studio:BackendBaseUrl` points at an Elsa Server backend, the console panel expects that backend to expose its own console stream under:

- `/_elsa/server/diagnostics/console-logs/recent`
- `/_elsa/server/diagnostics/console-logs/sources`
- `/_elsa/server/diagnostics/console-logs/hub`

## Docker

`Elsa.Studio.Web` can be built and run as a container image. Build from the repository root
(the build context must be the repo root):

```bash
docker build -f src/Elsa.Studio.Web/Dockerfile -t elsa-studio-web:local .
docker run --rm -p 8080:8080 \
  -e Studio__BackendBaseUrl=https://your-elsa-server:443 \
  elsa-studio-web:local
```

See [`docs/docker.md`](docs/docker.md) for the full runtime configuration surface
(environment variables, `shells.json` mounting, the optional `packages/` feed volume, and
a compose snippet).
