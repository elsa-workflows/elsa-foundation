# Running Elsa.Studio.Web in Docker

`src/Elsa.Studio.Web` ships with a production `Dockerfile` that produces a self-contained
container image of the Studio host. Point it at an Elsa Server backend at runtime via
environment variables and, optionally, a mounted `shells.json`.

The Dockerfile lives at `src/Elsa.Studio.Web/Dockerfile`, but the **build context must be
the repository root** because the build depends on repo-root configuration
(`Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`,
`NuGet.config`).

> The React/Vite frontend assets are generated into each project's `wwwroot` by
> `pnpm build` before the Docker build. No MSBuild target invokes pnpm/vite, so the
> image has **no Node/pnpm build stage**. The Docker workflow performs this frontend
> build and the Dockerfile fails if the host shell asset is missing. All three NuGet
> feeds used by the build are public, so no NuGet credentials are required.

## Quick start — prebuilt image (no clone or build)

CI publishes a prebuilt image to Docker Hub as **`elsaworkflows/elsa-studio`**, tagged with the
Elsa major version (`4`, `4.0`, `4.0.0`), plus `latest` and `4.0.0-preview.<n>` from `main`.

### With Docker Compose

Download the compose file from the `elsa-foundation` repo and start the full stack
(PostgreSQL + Elsa.Server + Elsa Studio):

```bash
curl -O https://raw.githubusercontent.com/elsa-workflows/elsa-foundation/main/docker/compose/docker-compose.images.yml
docker compose -f docker-compose.images.yml up
```

### With the Docker CLI

Start the server, then Studio pointed at it:

```bash
# Elsa.Server
docker run -d --name elsa-server \
  -p 13000:8080 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e Elsa__ModuleManagement__ApiKey=elsa-docker-demo-key \
  -e Cors__AllowedOrigins__0=http://localhost:14000 \
  -v elsa-server-packages:/app/packages \
  elsaworkflows/elsa-server:latest

# Elsa Studio, pointed at the server backend
docker run -d --name elsa-studio \
  -p 14000:8080 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e Studio__BackendBaseUrl=http://localhost:13000 \
  -e Studio__BackendServerBaseUrl=http://host.docker.internal:13000 \
  -e Studio__BackendModuleManagementApiKey=elsa-docker-demo-key \
  elsaworkflows/elsa-studio:latest
```

Then open **http://localhost:14000** (Studio); it calls **http://localhost:13000** (the server).

### Wiring Studio to the server

Five environment variables wire the two containers together when the browser and Studio container need different
routes to the server:

| Setting | On | Value | Why |
|---|---|---|---|
| `Studio__BackendBaseUrl` | Studio | `http://localhost:13000` | Backend URL the Studio client calls. It runs **in the browser**, so this must be the server's **host-reachable** URL — *not* a Docker-internal service name. |
| `Studio__BackendServerBaseUrl` | Studio | `http://host.docker.internal:13000` | Backend URL used by Studio's server-side authentication, management bridge, and relays. It must be reachable **from the Studio container**. On Docker Desktop, `host.docker.internal` reaches the host; use the backend service name when both containers share a Docker network. |
| `Studio__BackendModuleManagementApiKey` | Studio | `elsa-docker-demo-key` | **Server-side-only** Studio secret. Studio's management bridge attaches it on Studio→server host-control calls; it is **never sent to the browser** (ADR 0037). **Must match** the server key. |
| `Elsa__ModuleManagement__ApiKey` | Server | `elsa-docker-demo-key` | The key the server accepts. |
| `Cors__AllowedOrigins__0` | Server | `http://localhost:14000` | Lets the browser (served from Studio's origin) call the server cross-origin. |

> **Persistence is ephemeral.** The server image defaults to SQLite under `/app`, which is
> discarded when the container is removed. For durable Postgres-backed persistence, build from
> source using the `elsa-foundation` repo's compose stack.

> ⚠️ `elsa-docker-demo-key` and the wide-open CORS origin are **demo-only** — change the key on
> both sides and scope CORS before exposing this anywhere.

`Studio__BackendServerBaseUrl` is optional. When omitted, Studio uses `Studio__BackendBaseUrl` for server-side calls,
which preserves the single-URL configuration used by existing deployments. Authentication remains enabled unless
`Studio__Auth__Enabled=false` is explicitly supplied; the backend must provide the identity endpoints and the user
must sign in before the Studio management and console surfaces can be used.

---

## Build from source

From the repository root:

```bash
pnpm install --frozen-lockfile
pnpm build
docker build -f src/Elsa.Studio.Web/Dockerfile -t elsa-studio-web:local .
```

The image is a two-stage build: `mcr.microsoft.com/dotnet/sdk:10.0` publishes the app and
`mcr.microsoft.com/dotnet/aspnet:10.0` runs it as the base image's non-root user.

## Run

```bash
docker run --rm -p 8080:8080 \
  -e Studio__BackendBaseUrl=https://your-elsa-server:443 \
  -e Studio__BackendServerBaseUrl=http://elsa-server:8080 \
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
| `Studio__BackendServerBaseUrl` | *(falls back to `Studio__BackendBaseUrl`)* | URL used by Studio's server-side authentication, management bridge, and relays. Use a Docker-internal URL here when it differs from the browser URL. |
| `Studio__BackendModuleManagementApiKey` | `local-dev-module-management-key` | **Server-side-only** key the Studio management bridge uses for Studio→backend host-control calls. It is **never emitted to the browser** (ADR 0037); the SPA carries no host management key. **Override in any real deployment.** |
| `Studio__Workflows__AutosaveEnabledByDefault` | `true` | Whether the workflow editor opens with Autosave enabled. Users can still toggle it per editing session. |
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
  the required `Studio__BackendBaseUrl`, optional `Studio__BackendServerBaseUrl`, and
  `Studio__BackendModuleManagementApiKey` there.

### Minimal compose snippet

```yaml
services:
  studio:
    image: elsa-studio-web:local   # build: { context: ., dockerfile: src/Elsa.Studio.Web/Dockerfile }
    ports:
      - "8080:8080"
    environment:
      # Browser -> backend URL. This must be resolvable from the user's browser.
      Studio__BackendBaseUrl: "http://localhost:13000"
      # Studio container -> backend URL. Use the service name on the shared Docker network.
      Studio__BackendServerBaseUrl: "http://elsa-server:8080"
      Studio__BackendModuleManagementApiKey: "change-me"
    # Optional overrides:
    # volumes:
    #   - ./shells.json:/app/shells.json:ro
    #   - ./packages:/app/packages
```
