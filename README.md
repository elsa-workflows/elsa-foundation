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

## Backend capabilities

Enabled CShell features can publish stable capability IDs such as `elsa.workflows.design` and `elsa.workflows.runtime`. The `CapabilitiesApi` shell feature exposes the active shell capability set for Studio module discovery:

```http
GET /default/_elsa/capabilities
```

The `/default` segment comes from the sample shell's `WebRouting:Path`; hosts with a different shell path should use that path instead.
