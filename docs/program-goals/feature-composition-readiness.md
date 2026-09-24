# Runtime Composition & Configuration

Status: active.

Area: feature composition / shared persistence / developer and operator configuration.

Steward(s): Joey plus active architects/agents.

Program: [#1959](https://github.com/elsa-workflows/elsa-foundation/issues/1959).

Scheduling view: [Runtime Composition & Configuration, project 51](https://github.com/orgs/elsa-workflows/projects/51).

## Purpose

Help developers and operators compose an Elsa runtime from understandable starting points, configure shared infrastructure once, and inspect the exact result while retaining granular feature control.

This evolves the existing Feature Composition Readiness bucket in place. Its classification and generator-readiness work remains part of the program. GitHub issues hold scope, dependencies, acceptance criteria, and progress; the project is the single scheduling view. This document routes work rather than duplicating the issue backlog.

The [program issue](https://github.com/elsa-workflows/elsa-foundation/issues/1959) captures the initial design investigation. Its expression-count improvements are synthetic evidence, not proof of deployed layouts or usability. The [persistence-boundary report](../reports/runtime-composition/persistence-boundaries.md) grounds the next delivery slice in current module ownership and transaction constraints. The [effective-configuration report](../reports/runtime-composition/effective-configuration.md) defines the proposed runtime/tooling seam and the remaining specification decisions.

## In Scope

- Shared persistence defaults and explicit feature bindings for reviewed consumer/layout sets.
- Versioned starting profiles, flat feature groups, and exact feature editing.
- Shared effective configuration for runtime activation, EF tooling, and developer plan/explain/export workflows.
- Runtime builder UX grounded in representative developer and operator tasks.
- Review/apply, revision, migration prerequisites, and failure recovery for supported compositions.
- Bounded dependency/settings classification, generator readiness, and host-loading/package compatibility evidence needed by those outcomes.

## Out Of Scope

- Implementing the CShells Appsettings Generator before required activations, settings, secrets, and host-loading are classified.
- Treating `src/Apps/Elsa.Server` as canonical shell composition policy.
- Consolidating modules to reduce configuration choices, a generic settings/constraint framework, or arbitrary database splits without evidence.
- Replacing separately owned package-loading, module-layout, OpenIddict, or connection-guard work.
- Broad runtime execution design.
- Broad constitution ratification unrelated to composition/configuration.

## Active Objectives

The initial backlog was published on 2026-09-23. Discovery #1965/#1966 is complete. The generic lifecycle prerequisite [CShells #134](https://github.com/valence-works/cshells/issues/134) is delivered and its published package is verified; #1967 is completing the first persistence specification. Elsa resource-mode implementation has not started. Consult the linked issues/project for current execution state.

1. Discover supported persistence boundaries in [#1965](https://github.com/elsa-workflows/elsa-foundation/issues/1965), then the shared runtime/tooling resolution path in [#1966](https://github.com/elsa-workflows/elsa-foundation/issues/1966). Both discovery leaves are complete; their reports ground the specification.
2. Reconcile the evidence and specify the bounded delivery slice in [#1967](https://github.com/elsa-workflows/elsa-foundation/issues/1967), using both reviewed spike results. Closing a spike with unknowns does not automatically make implementation ready.
3. Deliver the shared persistence path in [#1968](https://github.com/elsa-workflows/elsa-foundation/issues/1968), then a proven diagnostics override in [#1969](https://github.com/elsa-workflows/elsa-foundation/issues/1969). The reviewed specification is published in [PR #1973](https://github.com/elsa-workflows/elsa-foundation/pull/1973). #1968 is the active delivery leaf; #1969 remains blocked until shared-layout acceptance. The CShells preparation and catalog compatibility prerequisites are delivered in preview.158; the runtime resource integration remains in progress.
4. Refine later epic outlines only when their contracts and evidence are concrete. Keep one active integration lane and synchronize issue comments, labels, and project readiness when work changes state.

## Epic Outcomes

| Epic | Initial planning depth |
|---|---|
| [Shared persistence and explicit overrides #1960](https://github.com/elsa-workflows/elsa-foundation/issues/1960) | Two discovery spikes, a specification checkpoint, and two blocked outcome stories |
| [Profiles and feature groups #1961](https://github.com/elsa-workflows/elsa-foundation/issues/1961) | Outcome and unresolved decisions only |
| [Developer plan, explain, and export #1962](https://github.com/elsa-workflows/elsa-foundation/issues/1962) | Outcome and unresolved decisions only |
| [Runtime builder UX #1963](https://github.com/elsa-workflows/elsa-foundation/issues/1963) | Outcome and unresolved decisions only |
| [Apply, evolution, and recovery #1964](https://github.com/elsa-workflows/elsa-foundation/issues/1964) | Outcome and unresolved decisions only |

Existing [connection-guard #1902](https://github.com/elsa-workflows/elsa-foundation/issues/1902), [OpenIddict #1895](https://github.com/elsa-workflows/elsa-foundation/issues/1895), [Secrets migration-policy #1900](https://github.com/elsa-workflows/elsa-foundation/issues/1900), [unknown-feature #1159](https://github.com/elsa-workflows/elsa-foundation/issues/1159), and [package-loading #1145](https://github.com/elsa-workflows/elsa-foundation/issues/1145) issues remain separately owned. Reconcile their current evidence before consuming their guarantees; do not duplicate or reparent them into this program by assumption.

## Linked Surfaces

- [CShells composition evidence](../reports/cshells-composition-evidence.md)
- [Feature dependency map](../maps/feature-dependency-map.md)
- [Feature map](../maps/feature-map.md)
- [Package map](../maps/package-map.md)
- [Skills catalog](../skills/catalog.md)
- [Unfinished work](../reports/unfinished-work.md)

## Current Roadmap Notes

- Start with one bounded Runtime/Design/Publishing persistence composition and establish the physical constraints before promising overrides.
- Use the Feature Composition Explorer before generator implementation; leave unknown, disputed, or inferred activations/settings pending review.
- Before using generated maps as strong evidence, establish freshness with `dotnet run --project tools/maps/Elsa.Maps.Generator -- check`. If it is red or you cannot run it, refresh the relevant map first and review generated findings before continuing. See the [maps index](../maps/README.md#freshness).

## Drift / Review Notes

- Composition readiness should not pull the repo back into broad operating-model cleanup.
- If classification language becomes stable architecture vocabulary, revisit glossary or constitution placement through Source-of-Truth Audit.

## Removal or Completion Conditions

Complete this program when the linked epic outcomes have verified delivery evidence: shared persistence and overrides, reviewed profiles/groups, consistent developer tooling, evaluated builder UX, and supported composition evolution. Reassess or pause it explicitly if product scope changes; classification or research completion alone does not complete the program.
