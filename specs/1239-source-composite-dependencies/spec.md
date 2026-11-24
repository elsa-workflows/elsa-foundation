# Feature Specification: Source-owned composite activities with dependencies

**Feature Branch**: `agent/claude/3052-source-composite-deps`

**Status**: Approved (2026-11-24, D-1 A and D-2 as recommended)

**Issue**: #5681, part of initiative #3052 (Elsa 3 to Elsa 4 conversion)

**Input**: A composite activity that a reconciliation source owns, such as a JSON activity catalog entry with provider
`elsa.activity-graph`, publishes when it uses other activity versions, the way the same composite publishes from
Studio. Unknown or refused dependencies stay a refusal with a diagnostic.

## Problem

The engine refuses every source-owned composite that uses another activity version. The source-owned publication
bridge adds `activity.source.dependencies-unsupported` whenever the compiled composite has any direct dependency, and
every graph node that is not a local intrinsic is one. In practice no composite from a reconciliation source
publishes. The JSON activity reconciliation README documents the limit: "Author such composites in Studio".

Studio content does not hit this. Studio publication records one dependency edge per occurrence of another activity
version and updates the reverse dependency projection, in the same commit as the publication. The source-owned commit
has no place for edges, so the bridge refuses instead of publishing a composite whose dependencies nobody can see.

**Example.** The Elsa 3 migration CLI (spec 1237) converts reusable S, an Elsa 3 definition used as an activity that
runs a WriteLine and an HTTP call, to an `elsa.activity-graph` composite and writes it to `migrated/activities/`.
A fresh essentials engine loads `migrated/` with JSON activity and workflow reconciliation. At startup it refuses S
with `activity.source.dependencies-unsupported`, and workflow R, which uses S, cannot publish either. Spec 1237's
acceptance check "migrated/ loads in a fresh engine" waits on this.

A second example: reusable T uses S. In `migrated/activities/` T may be listed before S, and the CLR activities S uses
are contributed by another source. T can only compile once S is published, and S only once the CLR activities are.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A source-owned composite that uses CLR activities publishes (Priority: P1)

An operator starts an engine that reconciles a JSON activity catalog holding composite S, which uses two CLR
activities. S publishes at startup and is usable by workflows, exactly as if it had been published from Studio.

**Why this priority**: This is every converted Elsa 3 reusable. Without it the migration journey cannot load its output.

**Independent Test**: Reconcile a source that contributes S and the CLR activities it uses; S is published, with one
dependency edge per occurrence, and its dependencies list shows those CLR activity versions.

**Acceptance Scenarios**:

1. **Given** a source contributing composite S that uses published CLR activity versions A and B, **When**
   reconciliation runs, **Then** S is published, no refusal is recorded for S, and S's dependency edges name A and B
   with their occurrences.
2. **Given** S was published, **When** someone asks which activity versions use A, **Then** S is among them, as a
   Studio-published composite using A would be.
3. **Given** S was published by one node, **When** a second node reconciles the same source, **Then** the second node
   reports S as published and writes nothing new.

### User Story 2 - Composites that use other composites publish in dependency order (Priority: P1)

The source holds composite T, which uses composite S, and S uses CLR activities. The order the sources contribute
these in does not matter: one reconciliation pass publishes all of them.

**Why this priority**: Elsa 3 reusables use other reusables; the CLI cannot be required to know the engine's
contribution order across sources.

**Independent Test**: Reconcile a source that lists T before S; after one pass both are published and T's edge names S.

**Acceptance Scenarios**:

1. **Given** a source listing T before S, **When** one reconciliation pass runs, **Then** S and T are both published and
   no refusal is recorded for either.
2. **Given** a source composite U that uses a composite V published from Studio, **When** reconciliation runs, **Then**
   U publishes with an edge to V.

### User Story 3 - Unknown and refused dependencies stay a refusal (Priority: P2)

A composite that uses an activity version the engine does not have, or a version that was itself refused, is refused
with a diagnostic that names the missing or unusable dependency, and the rest of the pass goes on.

**Why this priority**: Operators must see why a migrated reusable did not load, and nothing partial may be published.

**Independent Test**: Reconcile a composite that uses an unknown version id; it is refused with
`activity.dependency.version-not-found` naming that id, and nothing of it is stored.

**Acceptance Scenarios**:

1. **Given** composite W uses version id X that no source contributes and nothing published, **When** the pass ends,
   **Then** W is refused with a diagnostic naming X, and no publication, edge or template of W is stored.
2. **Given** composite W uses composite Y and Y is refused, **When** the pass ends, **Then** W is refused too, and both
   refusals are recorded.
3. **Given** composites P and Q use each other, **When** the pass ends, **Then** both are refused with a diagnostic, and
   the pass ends.

### Edge Cases

- A dependency that is published but not active (deprecated or retired) refuses the composite, as it does in Studio.
- A dependency in another tenant refuses the composite, as it does in Studio.
- A composite with no dependencies (local intrinsics only) publishes as today.
- A refusal from an earlier pass is cleared once a later pass publishes the version.
- A composite refused only because its dependency was not yet published is not reported as refused if the same pass
  publishes it later.
- Two nodes publishing the same composite at the same time converge on one publication and one set of edges.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Source-owned publication MUST publish provider-backed content that has direct dependencies, instead of
  refusing it with `activity.source.dependencies-unsupported`. That diagnostic code is removed.
- **FR-002**: A source-owned publication with dependencies MUST record one dependency edge per resolved occurrence,
  carrying the same facts the Studio path records (owner and dependency version and template hash, occurrence, parent
  occurrence, child slot and index, node origin, member usage), in the same commit as the publication.
- **FR-003**: The reverse dependency projection MUST include the source-owned composite as the owner of those edges, in
  the same commit, so dependency and where-used views show it like a Studio-published composite.
- **FR-004**: Edge identities of a source-owned publication MUST be derived from the owner version and occurrence, so
  every node publishing the same source content writes identical edges and the existing convergence rule holds.
- **FR-005**: A composite whose dependencies are not all published, active and visible in its tenant MUST be refused
  with the existing dependency diagnostics, which name the dependency; nothing of it is written.
- **FR-006**: One reconciliation pass MUST publish every source-owned version whose dependencies the same pass
  publishes, whatever order the sources contribute them in.
- **FR-007**: A version MUST be recorded and logged as refused only with its final outcome for the pass; a version that
  the pass publishes later carries no refusal for that pass.
- **FR-008**: A dependency cycle among source-owned composites MUST end the pass with every version in the cycle refused.
- **FR-009**: The JSON activity reconciliation README MUST describe the new behavior in place of the limit.

### Key Entities

- **Dependency edge**: one occurrence of an activity version inside a composite version; exists today for Studio
  publications and now also for source-owned ones.
- **Reverse dependency projection**: the snapshot answering "who uses this version"; its owners now include
  source-owned composite versions.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A fresh engine reconciling a catalog of composites that use CLR activities and each other publishes 100% of
  them in one startup pass, whatever their order in the catalog.
- **SC-002**: A source-owned composite and the same composite published from Studio show the same dependencies and the
  same where-used entries.
- **SC-003**: Every composite with an unknown, inactive or refused dependency is refused with a diagnostic naming the
  dependency, and leaves no stored publication, template or edge.
- **SC-004**: Spec 1237's check "migrated/ loads in a fresh engine" is no longer blocked by this refusal.

## Decisions (approved 2026-11-24)

**D-1. How one pass gets dependency order.** S must be published before T compiles, and CLR activities before S.

| Option | What it means | Cost |
|---|---|---|
| A (recommended) | The reconciler retries the versions it refused, in rounds, while a round publishes at least one more. Interim refusals are not recorded; each version's last outcome is. | Generic; no graph knowledge in the reconciler. A refused version compiles up to once per round (n rounds worst case for an n-deep chain). |
| B | Sources must contribute in dependency order (the CLI writes S before T; CLR before JSON). | No engine change, but fragile across sources and undocumented contribution order. |
| C | The reconciler sorts candidates by a provider dependency-discovery hook before publishing. | Correct in one round, but adds a provider-facing extension point to reconciliation. |

Recommendation: **A**. It needs no new extension point and matches FR-006 and FR-007; the extra compile rounds are
bounded and only happen for versions that are refused anyway.

**D-2. Diagnostic code.** `activity.source.dependencies-unsupported` is removed outright (pre-stable, no shim);
refusals use the existing `activity.dependency.*` codes from the template compiler. Recommendation: accept.

## Assumptions

- Dependencies are referenced by exact activity version id, as the graph provider and template compiler do today; this
  spec does not add lookup by type key or version range.
- Only the Entity Framework publication commit exists for source-owned activity publication; it is the one changed.
- Content whose provider key no compiler owns (such as `elsa.clr-activity`) keeps its single-node root and has no
  dependencies.
- Pre-stable: fixtures and hashes are regenerated in place; no compatibility shim for the removed diagnostic code.
- Producing correct version ids in `migrated/` is spec 1237's concern, not this spec's.
