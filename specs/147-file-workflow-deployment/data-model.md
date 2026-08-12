# Data Model: File-based workflow deployment at startup

**Feature**: `specs/147-file-workflow-deployment` | **Date**: 2026-08-06

No persisted entities change. Everything below is options surface, event contracts, and in-memory flow models. Naming follows §E6 (R1–R8); all new logic-bearing classes are `public sealed` (§2.23.3).

## 1. Options — `Elsa.Workflows.Design.Reconciliation.Json`

### `JsonWorkflowReconciliationOptions` (extended)

| Member | Type | Default | New | Semantics |
|---|---|---|---|---|
| `SourceId` | `string` | `""` | — | Required source identity (unchanged). |
| `FilePath` | `string?` | `null` | — | Single file (unchanged). |
| `Files` | `IEnumerable<JsonWorkflowReconciliationFileOption>` | `[]` | — | Explicit ordered list (unchanged). |
| `FolderPath` | `string?` | `null` | ✚ | Directory scanned for `*.json`, top level only, ordinal file-name order. |
| `PublishOnReconcile` | `bool` | `false` | ✚ | When `true`, the source requests publication of the latest reconciled version of each definition it owns. Default `false` preserves today's import-only behaviour (§4.2 MINOR). |

**Validation (registration time, before `base.ConfigureServices`)**:
- `SourceId` non-empty (unchanged).
- Exactly **one** of `FilePath` / `Files` / `FolderPath` configured. Count-based check replacing the two-way boolean-equality XOR; violation ⇒ `InvalidOperationException` naming all three options and the exactly-one rule (FR-002, SC-006).

**State transitions**: none — options are an immutable singleton snapshot (`Options.Create`, §2.5.1).

## 2. Source contract — `Elsa.Workflows.Design.Reconciliation`

### `IWorkflowReconciliationSource` (extended, additive)

```csharp
bool RequestsPublication => false;   // default interface member — MINOR
```

`JsonWorkflowReconciliationSource` overrides with `Options.PublishOnReconcile`. Git and future sources inherit `false` (no behaviour change).

### `JsonWorkflowReconciliationSource.EffectiveFiles()` (extended)

Resolution order: `Files` (explicit order) → `FilePath` (single) → `FolderPath` (scan). Scan: `Directory.EnumerateFiles(folder, "*.json", SearchOption.TopDirectoryOnly)` → `OrderBy(Path.GetFileName, StringComparer.Ordinal)` → sequential `JsonWorkflowReconciliationFileOption(index, path)`. Missing folder ⇒ `InvalidWorkflowCatalogJsonException(folderPath, "the folder does not exist.")`; empty scan ⇒ info log, empty result. Envelope with null/blank `definitionId` ⇒ warning log naming file + definition name.

## 3. Event contracts — `Elsa.Workflows.Design.Reconciliation.Core`

### `WorkflowVersionSourceClaim` (new, `sealed record`)

The provenance carrier — the only place source identity survives past the contribution phase (it is not persisted).

| Field | Type | Semantics |
|---|---|---|
| `DefinitionId` | `string` | Resolved definition id (post-factory — generated ids are final). |
| `Version` | `string` | Author SemVer of the contributed envelope. |
| `SemVerSortKey` | `string` | `SemVer.ToSortKey(Version)` — ordinal-comparable, the repo-canonical ordering. |
| `SourceId` | `string` | Contributing source's identity. |
| `SourceKind` | `string` | Contributing source's kind (e.g. `"Json"`). |
| `PublishRequested` | `bool` | Snapshot of the source's `RequestsPublication`. |
| `Deleted` | `bool` | Envelope's deletion marker — deleted definitions are never published. |

### `WorkflowVersionsReconciling` (extended, additive)

Gains `public ICollection<WorkflowVersionSourceClaim> Claims { get; } = [];` beside the existing `Versions` collection (fan-in payload shape per §2.6.1 — get-only collection auto-property). `WorkflowVersionsReconcilingHandler` adds one claim per contributed entry in the same loop that adds the version.

### `WorkflowVersionsReconciled` (new, `sealed class : IEvent`)

| Aspect | Value |
|---|---|
| Payload | `IReadOnlyList<WorkflowVersionSourceClaim> Claims` (ctor-carried; notification, not fan-in — no mutable collection needed). |
| Delivery | **Sequential** (`IInlineEventPublisher`) — business-critical subscriber; publish must complete before shell activation finishes (readiness ordering). |
| Publication site | `WorkflowsVersionReconciler.Reconcile`, after every version reconciled without error. Not published on a failed pass. |
| Dispatcher failure policy | A subscriber throw would fail the pass ⇒ subscribers MUST NOT throw (documented in `EXTENSION_POINTS.md`). |
| Known subscribers | `PublishReconciledWorkflowVersions` *(cross-domain — Elsa.Workflows.Publishing)*. |

Catalogued under `### WorkflowVersionsReconciled` in `src/Elsa/Workflows/Design/Reconciliation/EXTENSION_POINTS.md` (CatalogParityTests-enforced).

## 4. Subscriber — `Elsa.Workflows.Publishing` (engine)

### `PublishReconciledWorkflowVersions` (new, `public sealed class : IEventHandler<WorkflowVersionsReconciled>`)

Verb-named per the Publishing feature's own handler style (`CollectExecutableCompilation`). Registered in `WorkflowsPublishingFeature.ConfigureServices` via `services.AddEventHandler<WorkflowVersionsReconciled, PublishReconciledWorkflowVersions>()` (scoped). New project reference: `Elsa.Workflows.Design.Reconciliation.Core` (allowed direction; see research D1/finding 8).

**Dependencies** (all existing contracts): `IWorkflowDefinitionVersionStore`, `IWorkflowDefinitionStore` (Design read ports), `IPublicationSlotStore`, `IPublicationRecordStore` (Publishing authority stores), `IRequestSender` (mediator), `ILogger<>`.

**Algorithm** (per `Handle`):
1. Group `Claims` by `DefinitionId`; per group take the claim with the highest `SemVerSortKey` (ordinal).
2. Skip when `!PublishRequested` (debug log) or `Deleted` / definition `DeletedAt != null` (info log).
3. Resolve target row: `ListByDefinitionAsync(DefinitionId)` filtered on `SemVerSortKey`; absent ⇒ warning (reconcile/publish disagreement), continue.
4. Idempotency pre-check (FR-007): resolve the slot a slot-less `PublishWorkflow` would update (workflow policy, else host policy, else the synthesized `default` host policy) and skip when *that* slot's active `PublicationRecord` has `WorkflowDefinitionVersionId == target.Id` (debug log). Checking every slot would let a side-by-side `canary` publication of the same version suppress a publish the target slot still needs. An unresolvable policy (`RequireExplicitSlot`) yields no target slot ⇒ no skip, and `PublishWorkflow` raises the authoritative error. (`PublishWorkflow`'s `WasCreated=false` short-circuit is the second net.)
5. `await requestSender.Send(new PublishWorkflow(target.Id), ct)`; log info with `ArtifactId`/`WasCreated`.
6. **Per-definition try/catch** (FR-009): failures logged as structured errors (`DefinitionId`, `Version`, `SourceId`, exception code where typed); loop continues; `Handle` never throws.

## 5. Flow (end to end)

```text
shells.json Options ──▶ JsonWorkflowReconciliationFeature (validate: SourceId + exactly-one-of-three)
                          │ registers source (RequestsPublication = PublishOnReconcile)
shell activation ──▶ WorkflowsVersionReconcilerStartupTask [SingleNodeTask][Order(2)] + lock
                          │ WorkflowsVersionReconciler.Reconcile
                          │   publish WorkflowVersionsReconciling (Sequential)
                          │     WorkflowVersionsReconcilingHandler: per source → Versions + Claims
                          │   per version: materialize (Model X: exists-check, mismatch tripwire, duplicate policy)
                          │   pass succeeded ⇒ publish WorkflowVersionsReconciled(Claims) (Sequential)
                          │     PublishReconciledWorkflowVersions: latest claim per definition
                          │       slot pre-check ─▶ skip | PublishWorkflow(versionId) ─▶ PublicationRecord Active
                          ▼
shell Active ──▶ DefaultShellWarmup.MarkReady ──▶ GET /health/ready = 200
```

## 6. Invariants

- Import idempotency unchanged: `(DefinitionId, SemVerSortKey)` existence check (Model X, §E2.9.5).
- Publish idempotency: slot pre-check + `PublishWorkflow` short-circuit ⇒ restart with unchanged files creates zero new versions and zero new publications (SC-002).
- Single-node: publish inherits the reconcile task's `[SingleNodeTask]` + distributed lock (FR-008) — no second lock.
- Seam: Design never references Publishing; Publishing references only Design contracts/read ports + `Reconciliation.Core` (event contract). No Runtime edge changes.
- Deleted definitions are never published or resurrected.
