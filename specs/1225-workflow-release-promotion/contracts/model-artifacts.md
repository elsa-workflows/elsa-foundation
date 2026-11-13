# Native, BPMN and ETL release artifacts

**Status: Proposed.** Native workflows, BPMN and ETL are mandatory in the first complete #3590 delivery.
The mechanisms and proofs below are design requirements, not implemented APIs or production acceptance evidence.
Apply [release-bundle](release-bundle.md) and [promotion](promotion.md) without rebuilding or rewriting tested content.
Accepted ADR 0043 governs start authority; its proposed October amendment and proposed ADR 0034 are not ratified here.

## Root roles and owner identities

| Selected root | Immutable release content | Target effect |
|---|---|---|
| Native workflow, including ordinary `BpmnProcess` | Exact authored workflow version and native executable/closure | Stage, then select through its Runtime-owned publication slot. |
| Direct published BPMN model | Exact authored source association, `RuntimeModelMaterial`, protocol/profile and mixed dependencies | Stage, then select through a qualified Runtime-owned model serving bridge. |
| Standalone ETL pipeline | Exact ETL published artifact, owner format and integer version | Make that exact immutable pipeline catalog-available; no implicit workflow or serving slot. |

Dependency-only members remain staged and retained; they do not acquire independent start eligibility merely because their
content contains triggers. A member that is also explicitly selected as a startable root has its own reviewed owner action.
ETL roots must be selectable, previewable, importable and inspectable in their actual catalog surface; an embedded JSON copy
alone does not deliver that root experience. Do not manufacture a workflow wrapper to simulate pipeline activation.

Preserve each owner's version/hash scheme. Workflow/reusable versions and ETL integer versions are typed identities,
not interchangeable labels. Selected source-version identity is separate from a stored artifact's producer metadata;
shared behavioral content must not silently rewrite either association. Source tenant placement and target physical
incarnations are deployment facts, distinct from immutable content identities.

## BPMN: two publication shapes

Ordinary [BpmnProcess](../../../Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Activities/BpmnProcess.cs) is a structural
native activity. Its child work and trigger declarations belong to the native workflow executable and normal publication
slot lifecycle. Preserve its explicit `CanStartWorkflow` behavior; promotion must not turn child processes into new roots.

The visible direct publication action follows a different path:
[BpmnAuthoredDirectPublisher](../../../Essentials/src/Activities/Bpmn/Interchange/Services/BpmnAuthoredDirectPublisher.cs)
projects an exact authored workflow version, verifies permissions/source ownership and resolves exact published workers.
[BpmnModelRuntime](../../../Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Internal/BpmnModelRuntime.cs) creates typed
model material with full service/event-worker pins, hashes it, saves it and creates a live Published source reference.
Current `StartAsync` accepts a live model source reference; this is not an existing revisioned release serving selector.

Direct-root promotion therefore needs a Runtime-owned serving bridge with the same reviewed owner/revision, binding-context,
receipt/fence, trigger-conflict and recovery obligations as other roots. A release record must not supply the missing
authority. Close live-source-reference start bypasses for release-managed roots; accepted-work replay keeps its original
model pin. Until this bridge and actual start/rollback journeys are proved, direct BPMN release activation is incomplete.
Do not call the current publisher during promotion to recreate model material or issue fresh content identities.

Use the trusted authored workflow definition ID as the stable slot subject, separately recorded in publication provenance.
The immutable model identity currently uses `bpmn-model:<content hash>`; deriving the subject from that value would create
one slot per version and defeat replacement/rollback. Preserve that identity and extend the existing Runtime coordinator
to typed native/model selection, using the same slot/CAS with explicitly empty trigger/schedule projections for current
direct profiles. Root start admission must atomically validate the selected activation/context and persist accepted input;
a slot read followed by unguarded enqueue leaves a replacement race. Exact source-reference start requests never silently
redirect to a newer version. The owner selected slot enforcement for direct BPMN: legacy unslotted publications require
explicit owner activation/adoption before fresh starts. Inspection, running models and already accepted input replay retain
their pins. Do not auto-adopt during read/start or infer an owner from hash-derived IDs; raw models need an explicit trusted subject.

## BPMN portability and mixed closure

Export the exact model definition, `RuntimeExecutionProtocol`, profile, full executable identity, input/output contracts,
declared worker/dependency edges and prerequisites using the owning codec. Preserve `RuntimeModelMaterial.ComputeContentHash`
semantics; do not substitute native `WorkflowArtifactClosure` or a generic empty native wrapper for typed model material.
[RuntimeExecutionMaterial](../../../Essentials/src/Workflows/Runtime/Core/Models/RuntimeExecutionMaterial.cs) distinguishes
native/model bodies from provider-owned physical incarnation. Import preserves the body and obtains a target incarnation.

Walk model-to-worker, native-to-model, native dispatch and reusable-template edges transitively, including ETL used inside
worker workflows. Verify full pins, shared edges, profile/protocol support and cycles. Every required member must be in the
bundle; target catalogs cannot silently complete missing release content. Retain model and native dependencies together.

Embedded [RunBpmn](../../../Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Activities/RunBpmn.cs) exposes a portability
constraint: [BpmnEmbeddingPinSource](../../../Essentials/src/Activities/Bpmn/Design/Internal/BpmnEmbeddingPinSource.cs)
stores source-reference ID plus executable pin and physical material incarnation in the tested native body.
[BpmnEmbeddingBoundary](../../../Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Internal/BpmnEmbeddingBoundary.cs)
currently requires that source selection and incarnation exactly. Replacing these fields on import would change tested content.

Introduce deployment-context locator indirection outside the immutable body: map the original located dependency/occurrence
to target-owned reference and physical incarnation, verified against the same exact model body/pin. Resolve that mapping
before target physical-incarnation validation; preserve original fields as immutable lineage. Never reuse an unverified
source physical incarnation as target authority, and never let the mapping select different model content.
Capture the target locator context when accepting a run and propagate it through model/native worker and embedded calls;
continuations keep that captured choice across later activation, rollback, restart and target binding changes.

## ETL artifacts and exact catalog import

ETL is currently a separate pipeline catalog consumed by a native activity, not a `RuntimeModelMaterial` implementation.
Studio's [module](../../../Extensions/Etl/studio/Elsa.Studio.Etl/Client/src/module.tsx) exposes `/etl/definitions`,
`/etl/connectors` and `/etl/runs`; its published-selection editor supplies exact `RunEtl.Pipeline` values.
[EtlArtifact](../../../Extensions/Etl/src/Core/EtlArtifact.cs) retains format, definition ID, positive integer definition
version, artifact ID, content hash and canonical pipeline. [EtlArtifactJson](../../../Extensions/Etl/src/Core/EtlArtifactJson.cs)
uses typed compact serialization and unprefixed lowercase SHA-256; its framed artifact ID includes format/definition/version.
Both retained file format 1 and connector format 2 require owner-specific verification. Do not generic-sort pipeline JSON.

Package standalone roots and dependencies as ETL-owned published members, preserving exact envelope/pipeline bytes and
identities. [EtlPublishedArtifactPinSource](../../../Extensions/Etl/src/Workflows/Design/Services/EtlPublishedArtifactPinSource.cs)
embeds those envelopes in native `etl.pipeline.v1` metadata; [EtlPublishedSelection](../../../Extensions/Etl/src/Core/Runtime/EtlPublishedSelection.cs)
pins definition/integer-version/full hash in a literal. Verify the association at every supported occurrence, including
reusable placements and BPMN native workers. A missing ETL catalog record is not repaired from a newer draft/version.

Existing [IEtlDefinitionStore](../../../Extensions/Etl/src/Design/Elsa.Etl.Design/Stores/IEtlDefinitionStore.cs) publication
is not portable import: [EfEtlDefinitionStore](../../../Extensions/Etl/src/Persistence/EntityFrameworkCore/Stores/EfEtlDefinitionStore.cs)
recompiles and requires the current draft/revision and next sequential version. Add an ETL-owner exact immutable import
bridge that validates retained content without recompilation, creates no editable draft/fake version history, handles
identical retries/conflicting identities and returns durable owner staging receipts. Imported historical roots must remain
discoverable through ETL's read-only catalog; current version ceilings depend on drafts and need an appropriate owner bridge.

## ETL mappings, execution and target prerequisites

Resource requirements include scoped source/lookup/main/error roles, connector kinds and table/queue/endpoint constraints.
Distinguish logical aliases by owning member and occurrence origin; map target resources outside pipeline JSON. Preserve
ETL's existing host revision/handle policies rather than changing them to make a release work. Credentials remain target-owned.

[RunEtl](../../../Extensions/Etl/src/Workflows/Runtime/Activities/RunEtl.cs) captures exact artifact and native continuation;
[EtlCheckpointEnricher](../../../Extensions/Etl/src/Workflows/Runtime/Services/EtlCheckpointEnricher.cs) commits start/cancel
responsibility with normal checkpoints/outbox. Current [admission](../../../Extensions/Etl/src/Runtime/Elsa.Etl.Runtime/Execution/EtlExecution.cs)
resolves current aliases when ETL is admitted, potentially after its native parent started. Add capture/occurrence propagation
through private state, checkpoint, outbox and admission so a delayed child uses its parent's selected mapping choice.
Already admitted ETL runs retain their existing exact artifact, bindings, cursors, leases and accepted effects. Releases do
not contain source data, snapshots, checkpoints, worker claims or result addresses; rollback does not reverse external effects.

Readiness must prove installed `EtlRuntime`/`EtlWorkflowsRuntime`, resumption/checkpoint/outbox, compatible artifact readers,
actual runtime storage/worker scope, host capacity limits, retained binding revisions and relevant connector providers.
SQL/RabbitMQ package availability and permitted table/queue/exchange operations are explicit prerequisites. HTTP authentication
requires compatible Connections/authentication and tenant scope; preserve credential rotation and validate incarnation identity.
Current [runtime registration](../../../Extensions/Etl/src/Persistence/EntityFrameworkCore/DependencyInjection/EtlRuntimeEntityFrameworkCoreRegistration.cs)
refuses non-SQLite engines despite available server schemas. Report this accurately; do not activate broader paused ETL
provider work. Exact import must not install providers or migrate target schemas. ETL runtime remains independent of Design.

## Required engineering proofs

- Native, ordinary BPMN, direct BPMN and standalone ETL root selection/export/import/preview, plus mixed dependency closure.
- Direct BPMN Runtime serving bridge and every managed start path; activation/rollback with real retained model/native work.
- Untouched embedded RunBpmn bytes with different target incarnations, exact locator capture and restart/rollback continuity.
- ETL v1/v2 historical exact import, read-only catalog discovery and typed version/hash conflicts without draft/recompile.
- ETL late admission after parent mapping capture, repeated occurrences, two target bindings, credential rotation/revocation.
- Model/native/template/ETL retention and owner receipt/fence interruption tests in real composed hosts and packaged consumers.
- Extend existing `EtlPublishedWorkflowJourneyTests`, process-recovery suites and actual Studio journeys; controlled connector
  substitutes do not establish real SQL/RabbitMQ/HTTP integration evidence. No listed proof is claimed by this design.
