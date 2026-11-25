# Implementation Plan: Source-owned composite activities with dependencies

**Branch**: `agent/claude/3052-source-composite-deps` | **Spec**: [spec.md](spec.md) | **Issue**: #5681, part of #3052

## Summary

Remove the `activity.source.dependencies-unsupported` refusal and give the source-owned commit the dependency edges the
Studio commit already writes. The publisher builds edges from the compiler's resolved dependencies with ids derived
from the owner version and occurrence; the EF design commit stores them and updates the reverse dependency projection in
the same transaction; the reconciler retries refused versions in rounds until a round publishes nothing more (D-1 A),
recording only each version's final outcome. The JSON reconciliation README describes the new behavior.

## Technical Context

**Language/Version**: C# / .NET 10.

**Primary Dependencies**: none new.

**Storage**: Activities Design EF stores: `ActivityDependencyEdges` and the dependency projection, both existing tables.
No schema change, so no migration.

**Testing**: xunit only, scoped to the three changed areas (see Test scope).

**Project Type**: engine fix inside existing services.

**Constraints**: pre-stable, no shim for the removed code; no new extension point or pattern.

## Design

### 1. Source-owned commit carries edges

`SourceActivityPublicationCommit<,>` (Activities Design Persistence Core) gains
`IReadOnlyList<ActivityDependencyEdge> DirectDependencies`, defaulting to empty, the same type the draft mutation uses.

### 2. Publisher builds edges instead of refusing

`SourceOwnedActivityVersionPublisher`:

- drops the `DirectDependencies.Count > 0` refusal;
- maps `Compilation.DirectDependencies` to `ActivityDependencyEdge` with the same field mapping as
  `ActivityDefinitionPublisher` (owner version and template hash, dependency version and template hash, occurrence,
  parent occurrence, child slot and index, node origin, member usage);
- derives each edge id as `StableId("activity-edge", ownerVersionId + U+001F + occurrenceId)`, so nodes converge (FR-004);
- passes the edges into the commit. The CLR single-node path passes none.

Unknown, inactive and foreign-tenant dependencies are already refused by `ActivityTemplateCompiler` with
`activity.dependency.*` diagnostics that carry the dependency (FR-005); no new check is needed.

### 3. EF design commit stores edges and the projection

`EfActivityPublicationDesignCommit`:

- source admissibility check refuses an existing edge id, like the draft path (`EnsureAbsentAsync` per edge);
- source staging adds the edges and stages the projection with owner `ActivityVersion` (definition, version, template
  hash, lifecycle of the publication), replacing items of that same owner;
- the projection staging is factored so the draft path (owner `ActivityDraft`, replaced by the draft reference) and the
  source path share the edge-to-item mapping;
- `EfSourceActivityPublicationCommand.Validate` also requires every edge's tenant and owner version to match the
  publication.

A lost race to the same publication still surfaces as `ActivityVersionAlreadyPublishedException` before any edge is
written, and the publisher's identical-publication check is unchanged.

### 4. Reconciler rounds (D-1 A)

`ActivityVersionReconciler.Reconcile`, only when a source-version publisher is registered:

- round 1 is today's ordered loop, except a refusal is held, not recorded or logged;
- each further round re-runs `ReconcileVersion` for the versions refused in the previous round, in their original order,
  against the updated indexes;
- the loop stops when a round publishes nothing; then each still-refused version's last diagnostics are recorded and
  logged, and the end-of-pass clear runs as today.

Throws (hash mismatch, identity collision) keep firing in round 1 at the same candidate. Without a publisher, behavior is
unchanged. A cycle never publishes, so the round after the first adds nothing and the pass ends (FR-008).

### 5. Docs

`Activities/Design/Reconciliation/Json/README.md` replaces the limit paragraph: composites with dependencies publish,
in any order within a pass, and unknown or refused dependencies refuse with the dependency named. The publisher's class
and method docs drop the "commits no dependency edges" sentence. `EXTENSION_POINTS.md` is unchanged (no extension
surface changes).

## Constitution Check

| Gate | Status | Notes |
|---|---|---|
| §2.24.2 sanctioned patterns | PASS | No new pattern: changes stay inside the existing publisher bridge, EF commit and reconciler. The reconciliation pattern (abstract base feature + `IXxxReconciliationSource`) is untouched. |
| §E2.2 Runtime takes no Design reference | PASS | Only Design persistence, Publishing API and Design reconciliation change. |
| §2.23 risk-based tests | PASS | Every changed branch gets a unit or integration test (Test scope). |
| §2.22 feature documentation | PASS | README updated; no feature or extension point added. |
| ADR 0080 internal by default | PASS | One public record gains an optional member; no new public type. |
| Pre-stable compatibility | PASS | Diagnostic code removed outright; no stored format changes. |

## Test scope

Run only these after each change or review round:

- `essentials/tests/Workflows/Publishing/Api/Tests` — `SourceOwnedActivityVersionPublisherTests` (edges built, ids
  stable, no refusal; the existing dependencies-unsupported test becomes a publish test).
- `essentials/tests/Workflows/Publishing/Persistence/EntityFrameworkCore/Tests` — `SourceOwnedProviderPublicationPassTests`
  plus a new pass test: graph composites T → S → CLR, T listed first, all published with edges and projection; an
  unknown dependency refused with nothing stored; a cycle refused.
- `essentials/tests/Activities/Design/Tests` — `SourceVersionRefusalTests` (rounds, final-outcome recording, cleared
  refusal) and the reconciler harness tests.
- `essentials/tests/Activities/Design/Persistence/EntityFrameworkCore/Tests` — EF commit: edges and projection written,
  existing edge refused.

## Project Structure

```text
essentials/src/Activities/Design/Persistence/Core/Contracts/ReusableActivityCommands.cs
essentials/src/Activities/Design/Persistence/EntityFrameworkCore/Stores/EfActivityPublicationDesignCommit.cs
essentials/src/Activities/Design/Reconciliation/Elsa.Activities.Design.Reconciliation/Services/ActivityVersionReconciler.cs
essentials/src/Activities/Design/Reconciliation/Json/README.md
essentials/src/Workflows/Publishing/Api/Services/SourceOwnedActivityVersionPublisher.cs
essentials/src/Workflows/Publishing/Persistence/EntityFrameworkCore/Services/EfSourceActivityPublicationCommand.cs
```
