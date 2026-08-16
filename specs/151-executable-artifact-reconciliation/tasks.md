# Tasks: Executable Artifact Reconciliation

**Feature**: 151 · **Branch**: `1304-executable-artifact-reconciliation` · **Issue**: [#1304](https://github.com/elsa-workflows/elsa-foundation/issues/1304) · **PR**: [#1330](https://github.com/elsa-workflows/elsa-foundation/pull/1330)

**Input**: [spec.md](spec.md) (Clarifications 2026-08-14, 2026-08-14 PR-review, 2026-08-15 architect review), [plan.md](plan.md), [research.md](research.md) (D1–D9 + writer census), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: REQUIRED, not optional — §2.23.1 registration tests and §2.23.2 branch-covered unit tests are constitutional gates. **xunit only; FluentAssertions is constitutionally absent** from `Directory.Packages.props` — ignore any task phrasing elsewhere that suggests otherwise.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: parallelizable (different files, no dependency on an incomplete task)
- **[Story]**: `[US1]`–`[US5]` for user-story phases only; Setup/Foundational/Polish carry no story label

## Pinned decisions this task list carries (do not soften)

| # | Pin | Source |
|---|---|---|
| P1 | **Three** extractions Publishing → Runtime: requirements checker (two axes, executables **and** templates), executable hasher (**byte-stable**, golden-hash test), activation authority | FR-B-005/005a/006/010 |
| P2 | Activation = **new neutral contracts** + **one** coordinator owning the complete lifecycle; publishing's slot store **deleted** (no migration); **total** runtime-side rename sweep, no grandfathering | FR-B-006, research D3 |
| P3 | Ownership is the explicit `WorkflowActivationSource` field — **never** inferred from id prefixes | FR-B-006, 2026-08-15 review |
| P4 | Keep `SlotId`, `Scope == Published`, `PublishedAt` (provenance, not activation machinery) | FR-B-006 |
| P5 | Census back doors are **v1**: remove `IndexAsync` fallback + artifact-scoped write path; route pump hard-deletes; collapse schedule double-write. `TryAdvanceAsync` fire-cursor is carved out | FR-B-006 census |
| P6 | Import isolation unit = **closure unit**; all gates before **any** write; failed unit writes nothing | FR-B-007, US2-3 |
| P7 | Latest-wins = **SemVer sort key** over `ArtifactVersion`; unparseable → reject | FR-B-007 |
| P8 | Export: GET serves the **`download` target only**; capability `elsa.api.publishing` / rel `workflow-executable-export` pinned for [studio#493](https://github.com/elsa-workflows/elsa-foundation-studio/issues/493) | FR-B-010a |
| P9 | §2.21.1 golden rule: existing publishing/runtime tests pass with **wiring-only** changes | plan Constitution Check |
| P10 | Retire reasons are **`"activation-replaced"` / `"activation-failed"`** — corrected in data-model.md, quickstart.md and contracts/closure-envelope.md on 2026-08-15; no stale `"publication-replaced"` / `publicationId` literal remains in the spec artifacts | FR-B-006 |

> **Post-`/speckit.analyze` remediation (2026-08-15).** A cross-artifact consistency pass over spec/plan/tasks against both constitutions returned **zero CRITICAL findings and 100% requirement coverage**. Its 16 findings were remediated in one pass: plan.md gained a §2.21.1 Complexity Tracking entry recording the architect-approved test removals and a corrected golden-rule row; the phase-2 conventions block below carries the §2.23.3 / §2.6.2 / §2.5 obligations that had no task home; T021 carries the §E6 R4 reviewer flag; T091 was re-scoped after inspecting #1346's scanner; T115 covers the §2.22 README obligation; and the stale pre-sweep literals were fixed in the artifacts rather than deferred into an implementation task.

## Endpoint-framework resolution (decided 2026-08-15, supersedes contracts/export-endpoint.md as written)

[ADR 0068](../../docs/adr/0068-first-party-rest-apis-use-aspnet-core-minimal-apis.md) (accepted 2026-08-15) landed **after** the export-endpoint contract was written citing FastEndpoints, and makes Minimal APIs the normative model for new first-party REST endpoints. Resolution for this feature:

- **The endpoint ships as FastEndpoints**, consistent with the ~20 sibling endpoints in `Elsa.Workflows.Publishing.Api`.
- **Capability-gap evidence** (the ADR's exception bar — "convenience is not an exception"): there is **no shell-scoped Minimal API mapping seam for Elsa module features**. Every `IEndpointRouteBuilder` usage in `src/` today is a host/root surface (`Elsa.Foundation.Host`, `Elsa.Workbench`, `Elsa.Modularity/ExtensionBuilder`); shell-prefixed module routes resolve through FastEndpoints' process-global discovery. The per-shell mapping seam is owned by [#1345](https://github.com/elsa-workflows/elsa-foundation/issues/1345), unlanded. Building it here would pull #1344/#1345 program work into this feature.
- **Containment**: the closure factory and export-target seam stay framework-neutral (no HTTP types), so the migration wave is a mechanical re-host of one handler.
- **This is not a new architectural exception.** `Elsa.Workflows.Publishing.Api` is *already* transitional inventory in its entirety — all 19 of its registrations in [#1346](https://github.com/elsa-workflows/elsa-foundation/issues/1346)'s registry carry `removalOwner: "First-party REST API Consolidation"`. The export endpoint joins that inventory and migrates in the module's wave. Building it as a one-off Minimal API *now* — with no shell-scoped mapping seam and no module migration underway — would create exactly the bespoke surface the ADR's "no Elsa endpoint framework over Minimal APIs" clause warns against, and would leave one route stranded outside the wave that moves the other 19.
- **Registry**: handled by T091, with the cross-branch coordination split into T091a. Note the mechanic: `sourceHash` in that registry is an **owner fingerprint** over every `.cs` file in the owning project, so this feature's edits to Publishing.Api invalidate all 19 existing rows, not just the new one. No hard dependency on #1346's merge order either way.
- **Unchanged**: route, capability id, rel, href, response shape. studio#493's pins hold regardless of framework.

---

## Verified test baselines (captured 2026-08-15, before any implementation code)

Recorded at commit `100a5497c` so the §2.21.1 golden-rule checks (T040, T112) can tell *my* regression from *already broken*. Verified by running the suites in a clean worktree at that commit — measured, not inferred.

| Suite | Baseline | Note |
|---|---|---|
| `dotnet build Elsa.Server.slnx` | 0 errors, 228 warnings | |
| `Elsa.Workflows.Runtime.Tests` | 1653 passed, 0 failed | |
| `Elsa.Workflows.Publishing.Tests` | 23 passed, 0 failed | |
| `Elsa.Workflows.Runtime.Api.Tests` | 93 passed, 0 failed | |
| `Elsa.Persistence.Groundwork.Tests` | **559 passed, 209 FAILED** | **Already red, and environmental.** Every failure is `System.IO.IOException: The process cannot access the file '…elsa-groundwork-*.db' because it is being used by another process` — the SQLite fixture cannot delete its temp database during teardown on Windows. It hits every Groundwork store test class equally (`…WorkflowExecutableStoreTests` 17, `…RuntimePostCommitOutboxStoreTests` 22, `…WorkflowTriggerBindingStoreTests` 8, …). Test **bodies pass**; only disposal fails, and xunit scores that as a failure. Measured at `b1f395af9` with T026's work stashed. A new store class added here will appear to "fail" ~half its tests for this reason alone — check the exception type before believing it. |
| `Elsa.Architecture.Tests` | **330 passed, 37 FAILED** | **Already red before this feature.** Failures are in `CheckpointFenceEvidenceImporterTests` (16), `GroundworkCoverageLedgerTests` (13), plus `EfCoreSurfaceRatchetTests` and `ArchitectureGuardTests` — checkpoint-fence / mongodb evidence / coverage-ledger assertions, unrelated to spec 151. T040/T112 must compare against 37, not 0, and T070/T111 add new tests to a suite that is not currently green. |

**Decision on the 37 (Joey, 2026-08-15): do not fix them during this feature.** They are out of spec-151 scope and chasing them now would stall the path to real-life testing. Revisit after the task list is complete — the resolution may be to converge the code *or* to change the architecture tests, and that is a call for Joey and Sipke together. Until then, treat 37 as the pass/fail line for this suite and isolate spec-151's own architecture tests (T070, T111) by filter rather than by whole-suite green.

## Code-vs-doc discrepancies found during T024 (verified 2026-08-15 against `main`)

Found while absorbing `PublicationActivator` into the coordinator, and confirmed by reading the code directly. **Where these differ from research D3/D5, the code is right and the doc citation is wrong.** They bind T029, T030, T035 and T044.

1. **`PublicationActivator` owns only 4 of the 7 sequence steps.** The root-write lease, the source-reference mint/save, and the predecessor retire live in `PublishWorkflowRequestHandler.Handle` (`:129`, `:146-177`, `:243-254`), **not** in the activator. The activator covers save-record → prepare projections → slot CAS → activate projections → transition records → retire replaced record. T024's citation of `PublicationActivator.cs:13-139` is therefore incomplete; **T029 and T030 must retarget both halves**, which is what D3's prose ("the publish handler's activation sequence") already implied.
2. **Production retire literals are `"publication-activation-failed"` (`PublishWorkflowRequestHandler.cs:168`) and `"publication-replaced"` (`:253`).** The spec's `"activation-failed"` / `"activation-replaced"` are the post-sweep targets. **T035 must map both**, and note the failure literal is `publication-activation-failed`, not `publication-failed`.
3. **Publishing's same-artifact no-op requires FOUR conditions, not one** (`PublishWorkflowRequestHandler.cs:112-127`): same `ArtifactId`, `plan.Result.Changes.All(Retained)`, a live (non-retired) reference, **and** a tenant match. The coordinator implements only the two that are runtime-resolvable (same artifact + live reference); trigger-change retention and tenant are publication-plan concepts the runtime cannot see. **Decision for T029/T030: publishing keeps its own four-condition guard and calls the coordinator only once it has decided to activate.** The coordinator's no-op is a safety net for the importer and for idempotent re-requests — never the decider for publishing. Delegating it wholesale would drop the tenant condition and silently no-op a publish for a second tenant reusing one artifact.
4. **Pre-flip failures leak prepared projection rows in production.** On projection-prepare failure or slot conflict, `PublicationActivator` marks the record failed and returns; nothing deletes the prepared bindings/schedules. The coordinator **uniformly** removes the candidate's projections and retires its reference on every failure path, restoring the predecessor only when the slot actually flipped. This is a deliberate strengthening, not a verbatim port — §2.23.4 treats "the refactor resolved a bug the tests silently relied on" as an architect-recorded decision, so **confirm before T029/T030 retarget publishing onto it**.
5. **Tenancy is deliberately absent from the activation slot — decided 2026-08-15, do not "fix" it.** The slot is keyed `(DefinitionId, SlotName)` with no tenant axis. **This is consistent with the runtime, not an oversight:** `WorkflowTriggerBinding` — the projection that actually routes a stimulus — carries no `TenantId` either. Across `Workflows/Runtime/Core/Models`, tenancy appears on source references, dispatch records, execution state and test scopes: instance- and reference-level, never on definition-keyed serving projections. Adding `TenantId` to the slot alone would create a per-tenant *activation decision* projecting into tenant-blind *bindings* — an inconsistency, not future-proofing. Per-tenant activation is therefore a change to the whole projection chain (slot + bindings + recurring schedules together), which is exactly what FR-B-002 defers as per-tenant fan-out. Nothing is painted in: the same "no consumers of elsa-foundation yet" argument that makes deleting publishing's slot store safe makes adding a tenant axis safe later, and the durable slot store does not exist until T026. Recorded as a **known open axis** so it stays a decision rather than being rediscovered as a bug.

## FOR REVIEW — publish/activation responsibility split (decided 2026-08-16, Joey; @sfmskywalker to counter if he disagrees)

Raised by Joey while reviewing T027–T031. **Not** escalated for approval before proceeding — recorded here deliberately so a full review sees the choice and can reverse it. It adjusts how FR-B-006 is *framed*; it does not change what FR-B-006 requires.

**The smell.** Publishing currently means two things at once: *produce an artifact and deliver it somewhere*, and *make it live here*. They are tangled because the only delivery destination today is the local executable store.

**The reframing.** Publishing produces an artifact and delivers it to one or more **targets**. The *local* target's delivery happens to include requesting activation; a blob or folder target's does not. **Activation is the runtime's responsibility, requested by a target — not something publishing does.** Behaviour today is unchanged; only the concept separates. Note this is the same seam FR-B-010a already defines (`IWorkflowArtifactExportTarget`, `download` built in, blob/folder deferred) — publish-delivery and export-delivery are one idea reached from two directions.

**Naming: `Target`.** Reuses FR-B-010a's pinned term so publish and export share one vocabulary. `Pipeline` is wrong (a pipeline is a sequence of stages; this is a strategy selection among destinations) and collides with `Elsa.Pipelines.Core`. `Channel` is a borrowed messaging metaphor implying a conduit rather than a destination — §E6 R5 disfavours it.

**Consequences adopted:**
1. **A missing `PublicationRecord` is a normal answer, never an error.** It means "not published *by me*". A runtime-only engine has none at all — its publishing tables may not even exist, let alone be in the same database. Three sites throw `InvalidOperationException` on absence today and must not: `PublishWorkflowRequestHandler.cs:118`, `WorkflowPublicationPreflightReader.cs:55`, `PublicationSlotLifecycleRequestHandlers.cs:28`. A GET returning 404 is correct — it says nothing about whether the artifact is activated.
2. **`PublicationRecord.Status` describes publication, not activation.** It must not mirror activation outcomes. **This dissolves the journal-repair service** that was being scoped as `T044a`: slot/journal drift only existed because we made the journal mirror activation state. Stop mirroring and drift is impossible by construction. `T044a` is parked, not deferred.
3. **`PublicationId` / `ActivationId` are a foreign key, not an identity.** They hold equal values today (verified: `PublishWorkflowRequestHandler.cs:88` mints one id used as both). The values stay equal for now — re-minting mid-2C is churn with no behavioural payoff — but they stop being treated as the same *concept*.
4. **No operator-facing deactivation surface in a runtime-only engine.** It is configured at startup and re-reconciles through shell reload (FR-B-008); a mutation surface would undermine the immutability that makes a hardened runtime worth having. The runtime still deactivates **internally** — the coordinator retires the predecessor on supersession and compensates on failure, which is why `IWorkflowActivationAuthority.TryDeactivateAsync` exists. The rule is *no external deactivation surface*, not *no deactivation*.

**Evidence in current code** (read 2026-08-16, uncommitted T027–T031 tree):
- `UnpublishPublicationSlot` is declared `IRequestHandler<UnpublishPublicationSlot, WorkflowActivationSlot>` — a publishing endpoint typed to return a runtime activation type.
- `PublicationSlotLifecycleRequestHandlers.cs:26-29` names its local `publicationId` when the value is `slot.ActiveActivationId`; the naming itself encodes the conflation.
- Unpublish fetches the `PublicationRecord` only to feed `projectionPreparer.RemoveAsync(publication)` — the record is a *carrier for projection removal*, not publication state. **`IPublicationProjectionPreparer.RemoveAsync` keying on `PublicationRecord` rather than on activation identity is the single concrete coupling point**, and is where a future separation would cut.

**Not decided here:** whether publish should ever stop auto-activating for the local target. It should not change now — decoupling it into two operator steps is a large API/UX change well outside spec 151 and a §2.21.1 bulldozer. Only the modelling changes.

## Conventions binding EVERY task in this feature

Stated once here rather than repeated per task. These apply in all phases, not just the one you are working in.

- **§2.23.3 visibility** — feature classes are `public` and **NOT sealed** (§2.5 inheritance depends on it; a sealed feature class amputates the only sanctioned cross-feature coupling pattern). Logic-bearing implementations are `public sealed`. This bites hardest on T064/T065 and on every new service in Phase 2.
- **§2.6.2 replacement contracts** — `IRuntimeRequirementChecker`, `IWorkflowExecutableHasher`, and `IWorkflowActivationAuthority` are **replacement contracts**: exactly one implementation is meaningful per engine. Their kind is declared through the extension-point catalogs' *Overridable contracts* section (T106 — the repo's declaration mechanism; there is no marker-interface convention in `src/`). Conflicts MUST be prevented at registration or diagnosed at startup — **silent last-write-wins is forbidden**. `TryAdd*` registration is the chosen prevention (first-wins, consistent with ADR 0033); where a stronger guarantee is warranted, the precedent is the dispatcher factory in `RuntimeCoreServiceCollectionExtensions.cs:377-396`, which enforces exactly one `IWorkflowExecutableStartPolicy` with an explicit diagnostic.
- **§2.5 registration discipline** — every collaborator a feature owns is registered against a contract and injected as that contract, never as the concrete type; `ConfigureServices` is `virtual`.
- **§2.23.5 exception wrapping** — no raw `JsonException`, `IOException`, or storage exception escapes a feature boundary; wrap in a domain exception carrying identifiers, preserve the original as `InnerException`.
- **Tests are xunit only.** FluentAssertions is constitutionally absent from `Directory.Packages.props`.

---

## Phase 1: Setup (project scaffolding)

**Purpose**: create the two new projects and their test project so every later phase has a home. Blocks everything.

- [x] T001 [P] Create `src/Elsa/Workflows/Runtime/Reconciliation/Core/Elsa.Workflows.Runtime.Reconciliation.Core.csproj` — contracts-only `.Core` seam (§2.16.1 exempt class), referencing `Elsa.Workflows.Runtime.Core`, `Elsa.Serialization.Core`, `Elsa.Primitives`. No `<Version>` element (§E5 Line B, computed patch).
- [x] T002 [P] Create `src/Elsa/Workflows/Runtime/Reconciliation/Elsa.Workflows.Runtime.Reconciliation.csproj` — feature project referencing `Elsa.Workflows.Runtime.Reconciliation.Core`, `Elsa.Workflows.Runtime.Core`, `Elsa.Workflows.Runtime`, `Elsa.Tasks.Core`, `Elsa.Locking.Core`, `Elsa.Serialization.Core`, `Elsa.Persistence.Core`, and `Elsa.Activities.Runtime` (needed for the `[TaskDependency(typeof(RegisterActivityTypesStartupTask))]` type reference — research D5). No `<Version>` element. **JSON source stays inside this project** — §2.20 forbids premature per-provider decomposition while only one source kind exists (research D1).
- [x] T003 Extend the `<Compile Remove>` glob at `src/Elsa/Workflows/Runtime/Elsa.Workflows.Runtime.csproj:19` with `Reconciliation/**/*` so the sibling folders are not compiled into `Elsa.Workflows.Runtime` (mechanical caveat, research D1).
- [x] T004 Add both projects to `Elsa.Server.slnx` under a new `/src/Elsa/Workflows/Runtime/Reconciliation/` folder, mirroring the `/src/Elsa/Workflows/Design/Reconciliation/` folder block at `Elsa.Server.slnx:283-287`.
- [x] T005 Create `tests/Elsa/Workflows/Runtime/Reconciliation/Tests/Elsa.Workflows.Runtime.Reconciliation.Tests.csproj` mirroring `tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj` (**xunit 2.9.3 only — no FluentAssertions**), and add it to `Elsa.Server.slnx`.

**Checkpoint**: `dotnet build Elsa.Server.slnx` succeeds with three empty new projects.

---

## Phase 2: Foundational (BLOCKING — all three extractions, the shared coordinator, the census back doors, and the envelope model)

**Purpose**: everything below is a prerequisite for **every** user story. US1/US2 cannot import without the coordinator, hasher, and checker; US3 cannot export without the hasher and envelope. §2.21.1 governs the whole phase: behavior must be preserved — existing publishing/runtime tests must pass with **wiring or naming** changes only. (The checker and hasher moves are pure relocations; the activation authority is a *supersession* — see plan.md's Complexity Tracking entry for the recorded test-removal approval.)

### 2A — Extraction 1 of 3: requirements checker → Runtime (FR-B-005 / FR-B-005a)

- [x] T006 [P] Add `RuntimeRequirementCheckResult` + `RuntimeRequirementStatusEntry` / `StorageDriverStatusEntry` / `ActivityTypeStatusEntry` records and the status enum member `MissingActivityType` in `src/Elsa/Workflows/Runtime/Core/Models/` per [data-model.md](data-model.md) — `IsSatisfied` is true iff every entry across all three collections is `Available`. Runtime-layer result only: no Publishing view type, no Design `ActivityDiagnostic`.
- [x] T007 Add `IRuntimeRequirementChecker` to `src/Elsa/Workflows/Runtime/Core/Contracts/` per [contracts/runtime-contracts.md](contracts/runtime-contracts.md). **The contract MUST accept requirement sets from both executables and reusable-activity templates** (2026-08-15 architect review — the publishing preflight's template fallback at `RuntimeRequirementPreflight.cs:100-103` is preserved capability, not publishing residue).
- [x] T008 Implement `RuntimeRequirementChecker` in `src/Elsa/Workflows/Runtime/Services/`: axis (a) relocates the capability/driver logic **verbatim** from `src/Elsa/Workflows/Publishing/Api/Services/RuntimeRequirementPreflight.cs:111-144` — exact ordinal set-membership over the advertised supported-schema list, exact unversioned driver-key containment (clarified: extraction relocates, never redefines); axis (b) is per-node CLR type presence via `IWellKnownTypeRegistry.TryGetTypeOrDefault(ClrActivityDescriptor.TypeAlias)`, the exact predicate at `ClrActivityActivator.cs:32`. One call → one verdict covering both axes.
- [x] T009 Register the checker via `TryAddScoped` in `AddWorkflowRuntime()` (`src/Elsa/Workflows/Runtime/Extensions/RuntimeCoreServiceCollectionExtensions.cs`) per ADR 0033 (contracts in `.Core`, defaults in impl).
- [x] T010 Rewrite `src/Elsa/Workflows/Publishing/Api/Services/RuntimeRequirementPreflight.cs` as a **thin wrapper** over the shared checker, keeping its retained-set scope selection, its `RuntimeRequirementPreflightViews` shapes, and its `ActivityDiagnostic` formatting. Publishing depends on Runtime (already-legal direction); no Runtime→Publishing edge is introduced.
- [x] T011 Add the missing **activity-consumer diagnostics** to `BuildDiagnostics` (`RuntimeRequirementPreflight.cs:149-188`), which today hardcodes `DurableValueStorageDriver` and emits nothing for a failing `ActivityConsumer` — mirror `ActivityPublicationReviewPolicy`'s `activity.runtime.consumer-missing` / `consumer-schema-unsupported` keys (plan note 1, research D2).
- [x] T012 Re-parent `UnknownActivityTypeException` to `ActivityResolutionException` and add an `ActivityActivationFailureKind` member (e.g. `MissingActivityType`) in `src/Elsa/Activities/Runtime/Core/`, so `ActivityActivationFailureHandler.Classify` stops returning null and a missing CLR type classifies as a non-retryable `CorrectDeploymentAndResume` deployment incident like every sibling failure (plan note 2, research D2).
- [x] T013 [P] §2.23.2 branch-covered tests for `RuntimeRequirementChecker` in `tests/Elsa/Workflows/Runtime/Tests/`: both axes independently and together; every status (`Available` / `Missing` / `UnsupportedSchema` / `MissingActivityType`); the template requirement-set path; multi-node alias dedup with `NodeIds` attribution.
- [x] T014 [P] Parity tests for the preflight wrapper in `tests/Elsa/Workflows/Publishing/Api/Tests/` — §2.21.1 gate: **existing preflight tests must pass with wiring-only changes**. Add coverage for the newly-emitted consumer diagnostics (T011).
- [x] T015 [P] Tests for the `UnknownActivityTypeException` re-parenting: `Classify` returns the new kind and the incident is non-retryable.

### 2B — Extraction 2 of 3: executable hasher → Runtime, byte-stable (FR-B-010)

- [x] T016 [P] Add `IWorkflowExecutableHasher` (`ComputeHash(executable) → "sha256:…"`, `CreateArtifactId(prefix, hash)`) to `src/Elsa/Workflows/Runtime/Core/Contracts/`.
- [x] T017 Move `src/Elsa/Workflows/Publishing/Services/WorkflowExecutableHasher.cs` to `src/Elsa/Workflows/Runtime/Services/` as the default implementation. **The canonical algorithm and payload version MUST stay byte-stable** — the hash is identity (ADR 0038), so identical input MUST hash identically before and after the move. Verify at implementation time that the canonical payload reads only `WorkflowExecutable` model data (rootNodeId, incident strategy, ordinally-sorted nodes, input contract, dependencies); if it reaches anything Publishing-local, stop and escalate rather than altering the payload.
- [x] T018 **Golden-hash test** (2026-08-15 architect review, pinned): capture the pre-move hash of a fixture executable as a committed golden value and assert the relocated hasher reproduces it byte-for-byte. This is the extraction's acceptance gate, not a nice-to-have.
- [x] T019 Point the compiler's existing derivation site at `IWorkflowExecutableHasher` instead of the concrete Publishing type; delete the Publishing class once no references remain. Do **not** touch `ExecutableActivityTemplateBehaviorHasher` (`Publishing/Core/Services/`) — different concern, stays in Publishing.
- [x] T020 Register the hasher via `TryAdd` in `AddWorkflowRuntime()`.

### 2C — Extraction 3 of 3: neutral activation authority + ONE shared lifecycle coordinator (FR-B-006)

**This is the largest cluster and the highest-blast-radius one (research risk R1). Behavior-preserving by construction: the coordinator absorbs the existing `PublicationActivator` sequence verbatim, including compensation, and inherits publishing's activator test matrix as its baseline.**

- [x] T021 Add the neutral contracts to `src/Elsa/Workflows/Runtime/Core/`: `IWorkflowActivationAuthority`, `WorkflowActivationSlot(SlotId, WorkflowDefinitionId, SlotName, ActiveActivationId, Source, Revision, UpdatedAt)`, `WorkflowActivationSource(Kind, SourceId?)`, `WorkflowActivationRequest`, `WorkflowActivationTransition`. **New contracts, not relocated publishing types** — the runtime must not become responsible for concepts still named "Publication". **§E6 R4 reviewer flag (research D8, carry into review)**: `WorkflowActivationSource` uses `…Source` as an *ownership descriptor record*, not in R4's codified sense (`…Source` = a pull contract that returns items). The name is intended and spec-pinned; surface it explicitly for reviewer judgment rather than letting the naming gate pass it silently.
- [x] T022 Add `IWorkflowActivationCoordinator` + `WorkflowActivationCommand` / `WorkflowActivationResult` to `src/Elsa/Workflows/Runtime/Core/Contracts/` — the **only** activation entry point for every path.
- [x] T023 [P] Implement the in-memory `IWorkflowActivationAuthority` default in `src/Elsa/Workflows/Runtime/Services/` and register it with `TryAdd` in `AddWorkflowRuntime()` (non-Groundwork fallback).
- [x] T024 Implement `WorkflowActivationCoordinator` in `src/Elsa/Workflows/Runtime/Services/`, absorbing the sequence currently in `src/Elsa/Workflows/Publishing/Services/PublicationActivator.cs:13-139` in order: root-write lease (`IWorkflowExecutableRootWriteLeaseManager`, so reference GC cannot race) → mint/save live source reference → prepare **both** projections (`IWorkflowTriggerBindingStore` **and** `IRecurringTriggerScheduleStore`) → slot CAS on the authority → activate both projections → notify `IWorkflowTriggerIndexObserver` → retire the predecessor's reference with reason `"activation-replaced"`. Port `CompensateActivationFailureAsync` (`PublicationActivator.cs:103-139`) verbatim: on failure after the slot flip, restore the replaced activation, re-activate its projections with `forceReplay`, remove the candidate's projections, retire the failed reference with `"activation-failed"`. **Recurring schedules are not optional** — a binding-only activation imports timer/cron workflows that never fire. **Placement decision (research D3 left this as a task-time call — record it here rather than re-deriving it)**: the coordinator and authority defaults live in `Elsa.Workflows.Runtime` alongside the other runtime services, not in a dedicated `Runtime.Activation` sibling project. Revisit only if the activation implementation cluster grows past the composition root's comfort; §2.16.1's exemption classes and the `ReferenceGarbageCollection` precedent make the split cheap if it becomes warranted.
- [x] T025 Implement the **explicit ownership conflict rules** on the authority/coordinator transition: same artifact requested by any source → idempotent no-op success; concurrent change → CAS failure on `Revision`; **different artifact from a non-owning source → loud rejection with a diagnostic naming the owning `WorkflowActivationSource`**. Ownership is read from the slot's `Source` field **only**. Id prefixes (`import:{sourceId}:…`, `publication-…`) MAY exist for log readability but **MUST NOT** be parsed for ownership decisions — prefix-sniffing is the explicitly rejected earlier design. Ownership transfer is an operator action, out of v1.
- [x] T026 Add the activation-slot document kind to the **runtime** Groundwork store family: `src/Elsa/Persistence/Groundwork/ElsaRuntimeStorageManifest.cs` + `src/Elsa/Persistence/Groundwork/DependencyInjection/GroundworkRuntimeStoreRegistration.cs`, and implement the durable authority store beside the other runtime stores in `src/Elsa/Persistence/Groundwork/Stores/`. Registered whenever runtime Groundwork persistence is composed, exactly like the trigger-binding store.
- [ ] T027 **Delete** `src/Elsa/Workflows/Publishing/Persistence/Groundwork/Stores/GroundworkPublicationSlotStore.cs` and its entries in `PublishingGroundworkStorageManifestSource.cs` and `DependencyInjection/GroundworkPublishingStoreRegistration.cs`. **One physical ledger per engine** — this removes the dual-ledger composition-transition hole (runtime-only deployment that later enables Publishing). No data migration: elsa-foundation has no consumers.
- [ ] T028 **Delete** `IPublicationSlotStore` from `src/Elsa/Workflows/Publishing/Core/Contracts/IPublicationManagement.cs` and `PublicationSlot` / `PublicationSlotIdentity` / `PublicationSlotTransitionResult` from `src/Elsa/Workflows/Publishing/Core/Models/PublicationAuthority.cs`, plus the in-memory slot implementation in `src/Elsa/Workflows/Publishing/Services/InMemoryPublicationStores.cs` and its registration in `WorkflowsPublishingFeature.cs`. Keep `PublicationRecord`, publication policies, and `IPublicationRecordStore` — publishing-internal types keep publication naming in their own domain.
- [ ] T029 Refactor `src/Elsa/Workflows/Publishing/Services/PublicationActivator.cs` into a **caller** of `IWorkflowActivationCoordinator`, retaining only compilation-adjacent concerns and `PublicationRecord` bookkeeping wrapped **around** the coordinator call. It MUST NOT implement a parallel copy of the activation sequence.
- [ ] T030 Retarget `src/Elsa/Workflows/Publishing/Handlers/PublishWorkflowRequestHandler.cs` and `src/Elsa/Workflows/Publishing/Handlers/PublishReconciledWorkflowVersions.cs` to request activation through the coordinator, preserving the existing **slot-first read** pattern. `PublicationRecord.Status` is publishing's journal of requests to the authority — it MUST NOT be consulted to decide serving, and any Status/slot divergence resolves in favor of the slot.
- [ ] T031 Retarget the Publishing.Api slot surfaces to the runtime authority: `Api/Endpoints/PublicationSlots.cs`, `Api/Endpoints/PublicationSlotLifecycle.cs`, `Api/Handlers/PublicationSlotLifecycleRequestHandlers.cs`, `Api/Requests/PublicationSlotLifecycleRequests.cs`, `Api/Models/PublicationManagementViews.cs`, and `Services/WorkflowPublicationPreflightReader.cs` + `Services/PublishedWorkflowDeletionGuard.cs`. **Public HTTP response shapes are unchanged** — this is an internal retarget, not an API contract change.
- [ ] T032 **Rename sweep (1/4) — projection-store members** (pure compile-time renames; method names are never persisted): `PreparePublicationAsync` → `PrepareActivationAsync`, `ActivatePublicationAsync` → `ActivateAsync`, `DeleteByPublicationAsync` → `DeleteByActivationAsync`, `ListByPublicationAsync` → `ListByActivationAsync` across `src/Elsa/Workflows/Runtime/Core/Contracts/IWorkflowTriggerBindingStore.cs`, `IRecurringTriggerScheduleStore.cs`, `WorkflowTriggerBindingStoreExtensions.cs`, and every implementation and call site. **No grandfathering** — do not leave obsolete aliases.
- [ ] T033 **Rename sweep (2/4) — persisted fields**: `PublicationId` → `ActivationId` on `src/Elsa/Workflows/Runtime/Core/Models/WorkflowTriggerBinding.cs` (incl. the `BuildId` parameter), `WorkflowExecutableSourceReference.cs`, `WorkflowExecutableSourceSelection`, and the projection-state documents. §E6's wire-value protection exists to avoid breaking consumers — there are none, and the schema baselines already move for the ledger relocation.
- [ ] T034 **Rename sweep (3/4) — Groundwork storage layer**: `PublicationIdField` and sibling manifest field constants, the by-publication index → by-activation, and `src/Elsa/Persistence/Groundwork/Stores/GroundworkPublicationProjectionStore.cs` → activation naming, propagating to `GroundworkWorkflowTriggerBindingStore.cs` and `GroundworkRecurringTriggerScheduleStore.cs`.
- [ ] T035 **Rename sweep (4/4) — retire-reason literals**: `"activation-replaced"` / `"activation-failed"` replace the publication-rooted literals everywhere they are written or asserted. **Kept unchanged and explicitly out of the sweep**: `SlotId` (already neutral, §E6-protected noun) and the source-provenance facts `WorkflowExecutableReferenceScope.Published` and `PublishedAt` — they describe the design-side event that produced the reference, not activation machinery. `Scope` stays a pure provenance axis: **there is no `Activated` scope and none may be added.**
- [ ] T036 Update the Groundwork **historical-schema and target baselines deliberately and by name**: `tests/Elsa/Persistence/Groundwork/UnifiedHost/Tests/HistoricalSchemaUpgradeTests.cs` and `tests/Elsa/Persistence/Groundwork/DesignConformance/Sqlite/Tests/GroundworkTargetBaselineTests.cs`. The slot document kind moves families and the projection field constants rename — both are legitimate pre-1.0 changes with no consumers. **Silent baseline churn is the failure mode to avoid**: the commit message must name the moved kind and the renamed constants (research risk R2).
- [ ] T037 [P] §2.23.2 branch-covered tests for `WorkflowActivationCoordinator` in `tests/Elsa/Workflows/Runtime/Tests/`, inheriting publishing's activator matrix: failure injected between **each pair** of steps in the sequence, asserting the compensation invariants (replaced activation restored, its projections re-activated with `forceReplay`, candidate projections removed, failed reference retired) — research risk R6 names this as the branch-heaviest path.
- [ ] T038 [P] §2.23.2 tests for the conflict rules (T025): same-artifact-any-source no-op; CAS conflict on stale `Revision`; non-owner different-artifact rejection whose diagnostic **names the owning source**; and a negative test asserting **ownership is not inferred from id prefixes** (an `import:`-prefixed activation id owned by publishing still resolves as publishing-owned).
- [ ] T039 [P] §2.23.1 registration tests: the authority and coordinator resolve from `AddWorkflowRuntime()` (in-memory) and from runtime Groundwork composition (durable), and **no publishing-family slot registration remains**.
- [ ] T040 §2.21.1 golden-rule verification for the whole of 2C: run the existing `tests/Elsa/Workflows/Publishing/Tests` and `tests/Elsa/Workflows/Publishing/Api/Tests` suites and confirm every change needed was **wiring or naming only** — no objective/assertion changes. Any test whose *expected behavior* had to change is a defect in the extraction, not in the test.

### 2D — Close the censused back doors (FR-B-006 single-writer rule; all three are v1 requirements)

- [ ] T041 Remove the default-interface fallback `PreparePublicationAsync => IndexAsync` at `src/Elsa/Workflows/Runtime/Core/Contracts/IWorkflowTriggerIndexer.cs:26-31` **and** the artifact-scoped `IndexAsync` write path in `src/Elsa/Workflows/Runtime/Services/WorkflowTriggerIndexer.cs` and `src/Elsa/Workflows/Runtime/Scheduling/RecurringTriggerScheduleIndexer.cs` (delete-by-artifact + per-row save with `PublicationId = null` and bindings born `IsActive = true`). This is the census's **most dangerous** finding: zero live callers, but any indexer implementing only the documented `IndexAsync` signature is silently routed into an activate-bypassing artifact-wide wipe. After removal a partial implementation **fails loudly** instead.
- [ ] T042 Update `src/Elsa/Workflows/Runtime/EXTENSION_POINTS.md:263` to advertise the **activation-scoped** signature as the `IWorkflowTriggerIndexer` extension-point contract, removing the now-invalid `IndexAsync` documentation.
- [ ] T043 Route the activation-owned schedule-row hard-deletes at `src/Elsa/Workflows/Runtime/Scheduling/RecurringTriggerPumpTask.cs:189` and `:196` (invalid expression, exhausted cron) through the coordinator — or replace them with deactivation + diagnostic. Today they delete outside any activation lifecycle, so restore/compensation later re-prepares an empty set. **`TryAdvanceAsync` (`:202`) is explicitly carved out** as legitimate operational fire-cursor state, not activation authority — do not route it.
- [ ] T044 Collapse the schedule-prepare **double-write** in `src/Elsa/Workflows/Publishing/Services/PublicationProjectionReconciler.cs` — currently nested inside the binding intent at `:35-37` and re-prepared under its own intent at `:54`, with a short-circuit hazard at `:199-200` — into **one coordinator-owned write per projection**.
- [ ] T045 [P] §2.23.2 tests for T041: an indexer implementing only the legacy signature now fails to compile/register rather than silently bypassing prepare/activate; the artifact-scoped write path is gone.
- [ ] T046 [P] §2.23.2 tests for T043 in `tests/Elsa/Workflows/Runtime/Scheduling/Tests/`: invalid-expression and exhausted-cron removals go through the coordinator (or deactivate + diagnose) and survive a subsequent restore/compensation; `TryAdvanceAsync` still advances the fire cursor without touching activation state.
- [ ] T047 [P] §2.23.2 test for T044: exactly one schedule write per activation, and the `:199-200` short-circuit no longer skips a needed write.

### 2E — Closure envelope model (FR-B-001 / FR-B-010)

- [ ] T048 [P] Add `WorkflowArtifactClosure` to `src/Elsa/Workflows/Runtime/Core/Models/` per [data-model.md](data-model.md) and [contracts/closure-envelope.md](contracts/closure-envelope.md): `FormatVersion` (int, starts at 1), `RootArtifactId`, `Artifacts`, `SourceReferences`, `TriggerBindings`. Shared by Publishing (export) and Reconciliation (import), so it rides `Runtime.Core` — the direction both extractions already use.
- [ ] T049 Implement envelope serialization through `IPayloadSerializer` with the **same converter discipline the Groundwork runtime document serializer uses** (drop the recomputed projections `Nodes`/`NodesById`, which the ctor rebuilds), so store-round-tripped and exported artifacts are byte-consistent.
- [ ] T050 [P] Implement **fail-loud `FormatVersion` parsing** mirroring `ElsaRuntimeDocumentVersions.Parse`: readers accept exactly the versions they know; unknown or newer → loud rejection, no silent upcast, no partial import.
- [ ] T051 [P] §2.23.2 tests for the envelope: round-trip fidelity, projection-drop correctness, unknown/newer `FormatVersion` rejection, missing-`RootArtifactId` rejection.

**Checkpoint**: all three extractions complete, one activation coordinator owns the lifecycle, no publishing slot store exists, the three back doors are closed, and the envelope model round-trips. Existing publishing/runtime suites pass with wiring-only changes (T040). **User stories may now proceed.**

---

## Phase 3: User Story 1 — Execute mounted artifacts on a design-free runtime (P1)

**Goal**: a runtime composed with execution features only imports and executes artifacts from a mounted folder, including trigger-started workflows.

**Independent test**: compose an engine with runtime execution + artifact reconciliation and **no** design/activity-design/publishing features; mount one valid dependency-satisfied artifact; start; execute to completion; assert no design/publishing assembly is loaded.

- [ ] T052 [P] [US1] Add `IWorkflowArtifactReconciliationSource` (`SourceId`, `SourceKind`, `ReadAsync → IAsyncEnumerable<WorkflowArtifactClosureFile>`) and `WorkflowArtifactClosureFile(Origin, WorkflowArtifactClosure)` to `src/Elsa/Workflows/Runtime/Reconciliation/Core/Contracts/`, mirroring `src/Elsa/Workflows/Design/Reconciliation/Contracts/IWorkflowReconciliationSource.cs`'s self-identification shape.
- [ ] T053 [P] [US1] Add `JsonWorkflowArtifactReconciliationOptions` to `Reconciliation/Core/Options/`: exactly one of `FilePath` | ordered `Files: [{Order, FilePath}]` | `FolderPath`; `SourceId` required; **`TenantId` nullable, default null** (stamped on minted references; per-tenant fan-out deferred). Mirror `JsonWorkflowReconciliationOptions` including its non-recursive top-level `*.json` ConfigMap rationale and ordinal filename ordering.
- [ ] T054 [P] [US1] Add the §2.23.5 domain-exception taxonomy to `Reconciliation/Core/Exceptions/`: `InvalidWorkflowArtifactClosureException(path, reason, inner)` for **file-level** parse/format/version failures (mirrors `InvalidWorkflowCatalogJsonException`), and the `WorkflowArtifactReconciliationException` family for **pass-aborting** conditions. **No raw `JsonException` / `IOException` may escape.** Per-artifact rejections are diagnostics on the pass result, never exceptions (batch isolation).
- [ ] T055 [P] [US1] Add `WorkflowArtifactReconciliationResult` with per-artifact outcomes `Imported | AlreadyCurrent | Skipped(olderVersion) | Rejected(diagnostic)` to `Reconciliation/Core/Models/`.
- [ ] T056 [US1] Implement the JSON folder/file source in `src/Elsa/Workflows/Runtime/Reconciliation/Services/JsonWorkflowArtifactReconciliationSource.cs`, mirroring `JsonWorkflowReconciliationSource.cs:78,99-102`: **missing folder → pass-aborting error; empty folder → no-op**.
- [ ] T057 [US1] Implement `IWorkflowArtifactReconciler` / `WorkflowArtifactReconciler` in `src/Elsa/Workflows/Runtime/Reconciliation/Services/` — the pipeline skeleton and per-source pass loop. Gates land in T058/T059 and US2/US4; this task establishes ordering and the result accumulation.
- [ ] T058 [US1] Implement pipeline **step 1 (parse + format gate)** and **step 2 (closure/dependency validation against the envelope alone)**: every `Dependencies` edge of every member must resolve **within `Artifacts`**, with declared-hash equality, no identity conflicts, no cycles (`MissingArtifact` / `HashMismatch` / `ConflictingIdentity` / `Cycle`). **Validate against the envelope, never the store** — FR-B-010 promises a self-contained closure, so a file must fail identically on every runtime; the store snapshot is consulted only afterward for idempotent skip-persistence.
- [ ] T059 [US1] Implement pipeline **step 2a (content-hash recompute)**: before any member persists, recompute its canonical hash via `IWorkflowExecutableHasher` (T017) and compare against `Identity.ArtifactHash` and the id-embedded hash prefix; mismatch → broken-source diagnostic rejecting the member and its dependents. This guards the ADR 0038 content-addressing invariant (equal hash ⇔ equal behavior) against corruption — an unverified payload must never become the stored content for a content-addressed id. It is **not** tamper-proofing; signing stays deferred.
- [ ] T060 [US1] Implement **explicit topological ordering** over the validated graph — `WorkflowExecutableDependencyGraph.ResolveClosure` returns results sorted by artifact id/hash (`WorkflowExecutableDependencyGraph.cs:56-60`), **not** dependency-first. Persist all artifacts first (order-free: the store is create-only), then activate dependencies-first so a parent never activates while a child's reference is absent.
- [ ] T061 [US1] Implement pipeline **step 5 (activate)** as a **single request to `IWorkflowActivationCoordinator`** carrying artifact, definition/slot, the importer's `WorkflowActivationSource`, minted activation id, and tenant option. **The importer MUST NOT implement any part of the activation sequence** — no lease handling, no projection writes, no observer notification, no compensation. Its recovery unit is the next reconcile pass.
- [ ] T062 [US1] Implement source-reference minting per [data-model.md](data-model.md): `SourceKind`/`SourceId` from the source, `Scope = Published`, `ActivationId` from the coordinator, `SlotId` importer-derived (default slot per definition), `TenantId` from the option, identity fields copied from the artifact. **Never mint or rewrite artifact identities** — content-addressed ids are stable by design. Write artifacts **only** through `IWorkflowExecutableStore.SaveAsync`, never as raw documents: the store's private `ExecutableDocument` shape carries legacy lease/guard fields the importer must not touch (research risk R3 — mitigated by this constraint, so do not work around it).
- [ ] T063 [US1] Implement trigger-binding **recomputation** via the runtime trigger indexer's prepare path (deterministic `WorkflowTriggerBinding.BuildId`) — the envelope's carried bindings and references are **provenance/expectations only and are never persisted**; the exporting engine's activation ids are meaningless here. A node/stimulus-set mismatch between recomputed and carried surface is a broken-source diagnostic.
- [ ] T064 [US1] Add the abstract `WorkflowsArtifactReconciliationFeature` (no `[ShellFeature]` attribute) to `src/Elsa/Workflows/Runtime/Reconciliation/`, mirroring `WorkflowsDesignReconciliationFeature`'s inheritance shape (§2.24.2 pattern #2).
- [ ] T065 [US1] Add the concrete `JsonWorkflowArtifactReconciliationFeature` with `[ShellFeature]` id **`JsonWorkflowArtifactReconciliation`**, depending on `Tasks` and `WorkflowsRuntimeTriggers` (the binding/schedule/indexer spine is registered by the triggers feature, **not** by `AddWorkflowRuntime()`) and calling `AddWorkflowRuntime()` itself (idempotent per ADR 0029).
- [ ] T066 [US1] Add `WorkflowArtifactReconcilerStartupTask` in `Reconciliation/Startup/` with `[SingleNodeTask]` + distributed lock (`TryAcquireLockAsync(nameof(...))`, null lock → log + return), mirroring `WorkflowsVersionReconcilerStartupTask`. **MUST complete before readiness.**
- [ ] T067 [US1] Order the startup task **after** `RegisterActivityTypesStartupTask` via `[TaskDependency(typeof(RegisterActivityTypesStartupTask))]` — the import gate's type-presence axis is meaningless before the assembly scan completes. Verify the attribute accepts a cross-assembly type (research risk R4); documented fallback is `[Order]` above the scan task's order.
- [ ] T068 [P] [US1] §2.23.1 registration tests in the new test project: abstract base via a test double, concrete Json feature, and the startup task's single-node/lock/ordering attributes.
- [ ] T069 [P] [US1] §2.23.2 tests for the JSON source: folder scan ordering, explicit ordered files, single file, missing folder aborts, empty folder no-ops.
- [ ] T070 [US1] **SC-B-001/005 composition assertion test** in `tests/Elsa/Architecture/`: a runtime-only composition executes a mounted artifact end-to-end (including a trigger-started workflow) while asserting **no `Elsa.Workflows.Design.*`, `Elsa.Workflows.Publishing*`, or `Elsa.Activities.Design.*` assembly is loaded**. This is the claim the feature exists to serve — assembly-enforced, not documentation.
- [ ] T071 [US1] End-to-end test for US1 acceptance scenarios 1–2: mounted valid artifact reaches the executable store and runs to completion; a trigger-started (HTTP/timer) artifact routes its stimulus and executes — proving **both** projections were activated.

**Checkpoint**: US1 independently testable — a design-free runtime imports and executes mounted artifacts, MVP delivered.

---

## Phase 4: User Story 2 — Reject artifacts the runtime cannot execute (P1)

**Goal**: unsatisfiable artifacts are rejected **at import** with a diagnostic naming what is missing, never faulting at first activation.

**Independent test**: mount an artifact declaring an unsatisfied requirement; reconcile; assert rejection with a clear diagnostic and no activation, while a satisfied artifact in the same batch still activates.

- [ ] T072 [US2] Wire the **two-axis import gate** (FR-B-005a) into the pipeline as step 3, calling `IRuntimeRequirementChecker` (T008) per artifact. **Failing either axis rejects the artifact** with a diagnostic naming the missing requirement; it is never activated. Fold the T063 trigger-surface cross-check in here.
- [ ] T073 [US2] Implement **closure-unit isolation** (P6): steps 1–4 complete for the **entire closure unit** (root + transitive dependencies) **before any write**; any member failing any gate rejects the whole unit; **a failed unit writes nothing** — no sibling persistence. Isolation across the mounted set is per closure unit, so one bad unit never fails the batch.
- [ ] T074 [P] [US2] Implement the reject-with-diagnostic surface: every rejection is a named diagnostic on `WorkflowArtifactReconciliationResult` **and** a log entry — per-artifact rejections are diagnostics, never thrown exceptions (batch isolation).
- [ ] T075 [P] [US2] §2.23.2 tests: unregistered activity type rejected at import (US2 scenario 1); unmet storage-driver requirement rejected (scenario 2); incompatible consumer schema rejected; each diagnostic **names** the missing requirement.
- [ ] T076 [P] [US2] §2.23.2 tests for the **mixed batch** (US2 scenario 3): satisfiable closure units activate, unsatisfiable ones are rejected individually, and a unit whose *dependency* fails a gate writes **nothing at all** — assert the store is untouched for every member of the failed unit.
- [ ] T077 [P] [US2] §2.23.2 tests for the remaining edge cases in spec.md: missing child dependency rejects the parent; malformed/truncated artifact → clear error, no partial import; unknown/newer `FormatVersion` → loud rejection; hash-mismatch → broken-source diagnostic before persistence.
- [ ] T078 [US2] **SC-B-002 test**: assert the failure surfaces at reconcile and **never** as a first-activation `UnknownActivityTypeException`; pair it with the T012 classification so the defense-in-depth path (an artifact that somehow activates past the gate) classifies as a non-retryable deployment incident.

**Checkpoint**: US2 independently testable; US1 + US2 together are the full runtime-side import story.

---

## Phase 5: User Story 3 — Export a portable executable artifact with its closure (P1)

**Goal**: a publish-capable engine produces a portable closure unit and delivers it through a pluggable target; the v1 built-in target is an API download.

**Independent test**: publish a workflow that dispatches a child; export; verify the unit contains the child artifact(s); import into a fresh runtime and confirm the parent executes the child.

- [ ] T079 [P] [US3] Add `IWorkflowArtifactClosureFactory` to `src/Elsa/Workflows/Publishing/Contracts/` per [contracts/runtime-contracts.md](contracts/runtime-contracts.md) — `CreateAsync(definitionVersionId) → WorkflowArtifactClosure`, destination-agnostic. `…Factory` is the sanctioned §E6 R4 suffix ("constructs"); "Producer" is not codified.
- [ ] T080 [US3] Implement the closure factory in `src/Elsa/Workflows/Publishing/Services/`: read the executable + source-reference + trigger-binding stores (all already inside Publishing's envelope) and walk `Dependencies` transitively. **Restricted to `Scope == Published` references (FR-B-011)** — `TestRun`-scope references are expiring, tied to a `WorkflowTestScope`, carry `draft:` version ids, and are non-portable. Throw domain exceptions for missing dependencies (export never emits an incomplete closure) and for non-Published references.
- [ ] T081 [P] [US3] Add the export-target seam `IWorkflowArtifactExportTarget` (`TargetId`, `DeliverAsync(closure) → WorkflowArtifactExportDelivery`) and `WorkflowArtifactExportDelivery(TargetId, Kind: InlinePayload | Receipt, Payload?, Location?)` to `src/Elsa/Workflows/Publishing/Core/Contracts/` — Strategy (§2.24.2 #9), fan-in via `TryAddEnumerable`, symmetric to the import source. Future targets **contribute, never replace**. `…Target` is not R4-codified: it is the domain term pinned by FR-B-010a, flagged for reviewer judgment (research D8).
- [ ] T082 [US3] Implement the single v1 built-in target `DownloadWorkflowArtifactExportTarget` (`TargetId = "download"`, `Kind = InlinePayload`) in `src/Elsa/Workflows/Publishing/Api/Services/`. Folder-writer and blob-push are **deferred targets on the same producer** — do not implement them.
- [ ] T083 [US3] Add the route constant `publishing/workflows/{versionId}/executable-export` to `src/Elsa/Workflows/Publishing/Api/Constants/RouteConstants.cs`, reusing the existing `VersionIdConstraint` (`regex(^(?!drafts$).+$)`).
- [ ] T084 [US3] Add a new read-shaped permission to `src/Elsa/Api/FastEndpoints/Constants/PermissionNames.cs` (distinct from `WorkflowPublishingManage`; resolve the final constant name against the file's existing conventions) and apply it via `ConfigurePermissions(...)` per the endpoint convention used at `Api/Endpoints/PublishWorkflow.cs:26`.
- [ ] T085 [US3] Implement `GET publishing/workflows/{versionId}/executable-export` in `src/Elsa/Workflows/Publishing/Api/Endpoints/`. **The GET route binds to the `download` target ONLY** — GET is a safe method and receipt-producing targets are external side effects that crawlers, retries, and caches may repeat. **There is no `?target=` selector in v1**; when a side-effecting target ships it arrives with its own POST command endpoint carrying an explicit idempotency contract, defined with that feature. See the endpoint-framework resolution at the top of this file for why this is FastEndpoints.
- [ ] T086 [US3] Implement the response per [contracts/export-endpoint.md](contracts/export-endpoint.md): 200 with closure JSON (`application/json`) and `Content-Disposition: attachment; filename="{definitionId}-{artifactVersion}-closure.json"` (safe-name rules; filename shape shared with studio#493); 404 unknown version / no Published reference; 409 test-run-only version; 409 incomplete closure naming the missing artifact id(s). No FastEndpoints byte-download precedent exists — use `Send.StringAsync(json, 200, "application/json")` plus a manual header via a small response helper placed beside `ServerSentEventResponseExtensions` in `src/Elsa/Api/FastEndpoints/`.
- [ ] T087 [US3] Add the capability rel to `src/Elsa/Workflows/Publishing/Api/Capabilities/PublishingApiCapabilities.cs` `StaticDeclaration` under capability id **`elsa.api.publishing`**: `{ "rel": "workflow-executable-export", "href": "publishing/workflows/{versionId}/executable-export", "templated": true }`. **These strings are pinned verbatim for studio#493** — do not adjust spelling. Review `contractVersion` per the capability doc rules (additive link → no major bump expected, research risk R5).
- [ ] T088 [P] [US3] Produce the OpenAPI contract fragment alongside the endpoint, mirroring `specs/148-authoring-schema-endpoints/contracts/management-api.openapi.yaml` practice. `elsa.api.publishing`'s enumerated capability ids are unchanged — rel additions are data, not schema.
- [ ] T089 [P] [US3] §2.23.2 tests for the closure factory: transitive closure walk (parent → child → grandchild), Published-only enforcement, `TestRun`/draft exclusion (US3 scenario 6), missing-dependency rejection.
- [ ] T090 [P] [US3] §2.23.1 + §2.23.2 tests for the export target seam and download target (fan-in registration resolves the built-in target; `InlinePayload` delivery) and for the endpoint handler (all four response cases from T086), plus a `DomainApiCapabilityRegistrationTests` assertion that the new rel is advertised.
- [ ] T091 [US3] **Reconcile the FastEndpoints transition registry** for `Elsa.Workflows.Publishing.Api` per ADR 0068. This is **inventory bookkeeping, not a new architectural exception**: the module is already wholly transitional — all 19 of its existing registrations carry `removalOwner: "First-party REST API Consolidation"`, so the 20th route inherits that exit condition and migration wave. **Registry mechanic (verified against the #1346 branch — do not mistake this for a one-row edit)**: `sourceHash` is an **owner fingerprint**, `SHA256` over *every* `.cs` file in the owning project (`FastEndpointsRegistrationScanner.cs:29-31,51`), and all 19 Publishing.Api rows share one value. Spec 151 edits that project in T010, T011, T031, and T082–T087, so **every** Publishing.Api row's `sourceHash` must be restamped, plus a new row added for the export endpoint. Execution depends on merge order: if `tests/Elsa/Architecture/Baselines/fastendpoints-transition-exceptions.json` exists at implementation time ([#1346](https://github.com/elsa-workflows/elsa-foundation/issues/1346) merged), restamp + add there; if not, record the pending row in [contracts/export-endpoint.md](contracts/export-endpoint.md) so it lands with that branch. No hard dependency on merge order either way.
- [x] T091a **DONE 2026-08-15 — do not repeat** ([#1346 comment](https://github.com/elsa-workflows/elsa-foundation/issues/1346#issuecomment-5303337529)). Raised the registry-collision finding with the #1346 owner. Carries no story label: it was cross-branch coordination, not spec-151 implementation work. Recorded here for the audit trail; the content it raised was: Two points: **(1)** the owner-fingerprint design means *any* edit to *any* file in a FastEndpoints-owning project invalidates every registry row for that owner — with 106 registrations across 21 owners, every concurrent branch collides and must restamp; **(2)** a rule question ADR 0068's text does not resolve — does a route added to a module that is *already wholly transitional*, inheriting an existing `removalOwner` and wave, require a fresh approved compatibility exception, or is it bookkeeping under the module's existing entry? This task list assumes the latter. **Open until answered**: if the ADR owner rules otherwise, obtain the architect approval and record the approving reviewer + linked PR on the new row (T091). A related coordination note was also posted to [#1358](https://github.com/elsa-workflows/elsa-foundation/issues/1358#issuecomment-5303338640), telling that issue's fix not to add a fourth writer of serving state.
- [ ] T092 [US3] Amend [contracts/export-endpoint.md](contracts/export-endpoint.md) with the endpoint-framework resolution (ADR 0068, capability gap, containment, registry entry, removal follow-up) so the contract doc stops implying FastEndpoints was the unexamined default.
- [ ] T093 [US3] **SC-B-003 round-trip test**: export a parent-with-child closure from a publish-capable engine and import it into a fresh runtime that never saw the source definitions; assert parent + child execute with behavior parity versus compile-in-place (US3 scenario 5).

**Checkpoint**: US3 independently testable; the export→import round trip closes and studio#493 is unblocked.

---

## Phase 6: User Story 4 — Idempotent re-import & version supersession (P2)

**Goal**: re-reconciling is a no-op; a newer version supersedes; activation never moves backward.

**Independent test**: import v1, execute; add v2, reload the shell, execute — v2 active, exactly one active version per definition; reconcile the same set again — no duplication or corruption.

- [ ] T094 [US4] Implement pipeline **step 4 (idempotency)**: an artifact already in the store with the same id is a content-addressed no-op (`SaveAsync` is create-only; `ConcurrencyConflict` means already-exists). Same `(DefinitionId, ArtifactVersion)` claimed with **different content** → broken-source diagnostic in the shape of `ActivityVersionHashMismatchException` (the typed throw is safe here because artifacts are content-addressed; the design reconciler's log-only behavior is the weaker precedent).
- [ ] T095 [US4] Implement **latest-wins supersession** using the **SemVer sort key** over `ArtifactVersion` (`SemVer.ToSortKey` in `Elsa.Primitives.Versioning` + ordinal compare — the same comparator `WorkflowsVersionReconciler.cs:78-85` and the design version store use, so design and runtime engines order versions identically). The active version is read from the **active activation's minted source reference** (which carries `ArtifactVersion`) — no new state. Candidate sort key ≤ active → skip (equal + same content = the idempotent no-op path; equal + different content = the T094 diagnostic). Activation MUST NOT move backward onto an older artifact.
- [ ] T096 [US4] Reject an `ArtifactVersion` that does **not parse as SemVer** with a clear diagnostic — latest-wins requires orderability, so an unorderable version is unimportable.
- [ ] T097 [P] [US4] §2.23.2 tests: v1 → v2 supersession activates v2 and deactivates v1 with the predecessor's reference retired as `"activation-replaced"`; a v1 candidate arriving after v2 is skipped (no backward activation); unparseable `ArtifactVersion` rejected.
- [ ] T098 [P] [US4] **SC-B-004 test**: N repeated reconciles over an unchanged mounted set yield **exactly one active version per definition**, no duplicate records, no corruption. Assert additionally that every artifact id is **byte-identical across all N passes** — the spec's "artifact id not pinned" edge case requires that the importer never mints a fresh identity per reconcile, and only a cross-pass comparison catches a regression there.
- [ ] T099 [US4] Test the **crashed half-import heal**: inject a failure mid-activation, assert the coordinator's compensation restores the replaced activation, then assert the **next reconcile pass** completes the import — the importer's recovery unit is the next pass, and no importer-side journal is introduced (symmetric bookkeeping is an explicit non-goal).
- [ ] T100 [US4] Test **re-reconciliation via the existing shell-reload path** (reloading a shell re-runs its startup tasks) — no new trigger coordinator is in scope (#1303 deferred).

**Checkpoint**: US4 independently testable; the promote/rollout loop is operationally sound.

---

## Phase 7: User Story 5 — Design and execution coexist in one engine (P2)

**Goal**: the combined engine is unchanged, and the shared authority resolves dual-path claims by explicit ownership.

**Independent test**: on a combined engine, author → publish → execute in-process still passes; enabling export/import alongside does not regress it.

- [ ] T101 [P] [US5] Regression test for US5 scenario 1: a combined engine with the new features enabled authors, publishes, and executes in-process with behavior unchanged from today.
- [ ] T102 [US5] End-to-end test for US5 scenario 2 (the feature's sharpest invariant): with **both** design-side workflow version reconciliation and executable artifact reconciliation enabled, the same definition arriving through both paths resolves by explicit ownership — **same artifact → idempotent no-op; different artifact from the non-owning source → loud rejection naming the owning `WorkflowActivationSource`**. Assert the definition is **never double-activated** and that a single stimulus **never starts two instances**. Assert the importer-side rejection is per-closure-unit (batch continues) and the publish-side surfaces on the existing preflight conflict path.
- [ ] T103 [P] [US5] Test **FR-B-009 independent composability**: an engine may enable design-side reconciliation, artifact reconciliation, both, or neither — all four compose and start.
- [ ] T104 [P] [US5] Test **FR-B-012 provenance rendering**: design-provenance ids that do not resolve on a runtime-only engine render as opaque/unresolved on inspection surfaces rather than erroring. *(The stale pre-sweep doc literals this task previously also carried were corrected in the spec artifacts on 2026-08-15 — see the P10 pin — so nothing doc-shaped remains here.)*

**Checkpoint**: all five user stories independently testable.

---

## Phase 8: Polish & cross-cutting obligations

**Every task here is a constitutional or CI gate, not optional cleanup.**

- [ ] T105 [P] Create `src/Elsa/Workflows/Runtime/Reconciliation/EXTENSION_POINTS.md` (§2.22.1) cataloguing the source contract, the reconciler, the feature inheritance point, and the options surface.
- [ ] T106 [P] Update `src/Elsa/Workflows/Runtime/EXTENSION_POINTS.md` with the requirements checker, the activation authority + coordinator, and the closure envelope (the `IWorkflowTriggerIndexer` signature correction is already covered by T042).
- [ ] T107 [P] Update `src/Elsa/Workflows/Publishing/EXTENSION_POINTS.md` (closure factory; slot contract **removed**) and `src/Elsa/Workflows/Publishing/Api/EXTENSION_POINTS.md` (export-target seam, download target, capability rel).
- [ ] T108 Add the new Reconciliation catalog to the **root `EXTENSION_POINTS.md` index** — a catalog that is not linked from the root index does not satisfy §2.22.1 (§2.22.2: the index is pure links, no inline entries).
- [ ] T115 [P] Create `src/Elsa/Workflows/Runtime/Reconciliation/README.md` — the **§2.22 per-feature documentation** obligation, which is distinct from the §2.22.1 domain catalog (T105) and not satisfied by it. Minimum required content: the event handlers the feature registers, the contributor interfaces it implements and registers via DI, and **the tasks it registers with their cadence** — here `WorkflowArtifactReconcilerStartupTask` (single-node, distributed-locked, runs at shell activation before readiness, ordered after `RegisterActivityTypesStartupTask`, re-runs on shell reload). Follow the established repo convention (46 existing `README.md` files); the two closest precedents are `src/Elsa/Workflows/Design/Reconciliation/README.md` (the family this mirrors) and `src/Elsa/Workflows/Runtime/ReferenceGarbageCollection/README.md` (the sibling-project shape). *ID is out of sequence deliberately — T001–T114 are referenced by a pushed commit and are not renumbered.*
- [ ] T109 Register the new feature assemblies in `src/Apps/Elsa.Workbench/Program.cs` (the assembly list around `:267`, beside `WorkflowsRuntimeReferenceGarbageCollectionFeature`), plus the matching `using`. Optionally add a `shells.json` demo entry per [quickstart.md](quickstart.md).
- [ ] T110 Regenerate the maps: `dotnet run --project tools/maps/Elsa.Maps.Generator -- all`, then **stage the changed maps and `manifest.json` explicitly** — the "Generated maps fresh" CI check is required and two new projects guarantee map drift.
- [ ] T111 **SC-B-006 composition matrix test**: design-only, runtime-only, and combined compositions are each valid and pass their smoke tests.
- [ ] T112 Final §2.21.1 golden-rule sweep across the whole feature: confirm every existing publishing and runtime test that changed did so for **wiring, location, or naming only**. Any changed assertion about behavior is a defect in the extraction — fix the code, not the test.
- [ ] T113 Verify §2.23.5 coverage: no raw `JsonException`, `IOException`, or storage exception escapes the reconciliation or export paths unwrapped.
- [ ] T114 Walk [quickstart.md](quickstart.md) end to end against the built solution (export → runtime-only import → v2 rollout) and correct any drift between the doc and the shipped feature ids, option names, and diagnostics.

---

## Dependencies & execution order

### Phase order

```
Phase 1 (Setup)
   └─> Phase 2 (Foundational — BLOCKING)
          ├─ 2A checker ─┐
          ├─ 2B hasher ──┤
          ├─ 2C activation authority + coordinator  (2C is the critical path)
          ├─ 2D back doors        (depends on 2C's coordinator)
          └─ 2E envelope ─┘
                 └─> Phase 3 (US1, P1)  ──> Phase 4 (US2, P1)
                 └─> Phase 5 (US3, P1)   [needs 2B + 2E only]
                          └─> Phase 6 (US4, P2)
                                 └─> Phase 7 (US5, P2)
                                        └─> Phase 8 (Polish)
```

### Story dependencies

- **US1** requires all of Phase 2 (coordinator, checker, hasher, envelope).
- **US2** extends US1's pipeline with the gate and isolation semantics — implementable immediately after US1's pipeline skeleton exists (T057).
- **US3** needs only 2B (hasher) + 2E (envelope) from Phase 2, so it can run **in parallel with US1/US2** once Phase 2 completes. Its round-trip test (T093) needs US1 landed.
- **US4** extends US1's pipeline with steps 4/supersession.
- **US5** needs US1 (import path) and the publish path retargeted (2C).

### Critical path

`T001–T005 → 2C (T021–T040) → 2D (T041–T044) → T057–T067 → T072–T073 → T094–T095 → T102 → T110`

2C is the long pole: it is the largest cluster, has the widest blast radius (research risk R1), and 2D depends on its coordinator existing.

### Parallel opportunities

- **Phase 1**: T001, T002 in parallel (T003–T005 follow).
- **Phase 2**: 2A, 2B, and 2E are mutually independent and can run concurrently with 2C's contract definition (T021–T022). 2D must wait for T024.
- **Phase 3**: T052–T055 all in parallel (separate files in the new `.Core`).
- **Phase 5**: T079 and T081 in parallel; T088–T090 in parallel after the endpoint lands.
- **Phase 8**: T105–T107 and T115 in parallel; T108 after T105; T110 last among the doc/map tasks (maps must be regenerated after every project/doc add).
- All `[P]`-marked test tasks within a phase run concurrently.

---

## Implementation strategy

**MVP = Phase 1 + Phase 2 + Phase 3 (US1).** That delivers the feature's reason to exist: a design-free runtime that executes mounted artifacts. It is shippable and independently valuable even before export exists, because artifacts can be produced by hand or by a temporary harness.

**Increment 2 = Phase 4 (US2).** Turns "it runs" into "it refuses safely" — SC-B-002, the deploy-time-not-production-time guarantee.

**Increment 3 = Phase 5 (US3).** Closes the loop and unblocks studio#493.

**Increments 4–5 = Phases 6–7.** Operational hygiene and combined-engine safety.

**Do not defer Phase 8.** Maps regeneration (T110) is a required CI gate, and the extension-point catalogs (T105–T108) are §2.22.1 obligations that the review will bounce.

### Task count

| Phase | Tasks | Story |
|---|---|---|
| 1 — Setup | 5 (T001–T005) | — |
| 2 — Foundational | 46 (T006–T051) | — |
| 3 — US1 | 20 (T052–T071) | US1 (P1) |
| 4 — US2 | 7 (T072–T078) | US2 (P1) |
| 5 — US3 | 16 (T079–T093, +T091a) | US3 (P1) |
| 6 — US4 | 7 (T094–T100) | US4 (P2) |
| 7 — US5 | 4 (T101–T104) | US5 (P2) |
| 8 — Polish | 11 (T105–T114, +T115) | — |
| **Total** | **116** | |

## Out of scope — do not generate or expand work for these

- [#1303](https://github.com/elsa-workflows/elsa-foundation/issues/1303) shared trigger-agnostic coordinator (deferred; this feature uses the existing startup-task + shell-reload model)
- Artifact **signing / verification** (recomputed-hash validation **is** in v1 — signing is not)
- **Per-tenant import fan-out** (v1 stamps an optional per-source tenant id, default null)
- **Zip / multi-entry** export packages (v1 unit is one JSON closure envelope)
- **Folder-writer and blob-push** export targets (deferred targets on the same producer)
- **Studio UI** — [elsa-foundation-studio#493](https://github.com/elsa-workflows/elsa-foundation-studio/issues/493), cross-repo, blocked on T085/T087
- [#1358](https://github.com/elsa-workflows/elsa-foundation/issues/1358) GC of orphaned serving rows — the adjacent pre-existing gap found by the writer census, filed separately; **coordinate designs only**, do not fix here
- Changing how **execution** works (already design-free and assembly-enforced)
- Pulling publishing-only publication machinery into the runtime (`IPublicationRecordStore` history, publication policies, preflight views stay in Publishing)
