# Research: Executable Artifact Reconciliation (spec 151)

**Date**: 2026-08-14 · **Base**: `main` @ `2f7ea5136` · **Inputs**: issue #1304 (rev 4), spec.md Clarifications (7/7 resolved), four codebase research passes (clarify phase) + two plan-phase passes (citation verification, project-layout mapping).

## Citation re-verification (issue appendix vs current base)

**Zero behavioral drift.** `git diff HEAD~16..HEAD` touches none of the 12 cited files; #1314/#1235 landed in neighboring files only. Corrections to carry:

| Citation | Current location |
|---|---|
| Dispatcher wiring `RuntimeCoreServiceCollectionExtensions.cs:371-373` | **:377-396** — a 20-line factory that also enforces exactly one `IWorkflowExecutableStartPolicy` and injects the source-reference store. Not a 3-line `TryAddScoped`. |
| `WorkflowExecutable.cs:116-133` requirements | RuntimeRequirements :118-125, StorageDriverRequirements :126-131 |
| `WorkflowExecutableCompiler.cs:227` | `ValidateDependencyGraphAsync` call at **:229**; template inlining :179-189; `ValidatePinnedActivityContracts` :338-363 |
| All others (dispatch :268/:262-317, `ClrActivityActivator` :31-33, preflight :91-188, test-run :159, third writer :246, publish sequence :88-177, store :13/:69, plans doc :82, projection store :40-114, trigger-binding contract :22-52, activator :13-139, publication contracts/models) | verified exact |

The in-file comment at `WorkflowStartDispatcher.cs:259-261` already states the provenance problem this feature addresses — the seam is unchanged and confirmed.

## Decisions

### D1 — Project placement: two new projects, contracts shared via Runtime.Core

**Decision**: 
- `Elsa.Workflows.Runtime.Reconciliation.Core` (new, contracts-only): `IWorkflowArtifactReconciliationSource`, reconciliation option/result models, domain exceptions. Refs: `Workflows.Runtime.Core`, `Serialization.Core`, `Primitives`.
- `Elsa.Workflows.Runtime.Reconciliation` (new, feature): abstract `WorkflowsArtifactReconciliationFeature` + concrete `JsonWorkflowArtifactReconciliationFeature` (folder/files source), `WorkflowArtifactReconciler`, `WorkflowArtifactReconcilerStartupTask`, the import gate. Refs: Reconciliation.Core, Runtime.Core, Runtime, Tasks.Core, Tasks.Schedules, Locking.Core, Serialization.Core, Persistence.Core.
- Types shared with Publishing go in **`Elsa.Workflows.Runtime.Core`** (envelope model `WorkflowArtifactClosure`; `IRuntimeRequirementChecker`; the relocated `IPublicationSlotStore`/`PublicationSlot`) with default impls in **`Elsa.Workflows.Runtime`** (per ADR 0033: contracts in `.Core`, defaults in impl; registered in `AddWorkflowRuntime()` via `TryAdd*`).

**Rationale**: Sibling-project-per-feature is the established Runtime shape (precedent: `Elsa.Workflows.Runtime.ReferenceGarbageCollection`); the Design reconciliation family (`.Core`/base/`Json`/`Git`) is the shape FR-B-002 mirrors. The JSON source stays **inside** the base project in v1 — §2.20 rule 1 forbids premature per-provider decomposition while only one source kind exists (Design's split is justified by Json+Git being two kinds); a blob/OCI source later triggers the split. The thin `.Core` is exempt under §2.16.1 class "contracts-only `.Core` seams" and lets future sources implement the contract without the base feature's Tasks/Locking envelope. **Mechanical caveat**: adding sibling folders requires extending the `<Compile Remove>` glob in `Elsa.Workflows.Runtime.csproj`, and new assemblies must be added to `Elsa.Workbench/Program.cs` (~:251-258) and regenerated maps.

**Alternatives rejected**: 4-project exact mirror of Design (premature per §2.20); putting the source contract in `Workflows.Runtime.Core` (reconciliation is a feature-family concern, not an engine concern — Design keeps it out of `Design.Core` too).

### D2 — Requirements checker: extract to Runtime, two axes in one verdict

**Decision**: `IRuntimeRequirementChecker` in `Workflows.Runtime.Core`; `RuntimeRequirementChecker` (default) in `Elsa.Workflows.Runtime`, registered in `AddWorkflowRuntime()`. One call → one `RuntimeRequirementCheckResult` covering **both axes**: (a) consumer capabilities + storage drivers (logic relocated verbatim from `RuntimeRequirementPreflight.cs:111-144` — exact ordinal set-membership / key containment, unchanged per clarification); (b) per-node CLR type presence via `IWellKnownTypeRegistry.TryGetTypeOrDefault(ClrActivityDescriptor.TypeAlias)` — the exact predicate `ClrActivityActivator.cs:32` uses. Statuses: `Available | Missing | UnsupportedSchema | MissingActivityType` (extends the existing `RuntimeCapabilityStatus` enum or a runtime-layer sibling; plan keeps the existing enum and adds the fourth member — wire impact: none, it's in-process). Dependency check: `Workflows.Runtime.Core` already references `Activities.Runtime.Core` (consumer capability) and `Serialization.Core` (type registry contract); driver registry is local. No new project references.

**Publishing becomes a thin wrapper**: `RuntimeRequirementPreflight` keeps its retained-set scope selection, views, and `ActivityDiagnostic` formatting, delegating capability evaluation to the shared checker. Two recorded fixes ride along: (1) **diagnostics asymmetry** — `BuildDiagnostics` (:149-188) today emits no diagnostic for a failing `ActivityConsumer` (hardcoded to `DurableValueStorageDriver`); the wrapper gains consumer diagnostics from the shared result, mirroring `ActivityPublicationReviewPolicy`'s `activity.runtime.consumer-missing`/`consumer-schema-unsupported`. (2) **`UnknownActivityTypeException` classification** — it extends plain `Exception`, so `ActivityActivationFailureHandler.Classify` returns null and a missing CLR type is never classified as a deployment incident; re-parent to `ActivityResolutionException` + add an `ActivityActivationFailureKind` member (e.g. `MissingActivityType`) so the defense-in-depth path (an artifact that somehow activates past the gate) classifies as non-retryable `CorrectDeploymentAndResume` like every sibling failure.

**Rationale**: FR-B-005/005a as clarified; the checker's dependencies are all runtime-layer (verified); extraction (not duplication) is the reviewed pattern. **Alternatives rejected**: type-presence as a separate service beside the checker (two calls, two result shapes, and the import gate must remember both — one verdict is the spec's "rejected on either axis").

### D3 — Activation authority: contract moves to Runtime.Core; two Groundwork implementations; guard via id namespace

**Decision** (clarified A2): move `IPublicationSlotStore` + `PublicationSlot` + `PublicationSlotTransitionResult` from `Elsa.Workflows.Publishing.Core` to `Elsa.Workflows.Runtime.Core`, **names unchanged** (R1/R4-compliant; `Slot` is a §E6 protected noun; "publication" is already runtime vocabulary via `PreparePublicationAsync`). Publishing.Core re-exports nothing — consumers recompile against the new namespace (compile-time move; publishing already references Runtime.Core).

- **Implementations**: (a) existing `GroundworkPublicationSlotStore` in `Elsa.Workflows.Publishing.Persistence.Groundwork` retargets to the relocated contract — same table, **no data migration**; (b) new in-memory default in `AddWorkflowRuntime()` (`TryAdd`, replacement-contract semantics); (c) new runtime-family Groundwork implementation registered by a **reconciliation-owned Groundwork persistence unit** (own `IGroundworkStorageManifestSource`, precedent `Elsa3ImportStorageManifest`) so runtime-only engines get a durable slot table only when the feature is composed.
- **Combined-engine precedence**: exactly one durable implementation may be active (§2.6.2 replacement contract). Rule: **publishing's implementation wins** when both persistence units are composed (it owns the legacy data); the reconciliation Groundwork unit registers with `TryAdd`-style deference and the composition documents the precedence. A conflicting double-`Replace` is detected loudly at startup, not silently ordered.
- **Cross-authority guard** (clarified): importer mints `PublicationId` as `import:{sourceId}:{shortId}`; publish mints `publication-{shortId}` (existing). Guard rule in both actors: before superseding, read the slot; if `ActivePublicationId` carries the other actor's namespace → reject the candidate with a diagnostic naming the conflicting authority. Importer-side: per-artifact rejection, batch continues (US2 scenario 3 semantics). Publish-side: the existing preflight conflict path (`PublishWorkflowRequestHandler.cs:103-104`) gains the namespace check.

**Rationale**: single ledger per engine (clarification A2); no data migration; the `EnsureCanActivate` preconditions on the trigger-binding projection store remain the enforcement backstop. **Alternatives rejected** (recorded in spec Clarifications): parallel importer-owned record (two ledgers, silent double activation), scan-derived state (no definition-scoped query exists on the source-reference store; no rollback anchor).

### D4 — Closure envelope: new portable format, carried bindings are expectations

**Decision**: `WorkflowArtifactClosure` (model in `Workflows.Runtime.Core`): `FormatVersion` (int, starts at 1, fail-loud on unknown — mirrors `ElsaRuntimeDocumentVersions.Parse` discipline), `RootArtifactId`, `Artifacts` (root + transitive closure, each a `WorkflowExecutable`), `SourceReferences` (the exporting engine's Published-scope references — **provenance/expectations, not imported rows**), `TriggerBindings` (the exporting engine's active bindings — **expectations, not imported rows**). Serialization via `IPayloadSerializer` with the same converter discipline the Groundwork runtime document serializer uses (drop recomputed projections `Nodes`/`NodesById`).

**Importer treatment of carried collections**: the importer **mints its own** source references (SourceKind/SourceId from the reconciliation source, its own `PublicationId`/`SlotId`, `TenantId` from the source option, Published scope) and **recomputes** trigger bindings from the executable via the existing runtime `WorkflowTriggerIndexer.PreparePublicationAsync` (deterministic `WorkflowTriggerBinding.BuildId`). The carried bindings/references serve as an integrity cross-check: a mismatch between recomputed and carried trigger surface (node/stimulus set) is a broken-source diagnostic. This keeps binding identity and publication stamping engine-local (the exporting engine's publication ids are meaningless on the importer) while staying faithful to FR-B-001/010's envelope contents.

**Rationale**: the executable store is one-document-per-artifact (kind 9, create-only) — a closure is necessarily a new envelope; single JSON file per clarification. **Alternatives rejected**: importing carried binding rows verbatim (foreign publication ids; binding ids embed publicationId → guaranteed re-stamp churn); zip packages (deferred).

### D5 — Import pipeline: per-artifact gates, Model X alignment, wrapped exceptions

**Decision** — reconcile pass per closure file, per artifact (topological order, dependencies first):

1. **Parse + format gate**: unreadable/malformed/unknown `FormatVersion` → `InvalidWorkflowArtifactClosureException` (file-scoped, carries path; §2.23.5 wrapping — no raw `JsonException`/`IOException` escapes). Missing folder → error; empty folder → no-op (mirrors `JsonWorkflowReconciliationSource.cs:78,99-102`).
2. **Closure/dependency validation**: the existing runtime `WorkflowExecutableDependencyGraph.ResolveClosure` semantics over the envelope + store snapshot — `MissingArtifact`/`HashMismatch` (declared)/`ConflictingIdentity`/`Cycle` → reject the parent artifact, batch continues.
3. **Requirements gate** (D2): both axes; any unmet → per-artifact rejection diagnostic; not activated. Trigger-surface cross-check (D4) folds in here.
4. **Idempotency/supersession** (Model X §E2.8 by extension): artifact already in store with same id → content-addressed no-op (`SaveAsync` is create-only; `ConcurrencyConflict` = already-exists). Same `(DefinitionId, ArtifactVersion)` claimed with different content → broken-source diagnostic (mirror `ActivityVersionHashMismatchException` shape — the workflow-design reconciler's log-only behavior is the weaker precedent; artifacts are content-addressed so the typed throw is safe here). Older version than the slot's active → skip (no backward activation, FR-B-007).
5. **Activate**: mint namespaced publication id (D3) → save minted source reference (live) → `PreparePublicationAsync` (recomputed bindings) → slot CAS `TryActivateAsync` (relocated contract) → `ActivatePublicationAsync(new, replaced)` → retire predecessor's minted reference (`"publication-replaced"` reason, mirroring publish).
6. **Batch isolation**: one artifact's failure never fails the batch (US2 scenario 3); every rejection is a named diagnostic on the pass result + log.

Startup: `WorkflowArtifactReconcilerStartupTask`, `[SingleNodeTask]`, distributed lock (`TryAcquireLockAsync(nameof(...))`, null-lock → log + return, mirroring `WorkflowsVersionReconcilerStartupTask`), ordered **after** `RegisterActivityTypesStartupTask` — mechanism: `[TaskDependency(typeof(RegisterActivityTypesStartupTask))]` (topological sorter honors it; requires a project reference to `Elsa.Activities.Runtime`, acceptable: the reconciliation feature is a leaf and any executing runtime composes `ActivitiesRuntime` anyway). Fallback if the attribute shape doesn't fit: `[Order]` above the scan task's order + documented. Re-reconcile: existing shell-reload path re-runs startup tasks (FR-B-008; no new coordinator).

### D6 — Export: `IWorkflowArtifactClosureFactory` + `IWorkflowArtifactExportTarget` + one endpoint

**Decision**:
- **Producer**: `IWorkflowArtifactClosureFactory` in `Elsa.Workflows.Publishing` (engine project — it reads executable + reference + binding stores and walks `Dependencies`; all runtime stores are already in its envelope). Name uses the sanctioned `…Factory` suffix (§E6 R4 "constructs"; "Producer" is not a codified suffix). Restricted to `Scope == Published` references (FR-B-011); rejects `TestRun`/draft references.
- **Target seam**: `IWorkflowArtifactExportTarget` (`TargetId`, `DeliverAsync(closure) → WorkflowArtifactExportDelivery { Kind: InlinePayload | Receipt, Payload?, Location? }`) in `Elsa.Workflows.Publishing.Core` — Strategy pattern (§2.24.2 #9), fan-in registration via `TryAddEnumerable`, symmetric to the import source. "Target" is not an R4-codified suffix: recorded as the domain term pinned by FR-B-010a (R5 concrete noun; reviewer attention flagged). v1 built-in: `DownloadWorkflowArtifactExportTarget` (`"download"`, InlinePayload).
- **Endpoint**: `Elsa.Workflows.Publishing.Api` — `GET publishing/workflows/{versionId}/executable-export` (RouteConstants + existing `VersionIdConstraint`), optional `?target=` (default `download`), resolves the target from the registered set, runs factory → target; InlinePayload → closure JSON bytes with `Content-Disposition` (new small response helper beside `ServerSentEventResponseExtensions` — no FastEndpoints file precedent exists; `Send.StringAsync` + manual header is the near-pattern); Receipt → JSON receipt. New permission in `PermissionNames` (read-shaped: `WorkflowPublishingRead` if it exists, else a dedicated export permission — resolve at task time against the actual constants file). Capability: rel **`workflow-executable-export`** added to `PublishingApiCapabilities.StaticDeclaration` under **`elsa.api.publishing`** (pinned; studio#493 consumes verbatim). Contract-version bump per the capability doc rules if required.

### D7 — Tenancy & trust (v1)

Per clarification: JSON source option `TenantId` (nullable, default null) stamped on minted references; per-tenant fan-out deferred. Integrity = declared-hash closure validation (D5 step 2) within an operator-controlled source; recompute (`WorkflowExecutableHasher` extraction) and signing are named follow-ups — **not** in this plan.

### D8 — Naming table (§E6 R1–R8 check)

| Type | Components | R-check |
|---|---|---|
| `WorkflowArtifactClosure` | 3 | ✓ |
| `IWorkflowArtifactReconciliationSource` | 4 | ✓ R4 `…Source` (pull) |
| `WorkflowsArtifactReconciliationFeature` (abstract, no `[ShellFeature]`) | 4 | ✓ mirrors `WorkflowsDesignReconciliationFeature` |
| `JsonWorkflowArtifactReconciliationFeature` / id `"JsonWorkflowArtifactReconciliation"` | 5 (hard cap) | ✓ mirrors `JsonWorkflowReconciliation` id family |
| `IWorkflowArtifactReconciler` / `WorkflowArtifactReconciler` | 3 | ✓ mirrors `IWorkflowVersionReconciler` |
| `WorkflowArtifactReconcilerStartupTask` | 5 (hard cap) | ✓ mirrors `WorkflowsVersionReconcilerStartupTask` |
| `IRuntimeRequirementChecker` | 3 | ✓ (spec-pinned name) |
| `IWorkflowArtifactClosureFactory` | 4 | ✓ R4 Factory |
| `IWorkflowArtifactExportTarget` | 4 | ⚠ "Target" not R4-codified — pinned domain term (FR-B-010a), flagged for review |
| `IPublicationSlotStore` / `PublicationSlot` (relocated, unrenamed) | 3/2 | ✓ `Slot` protected noun |
| `InvalidWorkflowArtifactClosureException` | 4+Exception | ✓ mirrors `InvalidWorkflowCatalogJsonException` |

### D9 — Test & documentation obligations

- §2.23.1 registration tests for every new/changed feature class (abstract base via a test double, concrete Json feature, publishing features re-asserted). §2.23.2 branch-covered unit tests for: checker (both axes, all statuses), reconciler (all six pipeline gates incl. batch isolation), closure factory (closure walk, Published-only, TestRun exclusion), download target, endpoint handler, slot-store relocation consumers, guard paths (both directions). xunit only — FluentAssertions absent from `Directory.Packages.props` (verified).
- Composition assertions (SC-B-001/005): a runtime-only composition test asserting no Design/Publishing assembly loads (precedent: `tests/Elsa/Workflows/Publishing/Tests` proves engine-standalone by construction; architecture guard tests exist for §E2.2).
- EXTENSION_POINTS.md updates: new catalog for the Reconciliation feature project (+ link from root index), `Workflows/Runtime/EXTENSION_POINTS.md` (checker + slot contract + envelope), `Publishing/EXTENSION_POINTS.md` + `Publishing/Api` (factory, target seam, capability rel), `Api/Capabilities` untouched (contributor mechanism reused, not changed).
- Maps: `dotnet run --project tools/maps/Elsa.Maps.Generator -- all` after project adds; stage changed maps + `manifest.json` explicitly (required CI check "Generated maps fresh").
- Workbench: register new feature assemblies in `Elsa.Workbench/Program.cs`; optional shells.json demo entry.

## Risks

1. **Slot-contract relocation blast radius**: `PublicationActivator`, preflight reader, restore handlers, publishing Groundwork registration all recompile against the new namespace. Mitigation: pure move (no rename, no behavior), golden rule §2.21.1 — existing publishing tests must pass unchanged.
2. **Combined-engine double-registration of the slot store** (D3): must fail loudly, not order-dependently. Task adds an explicit conflict assertion test.
3. **`ExecutableDocument` legacy fields**: the store's private document shape carries legacy lease/guard fields — the importer writes through `IWorkflowExecutableStore.SaveAsync` only (never raw documents), so no exposure.
4. **Startup-task ordering across assemblies** (D5): `[TaskDependency]` signature must accept a cross-assembly type; verify at task time, fallback `[Order]`.
5. **Capability contract-version**: adding a rel to `elsa.api.publishing` may require a `contractVersion` review (additive → no bump expected; confirm against capability doc rules).
