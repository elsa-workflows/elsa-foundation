# Feature Specification: Activity Semantic Versioning

**Feature Branch**: `004-activity-semantic-versioning` *(spec authored on `main`; no feature branch cut — consistent with units 001–003)*
**Created**: 2026-06-03
**Status**: Draft
**Input**: Unit 3 of the Elsa entity-design refactor. Replace the `int` activity version with an author-controlled **string semantic version** across the activity-definition-version model (Unit B catalog). CLR activities in assemblies carry a `Version` attribute that an assembly-reading reconciliation source reads when seeding the catalog. Prerequisite for Unit 4 (workflow-as-activity). Works against constitution v3.0.0 (draft).

## Clarifications

### Session 2026-06-03

- Q: Migration of existing `int`-versioned rows? → A: **Resolved — no data migration.** Per Unit B's "no preserved production data" convention (FR-015 in `specs/001`), the SQLite initial migration for the activities-design context is regenerated fresh. There is no production int→semver data backfill. (See FR-016, SC-005.)
- Q: Does this unit also re-type the vestigial `int Version` on `ActivityBase` / `IActivity` (`Elsa.Workflows.Runtime.Core`)? → *(pending — see [NEEDS CLARIFICATION] in FR-018)*
- Q: Where does the `Version` attribute type live? → *(pending — see [NEEDS CLARIFICATION] in FR-009)*
- Q: Which semver format is accepted (full SemVer 2.0.0 vs `MAJOR.MINOR.PATCH` only)? → *(pending — see [NEEDS CLARIFICATION] in FR-011)*

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Activity author owns the version via an attribute (Priority: P1)

An activity author writes a CLR activity in an assembly and annotates it with a version attribute, e.g. `[Version("2.1.0")]`. When the host reconciles its activity catalog, the assembly-reading source reads that attribute and the catalog records `2.1.0` as that activity-definition-version's version — exactly as the author wrote it. The author bumps the version when they change behaviour; the system never invents, increments, or reinterprets the number.

**Why this priority**: This is the load-bearing reason Unit 3 exists. Today the catalog version is a system-assigned `int` with no author intent. Author-controlled semver is the prerequisite for a consuming workflow to *pin* a meaningful activity version (Unit 4). Every other requirement is downstream of moving version ownership to the author.

**Independent Test**: Annotate a test activity type with a version attribute, run the assembly-reading source through the reconciler, and assert the persisted `ActivityDefinitionVersion.Version` equals the attribute's string value verbatim.

**Acceptance Scenarios**:

1. **Given** a CLR activity annotated `[Version("2.1.0")]`, **When** the assembly source is read during reconciliation, **Then** a catalog version row is created with `Version = "2.1.0"`.
2. **Given** the same activity with the same attribute value, **When** reconciliation runs again, **Then** no new version row is created (idempotent — the existing `(DefinitionId, Version)` is observed and skipped per Model X).
3. **Given** an author changes the activity's content **and** bumps the attribute to `[Version("2.2.0")]`, **When** reconciliation runs, **Then** a new version row `2.2.0` is appended alongside the retained `2.1.0`.

---

### User Story 2 — The catalog stores and exposes versions as semver strings end-to-end (Priority: P1)

Every surface that previously carried an `int` activity version now carries a string semver: the persisted entity, the design-domain read contract, the listing/projection records, the reconciliation contribution model, and the design-time API surface. A consumer reading an activity version through any of these surfaces sees the author's semver string.

**Why this priority**: The type change is the structural substrate of the unit. US1 (author attribute) and US3 (ordering) cannot land without the model carrying a string version coherently across all layers.

**Independent Test**: Read an activity version back through `IActivityDefinitionVersion`, the `ListVersions` projection, and the API details view; confirm each exposes the string semver and that no `int`-typed version member remains on these surfaces.

**Acceptance Scenarios**:

1. **Given** a persisted version row, **When** it is read through `IActivityDefinitionVersion`, **Then** `Version` is the author's semver string.
2. **Given** a definition with several versions, **When** the listing projection is queried, **Then** each entry's version is the semver string.
3. **Given** the design-time API "add version" / "get version" / "list versions" surface, **When** a client interacts with it, **Then** versions are represented as semver strings (no integer version field remains).

---

### User Story 3 — Versions are compared and ordered by semver precedence (Priority: P1)

When the system lists an activity's versions newest-first, or resolves "the latest version", it orders them by **semantic-version precedence**, not by lexical string order and not by a numeric counter. `10.0.0` is newer than `2.0.0`; `1.2.0` is newer than `1.10.0`'s predecessor `1.2.0`… i.e. numeric-segment precedence, not character order.

**Why this priority**: A naïve string sort silently returns the wrong "latest" (`"10.0.0" < "2.0.0"` lexically). Pickers, listings, and Unit 4's version resolution all depend on correct ordering; getting it wrong is a silent-wrong-answer class of bug, not a crash. It must be correct from the first commit of the new model.

**Independent Test**: Persist versions `1.0.0`, `2.0.0`, `10.0.0`, `1.2.0` for one definition; query the ordered listing; assert the order follows semver precedence (`10.0.0`, `2.0.0`, `1.2.0`, `1.0.0`), not lexical order.

**Acceptance Scenarios**:

1. **Given** versions `2.0.0` and `10.0.0`, **When** ordered descending, **Then** `10.0.0` precedes `2.0.0`.
2. **Given** a request for the latest version of a definition, **When** resolved, **Then** the highest-precedence semver is returned.
3. **Given** versions differing only in patch (`1.0.1`, `1.0.10`, `1.0.2`), **When** ordered, **Then** `1.0.10` is highest (numeric patch precedence).

---

### User Story 4 — A consumer can resolve an exact activity version (Priority: P2)

A consumer (a workflow definition, or the runtime resolver) looks up an activity-definition-version by its definition identity plus an exact semver string and gets that specific record. This is the seam Unit 4 builds on: a consuming workflow pins, e.g., `Elsa.Http.SendRequest` at `2.1.0`.

**Why this priority**: Unit 4 ships the actual consumer; this unit only guarantees the lookup-by-exact-version works against the new string-typed model. P2 because no Unit-3 user-visible behaviour depends on it, but the model must support it now so Unit 4 is unblocked.

**Independent Test**: Persist two versions of one definition; resolve by `(DefinitionId, "2.1.0")`; assert the correct record returns and a non-existent version returns no match.

**Acceptance Scenarios**:

1. **Given** a definition with versions `2.0.0` and `2.1.0`, **When** resolved by `(DefinitionId, "2.1.0")`, **Then** the `2.1.0` record is returned.
2. **Given** the same definition, **When** resolved by `(DefinitionId, "9.9.9")`, **Then** no record matches (lookup is exact, not nearest).

---

### User Story 5 — A mis-versioned source fails loudly, not silently (Priority: P2)

If an author changes an activity's content but forgets to bump its version attribute, reconciliation observes the same `(DefinitionId, Version)` with a different content hash and throws `ActivityVersionHashMismatchException` (Model X safety net). The catalog is never silently overwritten.

**Why this priority**: Moving version ownership to the author introduces a new, common human error (forgetting to bump). The existing Model X hash-mismatch path is exactly the right guard, but its trigger shifts from "system bug" to "author oversight", so it must be preserved and re-pointed at string versions. P2: it protects integrity rather than delivering new capability.

**Independent Test**: Reconcile an activity at `1.0.0`; mutate its content (changing the hash) without changing the attribute; reconcile again; assert `ActivityVersionHashMismatchException` is raised carrying the string version.

**Acceptance Scenarios**:

1. **Given** a persisted `(DefinitionId, "1.0.0")` with hash H1, **When** a source contributes `(DefinitionId, "1.0.0")` with a different hash H2, **Then** `ActivityVersionHashMismatchException` is thrown reporting version `"1.0.0"`.
2. **Given** the same `(DefinitionId, "1.0.0")` contributed with the matching hash H1, **When** reconciliation runs, **Then** the duplicate is skipped or thrown per the configured duplicate-handling option (unchanged from Model X).

---

### Edge Cases

- **An activity assembly type carries no version attribute.** Reconciliation behaviour is governed by FR-012 (default vs required). The decision affects whether un-annotated activities silently default or refuse to reconcile.
- **The attribute carries a string that is not a valid semver** (per the accepted format, FR-011). Reconciliation translates this into a domain-scoped exception with the offending activity type + value (framework §2.23.5 — no raw parse exception escapes the source boundary).
- **Pre-release and build-metadata semantics** (`1.0.0-alpha`, `1.0.0+build.5`). In/out of scope and their precedence/equality behaviour follow the accepted format (FR-011). Build metadata, if accepted, MUST be ignored for precedence and for `(DefinitionId, Version)` equality per the SemVer spec.
- **Two version strings that are precedence-equal but not byte-equal** (e.g. `1.0.0` vs `1.0.0+build`). Equality lookup by `(DefinitionId, Version)` MUST treat them as the same logical version (precedence equality), or the format is restricted so this case cannot arise (FR-011).
- **The vestigial `int Version` on `ActivityBase` / `IActivity`** (`Elsa.Workflows.Runtime.Core`) is a separate concern from the catalog definition-version. Its disposition is FR-018.
- **Elsa 3 import** carries `int` activity versions. The import adapter must map an Elsa-3 `int` version onto a semver string (FR-007).

## Requirements *(mandatory)*

### Functional Requirements

**Model: int → string semver across all surfaces**

- **FR-001**: `IActivityDefinitionVersion.Version` (read contract, `Elsa.Activities.Design.Core`) MUST change from `int` to `string` (an author-controlled semver). The member remains read-only.
- **FR-002**: `ActivityDefinitionVersion.Version` (entity, `Elsa.Activities.Design.Persistence.Core`) MUST change from `int` to `string`, retaining the `[Immutable]` enforcement and the constructor-supplied initialisation. The persisted column type is a string.
- **FR-003**: `ActivityDefinitionVersionInfo` (projection record, `Elsa.Activities.Design.Core.Models`) MUST carry `string Version`.
- **FR-004**: `ActivityVersionReconciliationModel` (contribution model, `Elsa.Activities.Design.Reconciliation.Models`) MUST carry `string Version`.
- **FR-005**: The design-time API surface — the `AddVersion` command + endpoint, the version details view(s), and the `ListVersions` request/handler/projection — MUST represent the version as a semver string. No integer version field may remain on any request, response, or view model.
- **FR-006**: `ActivityVersionHashMismatchException` MUST accept and report the version as a `string` (currently `int`).
- **FR-007**: The Elsa 3 import adapter (`Elsa3.Activities.Design.Import` — `ActivityDefinitionVersionImport`, `ImportActivityVersions`, and the `Elsa3ActivityToState` mapping) MUST map an Elsa-3 `int` activity version onto a semver string. The default mapping is `n` → `"n.0.0"` (documented in Assumptions); a different mapping is an import-adapter decision, not a catalog-model decision.

**Ordering & comparison**

- **FR-008**: Listing and "latest version" resolution MUST order activity versions by **semantic-version precedence** (numeric-segment precedence per the accepted format, FR-011), not lexical string order and not an integer counter. `ActivityVersionOrderDefinition` (today `OrderDefinition<ActivityDefinitionVersion, int>` sorting by descending `int`) MUST be reworked so its output is semver-precedence-ordered. **Risk to resolve at plan stage:** EF Core applies `OrderDefinition` to an `IQueryable` (DB-side); a plain string column does not sort by semver precedence in SQL. The plan MUST choose a mechanism (e.g. a persisted normalised sortable representation alongside the human-readable string, or an in-memory comparison after materialisation) and justify it against query-shape and large-catalog implications. No new constitutional pattern is introduced; a comparison/comparer is standard (framework §2.24.2 — *Strategy*/comparator are sanctioned).

**The version attribute & the assembly-reading source**

- **FR-009**: A new `Version` attribute type MUST be introduced, applicable to a CLR activity type, carrying the author's semver string. [NEEDS CLARIFICATION: attribute home project — (A) `Elsa.Workflows.Runtime.Core` alongside `ActivityBase`/`IActivity`, which activity-author assemblies already reference (zero new dependency for authors); (B) `Elsa.Activities.Design.Core` (zero-dep, where `ClrImplementationDescriptor` and the read contracts live, but design-side); (C) a new zero-dep shared package (e.g. `Elsa.Activities.Core`) referenced by both author assemblies and the reconciliation source.]
- **FR-010**: A new assembly-reading `IActivityReconciliationSource` MUST be introduced (in `Elsa.Activities.Design.Reconciliation`, the home of the existing source pattern) that scans the configured activity assemblies, reads each activity type's `Version` attribute, and contributes one `ActivityVersionReconciliationModel` per discovered activity with `Version` set to the attribute's string value and `SourceKind` set to the CLR/assembly source kind. This fits the existing Unit B `IActivityReconciliationSource` contract (`Read` + `SourceId` + `SourceKind`); the existing single aggregating `ActivityVersionsReconcilingHandler` consumes it unchanged. *(No assembly-reading reconciliation source exists today; the only contributed source path is JSON. `ClrImplementationDescriptorSource` is an `IImplementationDescriptorSource` — kind→type registration — not a reconciliation source.)*
- **FR-011**: The accepted semver format MUST be defined. [NEEDS CLARIFICATION: (A) full **SemVer 2.0.0** — `MAJOR.MINOR.PATCH` with optional `-prerelease` and `+buildmetadata`, with precedence and equality per the SemVer spec (prerelease lowers precedence; build metadata ignored for precedence/equality); (B) **`MAJOR.MINOR.PATCH` only** — three numeric segments, no prerelease/build, simpler validation and ordering.] An attribute value that does not conform MUST cause reconciliation to fail with a domain-scoped exception carrying the activity type + offending value (framework §2.23.5); a raw parse exception MUST NOT escape the source.
- **FR-012**: The behaviour for an activity type with **no** `Version` attribute MUST be defined: either a default version is assigned, or the activity is refused with a domain-scoped diagnostic. *(Assumption pending review: default to `"1.0.0"` for a missing attribute so existing un-annotated activities reconcile — documented in Assumptions; an architect may instead require explicit annotation.)*

**Reconciliation & lookup semantics**

- **FR-013**: The reconciler's lookup by `(DefinitionId, Version)` MUST operate on the string version. Exact-version equality MUST follow the accepted format's equality rule (FR-011) — i.e. precedence-equality where the format admits non-byte-equal equivalents. The Model X find-or-create / append / hash-mismatch flow (`ActivityVersionReconciler`) is otherwise unchanged: absent → create; present + hash differs → throw `ActivityVersionHashMismatchException`; present + hash matches → skip-or-throw per option.
- **FR-014**: Exactly one `ActivityDefinitionVersion` record MUST exist per `(DefinitionId, semver)` — record-level identity is preserved (each semver is its own uniquely-locatable row), as in Unit B.

**Out-of-scope re-typing**

- **FR-018**: The disposition of the vestigial `int Version` on `ActivityBase` / `IActivity` (`Elsa.Workflows.Runtime.Core`) MUST be decided. [NEEDS CLARIFICATION: (A) **out of scope** — leave the `int Version` on the runtime activity instance untouched; this unit changes only the *definition-version catalog* model, and the runtime instance version is a separate (and per the code comment, possibly vestigial) concern to be addressed later; (B) **in scope** — re-type or remove it now so "activity version" has a single semver meaning across design and runtime.] *(Default pending review: (A) out of scope, flagged for a later runtime unit — consistent with the follow-up's scope, which names the activity-definition-version model only.)*

**Tests, docs & constitution (in-unit)**

- **FR-015**: A fresh SQLite migration MUST replace the existing activities-design initial migration to reflect the string version column. No production int→semver data migration path is required (Unit B "no preserved production data" convention).
- **FR-016**: The constitution MUST be updated in-unit: Elsa **§E2.8** — state that the activity version is an author-annotated **string semver** read from the activity's `Version` attribute by the assembly reconciliation source; and any reconciliation wording that references `int` versions or `(DefinitionId, Version)` integer lookup MUST be reworded for string semver. *(Note: §E2.8 currently also says `ProvisioningHash`; the code uses `ReconcilliationHash` on the version row. Aligning that stale term is a pre-existing drift, optionally tidied in the same edit but not caused by this unit.)*
- **FR-017**: Every reshaped feature class MUST retain its framework §2.23.1 registration test; every logic-bearing reshaped class (the new assembly source, the reworked order definition / comparer, the reconciler changes) MUST be covered by §2.23.2 branch-covered unit tests; existing tests on refactored implementations MUST continue to pass without changes to the test cases themselves (framework §2.21.1 golden rule). The `001` read-contract surface pin (`ReadContractSurfaceTests`) MUST be updated to assert `Version` is `string`.
- **FR-019**: A Unit 3 follow-up file (`epic1-elsa-refactor-constitution/follow-up-items/2026-06-02_unit_activity_semantic_versioning.md`) MUST be updated to reflect resolution, and `PERSONAL_TODO.md` updated to reflect Unit 3 status.

### Key Entities

- **`ActivityDefinitionVersion`** — the catalog version row. Its `Version` becomes an author-controlled semver **string** (was `int`), immutable. One row per `(DefinitionId, semver)`. Carries the immutable `ReconcilliationHash` (Model X) used to detect a mis-versioned source.
- **`Version` attribute** — new metadata annotation on a CLR activity type carrying the author's semver string. Read by the assembly reconciliation source. Home project pending (FR-009).
- **Assembly-reading reconciliation source** — new `IActivityReconciliationSource` implementation that scans activity assemblies, reads the `Version` attribute, and contributes version models. Fits the Unit B source pattern; consumed by the existing aggregating handler.
- **Semver value & ordering** — the accepted semver format (FR-011) and the precedence ordering it induces (FR-008). The version string is the human-readable identity; a precedence ordering (and possibly a normalised sortable representation) supports "latest"/listing queries.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of the time, the version string an author writes in the attribute is the version string persisted in the catalog — no system reinterpretation, increment, or normalisation that changes the author's value (build-metadata stripping per FR-011 excepted and documented).
- **SC-002**: Version listings and "latest version" resolution return correct semver-precedence order in 100% of cases, including multi-digit segments (`10.0.0` before `2.0.0`) — verified by a test covering the lexical-vs-precedence divergence.
- **SC-003**: Re-running reconciliation against unchanged sources produces zero new version records (idempotent), proving the string-version `(DefinitionId, Version)` identity is stable.
- **SC-004**: An author who changes activity content without bumping the version is stopped by a loud failure (hash-mismatch), never a silent catalog overwrite — verified by a dedicated test.
- **SC-005**: All pre-existing activity-catalog and reconciliation tests pass unchanged in subject and objective (framework §2.21.1); the build is green with no `int`-typed version member remaining on the catalog model, read contract, projections, reconciliation model, or API surface.
- **SC-006**: A consumer can resolve an activity-definition-version by `(DefinitionId, exact semver string)`, returning the one matching record or none — the lookup Unit 4 depends on.

## Assumptions

- **No production data.** The activities-design schema is regenerated fresh; there is no int→semver backfill (Unit B convention). Any existing local SQLite store is recreated.
- **Elsa-3 import mapping default.** An Elsa-3 `int` version `n` maps to `"n.0.0"` unless the import adapter specifies otherwise (FR-007).
- **Missing-attribute default.** Pending architect confirmation (FR-012), an activity with no `Version` attribute defaults to `"1.0.0"` rather than being refused, so currently un-annotated activities continue to reconcile.
- **Equality semantics follow the chosen format.** If full SemVer 2.0.0 is chosen (FR-011), build metadata is ignored for precedence and `(DefinitionId, Version)` equality, per the SemVer spec.
- **The runtime instance `int Version` is a separate axis.** This spec assumes the catalog definition-version is the subject; the `ActivityBase`/`IActivity` `int Version` is treated as out of scope pending FR-018.
- **Source-pattern reuse.** The assembly-reading source plugs into the existing `IActivityReconciliationSource` + single-aggregating-handler shape from Unit B; no new contribution pattern is introduced.

## Constitutional Compliance

This spec is implemented against the two-layer constitution at `.specify/memory/constitution.md` (Elsa) and `.specify/memory/constitution-framework.md` (framework). Compliance is enforced at the plan stage via the *Constitution Check* gates — not duplicated here. Spec-level constitutional notes / flags:

- **§E2.8 is amended in-unit** (FR-016): the activity version is an author-annotated string semver read from the `Version` attribute by the assembly reconciliation source. This is an extension of Unit B's catalog identity model, not a new structural pattern.
- **Sanctioned-patterns check (framework §2.24.2).** The assembly-reading source reuses the already-sanctioned `IActivityReconciliationSource` + single-aggregating-handler pattern (Unit B / §2.6.1). Semver precedence ordering is a comparator/Strategy (sanctioned). Reading a `Version` attribute via reflection inside a source's `Read()` is an implementation detail of an existing pattern, not a new pattern. **No §2.24.3 gate is triggered.**
- **Flag — ordering mechanism (FR-008).** If the plan chooses a *persisted normalised sortable representation* of the semver, that is a new persistence-only field on the version row; it must respect framework §2.9.1 (real CLR property, not an EF shadow, if it bears invariant semantics) and the §E2.8 Model X immutability of version rows. Surface this in the plan's Constitution Check.
- **Flag — open clarifications.** FR-009 (attribute home), FR-011 (semver format), and FR-018 (runtime `int Version` disposition) are open architectural decisions. Per the working loop, these are candidates for the next architecture touchpoint if not resolved at `/speckit-clarify`.
