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

The initial backlog was published on 2026-09-23. The persistence boundary and effective-configuration spikes, the reviewed specification, and the first two delivery stories are complete. [#1960](https://github.com/elsa-workflows/elsa-foundation/issues/1960) closed after [PR #1974](https://github.com/elsa-workflows/elsa-foundation/pull/1974) and [PR #1981](https://github.com/elsa-workflows/elsa-foundation/pull/1981) passed their gates. The [versioned catalog spike #1982](https://github.com/elsa-workflows/elsa-foundation/issues/1982) completed through [PR #1983](https://github.com/elsa-workflows/elsa-foundation/pull/1983); [planner specification #1984](https://github.com/elsa-workflows/elsa-foundation/issues/1984) completed through [PR #1985](https://github.com/elsa-workflows/elsa-foundation/pull/1985). [Pure planner #1986](https://github.com/elsa-workflows/elsa-foundation/issues/1986) completed through [PR #1990](https://github.com/elsa-workflows/elsa-foundation/pull/1990). [Authoring host-evidence spike #1991](https://github.com/elsa-workflows/elsa-foundation/issues/1991) completed through [PR #1992](https://github.com/elsa-workflows/elsa-foundation/pull/1992); [Embedded host-evidence spike #1994](https://github.com/elsa-workflows/elsa-foundation/issues/1994) completed through [PR #1996](https://github.com/elsa-workflows/elsa-foundation/pull/1996); [Worker host-evidence spike #1997](https://github.com/elsa-workflows/elsa-foundation/issues/1997) completed through [PR #1998](https://github.com/elsa-workflows/elsa-foundation/pull/1998). These are host evidence for candidate fixtures, not released profiles. The [developer plan contract spike #1999](https://github.com/elsa-workflows/elsa-foundation/issues/1999) completed through [PR #2000](https://github.com/elsa-workflows/elsa-foundation/pull/2000), and the [offline command story #2001](https://github.com/elsa-workflows/elsa-foundation/issues/2001) completed through [PR #2002](https://github.com/elsa-workflows/elsa-foundation/pull/2002). The [selected-host evidence spike #2003](https://github.com/elsa-workflows/elsa-foundation/issues/2003) records the boundary for a later host-check story. The [import/export contract spike #2005](https://github.com/elsa-workflows/elsa-foundation/issues/2005) records the boundary for turning existing CShells files into authored intent and generated output while preserving unknown configuration. Consult the linked issues/project for current execution state.

1. Use the delivered shared persistence and diagnostics split as the effective-configuration input. The reviewed specification is in [PR #1973](https://github.com/elsa-workflows/elsa-foundation/pull/1973); [#1968](https://github.com/elsa-workflows/elsa-foundation/issues/1968) and [#1969](https://github.com/elsa-workflows/elsa-foundation/issues/1969) have live runtime/tooling acceptance evidence.
2. Use the bounded pure planner delivered in [#1986](https://github.com/elsa-workflows/elsa-foundation/issues/1986) from [spec 174](../../specs/174-profile-selection-planner/spec.md). The [Authoring proof](../reports/runtime-composition/authoring-host-proof.md) found an implicit Runtime API dependency. The [Embedded proof](../reports/runtime-composition/embedded-host-proof.md) ran a useful non-HTTP host but found Runtime API in its feature closure; [spec 178](../../specs/178-embedded-runtime-profile/spec.md) is the first published starting-profile implementation. The [Worker proof](../reports/runtime-composition/worker-host-proof.md) exercised authorized HTTP execution and resumption with a named SQLite resource, while leaving production identity and broader provider evidence open. Authoring and Worker examples remain planning fixtures rather than released profiles.
3. Use the delivered file-only plan command from [#2001](https://github.com/elsa-workflows/elsa-foundation/issues/2001), which shares selection rules with the future builder and follows the reviewed [contract from #1999](../reports/runtime-composition/developer-plan-command-contract.md). Use the selected-host evidence boundary from [#2003](https://github.com/elsa-workflows/elsa-foundation/issues/2003) before designing a host-check story. The [file-bridge specification](../../specs/176-composition-file-bridge/spec.md) has a delivered import checkpoint [#2019](https://github.com/elsa-workflows/elsa-foundation/issues/2019) and reviewed candidate generation [#2023](https://github.com/elsa-workflows/elsa-foundation/issues/2023). Keep live host checking separate until its trust contract is reviewable. Keep one active integration lane and synchronize issue comments, labels, and project readiness when work changes state.

## Epic Outcomes

| Epic | Initial planning depth |
|---|---|
| [Shared persistence and explicit overrides #1960](https://github.com/elsa-workflows/elsa-foundation/issues/1960) | Complete: reviewed specification, shared-primary story, and diagnostics-override story |
| [Profiles and feature groups #1961](https://github.com/elsa-workflows/elsa-foundation/issues/1961) | Catalog decision [#1982](https://github.com/elsa-workflows/elsa-foundation/issues/1982), planner specification [#1984](https://github.com/elsa-workflows/elsa-foundation/issues/1984), pure planner [#1986](https://github.com/elsa-workflows/elsa-foundation/issues/1986), and Authoring [#1991](https://github.com/elsa-workflows/elsa-foundation/issues/1991), Embedded [#1994](https://github.com/elsa-workflows/elsa-foundation/issues/1994), and Worker [#1997](https://github.com/elsa-workflows/elsa-foundation/issues/1997) host proofs delivered. [#2048](https://github.com/elsa-workflows/elsa-foundation/issues/2048) chose Embedded as the first candidate, [#2050](https://github.com/elsa-workflows/elsa-foundation/issues/2050) proved a deployable filesystem lock member, and [#2053](https://github.com/elsa-workflows/elsa-foundation/issues/2053) established the activation-mapping boundary. [#2052](https://github.com/elsa-workflows/elsa-foundation/issues/2052) implements the first published profile. |
| [Developer plan, explain, and export #1962](https://github.com/elsa-workflows/elsa-foundation/issues/1962) | Offline plan [#2001](https://github.com/elsa-workflows/elsa-foundation/issues/2001) and file-only import/generation [#2019](https://github.com/elsa-workflows/elsa-foundation/issues/2019)/[#2023](https://github.com/elsa-workflows/elsa-foundation/issues/2023) delivered; selected-host evidence [#2003](https://github.com/elsa-workflows/elsa-foundation/issues/2003) remains a boundary, not a live check |
| [Runtime builder UX #1963](https://github.com/elsa-workflows/elsa-foundation/issues/1963) | [#2029](https://github.com/elsa-workflows/elsa-foundation/issues/2029) delivered three non-production mocks and an evaluation protocol; real-user evaluation and production surface remain open |
| [Apply, evolution, and recovery #1964](https://github.com/elsa-workflows/elsa-foundation/issues/1964) | [#2034](https://github.com/elsa-workflows/elsa-foundation/issues/2034) documented the saved-versus-active failure boundary; [#2036](https://github.com/elsa-workflows/elsa-foundation/issues/2036) specified the file-deployed default-shell flow; [#2038](https://github.com/elsa-workflows/elsa-foundation/issues/2038) delivered the file-only safe handoff. [#2039](https://github.com/elsa-workflows/elsa-foundation/issues/2039) found no exact-candidate marker in the current host path; [#2041](https://github.com/elsa-workflows/elsa-foundation/issues/2041) delivers safe default-shell generation/readiness observation with candidate match unverified. [#2042](https://github.com/elsa-workflows/elsa-foundation/issues/2042) investigates the immutable source, package and generation seam before any verified activation claim |

Existing [connection-guard #1902](https://github.com/elsa-workflows/elsa-foundation/issues/1902), [OpenIddict #1895](https://github.com/elsa-workflows/elsa-foundation/issues/1895), [Secrets migration-policy #1900](https://github.com/elsa-workflows/elsa-foundation/issues/1900), [unknown-feature #1159](https://github.com/elsa-workflows/elsa-foundation/issues/1159), and [package-loading #1145](https://github.com/elsa-workflows/elsa-foundation/issues/1145) issues remain separately owned. Reconcile their current evidence before consuming their guarantees; do not duplicate or reparent them into this program by assumption.

## Linked Surfaces

- [CShells composition evidence](../reports/cshells-composition-evidence.md)
- [Authoring fixture host proof](../reports/runtime-composition/authoring-host-proof.md)
- [Embedded fixture host proof](../reports/runtime-composition/embedded-host-proof.md)
- [First starting-profile decision](../reports/runtime-composition/first-profile-decision.md)
- [Embedded starter with deployable filesystem locking](../reports/runtime-composition/embedded-lock-host-proof.md)
- [Activation mapping for the first Embedded profile](../reports/runtime-composition/activation-mapping-boundary.md)
- [Worker fixture host proof](../reports/runtime-composition/worker-host-proof.md)
- [Developer plan command contract](../reports/runtime-composition/developer-plan-command-contract.md)
- [Selected-host evidence investigation](../reports/runtime-composition/selected-host-evidence-boundary.md)
- [Composition import/export investigation](../reports/runtime-composition/import-export-boundary.md)
- [Composition apply/recovery boundary](../reports/runtime-composition/apply-recovery-boundary.md)
- [Source-to-generation attestation investigation](../reports/runtime-composition/source-generation-attestation.md)
- [File-deployed activation specification](../../specs/177-file-deployed-activation/spec.md)
- [Feature dependency map](../maps/feature-dependency-map.md)
- [Feature map](../maps/feature-map.md)
- [Package map](../maps/package-map.md)
- [Skills catalog](../skills/catalog.md)
- [Unfinished work](../reports/unfinished-work.md)

## Current Roadmap Notes

- Keep the supported Runtime/Design/Publishing primary target and two-store diagnostics override as the bounded persistence baseline; other layouts require separate evidence.
- Use the Feature Composition Explorer before generator implementation; leave unknown, disputed, or inferred activations/settings pending review.
- Before using generated maps as strong evidence, establish freshness with `dotnet run --project tools/maps/Elsa.Maps.Generator -- check`. If it is red or you cannot run it, refresh the relevant map first and review generated findings before continuing. See the [maps index](../maps/README.md#freshness).

## Drift / Review Notes

- Composition readiness should not pull the repo back into broad operating-model cleanup.
- If classification language becomes stable architecture vocabulary, revisit glossary or constitution placement through Source-of-Truth Audit.

## Removal or Completion Conditions

Complete this program when the linked epic outcomes have verified delivery evidence: shared persistence and overrides, reviewed profiles/groups, consistent developer tooling, evaluated builder UX, and supported composition evolution. Reassess or pause it explicitly if product scope changes; classification or research completion alone does not complete the program.
