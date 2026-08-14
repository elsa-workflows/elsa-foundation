# Feature Specification: Executable Artifact Reconciliation

**Feature Branch**: `1304-executable-artifact-reconciliation`

**Created**: 2026-08-13

**Status**: Draft

**Input**: User description: "Executable Artifact Reconciliation. A runtime engine can already be composed standalone (Workflows.Runtime / Activities.Runtime reference no Design or Publishing project; dispatch resolves by artifact id) but nothing populates its executable store — today only the compiler writes it, inside Elsa.Workflows.Publishing. Add the ability to (a) EXPORT a portable, self-contained executable artifact (a single JSON doc: compiled structure + source references + trigger bindings + the transitive dependency closure of any child-workflow artifacts) from a publish, and (b) RECONCILE/IMPORT executable artifacts directly into a runtime engine's executable store from a configured source (folder/files) — with no design catalog, no activity-design/publication catalog, and no compiler on the runtime side. Before activating an imported artifact, the runtime MUST run a runtime-side requirements preflight (validate RuntimeRequirements + StorageDriverRequirements + activity-type availability) and reject at import with a clear diagnostic rather than fault at first activation. The outcome is a clean separation of authoring from execution: a design/build engine compiles + exports; a standalone runtime imports, preflights, and executes only artifacts. Both independently composable."

**Authoritative source**: GitHub issue [#1304](https://github.com/elsa-workflows/elsa-foundation/issues/1304) (FR rev 4, code-reviewed by @sfmskywalker). Requirement and success-criterion identifiers (`FR-B-…`, `SC-B-…`) are carried from the issue for traceability. The issue was grounded against `main` @ `22bc199e9787faa67eb2b75cd6ada8e3cdc517cb`; this feature branch is based on `main` @ `2f7ea51367d13710b5edf21d642dffd30a0662dc` (16 commits newer, including runtime dispatch-model and Groundwork store refactors). Code citations in the issue's appendix are non-normative and MUST be re-confirmed in the plan phase.

**Standalone**: The shared-triggering companion FR ([#1303](https://github.com/elsa-workflows/elsa-foundation/issues/1303)) was **deferred**. This feature does **not** depend on it: the artifact reconciler uses the **existing startup-task trigger model** (`[SingleNodeTask]` + distributed lock, as the design reconcilers do today), and on-demand re-reconciliation rides the **existing CShells shell-reload API** (reloading a shell re-runs its startup tasks). A trigger-agnostic coordinator is out of scope.

## Problem / Motivation

- **Nothing populates a runtime-only engine's executable store.** Composition is already possible (assembly-enforced: the Runtime projects reference no Design/Publishing project), but the executable store's only writers today live in `Elsa.Workflows.Publishing` (publish, workflow draft test-run, activity draft test-run). There is no cheap alternative — no import path exists.
- **Authoring is coupled to execution only through compilation.** Compilation resolves the workflow version *and every activity node* against the design/activity-publication catalog. Execution does not. The missing piece is a way to move the *compiled artifact* to a runtime, not the design input.
- **Operational goal.** Run a minimal, hardened runtime that imports only vetted, pre-compiled artifacts — no authoring surface, no compiler, no design tables — executing the *exact* artifact that was validated. This also means the runtime must be able to **verify it can execute an artifact before activating it**, since a design-free runtime can legitimately be missing an artifact's dependencies.

## Clarifications

### Session 2026-08-14

- Q: Does the consumer-capability check cover raw CLR activity-type availability, or is a type-presence check a second axis? → A: **Second axis — the import gate checks both**: the shared requirements checker (consumer capabilities + storage drivers) **and** a per-node CLR activity-type presence check against the well-known-type registry, using the type aliases the artifact already carries per node. The capability check is per activation mechanism (two capabilities framework-wide); type registration is a startup-time assembly scan in a different feature — the two never intersect, so either axis alone under-gates. The import gate MUST run after the runtime's activity-type registration completes (startup ordering constraint).
- Q: Artifact package format for a closure (single file vs package)? → A: **Single self-describing JSON closure envelope** — one file per export unit carrying an explicit format version, the root artifact id, and the closure's artifacts + source references + trigger bindings. One file = atomic import (no partial closure); a folder of closure files is the import-source unit. Multi-entry packages (zip) are deferred until a real size need exists.
- Q: Which export targets ship v1? → A: **The pluggable export-target contract plus exactly one built-in target: the API endpoint** returning the closure JSON (client download). Folder-writer and blob-push are named-but-deferred targets on the same producer.
- Q: Compatibility strictness (exact vs ranges)? → A: **Exact ordinal matching, unchanged from today** — consumer schema is exact set-membership over the advertised supported-schema list; storage-driver keys are exact, unversioned containment. The checker is extracted, not redefined; range/semver policy is out of scope ("compatible ranges" remain expressible by a consumer enumerating multiple schema versions).
- Q: Trust/signing and multi-tenancy? → A: **v1 = declared-hash closure integrity within an operator-controlled source; signing deferred.** The importer validates the dependency closure (missing artifact / hash mismatch / conflicting identity / cycle) against **declared** hashes; recomputed-hash tamper-evidence (requires extracting the executable hasher from Publishing to Runtime, analogous to the requirements-checker extraction) is the named follow-up, and signing/verification is a follow-up feature. Tenancy: the artifact stays tenant-free; the artifact source accepts an **optional tenant-id option** stamped onto the source references it mints, default null (single-tenant runtime); per-tenant fan-out is deferred.
- Q: Export capability id / rel name? → A: **Capability `elsa.api.publishing`, rel `workflow-executable-export`, href `publishing/workflows/{versionId}/executable-export`** (templated, reusing the publishing route family's version-id constraint), guarded by a new permission. Reads as a sibling of the existing `workflow-executable-provenance` rel. Export belongs to the publishing surface — a runtime-only engine cannot export. These strings are pinned for the Studio companion ([elsa-foundation-studio#493](https://github.com/elsa-workflows/elsa-foundation-studio/issues/493)).
- Q: Runtime activation — dedicated runtime-owned activation record vs opaque importer-minted correlation keys (FR-B-006)? → A: **Neither as originally framed — extract the activation authority (option A2)**. The definition-keyed "which publication is active" ledger (today publishing's `IPublicationSlotStore`/`PublicationSlot`) is a runtime-shaped concept living in the bridge incidentally; it moves to the runtime layer as a contract (same direction and justification as the FR-B-005 preflight extraction). Slot storage lives in one physical place — a slot document kind in the runtime Groundwork store family — and the publishing-family slot store is deleted (rev after PR #1330 review: with no consumers of elsa-foundation yet, the no-migration constraint is moot; a single ledger removes the dual-ledger failure mode outright). The publish pipeline and the artifact importer thereby share **one ledger** on any engine, making the dual-reconciliation overlap (design version reconciliation + artifact reconciliation touching the same definition) structurally detectable instead of silently double-activating: importer-minted publication ids are namespaced, and neither authority may supersede a publication it did not mint — the later actor rejects its candidate loudly. Publishing-only concerns (`PublicationRecord` attempt history, policies, preflight views, compensation orchestration) stay in publishing. Rejected alternatives: a parallel runtime-only activation record (two ledgers for one invariant; overlap on combined engines only detectable approximately, silent double execution in the worst case) and scan-derived opaque-keys-only state (no rollback anchor; `Ambiguous`-dispatch hazard after crashed half-imports).

### Session 2026-08-14 (PR #1330 review follow-ups)

- Q: What defines "newer" for FR-B-007's latest-wins (`ArtifactVersion` is an unconstrained string)? → A: **The SemVer sort key** (`SemVer.ToSortKey`, `Elsa.Primitives.Versioning`) with ordinal comparison — the comparator the design-side reconciler and version store already use, so design and runtime engines order versions identically. `ArtifactVersion` is copied verbatim from `WorkflowDefinitionVersion.Version` (a SemVer string) at compile; the only non-SemVer value in the wild (`"draft"`, test-runs) is already excluded from export by FR-B-011. Unparseable values are rejected at import.
- Q: Does import activation cover the full serving-projection lifecycle (Copilot re-review)? → A: **Yes, all of it** — trigger bindings AND recurring trigger schedules (same prepare/activate shape) AND trigger-index observer notification; binding-store-only activation would import timer/cron workflows that never fire. The projection fan-out is extracted/decoupled from publishing's `PublicationRecord`-typed reconciler.
- Q: What is the import isolation unit — individual artifact or closure file (Copilot re-review; reconciles US2-3 with the "no partial import" edge case)? → A: **The closure unit** (root + transitive dependencies): all gates complete for the whole unit before any write; a failing member rejects the unit; per-unit isolation across the mounted set preserves US2-3's mixed-batch semantics. Stricter than allowing sibling persistence — a failed unit writes nothing.
- Q: Should the importer recompute artifact content hashes, or trust the declared hashes (Greptile P1 on the re-review; revisits Q6)? → A: **Recompute in v1** — extract the executable hasher from Publishing to the runtime layer (the third application of the extraction pattern, after the requirements checker and the slot authority) and recompute each received artifact's canonical content hash before persistence, rejecting mismatches as broken-source diagnostics. Decisive argument: not security (a tamperer can re-hash — signing stays deferred) but the **content-addressing invariant** (ADR 0038, equal hash ⇔ equal behavior): the create-only store dedups by id, so persisting an unverified payload under a claimed id would let a corrupted file *become* that id's content on a fresh engine. Corruption is an accident path, not just a malice path, so the guard belongs in v1.
- Q: One activation ledger or two (runtime-family vs publishing-family slot storage)? → A: **One** — the slot document kind lives in the runtime Groundwork store family; the publishing-family slot store is deleted. With no consumers of elsa-foundation yet there is no data to migrate, and a single physical ledger removes the dual-ledger composition-transition hole (a runtime-only deployment that later enables Publishing) at the root. Cost accepted: Groundwork historical-schema baselines update.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Execute mounted artifacts on a design-free runtime (Priority: P1)

An operator composes a runtime engine with execution features only (no design/authoring, no compiler) and points its artifact source at a mounted folder of pre-compiled artifacts. At shell activation the engine imports and preflights those artifacts, and can execute the ones it can run, including trigger-started workflows.

**Why this priority**: The core value — a production runtime with no authoring/compile surface and no design/activity persistence.

**Independent Test**: Compose an engine with runtime execution + artifact reconciliation and *no* design/activity-design/publishing features. Mount one artifact whose dependencies the engine has. Start. Execute it. Assert it runs to completion, while asserting no design/activity-design/publishing assembly is loaded.

**Acceptance Scenarios**:

1. **Given** a runtime with no design/authoring/compile features and a folder with one valid, dependency-satisfied artifact, **When** the shell activates, **Then** the artifact is in the executable store and its workflow starts and runs to completion.
2. **Given** an artifact for a trigger-started workflow (HTTP/timer), **When** the stimulus arrives, **Then** the runtime routes it and the workflow executes.
3. **Given** the same engine, **When** inspected, **Then** no design/activity-design/publishing assembly is required for it to start or execute.

---

### User Story 2 - Reject artifacts the runtime cannot execute (requirements preflight) (Priority: P1)

An operator mounts an artifact whose required activity assemblies or storage drivers are **not** present in this runtime. On reconcile the engine detects the gap and refuses to activate it, with a diagnostic naming what's missing — it does **not** silently import something that will fault on first execution.

**Why this priority**: A design-free runtime can legitimately lack an artifact's dependencies. Without an import-time check, the failure surfaces as an `UnknownActivityTypeException` at first activation (possibly much later, in production), not at deploy time. This is the runtime counterpart of the check publishing already performs.

**Independent Test**: Mount an artifact declaring a `RuntimeRequirement` / `StorageDriverRequirement` the engine doesn't satisfy. Reconcile. Assert it is rejected at import with a clear diagnostic and is **not** activated; assert a satisfied artifact in the same batch still activates.

**Acceptance Scenarios**:

1. **Given** an artifact whose required activity type is not registered in this runtime, **When** it is reconciled, **Then** it is rejected at import with a diagnostic naming the missing requirement, and it is not activated.
2. **Given** an artifact whose declared storage-driver requirement is unmet, **When** reconciled, **Then** it is rejected at import with a clear diagnostic.
3. **Given** a mixed batch, **When** reconciled, **Then** satisfiable **closure units** (a file's root + its transitive dependencies) activate and unsatisfiable ones are rejected individually — one bad closure unit does not fail the batch, and within a unit any failing member rejects the whole unit **before any write** (no partially imported closure).

---

### User Story 3 - Export a portable executable artifact (with its dependency closure) (Priority: P1)

A designer or build pipeline publishes a workflow and exports the resulting artifact — **including the transitive closure** of any child-workflow artifacts it dispatches — as a portable unit for a runtime.

**Why this priority**: Without a portable export there is nothing to import. A single artifact is incomplete when it dispatches child workflows (it carries `Dependencies` = child artifact id + hash), so the export unit must be the closure.

**Independent Test**: On a design+publish engine, publish a workflow that dispatches a child, export it, and verify the exported unit contains the child artifact(s); import into a fresh runtime and confirm the parent executes the child.

**Acceptance Scenarios**:

1. **Given** a publish-capable engine (publish arms the runtime spine, so the executable store is always present), **When** a workflow is **published**, **Then** the compiled artifact is written to the executable store (existing behavior) — this is the server-side result of publish, distinct from export.
2. **Given** a published workflow with no children, **When** **exported**, **Then** a self-contained artifact is produced (templates are already inlined at compile time; no design catalog is needed to run it).
3. **Given** a published workflow that dispatches child workflows, **When** exported, **Then** the exported unit includes the transitive closure of dependency artifacts.
4. **Given** a published artifact on a design/build engine, **When** export is invoked with the **default (v1) target**, **Then** the server returns the artifact-closure bytes and the client (Studio) **downloads them to a JSON file** — no execution required. (Producing the bytes is the server's job; the client download is the v1 delivery target — see the cross-repo companion note.)
5. **Given** an exported unit, **When** imported into a runtime that never saw the source definitions, **Then** execution behavior (parent + children) is identical to the compiling engine's.
6. **Given** a workflow with an unpublished/test-run-only version, **When** export is attempted, **Then** only **published**-scope references are exported (non-portable test-run references are excluded).

---

### User Story 4 - Idempotent re-import & version supersession (Priority: P2)

The mounted artifact set is updated; on the next reconcile — at startup, or by reloading the shell via the existing API — the newest artifact per definition becomes active, with no duplication and no backward activation.

**Why this priority**: Operational hygiene for the promote/rollout loop. Re-triggering uses the existing startup + shell-reload mechanisms (no new coordinator; see #1303, deferred).

**Independent Test**: Import v1, execute. Add v2, reload the shell (existing API), execute — assert v2 active and exactly one active version per definition. Reconcile the same set again — assert no duplication or corruption.

**Acceptance Scenarios**:

1. **Given** v1 active, **When** v2 for the same definition is imported and the shell is reloaded, **Then** v2 becomes active and v1 is no longer activated.
2. **Given** an unchanged artifact set, **When** reconcile runs repeatedly, **Then** exactly one active version per definition remains and no duplicate records accumulate.

---

### User Story 5 - Design and execution coexist in one engine (Priority: P2)

An all-in-one engine (design + publish + runtime) works exactly as today; artifact export/import is additive.

**Why this priority**: The combined composition is the common case today; the new features must not regress it.

**Independent Test**: On a combined engine, author → publish → execute in-process still passes; enabling export/import alongside does not regress it.

**Acceptance Scenarios**:

1. **Given** a combined engine with the new features enabled, **When** a workflow is authored, published, and executed in-process, **Then** behavior is unchanged from today.
2. **Given** a combined engine with **both** design-side workflow version reconciliation (publish-on-reconcile) **and** executable artifact reconciliation enabled, **When** the same definition arrives through both paths, **Then** the later actor detects the foreign active publication via the shared activation authority and rejects its candidate with a diagnostic naming the conflicting authority — the definition is never double-activated and a stimulus never starts two instances (FR-B-006 cross-authority guard).

---

### Edge Cases

- An artifact's **required activity type is not registered** in this runtime: rejected at import (US2), not at first activation.
- An artifact's **storage-driver requirement** is unmet: rejected at import with a clear diagnostic.
- A **child-workflow dependency is missing** from the imported set: the importer's dependency-graph validation rejects the parent with a clear diagnostic (do not activate a parent whose children are absent).
- **Incompatible schema/consumer version** (`RuntimeRequirements`): rejected at import.
- Source folder **missing or empty**: mirror the design reconciler (missing → error; empty → no-op).
- **Two artifacts, same definition+version, different content**: broken-source diagnostic (mirror the design reconciler).
- **Malformed / truncated artifact**: clear error; no partial import.
- **Closure envelope with an unknown or newer format version**: rejected loudly with a clear diagnostic (mirroring the runtime document codec's fail-loud versioning discipline); no partial import.
- **`ArtifactVersion` not parseable as SemVer**: rejected at import with a diagnostic — latest-wins (FR-B-007) requires the platform's SemVer sort-key ordering, so an unorderable version is unimportable.
- **Artifact payload does not match its declared content hash** (recomputed via the runtime-owned hasher): rejected as a broken-source diagnostic before persistence — an unverified payload must never become the stored content for a content-addressed id (FR-B-010).
- Artifact **id not pinned**: content-addressed artifact ids are stable by design; the importer MUST NOT mint fresh identities per reconcile.
- **Provenance ids that don't resolve** on a runtime-only engine (dangling design ids in the artifact's identity): harmless to execution, but inspection surfaces must render them gracefully (FR-B-012).
- **Same definition claimed by two activation authorities** (compile-in-place publish or design reconciliation vs artifact import, on one engine): the shared activation authority makes the overlap structurally detectable; the later actor MUST reject its candidate with a diagnostic naming the conflicting authority — never silent double activation (FR-B-006).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-B-001**: The publish/compile pipeline MUST be able to emit a **portable executable artifact** for a published workflow version — a single self-contained document that carries the compiled node structure, its source references, trigger bindings, its declared runtime requirements, and (for workflows that dispatch children) its dependency references. Reusable-activity templates are already inlined at compile time, so no separate template bundling is needed.
- **FR-B-002**: The system MUST provide a **runtime-side artifact reconciler** that imports executable artifacts from a configured source (folder and/or explicit ordered file list) into the runtime's executable store. It SHOULD mirror the abstract `WorkflowsDesignReconciliationFeature` shape and its pluggable source options (the existing `JsonWorkflowReconciliationSource` already exposes the folder/file options this needs). The source MUST accept an **optional tenant-id option** applied to the source references it mints (default null — single-tenant runtime); per-tenant fan-out is deferred.
- **FR-B-003**: The import MUST NOT require, query, or populate any design or activity-design persistence, and MUST NOT invoke the compiler.
- **FR-B-004**: A runtime engine composed **without** any design/authoring/publishing feature and without the compiler MUST execute imported artifacts end to end, including trigger-started workflows (trigger bindings from the artifact MUST be registered so stimuli route correctly). Execution's design-freedom is **assembly-enforced today and MUST remain so** — the artifact features MUST NOT reintroduce a Design/Publishing assembly dependency into the runtime.
- **FR-B-005 (requirements preflight — shared, moved to Runtime)**: The requirements check MUST be a **shared runtime-layer service** (e.g. `IRuntimeRequirementChecker` in `Elsa.Workflows.Runtime.Core`) that, given an artifact's declared `RuntimeRequirements` (consumer key + schema version) and `StorageDriverRequirements`, evaluates them against the installed runtime registries (`IRuntimeActivityConsumerCapability`, `IRuntimeDurableValueStorageDriverRegistry`) and returns per-requirement **statuses** (Available / Missing / UnsupportedSchema) as a runtime-layer result (no Publishing view, no Design `ActivityDiagnostic`). Today this logic lives in `Elsa.Workflows.Publishing.Api` (`RuntimeRequirementPreflight`) but depends only on runtime types — Publishing is the design→runtime *bridge*, so the check belongs in Runtime, **extracted, not duplicated**. **Both** consumers reuse the one service: publishing's deployment preflight becomes a thin wrapper (keeping its retained-set scope + views + diagnostics), and the artifact importer calls it per artifact. Requirement evaluation is **exact ordinal matching** (consumer schema = set membership over the advertised supported-schema list; storage-driver key = exact, unversioned containment), unchanged from the existing preflight — the extraction relocates the check without redefining its semantics.
- **FR-B-005a (import gate — two axes)**: On reconcile, before activating an artifact, the runtime MUST run FR-B-005's checker **and** a second, independent axis: per-node CLR activity-type presence against `IWellKnownTypeRegistry`, using the `ClrActivityDescriptor.TypeAlias` values the artifact already carries per node (resolved by clarification: the consumer-capability check is per activation *mechanism* and does NOT cover type availability — an artifact can pass it and still throw `UnknownActivityTypeException` at first activation). An artifact failing **either** axis MUST be **rejected at import** with a diagnostic naming what is missing, and MUST NOT be activated. The gate MUST run after the runtime's activity-type registration (startup assembly scan) completes.
- **FR-B-006 (activation — shared runtime-owned authority, extracted from publishing)**: Resolved by clarification (2026-08-14, option A2). The **definition-keyed activation authority** — `(DefinitionId, SlotName) → ActivePublicationId + Revision` with CAS-guarded activate semantics, today publishing's `IPublicationSlotStore`/`PublicationSlot` — MUST be extracted to a contract in `Elsa.Workflows.Runtime.Core`, in the same direction as FR-B-005 (the model is strings-only; `DefinitionId`/`PublicationId`/`SlotId` are already runtime-core vocabulary on `WorkflowExecutableIdentity`, `WorkflowExecutableSourceReference`, and `WorkflowTriggerBinding`; publishing already references Runtime.Core, so no dependency is inverted). Slot storage lives in **one physical place**: a slot document kind in the **runtime Groundwork store family**, registered alongside the other runtime stores (in-memory default otherwise); the publishing-family Groundwork slot store is **deleted** (rev after PR #1330 review: elsa-foundation has no consumers yet, so the earlier no-migration constraint is moot, and a single ledger removes the dual-ledger/composition-transition failure mode outright). Activation MUST drive the **complete publication-scoped serving projection set** — trigger bindings (`IWorkflowTriggerBindingStore`) **and** recurring trigger schedules (`IRecurringTriggerScheduleStore`, same prepare/activate shape) — and notify the trigger-index observers (`IWorkflowTriggerIndexObserver`) so route projections refresh; an imported timer/cron workflow that never fires, or a stale route table, is an activation-completeness bug. The projection fan-out publishing performs today is extracted/decoupled runtime-side (it currently takes a publishing `PublicationRecord`; shape settled at plan/task level). The authority is what computes the replaced publication and makes re-reconcile idempotent and rollback anchorable. `PublicationId`/`SlotId` remain opaque strings; importer-minted publication ids MUST be namespaced (e.g. `import:{sourceId}:…`) so authority is attributable. **Cross-authority guard**: neither actor may supersede a publication it did not mint — the artifact importer MUST reject a candidate for a definition actively governed by a publish-minted publication, and the publish pipeline MUST reject a candidate for a definition actively governed by an importer-minted publication, each loudly with a diagnostic naming the conflicting authority. Publishing-only concerns stay in publishing: `IPublicationRecordStore` attempt history/audit, publication policies, preflight views/diagnostics, and compensation orchestration. The runtime MUST NOT depend on `Elsa.Workflows.Publishing` (or its Design closure) — the extraction preserves the assembly direction (SC-B-005).
- **FR-B-007**: Import MUST be **idempotent**: re-importing an unchanged artifact MUST NOT duplicate or corrupt state; a newer version for a definition MUST supersede the older (latest-wins) and MUST NOT activate backward onto an older artifact. **"Newer" is defined by the SemVer sort key over `ArtifactVersion`** (`SemVer.ToSortKey` + ordinal comparison — the same comparator the design-side reconciler and version store use), read against the currently-active publication's source reference. A candidate whose sort key is not greater than the active version's is skipped (equal + same content = the idempotent no-op path; equal + different content = the broken-source diagnostic). An `ArtifactVersion` that does not parse as SemVer MUST be rejected at import with a clear diagnostic. **The import unit is the closure** (a file's root + its transitive dependencies): every gate — parse, graph validation, hash recompute, requirements — runs for the entire unit **before any write**; any member failing rejects the whole unit; isolation across the mounted set is per closure unit (US2). A crashed half-import MUST be healed by the next reconcile pass — activation failures compensate (restore the replaced publication, remove the candidate's projections, retire the failed minted reference), mirroring the publish pipeline's compensation shape.
- **FR-B-008 (triggering — existing model)**: The reconciler MUST run at shell activation via the existing startup-task mechanism (single-node + distributed lock, mirroring the design reconcilers) and MUST complete before readiness. On-demand re-reconciliation is provided by the existing shell-reload path (reloading the shell re-runs its startup tasks); no new trigger coordinator is in scope (deferred — #1303).
- **FR-B-009**: The design-side reconciliation and this runtime-side artifact reconciliation MUST be **independently composable** — an engine may enable either, both, or neither.
- **FR-B-010 (export = PRODUCE the portable closure)**: There MUST be a first-class operation that **produces** a portable artifact unit from a published version — serializing the executable, its source references, trigger bindings, and the **transitive dependency closure** (for workflows that dispatch children, the child artifacts by `ArtifactId` + `ArtifactHash`). This producer is destination-agnostic. The produced unit is a **single self-describing JSON closure envelope** (explicit format version + root artifact id + the closure's artifacts, source references, and trigger bindings) — one file per export unit (per clarification; zip/multi-entry packages deferred). The importer MUST validate the dependency graph (the runtime equivalent of the compiler's `ValidateDependencyGraphAsync`) and reject a parent whose dependencies are absent. Closure validation operates in two layers: **declared** hashes/identities across the graph (missing dependency, hash mismatch, conflicting identity, cycle → reject), and — before any member is persisted — a **recomputed content hash** of each received artifact via the runtime-owned executable hasher (extracted from Publishing to the runtime layer, the same move as FR-B-005/FR-B-006), rejected as a broken-source diagnostic on mismatch. The recompute is an **integrity/corruption guard protecting the store's content-addressing invariant** (equal hash ⇔ equal behavior, ADR 0038 — an unverified payload persisted under a claimed id would poison create-only dedup); it is NOT tamper-proofing (the hasher is deterministic and public) — signing remains the follow-up (see Non-Goals). Note: **publish** writes the artifact to the executable store (existing, server-side); **export** is a distinct action — produce + deliver.
- **FR-B-010a (export = DELIVER to a pluggable target)**: Delivery of the produced unit MUST be a **pluggable export target/sink**, symmetric to the import *source* abstraction — the producer is fixed; the destination is a strategy. This is where "downloadable JSON / copyable JSON / push to blob" differ. Resolved (v1, per clarification): **the export-target contract ships with exactly one built-in target — an API endpoint on the publishing surface returning the closure JSON** for client download, advertised as capability **`elsa.api.publishing`**, rel **`workflow-executable-export`**, href **`publishing/workflows/{versionId}/executable-export`** (templated, reusing the publishing route family's version-id constraint), guarded by a new permission; these strings are pinned for the Studio companion (elsa-foundation-studio#493). A design-side **export writer** (mirroring the git-export writer → folder) and a **blob push** are deferred targets on the same producer.
- **FR-B-011 (export scope)**: Export MUST be restricted to **published**-scope source references. Test-run references (`TestRun` scope, expiring, tied to a `WorkflowTestScope`, with `draft:` version ids) are non-portable and MUST be excluded.
- **FR-B-012 (provenance/inspection)**: A runtime-only engine MUST render an artifact's design-provenance ids that do not resolve locally (they are never dereferenced during execution, but inspection views render them) as opaque/unresolved rather than as an error.

### Key Entities

- **Executable Artifact**: the portable, immutable, content-addressed compiled representation of one published workflow version — a single JSON document carrying the compiled node structure, source references, trigger bindings, `RuntimeRequirements` + `StorageDriverRequirements`, and `Dependencies` (child artifact id + hash). Tenant/scope/expiry live on the *source reference*, not the artifact.
- **Artifact source / reconciler**: a reconciler (existing startup-task model) that reads artifacts from a source (folder / explicit files), preflights, and imports them.
- **Activation authority (shared runtime service)**: the definition-keyed ledger of which publication is active per `(DefinitionId, SlotName)` with CAS-guarded activation — extracted from publishing's slot store into the runtime layer (per clarification, mirroring the FR-B-005 extraction) and shared by the publish pipeline and the artifact importer, so one engine has exactly one ledger. Publication attempt history stays publishing-only.
- **Requirements checker (shared runtime service)**: the runtime-layer executability check covering **both axes, always** — consumer capabilities + storage drivers, and per-node CLR activity-type presence (FR-B-005a) — reused by both publishing's deployment preflight and the importer's import gate; moved out of `Publishing.Api` into `Workflows.Runtime.Core`.
- **Executable hasher (shared runtime service)**: the canonical content-hash derivation for executables (hash + content-addressed artifact id), extracted from Publishing to the runtime layer; used by the compiler (existing derivation site) and by the importer's recompute-before-persist integrity guard.
- **Export producer**: destination-agnostic serialization of an artifact + its dependency closure into a portable unit.
- **Export target / sink**: a pluggable delivery strategy for the produced unit — symmetric to the import source. v1 ships exactly one built-in target: the API-download endpoint (`elsa.api.publishing` / rel `workflow-executable-export`); folder writer and blob push are deferred targets on the same producer.
- **Closure envelope**: the portable export unit — a single self-describing JSON document with an explicit format version, the root artifact id, and the closure's artifacts + source references + trigger bindings.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-B-001**: A runtime engine with **zero** design/activity-design/publishing assemblies loaded executes a mounted, dependency-satisfied artifact end-to-end (including a trigger-started workflow).
- **SC-B-002**: An artifact whose requirements the engine cannot meet is **rejected at reconcile** with a clear diagnostic — never surfacing as a first-activation fault in production.
- **SC-B-003**: An artifact (with its dependency closure) exported from engine A imports and executes on engine B — parent + children — with behavior parity versus compile-in-place.
- **SC-B-004**: Re-importing the same artifact set across N reconciles yields exactly one active version per definition — no duplicates, no corruption.
- **SC-B-005**: Enabling the artifact features on a runtime does not introduce any Design/Publishing assembly into the runtime (assembly-dependency assertion).
- **SC-B-006**: Design-only, runtime-only, and combined compositions are each valid and pass their smoke tests.

## Non-Goals / Out of Scope

- A **shared trigger-agnostic coordinator** / retrofit of existing reconcilers — deferred ([#1303](https://github.com/elsa-workflows/elsa-foundation/issues/1303)). This feature uses the existing startup-task trigger + shell-reload API.
- Changing how **execution** works — already design-free and assembly-enforced; stays as-is.
- Pulling the **publishing-only publication machinery** into the runtime — the `IPublicationRecordStore` attempt history/audit, publication policies, preflight views, and compensation orchestration stay in Publishing, and no Publishing assembly ever enters the runtime closure. (The slot *authority* is deliberately shared — relocated to the runtime layer per FR-B-006; it is no longer a publishing store.)
- **Studio UI is a companion cross-repo change, not part of this server feature.** The server-side export **producer + endpoint** (v1 target returning the closure bytes) is in scope here. The default "download to a file in the client" needs a new "Export executable artifact" action in `elsa-foundation-studio` — tracked as [elsa-foundation-studio#493](https://github.com/elsa-workflows/elsa-foundation-studio/issues/493), blocked on this feature's export endpoint. (Non-default targets — folder writer, blob push — are server-side and need no Studio work.)
- Non-file **transports** (OCI/registry/git-of-artifacts) — folder/files first.
- Artifact **signing / supply-chain trust** — signing/verification remains the follow-up. (Rev after PR #1330 re-review: **recomputed-hash validation moved INTO v1** — the executable hasher is extracted from Publishing to the runtime layer and the importer recomputes each received artifact's content hash before persistence, rejecting mismatches. This guards the content-addressing invariant against corruption and drift; it is not tamper-proofing, because the hasher is deterministic and public — only signing provides that, and the v1 trust boundary remains the operator-controlled source.)
- **Zip/multi-entry packages** for the export unit — deferred; the v1 unit is a single JSON closure envelope.
- **Per-tenant import fan-out** — deferred; v1 stamps an optional per-source tenant id (default null).
- Replacing the existing **design-side** reconciliation — this is the executable-side counterpart.

## Assumptions

- Execution reads only the executable + source-reference stores and resolves by artifact id — verified and assembly-enforced (see the issue's appendix). The import targets those stores plus the trigger-binding store.
- The compiled artifact is already a single self-contained JSON document (runtime document kind), with compatibility metadata (`RuntimeRequirements`, `StorageDriverRequirements`) already present — so the portable format is largely a freezing exercise, not new serialization work.
- Reusable-activity templates are inlined at compile time (artifacts are self-contained with respect to templates; the execution path never reads the template store).
- The runtime keeps its runtime/executable Groundwork stores; it drops design/activity-design and publishing stores.
- Publish-capable engines always have the executable store (publish arms the runtime spine), so the publish/export split is by **action**, not composition.
- Code citations in the issue's grounding appendix reflect `main` @ `22bc199e` (some from a slightly newer main) and MUST be re-confirmed during `/speckit.plan`.

## Open Questions / Deferred Decisions

All seven questions left open by issue #1304 (rev 4) were resolved in the 2026-08-14 clarification session (see Clarifications); their outcomes are encoded in the requirements above. Named follow-ups deferred out of v1: recomputed-hash tamper-evidence (executable-hasher extraction to the runtime layer), artifact signing/verification, per-tenant import fan-out, zip/multi-entry export packages, folder-writer and blob-push export targets.
