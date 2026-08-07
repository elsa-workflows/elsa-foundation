# Elsa on Docker Hub — quickstart

Run the prebuilt Elsa Workbench + Elsa Studio images from Docker Hub with plain `docker` commands —
no checkout, no build. For building the images yourself or the full PostgreSQL compose stack, see
[Docker & compose](docker.md).

| Service        | Image                          | URL                    | Login                    |
|----------------|--------------------------------|------------------------|--------------------------|
| Elsa Workbench | `elsaworkflows/elsa-workbench` | http://localhost:13000 | —                        |
| Elsa Studio    | `elsaworkflows/elsa-studio`    | http://localhost:14000 | `admin` / `Password123!` |

Both images publish `latest`, `4.0.0-preview.<n>` and `sha-<short-commit>` on every push to `main`.
There are no `4` / `4.0` / `4.0.0` release tags yet — to pin, use a preview or commit tag.

The login is the demo admin seeded by the image's built-in `shells.json` (Development posture).
Demo credentials and API key throughout — do not expose this setup beyond your machine.

## Quickstart

```bash
docker network create elsa-demo

docker pull elsaworkflows/elsa-workbench:latest
docker pull elsaworkflows/elsa-studio:latest

docker run -d --name elsa-workbench \
  --network elsa-demo \
  -p 13000:8080 \
  -e ASPNETCORE_ENVIRONMENT=Development \
  -e Elsa__ModuleManagement__ApiKey=elsa-docker-demo-key \
  -e Cors__AllowedOrigins__0=http://localhost:14000 \
  -e CShells__Shells__default__Features__FoundationIdentityAspNetCoreIdentity__AllowedReturnUrlOrigins__0=http://localhost:14000 \
  -v elsa-workbench-packages:/app/packages \
  elsaworkflows/elsa-workbench:latest

docker run -d --name elsa-studio \
  --network elsa-demo \
  -p 14000:8080 \
  -e ASPNETCORE_ENVIRONMENT=Development \
  -e Studio__Auth__Enabled=true \
  -e Studio__BackendBaseUrl=http://localhost:13000 \
  -e Studio__BackendServerBaseUrl=http://elsa-workbench:8080 \
  -e Studio__BackendModuleManagementApiKey=elsa-docker-demo-key \
  elsaworkflows/elsa-studio:latest
```

Open http://localhost:14000 and sign in as `admin` / `Password123!`.

The non-obvious settings:

- `Studio__BackendBaseUrl` is used by the Blazor WebAssembly client **in your browser**, so it must
  be the host-published server URL (`http://localhost:13000`), never the container name.
- `Studio__BackendServerBaseUrl` is used by Studio's server-side management bridge **inside the
  Docker network**, so it uses the container name (`http://elsa-workbench:8080`).
- `Studio__BackendModuleManagementApiKey` must equal the server's
  `Elsa__ModuleManagement__ApiKey`. It stays server-side (never sent to the browser) and gates the
  module *package* management API.
- `Cors__AllowedOrigins__0` and `...AllowedReturnUrlOrigins__0` both point at the Studio origin so
  the browser can call the API and the login flow can redirect back to Studio.
- `elsa-workbench-packages:/app/packages` is the Nuplane directory feed: drop `.nupkg`
  activity/extension packages into that volume to load them.

## Custom `shells.json`: controlling which features are enabled

The server composes itself from `/app/shells.json`. One shell (`default`) declares its features
under `CShells:Shells:default:Features`. **A feature is enabled by being present** (its value is
that feature's configuration object, often just `{}`) **and disabled by being absent** — there is
no `"Enabled": false` convention:

```jsonc
{
  "CShells": {
    "Shells": {
      "default": {
        "Name": "default",
        "Features": {
          "ActivitiesHttp": {},                          // enabled, default config
          "FastEndpoints": { "EndpointRoutePrefix": "" } // enabled, with settings
          // "WeatherForecastSample" absent => disabled
        }
      }
    }
  }
}
```

### Get a starting point

Don't write the file from scratch — extract the image's default (SQLite-backed, seeded demo admin)
and prune it:

```bash
docker run --rm --entrypoint cat elsaworkflows/elsa-workbench:latest /app/shells.json > shells.json
```

A Postgres-flavored curated example lives in the repo at
[`docker/compose/elsa-workbench.shells.json`](../docker/compose/elsa-workbench.shells.json).

> Keep the identity features (`FoundationIdentity*`) and `FastEndpoints` if you want to log in from
> Studio; the seeded admin comes from `FoundationIdentityAspNetCoreIdentityEntityFrameworkCore`.

### Mount it — two modes

Add **one** of these to the `elsa-workbench` run command:

```bash
# Mode A — writable: Studio feature toggles persist into YOUR file
-v "$(pwd)/shells.json:/app/shells.json"

# Mode B — read-only: the file is the single source of truth; Studio toggles are rejected
-v "$(pwd)/shells.json:/app/shells.json:ro"
```

|                                     | Mode A: writable                              | Mode B: read-only (`:ro`)                  |
|-------------------------------------|-----------------------------------------------|--------------------------------------------|
| Toggling features from Studio       | Works; written back to your host file         | Fails (Apply returns HTTP 500)             |
| Editing the file by hand            | Works (hot-reloaded), but see caveats         | Works; hot-reloaded, no restart needed     |
| Survives container recreation       | Yes (state lives in your file)                | Yes (file never changes)                   |
| Best for                            | Interactive demos, exploring features         | Config-as-code, reviewable/shared setups   |

Mode A caveats: the server **rewrites and normalizes** the file on every apply — formatting and
comments are lost and disabled features are removed, so don't hand-edit it while the server is
running (the apply API uses a revision hash; concurrent edits are rejected with HTTP 409). If you
mount nothing at all, Studio toggles land on the container's writable layer: they survive a
`docker restart` but are lost when the container is removed/recreated.

> **Scripting against the API directly?** `POST modularity/features/apply` takes the **full desired
> feature set**, not a delta — the file's features node is replaced by exactly what you send.
> Studio always sends the complete catalog with flipped flags. A hand-rolled request that omits
> currently-enabled features is rejected with HTTP 400 naming the omitted features: include every
> enabled feature — with `enabled: false` to disable it — rather than only the one you're toggling.
> (Images published before this guard silently wiped every omitted feature from the file instead.)

## Deploying workflow definitions from files

With a custom `shells.json` in place you can deploy workflow definitions as files — imported **and
published** (executable) at startup, zero API calls (spec 147). Add a second mount for the
definitions folder and enable the `JsonWorkflowReconciliation` feature:

```bash
-v "$(pwd)/workflow-definitions:/app/workflow-definitions:ro"
```

```jsonc
// inside CShells.Shells.default.Features
"JsonWorkflowReconciliation": {
  "Options": {
    "SourceId": "mounted-definitions",
    "FolderPath": "/app/workflow-definitions",   // top-level *.json, ordinal name order
    "PublishOnReconcile": true                   // omit for import-only
  }
}
```

Each `*.json` file is an array of workflow-version envelopes; **pin `definitionId`** in every envelope
(an omitted id is regenerated per restart, duplicating the definition) and use resolved `actver_*`
activity ids — authoring rules and the id recipe live in the
[feature README](../src/Elsa/Workflows/Design/Reconciliation/Json/README.md).

> **Readiness:** deployment completes during shell activation. Wait on **`GET /health/ready`**
> (200 = every definition imported and, with `PublishOnReconcile`, published). `GET /` returns 200
> before deployment finishes and is not a gate. A misconfigured feature (bad path, two path options)
> keeps `/health/ready` at 503 with `shell_activation_failed` — check the logs.

Restarts with unchanged files are no-ops: no duplicate definition versions, no duplicate publications.

## Configuration precedence

All configuration layers merge; later layers win:

1. `appsettings.json` (baked into the image)
2. `shells.json` (your mount)
3. `shells.{Environment}.json` (e.g. `shells.Production.json`, if present)
4. **Environment variables** (`-e ...`)
5. Command-line arguments

Any feature setting can therefore be overridden without touching the file, using `__` as the
section separator:

```bash
-e CShells__Shells__default__Features__GroundworkUnifiedPersistencePostgreSql__ConnectionString="Host=postgres;..."
```

> **Warning:** because env vars sit *above* the file, a `CShells__...` variable silently masks both
> your file edits **and** any toggle made from Studio for that same key. If a toggle "doesn't
> stick", check the container's environment first.

## FAQ: I toggled a feature in Studio — where did the change go?

Studio calls the server's feature-management API, which **rewrites `/app/shells.json` on disk** and
hot-reloads the shell in-process (no restart). So:

- **Writable mount (Mode A):** it's in your mounted `shells.json` on the host, and survives
  recreation.
- **Read-only mount (Mode B):** it didn't go anywhere — the write fails and Studio gets an error.
  Edit the file instead.
- **No mount:** it's on the container's writable layer — gone after `docker rm`.
- **Masked by an env var:** persisted, but invisible — the `CShells__...` variable wins.

Note this is separate from module *package* management (upload/feeds, gated by the
`Elsa__ModuleManagement__ApiKey`), which persists to `/app/appsettings.json` and `/app/packages`.

## Cleanup

```bash
docker rm -f elsa-workbench elsa-studio
docker network rm elsa-demo
docker volume rm elsa-workbench-packages
```
