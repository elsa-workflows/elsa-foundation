![Elsa Foundation architectural blueprint header](docs/assets/elsa-foundation-header.png)

# Elsa Foundation

`elsa-foundation` is the transitional Elsa 4 foundation workspace.

It contains the main Elsa domain core libraries, default foundation implementations, the Speckit specification flow, and the architecture knowledge needed to navigate and verify the refactor from `elsa-core`.

## Philosophy

Elsa Foundation should be a thin protocol, not a fat one.

Its job is to define the narrow shared surface that lets modules cooperate: domain language, core contracts, extension points, invariants, and the quality gates needed to keep the system understandable. It should not become a platform-shaped dependency that every feature inherits by default.

Modules should stay opt-in. A shell composes only the capabilities it needs, and features such as persistence, HTTP, JavaScript, scheduling, and host-specific integrations should enter through explicit dependencies and composition. Keeping the foundation thin makes coupling visible, protects extension authors from choices they did not select, and keeps feature-workspace assets movable as Elsa continues the modular refactor.

## Start here

- [AGENTS.md](AGENTS.md) is the provider-neutral entrypoint for AI agents and engineers.
- [docs/README.md](docs/README.md) routes to glossary, skills, maps, reports, and architecture orientation.
- `.specify/memory/constitution-framework.md` and `.specify/memory/constitution.md` are the two-layer constitution and should be treated as quality gates, not as the primary learning path.

## Repository role

This repo currently supports two roles:

- **Feature development:** shipping and testing Elsa foundation features through Speckit specs under `specs/`.
- **Architecture development:** reviewing, revising, and verifying the modular framework constitution and Elsa-specific architecture.

Feature-development workflows remain here for now, but the documentation and operating model are shaped so they can move to a future `elsa-workspace` repository with minimal churn.

## Build

```powershell
dotnet build Elsa.Server.slnx
```

## Supported management APIs

Management-client APIs are owned by their Elsa domains and can be composed directly by custom applications.
`Elsa.Server` is a reference composition, not the implementation home of a server-wide management facade.

The canonical areas are Workflow Design, Activity Design, Expressions, Publishing, Runtime, and
[API Capabilities](src/Elsa/Api/Capabilities/README.md). An authenticated client loads one shell-relative
`GET /capabilities` document, then follows the advertised domain links. Omitted domain features advertise no
capability and expose no routes; endpoint-level action permissions remain authoritative.

See the [domain-owned API specification](specs/091-domain-owned-apis/spec.md),
[OpenAPI contract](specs/091-domain-owned-apis/contracts/management-api.openapi.yaml), and
[validation quickstart](specs/091-domain-owned-apis/quickstart.md) for composition and compatibility details.

## Run with Docker

Fastest way to try the stack — pull the published images and run them, no clone or build:

```bash
curl -O https://raw.githubusercontent.com/elsa-workflows/elsa-foundation/main/docker/compose/docker-compose.images.yml
docker compose -f docker-compose.images.yml up
```

This starts **Elsa.Server** (`http://localhost:13000`) and **Elsa Studio**
(`http://localhost:14000`) from the Docker Hub images `elsaworkflows/elsa-server` and
`elsaworkflows/elsa-studio` (SQLite, ephemeral). The [Docker quickstart](docker/compose/README.md)
covers this plus the build-from-source path with PostgreSQL, and explains the settings that wire
Studio to the server backend (`Studio__BackendBaseUrl`, the Elsa host management key, CORS). For the
full container/image reference (environment variables, mounts, persistence composition,
troubleshooting), see [docs/docker.md](docs/docker.md).
