# Running the Studio apps in Docker

Studio Foundation and Workbench each have a Dockerfile under `studio/src/apps`. The Studio Essentials profile extends the
Foundation image with its package feed and shell configuration. Point each image at an Elsa backend using environment
variables. You can also mount a `shells.json` file.

| Studio host or profile | Dockerfile | Published image | Pairs with |
| --- | --- | --- | --- |
| [Essentials profile](../docker/profiles/essentials/README.md) | `studio/docker/profiles/essentials/Dockerfile` | `elsaworkflows/elsa-studio` | `elsaworkflows/elsa-essentials-host` |
| `Elsa.Studio.Workbench.Host` | `studio/src/apps/Elsa.Studio.Workbench.Host/Dockerfile` | `elsaworkflows/elsa-studio-workbench` | `elsaworkflows/elsa-workbench` |
| [`Elsa.Studio.Foundation.Host`](../src/apps/Elsa.Studio.Foundation.Host/README.md) | `studio/src/apps/Elsa.Studio.Foundation.Host/Dockerfile` | `elsaworkflows/elsa-studio-foundation` | The bare `Elsa.Foundation.Host` |

Before [#5768](https://github.com/elsa-workflows/elsa-foundation/issues/5768), `elsaworkflows/elsa-studio` ran the Workbench
app under its former name, `Elsa.Studio.Web`.

Build Foundation and Workbench with the repository root as the context. Each host Dockerfile copies its project
dependencies and shared build configuration from that context. The [Essentials profile build](../docker/profiles/essentials/README.md)
uses a prepared context with the pinned packages and shell configuration.

> The React/Vite frontend assets are generated into each project's `wwwroot` by
> `pnpm build` before building host images. The Docker build stages consume those
> assets and build the .NET host. The Docker workflow performs the frontend build,
> and each host Dockerfile checks that the shell asset exists. The host builds use
> public NuGet feeds.

All three images serve installed add-on browser files from verified package archives. See
[upgrading older installs](../src/essentials/Elsa.Studio.Hosting.Nuplane/README.md) if you reuse an existing package store.

## Quick start — prebuilt image (no clone or build)

`.github/workflows/studio-docker.yml` publishes the three images above to Docker Hub. Each gets `latest`,
`4.0.0-preview.<n>` and `sha-<commit>` tags from `main`, and the version tags (`4`, `4.0`, `4.0.0`) from a release tag. The
flavors build in parallel and publish independently: one image failing to push does not stop the others. The examples below
use `elsaworkflows/elsa-studio`; the other two images take the same settings.

### With Docker Compose

Download the Compose file from the `elsa-foundation` repo to start Elsa Workbench and Studio
with the bundled local SQLite settings.

```bash
curl -O https://raw.githubusercontent.com/elsa-workflows/elsa-foundation/main/docker/compose/docker-compose.images.yml
docker compose -f docker-compose.images.yml up
```

### With the Docker CLI

Follow the [Docker CLI quickstart](../../docs/docker-hub-quickstart.md#start-the-containers) to start Workbench and Studio
on a shared Docker network. For the curated server image, follow the
[server Essentials profile deployment instructions](../../docker/profiles/essentials-curated/README.md).

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
pnpm --dir studio install --frozen-lockfile
pnpm --dir studio build
docker build -f studio/src/apps/Elsa.Studio.Workbench.Host/Dockerfile -t elsa-studio-web:local .
docker build -f studio/src/apps/Elsa.Studio.Foundation.Host/Dockerfile -t elsa-studio-foundation:local .
```

Build the derived Essentials image with the [profile instructions](../docker/profiles/essentials/README.md).
The runtime examples below use the Workbench image. The Foundation host and Essentials profile accept the same settings.

The host images use a two-stage build. `mcr.microsoft.com/dotnet/sdk:10.0` publishes the app and
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
| `Studio__BackendBaseUrl` | Workbench `https://localhost:7243`, Essentials `https://localhost:7262`, Foundation `https://localhost:7250` | Base URL of the Elsa Server backend. Surfaced to the browser via `/studio-runtime.js`. |
| `Studio__BackendServerBaseUrl` | *(falls back to `Studio__BackendBaseUrl`)* | URL used by Studio's server-side authentication, management bridge, and relays. Use a Docker-internal URL here when it differs from the browser URL. |
| `Studio__BackendModuleManagementApiKey` | `local-dev-module-management-key` | **Server-side-only** key the Studio management bridge uses for Studio→backend host-control calls. It is **never emitted to the browser** (ADR 0037); the SPA carries no host management key. **Override in any real deployment.** |
| `Studio__Workflows__AutosaveEnabledByDefault` | `true` | Whether the workflow editor opens with Autosave enabled. Users can still toggle it per editing session. |
| `Studio__ActivityDefinitions__LocalRecovery__Enabled` | `false` | Opts the host into expiring device-local recovery snapshots for unsaved Activity Definition provider state. Keep disabled unless the deployment's data-handling policy allows browser storage. |
| `Studio__ActivityDefinitions__LocalRecovery__TtlMinutes` | `1440` | Maximum retention for an enabled Activity Definition recovery snapshot. Snapshots are also scoped to tenant/user/draft/provider/schema and cleared after save, discard, or logout. |
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
    image: elsa-studio-web:local   # build: { context: ., dockerfile: studio/src/apps/Elsa.Studio.Workbench.Host/Dockerfile }
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
