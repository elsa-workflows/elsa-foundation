# elsa-foundation

`elsa-foundation` is the transitional Elsa brain and foundation-library workspace.

It contains the main Elsa domain core libraries, default foundation implementations, the Speckit specification flow, and the architecture knowledge needed to navigate and verify the refactor from `elsa-core`.

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
