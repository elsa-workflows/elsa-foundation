# Feature Specification: Activity Data Selection

**Feature Branch**: `agent/codex/3885-data-selection-contracts`

**Created**: 2026-11-05

**Status**: Approved — effective on M2 PR merge; production dispatch requires resulting-main qualification.

**Input**: Authors want to select an earlier activity's output or nested field at a consuming input without declaring a variable or writing an expression. The owner approved the input-centered direction and instructed: "Set a goal to deliver all milestones".

**Program**: [Activity Data Selection #3882](https://github.com/elsa-workflows/elsa-foundation/issues/3882).
**Specification owner**: [M2 #3885](https://github.com/elsa-workflows/elsa-foundation/issues/3885).
**Existing feature**: [#3094](https://github.com/elsa-workflows/elsa-foundation/issues/3094).
**Evidence**: [M1 research](../../docs/reports/activity-data-selection-m1.md).
**Delivery sequence**: [Program plan](../../docs/plans/activity-data-selection.md).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Choose earlier data at an input (Priority: P1)

An author adds Get customer and a consuming activity to a Sequence or Flowchart. At the consuming
Customer ID input, Choose data opens a source browser. The author expands Customer, selects Customer ID
and sees a readable reference identifying the producer and field. The workflow saves, reopens, publishes
and runs with the expected ID. No workflow variable, output capture or handwritten expression is needed.

**Why this priority**: It directly removes the reported variable ceremony from the ordinary authoring path.

**Independent Test**: Author and execute the customer journey in both structures, including an untouched
input, whole-object selection and nested-field selection, with the optional text-language modules absent.

**Acceptance Scenarios**:

1. **Given** a typed producer and an untouched eligible input, **When** Choose data opens before any run,
   **Then** the available producer and its declared fields are discoverable without first entering a literal.
2. **Given** a customer object with Customer ID, **When** its ID is selected, saved, reopened and executed,
   **Then** the consumer receives the selected value and the graph contains no synthetic variable or capture.
3. **Given** an object, collection or dictionary target, **When** a compatible whole value or member is selected,
   **Then** its shape is retained; collection choice does not implicitly select an element or repeat an activity.
4. **Given** a pending graph edit, **When** source discovery occurs, **Then** offered sources correspond to the
   current editable graph and its base revision; a later edit cannot leave stale facts presented as current.
5. **Given** a host without optional text-language modules, **When** an ordinary supported member is selected,
   **Then** basic mapping can still save, publish and execute with the same meaning.

### User Story 2 - Transform selected data explicitly (Priority: P1)

An author uses the same source browser while composing an input expression. Selection inserts a reference
appropriate to the installed syntax and supplies its binding. The author can transform a customer ID or
combine it with another value without manually maintaining parameter declarations.

**Why this priority**: Simple mapping and flexible expressions form one authoring experience.

**Independent Test**: Select a source in compact and expanded installed expression editors, add a small
transformation, undo/redo, save/reopen and run. Verify a parameter-name collision and unavailable editor.

**Acceptance Scenarios**:

1. **Given** an installed syntax adapter, **When** a source is inserted, **Then** both the reference text and
   required binding are added together using that syntax's supported access rules and escaping.
2. **Given** an existing parameter with the same proposed name, **When** another source is inserted,
   **Then** the sources remain distinct and the existing expression meaning is preserved.
3. **Given** source insertion, **When** Undo is used once, **Then** the inserted text and associated binding
   are restored together; expansion and collapse preserve selection, source and editing continuity.
4. **Given** a missing or unsupported editor, **When** an existing expression is inspected or the document
   is saved, **Then** the original source and bindings remain intact and the unavailable capability is explicit.

### User Story 3 - Edit and repair connected activities (Priority: P1)

An author renames, moves, duplicates or deletes activities after mapping values. References retain their
identity or remain inspectable with a useful explanation and repair action. Display names do not become
identities, and changing the graph never silently changes the chosen producer or drops a member selection.

**Why this priority**: A convenient picker is unsafe if routine graph edits corrupt its mappings.

**Independent Test**: Map a source, rename it, duplicate a producer/consumer pair and a consumer alone,
delete/move a source, use Undo/Redo, repair the reference, save/reopen and publish/run.

**Acceptance Scenarios**:

1. **Given** two producers with the same label, **When** one is selected and later renamed, **Then** the
   reference still identifies that producer and displays its current human-readable name.
2. **Given** a copied producer/consumer pair, **When** it is pasted into a compatible graph, **Then** the
   copied consumer points to the copied producer; copying the consumer alone retains a valid external source
   or shows a repairable invalid reference.
3. **Given** a deleted or unavailable required source, **When** the workflow is published or test-run,
   **Then** validation rejects it before creating or activating an executable and identifies the input to repair.
4. **Given** a mapped document, **When** graphical save, code Apply, reload or Undo/Redo is used,
   **Then** source identity, member selection, optionality and conversion meaning survive.

### User Story 4 - Use the correct causal value through control flow and recovery (Priority: P1)

An author uses mapped data in loops, branches, reusable activities and workflows that suspend. A reference
selects the causal producer occurrence belonging to the consumer's path and iteration. Restart and retry
preserve committed input meaning; another branch or a later occurrence cannot silently supply a replacement.

**Why this priority**: The feature must preserve Elsa's execution and durability semantics.

**Independent Test**: Run two distinct loop items, a conditional/join case, an explicit scope return and a
producer-once → suspension → process restart → consumer-completion journey on the same disposable store.

**Acceptance Scenarios**:

1. **Given** customers A and B in two iterations, **When** each consumer executes, **Then** it receives its
   corresponding ID, independent of completion order.
2. **Given** a producer missing on one valid path, **When** a required reference is offered or published,
   **Then** it is not presented as guaranteed available; existing explicit optional/return/merge rules apply.
3. **Given** a successfully committed producer, **When** a required selected member is missing or invalid,
   **Then** the consuming input reports the failure without retroactively failing or rerunning the producer.
4. **Given** suspension after the producer, **When** the owned process restarts and execution resumes,
   **Then** the consumer completes with the same selected value and the producer's execution count remains one.

### User Story 5 - Use the same capability across eligible editor surfaces (Priority: P2)

An author encounters the same selection behavior wherever Studio composes the eligible consuming-input
editor, while source-owned, constrained and model-specific controls keep their actual contracts.

**Why this priority**: Equivalent authoring surfaces must not make users relearn value transfer.

**Independent Test**: Exercise the finite surface and input-family inventory below, including restricted
composition, a reusable public output and an activity-bearing BPMN node.

**Acceptance Scenarios**:

1. **Given** native workflow and reusable activity-definition implementation graphs, **When** an eligible
   input is mapped, **Then** the same stored meaning and document-history behavior are available.
2. **Given** a BPMN node hosting an ordinary activity or an intrinsic/Switch consuming-value surface,
   **When** its eligible input is edited, **Then** selection uses that surface's correct document location.
3. **Given** a secret-only, provider-constrained or read-only target, **When** it is inspected,
   **Then** source choices and mutation remain within the target's declared contract and permissions.

### Edge Cases

- Optional producer absence, absent selected member and explicit null are distinct; target-requiredness,
  nullable and existing default rules remain authoritative.
- Serialized names can differ from display or declaration names, contain dots or be explicitly renamed.
- Known incompatible sources remain understandable; ranking must not turn an unexplained empty list into
  the only indication of a type mismatch. Unknown shape is distinct from known incompatibility.
- A collection is not a scalar; collection element access uses an explicit iteration or transformation.
- Recursive, dynamic, ignored, provider-specific or truncated member shapes remain honest and safe.
- Save failure, source-service failure, denied access, revocation and a late response after switching target
  preserve the authored value and provide a retry or appropriate non-editable state.
- Source-owned immutable templates, cross-resource paste and missing modules preserve unresolved references
  for diagnosis rather than coercing them into literals or silently changing their meaning.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Eligible consuming inputs MUST offer a consistent Choose data action with readable selected
  provenance and source navigation, preserving existing fixed-value and explicit-expression editing.
- **FR-002**: One source browser MUST expose authorized available activity outputs, workflow inputs,
  lexically visible variables and current-item sources exposed by the owning structure.
- **FR-003**: Authors MUST be able to choose whole values and supported nested object/dictionary members.
  Serialized member identity MUST remain separate from display labels and declaration names. This is read
  selection; variable/output write destinations do not acquire nested-write semantics.
- **FR-004**: Discovery MUST work for declared but untouched inputs and before a workflow run. It MUST NOT
  execute a producer, evaluate an expression or require diagnostic-value access to browse declared fields.
- **FR-005**: Basic whole/member selection MUST work without optional text-language/editor modules.
  Explicit transformations use installed syntax capabilities and preserve source when those are unavailable.
- **FR-006**: Offered facts MUST correspond to the authorized current draft/revision and target. Pending
  save or graph changes MUST invalidate stale responses; failed saves MUST preserve the input and allow retry.
  Browsing MUST use the existing save behavior and MUST NOT introduce hidden persistence writes or silently
  save unrelated manual edits. Equivalent unsaved editor surfaces require a non-persisting analysis path or
  an explicit save-and-load choice with the original input preserved.
- **FR-007**: Discovery, draft diagnostics, every publication/test-run path and reusable executable-producing
  paths MUST agree on source identity, declared projection, structural scope and definite availability.
- **FR-008**: Required sources MUST be guaranteed on every valid path. Conditional sources, scope crossings,
  joins and backedges MUST follow accepted explicit optional/return/collection/merge rules, never canvas order
  or a globally latest occurrence rule.
- **FR-009**: Selected member metadata MUST describe actual serialized name, type, presence, nullability,
  cardinality and applicable protection honestly. Ignored or unsupported custom shapes MUST NOT be invented.
- **FR-010**: Member selection MUST belong to the consuming input and MUST preserve the producer's declared
  output/completion contract. Failure of a required member MUST be attributed to consumer materialization.
- **FR-011**: Producer absence, member absence and explicit null MUST remain distinct through selection,
  validation and execution. Existing target conversion/default/nullability rules MUST remain authoritative.
- **FR-012**: Selected source type and shape MUST inform compatibility and conversion. Collections, maps and
  sets MUST preserve their declared semantics; basic selection MUST NOT introduce implicit fan-out or data loss.
- **FR-013**: Canonical saving, code Apply, reopen, publication and executable inspection MUST preserve source,
  literal member selection, optionality and conversion meaning. Unsupported components MUST report an
  unsupported selection before using it; silently ignoring the member and consuming the whole value is forbidden.
- **FR-014**: Statically missing/invalid direct references, known member selections and portable expression-source
  bindings MUST fail validation before executable/publication/activation writes. Deferred member-presence/null/type
  checks MUST fail before constructing/invoking the consumer activity or executing an intrinsic assignment;
  runtime causal checks remain as defense in depth.
- **FR-015**: Rename, move, copy/paste, delete, source replacement and Undo/Redo MUST preserve or explicitly
  invalidate the intended reference. Internal references in a copied pair MUST remap to the copied producer.
- **FR-016**: Invalid references MUST remain inspectable with a useful reason, source/target navigation where
  resolvable, and a working replacement/clear path. Same-label and shadowed sources MUST remain distinguishable.
- **FR-017**: Source insertion into an installed expression syntax MUST generate valid escaped access and
  required bindings atomically, handle alias collisions and preserve manual definitions and editor continuity.
- **FR-018**: Existing whole-value references, manual variables, expressions and supported conversion settings
  MUST retain their meaning. Source selection MUST use existing document save/history mechanisms.
- **FR-019**: Denied, masked, captured-run, read-only and source-owned inputs MUST preserve their authority
  and protected values. Generic selection MUST NOT bypass secret-only or provider-owned source restrictions.
- **FR-020**: Loading, empty, no-match, incompatible, unknown/truncated, denied and unavailable states MUST
  be distinguishable; later target/revision changes MUST prevent stale results from replacing current facts.
- **FR-021**: Committed selection/input meaning MUST survive durable suspension/restart and defined consumer
  retry behavior without re-executing the producer merely to read its output.
- **FR-022**: The surface and source-family inventory below MUST be reconciled against actual composition.
  Every eligible row needs functional evidence; a non-applicable row needs its contract reason, not a cosmetic pass.
- **FR-023**: Keyboard opening, browsing, selection, cancel and focus restoration MUST work. References and
  all introduced states MUST remain usable at narrow width and in the supported light/dark appearances.
- **FR-024**: Program completion MUST include current-head real persisted UI-to-runtime evidence,
  independently inspected downloadable captures, affected checks/review and resulting-main qualification.
  Baseline failed product criteria from M1 MUST remain historical failures and be explicitly retested in M3–M5.
- **FR-025**: When a member shape is unknown or truncated, authors MUST be able to retain the whole source
  or enter literal object/dictionary member segments without writing an expression. The interface MUST
  identify deferred checks; source permissions, target conversion and consumer-time missing/null rules still
  apply. Collections are selected whole or accessed through an explicit iteration/transformation.

### Finite Acceptance Inventory

| Family | Included behavior or actual contract boundary |
|---|---|
| Native Sequence/Flowchart | Root/nested consuming inputs, authoritative control-flow availability, including untouched inputs. |
| Reusable activity-definition graphs | Same consuming-input editor and document authority, public output consumed by another workflow, immutable template/provider handling. |
| BPMN activity-bearing nodes | Ordinary hosted activity inputs receive the shared capability. Model-owned events/gateways/service controls retain their own contracts; a closed published-service boundary is not an invented native data-mapping surface. |
| Intrinsic value inputs and Switch case conditions | Same consuming-value behavior at their actual document locations; output/variable write destinations remain destination selectors. |
| Scalar/object/JSON/map/collection inputs | Wrapped canonical consuming inputs; whole/nested selection preserves declared type, nullable, collection and set semantics. |
| Unwrapped repeater elements | Literal element controls retain their existing contract; whole-collection selection and explicit expressions remain available at eligible owning inputs. This does not imply an unsupported per-element binding location. |
| Secret/Connection/options/custom controls | Choices follow actual permitted sources and provider ownership; eligibility must be explained rather than bypassed. |
| Read-only/source-owned/run/code views | Authorized inspection and lossless representation; mutations only at editable document surfaces. |
| Source families | Activity results, workflow inputs, scoped variables and owner-exposed current item; known members, unknown/truncated shapes, absent/nullable/collection variants. |

### Key Entities *(include if feature involves data)*

- **Source identity**: identifies the existing workflow-input, scoped-variable, current-item or causal
  activity-result role; display labels are presentation. Canonical meanings remain in the [Elsa glossary](../../docs/glossary/elsa.md).
- **Member selection**: literal serialized member names selected by a consuming input, with authoritative
  type/presence/nullability/cardinality facts and existing source protection constraints.
- **Stored binding**: the canonical authored selection and its compiled execution meaning; neither UI tokens
  nor cached source lists become a second source of workflow state.
- **Source facts**: authorized metadata tied to a target and current document/revision; they contain no
  evaluated business values and distinguish supported empty from unavailable or unknown.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Both customer golden paths complete with the correct ID after save/reopen/code Apply,
  with zero synthetic variables/captures and zero handwritten source for ordinary mapping.
- **SC-002**: The nine production journey families in the [program acceptance matrix](../../docs/plans/activity-data-selection.md#acceptance-matrix)
  have explicit executed outcomes covering every eligible inventory row, without substituting narrower fixture evidence.
- **SC-003**: Every statically invalid required source/member case in the agreed matrix is rejected before
  publication or activation writes. Deferred member-presence/null/type failures are detected before consumer
  activity construction/invocation or intrinsic assignment, preserving the committed producer without re-executing it.
- **SC-004**: Copy/edit/history cases retain the correct source or present an actionable repair state after
  reopen, with no silent whole-value substitution, changed producer or discarded selection.
- **SC-005**: The persistent restart journey completes the consumer with its original selected value and
  exactly one producer execution; a green known-issue shortcut or absent resumed completion cannot pass.
- **SC-006**: Normal and minimal-host journeys both preserve basic selection; all restricted/source-owned
  cases retain their authorized behavior and protected values.
- **SC-007**: Independent inspection opens the exact-head desktop/narrow light/dark captures and a focused
  320px picker/reference capture, with keyboard/focus and recovery criteria passed.
- **SC-008**: Reviewed changes, applicable checks and resulting-main evidence prove all scoped milestones
  before the program is marked complete; pending, cancelled, skipped and failed evidence is never renamed passed.

## Assumptions

- The accepted product direction is the compact input-adjacent chooser. A sidebar is evaluated only if
  the composed journey demonstrates a need; it is not a separate first-release requirement.
- Accepted [ADR 0045](../../docs/adr/0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md)
  governs value roles and causal occurrences. This specification does not adopt draft ambient activity-name
  syntax or the broader proposed coercion model, and does not invent Initiative membership.
- Consumer-owned selection has completed independent contract/architecture review. M1 closed through its
  precise-failing-seam research exit; [plan readiness](plan.md#readiness-and-qualification) records the exact
  qualified source and remaining M2 merge/qualification gates. Research closure does not accept product behavior.
- Declared metadata is sufficient to start mapping. Authorized runtime examples are a later separately
  scheduled increment; general schema inference, guessed mapping, a new language/editor, blanket output
  retention, automatic iteration, releases and deployment remain outside this scope.
- Existing Studio/module contribution, save/history, authorization, conversion, durable execution and
  activity-owned structure mechanisms are reused. Draft/provisional constitutional material is not ratified here.


### Owner clarification14Nov — workflow inputs stay explicit

For new authoring/publication, a request source must identify exactly one declared workflow input by its
name. An empty declared-input list is closed; Studio must not propose undeclared request names, infer
public inputs from references, or relax runtime start validation. Declared Object/JSON inputs retain
nested literal member selection, including dots and empty names. Existing unsupported whole/member
references remain authored content with a declare-input problem and explicit Replace/Clear; inspection,
save or failed publication must not rewrite or delete them. Reusable boundary input identity and stored
legacy executable artifacts retain their existing contracts. Decision: [#4309 owner resume](https://github.com/elsa-workflows/elsa-foundation/issues/4309#issuecomment-6871062664).
