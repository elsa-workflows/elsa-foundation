# Implementation Plan: First reviewed diagnostics feature group

**Branch**: `codex/2066-diagnostics-group` | **Date**: 2026-09-26 | **Spec**: [spec.md](spec.md)

## Summary

Publish one flat diagnostics group in a second immutable Foundation catalog snapshot. Resolve bundled snapshots by the authored exact catalog pin in planning and candidate generation; let init author the group using the shared planner. The group selects features only and leaves database bindings and live readiness separate.

## Technical Context

**Language/Version**: C# / .NET 10; JSON catalog and authored composition.

**Dependencies**: Existing Modularity Planning catalog, digest reader, pure planner, and `dotnet elsa composition` commands. No new package or storage service.

**Storage**: Two embedded immutable catalog resources. Existing authored JSON remains source-controlled by the developer.

**Testing**: Focused planning and CLI tests for exact membership, v1/v2 pin resolution, group authoring, required removal, and explicit mismatch; architecture, map, and solution-filter gates.

**Constraint**: Preserve original catalog bytes/digest and profile definition. Do not infer resource configuration, package loadability, or host readiness from group selection.

## Constitution Check

- [Framework §2.1–2.2](../../.specify/memory/constitution-framework.md): catalog/planner semantics remain in Modularity; CLI owns files and console.
- [Framework §2.19](../../.specify/memory/constitution-framework.md): exact stable feature IDs identify group members; display categories do not become membership.
- [Framework §2.21, §2.23](../../.specify/memory/constitution-framework.md): pin compatibility and negative dependency paths receive focused tests.
- [Framework §2.25](../../.specify/memory/constitution-framework.md): reuse one planner and loader rather than introduce a parallel expansion path.
- [Elsa §E2, §E5](../../.specify/memory/constitution.md): preserve domain/package boundaries and immutable published selection identity.

Framework §2.24 and Elsa §E2.9 remain draft/provisional and are not used as ratified gates. No exception requested. Recheck after implementation for hidden settings inheritance or a false runtime-ready claim.

## Structure and decisions

The [research](research.md) records the immutable-snapshot and dependency-evidence decisions. [Data model](data-model.md) and the [catalog/CLI contract](contracts/diagnostics-group-v1.md) define the exact IDs, pins, and refusal behavior. [Quickstart](quickstart.md) validates the end-to-end developer path. The existing [spec 174 contract](../174-profile-selection-planner/contracts/selection-planner-v1.md) remains the shared planner format; this unit adds no new plan schema.

Implement in `src/essentials/Modularity/Planning/Catalogs/`, `Catalog/FoundationSelectionCatalog.cs`, and the three `src/essentials/Cli/Composition*Command.cs` consumers. Keep tests in the existing Planning and CLI test projects and documentation in `docs/reference/`. No EF suite or database fixture is added because this work changes selection metadata and CLI pin resolution, not EF behavior.

## Verification boundary

Run the focused Planning and CLI suites, architecture guard, generated-map check, solution-filter check, and diff review. Require exact-head PR checks before merge and post-merge main gates. #1969 remains the live database/target proof; this story does not repeat it as a group-activation claim.
