# Feature Specification: Activity Semantic Versioning

**Feature Branch**: `004-activity-semantic-versioning` *(spec authored on `main`; no feature branch cut — consistent with units 001–003)*
**Created**: 2026-06-03
**Status**: Draft
**Input**: Unit 3 of the Elsa entity-design refactor. Replace the `int` activity version with an author-controlled **string semantic version** across the activity-definition-version model (Unit B catalog). CLR activities in assemblies carry a `Version` attribute that an assembly-reading reconciliation source reads when seeding the catalog. Prerequisite for Unit 4 (workflow-as-activity). Works against constitution v3.0.0 (draft).

## Clarifications

### Session 2026-06-03

- Q: Migration of existing `int`-versioned rows? → A: **Resolved — no data migration.** Per Unit B's "no preserved production data" convention (FR-015 in `specs/001`), the SQLite initial migration for the activities-design context is regenerated fresh. There is no production int→semver data backfill. (See FR-016, SC-005.)
- Q: Does this unit also re-type the vestigial `int Version` on `ActivityBase` / `IActivity`? → A: **Resolved — in scope.** "Activity version" has a single semver meaning across design and runtime. The runtime activity abstraction carries the semver, not an `int`. (See FR-018.)
- Q: Which semver format is accepted (full SemVer 2.0.0 vs `MAJOR.MINOR.PATCH` only)? → A: **Resolved — full SemVer 2.0.0** (`MAJOR.MINOR.PATCH` with optional `-prerelease` and `+buildmetadata`; precedence and equality per the SemVer spec). (See FR-011.)
- Q: Is the version author-supplied per-activity, or derived? → A: **Resolved — the assembly version is the source of truth; a per-activity `Version` attribute is an *optional override*.** An un-annotated activity inherits its declaring assembly's version. The attribute exists for the case where an author wants a per-activity semver that differs from the assembly (e.g. multiple activity versions co-located in one assembly). (See FR-009, FR-012.) *Note: the assembly version being the **fallback** (not an inescapable equality), with the optional `[Version]` attribute as the deviation mechanism, is exactly the "there must be a way to deviate" point Sipke/Frans raised — and aligns with a version-attribute suggestion Sipke made at the 2026-06-01 review. No live dissent on this shape.*
- Q: Where do the `Version` attribute type and the activity base abstractions live? → A: **Working direction — a new zero-dep `Elsa.Activities.Runtime.Core`** housing `IActivity`, `ActivityBase`, `IActivityExecutionContext`, and the `Version` attribute, extracted out of `Elsa.Workflows.Runtime.Core` (which should not be a catch-all bucket). *Note: a project named `Elsa.Activities.Runtime.Core` already exists but currently holds activity runtime-service contracts (factory, implementation-resolver) — so this is a **move of the abstractions into the existing package**, not a greenfield package.* Author assemblies and the design-time assembly scanner both reference it. **This is a package re-organisation beyond the type change — validated at plan stage against §E2.2 (Design→Runtime is the allowed direction) and module-decomposition rules, and adopted: an `Elsa.Activities.Design`/`Elsa.Activities.Runtime` split mirroring the existing Workflows split.** (See FR-009, FR-020, plan Constitution Check G12/G15.)
- Q: What hosts the assembly scanner, and what does it read? → A: **Resolved — a new project `Elsa.Activities.Design.Reconciliation.Clr`** (named by source kind, `SourceKind = "CLR"`, matching the runtime's `Clr*Source` vocabulary). It is a **clean `IShellFeature`** (NOT derived from the reconciliation feature) that registers its `IActivityReconciliationSource` in DI, configured with a **folder path of DLLs**. The source loads + scans the assemblies, discovers `IActivity` implementations, and reads **name, display name, description, category, inputs, outputs, ports, and the version** for each. It is the only reconciliation project that references the runtime activity abstractions (Design→Runtime, the allowed §E2.2 direction). (See FR-010.)
- Q: Is the existing reconciliation feature shaped correctly? → A: **No — corrected in-unit (FR-021).** The current `abstract` base feature with a `virtual Sources` list that consumers override by deriving is a defect. The canonical pattern (used everywhere, framework §2.6.1): contracts (`IActivityReconciliationSource` + model) live in `.Core`; a **single non-abstract** reconciliation feature owns the one startup task + reconciler + handler; provider libraries contribute sources by **registering `IActivityReconciliationSource` in DI from their own clean feature**, never by inheriting. Reason given: only one startup task may be configured. (See FR-021.)

### Session 2026-06-04

- Q: How does the CLR scanner derive `ActivityTypeKey` from a scanned `IActivity` type? (`IActivity.Type` is a mutable per-instance field, not a static key source.) → A: **The CLR type's full name** (namespace + type name, e.g. `Elsa.Http.SendRequest`). Stable across recompiles, no annotation, excludes assembly identity (so repackaging doesn't change the key), and matches the namespace-qualified form Unit 4 pins. (See FR-010, FR-022.)
- Q: How does the CLR scanner treat folder DLLs with no `IActivity` types, or that fail to load/reflect? → A: **Resilient scan.** Skip DLLs containing no `IActivity` types silently; on a DLL that fails to load/reflect, log a warning and continue. Per-activity faults (invalid semver per FR-011, unresolvable assembly version per FR-020) still throw domain-scoped exceptions. No strict-mode option is added (avoid config for a hypothetical). (See FR-023.)

## User Scenarios & Testing *(mandatory)*

### User Story 1 — The author owns the version; the assembly version is the default (Priority: P1)

An activity author writes a CLR activity in an assembly. By default, the activity's version **is its declaring assembly's version** — the author controls it by versioning the assembly. When a per-activity override is wanted (e.g. a semver that differs from the assembly, or co-locating multiple versions of one activity), the author annotates the type with an optional version attribute, e.g. `[Version("2.1.0")]`. When the host reconciles its activity catalog, the assembly-reading source resolves each activity's version (attribute override if present, else the assembly version) and the catalog records it verbatim. The system never invents, increments, or reinterprets the number.

**Why this priority**: This is the load-bearing reason Unit 3 exists. Today the catalog version is a system-assigned `int` with no author intent. Author-controlled semver — sourced from the assembly the author already versions, overridable per-activity — is the prerequisite for a consuming workflow to *pin* a meaningful activity version (Unit 4). Every other requirement is downstream of moving version ownership to the author.

**Independent Test**: (a) Build a test activity with no attribute in an assembly versioned `2.1.0`; reconcile; assert the persisted `Version` is `2.1.0`. (b) Annotate a second activity `[Version("3.0.0")]` in the same assembly; reconcile; assert that activity's persisted `Version` is `3.0.0` (override wins over the assembly version).

**Acceptance Scenarios**:

1. **Given** a CLR activity with **no** version attribute in an assembly versioned `2.1.0`, **When** the assembly source is read during reconciliation, **Then** a catalog version row is created with `Version = "2.1.0"` (assembly version inherited).
2. **Given** a CLR activity annotated `[Version("3.0.0")]`, **When** the assembly source is read, **Then** the catalog row is created with `Version = "3.0.0"` (attribute overrides the assembly version).
3. **Given** the same activity with the same resolved version, **When** reconciliation runs again, **Then** no new version row is created (idempotent — the existing `(DefinitionId, Version)` is observed and skipped per Model X).
4. **Given** an author changes the activity's content **and** bumps the resolved version (assembly bump or attribute change) to `2.2.0`, **When** reconciliation runs, **Then** a new version row `2.2.0` is appended alongside the retained `2.1.0`.

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

- **An activity type carries no version attribute.** It inherits its declaring assembly's version (FR-012). It never refuses to reconcile for lack of an attribute.
- **The declaring assembly's version is not a valid SemVer 2.0.0 string** (.NET `AssemblyVersion` is the 4-part `Major.Minor.Build.Revision`). The fallback mapping is governed by FR-020: prefer `AssemblyInformationalVersion` (commonly a real semver); else map the 4-part assembly version's `Major.Minor.Build` → `MAJOR.MINOR.PATCH`. A still-unmappable value raises a domain-scoped exception (framework §2.23.5).
- **The attribute carries a string that is not a valid SemVer 2.0.0** (FR-011). Reconciliation translates this into a domain-scoped exception with the offending activity type + value (framework §2.23.5 — no raw parse exception escapes the source boundary).
- **Pre-release and build-metadata semantics** (`1.0.0-alpha`, `1.0.0+build.5`). SemVer 2.0.0 is the accepted format (FR-011): prerelease versions have **lower** precedence than the associated normal version; **build metadata is ignored** for precedence and for `(DefinitionId, Version)` equality.
- **Two version strings that are precedence-equal but not byte-equal** (e.g. `1.0.0` vs `1.0.0+build`). Equality lookup by `(DefinitionId, Version)` MUST treat them as the same logical version (precedence equality, ignoring build metadata per SemVer 2.0.0). (FR-013.)
- **Multiple versions of the same activity co-located in one assembly.** The model permits it: each annotated activity type contributes its own `(DefinitionId, semver)` row, and the attribute is what disambiguates them (the assembly version alone cannot). *CLR multi-version assembly-load-context loading of the same type is out of scope (per the Unit 3 follow-up); this unit only requires the catalog model to represent the distinct versions.*
- **The scan folder contains non-activity or unloadable DLLs** (dependency assemblies, native images, DLLs with missing transitive deps). The scanner skips DLLs with no `IActivity` types silently and logs-and-skips DLLs that fail to load/reflect; it never aborts the whole reconciliation on such DLLs (FR-023). Per-activity faults still throw.
- **The runtime `int Version` on `ActivityBase` / `IActivity`** is re-typed to the semver as part of this unit (FR-018) so design and runtime share one version meaning.
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

- **FR-009**: A new **optional** `Version` attribute type MUST be introduced, applicable to a CLR activity type, carrying the author's semver string. When present it **overrides** the assembly-version default (FR-012). **Working direction for its home (and the activity base abstractions):** a new zero-dep `Elsa.Activities.Runtime.Core` package housing `IActivity`, `ActivityBase`, `IActivityExecutionContext`, and the `Version` attribute, extracted out of `Elsa.Workflows.Runtime.Core`. Author assemblies reference it to define activities; the design-time assembly scanner (FR-010) references it to read them. *This package extraction is broader than the type change and carries a dependency-direction question (Design scanning a Runtime-activities package); it was validated at plan stage against Elsa §E2.2 (Design→Runtime is the allowed direction) and the framework module-decomposition rules (see FR-020) and adopted — the symmetric `Elsa.Activities.Design`/`Elsa.Activities.Runtime` split.*
- **FR-010**: A new project **`Elsa.Activities.Design.Reconciliation.Clr`** MUST be introduced. It contains:
  - a **clean `IShellFeature`** (it does **NOT** derive from the reconciliation feature — see FR-021) that registers its CLR source in DI: `services.AddSingleton<IActivityReconciliationSource, ClrActivityReconciliationSource>()`. It is configured with a **folder path** identifying where activity assemblies (DLLs) live. The host enables both this feature and the reconciliation feature; the reconciliation handler discovers the source via `IEnumerable<IActivityReconciliationSource>`.
  - an `IActivityReconciliationSource` implementation (the **assembly scanner**) whose `SourceKind = "CLR"`. Its `Read()` loads the assemblies from the configured folder, discovers `IActivity` implementations, and for each one reads its **name (ActivityTypeKey), display name, description, category, inputs, outputs, ports**, and resolves its **version** (the `Version` attribute if present, else the declaring assembly's version per FR-012/FR-020). It contributes one `ActivityVersionReconciliationModel` per discovered activity with `ImplementationKind = "CLR"` and a `ClrImplementationDescriptor`.
  - It references only `Elsa.Activities.Design.Reconciliation.Core` (for the source interface + model, relocated by FR-021) and the runtime activity abstractions — **not** the reconciliation feature project. It is the **only** reconciliation-side project that pulls in the runtime activity abstractions (`IActivity` etc.) and assembly-loading. It is a Design project depending on the Runtime activity abstractions — the **allowed** direction under §E2.2 (which only forbids `Workflows.Runtime.*` → `Workflows.Design.*`).
  - *(No assembly-reading reconciliation source exists today — `IActivityReconciliationSource` has zero implementations; this is the first. `ClrImplementationDescriptorSource` / `ClrActivityImplementationResolverSource` in `Elsa.Activities.Runtime` are runtime-side `IImplementationDescriptorSource` / `IActivityImplementationResolverSource` — not reconciliation sources. This source reuses the established `Clr*` vocabulary on the design/reconciliation side.)*

- **FR-021**: The existing reconciliation feature MUST be corrected to the canonical DI-source/contributor pattern (framework §2.6.1) before/as the CLR source is added — the current abstract-base-with-`Sources`-override shape is a defect:
  - `IActivityReconciliationSource` and `ActivityVersionReconciliationModel` MUST be **moved from the feature project (`Elsa.Activities.Design.Reconciliation`) into `Elsa.Activities.Design.Reconciliation.Core`**, so provider libraries reference only `.Core`.
  - `ActivitiesDesignReconciliationFeature` MUST become **non-abstract**, MUST **drop the `virtual IEnumerable<IActivityReconciliationSource> Sources` property**, and MUST stop registering sources. It registers only: the options, the replaceable default hasher (§2.6.2), the reconciler service, the single startup task, and the universal handler. **Exactly one startup task** is configured (the reason for a single non-abstract feature rather than per-provider derivation).
  - Sources are contributed exclusively by independent features (like FR-010's CLR feature) that register their `IActivityReconciliationSource` implementation in DI; the universal `ActivityVersionsReconcilingHandler` already resolves them via `IEnumerable<IActivityReconciliationSource>` and is otherwise unchanged.
  - This correction is a §2.21.1 golden-rule refactor: existing reconciliation tests MUST continue to pass (their subject/behaviour unchanged); only registration wiring and type locations move.
- **FR-011**: The accepted semver format is **full SemVer 2.0.0** — `MAJOR.MINOR.PATCH` with optional `-prerelease` and `+buildmetadata`. Precedence and equality follow the SemVer spec: prerelease versions have lower precedence than the associated normal version; build metadata is ignored for precedence **and** for `(DefinitionId, Version)` equality. A value (attribute or fallback) that does not conform MUST cause reconciliation to fail with a domain-scoped exception carrying the activity type + offending value (framework §2.23.5); a raw parse exception MUST NOT escape the source.
- **FR-012**: An activity type with **no** `Version` attribute MUST inherit its **declaring assembly's version** as its semver (the assembly version is the source of truth; the attribute is an optional override). It MUST NOT be refused for lack of an attribute, and MUST NOT be assigned an invented constant. The assembly-version→semver resolution is FR-020.
- **FR-022**: The CLR scanner MUST derive each activity's `ActivityTypeKey` (the catalog natural key) from the **CLR type's full name** (namespace + type name, e.g. `Elsa.Http.SendRequest`). The key MUST NOT include assembly identity (moving a type between assemblies preserves its key) and MUST NOT be sourced from the mutable per-instance `IActivity.Type` property. This is the key Model X uses for `(SourceKind, SourceId, ActivityTypeKey)` reconciliation and that Unit 4 consumers pin against.
- **FR-023**: The CLR scanner MUST perform a **resilient folder scan**: a DLL containing no `IActivity` implementations is skipped silently; a DLL that fails to load or reflect is logged at warning level and skipped, and MUST NOT abort reconciliation. This resilience applies only to *whole-DLL* load/discovery failures — once an activity type is discovered, a per-activity fault (invalid semver per FR-011, unresolvable assembly version per FR-020, identity collision per Model X) still throws a domain-scoped exception. No strict-mode toggle is introduced.

**Reconciliation & lookup semantics**

- **FR-013**: The reconciler's lookup by `(DefinitionId, Version)` MUST operate on the string version. Exact-version equality MUST follow the accepted format's equality rule (FR-011) — i.e. precedence-equality where the format admits non-byte-equal equivalents. The Model X find-or-create / append / hash-mismatch flow (`ActivityVersionReconciler`) is otherwise unchanged: absent → create; present + hash differs → throw `ActivityVersionHashMismatchException`; present + hash matches → skip-or-throw per option.
- **FR-014**: Exactly one `ActivityDefinitionVersion` record MUST exist per `(DefinitionId, semver)` — record-level identity is preserved (each semver is its own uniquely-locatable row), as in Unit B.

**Runtime version axis & package extraction**

- **FR-018**: The `int Version` on `ActivityBase` / `IActivity` MUST be re-typed to the semver `string` (or replaced by a member that surfaces the resolved semver), so "activity version" has a single semver meaning across design and runtime. The runtime activity abstraction MUST NOT retain an `int` version member with conflicting semantics.
- **FR-020**: The **assembly-version → SemVer 2.0.0 resolution** used by the assembly source (FR-010, FR-012) MUST be defined: prefer the assembly's `AssemblyInformationalVersion` when it is a valid SemVer 2.0.0; otherwise map the `AssemblyVersion`/`AssemblyFileVersion` `Major.Minor.Build` onto `MAJOR.MINOR.PATCH`; a value that cannot be resolved to a valid semver raises a domain-scoped exception (framework §2.23.5). Additionally, the **package extraction** introducing `Elsa.Activities.Runtime.Core` (FR-009) MUST be validated against Elsa §E2.2 (`Workflows.Runtime.*` MUST NOT depend on `Workflows.Design.*`) and the framework module-decomposition rules (no premature umbrella; zero-dep core). The dependency direction (the Design assembly scanner referencing the Runtime-activities core to read `IActivity`) MUST be confirmed acceptable at plan stage and recorded in the plan's Constitution Check.

**Tests, docs & constitution (in-unit)**

- **FR-015**: A fresh SQLite migration MUST replace the existing activities-design initial migration to reflect the string version column. No production int→semver data migration path is required (Unit B "no preserved production data" convention).
- **FR-016**: The constitution MUST be updated in-unit: Elsa **§E2.8** — state that the activity version is an author-controlled **string semver (SemVer 2.0.0)** whose source of truth is the declaring assembly's version, optionally overridden per-activity by a `Version` attribute, read by the assembly reconciliation source; and any reconciliation wording that references `int` versions or `(DefinitionId, Version)` integer lookup MUST be reworded for string semver. If the `Elsa.Activities.Runtime.Core` extraction (FR-009/FR-020) is adopted, the relevant module-decomposition / dependency wording MUST be updated in-unit too. *(Note: §E2.8 currently also says `ProvisioningHash`; the code uses `ReconcilliationHash` on the version row. Aligning that stale term is a pre-existing drift, optionally tidied in the same edit but not caused by this unit.)*
- **FR-017**: Every reshaped feature class MUST retain its framework §2.23.1 registration test; every logic-bearing reshaped class (the new assembly source, the reworked order definition / comparer, the reconciler changes) MUST be covered by §2.23.2 branch-covered unit tests; existing tests on refactored implementations MUST continue to pass without changes to the test cases themselves (framework §2.21.1 golden rule). The `001` read-contract surface pin (`ReadContractSurfaceTests`) MUST be updated to assert `Version` is `string`.
- **FR-019**: A Unit 3 follow-up file (`epic1-elsa-refactor-constitution/follow-up-items/2026-06-02_unit_activity_semantic_versioning.md`) MUST be updated to reflect resolution, and `PERSONAL_TODO.md` updated to reflect Unit 3 status.

### Key Entities

- **`ActivityDefinitionVersion`** — the catalog version row. Its `Version` becomes an author-controlled semver **string** (was `int`), immutable. One row per `(DefinitionId, semver)`. Carries the immutable `ReconcilliationHash` (Model X) used to detect a mis-versioned source.
- **Assembly version** — the **source of truth** for an activity's version. An un-annotated activity inherits its declaring assembly's version (resolved to SemVer 2.0.0 per FR-020).
- **`Version` attribute** — new **optional** metadata annotation on a CLR activity type carrying an author-supplied semver string that **overrides** the assembly version. Read by the assembly reconciliation source. Working home: the new `Elsa.Activities.Runtime.Core` (FR-009).
- **`Elsa.Activities.Runtime.Core`** *(working direction, FR-009/FR-020)* — a new zero-dep package extracted from `Elsa.Workflows.Runtime.Core`, housing `IActivity`, `ActivityBase`, `IActivityExecutionContext`, and the `Version` attribute. Referenced by author assemblies and the design-time scanner. Subject to plan-stage constitution validation.
- **`Elsa.Activities.Design.Reconciliation.Clr`** *(new project, FR-010)* — a clean `IShellFeature` (does NOT derive from the reconciliation feature) that registers a CLR assembly-scanner `IActivityReconciliationSource` (`SourceKind = "CLR"`) in DI. Configured with a folder path of DLLs; loads + scans the assemblies, discovers `IActivity` implementations, reads name/displayname/description/category/inputs/outputs/ports + resolved version, and contributes `ActivityVersionReconciliationModel`s with `ImplementationKind = "CLR"`. The only reconciliation-side project that depends on the runtime activity abstractions + assembly loading.
- **Reconciliation feature & contracts** *(corrected, FR-021)* — `IActivityReconciliationSource` + `ActivityVersionReconciliationModel` relocate to `.Core`; `ActivitiesDesignReconciliationFeature` becomes a single non-abstract feature owning the one startup task + reconciler + handler, with **no** `Sources` option. Sources are contributed by independent features via DI registration (framework §2.6.1), not by feature inheritance.
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
- **Assembly version is the source of truth.** An activity with no `Version` attribute inherits its declaring assembly's version (FR-012). The attribute is an optional per-activity override (FR-009). Assembly-version→semver resolution prefers `AssemblyInformationalVersion`, else maps the 4-part assembly version's `Major.Minor.Build` (FR-020).
- **SemVer 2.0.0 equality.** Build metadata is ignored for precedence and for `(DefinitionId, Version)` equality, per the SemVer spec (FR-011, FR-013).
- **Runtime version axis is unified, in-unit.** The `ActivityBase`/`IActivity` `int Version` is re-typed to the semver (FR-018); design and runtime share one version meaning.
- **`Elsa.Activities.Runtime.Core` extraction is adopted.** A symmetric `Elsa.Activities.Design`/`Elsa.Activities.Runtime` split mirroring the existing Workflows Design/Runtime split — the most logical home for `IActivity`/`ActivityBase`/`IActivityExecutionContext` + the `[Version]` attribute. The extraction and its dependency direction are validated at plan stage (FR-020): Design→Runtime-activities is the allowed §E2.2 direction, and the package already exists (a move, not greenfield). No live dissent: Frans/Sipke asked only that assembly-version need not *inherently* equal the activity version — i.e. that there be a way to deviate — which the optional `[Version]` attribute provides (assembly version is the fallback, not an inescapable equality). This aligns with the version-attribute shape Sipke raised in the 2026-06-01 meeting.
- **Source-pattern reuse.** The assembly-reading source plugs into the existing `IActivityReconciliationSource` + single-aggregating-handler shape from Unit B; no new contribution pattern is introduced.

## Constitutional Compliance

This spec is implemented against the two-layer constitution at `.specify/memory/constitution.md` (Elsa) and `.specify/memory/constitution-framework.md` (framework). Compliance is enforced at the plan stage via the *Constitution Check* gates — not duplicated here. Spec-level constitutional notes / flags:

- **§E2.8 is amended in-unit** (FR-016): the activity version is an author-controlled string semver (SemVer 2.0.0) sourced from the declaring assembly's version, optionally overridden per-activity by a `Version` attribute, read by the assembly reconciliation source. This is an extension of Unit B's catalog identity model, not a new structural pattern.
- **Sanctioned-patterns check (framework §2.24.2).** The CLR source contributes via the sanctioned §2.6.1 DI-source/contributor pattern: it registers an `IActivityReconciliationSource` in DI from its own clean feature, consumed by the universal handler as `IEnumerable<…>`. (FR-021 corrects the prior abstract-base-with-`Sources`-override shape, which mis-used inheritance for what is a DI-contribution concern.) Semver precedence ordering is a comparator/Strategy (sanctioned). Discovering `IActivity` implementations and reading a `Version` attribute / assembly version via reflection inside a source's `Read()` is an implementation detail of an existing pattern, not a new pattern. **No §2.24.3 gate is triggered.**
- **Flag — ordering mechanism (FR-008).** If the plan chooses a *persisted normalised sortable representation* of the semver, that is a new persistence-only field on the version row; it must respect framework §2.9.1 (real CLR property, not an EF shadow, if it bears invariant semantics) and the §E2.8 Model X immutability of version rows. Surface this in the plan's Constitution Check.
- **`Elsa.Activities.Runtime.Core` extraction (FR-009/FR-020) — validated and adopted.** Hosting `IActivity`/`ActivityBase`/`IActivityExecutionContext` + the `[Version]` attribute in the Runtime-activities package, and having the Design assembly scanner reference it, is a module-decomposition change whose dependency direction (Design→Runtime-activities) is the **allowed** §E2.2 direction (G15 PASS in the plan's Constitution Check). It mirrors the established Workflows Design/Runtime split applied to the Activities sub-tree. No §2.24.3 gate and no live dissent (see Assumptions).
- **Flag — runtime version unification (FR-018).** Re-typing the runtime activity `int Version` reaches into `Elsa.Workflows.Runtime.*`; the plan MUST confirm the blast radius stays within the activity abstraction and does not violate §E2.2.
