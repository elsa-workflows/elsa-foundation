# Elsa Foundation Studio

Elsa Foundation Studio is a modular React studio shell hosted by ASP.NET Core. The repository owns the Studio module protocol, Vite React shell, TypeScript SDK, and sample class-library modules that contribute same-origin ESM assets and endpoints.

The first slice intentionally proves the extension boundary only:

- `Elsa.Studio.Core` defines manifests, diagnostics, and the manifest collection event.
- `Elsa.Studio.Api` exposes `GET /_elsa/studio/modules`.
- `Elsa.Studio.Web` hosts the root-mounted Vite shell.
- `Elsa.Studio.Samples.Dashboard` contributes a frontend-only dashboard module.
- `Elsa.Studio.Samples.WeatherForecast` contributes a frontend route and deterministic sample endpoint.

The shell UX is adapted from [`elsa-workflows/elsa-foundation-designer`](https://github.com/elsa-workflows/elsa-foundation-designer) main at `2a04fdb`, without importing its Next.js runtime or product feature modules.

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
- `/_elsa/studio/samples/weather-forecast`

