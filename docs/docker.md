# Running Elsa.Server in Docker

This guide covers the production-style container image for `src/Apps/Elsa.Server` and the
`docker/compose/` reference stack that runs **PostgreSQL + Elsa.Server + Elsa Studio** with
Postgres-backed persistence.

- Image: `src/Apps/Elsa.Server/Dockerfile` (multi-stage, `net10.0`, non-root, port 8080)
- Reference stack: `docker/compose/docker-compose.yml`
- Curated demo composition: `docker/compose/elsa-server.shells.json`

The repo default `src/Apps/Elsa.Server/shells.json` (SQLite) is intentionally left untouched; the
compose stack mounts its own curated `shells.json` instead.

---

## Quickstart (full reference stack)

From `docker/compose/`:

```bash
# Build the Studio image once from the sibling repo (see "Elsa Studio" below), then:
docker compose --profile studio up --build
```

| Service      | URL                     | Notes                                   |
|--------------|-------------------------|-----------------------------------------|
| Elsa.Server  | http://localhost:13000  | Root path returns `{"status":"Healthy"}`|
| Elsa Studio  | http://localhost:14000  | Management UI (Blazor WebAssembly)      |
| PostgreSQL   | localhost:5432          | user/pw/db: `elsa` / `elsa` / `elsa`    |

Without the Studio image, run just the backend subset:

```bash
docker compose up --build        # postgres + elsa-server only
```

Prove persistence actually hits Postgres:

```bash
docker compose exec postgres psql -U elsa -d elsa -c '\dt'
# -> groundwork_documents, groundwork_document_indexes, groundwork_schema_history
docker compose exec postgres psql -U elsa -d elsa \
  -c "SELECT document_kind, count(*) FROM groundwork_documents GROUP BY document_kind;"
```

Tear down (including volumes):

```bash
docker compose --profile studio down -v
```

---

## Building the image standalone

The build context **must be the repository root** — the project references span the whole `src/`
tree and the build needs repo-root `Directory.Packages.props`, the `.slnx`, and a NuGet config.

```bash
docker build -f src/Apps/Elsa.Server/Dockerfile -t elsa-server:local .
docker run --rm -p 13000:8080 elsa-server:local
```

The image:

- builds on `mcr.microsoft.com/dotnet/sdk:10.0`, runs on `mcr.microsoft.com/dotnet/aspnet:10.0`;
- runs as the base image's non-root `$APP_UID`;
- listens on `8080` (`ASPNETCORE_URLS=http://+:8080`);
- has an HTTP `HEALTHCHECK` against `/` (curl is installed for this purpose only);
- pre-creates writable `/app/packages`, `/app/.nuplane`, `/app/.elsa`.

### NuGet restore in a clean container

The build restores against the repo-root `NuGet.config` directly (no
`--configfile` override). Its `packageSourceMapping` maps every package the
solution uses — including third-party transitives such as FastEndpoints,
JetBrains.Annotations, Polly, `NuGet.*`, and `SQLitePCLRaw.*` — to an explicit
source, so restore is fully self-contained and needs no user-level
`~/.nuget/NuGet/NuGet.Config` fallback. Preview packages (CShells, Nuplane,
Groundwork, Elsa.Platform) map narrowly to their feedz.io feeds; everything else
maps to nuget.org. All feeds are public (no auth).

---

## Configuration surface

Configuration layers, lowest to highest precedence: `appsettings.json` → mounted `shells.json` →
environment variables. Standard .NET double-underscore (`__`) env keys override any config path.

### Environment variables

| Variable | Purpose | Compose value |
|---|---|---|
| `ASPNETCORE_ENVIRONMENT` | Hosting environment | `Production` |
| `ASPNETCORE_URLS` | Kestrel bind (already set in the image) | `http://+:8080` |
| `Elsa__ModuleManagement__ApiKey` | The Elsa host management key the server accepts on its `/_elsa/module-management` API (header `X-Elsa-Module-Management-Key`). Server-side only — the browser never sees or sends it. **Must match** Studio's `Studio__BackendModuleManagementApiKey`. | `elsa-docker-demo-key` |
| `Cors__AllowedOrigins__0`, `__1`, … | Browser origins allowed by the `ElsaStudio` CORS policy. The Studio container's **published host origin** must be listed. | `http://localhost:14000` |
| `CShells__Shells__default__Features__FoundationIdentityAspNetCoreIdentity__AllowedReturnUrlOrigins__0`, `__1`, … | Origins the development login provider may redirect back to after sign-in. Add the Studio origin when Studio is hosted separately from the backend. | `http://localhost:14000` |
| `CShells__Shells__default__Features__GroundworkUnifiedPersistencePostgreSql__ConnectionString` | Optional: override the Postgres connection string without editing the mounted `shells.json`. | *(commented out)* |

`Cors:AllowedOrigins` defaults (in `appsettings.json`) are localhost dev values for running the
server outside Docker; the compose file adds the Studio container origin.

### Mounts

| Container path | Purpose |
|---|---|
| `/app/shells.json` (ro) | Shell composition. The compose stack mounts `elsa-server.shells.json`. |
| `/app/packages` | Nuplane directory feed — drop `.nupkg` activity/extension packages here to load them at runtime (watched). Backed by a named volume in compose. |

---

## Demo persistence composition

`docker/compose/elsa-server.shells.json` swaps the repo default (SQLite) for a single Groundwork
PostgreSQL document store:

- **`GroundworkUnifiedPersistencePostgreSql`** backs the **runtime**, **workflows-design** and
  **activities-design** lanes from one database. Its connection string binds from a **top-level
  `ConnectionString` property** on the feature section (not an `Options` wrapper):

  ```json
  "GroundworkUnifiedPersistencePostgreSql": {
    "ConnectionString": "Host=postgres;Port=5432;Database=elsa;Username=elsa;Password=elsa"
  }
  ```

- The SQLite EFCore design lanes (`WorkflowsDesignPersistenceEFCoreSqlite`,
  `ActivitiesDesignPersistenceEFCoreSqlite`) and the SQLite runtime lane
  (`GroundworkRuntimePersistenceSqlite`) are **removed** — the unified feature covers those lanes.

- The **diagnostics** EFCore persistence lanes ship only SQLite providers, so they are **omitted**
  to keep this a pure-Postgres persistence demo. The diagnostics *features*
  (`DiagnosticsConsoleLogStreaming`, `DiagnosticsOpenTelemetry`, `DiagnosticsStructuredLogs`) stay
  enabled but do not persist. If you want persisted diagnostics, add
  `DiagnosticsOpenTelemetryPersistenceEFCoreSqlite` / `DiagnosticsStructuredLogsPersistenceEFCoreSqlite`
  back and give `/app` a writable volume for the SQLite files.

- Engine self-instrumentation is enabled: `WorkflowsRuntimeTracing` emits engine spans and
  `DiagnosticsOpenTelemetryEngineBridge` forwards them into the OpenTelemetry ingestion store, so
  Studio's timing view is populated. Without the persistence lane above, the traces live in the
  in-memory store and reset on restart.

- The `SampleNuplaneActivities` and `WeatherForecastSample` sample features are dropped because they
  require Nuplane feed packages that are not present in the image.

`GroundworkUnifiedPersistencePostgreSql` declares `DependsOn "WorkflowsRuntimeResumption"`; CShells
auto-enables that dependency, so it does not need a `shells.json` entry (its assembly is referenced
by the host — see the note in `Elsa.Server.csproj`).

---

## Elsa Studio

The Studio image is **not** built from this repository. Build it from the sibling repo
`elsa-foundation-studio` (main), whose Dockerfile also uses its repo root as build context:

```bash
cd ../elsa-foundation-studio
git checkout main
docker build -f src/Elsa.Studio.Web/Dockerfile -t elsa-studio-web:local .
```

Then start the stack with the `studio` profile (`docker compose --profile studio up`).

Studio wiring (see `docker-compose.yml`):

- **`Studio__BackendBaseUrl` must be a host-reachable URL** (`http://localhost:13000`), **not** the
  compose service name. Studio is a Blazor WebAssembly app: the browser downloads the client and
  calls the backend directly, using the value surfaced at `GET /studio-runtime.js`. A compose
  service name like `http://elsa-server:8080` is not resolvable from the browser.
- **`Studio__BackendModuleManagementApiKey` must equal the server's `Elsa__ModuleManagement__ApiKey`.**
  This is the Elsa host management key, and it stays server-side in the Studio container — it is
  never published to the browser. Browser-facing host-control reads (module management, Extension
  Builder) go through the Studio management bridge, which attaches the key when calling the
  backend (see ADR 0037).
- The server's CORS policy must allow the Studio published origin (`Cors__AllowedOrigins__0`).

---

## Troubleshooting

- **`/` returns 500 with `FeatureNotFoundException: WorkflowsRuntimeResumption`** — the host is
  missing the `Elsa.Workflows.Runtime.Resumption` project reference that every Groundwork
  persistence provider depends on. It is wired in `Elsa.Server.csproj` / `Program.cs`; rebuild the
  image if you see this.
- **`Npgsql … Failed to connect to 127.0.0.1:5432`** — the feature fell back to its default
  connection string, meaning `ConnectionString` did not bind. Ensure it is a **top-level** property
  on the feature section (not nested under `Options`).
- **Studio shows connection/timeout errors to the backend** — `Studio__BackendBaseUrl` is not
  reachable from the browser, or the server's CORS policy does not allow the Studio origin.
