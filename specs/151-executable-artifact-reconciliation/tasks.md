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

## ⚠ RESOLVED 2026-08-17 — the export endpoint is FASTENDPOINTS (architect exception to ADR 0068)

**Decided by Joey, 2026-08-17. This supersedes the 2026-08-16 Minimal API block, collapsed below along
with the 2026-08-15 one.** The resolution moved three times; the full reasoning and the recorded
exception live in [contracts/export-endpoint.md](contracts/export-endpoint.md).

**What was wrong with the 2026-08-16 reasoning.** It said the `main` merge "landed the whole first-party
Minimal API migration". Waves 1 and 2 landed — **`Elsa.Workflows.Publishing.Api` was not in them.** It
still carries live rows in the transition registry and all ~20 of its endpoints are FastEndpoints. The
Minimal API decision rested on that overstatement.

**Why FastEndpoints.** One Minimal API route in an otherwise wholly-FastEndpoints module would differ
from its siblings on problem details, permissions, metadata and testing — for a single route, in a
module that migrates as one wave regardless. **Be precise: ADR 0068's capability gap genuinely did
close, so this is not compliance but a recorded architect exception on module-consistency grounds.**

**Task dispositions:**
- **T084** — no new permission: `ConfigurePermissions(PermissionNames.WorkflowPublishingRead)`. A
  `.export` action is forbidden by `EndpointSecurityTests`' pinned name map *and* would gate nothing,
  since executable content is already readable under this family.
- **T085** — a FastEndpoints `ElsaEndpoint<TRequest>`, one endpoint per class, like every sibling.
- **T085a — VOID.** `WithOwner` / `WithAuthoringModel` / route-ownership metadata are Minimal API
  obligations; `ConfigurePermissions(...)` supplies the security disposition as it does for every
  sibling. The feature class does **not** implement `IWebShellFeature`, so no feature-map regeneration.
- **T086** — `Send.StringAsync` plus an explicitly written `Content-Disposition`. This endpoint is the
  repo's first FastEndpoints byte-download, so there was no precedent to follow.
- **T091 — BACK IN SCOPE, and done.** Its old text ("DELETE", "46 entries") is stale; the real count was
  23, now 24. Counts move 112→113 and 23→24 with the reason stated in the test. No `sourceHash` restamp
  was needed — measured: the validator compares the fingerprint only for dynamic routes.
- **T091a — its open rule question is now LIVE**, having been moot while the route was a Minimal API:
  does a route added to an already-wholly-transitional module need a fresh approved exception, or is it
  bookkeeping under the module's existing entry? **This feature assumes bookkeeping.** If the ADR owner
  rules otherwise, record an approving reviewer and linked PR on the new registry row.

**Unchanged and still pinned for [studio#493](https://github.com/elsa-workflows/elsa-foundation-studio/issues/493)**:
route `publishing/workflows/{versionId}/executable-export`, capability `elsa.api.publishing`, rel
`workflow-executable-export`, and the response shape. The framework question changed three times; the
contract never moved.

**One thing worth keeping from the Minimal API attempt:** it proved a feature can implement both
`FastEndpointsFeatureBase` and `IWebShellFeature` and have `MapEndpoints` actually fire — unprecedented
here, and the open risk that had blocked the decision. Evidence is preserved in the contract doc, and
will matter when Publishing.Api's migration wave arrives.

<details>
<summary>Superseded 2026-08-16: the export endpoint is a MINIMAL API — kept for the record</summary>

</details>

<details>
<summary>Superseded: endpoint-framework resolution (decided 2026-08-15) — kept for the record</summary>

## Endpoint-framework resolution (decided 2026-08-15, supersedes contracts/export-endpoint.md as written)

[ADR 0068](../../docs/adr/0068-first-party-rest-apis-use-aspnet-core-minimal-apis.md) (accepted 2026-08-15) landed **after** the export-endpoint contract was written citing FastEndpoints, and makes Minimal APIs the normative model for new first-party REST endpoints. Resolution for this feature:

- **The endpoint ships as FastEndpoints**, consistent with the ~20 sibling endpoints in `Elsa.Workflows.Publishing.Api`.
- **Capability-gap evidence** (the ADR's exception bar — "convenience is not an exception"): there is **no shell-scoped Minimal API mapping seam for Elsa module features**. Every `IEndpointRouteBuilder` usage in `src/` today is a host/root surface (`Elsa.Foundation.Host`, `Elsa.Workbench`, `Elsa.Modularity/ExtensionBuilder`); shell-prefixed module routes resolve through FastEndpoints' process-global discovery. The per-shell mapping seam is owned by [#1345](https://github.com/elsa-workflows/elsa-foundation/issues/1345), unlanded. Building it here would pull #1344/#1345 program work into this feature.
- **Containment**: the closure factory and export-target seam stay framework-neutral (no HTTP types), so the migration wave is a mechanical re-host of one handler.
- **This is not a new architectural exception.** `Elsa.Workflows.Publishing.Api` is *already* transitional inventory in its entirety — all 19 of its registrations in [#1346](https://github.com/elsa-workflows/elsa-foundation/issues/1346)'s registry carry `removalOwner: "First-party REST API Consolidation"`. The export endpoint joins that inventory and migrates in the module's wave. Building it as a one-off Minimal API *now* — with no shell-scoped mapping seam and no module migration underway — would create exactly the bespoke surface the ADR's "no Elsa endpoint framework over Minimal APIs" clause warns against, and would leave one route stranded outside the wave that moves the other 19.
- **Registry**: handled by T091, with the cross-branch coordination split into T091a. Note the mechanic: `sourceHash` in that registry is an **owner fingerprint** over every `.cs` file in the owning project, so this feature's edits to Publishing.Api invalidate all 19 existing rows, not just the new one. No hard dependency on #1346's merge order either way.
- **Unchanged**: route, capability id, rel, href, response shape. studio#493's pins hold regardless of framework.

</details>

---

## Verified test baselines (captured 2026-08-15, before any implementation code)

Recorded at commit `100a5497c` so the §2.21.1 golden-rule checks (T040, T112) can tell *my* regression from *already broken*. Verified by running the suites in a clean worktree at that commit — measured, not inferred.

| Suite | Baseline | Note |
|---|---|---|
| `dotnet build Elsa.Server.slnx` | 0 errors, 228 warnings | |
| `Elsa.Workflows.Runtime.Tests` | **1706 passed, 0 failed** | Was 1653 at `100a5497c`; grew with this feature's own tests, then +18 from the 2026-08-16 `main` merge. **Current working number: 1706.** |
| `Elsa.Workflows.Publishing.Tests` | 23 passed, 0 failed | |
| `Elsa.Workflows.Runtime.Api.Tests` | 93 passed, 0 failed | |
| `Elsa.Activities.DispatchWorkflow.Tests` | **202 passed, 0 failed** | **Added to the baseline set 2026-08-16 after it caught a regression nothing else did.** The hasher extraction (T016–T020) deleted `TryAddScoped<WorkflowExecutableHasher>()` from `WorkflowsPublishingFeature`; the compiler then depended on `IWorkflowExecutableHasher`, which only `AddWorkflowRuntime()` registers, so a host composing publishing **standalone** could not construct it. Invisible to Runtime/Publishing/Publishing.Api because they all arm the runtime spine. **Lesson: when a service moves out of a feature, check every composition that used to get it from there** — run this suite after any registration change. |
| `Elsa.Persistence.Groundwork.Tests` | **559 passed, 209 FAILED** | **Already red, and environmental.** Every failure is `System.IO.IOException: The process cannot access the file '…elsa-groundwork-*.db' because it is being used by another process` — the SQLite fixture cannot delete its temp database during teardown on Windows. It hits every Groundwork store test class equally (`…WorkflowExecutableStoreTests` 17, `…RuntimePostCommitOutboxStoreTests` 22, `…WorkflowTriggerBindingStoreTests` 8, …). Test **bodies pass**; only disposal fails, and xunit scores that as a failure. Measured at `b1f395af9` with T026's work stashed. A new store class added here will appear to "fail" ~half its tests for this reason alone — check the exception type before believing it. |
| `Elsa.Architecture.Tests` | **413 passed, 43 FAILED** (measured 2026-08-17; was 330/37 at capture) | **Already red before this feature.** Failures are in `CheckpointFenceEvidenceImporterTests` (16), `GroundworkCoverageLedgerTests` (13), plus `EfCoreSurfaceRatchetTests` and `ArchitectureGuardTests` — checkpoint-fence / mongodb evidence / coverage-ledger assertions, unrelated to spec 151. T040/T112 must compare against 37, not 0, and T070/T111 add new tests to a suite that is not currently green. |

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

## T036 is wider than written, and T033 left named leftovers (found 2026-08-16)

**T036 scope gap.** T036 names only `HistoricalSchemaUpgradeTests` and `GroundworkTargetBaselineTests`. The rename sweep also breaks, and T036 must therefore also cover:
- `tests/Elsa/Persistence/Groundwork/Tests/Fixtures/v2/workflowTriggerBinding.json`, `v5/workflowExecutableSourceReference.json`, `v1/publicationProjectionState.json` — the serialized property names changed.
- `Goldens/runtime.json` — gained `workflowActivationSlot` from T026.
- `GroundworkPersistenceCoverageTests.Checked_in_contract_registration_and_manifest_inventory_reconciles` — also T026's new document kind (this is why `Elsa.Architecture.Tests` now measures **38** failed, not the recorded 37; the 37 was measured at `100a5497c`, before T001–T031 existed, so its per-class breakdown is stale — the delta was verified as T026's, not a rename regression).
- `HistoricalSchemaUpgradeTests` fails `GW-SCHEMA-003` on `add-projected-column:workflowTriggerBinding:by-publication` because T033 had to change `ElsaRuntimeStorageManifest.PublicationIdField`'s **value** (`"publicationId"` → `"activationId"`; the constant *name* is still T034's). Unavoidable: the Groundwork serializer uses `JsonSerializerDefaults.Web`, so the index field path *is* the camelCase property name — renaming the property without it leaves the index pointing at a field that no longer exists.

**Decide explicitly in T036, do not paper over.** The fixture test demands a version bump + upcaster for post-GA evolution; the spec's pre-1.0/no-consumers stance points to a clean-break fixture replacement plus an advanced minimum-readable version. That is a **named decision**, not a silent `GROUNDWORK_FIXTURE_REGEN=1`.

**Leftovers T033 did not cover — assign these to T034 or a follow-up:**
- `RecurringTriggerSchedule.PublicationId` is not in T033's list although `WorkflowTriggerBinding` is, and they are sibling projections written by the same coordinator call. `RecurringTriggerPumpTask.cs:260` now reads `binding.ActivationId == schedule.PublicationId`. Renaming it also moves `RecurringTriggerSchedulePublicationIdField = "schedule.publicationId"`.
- Store members not listed by T032, all still publication-named: `ListByPublicationPageAsync`, `ListAllByPublicationAsync` (×2), the `…PublicationCoreAsync` family, and every `publicationId` / `replacedPublicationId` **parameter** name.
- Model properties on types T033 does not name: `WorkflowTriggerBindingPublicationPageQuery`, `RecurringTriggerSchedulePublicationPageQuery`, `WorkflowExecutableSourceProvenance` (persisted in dispatch documents — renaming moves another schema baseline).
- **Deliberately kept**: `IPublicationProjectionIntentStore.ListByPublicationAsync` (publishing-domain, T028 keeps publishing-internal types on publication naming) and the API view models `WorkflowExecutableInspectionViews` / `WorkflowExecutionViews` (T031 pins public HTTP response shapes unchanged).

## What T034/T035 actually did, and the publication naming that survives them (2026-08-16)

**T034 landed all of the above leftovers**, plus the index identity rename T034 names: `ByPublicationIndex "by-publication"` → `ByActivationIndex "by-activation"`, `by-publication-and-trigger-binding-id` → `by-activation-and-trigger-binding-id`, `by-publication-and-schedule-id` → `by-activation-and-schedule-id`, and `RecurringTriggerScheduleActivationIdField "schedule.activationId"`. `GroundworkPublicationProjectionStore`/`State`/`Transition` are now `GroundworkActivationProjection*` (files renamed); the core family is `PrepareActivationCoreAsync` / `ActivateCoreAsync` / `DeleteByActivationCoreAsync` / `ListAllByActivationCoreAsync`.

**T035 was already satisfied by T027–T031.** `"publication-activation-failed"` and `"publication-replaced"` no longer exist anywhere in `src/` or `tests/`; the only retire-reason writers are `WorkflowActivationCoordinator.ReplacedRetireReason`/`FailedRetireReason` (`"activation-replaced"` / `"activation-failed"`). `"publication-restore-failed"` never existed in this tree.

**Publication naming deliberately left standing — decide in T036 or a follow-up, do not rediscover as a bug:**
- **`"publication-unpublished"`** (`Api/Handlers/PublicationSlotLifecycleRequestHandlers.cs:89`, asserted in `PublishedWorkflowDeletionGuardTests`). **Recommended out of scope for T035**: it records a *publishing* event (an operator unpublished the record), not an activation supersession or failure, and it is written from the publishing domain that T028 keeps on publication naming. It is not a member of T035's target pair.
- **`WorkflowExecutableSourceProvenance.PublicationId`** — **not renamed**. It is persisted inside `WorkflowDispatchRecord.ChildSource`, `WorkflowExecutionState.PinnedSource`, `RuntimeCheckpointCommandPayload.PinnedSource` and `DispatchWorkflowPin`, so renaming it moves the `workflowDispatch`, `workflowExecutionState`, `checkpointCommand` and `workflowTestScope` fixtures — baselines strictly beyond the ones T036 already owns. Its `From(reference)` factory already reads `reference.ActivationId`, so the value is correct; only the field name lags.
- **Query identity constants and values**: `ListTriggerBindingsByPublicationQuery` / `ListRecurringTriggerSchedulesByPublicationQuery` (`"list-by-publication"`) and `PageRecurringTriggerSchedulesByPublicationQuery` (`"page-by-publication"`), plus the `ElsaGroundworkQueryRoutes` route identity `"list-by-publication-bounded"`. T034 names the *index*, not the query. Renaming them additionally breaks `RuntimeBoundedQueryContractTests`, `RuntimeProviderEvidenceScenarios` and `GroundworkPerformanceHandoffTests` (an `Elsa.Architecture.Tests` member) — none of which T036 currently owns. Left standing so the index rename's blast radius stays inside T036's files.
- **`PublicationProjectionStateDocumentKind = "publicationProjectionState"`** and the storage unit id `"runtime-publication-projection-state"`. A document kind is neither a field nor an index, so it is outside T034's written scope; moving its value renames the fixture *file* `Fixtures/v1/publicationProjectionState.json`, the `GroundworkPersistenceReconciler` map row, `EXTENSION_POINTS.md`, and the coverage-ledger unit id.
- **`EndpointRoutingConflictException.CandidatePublicationId` / `ConflictingPublicationId`** (`Workflows/Runtime/Http`) — public exception properties, not storage; outside T034's Groundwork-storage scope.

## FOLLOW-UP — the indexer now silently owns a second projection (found closing T044, 2026-08-16)

**Not a blocker; recorded so it is a decision rather than a discovery.**

Collapsing T044's double-write required moving ownership. The read-back-then-re-prepare in `WorkflowActivationCoordinator.PrepareProjectionsAsync` and `PublicationProjectionReconciler.PrepareAsync` was doing **two** jobs, not one: besides rewriting the same set, it registered the store's prepared-projection marker when the indexer chain had not — `IRecurringTriggerScheduleStore.ActivateAsync` throws *"Activation '…' has no prepared recurring-schedule projection."* So deleting the second write alone **breaks activation**, which is how the second job was found.

The fix makes `RecurringTriggerScheduleIndexer` prepare **unconditionally** (empty set when no providers are composed), so it is the single owner of that projection's preparation. Good outcome — the reconciler now writes 5 records per publication instead of 6, one delivery record governing one write per projection, which is what actually removes the `:199-200` divergence hazard.

**The residual concern.** `IWorkflowTriggerIndexer` is a **§2.6.2 replacement contract** that now *also* silently owns the recurring projection's preparation. Replace it while composing `WorkflowsRuntimeRecurringTriggers` and recurring activation breaks — loudly, but at the activate step, **after the slot CAS**, so it lands in compensation rather than failing fast. This is the same hazard class T041 just closed (a public extension point quietly doing more than it advertises), one level up. Two existing test compositions were doing exactly this and had to be corrected, which is evidence it is easy to get wrong.

**Today the invariant rests on `WorkflowsRuntimeRecurringTriggersFeature` registering the store and the decorator together.**

**Clean fix, deliberately not built here** (a real design change, beyond T044's scope): a separate `IRecurringTriggerScheduleProjectionPreparer` that the coordinator calls beside the indexer, retiring the decorator entirely. Decide it on its own merits — alongside the parked `T044a` — rather than smuggling it into a back-door-closing task.

**Also newly true and previously undocumented:** a binding's `(ActivationId, SlotId)` now drives source-reference selection in `WorkflowStartDispatcher`. Surfaced when `HttpEndpointHostFixture` had to mint an activation-scoped reference. A real consequence of activation-scoped bindings that nothing recorded — relevant to US1's import path.

## ⚠ Phase 2E was skipped — caught 2026-08-16 by the Phase 3 agent, not by me

**Process failure worth recording.** Phase 2 was declared complete after 2D (T041–T047), but **2E (T048–T051, the closure envelope) was never built.** It surfaced only when Phase 3 could not compile: `WorkflowArtifactClosure` did not exist anywhere in the tree. The phase checkpoints in this file are not self-verifying — a phase can be "finished" with unticked tasks inside it. **Check the boxes, not the checkpoint prose.**

Current real state:
- **T048 — done** (built by the Phase 3 agent as a compile prerequisite): the record plus `WorkflowArtifactClosureFormat.CurrentVersion` / `SupportedVersions` / `IsSupported`, with null-collection normalization.
- **T050 — done**: fail-loud format gate in `JsonWorkflowArtifactClosureReader`, unknown/newer version rejected loudly.
- **T049 — DONE 2026-08-17**: `IWorkflowArtifactClosureSerializer` (contract in `Runtime.Core/Contracts`, default `WorkflowArtifactClosureSerializer` in `Runtime/Services`, `TryAddSingleton` in `AddWorkflowRuntime()`). It derives `IPayloadSerializer.GetOptions()` — copy-constructed so the frozen payload options are never mutated — and layers `WithAddedModifier` carrying the Groundwork runtime document serializer's projection drop (`WorkflowExecutable.Nodes` / `NodesById`). Cached on reference identity of the payload serializer's options instance, which tracks its converter-registry revision exactly. **`JsonWorkflowArtifactClosureReader` now decodes through the same codec**, so import and export are literally one encoder. **US3 (T079–T080) is unblocked.**
- **T051 — DONE 2026-08-17**: `tests/Elsa/Workflows/Runtime/Reconciliation/Tests/WorkflowArtifactClosureEnvelopeTests.cs` (13 tests). Round-trip fidelity incl. dependency edges and byte-stable re-encode; projection-drop asserted on the JSON **plus** a guard test proving the bare payload serializer *does* emit both projections (so the drop assertion cannot pass vacuously) **plus** a test that the constructor rebuilds them on decode; a hash-recompute survival test; unsupported `FormatVersion` (2/99/0/-1) rejected loudly by the reader; truncated JSON wrapped with the `JsonException` preserved; missing and non-member `RootArtifactId` rejected by `WorkflowArtifactClosurePlanner`.

**Three notes for review.** (1) The tests live in the **Reconciliation** test project, not `Runtime.Tests`, because that is where `ArtifactClosureFixture` mints content-addressed identities through the production hasher — a round-trip test on a hand-faked artifact would prove much less. (2) The codec deliberately does **not** wrap `JsonException`: §2.23.5's boundary is the reader, which owns the file path; wrapping in the codec would strip the identifier the caller has. (3) The codec deliberately does **not** copy the Groundwork serializer's *added* stimulus lookup-key properties — those are document-index machinery, not artifact content.

## ⚠ GAP IN US2's GATE — a third axis, found 2026-08-16 while writing T071

**FR-B-005a's two axes do not catch a real "imports cleanly, faults at first execution" case.** This is precisely the failure US2 exists to prevent, on an axis US2 does not check. **T072–T078 must close it.**

**What happens.** A compiled artifact's `descriptorType` must be the **consumer key** (`elsa.clr-activity`), not the descriptor's CLR type name. `WorkflowExecutionHarness.NewProbeNode` emits the CLR type name and relies on the harness rewriting it during `PinClrActivityContracts` on save. The importer — correctly — **never rewrites a content-addressed artifact**, so an envelope carrying the unpinned form:
1. passes parse and closure validation,
2. passes the content-hash recompute (the payload genuinely hashes to its id),
3. passes **both** gate axes — consumer capabilities are checked against the requirement set, and CLR type presence resolves fine,
4. activates cleanly,
5. then fails at **first execution** with `UnknownActivityConsumerException` → `Waiting/ArtifactActivationFailed`.

**Why the existing axes miss it.** Axis (a) evaluates the artifact's *declared* `RuntimeRequirements`. Axis (b) checks that each node's CLR **type alias** resolves in `IWellKnownTypeRegistry`. Neither checks that each node's `descriptorType` **consumer key resolves to a registered `IActivityActivationStrategy`** — the thing execution actually dispatches on.

**Required for T072**: add that third check to the import gate — for every node, the descriptor's consumer key must resolve to a registered activation strategy — and reject at import with a diagnostic naming the unresolvable key. Add a T075/T077 case for an artifact whose `descriptorType` carries a CLR type name rather than a consumer key, asserting import-time rejection rather than a first-execution fault.

**Cross-check with FR-B-005**: the checker's two axes are described as never intersecting and jointly sufficient. They are not jointly sufficient. Worth a line to Sipke at review, since it adjusts a clarified decision (2026-08-14 session, Q1).

### Outcome of closing the gap (T072–T078, 2026-08-17) — and a bigger defect found underneath it

**Axis (c) shipped in the importer, not in the shared checker.** `WorkflowArtifactReconciler.TryFindUnresolvableConsumerFault` checks every non-intrinsic node's `DescriptorType` against the installed `IActivityActivationStrategy` set — the registry `ActivityActivator` literally dispatches on — at the node's `DescriptorSchemaVersion`. Extending `RuntimeRequirementChecker` was tried first and rejected on evidence: the checker reads the *advertised* singleton `IRuntimeActivityConsumerCapability` set, its result record is a **pinned FR-B-005 shape** consumed by publishing's preflight views, and a fourth collection there forces `RuntimeRequirementPreflight` either to grow a diagnostic code it does not have or to reintroduce exactly the "`IsReady == false` with nothing explaining why" defect T011 removed (`RuntimeRequirementPreflightTests` asserts `Assert.Single(… "activity.runtime.consumer-missing")`, which any per-node mapping breaks). Only the importer admits artifacts to execution, so only the importer needs the stronger question. Publishing.Api stayed at **474/0 with no edits at all**.

**⚠ The bigger find: axis (b) was inverted and had never fired on a real node.** `RuntimeRequirementChecker` filtered CLR nodes with `node.DescriptorType == typeof(ClrActivityDescriptor).FullName`. Two identifiers one hop apart were conflated: `ActivityContract.DescriptorKind` *is* the descriptor's CLR full name (what `ClrActivityActivator` asserts on *after* selection, `:28`), but `ExecutableNode.DescriptorType` is the **consumer key** `ActivityActivator` *selects* on (`ExecutableNode.cs:108` feeds it straight into `RuntimeActivityDescriptor.ConsumerKey`; `ExecutableNodeCompiler` sets it from `activityVersion.ConsumerKey`). So the filter matched only the *unpinned* form no compiler emits, and **the CLR type-presence axis skipped every real CLR node** — US2 scenario 1, the feature's headline case, would not have been caught. Fixed to `WellKnownRuntimeActivityConsumers.ClrActivity`. Publishing's preflight is unaffected (its test subjects use neither identifier), which is why nothing was red before.

**Axis (c) is partly redundant for executables, and that is deliberate.** `WorkflowExecutable`'s constructor *derives* `RuntimeRequirements` as the union of the declared set and every node's `(Descriptor.ConsumerKey, SchemaVersion)` (`WorkflowExecutable.cs:118-124`), so axis (a) already rejects the CLR-type-name artifact — with a generic "requirement Missing". Axis (c) is kept because it is the invariant stated locally against the registry that actually dispatches, it survives any future change to that derivation, and its diagnostic names the offending **node** rather than a mystery consumer key. It is *not* redundant for `RuntimeRequirementCheckSubject.FromTemplate`, whose requirement set is declared rather than node-derived.

**Two things the spec still does not settle** (flagged, not fixed here):
1. **Intrinsic nodes declare a consumer nothing advertises.** `ExecutableNodeCompiler.cs:257` stamps `descriptorType: "intrinsic"`, so a compiled workflow containing any intrinsic carries `RuntimeRequirement("intrinsic", "1")` — and **no `IRuntimeActivityConsumerCapability` advertises `"intrinsic"`**. Axis (c) skips intrinsics (`IntrinsicKind is not null`, matching axis (b)), but **axis (a) cannot**: it sees only the derived requirement list. A real compiler-produced artifact with an intrinsic would therefore be rejected at import. No test in this repo exercises that path (every fixture and every US1/US2 artifact is intrinsic-free), so it is unproven either way — but it is the first thing a real export→import round trip (T093) will hit. The fix is a decision, not a patch: register an `"intrinsic"` capability, or teach the derivation to exclude engine-intrinsic nodes.
2. **`JsonWorkflowArtifactReconciliation` now requires `IWellKnownTypeRegistry`** (via the checker), i.e. the `Serialization` feature, which it already needed for `IPayloadSerializer` but never declared. Left as a host-composition requirement, matching how `IPayloadSerializer` and `IDistributedLockProvider` are already handled; the registration tests stub it beside the other two.

## Real-transport trigger coverage (added 2026-08-16 — US1 scenario 2 names HTTP/timer explicitly)

T071 pinned US1 scenario 2 with a test-owned `IActivityTriggerStimulusProvider` over a probe node. That was the right call for a test whose subject is import→activation→routing — pulling a transport in would have dragged its host, middleware and clock along. But scenario 2 names **HTTP/timer**, so the real transports need their own coverage, in their own domains.

- [x] T071a **Recurring/timer — do this first.** An imported timer/cron artifact must actually **fire**. This is the projection the 2026-08-14 PR re-review added on the explicit grounds that *"binding-store-only activation would import timer/cron workflows that never fire"* — and it is currently the least-proven part of the feature: the recurring-schedule projection is verified only at unit level (prepared, activated), never observed firing from an imported artifact. Belongs in `tests/Elsa/Workflows/Runtime/Scheduling/Tests` (baseline 83/0) beside the pump and indexer tests. Assert the schedule row is activation-scoped, that `RecurringTriggerPumpTask` picks it up, and that the workflow executes — then that a superseding import re-projects the schedule rather than leaving the old one firing.
- [x] T071b **HTTP — second.** Pin scenario 2 against a real transport using `HttpEndpointHostFixture`, in the HTTP integration suite (`Elsa.Activities.Http.IntegrationTests`, currently 28 passed / 1 failed — the failure is a SQLite-teardown `IOException`, environmental). Lower priority than T071a because it exercises the **binding** projection T071 already proved end to end; its value is confirming a real transport rather than an unproven mechanism. Note the fixture already had to mint an activation-scoped reference once `(ActivationId, SlotId)` began driving source-reference selection — reuse that.

**Outcome (2026-08-17). The answer to the question T071a exists to ask is yes: an imported timer artifact fires, and the workflow it starts runs to completion.** Both tasks are done, with three findings worth carrying into review.

- **T071a** — `tests/Elsa/Workflows/Runtime/Scheduling/Tests/ImportedRecurringTriggerEndToEndTests.cs` (3 tests, suite **83 → 86**). A real `Elsa.Timer` node in a mounted closure → the production JSON source and importer → the activation coordinator → both projections → the real `RecurringTriggerPumpTask` on a `FakeTimeProvider` → the real `IStimulusRouter` → a completed run pinned to the imported artifact and to the reference the importer minted. Nothing sleeps and nothing below the seam is stubbed — which is the gap versus the three existing tests, each of which covers one link (`RecurringTriggerScheduleIndexerTests` = prepared row; `RecurringTriggerPumpTaskTests` = hand-seeded row fires; `RecurringTriggerSampleWorkflowTests` = the two joined, but through a *recording* router that never starts anything).
- **T071b** — `tests/Elsa/Activities/Http/IntegrationTests/ImportedHttpEndpointArtifactEndToEndTests.cs` (3 tests, suite **28p/1f → 31p/1f**; the 1 failure is the pre-existing SQLite-teardown `IOException` in `HttpEndpointRuntimePerformanceTests`, verified unchanged). An imported artifact answers a real inbound POST with 202, the run observes the live request, and the unmatched/out-of-base-path cases still 404 and pass through.

**Findings.**
1. **Supersession deactivates the predecessor's recurring row; it does not delete it.** `ListByActivationAsync` still returns the old row with `IsActive = false`, and only `ListDueAsync` filters it out. That is consistent with `RecurringTriggerSampleWorkflowTests.Reactivation_ReplacesTheServingSchedule_ForSameArtifact` and is presumably deliberate (the row stays recoverable for compensation), but the spec never states it — recorded so it is a decision rather than a rediscovery.
2. **The HTTP fixture had to be given an `IDistributedLockProvider`.** `WorkflowArtifactReconcilerStartupTask` is `[SingleNodeTask]` and takes the lock, but nothing in `src/` registers a default provider — `Elsa.Locking.FileSystem` is the only implementation, and the `Tasks` feature consumes rather than provides one. Any host composing `JsonWorkflowArtifactReconciliation` without a locking feature fails at provider validation, not at run time with a diagnostic. Worth a look before a shell composition hits it.
3. **Two shared-fixture seams were opened rather than duplicated.** `WorkflowExecutionHarness.NewPinnedClrNode` (the existing private contract-pinning made static and public) is now the one way a test produces an *already-pinned* node — directly guarding the `descriptorType`-must-be-the-consumer-key trap; and `ArtifactClosureFixture` is `<Compile Include>`-linked into the scheduling and HTTP suites, following the `Persistence/Groundwork` precedent, so one builder owns content-addressed identity minting everywhere.

## Composition gap — no default `IDistributedLockProvider` (found 2026-08-16 during T071a/T071b)

**Nothing in `src/` registers a default `IDistributedLockProvider`.** `Elsa.Locking.FileSystem` is the only implementation, and the `Tasks` feature *consumes* rather than provides it. The reconciler's `[SingleNodeTask]` startup task requires one, so **any shell composing `JsonWorkflowArtifactReconciliation` without a locking feature fails at DI validation** — not at run time with a diagnostic naming what is missing.

**RESOLVED 2026-08-16 (Joey): change nothing mechanical; the gap was documentation.** `WorkflowsVersionReconcilerStartupTask` was checked and takes `IDistributedLockProvider` as a **required** dependency too, handling only a null *lock handle* (could not acquire → log and return). So the artifact reconciler already matches the established pattern — there was no inconsistency, only an undocumented requirement.

The current behaviour is the correct one and stays:
- **Required, not optional.** Optional-plus-throw would buy a nicer message at the cost of the type lying about its contract, for a failure that already cannot reach production.
- **Fails at container validation** — loud, at boot, un-shippable. The DI error already names both the missing type and the consumer.
- **No in-memory default, ever.** That is the one genuinely dangerous option: it satisfies DI, behaves perfectly on one node, and silently permits two nodes to reconcile the same mount concurrently — precisely what `[SingleNodeTask]` exists to prevent. **Absence of a default is the safety property.**
- **Not a `DependsOn`**, because that would pin one provider choice; `FileSystem` suits a single host, a multi-node deployment needs a genuinely distributed provider.

Fixed by documenting the requirement and its rationale on `JsonWorkflowArtifactReconciliationFeature`, so a composer learns it while reading the feature rather than on first boot.

## Recurring supersession deactivates rather than deletes (recorded 2026-08-16)

When an import supersedes an activation, the predecessor's recurring-schedule row is **deactivated, not deleted**: `ListByActivationAsync` still returns it with `IsActive = false`, and only `ListDueAsync` filters it out. Consistent with the pre-existing tests and presumably deliberate — the row stays recoverable for compensation — but the spec never states it. Asserted as-is by T071a. Worth confirming at review that recoverable-not-removed is the intended contract, since it interacts with [#1358](https://github.com/elsa-workflows/elsa-foundation/issues/1358)'s orphaned-serving-state cleanup.

## ⚠ LANDMINE — intrinsic nodes declare a consumer nothing advertises (found 2026-08-16, T093 will hit it first)

**Unproven either way, and the first real export→import round trip is where it surfaces.**

`ExecutableNodeCompiler.cs:257` stamps intrinsic nodes with `descriptorType: "intrinsic"`, and `WorkflowExecutable`'s constructor **derives** `RuntimeRequirements` from every node's consumer key (`:118-124`). So any compiled workflow containing an intrinsic — `Set`, `Merge`, `Return`, `Control`, `SetCorrelationId`… — carries `RuntimeRequirement("intrinsic", "1")`, and **no `IRuntimeActivityConsumerCapability` advertises `"intrinsic"`**.

Import gate axes (b) and (c) skip intrinsics deliberately (they carry no CLR type and are engine-resolved). **Axis (a) structurally cannot** — it evaluates the artifact's declared requirement set, and the derivation put `"intrinsic"` in it. So an intrinsic-bearing artifact looks like it should be **rejected at import as requiring an uninstalled consumer**.

Why it has not blown up yet: no fixture in the repo is intrinsic-bearing, so every gate test uses CLR-only artifacts. **T093** (SC-B-003, the real export→import round trip with a parent dispatching a child) is the first task that compiles a realistic workflow and pushes it through the gate — it will hit this before US3 finishes.

**RESOLVED 2026-08-16 (Joey): Option A — advertise an `"intrinsic"` capability.** Chosen over excluding intrinsics from the derivation because it costs no model change, no hash movement and no baseline churn (option B would have moved the `workflowExecutable` fixture and golden that T036 had just stabilised), it fixes publishing's preflight in the same stroke, and it keeps the requirement set a **complete** statement of what a portable artifact needs — so a future trimmed runtime lacking an intrinsic is caught at the import gate rather than passing silently. Correction to the pre-decision analysis above: option B would *not* have moved hashes, since the hasher's canonical payload does not include `RuntimeRequirements`; it would still have moved the persisted document baselines.

**Confirmed real, not theoretical.** Before the fix an intrinsic-bearing artifact was rejected as requiring an uninstalled `intrinsic` consumer; that rejection is now reproduced on demand by a negative test that removes the capability.

**The trap in implementing it**: the intrinsic descriptor *payload* carries `schemaVersion = "1.0.0"` (`ExecutableNodeCompiler.cs:249`), but the descriptor schema the capability axis matches on is `"1"` — the compiler passes no `descriptorSchemaVersion`, so `ExecutableNode` defaults it to `RuntimeActivityDescriptor.InitialSchemaVersion`. Advertising `"1.0.0"` would have looked right and silently failed.

The key is deliberately **unprefixed** (`"intrinsic"`, not `elsa.intrinsic`) unlike its two siblings: it is not a new identifier but the literal the compiler already stamps into every intrinsic node, so it is already durable wire content inside content-addressed artifacts. Renaming it would move every compiled artifact's bytes.

**Coverage gap that outlives this fix** — worth a line at review: publishing's deployment preflight would have reported *every* intrinsic-bearing workflow as not-ready, and `Publishing.Api.Tests` has **no coverage that would have caught it** (474/0 held before and after, with zero edits). The missing preflight coverage is a publishing concern, not a spec-151 one.

## Follow-up tasks raised during implementation

Numbered so they are executed and tracked, not rediscovered. IDs are appended (never renumbered) because T001–T115 are referenced by pushed commits.

- [ ] T036a Re-measure and update `GroundworkTargetBaselineTests`' `PendingTargetFingerprint` / `PendingPlanFingerprint` **on a machine where the Groundwork schema CLI runs**. All 25 scenarios currently fail with `Groundwork schema tool emitted invalid JSON (exit 1)` in this workspace, so the assertions are never reached and the values are genuinely unmeasurable here — they come from the CLI's JSON output via `GroundworkBaselineTelemetry`, not from in-process computation. A dated comment in the test names the three target-moving changes (the new `workflowActivationSlot` unit; the `by-publication`→`by-activation` index and field renames on `workflowTriggerBinding` + `recurringTriggerSchedule`; the removed `publishingPublicationSlot` unit), and the assertion messages print observed values — so the first run on a working machine reports exactly what to paste. **The ratified preview.81 `AcceptedTargetFingerprint`/`AcceptedPlanFingerprint` floor must stay untouched.** Blocks nothing in this feature; must not ship unresolved.
- [x] T039a [P] Add a golden fixture for the `workflowActivationSlot` document kind. T026's kind is absent from `GroundworkRuntimeDocumentFixtureFactory.AllKinds`, so the serialization drift test does not cover the one new persisted record this feature introduces — every other runtime kind is covered. Pairs naturally with T039's registration tests.
- [x] T014a Close the publishing-preflight coverage blind spot that let the intrinsic defect survive. `RuntimeRequirementPreflight` would have reported **every intrinsic-bearing workflow as not-ready**, and `Publishing.Api.Tests` held 474/0 identically before and after the fix — no test there exercises a preflight subject containing an intrinsic node, so nothing could have caught it. Add preflight coverage over an intrinsic-bearing executable asserting `IsReady == true` and no `intrinsic` diagnostic, plus the negative counterpart (capability absent → reported unready naming `intrinsic`) so the coverage cannot rot. Reuse the intrinsic fixture shape from `ArtifactClosureFixture.IntrinsicNode`. **This is a publishing-surface gap that spec 151 merely walked past** — it predates the feature and would outlive it uncovered.
- [ ] T093a **A publish-local identifier is content-hash input, so equal behaviour can yield unequal artifact hashes (found by T093, 2026-08-17; logged as a follow-up on Joey's call — it is a publishing-compiler concern, not a reconciliation one).** `DispatchPinSource` writes `DispatchWorkflowPin` into the parent node's metadata during compilation, and that pin carries `WorkflowExecutableSourceProvenance` — the **exporting engine's** `SourceReferenceId`. Node metadata is hash input, so the same child published under a different source-reference id produces a **different parent artifact hash**: two engines compiling byte-identical authored content can disagree on the parent's identity for a reason with no behavioural meaning. This is the converse of [ADR 0038](../../docs/adr/0038-content-addressing-invariant.md)'s invariant failing — not *equal hash, unequal behaviour*, but *equal behaviour, unequal hash* — which weakens deduplication and makes an artifact id non-reproducible across engines.
  - **Second symptom, same cause:** the envelope therefore ships a **dangling pointer**. That `SourceReferenceId` does not resolve on the importing engine. The round trip works only because child admission resolves through `WorkflowExecutableStartAuthorityKind.RetainedDependency` (the dependency edge), never through the pin's source reference. **Nothing currently reads it, and that is the only reason this is latent rather than broken.**
  - **Doc gap:** [contracts/closure-envelope.md](contracts/closure-envelope.md) describes carried source references as *envelope-level* provenance that the importer ignores, and says nothing about provenance embedded **inside node metadata**, where it is both hash input and un-ignorable.
  - **Why it is not fixed here:** the write happens in the publishing compiler's pin source, upstream of everything spec 151 owns. Changing what enters the hash moves every existing artifact id, so it is a deliberate migration, not a tidy-up. **Cheap now, expensive once artifacts exist in the wild.**
  - **Regression tripwire already in place:** `ArtifactExportImportRoundTripTests` asserts the id's presence in the serialized wire bytes, so the day something starts resolving it — or the day it stops being emitted — that test fails first and names this task.
  - **Likely resolutions, for whoever takes it:** exclude publish-local provenance from hash input (keep it as a sidecar outside the hashed node), or keep it hashed and accept that artifact ids are engine-local — which would contradict FR-B-010's portability claim and should then be written down as such.
- [ ] T116 **A missing `PublicationRecord` must be a normal answer, not an exception — implement the recorded FR-B-006 consequence.** The 2026-08-16 publish/activation split named three sites that "throw `InvalidOperationException` on absence today and must not". **All three still throw**, verified 2026-08-17: `PublishWorkflowRequestHandler.cs:119`, `WorkflowPublicationPreflightReader.cs:55`, `PublicationSlotLifecycleRequestHandlers.cs:29`. The consequence was recorded as adopted and never implemented.
  - **This is functional, not cosmetic.** A slot activated by artifact reconciliation legitimately has an `ActiveActivationId` and **no publication record**. Listing slots already handles that correctly (`ResolveVisiblePublicationAsync` returns `PublicationRecord?`), but publishing, preflight and unpublish all crash on it.
  - **Confirmed independently from both directions**: by reading the code, and by US5's T102, which found publishing over an import-owned definition throws `System.InvalidOperationException: Active publication 'import:mounted-artifacts:…' does not exist` out of the preflight reader — so FR-B-006's ownership rules never run at all, on **both** the different-artifact rejection leg and the same-artifact no-op leg.
  - **Fix shape:** check ownership **before** looking for the record, so an import-owned slot answers with the owning `WorkflowActivationSource` instead of "your data is missing". Absence of a record is information, not a fault.
  - Also a **§2.23.5** breach: a raw `InvalidOperationException` crosses the publishing feature boundary.
  - `DualReconciliationOwnershipTests.An_import_owned_definition_is_not_taken_out_by_a_publish_but_the_refusal_is_not_a_preflight_conflict` **pins the current wrong shape on purpose** — fixing this must make that test fail, and it must then be rewritten to assert the correct diagnostic.
- [ ] T117 **Move activation-slot reads to Runtime.Api; Publishing.Api serves publications only (Joey, 2026-08-17).** `ListPublicationSlotsEndpoint` / `GetPublicationSlotEndpoint` inject `IWorkflowActivationAuthority` directly — Publishing.Api reaching into the runtime-owned ledger to serve it. The slot is a runtime concept (§E2.2): **Runtime.Api `GET` returns activation slots; Publishing.Api returns publications.** Anything else makes the publishing surface responsible for knowing runtime-owned data.
  - **No consumer objection exists.** The earlier "renaming the route is a breaking change" caution is **withdrawn** — elsa-foundation has no consumers, which is the same argument that made deleting publishing's slot store safe. Route, rel and view names all move.
  - **Unpublish/restore stay in Publishing.Api** and do **not** migrate to runtime. Retracting a publication is a publishing command; it calls the runtime authority's `TryDeactivateAsync` and the coordinator, which already own deactivation. What must change is only that they stop assuming publishing is the only activation source (T116).
  - Retire the `publication-slot` vocabulary in the transport layer with the move: route, capability rel, request/response/view types.
- [ ] T118 **Publishing outranks reconciliation on the default slot, and reconciliation never takes it back (Joey, 2026-08-17 — approved decision, amends FR-B-006).** Today the coordinator rejects a different-artifact activation from a non-owning source, so on a **combined** engine an imported definition can never be published over: the operator's only escape is deleting the mount. That is wrong. Publishing is an explicit operator command; reconciliation is a boot-time declarative import. **Explicit wins.**
  - **The rule, in full — the second half is what makes the first half safe.** Publishing may take an import-owned **default** slot. Reconciliation must then **never reclaim it.** Without that asymmetry the next shell reload silently reverts the operator's publish, which is a worse failure than the refusal we have now. This is deliberately *not* the usual declarative-controller precedence (Terraform/k8s re-assert over imperative drift) — correct here because reconciliation runs at boot and reload, not continuously, so it is a seed, not an enforcer.
  - **A skipped definition must be loud.** When reconciliation skips because publishing owns the slot, emit a **named boot diagnostic**. A silent skip leaves a mount that looks configured, looks healthy, and serves nothing for that definition — exactly the failure this feature exists to prevent. Not a debug log.
  - **One slot, not two.** Overriding replaces the default slot; it must never leave the imported artifact live on one slot and the published one on another. Two live slots for one definition from two sources is the double-activation US5 forbids.
  - **Non-default slots only on explicit operator choice at publish time** — never automatic, never a fallback. Per-artifact or per-group slot selection in reconciliation configuration is a **future enhancement, explicitly out of scope**.
  - **A runtime-only engine keeps imports effectively immutable**, and that is a property to state rather than an accident: no import endpoint exists, so the only override path is reconfiguring the engine to add publishing and enabling local publishing.
  - **Scope:** the coordinator's ownership rule; a skip-with-diagnostic rule in the reconciler; amendments to **FR-B-006 and US5 scenario 2 in `spec.md`**; and a rewrite of `DualReconciliationOwnershipTests` — it currently asserts publishing over an import-owned definition is *rejected*, which this decision reverses. Sequence **after T116**, whose fix (stop throwing, fall through to the coordinator) is forward-compatible with either outcome.
- [ ] T119 **FR-B-012: render an unresolvable design id as the id plus an explicit flag (Joey, 2026-08-17).** FR-B-012 requires design-provenance ids that do not resolve on a runtime-only engine to render as "opaque/unresolved rather than erroring", but never defines what that looks like. T104 currently implements the weakest reading — echo the id verbatim and let a resolve attempt 404.
  - **Decision: echo **and** flag.** A bare foreign id is indistinguishable from a local one until something clicks it and gets a 404. Inspection surfaces must mark it as not resolvable on this engine, so a caller can render it as provenance rather than as a broken link.
  - Update `T104`'s test to assert the flag, and its remarks, which currently document the echo-only interpretation as the chosen reading.
  - **Cross-repo:** [elsa-foundation-studio#493](https://github.com/elsa-workflows/elsa-foundation-studio/issues/493) must be told, so Studio renders the flag instead of offering a dead link. Confirm the follow-up item covers it.
  - Note the coverage gap found by T104 while deciding this: the nastiest dangling design id in a real closure is the exporting engine's source-reference id inside the parent's **hashed node metadata**, and `WorkflowExecutableNodeView` exposes no `Metadata` member at all — so node metadata is currently outside inspection coverage entirely. **[[T093a]] removes that particular id from the pin**, which shrinks the problem but does not close the surface.
- [ ] T044b Extract `IRecurringTriggerScheduleProjectionPreparer` so the coordinator calls it beside `IWorkflowTriggerIndexer`, retiring the decorator. Closes the residual §2.6.2 concern recorded above: the indexer is a **replacement contract** that now also silently owns the recurring projection's preparation, so replacing it breaks recurring activation **after the slot CAS** — landing in compensation instead of failing fast. Same hazard class T041 closed, one level up; two existing test compositions got it wrong, which is the evidence it is easy to get wrong. Today the invariant rests only on `WorkflowsRuntimeRecurringTriggersFeature` registering store and decorator together. **Decide alongside the parked `T044a`** — both are quality-of-design work that should stand on their own merits rather than be smuggled into a task with another purpose.

**Status of `T044a` (journal repair): PARKED, not deferred.** The 2026-08-16 publish/activation reframing dissolved it — with `PublicationRecord.Status` describing publication rather than mirroring activation, slot/journal drift is impossible by construction. Revisit only if that reframing is reversed.

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
- [x] T027 **Delete** `src/Elsa/Workflows/Publishing/Persistence/Groundwork/Stores/GroundworkPublicationSlotStore.cs` and its entries in `PublishingGroundworkStorageManifestSource.cs` and `DependencyInjection/GroundworkPublishingStoreRegistration.cs`. **One physical ledger per engine** — this removes the dual-ledger composition-transition hole (runtime-only deployment that later enables Publishing). No data migration: elsa-foundation has no consumers.
- [x] T028 **Delete** `IPublicationSlotStore` from `src/Elsa/Workflows/Publishing/Core/Contracts/IPublicationManagement.cs` and `PublicationSlot` / `PublicationSlotIdentity` / `PublicationSlotTransitionResult` from `src/Elsa/Workflows/Publishing/Core/Models/PublicationAuthority.cs`, plus the in-memory slot implementation in `src/Elsa/Workflows/Publishing/Services/InMemoryPublicationStores.cs` and its registration in `WorkflowsPublishingFeature.cs`. Keep `PublicationRecord`, publication policies, and `IPublicationRecordStore` — publishing-internal types keep publication naming in their own domain.
- [x] T029 Refactor `src/Elsa/Workflows/Publishing/Services/PublicationActivator.cs` into a **caller** of `IWorkflowActivationCoordinator`, retaining only compilation-adjacent concerns and `PublicationRecord` bookkeeping wrapped **around** the coordinator call. It MUST NOT implement a parallel copy of the activation sequence.
- [x] T030 Retarget `src/Elsa/Workflows/Publishing/Handlers/PublishWorkflowRequestHandler.cs` and `src/Elsa/Workflows/Publishing/Handlers/PublishReconciledWorkflowVersions.cs` to request activation through the coordinator, preserving the existing **slot-first read** pattern. `PublicationRecord.Status` is publishing's journal of requests to the authority — it MUST NOT be consulted to decide serving, and any Status/slot divergence resolves in favor of the slot.
- [x] T031 Retarget the Publishing.Api slot surfaces to the runtime authority: `Api/Endpoints/PublicationSlots.cs`, `Api/Endpoints/PublicationSlotLifecycle.cs`, `Api/Handlers/PublicationSlotLifecycleRequestHandlers.cs`, `Api/Requests/PublicationSlotLifecycleRequests.cs`, `Api/Models/PublicationManagementViews.cs`, and `Services/WorkflowPublicationPreflightReader.cs` + `Services/PublishedWorkflowDeletionGuard.cs`. **Public HTTP response shapes are unchanged** — this is an internal retarget, not an API contract change.
- [x] T032 **Rename sweep (1/4) — projection-store members** (pure compile-time renames; method names are never persisted): `PreparePublicationAsync` → `PrepareActivationAsync`, `ActivatePublicationAsync` → `ActivateAsync`, `DeleteByPublicationAsync` → `DeleteByActivationAsync`, `ListByPublicationAsync` → `ListByActivationAsync` across `src/Elsa/Workflows/Runtime/Core/Contracts/IWorkflowTriggerBindingStore.cs`, `IRecurringTriggerScheduleStore.cs`, `WorkflowTriggerBindingStoreExtensions.cs`, and every implementation and call site. **No grandfathering** — do not leave obsolete aliases.
- [x] T033 **Rename sweep (2/4) — persisted fields**: `PublicationId` → `ActivationId` on `src/Elsa/Workflows/Runtime/Core/Models/WorkflowTriggerBinding.cs` (incl. the `BuildId` parameter), `WorkflowExecutableSourceReference.cs`, `WorkflowExecutableSourceSelection`, and the projection-state documents. §E6's wire-value protection exists to avoid breaking consumers — there are none, and the schema baselines already move for the ledger relocation.
- [x] T034 **Rename sweep (3/4) — Groundwork storage layer**: `PublicationIdField` and sibling manifest field constants, the by-publication index → by-activation, and `src/Elsa/Persistence/Groundwork/Stores/GroundworkPublicationProjectionStore.cs` → activation naming, propagating to `GroundworkWorkflowTriggerBindingStore.cs` and `GroundworkRecurringTriggerScheduleStore.cs`.
- [x] T035 **Rename sweep (4/4) — retire-reason literals**: `"activation-replaced"` / `"activation-failed"` replace the publication-rooted literals everywhere they are written or asserted. **Kept unchanged and explicitly out of the sweep**: `SlotId` (already neutral, §E6-protected noun) and the source-provenance facts `WorkflowExecutableReferenceScope.Published` and `PublishedAt` — they describe the design-side event that produced the reference, not activation machinery. `Scope` stays a pure provenance axis: **there is no `Activated` scope and none may be added.**
- [x] T036 Update the Groundwork **historical-schema and target baselines deliberately and by name**: `tests/Elsa/Persistence/Groundwork/UnifiedHost/Tests/HistoricalSchemaUpgradeTests.cs` and `tests/Elsa/Persistence/Groundwork/DesignConformance/Sqlite/Tests/GroundworkTargetBaselineTests.cs`. The slot document kind moves families and the projection field constants rename — both are legitimate pre-1.0 changes with no consumers. **Silent baseline churn is the failure mode to avoid**: the commit message must name the moved kind and the renamed constants (research risk R2).
- [x] T037 [P] §2.23.2 branch-covered tests for `WorkflowActivationCoordinator` in `tests/Elsa/Workflows/Runtime/Tests/`, inheriting publishing's activator matrix: failure injected between **each pair** of steps in the sequence, asserting the compensation invariants (replaced activation restored, its projections re-activated with `forceReplay`, candidate projections removed, failed reference retired) — research risk R6 names this as the branch-heaviest path.
- [x] T038 [P] §2.23.2 tests for the conflict rules (T025): same-artifact-any-source no-op; CAS conflict on stale `Revision`; non-owner different-artifact rejection whose diagnostic **names the owning source**; and a negative test asserting **ownership is not inferred from id prefixes** (an `import:`-prefixed activation id owned by publishing still resolves as publishing-owned).
- [x] T039 [P] §2.23.1 registration tests: the authority and coordinator resolve from `AddWorkflowRuntime()` (in-memory) and from runtime Groundwork composition (durable), and **no publishing-family slot registration remains**.
- [x] T040 §2.21.1 golden-rule verification for the whole of 2C: run the existing `tests/Elsa/Workflows/Publishing/Tests` and `tests/Elsa/Workflows/Publishing/Api/Tests` suites and confirm every change needed was **wiring or naming only** — no objective/assertion changes. Any test whose *expected behavior* had to change is a defect in the extraction, not in the test.

### 2D — Close the censused back doors (FR-B-006 single-writer rule; all three are v1 requirements)

- [x] T041 Remove the default-interface fallback `PreparePublicationAsync => IndexAsync` at `src/Elsa/Workflows/Runtime/Core/Contracts/IWorkflowTriggerIndexer.cs:26-31` **and** the artifact-scoped `IndexAsync` write path in `src/Elsa/Workflows/Runtime/Services/WorkflowTriggerIndexer.cs` and `src/Elsa/Workflows/Runtime/Scheduling/RecurringTriggerScheduleIndexer.cs` (delete-by-artifact + per-row save with `PublicationId = null` and bindings born `IsActive = true`). This is the census's **most dangerous** finding: zero live callers, but any indexer implementing only the documented `IndexAsync` signature is silently routed into an activate-bypassing artifact-wide wipe. After removal a partial implementation **fails loudly** instead.
- [x] T042 Update `src/Elsa/Workflows/Runtime/EXTENSION_POINTS.md:263` to advertise the **activation-scoped** signature as the `IWorkflowTriggerIndexer` extension-point contract, removing the now-invalid `IndexAsync` documentation.
- [x] T043 Route the activation-owned schedule-row hard-deletes at `src/Elsa/Workflows/Runtime/Scheduling/RecurringTriggerPumpTask.cs:189` and `:196` (invalid expression, exhausted cron) through the coordinator — or replace them with deactivation + diagnostic. Today they delete outside any activation lifecycle, so restore/compensation later re-prepares an empty set. **`TryAdvanceAsync` (`:202`) is explicitly carved out** as legitimate operational fire-cursor state, not activation authority — do not route it.
- [x] T044 Collapse the schedule-prepare **double-write** in `src/Elsa/Workflows/Publishing/Services/PublicationProjectionReconciler.cs` — currently nested inside the binding intent at `:35-37` and re-prepared under its own intent at `:54`, with a short-circuit hazard at `:199-200` — into **one coordinator-owned write per projection**.
- [x] T045 [P] §2.23.2 tests for T041: an indexer implementing only the legacy signature now fails to compile/register rather than silently bypassing prepare/activate; the artifact-scoped write path is gone.
- [x] T046 [P] §2.23.2 tests for T043 in `tests/Elsa/Workflows/Runtime/Scheduling/Tests/`: invalid-expression and exhausted-cron removals go through the coordinator (or deactivate + diagnose) and survive a subsequent restore/compensation; `TryAdvanceAsync` still advances the fire cursor without touching activation state.
- [x] T047 [P] §2.23.2 test for T044: exactly one schedule write per activation, and the `:199-200` short-circuit no longer skips a needed write.

**2D outcome and decisions (2026-08-17) — read before touching the indexer, the pump, or projection preparation.**

- **T041 removed `IndexAsync` from the contract entirely**, not just the fallback. `IWorkflowTriggerIndexer` now declares exactly one member, `PrepareActivationAsync`, with **no default implementation**, so a partial implementer fails to compile. `WorkflowTriggerIndexer` also loses its `IEnumerable<IWorkflowTriggerIndexObserver>` constructor parameter: observer notification only ever happened inside `IndexAsync`, and prepared rows do not serve, so notifying from the indexer would project routes that are not live. The coordinator is now the sole notifier — which it already was for every production path.
- **T041's test blast radius was nine files**, none of them anticipated by the task text: `WorkflowTriggerIndexerTests`, `RecurringTriggerScheduleIndexerTests`, `RecurringTriggerSampleWorkflowTests`, `RouteTableTriggerIndexObserverTests`, `HttpEndpointHostFixture`, `PublishWorkflowTriggerIndexingTests`, `PublicationActivationTests`, `PublicationProjectionReconcilerTests`, `PublishingGroundworkLifetimeTests`. "Zero live callers" was true of `src/`; tests used the artifact-scoped path as their standard publish shortcut. **Two tests were deleted**, both of which asserted that the *indexer* notifies observers (`Index_NotifiesObservers_AfterSave_WithNewBindings`, `Index_ObserverFailure_PropagatesAndFailsPublish`); the behaviour they covered now lives on the coordinator and is covered by `WorkflowActivationCoordinatorTests`. Every other test was migrated to prepare→activate, not weakened.
- **T043 parks, it does not delete.** `RecurringTriggerPumpTask` advances the fire cursor to `RecurringTriggerPumpTask.NeverOccurs` (`DateTimeOffset.MaxValue`) with a diagnostic instead of `DeleteAsync`. Routing through the coordinator was rejected: the coordinator's only entry point is a whole-activation `ActivateAsync`, and deactivating an entire activation because one cron expression went stale would take the workflow's other triggers down with it. There is no per-row deactivate on the schedule store. Parking uses `TryAdvanceAsync`, which P5 explicitly carves out as operational fire-cursor state, and leaves activation id, slot and `IsActive` untouched — so a restore or compensation still finds the row.
- **T044 collapsed in BOTH places, and the fix is not where the census pointed.** The second write was doing two jobs: re-writing the same set (waste) **and** registering the store's prepared-projection marker in the case where the indexer chain had not (necessary — `IRecurringTriggerScheduleStore.ActivateAsync` throws `"Activation '…' has no prepared recurring-schedule projection."`). So the collapse could not simply delete the second write. Instead **`RecurringTriggerScheduleIndexer` now prepares unconditionally**, including with zero recurring providers composed, making it the single owner of that projection's preparation; the read-back-then-re-prepare is deleted from `WorkflowActivationCoordinator.PrepareProjectionsAsync` **and** from `PublicationProjectionReconciler.PrepareAsync`. The reconciler consequently no longer creates a `RecurringSchedules`/`Prepare` intent at all (5 records per publication, not 6) — which is what removes the `:199-200` short-circuit hazard: one delivery record now governs one write per projection, so the two can no longer diverge.
- **Residual coupling, deliberately not fixed here (candidate follow-up).** `IWorkflowTriggerIndexer` is a §2.6.2 **replacement contract** that now also silently owns the recurring projection's preparation. Replacing it while composing `WorkflowsRuntimeRecurringTriggers` breaks recurring activation — loudly, at the activate step, but after the slot CAS. Two test compositions were doing exactly that and had to be corrected (`PublishWorkflowTriggerIndexingTests`, and the `WorkflowActivationCoordinatorTests` harness). The clean fix is a separate `IRecurringTriggerScheduleProjectionPreparer` contract that the coordinator calls beside the indexer, retiring the decorator; that is a real design change beyond T044's scope and is **not** done. Today the invariant rests on `WorkflowsRuntimeRecurringTriggersFeature` registering the store and the decorator together.

### 2E — Closure envelope model (FR-B-001 / FR-B-010)

- [x] T048 [P] Add `WorkflowArtifactClosure` to `src/Elsa/Workflows/Runtime/Core/Models/` per [data-model.md](data-model.md) and [contracts/closure-envelope.md](contracts/closure-envelope.md): `FormatVersion` (int, starts at 1), `RootArtifactId`, `Artifacts`, `SourceReferences`, `TriggerBindings`. Shared by Publishing (export) and Reconciliation (import), so it rides `Runtime.Core` — the direction both extractions already use.
- [x] T049 Implement envelope serialization through `IPayloadSerializer` with the **same converter discipline the Groundwork runtime document serializer uses** (drop the recomputed projections `Nodes`/`NodesById`, which the ctor rebuilds), so store-round-tripped and exported artifacts are byte-consistent.
- [x] T050 [P] Implement **fail-loud `FormatVersion` parsing** mirroring `ElsaRuntimeDocumentVersions.Parse`: readers accept exactly the versions they know; unknown or newer → loud rejection, no silent upcast, no partial import.
- [x] T051 [P] §2.23.2 tests for the envelope: round-trip fidelity, projection-drop correctness, unknown/newer `FormatVersion` rejection, missing-`RootArtifactId` rejection.

**Checkpoint**: all three extractions complete, one activation coordinator owns the lifecycle, no publishing slot store exists, the three back doors are closed, and the envelope model round-trips. Existing publishing/runtime suites pass with wiring-only changes (T040). **User stories may now proceed.**

---

## Phase 3: User Story 1 — Execute mounted artifacts on a design-free runtime (P1)

**Goal**: a runtime composed with execution features only imports and executes artifacts from a mounted folder, including trigger-started workflows.

**Independent test**: compose an engine with runtime execution + artifact reconciliation and **no** design/activity-design/publishing features; mount one valid dependency-satisfied artifact; start; execute to completion; assert no design/publishing assembly is loaded.

- [x] T052 [P] [US1] Add `IWorkflowArtifactReconciliationSource` (`SourceId`, `SourceKind`, `ReadAsync → IAsyncEnumerable<WorkflowArtifactClosureFile>`) and `WorkflowArtifactClosureFile(Origin, WorkflowArtifactClosure)` to `src/Elsa/Workflows/Runtime/Reconciliation/Core/Contracts/`, mirroring `src/Elsa/Workflows/Design/Reconciliation/Contracts/IWorkflowReconciliationSource.cs`'s self-identification shape.
- [x] T053 [P] [US1] Add `JsonWorkflowArtifactReconciliationOptions` to `Reconciliation/Core/Options/`: exactly one of `FilePath` | ordered `Files: [{Order, FilePath}]` | `FolderPath`; `SourceId` required; **`TenantId` nullable, default null** (stamped on minted references; per-tenant fan-out deferred). Mirror `JsonWorkflowReconciliationOptions` including its non-recursive top-level `*.json` ConfigMap rationale and ordinal filename ordering.
- [x] T054 [P] [US1] Add the §2.23.5 domain-exception taxonomy to `Reconciliation/Core/Exceptions/`: `InvalidWorkflowArtifactClosureException(path, reason, inner)` for **file-level** parse/format/version failures (mirrors `InvalidWorkflowCatalogJsonException`), and the `WorkflowArtifactReconciliationException` family for **pass-aborting** conditions. **No raw `JsonException` / `IOException` may escape.** Per-artifact rejections are diagnostics on the pass result, never exceptions (batch isolation).
- [x] T055 [P] [US1] Add `WorkflowArtifactReconciliationResult` with per-artifact outcomes `Imported | AlreadyCurrent | Skipped(olderVersion) | Rejected(diagnostic)` to `Reconciliation/Core/Models/`.
- [x] T056 [US1] Implement the JSON folder/file source in `src/Elsa/Workflows/Runtime/Reconciliation/Services/JsonWorkflowArtifactReconciliationSource.cs`, mirroring `JsonWorkflowReconciliationSource.cs:78,99-102`: **missing folder → pass-aborting error; empty folder → no-op**.
- [x] T057 [US1] Implement `IWorkflowArtifactReconciler` / `WorkflowArtifactReconciler` in `src/Elsa/Workflows/Runtime/Reconciliation/Services/` — the pipeline skeleton and per-source pass loop. Gates land in T058/T059 and US2/US4; this task establishes ordering and the result accumulation.
- [x] T058 [US1] Implement pipeline **step 1 (parse + format gate)** and **step 2 (closure/dependency validation against the envelope alone)**: every `Dependencies` edge of every member must resolve **within `Artifacts`**, with declared-hash equality, no identity conflicts, no cycles (`MissingArtifact` / `HashMismatch` / `ConflictingIdentity` / `Cycle`). **Validate against the envelope, never the store** — FR-B-010 promises a self-contained closure, so a file must fail identically on every runtime; the store snapshot is consulted only afterward for idempotent skip-persistence.
- [x] T059 [US1] Implement pipeline **step 2a (content-hash recompute)**: before any member persists, recompute its canonical hash via `IWorkflowExecutableHasher` (T017) and compare against `Identity.ArtifactHash` and the id-embedded hash prefix; mismatch → broken-source diagnostic rejecting the member and its dependents. This guards the ADR 0038 content-addressing invariant (equal hash ⇔ equal behavior) against corruption — an unverified payload must never become the stored content for a content-addressed id. It is **not** tamper-proofing; signing stays deferred.
- [x] T060 [US1] Implement **explicit topological ordering** over the validated graph — `WorkflowExecutableDependencyGraph.ResolveClosure` returns results sorted by artifact id/hash (`WorkflowExecutableDependencyGraph.cs:56-60`), **not** dependency-first. Persist all artifacts first (order-free: the store is create-only), then activate dependencies-first so a parent never activates while a child's reference is absent.
- [x] T061 [US1] Implement pipeline **step 5 (activate)** as a **single request to `IWorkflowActivationCoordinator`** carrying artifact, definition/slot, the importer's `WorkflowActivationSource`, minted activation id, and tenant option. **The importer MUST NOT implement any part of the activation sequence** — no lease handling, no projection writes, no observer notification, no compensation. Its recovery unit is the next reconcile pass.
- [x] T062 [US1] Implement source-reference minting per [data-model.md](data-model.md): `SourceKind`/`SourceId` from the source, `Scope = Published`, `ActivationId` from the coordinator, `SlotId` importer-derived (default slot per definition), `TenantId` from the option, identity fields copied from the artifact. **Never mint or rewrite artifact identities** — content-addressed ids are stable by design. Write artifacts **only** through `IWorkflowExecutableStore.SaveAsync`, never as raw documents: the store's private `ExecutableDocument` shape carries legacy lease/guard fields the importer must not touch (research risk R3 — mitigated by this constraint, so do not work around it).
- [x] T063 [US1] Implement trigger-binding **recomputation** via the runtime trigger indexer's prepare path (deterministic `WorkflowTriggerBinding.BuildId`) — the envelope's carried bindings and references are **provenance/expectations only and are never persisted**; the exporting engine's activation ids are meaningless here. A node/stimulus-set mismatch between recomputed and carried surface is a broken-source diagnostic.
- [x] T064 [US1] Add the abstract `WorkflowsArtifactReconciliationFeature` (no `[ShellFeature]` attribute) to `src/Elsa/Workflows/Runtime/Reconciliation/`, mirroring `WorkflowsDesignReconciliationFeature`'s inheritance shape (§2.24.2 pattern #2).
- [x] T065 [US1] Add the concrete `JsonWorkflowArtifactReconciliationFeature` with `[ShellFeature]` id **`JsonWorkflowArtifactReconciliation`**, depending on `Tasks` and `WorkflowsRuntimeTriggers` (the binding/schedule/indexer spine is registered by the triggers feature, **not** by `AddWorkflowRuntime()`) and calling `AddWorkflowRuntime()` itself (idempotent per ADR 0029).
- [x] T066 [US1] Add `WorkflowArtifactReconcilerStartupTask` in `Reconciliation/Startup/` with `[SingleNodeTask]` + distributed lock (`TryAcquireLockAsync(nameof(...))`, null lock → log + return), mirroring `WorkflowsVersionReconcilerStartupTask`. **MUST complete before readiness.**
- [x] T067 [US1] Order the startup task **after** `RegisterActivityTypesStartupTask` via `[TaskDependency(typeof(RegisterActivityTypesStartupTask))]` — the import gate's type-presence axis is meaningless before the assembly scan completes. Verify the attribute accepts a cross-assembly type (research risk R4); documented fallback is `[Order]` above the scan task's order.
- [x] T068 [P] [US1] §2.23.1 registration tests in the new test project: abstract base via a test double, concrete Json feature, and the startup task's single-node/lock/ordering attributes.
- [x] T069 [P] [US1] §2.23.2 tests for the JSON source: folder scan ordering, explicit ordered files, single file, missing folder aborts, empty folder no-ops.
- [x] T070 [US1] **SC-B-001/005 composition assertion test** in `tests/Elsa/Architecture/`: a runtime-only composition executes a mounted artifact end-to-end (including a trigger-started workflow) while asserting **no `Elsa.Workflows.Design.*`, `Elsa.Workflows.Publishing*`, or `Elsa.Activities.Design.*` assembly is loaded**. This is the claim the feature exists to serve — assembly-enforced, not documentation.
- [x] T071 [US1] End-to-end test for US1 acceptance scenarios 1–2: mounted valid artifact reaches the executable store and runs to completion; a trigger-started (HTTP/timer) artifact routes its stimulus and executes — proving **both** projections were activated.

**Checkpoint**: US1 independently testable — a design-free runtime imports and executes mounted artifacts, MVP delivered.

---

## Phase 4: User Story 2 — Reject artifacts the runtime cannot execute (P1)

**Goal**: unsatisfiable artifacts are rejected **at import** with a diagnostic naming what is missing, never faulting at first activation.

**Independent test**: mount an artifact declaring an unsatisfied requirement; reconcile; assert rejection with a clear diagnostic and no activation, while a satisfied artifact in the same batch still activates.

- [x] T072 [US2] Wire the **two-axis import gate** (FR-B-005a) into the pipeline as step 3, calling `IRuntimeRequirementChecker` (T008) per artifact. **Failing either axis rejects the artifact** with a diagnostic naming the missing requirement; it is never activated. Fold the T063 trigger-surface cross-check in here.
- [x] T073 [US2] Implement **closure-unit isolation** (P6): steps 1–4 complete for the **entire closure unit** (root + transitive dependencies) **before any write**; any member failing any gate rejects the whole unit; **a failed unit writes nothing** — no sibling persistence. Isolation across the mounted set is per closure unit, so one bad unit never fails the batch.
- [x] T074 [P] [US2] Implement the reject-with-diagnostic surface: every rejection is a named diagnostic on `WorkflowArtifactReconciliationResult` **and** a log entry — per-artifact rejections are diagnostics, never thrown exceptions (batch isolation).
- [x] T075 [P] [US2] §2.23.2 tests: unregistered activity type rejected at import (US2 scenario 1); unmet storage-driver requirement rejected (scenario 2); incompatible consumer schema rejected; each diagnostic **names** the missing requirement.
- [x] T076 [P] [US2] §2.23.2 tests for the **mixed batch** (US2 scenario 3): satisfiable closure units activate, unsatisfiable ones are rejected individually, and a unit whose *dependency* fails a gate writes **nothing at all** — assert the store is untouched for every member of the failed unit.
- [x] T077 [P] [US2] §2.23.2 tests for the remaining edge cases in spec.md: missing child dependency rejects the parent; malformed/truncated artifact → clear error, no partial import; unknown/newer `FormatVersion` → loud rejection; hash-mismatch → broken-source diagnostic before persistence.
- [x] T078 [US2] **SC-B-002 test**: assert the failure surfaces at reconcile and **never** as a first-activation `UnknownActivityTypeException`; pair it with the T012 classification so the defense-in-depth path (an artifact that somehow activates past the gate) classifies as a non-retryable deployment incident.

**Checkpoint**: US2 independently testable; US1 + US2 together are the full runtime-side import story.

---

## Phase 5: User Story 3 — Export a portable executable artifact with its closure (P1)

**Goal**: a publish-capable engine produces a portable closure unit and delivers it through a pluggable target; the v1 built-in target is an API download.

**Independent test**: publish a workflow that dispatches a child; export; verify the unit contains the child artifact(s); import into a fresh runtime and confirm the parent executes the child.

- [x] T079 [P] [US3] Add `IWorkflowArtifactClosureFactory` to `src/Elsa/Workflows/Publishing/Contracts/` per [contracts/runtime-contracts.md](contracts/runtime-contracts.md) — `CreateAsync(definitionVersionId) → WorkflowArtifactClosure`, destination-agnostic. `…Factory` is the sanctioned §E6 R4 suffix ("constructs"); "Producer" is not codified.
- [x] T080 [US3] Implement the closure factory in `src/Elsa/Workflows/Publishing/Services/`: read the executable + source-reference + trigger-binding stores (all already inside Publishing's envelope) and walk `Dependencies` transitively. **Restricted to `Scope == Published` references (FR-B-011)** — `TestRun`-scope references are expiring, tied to a `WorkflowTestScope`, carry `draft:` version ids, and are non-portable. Throw domain exceptions for missing dependencies (export never emits an incomplete closure) and for non-Published references.
- [x] T081 [P] [US3] Add the export-target seam `IWorkflowArtifactExportTarget` (`TargetId`, `DeliverAsync(closure) → WorkflowArtifactExportDelivery`) and `WorkflowArtifactExportDelivery(TargetId, Kind: InlinePayload | Receipt, Payload?, Location?)` to `src/Elsa/Workflows/Publishing/Core/Contracts/` — Strategy (§2.24.2 #9), fan-in via `TryAddEnumerable`, symmetric to the import source. Future targets **contribute, never replace**. `…Target` is not R4-codified: it is the domain term pinned by FR-B-010a, flagged for reviewer judgment (research D8).
- [x] T082 [US3] Implement the single v1 built-in target `DownloadWorkflowArtifactExportTarget` (`TargetId = "download"`, `Kind = InlinePayload`) in `src/Elsa/Workflows/Publishing/Api/Services/`. Folder-writer and blob-push are **deferred targets on the same producer** — do not implement them.
- [x] T083 [US3] Add the route constant `publishing/workflows/{versionId}/executable-export` to `src/Elsa/Workflows/Publishing/Api/Constants/RouteConstants.cs`, reusing the existing `VersionIdConstraint` (`regex(^(?!drafts$).+$)`).
- [x] T084 [US3] Add a new read-shaped permission to `src/Elsa/Api/FastEndpoints/Constants/PermissionNames.cs` (distinct from `WorkflowPublishingManage`; resolve the final constant name against the file's existing conventions) and apply it via `ConfigurePermissions(...)` per the endpoint convention used at `Api/Endpoints/PublishWorkflow.cs:26`.
- [x] T085 [US3] Implement `GET publishing/workflows/{versionId}/executable-export` in `src/Elsa/Workflows/Publishing/Api/Endpoints/`. **The GET route binds to the `download` target ONLY** — GET is a safe method and receipt-producing targets are external side effects that crawlers, retries, and caches may repeat. **There is no `?target=` selector in v1**; when a side-effecting target ships it arrives with its own POST command endpoint carrying an explicit idempotency contract, defined with that feature. See the endpoint-framework resolution at the top of this file for why this is FastEndpoints.
- [x] T086 [US3] Implement the response per [contracts/export-endpoint.md](contracts/export-endpoint.md): 200 with closure JSON (`application/json`) and `Content-Disposition: attachment; filename="{definitionId}-{artifactVersion}-closure.json"` (safe-name rules; filename shape shared with studio#493); 404 unknown version / no Published reference; 409 test-run-only version; 409 incomplete closure naming the missing artifact id(s). No FastEndpoints byte-download precedent exists — use `Send.StringAsync(json, 200, "application/json")` plus a manual header via a small response helper placed beside `ServerSentEventResponseExtensions` in `src/Elsa/Api/FastEndpoints/`.
- [x] T087 [US3] Add the capability rel to `src/Elsa/Workflows/Publishing/Api/Capabilities/PublishingApiCapabilities.cs` `StaticDeclaration` under capability id **`elsa.api.publishing`**: `{ "rel": "workflow-executable-export", "href": "publishing/workflows/{versionId}/executable-export", "templated": true }`. **These strings are pinned verbatim for studio#493** — do not adjust spelling. Review `contractVersion` per the capability doc rules (additive link → no major bump expected, research risk R5).
- [x] **T088 — VOID (Joey, 2026-08-17). No OpenAPI fragment is produced.** The cited precedent `specs/148-authoring-schema-endpoints/contracts/` **does not exist** — that spec folder holds only `spec.md` and `checklists/`. Only two fragments are consumed by tests (spec 092's and spec 141's, copied into the Architecture test output by `Elsa.Architecture.Tests.csproj`), so a spec-151-local fragment would be read by nothing. Adding the path to the 092 fragment was considered and rejected: it carries **8 publishing paths while the module serves ~13** (missing `incident-strategies`, `workflows/preflight`, `value-conversion/profiles` and the three activity-draft-test-run routes), so it is a frozen snapshot of spec 092's scope, not a living inventory — our path would be the 9th of 13 with no rule explaining the selection. The per-spec fragment idea was started deliberately and never finished; finishing or retiring it is its own piece of work across all endpoints at once, not a sidecar to this feature. Reasoning recorded in [contracts/export-endpoint.md](contracts/export-endpoint.md). `elsa.api.publishing`'s enumerated capability ids are unchanged either way — rel additions are data, not schema.
- [x] T089 [P] [US3] §2.23.2 tests for the closure factory: transitive closure walk (parent → child → grandchild), Published-only enforcement, `TestRun`/draft exclusion (US3 scenario 6), missing-dependency rejection.
- [x] T090 [P] [US3] §2.23.1 + §2.23.2 tests for the export target seam and download target (fan-in registration resolves the built-in target; `InlinePayload` delivery) and for the endpoint handler (all four response cases from T086), plus a `DomainApiCapabilityRegistrationTests` assertion that the new rel is advertised.
- [x] T091 [US3] **Reconcile the FastEndpoints transition registry** for `Elsa.Workflows.Publishing.Api` per ADR 0068. This is **inventory bookkeeping, not a new architectural exception**: the module is already wholly transitional — all 19 of its existing registrations carry `removalOwner: "First-party REST API Consolidation"`, so the 20th route inherits that exit condition and migration wave. **Registry mechanic (verified against the #1346 branch — do not mistake this for a one-row edit)**: `sourceHash` is an **owner fingerprint**, `SHA256` over *every* `.cs` file in the owning project (`FastEndpointsRegistrationScanner.cs:29-31,51`), and all 19 Publishing.Api rows share one value. Spec 151 edits that project in T010, T011, T031, and T082–T087, so **every** Publishing.Api row's `sourceHash` must be restamped, plus a new row added for the export endpoint. Execution depends on merge order: if `tests/Elsa/Architecture/Baselines/fastendpoints-transition-exceptions.json` exists at implementation time ([#1346](https://github.com/elsa-workflows/elsa-foundation/issues/1346) merged), restamp + add there; if not, record the pending row in [contracts/export-endpoint.md](contracts/export-endpoint.md) so it lands with that branch. No hard dependency on merge order either way.
- [x] T091a **DONE 2026-08-15 — do not repeat** ([#1346 comment](https://github.com/elsa-workflows/elsa-foundation/issues/1346#issuecomment-5303337529)). Raised the registry-collision finding with the #1346 owner. Carries no story label: it was cross-branch coordination, not spec-151 implementation work. Recorded here for the audit trail; the content it raised was: Two points: **(1)** the owner-fingerprint design means *any* edit to *any* file in a FastEndpoints-owning project invalidates every registry row for that owner — with 106 registrations across 21 owners, every concurrent branch collides and must restamp; **(2)** a rule question ADR 0068's text does not resolve — does a route added to a module that is *already wholly transitional*, inheriting an existing `removalOwner` and wave, require a fresh approved compatibility exception, or is it bookkeeping under the module's existing entry? This task list assumes the latter. **Open until answered**: if the ADR owner rules otherwise, obtain the architect approval and record the approving reviewer + linked PR on the new row (T091). A related coordination note was also posted to [#1358](https://github.com/elsa-workflows/elsa-foundation/issues/1358#issuecomment-5303338640), telling that issue's fix not to add a fourth writer of serving state.
- [x] T092 [US3] Amend [contracts/export-endpoint.md](contracts/export-endpoint.md) with the endpoint-framework resolution (ADR 0068, capability gap, containment, registry entry, removal follow-up) so the contract doc stops implying FastEndpoints was the unexamined default.
- [x] T093 [US3] **SC-B-003 round-trip test**: export a parent-with-child closure from a publish-capable engine and import it into a fresh runtime that never saw the source definitions; assert parent + child execute with behavior parity versus compile-in-place (US3 scenario 5).


> **T093's home, and why it is not in `Reconciliation.Tests` (recorded 2026-08-17 — do not "fix" this).** The round trip lives in `tests/Elsa/Architecture/` (`Elsa.Architecture.Tests`), beside its sibling `RuntimeOnlyArtifactCompositionTests`: it needs **both** the publishing compiler/export side and the runtime import side in one process, and that project already composes both halves for the same feature. Its supporting fixtures (`PublishCapableEngine`, `RuntimeEngines`) moved with it; the project's one added reference is `Elsa.Workflows.Design.Persistence.Core`, which the compiler needs — permitted here because `ArchitectureGuardTests.Runtime_projects_do_not_add_design_references` scopes that ban to `Elsa.Workflows.Runtime.*` / `Elsa.Activities.Runtime.*` production projects. It **cannot** live in `Elsa.Workflows.Runtime.Reconciliation.Tests`, and that constraint is unchanged and still load-bearing: `RuntimeOnlyLoadedAssemblyTests` there asserts over `AppDomain.CurrentDomain.GetAssemblies()`, and xunit runs a whole assembly in one process — so a Publishing project reference plus any test touching a Publishing type loads `Elsa.Workflows.Publishing` into that process and turns a load-bearing **SC-B-005** assertion red *non-deterministically, depending on test order*. For that same `AppDomain`-in-one-process reason — `Elsa.Architecture.Tests` references Publishing and Design on purpose — the round trip's runtime-only proof is a **transitive reference-closure walk** over the feature assemblies, not an `AppDomain` snapshot. An earlier revision gave the test a standalone `tests/Elsa/Workflows/ArtifactPortability/Tests/` project; that project is deleted.
**Checkpoint**: US3 independently testable; the export→import round trip closes and studio#493 is unblocked.

---

## Phase 6: User Story 4 — Idempotent re-import & version supersession (P2)

**Goal**: re-reconciling is a no-op; a newer version supersedes; activation never moves backward.

**Independent test**: import v1, execute; add v2, reload the shell, execute — v2 active, exactly one active version per definition; reconcile the same set again — no duplication or corruption.

- [x] T094 [US4] Implement pipeline **step 4 (idempotency)**: an artifact already in the store with the same id is a content-addressed no-op (`SaveAsync` is create-only; `ConcurrencyConflict` means already-exists). Same `(DefinitionId, ArtifactVersion)` claimed with **different content** → broken-source diagnostic in the shape of `ActivityVersionHashMismatchException` (the typed throw is safe here because artifacts are content-addressed; the design reconciler's log-only behavior is the weaker precedent).
- [x] T095 [US4] Implement **latest-wins supersession** using the **SemVer sort key** over `ArtifactVersion` (`SemVer.ToSortKey` in `Elsa.Primitives.Versioning` + ordinal compare — the same comparator `WorkflowsVersionReconciler.cs:78-85` and the design version store use, so design and runtime engines order versions identically). The active version is read from the **active activation's minted source reference** (which carries `ArtifactVersion`) — no new state. Candidate sort key ≤ active → skip (equal + same content = the idempotent no-op path; equal + different content = the T094 diagnostic). Activation MUST NOT move backward onto an older artifact.
- [x] T096 [US4] Reject an `ArtifactVersion` that does **not parse as SemVer** with a clear diagnostic — latest-wins requires orderability, so an unorderable version is unimportable.
- [x] T097 [P] [US4] §2.23.2 tests: v1 → v2 supersession activates v2 and deactivates v1 with the predecessor's reference retired as `"activation-replaced"`; a v1 candidate arriving after v2 is skipped (no backward activation); unparseable `ArtifactVersion` rejected.
- [x] T098 [P] [US4] **SC-B-004 test**: N repeated reconciles over an unchanged mounted set yield **exactly one active version per definition**, no duplicate records, no corruption. Assert additionally that every artifact id is **byte-identical across all N passes** — the spec's "artifact id not pinned" edge case requires that the importer never mints a fresh identity per reconcile, and only a cross-pass comparison catches a regression there.
- [x] T099 [US4] Test the **crashed half-import heal**: inject a failure mid-activation, assert the coordinator's compensation restores the replaced activation, then assert the **next reconcile pass** completes the import — the importer's recovery unit is the next pass, and no importer-side journal is introduced (symmetric bookkeeping is an explicit non-goal).
- [x] T100 [US4] Test **re-reconciliation via the existing shell-reload path** (reloading a shell re-runs its startup tasks) — no new trigger coordinator is in scope (#1303 deferred).

**Checkpoint**: US4 independently testable; the promote/rollout loop is operationally sound.

---

## Phase 7: User Story 5 — Design and execution coexist in one engine (P2)

**Goal**: the combined engine is unchanged, and the shared authority resolves dual-path claims by explicit ownership.

**Independent test**: on a combined engine, author → publish → execute in-process still passes; enabling export/import alongside does not regress it.

- [x] T101 [P] [US5] Regression test for US5 scenario 1: a combined engine with the new features enabled authors, publishes, and executes in-process with behavior unchanged from today.
- [x] T102 [US5] End-to-end test for US5 scenario 2 (the feature's sharpest invariant): with **both** design-side workflow version reconciliation and executable artifact reconciliation enabled, the same definition arriving through both paths resolves by explicit ownership — **same artifact → idempotent no-op; different artifact from the non-owning source → loud rejection naming the owning `WorkflowActivationSource`**. Assert the definition is **never double-activated** and that a single stimulus **never starts two instances**. Assert the importer-side rejection is per-closure-unit (batch continues) and the publish-side surfaces on the existing preflight conflict path.
- [x] T103 [P] [US5] Test **FR-B-009 independent composability**: an engine may enable design-side reconciliation, artifact reconciliation, both, or neither — all four compose and start.
- [x] T104 [P] [US5] Test **FR-B-012 provenance rendering**: design-provenance ids that do not resolve on a runtime-only engine render as opaque/unresolved on inspection surfaces rather than erroring. *(The stale pre-sweep doc literals this task previously also carried were corrected in the spec artifacts on 2026-08-15 — see the P10 pin — so nothing doc-shaped remains here.)*

**Checkpoint**: all five user stories independently testable.

---

## Phase 8: Polish & cross-cutting obligations

**Every task here is a constitutional or CI gate, not optional cleanup.**

- [x] T105 [P] Create `src/Elsa/Workflows/Runtime/Reconciliation/EXTENSION_POINTS.md` (§2.22.1) cataloguing the source contract, the reconciler, the feature inheritance point, and the options surface.
- [x] T106 [P] Update `src/Elsa/Workflows/Runtime/EXTENSION_POINTS.md` with the requirements checker, the activation authority + coordinator, and the closure envelope (the `IWorkflowTriggerIndexer` signature correction is already covered by T042).
- [x] T107 [P] Update `src/Elsa/Workflows/Publishing/EXTENSION_POINTS.md` (closure factory; slot contract **removed**) and `src/Elsa/Workflows/Publishing/Api/EXTENSION_POINTS.md` (export-target seam, download target, capability rel).
- [x] T108 Add the new Reconciliation catalog to the **root `EXTENSION_POINTS.md` index** — a catalog that is not linked from the root index does not satisfy §2.22.1 (§2.22.2: the index is pure links, no inline entries).
- [x] T115 [P] Create `src/Elsa/Workflows/Runtime/Reconciliation/README.md` — the **§2.22 per-feature documentation** obligation, which is distinct from the §2.22.1 domain catalog (T105) and not satisfied by it. Minimum required content: the event handlers the feature registers, the contributor interfaces it implements and registers via DI, and **the tasks it registers with their cadence** — here `WorkflowArtifactReconcilerStartupTask` (single-node, distributed-locked, runs at shell activation before readiness, ordered after `RegisterActivityTypesStartupTask`, re-runs on shell reload). Follow the established repo convention (46 existing `README.md` files); the two closest precedents are `src/Elsa/Workflows/Design/Reconciliation/README.md` (the family this mirrors) and `src/Elsa/Workflows/Runtime/ReferenceGarbageCollection/README.md` (the sibling-project shape). *ID is out of sequence deliberately — T001–T114 are referenced by a pushed commit and are not renumbered.*
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
| Follow-ups | 3 (T036a, T039a, T044b) + T044a parked | — |
| **Total** | **119 active** | |

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
