# Implementation Plan: First Embedded runtime starting profile

**Branch**: `codex/2052-embedded-runtime-profile` | **Date**: 2026-09-25 | **Spec**: [spec.md](spec.md)

**Input**: [Story #2052](https://github.com/elsa-workflows/elsa-foundation/issues/2052) and the [activation-mapping spike](../../docs/reports/runtime-composition/activation-mapping-boundary.md).

## Summary

Publish `embedded-runtime@1` as an immutable 16-member Foundation catalog and let the existing planner/CLI produce an exact, reviewable selection. Extend the file bridge only for proven object-map activation edits in one selected environment, refuse known missing required edges before writing, and re-read the candidate to verify exact IDs. Keep persistence, locking and signing values host-owned. The generic-host fixture supplies the positive activation proof; this does not assert live Workbench candidate attestation.

## Technical Context

**Language/Version**: C# / .NET 10 and JSON host configuration.

**Primary Dependencies**: Existing Modularity Planning library, CLI `composition plan`/`composition generate`, pinned CShells `0.0.30-preview.159`, shared-persistence contract. No new package.

**Storage**: Foundation-owned catalog JSON and fresh file-candidate directory. No live profile database.

**Testing**: Planning/CLI contract tests, pinned CShells source characterization, generic-host Embedded EF fixture; architecture guard, generated maps and solution-filter check.

**Target Platform**: Offline developer CLI, selected CShells shell/environment, generic .NET host acceptance example.

**Project Type**: Pure planner plus file-only CLI adapter; no browser/live apply endpoint.

**Performance Goals**: Deterministic local planning and bounded candidate readback; no new runtime performance claim.

**Constraints**: Exact 16-ID set, immutable digest, no secrets in portable artifacts, no published file on invalid selection, preserve unrelated files, no runtime-ready claim from static files.

**Scale/Scope**: One profile version, one selected shell/environment, object-map sources, SQLite/local file locking. Other profiles, arrays, setting insertion, package delivery and live deployment are separate.

## Constitution Check

- [Framework §2.1–2.2](../../.specify/memory/constitution-framework.md): planner/catalog semantics stay in Modularity; CLI owns filesystem/console work.
- [Framework §2.7](../../.specify/memory/constitution-framework.md): preserve CShells activation while validating authored intent before it can auto-resolve a removed dependency.
- [Framework §2.19](../../.specify/memory/constitution-framework.md): stable explicit IDs, reviewed dependency rationale and exact selection; no new settings layer.
- [Framework §2.21, §2.23](../../.specify/memory/constitution-framework.md): verify refusal, preservation, readback equality, redaction and real host execution.
- [Framework §3, §4.2](../../.specify/memory/constitution-framework.md): distinguish file candidate from package/runtime compatibility and activation; no silent upgrade.
- [Elsa §E2, §E5](../../.specify/memory/constitution.md): Workbench/generic fixtures are evidence targets, not a new product host.

Framework §2.24 and Elsa §E2.9 remain draft/provisional and are not ratified here. Framework §2.12/Elsa §E4 remain deferred. No exception requested. Rechecked after design: no reverse dependency, global settings inheritance or host-ready claim.

## Project Structure

```text
specs/178-embedded-runtime-profile/
  spec.md plan.md research.md data-model.md quickstart.md tasks.md
  contracts/embedded-profile-v1.md checklists/requirements.md
src/essentials/Modularity/Planning/
  Models/ Services/SelectionPlanner.cs
  Bridge/CompositionCandidateBuilder.cs Bridge/CshellsSourceReader.cs
src/essentials/Cli/
  CompositionPlanCommand.cs CompositionGenerateCommand.cs
tests/essentials/Modularity/Planning/Tests/
  HostAssessmentTests.cs CompositionBridgeSourceTests.cs
tests/essentials/Cli/Tests/CompositionGenerateCliTests.cs
tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/
  EmbeddedFixtureHostEvidenceTests.cs
```

**Structure decision**: Reuse the planner and bridge rather than add an orchestrator or EF test project. The catalog is reviewed developer data, not a runtime registry. The CLI and later UI consume one plan.

## Phase 0: Research

[Research](research.md) records proven membership, source semantics, dependency-evidence gap and host prerequisites.

## Phase 1: Design and contracts

[Data model](data-model.md), [v1 contract](contracts/embedded-profile-v1.md) and [quickstart](quickstart.md) define selection, candidate refusals and validation.

## Delivery and verification boundary

Implement the profile/planner finding, then reviewed activation mapping and readback, then CLI/test/docs integration. Run affected tests and architecture/maps/solution-filter gates before PR, exact-head CI before merge and main CI/Maps after. Deployment and live candidate attribution belong to spec 177.
