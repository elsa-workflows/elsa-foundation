# Optional BPMN editors — production preservation design

Status: **implemented source checkpoint for #3597; production qualification and review pending**. The source uses option A below, authored schema `2.0.0`, the shared Elsa inspector and both actual authoring hosts. Focused controls and backend qualification are recorded in the [evidence ledger](evidence/prototype-ledger.md). Whole current-source frontend, composed-host, package-consumer and independent UX gates remain separate acceptance requirements. Earlier prototype evidence covers only its recorded source and profile.

Owner correction: [5 November authorization and subsequent scope correction](https://github.com/elsa-workflows/elsa-foundation/issues/3597#issuecomment-6672569996) requires **both bpmn-js and Elsa's integrated editor to edit the full BPMN toolbox in this delivery**, with the shared Elsa inspector. A read-only fallback solely because a notation lacks integrated modeling support is superseded. Unsupported execution remains blocked; complete authored content remains editable and saveable.

## Selected implementation

Use **complete BPMN XML as the sole persisted authority for standard BPMN semantics, diagram interchange and vendor content**, inside a new version of the existing activity-owned authored structure. Keep Elsa activity configuration in a disjoint, identity-linked binding tree in that same authored document. Both canvases use transient complete syntax/graph views and apply normal authoring commands to this authority. XML is never a second editable copy of persisted `elements`, `sequenceFlows` or `diagram` arrays.

The implementation uses option A because bpmn-js already imports/exports complete XML, .NET and browsers have mature XML APIs, and the neutral library consumes XML for its supported execution projection. It introduces one BPMN-owned authoring format without also introducing a second cross-language XML-infoset wire grammar. The nested-scope, identity-transform and two-editor proofs below remain required for acceptance; this document does not assert architecture approval.

| Choice | Authority and advantages | Cost / risk |
|---|---|---|
| **A. Complete canonical XML + disjoint Elsa binding tree — recommended** | One standard document, including unsupported constructs, all DI and extensions. Both editors parse it into transient views. Current V1 arrays are derived only. Namespace declarations and expression text remain together in the standard source. | Integrated commands must patch XML through one BPMN adapter. bpmn-js XML serialization is asynchronous and needs explicit revision/flush discipline. XML string snapshots can increase history size. Namespace/extension preservation still needs real-modeler controls; XML storage alone does not prove it. |
| **B. Complete canonical syntax AST + the same binding tree** | One ordered, schema-independent syntax tree containing all element names, attributes, namespace bindings, text and children; XML is generated transiently. Integrated JSON patches and structural comparisons are straightforward. | Requires a separately versioned complete infoset contract, browser/.NET parsers/serializers and migration rules. A partly typed BPMN JSON model is insufficient. Omitting mixed text, unknown attributes, namespace context or DI recreates the loss problem. Prefer only if the bounded spike demonstrates a concrete advantage worth that surface. |
| Complete XML plus independently editable V1 arrays | Not proposed. | An atomic save does not remove competing authority. Integrated edits can leave XML stale; XML edits can leave bindings/routing stale. A checksum detects disagreement but does not decide which meaning wins. |

A transient DOM/AST and derived V1 compatibility view are implementation data, not extra persisted authoring stores. A later move from A to B is a reviewed format migration, not an opportunistic serializer change.

## Evidence and existing boundaries

- `Essentials/src/Workflows/Design/Core/Models/ActivityNode.cs:139-157`: `ActivityNodeStructure` owns a kind/schema version and raw cloned `JsonElement` payload. Domain core need not acquire BPMN fields.
- `Essentials/src/Workflows/Design/Persistence/EntityFrameworkCore/Commands/EfWorkflowDesignCommands.cs:338-356`: source state and layout already participate in the ordinary locked, atomic draft update. `StateSource` remains the existing workflow/version source; no second source table or publication identity is needed.
- `Essentials/src/Activities/Bpmn/Design/Internal/BpmnStructureHandler.cs:51-72`: `ReplaceChildren` currently reconstructs only `BpmnAuthoredStructure`'s known fields. Merely adding an opaque XML field to today's payload would lose it through a typed reconstruction path.
- The same handler's `CompileExecutableStructure` at lines 76-90 strips authoring material into the smaller executable structure. Admission must happen before this reduction.
- `Essentials/src/Workflows/Design/Core/Contracts/IActivityStructureHandler.cs`: child projection/replacement, scoped variables and executable lowering are activity-owned extension points. The new format should use them, not teach generic workflow core BPMN semantics.
- `Essentials/src/Workflows/Design/Core/Services/ScopedVariableReferenceRemapper.cs:23-46`: clone/remap projects and replaces children. It must be included in the new handler's preservation proof.
- `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/bpmn/bpmnAdapter.ts:146-205`: current synchronization rebuilds the persisted V1 graph from rendered nodes/edges. It must not remain the write path for a complete-document source.
- `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/bpmn/BpmnElementNode.tsx:40-100`: current specialized rendering covers plain start/end/terminate and three gateway types; other types use a task-like frame. That is not full-notation modeling and must be extended, not relabeled as acceptance.
- [Released Bpmn.Model 0.5.0 source](https://github.com/valence-works/bpmn/tree/021d2c5bba494f17d346144d97763cd3b2a13a4b), verified against the installed NuGet repository commit: useful typed execution/interchange projection, **not** a complete-toolbox retention format. Unknown nodes can survive as foreign children while connected flows cascade-drop (`BpmnXmlReader.cs:780-801`); unsupported recognized event/loop syntax can be consumed then degraded (`:1933-1953`). `BpmnExtensionCapture.cs:65-70` does not retain arbitrary unqualified attributes; `BpmnXmlReader.cs:954-1010` reads modeled DI fields only.
- ADR 0063 remains unchanged: the released host-agnostic interpreter owns execution semantics; this feature owns complete authoring and safe lowering to the currently supported profile.

## Proposed authored contract and field ownership

Use the existing structure kind with a **new authored schema version**, provisionally `elsa.bpmn.structure` / `2.0.0`. Do not change the compiled runtime structure's existing `1.0.0` identity or the frozen activity contract merely to store authoring syntax. Final schema naming/version and catalog advertisement require normal architecture/API review.

Conceptually the payload contains:

1. `documentXml`: one complete `<definitions>` document, retaining actual process/element/flow/DI IDs, namespaces, standard syntax and vendor extensions.
2. `processId`: the process selected by the surrounding Elsa composite, referencing the actual XML process. Additional processes/collaborations remain authored content; their execution availability is assessed explicitly.
3. `activities` and identity-linked `bindings`: the complete native forest and its associations to XML elements, with ordinary activity and listener roles. Native records retain exact activity versions, inputs, outputs, placement/intrinsics, contributed configuration and native child structures. Separate compact `scopeHeaders` retain configured standard scope identities, exact native invocation configuration and typed Elsa variables; they contain no second body structure. The selected process uses its existing physical owner's configuration.

These are ownership rules, not approved public field names:

- XML owns all standard routing/defaults/conditions, standard event/loop/body syntax, collaboration, data associations, documentation and complete BPMNDI. Elsa metadata must not duplicate those fields as another semantic graph.
- The binding tree owns native activity configuration, typed Elsa variable contracts, native body/listener identity and native data references. XML contains no copied secret values or complete serialized native activities. Public export handles portable Elsa references separately and reports intentional portability omissions.
- Choose one location for each element-to-binding association. A mapping keyed by XML element ID/role is sufficient; do not additionally persist editable XML binding-reference attributes with the same authority. Imported Elsa reference attributes become reviewed binding proposals/links through the existing trust boundary, not automatic execution permission.
- Existing V1 `elements`, `sequenceFlows`, pools/lanes and diagram are **derived views only for schema 2**. They may be returned transiently for current compiler/inspector compatibility; never save them as another editable copy.
- XML owns geometry for new-format BPMN elements, flows and labels. Do not also persist their coordinates in the generic `draft.layout` table. Project transient display records for the integrated canvas. Keep generic side tables for unrelated native activities and non-BPMN child bodies. Publication derives the immutable execution-inspection layout from the same source XML.
- Theme paint, viewport and current selection remain presentation/preferences; switching themes must not write theme colors into authored DI. Explicit authored colors/styles and all vendor DI metadata must survive.

### Native and standard nested bodies

This is the decisive contract spike, not a detail to gloss over:

- A **standard BPMN subprocess body appears exactly once in canonical XML**. The binding tree stores its existing native BpmnProcess invocation header, scope variables, listener and descendant binding configuration, but does not persist another V1 graph or another XML copy of that same standard body.
- The schema-2 `IActivityStructureHandler.ProjectChildren` derives ordinary native views for that XML scope using the stored stable node IDs and exact versions. Each transient view carries the unchanged canonical owner and a scope address. `ReplaceChildren` folds only the owned native/header configuration back into the physical owner and refuses attempts to rewrite XML through that child-replacement seam. Standard syntax changes use the explicit BPMN document command path; no duplicate graph is persisted.
- IDs must be allocated during an actual create/import/migration command. Traversal, rendering, save and switching must not invent new child/scope identities.
- A BPMN element bound to a **native Sequence/Flowchart/reusable activity** keeps that complete native subtree in its binding. It is not an empty standard BPMN body, nor is it flattened into standard XML. Shared Slots navigation edits the native subtree. Adding standard body syntax while a conflicting native body remains bound must preserve both authored intents and show an execution/binding conflict; never overwrite the native body implicitly.
- A BPMN process inside a native child structure can own its own complete document. The parent standard task only references that native invocation, so no standard body is duplicated.
- Listeners are a separate binding role. Keep exact listener identity/configuration, event-subprocess body identity, variables and control/outcome rules. Do not resynthesize a listener merely because XML was reparsed.

The bounded selected-scope spike proves pure projection/replacement and existing scoped-reference remapping without changing generic workflow contracts. Complete whole-document clone and subtree transforms remain unproved, including identities in other processes and root replacement. They require a BPMN-owned complete inventory/transform or explicit refusal before mutation; a hidden parent-context lookup or duplicated graph is not an acceptable substitute.

## Both canvases, commands and history

Add one BPMN-owned document authoring adapter; do not add a repository-wide command framework.

- Both editors read the complete document and binding tree. The integrated editor gains actual notation and editing for the full toolbox: boundaries and event definitions, all gateways, standard/transaction/event/ad-hoc subprocesses, loops, pools/lanes/message flows, data objects/stores and associations, annotations/groups, scripts and their relevant DI. The owning frontend agent's full-toolbox matrix supplies exact interactions and regressions. Missing notation is implementation work, not a read-only escape hatch.
- Integrated edits become operations on the complete document: create/delete/reparent, reconnect, defaults/conditions, notation replacement, properties and DI. They must not rebuild canonical content from the subset currently visible in a scope. Unknown extension content remains attached to the exact retained owner.
- bpmn-js edits serialize the complete model and merge native binding identity/configuration by actual modeling operations and stable XML IDs. Replacing a shape's type must retain incompatible existing binding intent until explicitly resolved; it must not silently drop native configuration.
- One ordinary Elsa authoring transaction covers syntax, relevant binding changes and side-table cleanup. The existing `editDraft` / Activity Graph authoring histories own undo/redo, dirty state and recovery. Native bpmn-js history must not diverge.
- Each asynchronous serialization/import carries the source revision and scope identity. Reject a stale result atomically. Saving, publishing, editor switching and navigation must await a successful flush or visibly retain the unsaved edit/recovery state. A timeout is not permission to discard a local model.
- Keep source-owned/read-only/permissions checks at the host boundary. Full modeling availability does not grant editing authority.
- Full integrated editing includes native drag/layout, keyboard/focus and accessibility for the new notation. Both canvas directions must demonstrate preservation through actual composed-host save/reload, not only XML parser fixtures.

## Migration, clone and remap

Support old schema 1 and new schema 2 side by side. Reading, choosing another editor, opening a version, autosaving unchanged content or an ordinary no-op Save must not migrate source.

On the **first actual authoring edit**, prepare and apply one reversible authoring transaction that converts the old graph into complete XML and the disjoint binding tree, then applies the edit. Preserve definition/draft/version lineage, native owner and child/listener IDs, supported element/flow IDs, typed variables, native references, custom configuration and layout. Undo can restore the complete previous V1 draft. Historical saved/published versions stay immutable.

Migration must account for fields public XML export intentionally omits; the public exporter is not the migration serializer. Existing V1 data with invalid/global-colliding BPMN IDs needs an explicit lossless compatibility design or user-visible repair path. Do not silently rename IDs or their references. This case is a migration spike/risk, not evidence that an integrated modeling fallback satisfies the new scope.

Whole-definition clones may preserve internal BPMN IDs in their new document while native source identities follow current clone rules. Subtree copies inside one document must allocate/remap standard XML IDs and known standard references, declared Elsa binding links and typed native scoped references. Never perform global string substitution through expressions/vendor text. Preserve unrecognized content; if a vendor-specific internal reference cannot be safely remapped, refuse that particular transform before mutation with a specific explanation. Both source and clone remain intact.

## Publication, Test Drive and #2736

Draft retention and execution admission are separate. Run a **content-derived, module-owned admission pass on the exact complete saved/compiled source before any typed lowering**.

- Walk every native and standard nested scope, including `CanStartWorkflow=false`, reusable graphs and direct publication. Validate all syntax/attributes that can influence execution, actual host capabilities and visible immutable activity bindings.
- Unsupported execution intent stays in the draft and receives element/attribute-specific diagnostics. A removable `unsupported` flag, cached import analysis or successful neutral graph build is not authority.
- Require actual bindings for service/user/script/business-rule and other execution-intent tasks unless the accepted profile explicitly permits a declared pass-through. Keep intentional plain abstract-task behavior. Released `BpmnGraph.cs:686-740` checks supplied references but does not require bindings for all task families; `:606-610` describes pass-through. Do not alter shared interpreter semantics to implement this host policy.
- Inspect conditions, scripts, data/I/O mappings, loop completion/repetition, event definitions, external references and vendor executable extensions before conversion. The library's findings alone do not cover all consumed or unqualified syntax.
- Only a fully admitted source lowers into the existing runtime `elsa.bpmn.structure` 1.0.0 graph, using existing native child bindings and released semantics. No ignored executable fragments may be discarded during that lowering.
- Use the same assessment for normal publication, publication review/preflight and Test Drive. Direct human/service publication remains its narrower declared profile and must assess the complete document before its existing direct projection. A native body may not be reinterpreted as direct work.
- Draft save must preserve unsupported content, even when execution admission fails. Immutable non-runnable version policy is the product question below.

Coordinate the exact scope/claim and shared adapter ownership through **#2736's existing records** before implementation. The full-toolbox authoring decision does not resume ETL/execution-model, StateMachine, Agents, Block or other paused streams and does not authorize a replacement BPMN engine.

### Older/absent backend behavior

Advertise complete-document authoring and admission capabilities through the existing backend/module capability mechanisms. Studio must not send schema-2 writes or offer runnable actions against an absent/incompatible backend. Preserve existing bytes and explain the mismatch; editing authority and backend compatibility are different checks.

Prove server-side refusal for unsupported authoring schemas and failed admission through raw HTTP, including nested `CanStartWorkflow=false` and direct routes. Do not claim that a version bump by itself makes every old binary fail closed: `DefaultActivityStructureService.CompileExecutableStructure` currently preserves unhandled payloads, and the trigger provider has a nested opt-out. The supported backend-version range and negative old-host tests are an explicit compatibility gate. If an old publisher can accept unfamiliar nested source, resolve that prerequisite through the owning publication/source-contract path before promising that pairing; client capability checks are not server admission.

## Import/export

Full-document import retains the entire readable document as authored data. Unsupported execution is reported and blocks runnable actions; it is not an instruction to import a reduced process. Native binding proposals retain current catalog/permission/confirmation safeguards. Importing never executes vendor configuration merely because it can be preserved.

Export standard XML from the canonical document, preserving all supported authored content and DI. Distinguish internal lossless source/binding preservation from portable native-configuration export. Omit only deliberately nonportable/sensitive Elsa configuration under the established policy, surface omissions, and never expose the prototype's private placeholder XML as executable interchange. Do not route full-document export back through a lossy typed library/Fundation reconstruction.

## Owning files and contracts

| Owning path | Proposed work |
|---|---|
| `Essentials/src/Activities/Bpmn/Design/Models/` | New versioned complete-document/binding/scope DTOs; public API and authored-schema documentation where required. Names in this proposal are provisional. |
| `Essentials/src/Activities/Bpmn/Design/Internal/BpmnStructureHandler.cs` | Retain V1 behavior; add a separate schema-2 handler with lossless ProjectChildren/ReplaceChildren/variables and admission-before-lowering. Preserve returned compiled runtime schema 1.0.0. |
| `Essentials/src/Activities/Bpmn/Design/ActivitiesBpmnDesignFeature.cs` | Register the new owning handler and focused BPMN services/capabilities, without a Design→Foundation.Interchange dependency cycle. |
| `Essentials/src/Activities/Bpmn/Interchange/Services/BpmnDocumentImporter.cs`, `BpmnDocumentExporter.cs` and their contracts/models/endpoints | Full-document import/export paths and accurate support/omission results; retain legacy caller contracts as needed. Share the BPMN-owned parsing/admission port at the correct layer. Do not make Design depend on its specializing Foundation Interchange module. |
| `Essentials/src/Activities/Bpmn/Interchange/Services/BpmnAuthoredDirectProjection.cs`, `BpmnAuthoredDirectPublisher.cs` | Complete-source assessment before direct projection, retaining exact immutable pin/source/permission restrictions. |
| `Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Internal/BpmnProcessTriggerStimulusProvider.cs` | Close/avoid the nested-validation gap via the owning admission/compiler path; do not turn trigger registration into the sole validator. |
| `Essentials/src/Workflows/Design/Core/Contracts/IActivityStructureHandler.cs`, `.../Services/DefaultActivityStructureService.cs`, `ScopedVariableReferenceRemapper.cs` | Reuse and test existing contracts first. Any necessary generic change requires evidence/review; no BPMN-specific core fields. |
| `Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services/WorkflowExecutableCompiler.cs`, `ExecutableNodeCompiler.cs` | Confirm all publication/Test Drive callers traverse the admission-owning handler before lowering and derive immutable presentation from the complete source. Change generic code only where a demonstrated owning seam is missing. |
| `Extensions/BpmnJs/studio/Elsa.Studio.BpmnJs/Client/src/bpmnJsBridge.ts`, `BpmnJsCanvas.tsx`; Workflows `bpmn/bpmnJsAuthoring.ts` | Replace restricted projection with complete-document integration and explicit flush/revision contracts; preserve the isolated prototype evidence and optional dependency boundary. |
| `Studio/.../Client/src/bpmn/bpmnAdapter.ts`, `bpmnTypes.ts`, `BpmnElementNode.tsx`, `BpmnElementInspector.tsx`, `bpmnPaletteElements.tsx` | Complete integrated read/edit/notation/DI coverage through shared document commands; V1 view adapters remain compatibility readers. |
| `Studio/.../Client/src/workflow-editor/WorkflowEditor.tsx`, `Studio/.../Client/src/ActivityGraphImplementationEditor.tsx` | Same authoring/version/validation/permission/history contracts on both surfaces; source schema/capability handling and flush before lifecycle actions. |
| `Studio/.../Client/src/api/bpmnInterchange.ts`, `workflowAdapter.ts`, `workflow-editor/duplicateActivities.ts`, `workflowGraphOperationBatch.ts` | Full source/omission APIs, complete scope projection, safe duplicate/remap and derived layout behavior. |
| Existing BPMN Interchange/Runtime/Hosting tests, Studio unit/browser/acceptance tests and spec 1217 | Risk-based preservation/admission/actual-editor/real-runtime controls; update canonical user/module/maintenance docs and exact-commit evidence. |

The parser/admission implementation must respect current package layering. A small BPMN Design-owned XML port/helper or existing neutral `Bpmn.Interchange` package use can be assessed; importing the specializing Foundation Interchange module into Design would create a cycle. Do not create a generic XML framework or broaden the runtime module graph without a demonstrated need.

## Bounded critical path

1. **Retain and assess the initial witness.** Root completes the actual-host supported-subset proof and records failed/unproved cases honestly. It proves integration mechanics, not full-toolbox production feasibility.
2. **Review this authority/schema proposal.** Confirm exact ownership and package layering; resolve the remaining version-policy question. No production freeze yet.
3. **One preservation/handler spike.** Complete XML plus native/nested/listener configuration, no persisted V1 mirrors. Exercise child projection/replacement, scoped-variable remap, whole clone and one subtree copy. Prove all untouched content/configuration survives and old formats remain unchanged on view/no-op Save.
4. **Admission prerequisite with #2736.** Add complete-content diagnostics and fail-closed publication/Test Drive/direct behavior before enabling full unsupported modeling in a production authoring route. Saving drafts stays available.
5. **Complete both editor surfaces.** Deliver the owning integrated-toolbox matrix in bounded notation groups, all writing the same source; then complete bpmn-js native commands, shared inspector/module contributions, derived DI, flush/recovery and packaging. No missing-notation read-only fallback.
6. **Full two-direction qualification.** Representative advanced standard and vendor content survives both editors, save/reload/versioning/clone/export. Supported source publishes and executes real edited paths; unsupported source is retained and refused by actual backend operations. Inspect actual full-notation UX across themes/narrow/keyboard states and collect independent acceptance.
7. **Existing delivery gates.** Targeted and required broader checks, independent review/merge, resulting-main qualification and canonical issue/spec/program/docs updates. No package release or production deployment.

Use a compact high-risk corpus rather than tests mirroring every XML field: complex gateway with connected flows; ad-hoc completion condition; standard/MI loops; conditional/timer/message event data; collaboration/lanes/message flow; data/I/O associations; annotations/groups/associations; script and namespace-sensitive extensions; multiple/child DI planes including genuine authored content; exact native bindings/listeners/nested composites. Run at least one meaningful contributed native activity through the supported execution profile. The empty-child-plane failure in the initial witness demonstrates why actual modeler normalization and native browser commands must supplement parser-only tests.

## Version policy

Keep the existing promotion/version policy while adding complete draft authoring. Advanced documents remain saveable drafts; current promotion, publication and Test Drive checks continue to apply. Creating a separate immutable non-runnable version state is not necessary for editor switching and is not introduced by this delivery. Future execution support can admit more complete documents through the same lifecycle.

**Resolved by owner:** the integrated editor must edit the full toolbox. This proposal does not retain the earlier advanced-notation read-only choice as an open question.

## Bounded spikes in progress — 6 November

These reversible spikes test the proposal; they do not freeze the production contract or establish full-toolbox acceptance.

- Frontend `bpmnDocument.ts` owns schema-2 parsing, complete XML/DI projections and immutable property, containment, connection, label and geometry transactions. The host still owns revision checks/history/persistence. It rejects competing persisted flat graphs and preserves opaque payload/native configuration, including namespace and inherited XML context during moves. Parser/command checks cover mixed vendor content, loops, nested events, collaboration, QNames, multiple data-association sources, metadata and atomic refusal. Binding/identity transformations, migration and composed editor integration remain outstanding.
- The BPMN Design schema-2 handler now proves compact scope headers, exact native/listener retention, pure nested scope projection and scope-only replacement that composes sibling changes. Independent processes use separate transient query roots rather than lexical children of the selected process. Physical-source validation rejects derived views at actual activity positions while allowing ordinary literal JSON. Whole-document clone/upgrade operations remain a prerequisite: selected-process traversal cannot see all configured processes, and stateless root replacement cannot infer a previous identity.
- The integrated source/DI adapter derives actual DI-instance IDs, semantic selection, parent-relative fractional geometry, lane memberships, standard scope containment and data-association display endpoints. Pure geometry and policy checks cover moves/resizes, labels, same-plane routes, duplicate-instance ambiguity and attachment/message semantics. This is not integrated GUI coverage. Authored styles, all explicit route edits and full operation/property coverage remain required.
- A per-resource authoring checkpoint coordinator proves delayed participant preparation, exact host-issued acknowledgement receipts, live cloned snapshot/lineage capture and fenced synchronous transitions. It retains detached pending work and distinguishes an applied transition with failed cleanup from a refused transition. It remains unimported; actual host parent acknowledgement, action coverage, recovery and native controller lifetime are not yet qualified.
- The optional module remains restricted to schema1 protocol1. Integrated notation and complete-source foundations must converge on one authority before full-toolbox acceptance.
- Actual pinned moddle round-trip probes show zero-warning changes to vendor attribute namespaces, mixed text/child ordering and opaque QName namespace context. A warning-free `saveXML` result cannot replace the canonical source. Map complete native command groups into typed source operations, then compare modeled baseline versus modeled result to detect any unaccounted change; retain opaque raw source and its namespace context. Capture the modeled baseline only after actual import/normalization completes. Qualify idless properties, moved/type-replaced owners, known QName projection repairs and DI normalization before enabling schema2 in the alternate editor. No modeled/raw sidecar is persisted.

## Risks and explicit nonclaims

- A complete XML string is only a retention starting point. Two full editors, namespace/vendor preservation, native body decomposition and clone/remap still require concrete proof.
- Existing typed handlers, old-host behavior and direct publication must not reduce unfamiliar source before admission.
- One XML document can contain several processes/collaborations; complete authoring does not claim the current host executes all of them or adopts a different engine.
- Existing V1 invalid/colliding IDs, unknown vendor reference rewriting and source-size/history costs need bounded evidence and clear refusal/recovery for unsafe transforms.
- This proposal neither changes ADR 0063 nor authorizes an upstream semantics release, another program's work, deployment or package publication.
