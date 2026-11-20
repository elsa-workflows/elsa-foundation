# Feature Specification: Weaver workflow authoring and inspection journeys

**Feature Branch**: `agent/claude/3442-weaver-workflow-journeys`

**Created**: 2026-11-02

**Status**: Implemented

**Input**: [Feature #3442](https://github.com/elsa-workflows/elsa-foundation/issues/3442), M3 ([#3432](https://github.com/elsa-workflows/elsa-foundation/issues/3432)) of [Program #3428](https://github.com/elsa-workflows/elsa-foundation/issues/3428). The owner authorizes autonomous delivery of the six acceptance criteria, the 30 October reusable-workflow-template and workflow-library refinements, and the two "Workflow authorization read-path follow-through" items recorded on the issue. Prerequisite #3440 is verified Done ([closeout](https://github.com/elsa-workflows/elsa-foundation/issues/3440#issuecomment-6570028483)); #3441 is closed as completed under [spec 1189](../1189-weaver-recovery/spec.md).

**Review**: Four independent review rounds (2026-11-02) converged. Every finding was checked against the code before it was applied. Round 4 fixed the operation-key storage: a unique column on the created resource, separate from the atomic writer's own server-generated key, with the same key on different input treated as a conflict. Round 4 also fixed the revision identity of the library, create-dialog and template-list contexts. Round 3 covered the base revision for hand edits, selection after a removal, operation keys stored on the resource, and approval in `full-auto`. Round 2 covered review in every autonomy mode, the persisted template revision, operation keys for view and tag creation, the history-based autosave hold, the scope frame, and a strong revision for every batch. Round 1 covered: draft revision identity, autosave suspension, strict batch schemas, removal of the legacy direct-apply classifier, autonomy for every effect, native library operation semantics, native credential and Secret Reference handling, the named Undo path, package ownership, the Design/Runtime split, replacement contracts, catalogs, SemVer, internal visibility and glossary terms.

**Terminology**: *Weaver*, *Weaver Tool*, *Tool Invocation*, *Proposal*, *Operation Batch*, *Interactive Weaver Apply*, *Run*, *Workflow Instance*, *Executable*, *Executable Inspector* and *Secret Reference* keep their meanings in [Studio/CONTEXT.md](../../Studio/CONTEXT.md). An *Expression Module* is the module that owns one expression type's support, as in [Studio ADR 0002](../../Studio/docs/adr/0002-keep-expression-specific-support-in-expression-modules.md); FR-035 adds the glossary entry. *Workflow folder*, *Workflow tag*, *Workflow library view*, *Workflow favorite*, *Graph activity* and *Non-addable activity node* keep their meanings in the [Elsa glossary](../../docs/glossary/elsa.md). This feature uses *Operation Batch* for template working state as well as workflow drafts; FR-035 widens the entry. Two terms are new and receive glossary entries under FR-035:
- A *Reusable Workflow Template* is a Workflows-module resource with its own identity and persisted template revision, from which a new workflow definition can start. It is not a workflow definition.
- A *Context Kind* is a module-owned, versioned description of one Studio context, such as `workflow.definition`.

The *active draft* is the workflow definition open in the visible designer, including its unsaved working state. Outcome words *proposed*, *approved*, *applied locally*, *persisted*, *refused* and *uncertain* keep their [spec 1186](../1186-weaver-effects/spec.md) meanings. Autonomy modes are the shipped `manual`, `auto-read-only` and `full-auto` ([session policy](../1186-weaver-effects/contracts/session-policy.md)).

## User Scenarios & Testing

### User Story 1 - Explain and troubleshoot the active draft against its current data (Priority: P1)

A user has a workflow open in the designer, possibly with unsaved changes, and asks Weaver what it does, why it fails validation, or what a selected activity or expression does. Weaver answers from the draft as it is now: its activities, connections, nested and parallel structure, graph activities, validation diagnostics, and the selected property's expression type and content. It says which revision it read. Credential values never leave the browser.

**Why this priority**: Explaining the current draft is the most frequent workflow request. Today the snapshot omits property and expression content and relies on an empty catalog, so explanations of the actual draft are incomplete.

**Independent Test**: In the normal host, open a seeded workflow that contains:
- a nested flowchart and a parallel branch;
- a graph activity;
- a validation error;
- one JavaScript expression;
- one credential input holding a canary literal, and one input bound to a visible Secret Reference.

Make an unsaved change. Through both provider adapters, using the conformance probe, ask Weaver to explain the workflow, the validation error and the selected expression. Compare the disclosed context with the designer's working state and the designer's diagnostics. Then search every disclosed payload and audit record for the canary.

**Acceptance Scenarios**:

1. **Given** an active draft with unsaved changes, **When** the user asks for an explanation, **Then** the disclosed context reflects the unsaved working state and names its revision identity (FR-005).
2. **Given** a draft with validation diagnostics, **When** the user asks why it is invalid, **Then** Weaver's answer references the current diagnostics by activity and property, and only diagnostics the designer shows.
3. **Given** a selected property with an expression, **When** the user asks about it, **Then** the Workflows module discloses the expression's type and content, bounded and attributed to the draft revision. Interpretation help and semantic diagnostics come from the owning Expression Module's Weaver Tools when that module provides them, and are otherwise reported as unavailable.
4. **Given** a credential input with a literal value, **When** any context is disclosed, **Then** only the fact that a value exists and is withheld is disclosed. **Given** an input bound to a Secret Reference the user can see, **Then** the reference (name, type and scope) is disclosed and no Secret value is.
5. **Given** nested containers, parallel branches and graph activities, **When** Weaver reads the graph, **Then** each activity is identified by its scope path, and a graph activity is described by its referenced definition and version, not by an inlined body.
6. **Given** the draft changes after context was disclosed, **When** Weaver continues, **Then** a later explanation uses the re-admitted current context, and an answer is never attributed to a revision it did not read.

---

### User Story 2 - Author the active draft through one reviewed Operation Batch (Priority: P1)

A user asks Weaver to build or change part of the open workflow. Weaver can add activities in a chosen scope, connect them, set the root, set literal property values or Secret Reference bindings, update allowed activity fields, remove activities or arrange the layout. Weaver proposes one Operation Batch. After the user approves it, the batch is applied to the visible designer in one step. Validation runs, the draft is marked dirty, autosave is suspended, and one step of designer history removes the whole batch. Saving and publishing stay with the user.

**Why this priority**: Real authoring is the core promise of the Weaver workflow lane. Today only a single designer-position change can be applied.

**Independent Test**: In the normal host, ask for a change that needs at least five operation kinds, including one inside a nested scope. Edit the draft by hand just before making the request, so that the edit is part of the batch's base revision. Approve the batch, and observe the designer, its validation, dirty state, autosave and designer history. Undo once and compare the working state with the pre-batch snapshot. Redo, wait past the autosave interval, and check that no batch content was persisted. Check on the server that nothing was saved or published.

As negative cases, edit by hand after the proposal and before approval, and separately change the selection, approve from a second tab, and remount the designer. Each approval is refused as stale.

A component test applies a batch while the designer history capture timer (`historyCaptureDelayMs`, 250 ms) is still armed, to prove the flush.

**Acceptance Scenarios**:

1. **Given** an approved batch bound to the active draft's definition, working-state content revision, client binding and selection, **When** it is applied, **Then** all its operations apply together to the visible working state, or none do.
2. **Given** an applied batch, **When** application finishes, **Then** normal validation runs, the draft is dirty, the outcome is reported as applied locally (not persisted), and the receipt carries the resulting content revision.
3. **Given** a user edit that is part of the batch's base revision but that designer history has not yet captured, **When** the batch is applied, **Then** that edit is captured first, so the batch is its own history entry. One Undo then restores exactly the pre-batch working state, and one Redo restores the whole batch. "Exactly" excludes the test-run result and the published-artifact association, which a batch clears and Undo does not restore (FR-008).
4. **Given** an applied batch, **When** autosave would otherwise run, **Then** autosave stays suspended for that document until the user explicitly saves, or until the designer history position moves before the batch entry. Manual edits made after the batch keep it suspended.
5. **Given** an applied batch, **When** the user undoes it and then redoes it, **Then** the Redo suspends autosave again, and no batch content is persisted without an explicit save.
6. **Given** a batch applied while the user is inside a nested scope, **When** application finishes, **Then** the designer stays in that scope if a node with the same id and slot still exists, and otherwise returns to the nearest existing ancestor. The selection is preserved, unless the batch removes the selected node. In that case the selection moves to the nearest surviving ancestor scope, or is cleared if none exists. The receipt is issued for the originating client binding before the binding changes, and the resulting binding change is expected, not an error.
7. **Given** a batch whose content revision, client binding or selection no longer matches the visible working state (an edit after the proposal, a selection change, a reload, a remount or navigation), **When** application is attempted, **Then** it is refused with zero change and a stale outcome. It is never rebased or retargeted silently.
8. **Given** any batch, **When** it is proposed, approved or applied, **Then** Weaver never saves, publishes or creates a persisted version. Those remain the user's explicit actions.
9. **Given** a batch with an operation that fails its strict schema (an unknown field, a missing property name, a missing target scope or slot, or an unknown activity reference), **When** the batch is prepared or applied, **Then** the whole batch is refused with zero change, and the failing operation is identified.
10. **Given** a batch whose operations carry different risks, **When** it is reviewed, **Then** the review labels each operation's risk, including destructive removals and disconnections. The batch takes the highest risk among its operations, and it is reviewed in every autonomy mode.

---

### User Story 3 - Get a clarification or refusal instead of a guess (Priority: P1)

A user asks for something Weaver cannot do faithfully: a new activity that is not installed or not visible to them, a property whose meaning is unknown, an ambiguous target, a published or historical version, a literal credential, or a change they are not permitted to make. Weaver asks a precise question or refuses with a reason. It never invents an activity, approximates semantics or retargets the request.

**Why this priority**: A confident wrong edit is worse than none. The issue requires explicit clarification or refusal for missing activities and unknown semantics.

**Independent Test**: With the conformance probe on both adapters, make each of these requests and inspect each result kind and the working state:
- add an activity type absent from the catalog;
- add one that is present but hidden from the actor or tenant;
- set a property the activity's descriptor does not declare;
- target an ambiguous activity name that matches two nodes;
- set a literal on a credential input;
- edit a published version;
- edit without manage permission;
- edit an unrelated property of an existing non-addable activity node.

**Acceptance Scenarios**:

1. **Given** a request to add an activity absent from the actor's authorized catalog, **When** Weaver prepares a batch, **Then** the result is a clarification that names the missing capability, with zero working-state change.
2. **Given** an existing non-addable activity node, **When** the user asks to edit it or unrelated content, **Then** the edit is prepared normally. Addability limits only new activities.
3. **Given** a property or input that the activity's descriptor does not declare, or an expression type without an installed Expression Module, **When** Weaver prepares a batch, **Then** the result is a clarification or refusal, not a guessed property.
4. **Given** a request that matches more than one activity or scope, **When** Weaver prepares a batch, **Then** it asks which one is meant and lists the candidates by scope path.
5. **Given** a literal value for a credential input, **When** Weaver prepares a batch, **Then** the operation is refused at preparation, and Weaver offers to bind a visible Secret Reference instead.
6. **Given** an actor without manage permission on the definition, **When** they request an edit, **Then** the edit is refused with the shared denied outcome. Explanation remains available when they can read.
7. **Given** a request whose risk classification is uncertain, or where Studio's recheck against live working state disagrees with the server classification, **When** it is evaluated, **Then** it fails closed into clarification or review (ADR 0003).

---

### User Story 4 - Rely on the real catalog, revision and permissions (Priority: P1)

Weaver's workflow context and tools use the host's actual activity design catalog, the resource's exact identity and the actor's real read and manage permissions. No supported path uses an empty catalog, an `unknown` revision, a deny-all evaluator, a hardcoded permission set or a classifier that grants direct apply.

**Why this priority**: These are the truth seams. Without them, clarification, risk and stale-target checks evaluate placeholders.

**Independent Test**: Compose the normal host and the packaged consumer. Seed a tenant-restricted activity and an actor with read but not manage permission. Read the Weaver workflow context and tool availability for each actor and tenant. Compare catalog, identity and permissions with the owning design services' answers for the same actor. Resolve the composed registrations and assert that the legacy placeholder implementations are absent.

**Acceptance Scenarios**:

1. **Given** the normal host, **When** Weaver reads the activity catalog, **Then** it returns the activity design catalog's entries for the current actor and tenant, with the same availability and versions the designer offers.
2. **Given** a persisted version, an unsaved draft or a template, **When** its context is read, **Then** its revision identity follows FR-005.
3. **Given** an actor's read and manage permissions, **When** context and tool availability are computed, **Then** they reflect the owning authorizer's answers for that actor and tenant, and they change when those permissions change.
4. **Given** the composed host, **When** its registrations are resolved, **Then** the deny-all evaluator, the `unknown` revision provider, the empty catalog provider, the hardcoded permission context, the unsupported proposal service and the direct-apply classifier are absent, and no supported path falls back to them.

---

### User Story 5 - Get a truthful outcome when workflow authorization is unavailable (Priority: P1)

A host replaces the workflow definition authorizer. When that authorizer cannot decide, Studio and Weaver receive an explicit "unavailable" outcome that is distinct from "denied" and discloses no cause. Reading a definition does no more authorization and store work than its guarantees need.

**Why this priority**: Today a replacement authorizer's failure surfaces as a generic server error. A single definition read performs about three permission checks, two store reads and two organization checks. The #3514 Code Review recorded both items for this leaf.

**Independent Test**: Register a replacement authorizer that throws `WorkflowDefinitionAuthorizationException`, and another that denies. Call the definition read API and the Weaver workflow tools. Compare status codes, stable codes and bodies. Count permission checks, store reads and organization checks per definition read, before and after the change. Run the existing authorization boundary tests for visibility, organization, actor, tenant and missing definitions.

**Acceptance Scenarios**:

1. **Given** a replacement authorizer that throws `WorkflowDefinitionAuthorizationException`, **When** a definition is read, **Then** the API returns an explicit unavailable outcome (503 with a stable code), not a generic 500, and the body discloses no cause.
2. **Given** a replacement authorizer that denies, **When** a definition is read, **Then** the existing denied outcome is returned, and it is distinguishable from unavailable.
3. **Given** a Weaver tool whose authority check meets the same exception, **When** it runs, **Then** it fails closed with the unavailable outcome and discloses nothing.
4. **Given** a definition read, **When** duplicate checks are reduced, **Then** native visibility, organization and current actor and tenant guarantees are unchanged. The existing order of not-found and owner decisions is kept, so existence is disclosed no more than today, and a successful replacement reader never stands in for the owning authorizer.

---

### User Story 6 - Work from the workflow library and start a new workflow (Priority: P2)

A user in the workflow library asks Weaver which workflows match an intent, what a selected workflow does, or to narrow the list. Weaver sees the visible list's search, folder, tag and value filters, favorites, sort, publication filter, workflow library view, columns, selection and the user's organization permissions. It can adjust the visible list, and it can propose library changes the user is allowed to make. When the user wants a new workflow, Weaver prefills the normal create dialog, and creating it stays the user's action.

**Why this priority**: The library is the entry point to most workflow work, and its organization contexts were delivered during M2. It builds on the P1 truth seams.

**Independent Test**: In the normal host, seed folders, marker and controlled tags, favorites, a private and a shared workflow library view, and published and draft definitions. Select two definitions, then ask Weaver to:
- explain them;
- narrow the list;
- tag one with an existing tag, and with a tag that does not exist;
- favorite one;
- move one to a folder;
- save the current view;
- start a new workflow for a stated intent.

Compare the disclosed context with the visible list, and compare server state before and after each reviewed effect. Drop the responses of the view save and the tag creation, then repeat everything with organization disabled for the backend and with an actor who lacks tag-management or view-sharing authority.

**Acceptance Scenarios**:

1. **Given** a filtered, sorted list with a workflow library view and a selection, **When** Weaver reads context, **Then** it receives:
   - the visible query;
   - the view's identity, owner kind (private or shared) and version;
   - the selected definitions' identities;
   - the actor's tag-assignment, tag-management, folder and view-sharing permission flags as the native API reports them.

   All of it is bounded to what the user can see.
2. **Given** a request to narrow or reorder the list, **When** the change is applied after approval, **Then** only the visible list state changes. It is reported as applied locally, and the user can revert it.
3. **Given** a request to assign tags, favorite, move to a folder, or save or share a view, **When** Weaver proposes it, **Then** it is a reviewed operation through the owning library service, under native granular authority. It is bound by revision where the native API offers one, and is otherwise an idempotent set.
4. **Given** a multi-target tag operation, **When** it completes, **Then** the outcome is reported for each target, and a partial result is never reported as applied.
5. **Given** a tag that does not exist, **When** Weaver proposes assigning it, **Then** the review shows the tag creation explicitly, and it requires tag-management authority.
6. **Given** a view save or a tag creation whose response is lost, **When** the outcome is resolved, **Then** an authoritative reread of the view or tag by its client-supplied operation key decides it (ADR 0051). A repeated create with the same key returns the existing resource, and no second view or tag is created.
7. **Given** a backend with workflow organization disabled, **When** Weaver reads context or proposes an organization change, **Then** those contexts and tools are unavailable with an explicit reason, and ordinary list support remains.
8. **Given** a new-workflow intent, **When** Weaver acts on it, **Then** it prefills the visible create dialog's working state: name, description, and a root activity from the authorized catalog or a chosen Reusable Workflow Template. Creating the definition remains the user's explicit Create action.
9. **Given** a request to delete or permanently delete definitions, **When** Weaver receives it, **Then** it refuses and points to the library's own delete actions.

---

### User Story 7 - Work with Reusable Workflow Templates as their own resource (Priority: P2)

A user on the templates page or in the selected template editor asks Weaver to explain a template, find one that fits, or change one. Weaver treats a template as its own resource, with its own identity, guidance, tools, validation, risk and rendering. It never turns a template request into a workflow-definition operation. Template persistence stays explicit.

**Why this priority**: Templates were delivered on main during M2, and the 30 October refinement brings them into this leaf. Prompt dispatch alone is not end-to-end support.

**Independent Test**: In the normal host, seed two templates. Filter the list and select one, open it in the template editor, make an unsaved change, and ask Weaver to explain it and to change it. Approve the change. Inspect the template editor's working state, validation, autosave and designer history, and the template store before and after the user's explicit save. Save the template from another tab, then retry the save.

**Acceptance Scenarios**:

1. **Given** the templates list with filters and a selection, **When** Weaver reads context, **Then** it receives the visible filters, the selection, and the template identities and persisted revisions, never workflow definition identities.
2. **Given** a selected template with unsaved changes, **When** Weaver explains it, **Then** the context is the template editor's working state, attributed to its revision identity (FR-005), and the explanation names the persisted template revision it is based on.
3. **Given** an approved template batch, **When** it is applied, **Then** it follows FR-008–FR-012 against the template editor's working state, including autosave suspension. It is persisted only by the user's explicit save through the template service, with that service's expected revision.
4. **Given** a template saved elsewhere after Weaver's change was applied, **When** the user saves, **Then** the template service's normal expected-revision conflict applies, and no change is lost silently.
5. **Given** any template request, **When** it is routed, **Then** no workflow definition is read, changed or used as a stand-in target.

---

### User Story 8 - Inspect versions and Executables read-only (Priority: P2)

A user viewing a workflow's version history or the Executable Inspector asks Weaver what changed between versions, what a published version does, or what an Executable contains and which sources point at it. Weaver answers from the exact version or Executable, read-only. A request to edit a published or historical version is refused, with a pointer to the draft.

**Why this priority**: Explaining what is published and running is valuable without a new mutation path. The current executable prompt also references a tool that does not exist.

**Independent Test**: In a combined host, seed a definition with three versions, one of them published, and an Executable with two Source References. Ask Weaver to compare two versions, explain the published one, explain the Executable, and edit the published version. Compare the answers with the version and Executable APIs. Repeat in a Design-only host and a Runtime-only host.

**Acceptance Scenarios**:

1. **Given** a selected historical or published version, **When** Weaver reads context, **Then** it is bound to that exact version identity, and its result says which version it describes.
2. **Given** two versions, **When** the user asks what changed, **Then** the comparison is Weaver's own, computed over two reads of those exact versions from the owning design services, and says so.
3. **Given** an Executable in a host with the Runtime side, **When** Weaver explains it, **Then** a registered read-only Runtime-side tool returns the Executable's identity, Artifact Hash, graph, and the Source References the actor may see.
4. **Given** a Design-only host, **When** Executable context is requested, **Then** it is explicitly unavailable. **Given** a Runtime-only host, **Then** version and draft journeys are explicitly unavailable.
5. **Given** a request to change a published or historical version or an Executable, **When** Weaver receives it, **Then** it refuses with zero effect and offers to work on the current draft instead. It never retargets the edit silently.

---

### User Story 9 - Explain one exact Run (Priority: P2)

A user viewing one Run asks Weaver why it faulted, where it is waiting, or what an activity execution produced. Weaver reads that exact Run (its Workflow Instance) and, where one is selected, that exact activity execution. Redacted values stay redacted. Data that is unavailable is reported as unavailable rather than inferred: not captured, pruned, unsupported by the backend or not permitted.

**Why this priority**: Troubleshooting runs is a common need. The journey is read-only and depends on existing execution evidence.

**Independent Test**: In a host with the Runtime side, run a seeded workflow to a fault. Include an activity input that is redacted by execution evidence and carries a canary, an activity whose evidence is pruned, and a backend without activity execution inspection. Through both adapters, ask Weaver to explain the fault and the selected activity execution. Compare the answers with the run and evidence APIs, and search disclosed payloads and audit records for the canary.

**Acceptance Scenarios**:

1. **Given** a selected Run, **When** Weaver reads context, **Then** it is bound to that Workflow Instance identity, its Executable and the actor's access, never to "the latest run" of the definition.
2. **Given** a faulted Run with incidents, **When** the user asks why, **Then** the answer cites the recorded incidents and activity executions of that Run.
3. **Given** execution data that existing execution-evidence redaction covers, **When** it is disclosed, **Then** the redaction applies and the value never appears.
4. **Given** evidence that is unavailable for any reason, **When** Weaver answers, **Then** it says, in product terms, which data is unavailable and why, and it does not fill the gap.
5. **Given** a request to retry, cancel, delete or dispatch the Run again, **When** Weaver receives it, **Then** it refuses and points to the existing run controls.
6. **Given** a Design-only host, **When** Run context is requested, **Then** it is explicitly unavailable.

### Edge Cases

- **The designer closes, navigates, remounts or reloads while a batch is pending.** The operation is not retargeted. The working-state adapter reports the target unavailable, and an operation dispatched without a receipt stays uncertain ([spec 1189](../1189-weaver-recovery/spec.md)).
- **Two tabs show the same draft.** A batch applies only in its originating client binding. The other tab is unaffected, and a later save conflict follows the designer's normal rules.
- **A batch references an activity created earlier in the same batch.** Temporary references resolve within the batch, and a dangling reference refuses the whole batch.
- **A batch exceeds the module's declared size or complexity limit.** It is labelled high-complexity, never split and never partly applied.
- **Applying a batch replaces the draft.** FR-008 governs the scope frame and selection. The last test-run result and the published-artifact association are cleared and are not restored by Undo, and the review states this.
- **Content revisions collide.** Two different working states with the same 32-bit content hash could pass a compare-and-replace. FR-008 requires a collision-resistant revision for every batch.
- **The activity catalog changes during a turn,** for example when a module unloads or a tenant loses an activity. Context is re-admitted, and an operation that adds the missing activity becomes a clarification.
- **A graph activity's referenced version is retired.** Explanation says so, and a batch that adds it is a clarification.
- **A property value is an object, a multi-line script or very large.** Context is bounded and says when content is truncated. Weaver never edits a value it could read only partly.
- **A workflow library view belongs to another user and is shared.** Weaver can read it, but cannot propose changing it without view-sharing authority.
- **The workflow authorizer is replaced and intermittently unavailable.** Every affected read and tool returns unavailable, and no partial context is disclosed.
- **Resource content contains instructions,** for example in an activity name, a description or expression text. It is treated as untrusted resource content, never as installed-module guidance.
- **A Run is still executing while it is explained.** The answer is attributed to the time of the read and says the Run is in progress.
- **A session reaches its disclosure limits** (64 snapshots or 8 MiB, [spec 1189](../1189-weaver-recovery/spec.md) FR-016). New context kinds fit within those limits, and each declares its own size bound.

## Requirements

### Functional Requirements

#### Read-path hardening

- **FR-001**: The workflow design API MUST map a `WorkflowDefinitionAuthorizationException` from any authorizer to an explicit fail-closed unavailable outcome. The outcome is HTTP 503 with a stable code, it is distinct from the denied outcome, and it discloses no cause. Weaver workflow tools MUST surface the same distinction.
- **FR-002**: A definition read MUST NOT repeat authorization, store or organization work that it does not need. Any reduction MUST keep native visibility, organization and current actor and tenant guarantees. It MUST also keep the existing order of not-found and owner decisions, so that existence is disclosed no more than today. This is demonstrated by the existing authorization boundary tests plus a regression on read counts. A successful replacement reader MUST NOT stand in for the owning authorizer's decision. The built-in reader MAY hand request-scoped facts it established (existence in the tenant, and the Read and organization checks it ran) to the built-in authorizer for the same definition, principal and request; any other combination does the authorizer's full work.

#### Truth seams

- **FR-003**: The Weaver workflow activity catalog MUST be read from the host's activity design catalog under the current actor and tenant authority, with the same availability and versions the designer offers.
- **FR-004**: Workflow context MUST report the actor's real read and manage permissions from the owning authorizer, and tool availability MUST follow them.
- **FR-005**: Every workflow context, tool result and operation MUST carry an exact revision identity:
  - **Unsaved draft:** the definition identity, the existing working-state content revision and the client binding (the editor generation). FR-008 strengthens the content revision.
  - **Persisted workflow definition version:** its version identity.
  - **Reusable Workflow Template:** its template identity plus its persisted template revision, which is also the expected revision for persistence. An unsaved template adds the working-state content revision and the client binding.
  - **Run:** its Workflow Instance identity, plus the activity execution identity where one is selected.
  - **Executable:** its Artifact Hash.
  - **Workflow library list:** the visible query (search, filters, sort, publication filter, columns) plus the actor's permission flags. When a Workflow library view is applied, it also carries that view's identity, its private or shared owner kind and its version.
  - **New-workflow create dialog:** the client binding of the open dialog.
  - **Template list:** the visible filters and selection, plus each listed template's identity and persisted template revision.

  No persisted revision field is added. The existing layout tool's strict receipt keeps working.
- **FR-006**: The placeholder implementations MUST be removed:
  - `DenyAllWorkflowChangePermissionEvaluator`;
  - the `unknown` revision provider;
  - the empty catalog provider;
  - the hardcoded permission context;
  - `DefaultWorkflowChangeProposalService`;
  - the direct-apply decision of `DefaultWorkflowGraphOperationBatchRiskClassifier`.

  Each legacy contract in `Elsa.Agent.Workflows` is either removed or kept as a replacement contract with a real default, and the plan records which. No supported path may fall back to a placeholder. A missing owning service MUST fail explicitly.

#### Draft and template authoring

- **FR-007**: The Workflows module MUST provide one versioned client working-state Operation Batch tool with strict per-operation schemas and allowlisted fields. Its operations are:
  - add an activity into an explicit target scope or slot;
  - set the root;
  - set a designer position;
  - set an activity property by explicit property name;
  - update allowlisted activity fields;
  - remove an activity;
  - connect and disconnect activities.

  The designer applier MUST be extended to enforce the same schemas. No operation may rewrite an activity's type or version, its children or undeclared fields, and no default property name exists. The tool declares which resources it supports, the workflow designer and the template editor (Studio ADR 0017).
- **FR-008**: A batch MUST be applied only to the visible working state of its originating client binding. It is applied atomically, after a compare-and-replace on resource identity, content revision, client binding and selection. The content revision MUST be collision-resistant for every batch, which strengthens the current 32-bit hash. Application MUST run normal validation, mark the document dirty, and return a correlated receipt with the resulting content revision. Application MUST also:
  - preserve the selection, because a selection change rotates the client binding. When the batch removes the selected node, the selection moves to the nearest surviving ancestor scope, or is cleared if none exists. The receipt is issued for the originating client binding before the binding changes, and the resulting binding change is expected, not an error;
  - keep the user's current scope frame when a node with the same id and slot still exists, and otherwise return to the nearest existing ancestor;
  - never reuse a removed node id for a temporary reference;
  - clear the test-run result and the published-artifact association, which Undo does not restore.
- **FR-009**: Undo of a batch MUST go through the designer history. Pending history capture MUST be flushed before a batch is applied, so the batch is exactly one history entry that one Undo reverts and one Redo restores. A component test MUST prove the flush by applying a batch while the capture timer is still armed. The review card's whole-change Undo MUST invoke designer-history Undo, so that a later history Undo cannot apply the batch again. It acts only while no later edit exists. After a later edit it MUST be disabled with a reason, and designer history remains the path.
- **FR-010**: Applying a batch MUST suspend autosave for that document until the user explicitly saves, or until the designer history position moves before the batch entry. Release depends on the history position, not on content equality, and a Redo of the batch entry suspends autosave again. Later manual edits keep it suspended. Template documents follow the same rule through their adapter.
- **FR-011**: Weaver MUST NOT save, publish or otherwise persist a workflow definition, version or template. Outcomes MUST distinguish proposed, approved, applied locally, persisted (by the user), refused and uncertain.
- **FR-012**: The Workflows module MUST classify each operation's risk with native `AgentRisk` values and label it in review: `ReviewRequired`, or `Destructive` for removals and disconnections. A batch takes its highest operation risk. Every Operation Batch is reviewed in every autonomy mode, including batches of only ordinary edits. This is stricter than the direct apply that root [ADR 0002](../../docs/adr/0002-direct-weaver-workflow-edits-target-designer-working-state.md) and [ADR 0003](../../docs/adr/0003-weaver-direct-apply-risk-classification-is-elsa-owned.md) describe. Stricter behavior is permitted, so no amendment is needed. Classification is Elsa-owned (ADR 0003): the server classifies a batch before returning it, and Studio rechecks against live working state before applying it. Any disagreement or uncertainty fails closed into clarification or review.
- **FR-013**: These requests MUST produce an explicit clarification or refusal with zero working-state change:
  - adding an activity that is absent from the actor's authorized catalog;
  - an undeclared property or input;
  - an expression type without an installed Expression Module;
  - an ambiguous target.

  Existing non-addable activity nodes remain editable.
- **FR-014**: Graph context and operations MUST address activities by scope path through nested containers and parallel branches. They MUST reference graph activities by definition identity and version, without inlining their bodies.
- **FR-015**: An operation targeting a stale revision identity, a published or historical version, or an Executable MUST be refused with zero effect, and never rebased or retargeted.

#### Property, expression and credential context

- **FR-016**: The Workflows module MUST disclose the selected activity's declared properties and, for expressions, their expression type and content only. The disclosure is bounded and attributed to the revision identity. Interpretation, semantic diagnostics and expression edits belong to the owning Expression Module's Weaver Tools (Studio ADR 0002). Without such a tool, expression edits are refused with an explicit outcome.
- **FR-017**: For credential inputs (`isCredential`), a literal value MUST be disclosed only as present and withheld. An operation that writes a literal to a credential input MUST be refused at preparation. Binding a Secret Reference that the user can see is allowed, and no Secret value is ever disclosed.

#### Explain and troubleshoot

- **FR-018**: Explanations MUST be grounded in the context disclosed for the current revision identity, including the designer's current validation diagnostics. They MUST name the revision they describe, and for a template that includes its persisted template revision.
- **FR-019**: Every Weaver prompt, guidance text and result renderer that the Workflows packages contribute MUST reference only registered tools and result kinds, and MUST be registered under its own package's module identity. The unused `workflows.proposal` renderer MUST be removed or bound to a real result kind. The explain-executable prompt MUST reference only the registered tool from FR-028.

#### Workflow library, new-workflow intent and templates

- **FR-020**: The workflow library MUST contribute a list context containing:
  - the visible search, folder, tag and value filters, favorites, sort and publication filter;
  - the workflow library view's identity, owner kind and version;
  - the column state;
  - the selected definitions' identities;
  - the actor's tag-assignment, tag-management, folder and view-sharing permission flags, as the native API returns them.

  The context is bounded to what the actor can see.
- **FR-021**: Library writes MUST be reviewed operations through the owning library services, under native granular authority. They cover tag assignment, favorites, folder moves, and saving or sharing a workflow library view.
  - Each operation is bound by revision where the native API offers one, and is otherwise an idempotent set.
  - Multi-target operations report per-target outcomes and never report a partial result as applied.
  - Assigning a tag that does not exist requires tag-management authority and shows the tag creation in the review.
  - View create and tag create gain an optional client-supplied operation key, as an additive library API change. The key is stored in a unique column on the created view or tag row. A repeated create with the same key and the same input returns that existing resource. The same key with different input is refused as a conflict. Recovery never depends on replaying a stored response: after an ambiguous response, the outcome is resolved by rereading the resource by its key. This follows ADR 0051's preference for a client-known or deterministically derived identity. The library store's atomic writer and its fence stay in place. The client key is not used as the writer's `DesignOperationKey`, which keeps its fresh server-generated key, so the `designOperation` ledger's replay plays no part in these commands' recovery.

  Deleting definitions MUST NOT be offered.
- **FR-022**: When workflow organization is disabled for a backend, organization contexts and tools MUST be unavailable with an explicit reason, while ordinary list support remains.
- **FR-023**: A new-workflow intent MUST only prefill the visible create dialog's working state: name, description, and a root activity from the authorized catalog or a Reusable Workflow Template. Creating the definition MUST remain the user's explicit action.
- **FR-024**: Reusable Workflow Templates MUST be their own resource, with module-owned context, guidance, tools, validation, risk and rendering, using the actual template service. The template list context MUST carry the visible filters, the selection, and the template identities and persisted revisions. Template batches follow FR-007–FR-012 through the template editor's adapter. Persistence MUST be the user's explicit save, with the template service's expected revision. No template request may read, change or target a workflow definition.

#### Effects and autonomy

- **FR-025**: Every effect MUST declare its native `AgentRisk`, its review requirement and its receipt. Every effect below is reviewed in every autonomy mode, and the existing session policy is reused unchanged:
  - client working-state tools require approval in every mode;
  - `auto-read-only` requires approval for mutations;
  - the server library-write tools return `RequiresApproval` from preparation, as the reviewed visibility operation does in the spec 1186 probe case `visibility-review-control`, so review holds in `full-auto` too.

  | Effect | Risk | Review | Receipt |
  | --- | --- | --- | --- |
  | List narrowing, sort and columns | `ReviewRequired` | every mode; revertible | local list adapter, applied locally |
  | Create-dialog prefill | `ReviewRequired` | every mode; revertible | create-dialog adapter, applied locally |
  | Operation Batch (draft or template) | `ReviewRequired` or `Destructive` (FR-012) | every mode | working-state adapter, applied locally |
  | Library server writes | `ReviewRequired` | every mode | owning service, persisted, per target |

#### Versions, Executables and Runs

- **FR-026**: Version context and tools MUST be read-only and bound to exact version identities, and MUST be able to compare two exact versions: Weaver's own comparison, computed over two reads from the owning design services (R6), and labelled as such.
- **FR-027**: Executable and Run context and tools MUST live in a Runtime-side package. They MUST be explicitly unavailable in Design-only hosts, and Design journeys MUST be explicitly unavailable in Runtime-only hosts. The Design-only, Runtime-only and combined deployment shapes MUST all keep working.
- **FR-028**: A registered read-only Executable tool MUST return an Executable's identity, Artifact Hash, graph, and the Source References the actor may see.
- **FR-029**: Run context and tools MUST be read-only and bound to one exact Workflow Instance and, where selected, one exact activity execution, under the actor's existing run access. They MUST apply existing execution-evidence redaction. Data that is not captured, pruned, unsupported or not permitted MUST be reported as explicitly unavailable.

#### Ownership, contracts and catalogs

- **FR-030**: Each contribution MUST live in its owning package:
  - **`Elsa.Agent.Workflows`:** Design-side server context kinds and tools for drafts, versions, the library and templates.
  - **A new Runtime-side Weaver workflows package beside it, named in the plan:** Executable and Run context kinds and tools.
  - **`Elsa.Studio.Workflows`:** client working-state adapters for the designer, the template editor, the create dialog and list state.
  - **`Elsa.Studio.Weaver.Workflows`:** prompt dispatch and result renderers.
  - **The owning Expression Module:** expression help, semantic diagnostics and edits.

  Weaver core, the Studio host and provider adapters MUST NOT gain workflow, template or library knowledge.
- **FR-031**: Context MUST be bounded, authorized and attributed to its resource and revision identity, and MUST fit within the session disclosure limits. Navigation MUST NOT retarget a pending operation. Resource content MUST be treated as untrusted, distinct from installed-module guidance.
- **FR-032**: Every replacement contract this feature adds or keeps MUST prevent or detect a second registration with a clear diagnostic (constitution-framework §2.6.2). The Agent-wide gap for existing contracts remains #3633. New implementations MUST be `internal sealed` (ADR 0080 D3).
- **FR-033**: Removed or changed public surface MUST carry its SemVer classification (constitution §4.2, ADR 0080 D4). The plan records each item:
  - the removed legacy contracts and implementations in `Elsa.Agent.Workflows` are Major, as pre-GA cleanup;
  - the removed `workflows.proposal` renderer is Major;
  - the new Operation Batch tool contract and the new context kinds are Minor;
  - the authorization-unavailable outcome is Minor;
  - the optional operation key on view create and tag create is Minor and additive. It is added as default interface member overloads on `IWorkflowLibraryStore.CreateTagAsync` and `SaveViewAsync`, so existing implementations keep compiling. The default reports that the operation key is unsupported. For a replacement store that ignores the key, a keyed create is therefore unavailable, never silently non-idempotent (SC-008).
- **FR-034**: The extension-point catalogs MUST be updated in the same change as the code they describe (constitution §2.22.1). These are:
  - the "Legacy domain seams" section of `Extensions/Agent/src/Workflows/Elsa.Agent.Workflows/EXTENSION_POINTS.md`;
  - in `Essentials/src/Workflows/Design/Api/EXTENSION_POINTS.md`, the authorizer and details-reader failure semantics, and the operation-key behavior of view create and tag create;
  - in `Essentials/src/Workflows/Design/Api/EXTENSION_POINTS.md`, which catalogues `IWorkflowLibraryStore`: its operation-key members and their unsupported default;
  - in `Essentials/src/Workflows/Design/Persistence/EntityFrameworkCore/EXTENSION_POINTS.md`: the unique operation-key index on the EF library store;
  - entries for each new tool, context kind and Runtime-side contribution.
- **FR-035**: `Studio/CONTEXT.md` MUST gain entries for *Reusable Workflow Template*, *Context Kind* and *Expression Module*, the last linking Studio ADR 0002. Its *Operation Batch* entry MUST be widened to cover template working state.

#### Evidence

- **FR-036**: Each journey in stories 1–9 MUST have normal-host end-to-end evidence. Where a journey has a tool or effect, it MUST also have a conformance-probe case on both provider adapters, with negative controls for stale revision identities, published and historical resources, denied permissions, unavailable or redacted data, and each deployment shape.
- **FR-037**: Evidence MUST keep three categories distinct:
  - deterministic component tests;
  - deterministic provider-protocol native evidence on the actual Copilot and Anthropic adapters, with packaged consumers;
  - opt-in live-provider evidence (none claimed).

  Failures, skips and quarantines MUST be recorded as they occurred.

### Key Entities

- **Context Kind**: a module-owned, versioned context. The kinds are the active draft, the selected activity property, the workflow library list, the create dialog, the template list, the template editor, a version, an Executable and a Run. Each has its own bounds and revision identity.
- **Revision identity**: the exact identity that a context or operation is bound to (FR-005).
- **Operation Batch**: a versioned set of strictly typed operations (FR-007), with temporary references, a target revision identity and per-operation risk.
- **Operation risk classification**: a risk and reasons for each operation, and the resulting decision of review, clarification or refusal. The reasons are ordinary edit (reviewed as `ReviewRequired`), destructive, high complexity, unavailable activity, stale, denied, credential literal and uncertain.
- **Authorization outcome**: allowed, denied or unavailable, each with a stable code and no disclosed cause.
- **Property context**: a selected activity's declared properties, expression types and bounded content, with markers for withheld credentials and Secret References.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Each of the nine stories has at least one passing normal-host journey. Each story with a tool or effect has passing conformance-probe cases on both provider adapters.
- **SC-002**: In every tested applied batch, one Undo restores the exact pre-batch working state, including a hand edit that is part of the base revision. The component test with an armed capture timer proves the same. The test-run result and published-artifact association are excluded, because they are not restored. Autosave never persists batch content without the user's explicit save, including across an Undo followed by a Redo. After an Undo, autosave may persist the pre-batch state. Zero definition versions or templates are persisted without the user's explicit save or publish.
- **SC-003**: Resolving the composed registrations of the normal host and the packaged consumer finds zero placeholder implementations. Every supported context reports a catalog, revision identity and permissions equal to the owning services' answers for the same actor and tenant.
- **SC-004**: 100% of these negative controls end in clarification or refusal with zero working-state or server change: missing or hidden new activity, undeclared property, ambiguous target, credential literal, stale revision identity, published or historical target, denied permission, and organization disabled.
- **SC-005**: Seeded canary values in credential literals and in redacted run data appear in zero disclosed payloads, results or audit records.
- **SC-006**: An authorizer that throws yields the unavailable outcome in 100% of tested reads and tools. The outcome is distinguishable from denied and discloses no cause. A definition read performs no more permission checks, store reads or organization checks than before, and fewer where the guarantees allow.
- **SC-007**: A behavior test over the composed Studio and server registrations finds zero Workflows-package prompts, guidance or renderers that reference an unregistered tool or result kind.
- **SC-008**: Each multi-target library operation reports one outcome per target, and none reports a partial result as applied. A view save or tag creation whose response is dropped resolves, by its operation key, to exactly one view or tag.
- **SC-009**: The Design-only, Runtime-only and combined hosts each start and serve their supported journeys. Each reports unsupported journeys as explicitly unavailable.
- **SC-010**: The issue closes only after the gates in [merging](../../docs/contributing/merging.md) pass, with evidence recorded by category.

## Assumptions and Boundaries

- Prerequisites #3440 and #3441 are Done. Their session policy, review binding, receipts, uncertainty, client completion and recovery are reused unchanged.
- **Delivery note:** one specification, delivered as sequential, independently reviewed PRs that each say "Refs #3442", with the last one closing it. The slice structure is in the plan.
- The following defaults are decisions taken under the owner's Program authority, and they are open to review:
  - Operation Batches apply only to the visible working state, atomically, with normal validation, dirty marking, autosave suspension and a single designer-history Undo. Save and publish are never Weaver actions.
  - The server reads the real activity design catalog under the current actor and tenant.
  - Revision identity follows FR-005. No persisted revision column is added.
  - A replacement authorizer's exception maps to a fail-closed unavailable outcome, distinct from denied. The plan names its stable code. Duplicate reads are reduced only where the same guarantees hold.
  - Property, expression and run data are disclosed only through module-owned context with the existing credential, Secret Reference and execution-evidence rules.
  - Every effect is reviewed in every autonomy mode under the existing session policy, which is reused unchanged. This covers Operation Batches, library server writes, list narrowing and create-dialog prefill. Reviewing graph batches of only ordinary edits is stricter than the direct apply in root ADRs 0002 and 0003, and needs no amendment.
  - View create and tag create gain an optional client-supplied operation key, an additive library API change. The key is stored in a unique column on the resource row, separate from the atomic writer's own server-generated operation key. The same key with different input is a conflict.
  - Library deletes, run controls, and edits to published versions or Executables are refused rather than offered.
  - Evidence follows FR-037. No live-provider purchase, credentials, release or deployment is part of this feature.
- Runtime model semantics are not redefined. Workflow-runtime agent execution stays with Program #2892.
- Broader current-module journeys remain #3443, and integrated qualification remains #3444.

## Issue Traceability

| #3442 acceptance | Requirements and stories |
| --- | --- |
| Real authorized host catalogs, exact versions and revisions, and domain APIs; replace the hardcoded catalog, unknown revision and seam-only permissions | FR-003–006/020/024/026–029; stories 4/6/7/8/9 |
| Definitions lists, filters and selections; new workflow intent; active unsaved drafts; selected activities and expressions; versions; executables; exact run occurrences | FR-016/017/020/023/026–029; stories 1/6/8/9 |
| Explain, troubleshoot and make reviewed edits against current data; clarification or refusal for missing activities or unknown semantics | FR-012/013/016/018/019; stories 1/2/3 |
| Atomic visible batches, normal validation, dirty state, whole-batch Undo; explicit save and publish | FR-007–011; story 2 |
| Nested, parallel and reusable scope; stale revisions; historical and published resources; denied permissions; unavailable or redacted execution data | FR-004/005/013–015/017/027/029; stories 1/3/8/9 |
| Normal-host end-to-end effects, meaningful regressions and the provider evidence boundary | FR-036/037; SC-001/010 |

| 30 October refinements | Requirements and stories |
| --- | --- |
| Reusable workflow templates: identity and revision, filters and selection, unsaved state, module-owned guidance, tools, validation, risk and rendering, reviewed effects, explicit persistence, no definition retargeting | FR-005/007/010/024/030; story 7 |
| Workflow library tag and value filters, favorites, sort and publication filters, view and column state, private and shared view ownership, exact revisions and native granular authority | FR-020–022/025; story 6; SC-008 |

| Workflow authorization read-path follow-through | Requirements and stories |
| --- | --- |
| Reduce duplicate GET authorization and store reads while keeping the visibility, organization, actor and tenant guarantees | FR-002; story 5; SC-006 |
| Explicit fail-closed behavior for a replacement authorizer's `WorkflowDefinitionAuthorizationException`: unavailable versus denied, without causes | FR-001; story 5; SC-006 |

### Acceptance evidence

Evidence categories:
- **C**: deterministic component tests on the real default services, the EF design store and the Studio client (Vitest and Playwright), and the probe driver's own unit tests (`test_*.py`), which check the driver's assertions on synthetic records and are not native evidence.
- **P**: deterministic provider-protocol native evidence. It uses the actual Copilot (delegated) and Anthropic (host-driven) adapters, with packaged consumers on the packaged EF store over SQLite, and drives each journey through the normal host in a real browser (`tools/studio/Weaver.Conformance.Probe`). Its cases are named `{copilot,anthropic}-workflow-<journey>`.
- **Live**: live-provider evidence. None is claimed.

The slices landed as #3671 (A), #3704 (B), #3715 (C), #3722 (D), #3727 (E), #3732 (F) and this specification's closing PR (G). Each PR records its witness runs, including the failed attempts and their causes, and its Code Review fallback evidence. The local native proofs ran on `8e5657d39`, slice G's integrated head with slices E and F and `main`. Three things came after it:
- run inspection follows a propagated incident's causal chain through each child's own incidents, and withholds child-control and poisoned-work incidents outright (child-control incidents now name their calling child);
- any reload cancellation after the save is logged;
- `main` was merged, adding #3730's Studio tag UX in 5 files.

The evidence for the first two is unit tests: `WorkflowRunInspectionTests`, `WorkflowControlInboxTests` and `ShellReloaderTests`. The witness does not exercise a child-control fault. The closing PR merges only once CI's independent package witness and the Studio browser shards pass on its exact head, and that run is recorded on the PR at merge.

| #3442 item | Evidence |
| --- | --- |
| Real authorized host catalogs, exact versions and revisions, and domain APIs | C: `ActivityAuthoringCatalogContractTests`, `WorkflowTruthSeamTests`, `WorkflowAgentNativeAuthorityTests`, `WorkflowVersionReaderTests`, `WorkflowExecutableInspectionTests`, `WorkflowRunInspectionTests`, `StudioModuleManifestProviderTests`. P: `workflow-truth-seams`, `workflow-versions`, `workflow-executables` and `workflow-run` on both adapters. C: `test_truth_seam_journeys.py`, `test_inspection_journeys.py`, `test_run_journeys.py` |
| Lists, filters and selections; new workflow intent; active drafts; selected activities and expressions; versions; Executables; exact Runs | C: `WorkflowLibraryAgentTests`, `WorkflowVersionAgentTests`, `WorkflowExecutableAgentTests`, `WorkflowRunAgentTests`, `workflowLibraryAgent.test.tsx`, `workflowInspectionAgent.test.tsx`, `workflowExplainContext.test.tsx`, `workflowContextSize.test.tsx`. P: `workflow-library`, `workflow-versions`, `workflow-executables` and `workflow-run` on both adapters |
| Explain, troubleshoot and reviewed edits against current data; clarification or refusal | C: `WorkflowExplainContextTests`, `WorkflowOperationBatchTests`, `workflowAgent.test.tsx`. P: `workflow-explain` and `workflow-batch` on both adapters, including the missing-activity, undeclared-property, ambiguous-target and credential-literal controls. C: `test_explain_journeys.py`, `test_batch_journeys.py` |
| Atomic visible batches, validation, dirty state, whole-batch Undo; explicit save and publish | C: `WorkflowBatchConformanceTests`, `workflowBatchConformance.test.ts`, `workflowGraphOperationBatch.test.ts`, `workflowOperationBatchEditor.test.tsx`, `workflowEditorSaveCommands.test.tsx`, `workflowPublicationOperations.test.tsx`. P: `workflow-batch` on both adapters (apply, Undo, autosave hold) |
| Nested, parallel and reusable scope; stale revisions; historical and published resources; denied permissions; unavailable or redacted data | C: `WorkflowOperationBatchTests`, `WorkflowVersionAgentTests`, `WorkflowRunAgentTests` (withheld sensitive values and incident messages). P, on both adapters: nested scope through the nested flowchart in `workflow-batch` (parallel and reusable scope rest on C alone); stale: the stale edit in `workflow-batch` and the stale hash in `workflow-executables`; published or historical targets: the historical-version control in `workflow-batch`, and the refused edits in `workflow-versions` and `workflow-executables`; denied permissions: `workflow-truth-seams`, `workflow-batch`, `workflow-library`, `workflow-versions`, `workflow-executables`, `workflow-run` and `workflow-authorization-unavailable`; unavailable or redacted data, through redaction canaries: `workflow-explain`, `workflow-batch`, `workflow-versions`, `workflow-executables` and `workflow-run`. |
| Normal-host end-to-end effects, regressions and the provider evidence boundary | P: the complete package witness below. C: the composed Studio suite below. Live: none |
| Deployment shapes (SC-009) | C: `WorkflowDesignOnlyHostTests`, `WorkflowRuntimeOnlyHostTests`, `WorkflowRuntimeOnlyCompositionTests`, `WorkflowDeploymentShapeTests`. P: the deployment-shape controls inside the `workflow-versions`, `workflow-executables` and `workflow-run` cases |

| 30 October refinements | Evidence |
| --- | --- |
| Reusable Workflow Templates | C: `WorkflowLibraryCommandsTests`, `TemplateDesignUpgradeTests`, `WorkflowsDesignRetainedMigrationTests`, `workflowDefinitions.test.tsx`. P: `workflow-templates` on both adapters. C: `test_library_journeys.py` |
| Workflow library filters, favorites, sort, views, ownership, revisions and native authority | C: `WorkflowLibraryApiContractTests`, `EfWorkflowDesignPersistenceTests` (library operation keys), and the native-database library witness tests on PostgreSQL, SQL Server and MySQL. Those are `*_workflow_library_organization_is_durable_and_bounded` and `*_operation_keys_resolve_to_exactly_one_tag_or_view` in `EfWorkflowDesignNativeProviderSmokeTests.cs`; they ran under Docker, 6 of 6 passing (#3727). P: `workflow-library` on both adapters, including the dropped-response operation-key replay (SC-008) |

| Workflow authorization read-path follow-through | Evidence |
| --- | --- |
| Fewer duplicate GET authorization and store reads | C: `DefinitionReadAuthorizationTests` (counted permission checks, store reads and organization checks) |
| Fail-closed replacement authorizer: unavailable versus denied | C: `WorkflowAuthorizationUnavailableTests`, `AgentOwnerUnavailableTests`, `AgentContextOwnerUnavailableTests`, `AgentRetainedOutcomeAdmissionTests`, `workflow-authorization-unavailable.spec.ts`. P: `workflow-authorization-unavailable` on both adapters. C: `test_authorization_journeys.py` |

Final-head proofs:
- **Complete package witness:** 107 of 107 cases, no gaps, `independentPackageWitness: true`, report SHA-256 `631345d80a9e41406b86d5048e313ef450b468fc7516bd2d408a56d15e313d98`. An earlier head, `f0b244f51`, also passed 107 of 107. The attempt on `2cfeced8f` failed because a feature apply's shell reload raced the configuration file watcher; `ShellReloader` now retries that CShells refusal, at most three attempts in all.
- **Composed Studio suite:** 948 tests: 942 passed, 6 skipped, 0 failed, 0 flaky. The 6 skips are opt-in screenshot captures that run only when asked: `activity-icons`, `elsa3-reusable-import`, `home`, `nav-placement` and two in `settings-shell`.
- **Required CI:** the closing PR merges only once `Build & test`, `Generated maps fresh` and `Full tier` are green on its exact head, including CI's own independent package witness. That run is recorded on the PR.
