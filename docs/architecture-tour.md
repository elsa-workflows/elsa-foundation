# Elsa Foundation Architecture Tour

This is the short orientation path. It explains where things are and why they exist without requiring a full constitution read.

## The repo shape

`elsa-foundation` is the transitional Elsa brain:

- `src/` contains foundation libraries and default implementations.
- `tests/` contains focused tests for foundation behavior.
- `specs/` contains Speckit work units and feature plans.
- `.specify/` contains Speckit templates, workflows, extensions, and the two-layer constitution.
- `docs/` contains lookup knowledge: glossary, skills, maps, and reports.
- `.claude/` contains Claude-specific Speckit adapters.

## The architecture in one pass

Elsa is being refactored into modular domains. Each domain exposes contracts through `.Core` libraries, keeps implementations behind those contracts, and composes with other domains through sanctioned patterns instead of direct implementation coupling.

The main patterns to recognize:

- **Feature inheritance:** a feature may build on another feature when the relationship is explicit and stable.
- **Replacement:** a consumer replaces one default implementation of a contract.
- **Contribution:** a consumer adds implementations that a single owner-owned handler aggregates.
- **Events:** one `IEvent` concept, with delivery strategy defining behavior.
- **Startup tasks:** startup-time composition and registry population.
- **Adapters and bridges:** integration code sits above the sides it connects.
- **Provider modules:** provider-specific implementations stay in provider-suffixed packages.

## Workflows.Design and Workflows.Runtime

`Elsa.Workflows.Design.*` owns authoring and persisted workflow definitions. `Elsa.Workflows.Runtime.*` owns execution. Runtime must not depend directly on Design. Published/executable artifacts are the intended boundary between them.

For the detailed worked example, read [seams and bridges](seams.md).

## Extension points

The repo-wide extension-point index is [../EXTENSION_POINTS.md](../EXTENSION_POINTS.md). Per-domain catalogs live beside the owning feature project. They answer: what can be replaced, what can be contributed, and which events are published.

## How to go deeper

- Need a definition? Use [glossary/root.md](glossary/root.md) or [glossary/elsa.md](glossary/elsa.md).
- Need to perform a task? Use [skills/catalog.md](skills/catalog.md).
- Need current gaps? Use [reports/unfinished-work.md](reports/unfinished-work.md).
- Need enforceable gates? Use the two constitution files under `.specify/memory/`.
