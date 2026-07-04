# Docker quickstart

Go from a fresh clone to a running Elsa stack — **PostgreSQL + Elsa.Server + Elsa Studio** —
by following this one document, top to bottom.

Everything here runs from this directory:

```bash
cd docker/compose
```

Two paths are covered:

- **Fast path** — Postgres + Elsa.Server only. No sibling checkout, no extra image. Good for
  API/backend work.
- **Full stack** — adds the Elsa Studio management UI, whose image is built from a *sibling* repo.

> This is a quickstart. For the full container/image reference — the complete environment-variable
> surface, mounts, the demo persistence composition, and troubleshooting — see
> [`docs/docker.md`](../../docs/docker.md).

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

## 2. Fast path — Elsa.Server + PostgreSQL

From `docker/compose`:

```bash
docker compose up --build
```

This builds the `elsa-server:local` image (context is the repo root) and starts two services:
`postgres` and `elsa-server`. Compose waits for Postgres to be healthy before starting the server.

Once the server reports healthy, hit its root endpoint:

```bash
curl http://localhost:13000/
```

Expected output:

```json
{"status":"Healthy","service":"elsa-server"}
```

That's the fast path — a running Elsa.Server backed by PostgreSQL. To also run the Studio UI,
continue to step 3.

> Tip: add `-d` (`docker compose up --build -d`) to run detached and get your terminal back;
> use `docker compose logs -f elsa-server` to follow logs.

---

## 3. Full stack — add Elsa Studio

The Studio image is **not** built from this repository. Build it once from the sibling repo
`elsa-foundation-studio`, whose Dockerfile also uses **its own repo root** as the build context.

Until the Studio Docker support is merged, check out its branch first
(`sfmskywalker-studio-docker-image`, PR [#182](https://github.com/elsa-workflows/elsa-foundation-studio/pull/182) in that repo):

```bash
cd ../elsa-foundation-studio                 # sibling checkout, next to this repo
git checkout sfmskywalker-studio-docker-image
docker build -f src/Elsa.Studio.Web/Dockerfile -t elsa-studio-web:local .
```

> Once that PR is merged, drop the `git checkout` line and just build from the sibling repo's default
> branch. The image tag must be `elsa-studio-web:local` — that is what the compose file expects.

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
downloads the client and calls the backend directly at `http://localhost:13000` (see the API key
note below).

---

## 4. Services, ports, and demo credentials

| Service      | Host URL / port          | What it is                                              |
|--------------|--------------------------|--------------------------------------------------------|
| Elsa.Server  | `http://localhost:13000` | Workflow server API. Root path returns `Healthy` JSON. |
| Elsa Studio  | `http://localhost:14000` | Management UI (Blazor WebAssembly). `studio` profile.  |
| PostgreSQL   | `localhost:5432`         | Persistence. Exposed for inspection only.              |

**Demo credentials & keys** (defined in `docker-compose.yml` / `elsa-server.shells.json`):

| What | Value |
|---|---|
| Postgres user / password / database | `elsa` / `elsa` / `elsa` |
| Module-management API key | `elsa-docker-demo-key` |

The API key wires Studio to the server: the server's `Elsa__ModuleManagement__ApiKey` **must match**
Studio's `Studio__BackendModuleManagementApiKey`. Both default to `elsa-docker-demo-key`.

> ⚠️ **These are demo-only values.** Change every credential and key — and lock down the exposed
> Postgres port — before using this for anything beyond local experimentation.

---

## 5. Verify Postgres persistence

The stack composes Elsa.Server with unified Postgres persistence (via the mounted
`elsa-server.shells.json`), so workflow/activity data lives in the `postgres` service. Confirm it:

```bash
docker compose exec postgres psql -U elsa -d elsa -c '\dt'
```

Expected tables:

```
 public | groundwork_document_indexes | table | elsa
 public | groundwork_documents        | table | elsa
 public | groundwork_schema_history   | table | elsa
```

To see what has been persisted:

```bash
docker compose exec postgres psql -U elsa -d elsa \
  -c "SELECT document_kind, count(*) FROM groundwork_documents GROUP BY document_kind;"
```

For the full explanation of the demo composition (which persistence lanes are included/omitted and
why), see [`docs/docker.md`](../../docs/docker.md#demo-persistence-composition) and the header notes
in [`elsa-server.shells.json`](elsa-server.shells.json).

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
docker compose logs -f elsa-server        # or: postgres, elsa-studio
```

**Override the Postgres connection string** without editing the mounted `shells.json` — set the env
var on the `elsa-server` service (there is a commented-out example in `docker-compose.yml`):

```
CShells__Shells__default__Features__GroundworkUnifiedPersistencePostgreSql__ConnectionString=Host=postgres;Port=5432;Database=elsa;Username=elsa;Password=elsa
```

**Override the API key** — change it on **both** services so they still match:

```
# elsa-server
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
