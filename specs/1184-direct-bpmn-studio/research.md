# Research and decisions

Inspected production main `1d77ed131de3305da8b8236f739c63085bff3e73` and spec-only head `e2caae036c88ed9cb9625b0db88e65f53a8cb22e`. Two read-only research agents covered authoring/publication and runtime/claims; the author checked ETL/navigation and ran the owned-host probe. These are planning inputs, not full PR reviews or implemented acceptance.

## Existing lifecycle and explicit direct publication

**Decision**: Keep workflow draft/save/promote and the BPMN canvas. Add explicit direct publication for a supported root BpmnProcess. Its optional feature belongs in the existing Design-bearing BPMN Interchange package and calls IBpmnModelRuntime through Core. Runtime-only recovery gains no Design dependency.

**Rationale**: The actual old host saved, reopened and promoted an unbound userTask graph. Ordinary PublishWorkflowRequestHandler always compiles native material, so cannot establish direct publication. Promotion retains in-lock validation/state-hash comparison.

**Alternatives considered**: Defaulting old publications to direct changes semantics. A generic model authoring/publication framework or new editor is unnecessary. #3597 retains optional bpmn-js feasibility.

Sources: WorkflowDefinitionState, IUpdateDraftCommand, IPromoteDraftToVersionCommand, EfWorkflowDesignCommands; BpmnGraphAdapter/BpmnStructureHandler/BpmnModelRuntime; existing bpmnAdapter.ts and WorkflowEditor.tsx.

## Versioned authored bindings and XML

**Decision**: Element property `elsa.direct.binding` contains versioned JSON describing explicit human/nativeService intent. Export/import the exact value through Elsa `directBinding` XML extension attribute. Reject malformed/oversized owned metadata; publisher validates complete semantics. Never store behavior in layout or reinterpret childNodeId.

**Rationale**: The actual probe preserved candidate metadata through JSON save/promote but **lost it through XML**. The property bag is not lossless. Owned XML attributes already use the vendor namespace. Meaningful Dropped/Degraded findings block replacing an active direct document; retain the original XML for correction. Native compatibility is separately qualified.

**Alternatives considered**: A separate binding database breaks version/export portability; silently drawing unsupported content admits meaning loss; UI-only metadata cannot define execution.

## Narrow supported profiles

**Decision**: Use existing direct profiles: plain human/parallel, one native service with existing error/fault policy, duration-bound human or service, and separate signal profile when its binding is supported. Refuse unsupported constructs; expose only implemented choices. Human and ETL fixtures are separate processes.

**Rationale**: Formats1–5 are narrower than native BpmnProcess. Variables, loops, listeners, transactions, scripts and arbitrary mixed inventories are rejected by the direct driver. The released library owns semantics.

**Alternatives considered**: Expanding the interpreter for an arbitrary combined fixture would invert the requested priority.

## Trusted provenance

**Decision**: Add a compatible publication context with immutable authored source version, label and layout/presentation, populated server-side. ProcessId is the authored definition identity. Keep current synthetic material identity/hash recipes and raw publication payloads. New-operation replay compares trusted source as well as behavior.

**Rationale**: Raw publications have synthetic source/version1. WorkflowExecutableSourceReference already separates source/version/presentation from material. Runtime renders pinned material without Design.

## ETL uses an ordinary service workflow

**Decision**: Select a closed published workflow, or create a normal RunEtl service workflow through the existing pipeline selector and save/review/publish lifecycle. Bind its complete native pin and live source. No hidden worker compiler/retention path.

**Rationale**: The adapter validates a closed same-tenant Published worker with no required workflow inputs. EtlPublishedSelectionEditor supplies pipeline selection; ordinary publication owns retention. New pipeline versions cannot change the pin.

**Alternatives considered**: Hidden material generation adds source-lifetime/GC machinery. Pasting artifact IDs fails ordinary Studio authoring. BPMN-owned ETL dispatch duplicates runtime ownership.

## Bounded authorized discovery

**Decision**: One raw source/model page per API request, default50/max500. A BPMN transport page permits empty filtered items with continuation. IRuntimeRecoveryContinuationCodec protects purpose, principal, tenant, scope, filter/page size and raw cursor. Retain the original live source clock. Resource-authorize each disclosed item.

**Rationale**: Readers/indexes already page. RuntimeStorePage forbids empty items+continuation; source cursors bind Now.UtcTicks. Do not fill in an unbounded loop.

**Alternatives considered**: New global execution table or browser-local authoritative catalog. Neither is needed for committed waits. Pending starts retain their operation identity; Pending is distinct from Running.

## Exact human claiming and reader fencing

**Decision**: First current resource-authorized same-tenant subject wins, using existing `bpmn:complete-human` permission for claim/completion. No publisher-only rule, teams, timed leases or reassignment. Server captures claimant. Existing mailbox/CAS/atomic checkpoint+receipt stores claims; Authority stays immutable.

Claim-bearing document format15 wraps the legacy document and exact claims. New claim-required human material uses explicit definition format6/profile with only existing human/duration semantics. Older readers must refuse both. New readers retain formats1–5 and frames1–14 unchanged. Old completion serialization/fingerprints remain exact; claimed completion uses a new input kind/payload. No table migration.

**Rationale**: Claiming is absent today. Optional fields on old frames are ignored by older readers and would permit unfenced completion. Existing CAS chooses one owner without a lease service. FR006 distinguishes new claim-required work from legacy completion.

**Admission**: New claim-required publication/start and optional legacy-wait upgrade are opt-in. Admission-off still supports inspection/claim/completion/recovery of already accepted work. Advertise capability support separately from admission.

**Evidence prerequisite**: Actual old-writer wait/completion/replay stores were captured before any claim change; see [receipt](evidence/preclaim/receipt.json). Capture P1's resulting checkpoints before the claim writer too.

## Inspection and ETL navigation

**Decision**: A small BPMN inspection projection exposes graph/pin/source, current work, physical attempts/incidents and settlement. No private-frame parsing in Studio or invented node history. Link exact native execution IDs. An ETL-owned IActivityStateViewProjector exposes authorized RunId/result-address identities without connector values. A narrow contextual Studio run-link contribution supplies durable ETL navigation.

**Rationale**: Existing SDK panels lack runtime resource context. Existing ETL pages and activity-state-view supply most behavior; this seam is justified by the actual cross-module link.

## Qualification and versions

**Decision**: Affected checks and source-bound real host/browser journeys. Preserve passed/failed/skipped/quarantined/cancelled distinctions; avoid unchanged matrices. Actual ApiCompat/magnitude must establish public API compatibility. PR3410's scoped preview exception is not reused. Reconcile newer legitimate main before delivery.

No unresolved research marker remains. These are design choices under program authorization, not claims of passed journeys or version gates.
