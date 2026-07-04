# Running Elsa.Studio.Web in Docker

`src/Elsa.Studio.Web` ships with a production `Dockerfile` that produces a self-contained
container image of the Studio host. Point it at an Elsa Server backend at runtime via
environment variables and, optionally, a mounted `shells.json`.

The Dockerfile lives at `src/Elsa.Studio.Web/Dockerfile`, but the **build context must be
the repository root** because the build depends on repo-root configuration
(`Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`,
`NuGet.config`).

> The React/Vite frontend assets are prebuilt and committed under each project's
> `wwwroot`, and no MSBuild target invokes pnpm/vite. `dotnet publish` alone produces a
> complete, runnable app, so the image has **no Node/pnpm build stage**. All three NuGet
> feeds used by the build are public, so no NuGet credentials are required.

## Build

From the repository root:

```bash
docker build -f src/Elsa.Studio.Web/Dockerfile -t elsa-studio-web:local .
```

The image is a two-stage build: `mcr.microsoft.com/dotnet/sdk:10.0` publishes the app and
`mcr.microsoft.com/dotnet/aspnet:10.0` runs it as the base image's non-root user.

## Run

```bash
docker run --rm -p 8080:8080 \
  -e Studio__BackendBaseUrl=https://your-elsa-server:443 \
  -e Studio__BackendModuleManagementApiKey=your-api-key \
  elsa-studio-web:local
```

Then open <http://localhost:8080/>. The container starts and serves HTTP even without a
reachable backend; backend-dependent features simply stay idle until the backend is up.

## Runtime configuration surface

Configuration follows standard .NET configuration layering: `appsettings.json` in the
image provides defaults, and environment variables override them. Nested keys use the
double-underscore (`__`) separator.

| Environment variable | Default (in image) | Purpose |
| --- | --- | --- |
| `Studio__BackendBaseUrl` | `https://localhost:7243` | Base URL of the Elsa Server backend. Surfaced to the browser via `/studio-runtime.js`. |
| `Studio__BackendModuleManagementApiKey` | `local-dev-module-management-key` | API key the Studio uses for backend module-management calls. **Override in any real deployment.** |
| `ASPNETCORE_URLS` | `http://+:8080` | Kestrel bind address/port. Change the port here if you don't want 8080. |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Standard ASP.NET Core environment name. |
| `Logging__LogLevel__Default` | `Information` | Default log level. e.g. set to `Warning` to quieten logs. |

You can confirm the backend URL override took effect:

```bash
curl http://localhost:8080/studio-runtime.js
# window.__ELSA_STUDIO_RUNTIME__ = {"backendBaseUrl":"https://your-elsa-server:443", ...};
```

## Customizing `shells.json` (feature composition)

`shells.json` controls which CShells features are enabled per shell. The image ships a
working default at `/app/shells.json`. To run a different feature set, mount a replacement
file over that path:

```bash
docker run --rm -p 8080:8080 \
  -v "$(pwd)/my-shells.json:/app/shells.json:ro" \
  elsa-studio-web:local
```

A mounted file takes precedence over the built-in default. The host loads `shells.json`
with `reloadOnChange: true`; this is safe with a bind-mounted file (the app boots normally
and does not crash on file-watch events).

## Optional: local Nuplane package feed

Studio watches a directory feed at `/app/packages` (the Nuplane
`studio-local-packages` feed) for dynamically loaded packages. The image creates this
folder empty. To supply packages from the host, mount a directory over it:

```bash
docker run --rm -p 8080:8080 \
  -v "$(pwd)/packages:/app/packages" \
  elsa-studio-web:local
```

Because Nuplane persists reconciliation state under the content root (`/app/.nuplane`) and
watches `/app/packages`, the runtime user owns `/app` so it can write there.

## Image name / tag convention

- Local development: `elsa-studio-web:local` (as used above).
- When referenced from an orchestrating `docker-compose` (e.g. alongside Postgres and an
  Elsa.Server image), publish/tag the image under the name that compose expects and set
  the required `Studio__BackendBaseUrl` / `Studio__BackendModuleManagementApiKey` there.

### Minimal compose snippet

```yaml
services:
  studio:
    image: elsa-studio-web:local   # build: { context: ., dockerfile: src/Elsa.Studio.Web/Dockerfile }
    ports:
      - "8080:8080"
    environment:
      Studio__BackendBaseUrl: "http://elsa-server:8080"
      Studio__BackendModuleManagementApiKey: "change-me"
    # Optional overrides:
    # volumes:
    #   - ./shells.json:/app/shells.json:ro
    #   - ./packages:/app/packages
```
