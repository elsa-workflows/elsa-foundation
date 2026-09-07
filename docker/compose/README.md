# Docker quickstart

Go from a fresh clone to a running Elsa stack — **PostgreSQL + Elsa.Workbench + Elsa Studio** —
by following this one document, top to bottom.

Everything here runs from this directory:

```bash
cd docker/compose
```

Three ways to run, fastest first:

- **Published images** — pull `elsaworkflows/elsa-workbench` + `elsaworkflows/elsa-studio` from Docker
  Hub and run them; no clone, no build. See [Quick start — published images](#quick-start--published-images-no-clone-or-build)
  right below. Best for just trying the stack.
- **Fast path (build)** — Postgres + Elsa.Workbench only, built from this repo. Good for API/backend work.
- **Full stack (build)** — adds the Elsa Studio management UI, whose image is built from a *sibling* repo.

> This is a quickstart. For the full container/image reference — the complete environment-variable
> surface, mounts, the demo persistence composition, and troubleshooting — see
> [`docs/docker.md`](../../docs/docker.md).

---

## Quick start — published images (no clone or build)

CI publishes two images to Docker Hub — **`elsaworkflows/elsa-workbench`** and
**`elsaworkflows/elsa-studio`**. Every push to `main` publishes three tags: `latest`,
`4.0.0-preview.<n>` (`<n>` is the CI run number, so it increments but is not contiguous), and
`sha-<short-commit>`. There are no `4` / `4.0` / `4.0.0` release tags yet — Elsa 4 is pre-release.
Run the whole stack straight from these images.

> **Persistence is ephemeral here.** The server image's baked-in default composition is **SQLite**
> (written under `/app`), which is discarded when the `elsa-workbench` container is removed. For
> durable, Postgres-backed persistence, use the build-from-source reference stack in sections 2–3
> below.

### With Docker Compose

Download just the compose file and start it:

```bash
curl -O https://raw.githubusercontent.com/elsa-workflows/elsa-foundation/main/docker/compose/docker-compose.images.yml
docker compose -f docker-compose.images.yml up
```

### With the Docker CLI

Same result without Compose — start the server, then Studio pointed at it:

```bash
# Elsa.Workbench (SQLite default composition; the volume is the Nuplane package feed)
docker run -d --name elsa-workbench \
  -p 13000:8080 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e Elsa__ModuleManagement__ApiKey=elsa-docker-demo-key \
  -e Cors__AllowedOrigins__0=http://localhost:14000 \
  -v elsa-workbench-packages:/app/packages \
  elsaworkflows/elsa-workbench:latest

# Elsa Studio, pointed at the server backend
docker run -d --name elsa-studio \
  -p 14000:8080 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e Studio__BackendBaseUrl=http://localhost:13000 \
  -e Studio__BackendModuleManagementApiKey=elsa-docker-demo-key \
  elsaworkflows/elsa-studio:latest
```

Then open **http://localhost:14000** (Studio); it calls **http://localhost:13000** (the server).

### Pointing Studio at the server backend

Four environment variables wire the two containers together (double-underscore = config nesting):

| Setting | On | Value | Why |
|---|---|---|---|
| `Studio__BackendBaseUrl` | Studio | `http://localhost:13000` | Backend URL the Studio client calls. It runs **in the browser**, so this must be the server's **host-reachable** URL — *not* the compose service name `http://elsa-workbench:8080`, which only resolves inside the Docker network. |
| `Studio__BackendModuleManagementApiKey` | Studio | `elsa-docker-demo-key` | The Elsa host management key the Studio management bridge attaches server-side when calling the server's module-management API. Never sent to the browser. **Must match** the server key below. |
| `Elsa__ModuleManagement__ApiKey` | Server | `elsa-docker-demo-key` | The Elsa host management key the server accepts. |
| `Cors__AllowedOrigins__0` | Server | `http://localhost:14000` | Lets the browser (served from Studio's origin) call the server cross-origin. |

To pin instead of tracking `latest`, use a preview or commit tag —
`elsaworkflows/elsa-workbench:4.0.0-preview.471` or `:sha-2e7dbf0`. The two images are versioned
independently (they are built by separate pipelines), so their run numbers do not line up; pick each
one from its own [Docker Hub tag list](https://hub.docker.com/r/elsaworkflows/elsa-workbench/tags).
Demo credentials and the demo-only warning are the same as the [table in section 4](#4-services-ports-and-demo-credentials).

> ⚠️ `elsa-docker-demo-key` and the wide-open CORS origin are **demo-only** — change the key on both
> sides and scope CORS before exposing this anywhere.

---

## 1. Prerequisites

- **Docker Engine** with the **Compose v2** plugin (`docker compose ...`, not the legacy
  `docker-compose`). Check with:

  ```bash
  docker --version
  docker compose version
  ```

- **For the full stack only:** a checkout of the sibling repo **`elsa-foundation-studio`** next to
  this repository (the Studio image is built from there — see step 3). Not needed for the fast path.
- **No credentials required.** All NuGet feeds used by the image builds are public, so the builds
  work on a clean machine with no `~/.nuget` setup.

The first build downloads base images and restores packages, so it takes a few minutes. Subsequent
builds are cached.

---

## 2. Fast path — Elsa.Workbench + PostgreSQL

From `docker/compose`:

```bash
docker compose up --build
```

This builds the `elsa-workbench:local` image (context is the repo root) and starts two services:
`postgres` and `elsa-workbench`. Compose waits for Postgres to be healthy before starting the server.

Once the server reports healthy, hit its root endpoint:

```bash
curl http://localhost:13000/
```

Expected output:

```json
{"status":"Healthy","service":"elsa-workbench"}
```

That's the fast path — a running Elsa.Workbench backed by PostgreSQL. To also run the Studio UI,
continue to step 3.

> Tip: add `-d` (`docker compose up --build -d`) to run detached and get your terminal back;
> use `docker compose logs -f elsa-workbench` to follow logs.

---

## 3. Full stack — add Elsa Studio

The Studio image is **not** built from this repository. Build it once from the sibling repo
`elsa-foundation-studio`, whose Dockerfile also uses **its own repo root** as the build context.

```bash
cd ../elsa-foundation-studio                 # sibling checkout, next to this repo
git checkout main
docker build -f src/Elsa.Studio.Web/Dockerfile -t elsa-studio-web:local .
```

Back in this directory, bring up the whole stack with the `studio` profile:

```bash
cd -                                         # back to docker/compose
docker compose --profile studio up --build
```

Then open **Elsa Studio** in a browser:

```
http://localhost:14000
```

Sign in with the demo credentials from step 4. Studio is a Blazor WebAssembly app: the browser
downloads the client and calls the backend's workflow APIs directly at `http://localhost:13000`
with the user's authorization. Host-control operations such as module management are the exception:
those go through the server-side Studio management bridge, which holds the Elsa host management key
(see the API key note below).

---

## 4. Services, ports, and demo credentials

| Service      | Host URL / port          | What it is                                              |
|--------------|--------------------------|--------------------------------------------------------|
| Elsa.Workbench  | `http://localhost:13000` | Workflow server API. Root path returns `Healthy` JSON. |
| Elsa Studio  | `http://localhost:14000` | Management UI (Blazor WebAssembly). `studio` profile.  |
| PostgreSQL   | `localhost:5432`         | Persistence. Exposed for inspection only.              |

**Demo credentials & keys** (defined in `docker-compose.yml` / `elsa-workbench.shells.json`):

| What | Value |
|---|---|
| Postgres user / password / database | `elsa` / `elsa` / `elsa` |
| Elsa host management key | `elsa-docker-demo-key` |

The Elsa host management key wires the server-side Studio management bridge to the server: the
server's `Elsa__ModuleManagement__ApiKey` **must match** Studio's
`Studio__BackendModuleManagementApiKey`. Both default to `elsa-docker-demo-key`. The key never
leaves the two containers — the browser neither sees nor sends it.

> ⚠️ **These are demo-only values.** Change every credential and key — and lock down the exposed
> Postgres port — before using this for anything beyond local experimentation.

---

## 5. Verify Postgres persistence

The stack composes Elsa.Workbench with an explicit Groundwork Postgres provider and persistence lanes (via the
mounted `elsa-workbench.shells.json`), so workflow/activity data lives in the `postgres` service. Confirm it:

```bash
docker compose exec postgres psql -U elsa -d elsa -c '\dt'
```

Groundwork gives every storage unit its own table, named after the unit, so `\dt` lists one per
unit across the composed lanes: the design lanes contribute `elsa_workflow_definitions_v2`,
`elsa_workflow_definition_drafts`, `elsa_workflow_definition_versions` and their siblings, and the
runtime lane contributes the `runtime_*` family (`runtime_workflow_execution_state`,
`runtime_checkpoint_commit`, and so on). Groundwork keeps its own bookkeeping in
`__groundwork_schema_history` and `__groundwork_search_key_algorithms`.

To confirm a workflow you designed actually landed:

```bash
docker compose exec postgres psql -U elsa -d elsa \
  -c "SELECT count(*) FROM elsa_workflow_definitions_v2;"
```

For the full explanation of the demo composition (which persistence lanes are included/omitted and
why), see [`docs/docker.md`](../../docs/docker.md#demo-persistence-composition) and the header notes
in [`elsa-workbench.shells.json`](elsa-workbench.shells.json).

---

## 6. Common operations

**Tear down** (stop and remove containers; the Postgres data volume survives):

```bash
docker compose --profile studio down
```

**Tear down and wipe data** (also drops the `pgdata` volume — a clean slate next time):

```bash
docker compose --profile studio down -v
```

**View logs:**

```bash
docker compose logs -f elsa-workbench        # or: postgres, elsa-studio
```

**Override the Postgres connection string** without editing the mounted `shells.json` — set the env
var on the `elsa-workbench` service (there is a commented-out example in `docker-compose.yml`):

```
CShells__Shells__default__Features__GroundworkProviderPostgreSql__ConnectionString=Host=postgres;Port=5432;Database=elsa;Username=elsa;Password=elsa
```

**Override the Elsa host management key** — change it on **both** services so they still match:

```
# elsa-workbench
Elsa__ModuleManagement__ApiKey=<your-key>
# elsa-studio
Studio__BackendModuleManagementApiKey=<your-key>
```

---

## 7. Troubleshooting

The common failure modes — `FeatureNotFoundException: WorkflowsRuntimeResumption`, the
`Failed to connect to 127.0.0.1:5432` connection-string binding issue, and Studio CORS / backend
connectivity errors — are documented in
[`docs/docker.md`](../../docs/docker.md#troubleshooting). Start there.
