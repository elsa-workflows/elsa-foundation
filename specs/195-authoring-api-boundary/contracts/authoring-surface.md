# Authoring surface contract

Status: proposed implementation contract for #2459; no route/service/profile is implemented by this document. Product boundary approved by Sipke on 2026-10-06. [Specification](../spec.md), [proof matrix](acceptance-proof-matrix.md).

## Feature ownership

| Feature | Contract |
| --- | --- |
| `WorkflowsRuntimePublicationSupport` (new) | Endpoint-free Runtime-owned support: artifact/template/source-reference stores/readers with owned backend markers, activation authority/switch/coordinator and required root-write lease/options/time, trigger-binding store/extractor/indexer, requirement checker, closure serializer and needed registry/catalog contributors. Never `AddWorkflowRuntime`, RuntimeApi or full Triggers routing. |
| `WorkflowsPublishing` (existing) | Replace `WorkflowsRuntimeTriggers` dependency with support; retain Events, compilation, journal, publication recovery/startup reconciliation and current engine contracts. No HTTP/authorization or test-run execution registration. |
| `WorkflowsPublishingAuthoringApi` (new) | Depends on Publishing plus ApiCapabilities. Shared non-executing transport services and explicit route allowlist below. Reduced `elsa.api.publishing` declaration; no test-run handlers/stores/runners/relations. |
| `WorkflowsPublishingApi` (existing) | Preserve full mapper, test-run operations, feature identity and combined-client behavior. Share retained services without changing its public direct mapper behavior. |
| `WorkflowsRuntimePublicationSupportEntityFrameworkCore` (new) | Depends on support; enroll with `UsesEfModule("Workflows.Runtime")` and `EfPersistenceResourceParticipant`; call unchanged atomic `AddRuntimeEntityFrameworkCore`, register Runtime migrations. Complete R01-R29 schema/private keys remain; no implicit Resumption/redrive. |
| `WorkflowsRuntimeTriggers`, `WorkflowsRuntimeApi`, `WorkflowsRuntimeEntityFrameworkCore` (existing) | Preserve explicit full Runtime composition and its execution/routing/resumption behavior. Reuse owned support helpers without duplicating defaults or weakening provider ownership. |

Full and narrow Publishing surface declarations intentionally differ under the same capability ID. Existing capability conflict validation must refuse co-selection before mapping, naming features without values. Equivalent repeat registration is idempotent. Direct repeated mapping must follow the existing owner contract; support+full helpers must not create duplicate service/route registrations. Workbench Authoring configuration replaces baseline full selections; merely adding the narrow feature to the combined baseline is invalid.

Publication support must preserve the five owned artifact backend descriptors. Do not install anonymous default stores/readers that the existing EF backend cannot safely replace. Verify support before/after/repeated aggregate registration, current selected-provider ownership and foreign provider refusal.

The complete EF aggregate includes durable dispatch *stores* and key-validation startup tasks; these are allowed storage infrastructure, not execution dispatchers or pumps. Preserve existing startup key validation and centrally named resource/provider/schema/connection/override rules. No new EF table/migration chain or smaller family is authorized here.

## Retained Publishing endpoints

Paths are relative to the host's existing API base/shell route; keep existing per-endpoint constraints, JSON contracts, owner conventions and permissions. `Read` and `Manage` mean the current `WorkflowPublishingPermissions` values. Design API operations outside this table remain owned by existing Design features.

| Method | Route | Permission | Capability relation when currently advertised |
| --- | --- | --- | --- |
| GET | `publishing/activities` | Read | Existing direct operation; no fabricated execution relation |
| GET | `publishing/activities/{activityId}/construct` | Read | Existing direct operation; reads authored contract, never constructs/executes a live activity |
| GET | `publishing/incident-strategies` | Read | `incident-strategies` |
| GET | `publishing/value-conversion/profiles` | Read | Existing Expressions conversion-profile contribution |
| POST | `publishing/preflight` | Read | Existing local requirement preflight; not remote deployment readiness |
| POST | `publishing/workflows/preflight` | Read | `publication-snapshot-preflight` |
| POST | `publishing/workflows/{versionId}/preflight` | Read | `publication-preflight` |
| POST | `design/activities/drafts/{draftId}/publication-preflight` | Read | `activity-publication-preflight` |
| POST | `design/activities/drafts/{draftId}/publish` | Manage | `activity-publication` |
| GET | `design/activities/publications/{idempotencyKey}` | Read | `activity-publication-receipt` |
| POST | `publishing/workflows/{versionId}/publish` | Manage | `workflow-publish` |
| GET | `publishing/publications/{publicationId}` | Read | `publication-record` |
| GET | `publishing/publications/{publicationId}/executable-export` (new) | Read | `publication-executable-export`, templated |
| GET | `publishing/workflows/{definitionId}/policy` | Read | `publication-policy` |
| PUT | `publishing/workflows/{definitionId}/policy` | Manage | `publication-policy` |
| DELETE | `publishing/workflows/{definitionId}/slots/{slotName}` | Manage | `publication-slot-unpublish` |
| POST | `publishing/workflows/{definitionId}/slots/{slotName}/restore` | Manage | `publication-slot-restore` |
| GET | `publishing/workflows/{versionId}/executable-export` | Read | `workflow-executable-export` |
| GET | `publishing/workflows/{definitionId}/slots` (new) | Read | `publication-slots`, templated |
| GET | `publishing/workflows/{definitionId}/artifacts` (new) | Read | `published-workflow-artifacts`, templated |

Existing version routes retain their `(?!drafts)` constraint. The new narrow mapper enumerates endpoint types with SDK `MapEndpoint<T>` and shares the plain export helper; it never scans all Publishing assembly endpoints. Current serialization/error/permission metadata applies to the new views. New reads may also be added to the full surface so updated combined clients can prefer them; this is additive, with existing routes/relations unchanged.

## Prohibited surface

No `/runtime` HTTP route, Runtime capability declaration or execution-support background dispatcher/pump may be implicit in the Authoring-only host. Inventory and actual requests cover all Runtime families discoverable on the candidate head, including executable inspection, activation-slot reads, execute/start, stimulus/bookmark resume, test runs, scheduling, alteration/recovery and execution/activity diagnostics. Do not freeze the prohibited set to today's representative URL list: inspect the actual candidate's endpoint metadata and capability relation families.

Every one of the following Publishing endpoints and corresponding relation is absent:

| Method | Route | Relation |
| --- | --- | --- |
| POST | `publishing/workflows/{versionId}/test-runs` | `workflow-test-runs` |
| POST | `publishing/workflows/drafts/test-runs` | `workflow-draft-test-runs` |
| POST | `publishing/activity-drafts/{draftId}/test-runs` | `activity-draft-test-run-dispatch` |
| GET | `publishing/activity-test-runs/{testRunId}` | `activity-draft-test-run-status` |
| GET | `publishing/activity-drafts/{draftId}/test-runs/idempotency/{idempotencyKey}` | `activity-draft-test-run-idempotency-status` |
| POST | `publishing/activity-test-runs/{testRunId}/cancel` | `activity-draft-test-run-cancel` |

Match method/path against actual mapped endpoints and test anonymous *and authenticated* requests. A401/403 proves protection, not absence; an unrelated404 proves nothing about the forbidden route. The prohibited actor must not mutate state or dispatch execution.

## Publication-owned views

### Slots

`GET publishing/workflows/{definitionId}/slots` returns `{ items: [...] }` with all authority slots for that definition, in ordinal slot-name/slot-ID order. Each item includes `slotId`, `definitionId`, `slotName`, `activeActivationId`, `sourceKind`, `sourceId`, `revision`, `updatedAt`, and optional joined `publication`/publication `status` using existing wire status values.

Use `IWorkflowActivationAuthority.ListByDefinitionAsync` and the current publication journal. Join an active publication only for a Publishing-owned source; foreign occupancy retains its source/active identity and has no invented Publishing record. For an inactive slot, preserve the current latest-journal semantics (createdAt descending then publicationId ordinal descending). Do not reinterpret foreign occupancy as empty or make a missing journal/artifact look active. Existing manage operations continue to enforce their ownership/revision rules.

### Artifacts

`GET publishing/workflows/{definitionId}/artifacts` / `published-workflow-artifacts` returns `{ items: [...] }`. The only selector is `includeRetired` (boolean, default `false`); malformed selectors receive the owner's normal400 validation problem. Definition identity and the current persistence access context bound the read. There is no runtime scope/execution selector.

Read closure: authority `ListByDefinitionAsync` → journal `ListBySlotAsync` for those slots → source-reference `FindAsync` by each eligible record's ID → immutable artifact `FindAsync`. Do not scan all artifacts/source references or resolve RuntimeApi's `WorkflowExecutableInspector`/execution state store.

Current publications are those explicitly named by a Publishing-owned active slot with an Active journal record and live Published source reference. `includeRetired=true` additionally includes Retired journal history for these definition-owned slots, including a slot now occupied by another source. Foreign occupancy itself never becomes a Publishing publication. Failed/candidate/test-run records are not artifact-list entries. A current slot naming missing/inconsistent journal, source reference or artifact returns409 using the existing owner problem shape and fixed detail `Publication artifact state is incomplete.`; never an empty successful list or a synthesized target. Queries perform no journal reconciliation/mutation.

Group eligible publications by content-addressed `artifactId`. Each item contains:

- `artifactId`, `definitionId`, immutable `artifactHash`, artifact `createdAt`, root activity type/version, node/resume-target counts (metadata nullable when the retired artifact has been pruned).
- Representative `definitionVersionId`, `artifactVersion`, `sourceReferenceId`, `sourceKind`, `sourceId`, `sourceVersion`, `publishedAt`, `retiredAt`, selected `publicationId`, `slotId`, `slotName`, existing publication wire status and `availability`.
- `publications`: every eligible publication/reference identity in the group, with its own version/source/slot/lifecycle facts and availability. Do not collapse repeated publishes of identical behavior into one publication.

Representative selection is current/live first, then publication timestamp descending (`reference.PublishedAt ?? journal.ActivatedAt ?? journal.CreatedAt`), then ordinal source-reference ID/publication ID. Artifact items sort by that publication timestamp descending and ordinal artifact ID. Nested publication entries use the same stable ordering. Version/source metadata comes from its corresponding reference and journal, never a different version stored on the shared immutable artifact.

Journal-backed fields that survive pruning are exactly `publicationId`, `definitionId`, `definitionVersionId` (journal versionId), `artifactId`, `sourceReferenceId` (nullable as already stored), `slotId`/`slotName`, existing publication status, journal creation/activation/retirement timestamps and failure facts already permitted by the publication view. Reference-only `artifactVersion`, `sourceKind`, `sourceId`, `sourceVersion`, reference creation/publication/retirement timestamps and artifact-only hash/root/count metadata are explicitly nullable when their owner row is absent; never reconstruct them from another version or slot source. A grouped representative keeps these same nullability rules.

Retired history can legitimately outlive a garbage-collected reference/artifact. Preserve its journal-backed identity with `availability: unavailable` and `unavailableReason: source-reference-missing` or `artifact-missing`; unavailable metadata is null and no export/run target is fabricated. Available rows use `availability: available`, with null unavailable reason. Identity/scope mismatches are integrity failures returning the fixed409 problem, including in retired history; they are never relabeled as pruning. Current missing state is also a409 rather than a retired-unavailable row. This distinguishes retired history from a current publication that cannot serve. DTO serialization registers nullable fields/enums through the current Publishing owner context.

The read excludes execution counts, operational diagnostics, test-run references, authored input values and layout payloads. Studio uses a publication-owned adapter shape and does not invent zero Runtime execution counts. It requests current rows for toolbar export and can request retired history for the artifact panel, while using slot identity to identify current occupancy.

### Export identity

The existing `GET publishing/workflows/{versionId}/executable-export` / `workflow-executable-export` remains unchanged. It is **version-based**: the current factory selects a Published reference, live first then newest, and can export a retained retired reference. It does not promise the source-reference/publication identity of an arbitrary historical summary row.

Add `GET publishing/publications/{publicationId}/executable-export` / `publication-executable-export`, Read permission. Find that exact journal record in the current persistence context; require Active or Retired publication and its exact Published source reference matching journal/reference definition-version, artifact, activation and slot identities (immutable artifact definition/version metadata is not an ownership test for content-addressed reuse). Retirement alone is not a refusal when the reference and artifact are retained. Missing journal/reference/artifact returns the existing owner404 problem; mismatched identity/scope, unexportable candidate/failed state, unsupported exact-export producer or incomplete closure returns the existing owner409 problem. No fallback selects a different publication/version/reference.

Add engine-producer `CreateForPublicationAsync(WorkflowPublicationArtifactSelection selection, CancellationToken cancellationToken = default)`. Its non-HTTP selection carries nonempty `definitionId`, `definitionVersionId`, `artifactId`, `sourceReferenceId`, `publicationId`, `slotId` and `slotName`; match them to the journal and Published reference before building the closure. Reuse current transitive closure/reference/binding assembly. Preserve `CreateAsync(versionId, cancellationToken)` unchanged. A custom producer that has not adopted the additive exact-export operation must fail closed through a default interface implementation raising a distinguishable unsupported-selection domain exception mapped to409 by the exact route (no new abstract member breaks existing implementers) rather than silently delegate to version-only selection. The returned closure must root at the requested artifact and contain the requested Published source reference with matching version/definition/activation/slot facts; carry the existing transitive dependency/provenance format without redefining it as exact historical runtime topology. Trigger bindings describe current exporting-engine projections as in the existing export contract. Verify identity before writing any bytes; current failure renderer/delivery target/encoding applies. No template ID or new private configuration is accepted through HTTP.

Studio prefers `publication-executable-export` for a selected publication/history row. Existing version export remains the toolbar/legacy combined-host compatibility path with its version-based semantics; absent precise relation must not be described as exporting an exact historical publication. Unavailable historical rows offer no invented download. If a custom producer refuses exact export, report that operation unavailable/refused rather than silently switching to a different root.

## Studio compatibility

Prefer advertised Publishing `publication-slots`/`published-workflow-artifacts`. For older combined hosts, use only their advertised Runtime slot/executable relations and existing Publishing joins. Missing capabilities yield task-specific unavailable state and no guessed route. No Runtime HTTP is enabled to populate authoring views.

Gate workflow draft Run by `workflow-draft-test-runs`, activity draft Test Run by `activity-draft-test-run-dispatch`, and published executable Run by the current Runtime execution relation. Authoring permits publication controls, review and export; these three execution controls explain absence and send no execution request. Full combined clients retain execution behavior.

## Version/profile boundary

Existing catalogv1/v2/v3 files, digests and accepted pins stay byte-identical. New feature/dependency/profile facts belong to a new version with explicit re-resolution. An existing old accepted selection may no longer match current host dependency facts: preserve normal reconciliation/refusal and require reviewed re-resolution, never silently update its pin.

Authoring remains an evaluation fixture until actual generated configuration is consumed by a fresh authenticated process and the host/client/restart/absence actors pass. Catalog/profile publication is a later verified outcome; the dependency extraction alone is not its proof.
