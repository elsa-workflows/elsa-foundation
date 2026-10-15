# Feature Specification: Elsa 3 activity map — convert Elsa 3 activity types, versions and members to Elsa 4

**Feature Branch**: `236-elsa3-activity-map`

**Created**: 2026-10-13

**Status**: Draft

**Issue**: [#2706](https://github.com/elsa-workflows/elsa-foundation/issues/2706)

**Tracking**: ADO 47607. Prerequisite of spec 235 (Elsa3JsonReconciliation, draft PR
[#2708](https://github.com/elsa-workflows/elsa-foundation/pull/2708), issue #2707). Followed by
[#2705](https://github.com/elsa-workflows/elsa-foundation/issues/2705) (Flowchart connections), which builds on this map.

**Input**: User description: "Elsa 3 converter: map Elsa 3 activity types and members to Elsa 4, and lower value flow
on the collection path." The issue is input; the decisions below come from a review session with the maintainer on
2026-10-13.

## Context (verified against origin/main 6768753c7, 2026-10-13; line numbers drift)

- **Catalog keys.** The Elsa 4 activity catalog keys a CLR activity by its full CLR type name
  (`ClrAssemblyScanner`, `ActivityTypeKey: type.FullName`), for example
  `Elsa.Activities.Primitives.Activities.WriteLine`. `IActivityDefinitionLookup.GetDefinition` matches a definition id
  or that key, ordinally, and throws `EntityNotFoundException` when neither matches. **No Elsa 3 type name
  (`Elsa.WriteLine`, `Elsa.Delay`, …) matches any catalog key.** The existing Elsa 3 tests pass only because their
  lookup stubs answer any key or register `Elsa.*` keys.
- **Catalog versions.** A CLR activity's version is its `[Version]` attribute, otherwise its assembly's informational
  version (`1.0.0+<sha>` in a dev build, the package version in a release), otherwise `Major.Minor.Build`
  (`ActivityTypeVersionResolver`). No in-repo activity has a `[Version]` attribute. The converter matches the Elsa 3
  integer version as the literal string `"{n}.0.0"` (`Elsa3ActivityToState.GetVersion`), which matches no real catalog
  version.
- **Two lookup sites.** The same type-and-version lookup is duplicated in `Elsa3MemoryReferenceGraph.GetVersionAsync`
  (runs first, during value-flow lowering) and `Elsa3ActivityToState.GetVersion`. Both throw `ArgumentException` or let
  `EntityNotFoundException` escape.
- **Intrinsics.** SetVariable, SetOutput, Correlate, SetName and Finish/Complete became engine intrinsics in Elsa 4.
  They are built-in authoring descriptors, not activity catalog rows, so `GetDefinition` does not find them; a placed
  node uses the stable version id directly (`elsa.intrinsic.set@1`,
  `…set-output@1`, `…set-correlation-id@1`, `…set-instance-name@1`, `…finish@1`, `IntrinsicAuthoringDescriptorProvider`).
- **Members (current behaviour, which this spec replaces).** Today an Elsa 3 property that names no declared Elsa 4
  input or output is skipped without a diagnostic (`Elsa3ActivityToState.ExtractInputsAndOutputs`), and member
  renames are not translated. Child activities that Elsa 3 stores in port properties (for example `then`, `else`,
  `body`) are objects without an `expression`, so they are skipped the same way: only children in `activities` are
  carried over. Silent loss of members or whole branches is the defect this spec removes (FR-002, FR-010).
- **Parity data.** `docs/reports/elsa-4-activity-contract-parity-2026-07.md` lists the member renames (§2.8), the
  activities with no Elsa 4 counterpart (§3) and the removed flow activities (§4), by bare class name only. The
  machine-readable surfaces in `docs/reports/evidence/activity-contract-parity/` list 67 Elsa 3 activity classes
  (modules Workflows.Core, Workflows.Runtime, Http, Scheduling, Expressions.JavaScript) with every input and output,
  and 28 Elsa 4 activity classes.
- **Two conversion paths.**
  - Single definition: `Elsa3WorkflowDefinitionImporter.ImportAsync` clones the definition, collects the memory
    reference graph, runs `Elsa3ValueFlowLowerer`, then maps. Any exception becomes one
    `ELS3-MIGRATION-DEFINITION-MAPPING-FAILED` diagnostic whose message is the exception text.
  - Collection: `ReusableActivityCollectionAnalyzer` (catalog-free) builds a plan; at apply,
    `Elsa3ReusableActivityImportMaterializer` calls the state mapper directly with the plan's reusable-reference
    replacements. **It skips memory-reference collection and value-flow lowering**, and a missing type or version
    escapes as an exception rather than a diagnostic.
- **Other throws on input.** Flowchart `Connections` (`NotSupportedException`, owned by #2705), nesting deeper than
  `Elsa3ImportedActivityStructure.MaxNestingDepth`, and two properties binding one input (`ArgumentException`).
- **Diagnostic codes.** Existing families: `ELS3-MIGRATION-*` (`Elsa3MigrationDiagnosticCodes`), `ELS3-REUSABLE-*`
  (`ReusableActivityImportDiagnosticCodes`), `VF-IMP-001…009` (`Elsa3ImportDiagnosticCodes`).
- **Spec 235.** Converted items get version `N.R.0`, where R is one converter revision constant owned by the
  converter; a golden-output test fails when output changes while R is unchanged. Spec 235 skips and reports an
  unconvertible item by its stable diagnostic code (its FR-018/FR-019) and plans to move the analyzer and the
  materializer's building helpers into a shared package. It depends on no API shape from this spec.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Built-in Elsa 3 activities convert to their Elsa 4 counterparts (Priority: P1)

A team migrating to Elsa 4 converts an Elsa 3 workflow that uses ordinary built-in activities (WriteLine, Delay,
ForEach, If, SendHttpRequest, RunJavaScript, …). Each node becomes the Elsa 4 activity that replaces it, at the version
installed in the Elsa 4 catalog, with every Elsa 3 property carried to the Elsa 4 member that replaced it.

**Why this priority**: Without it no real Elsa 3 workflow converts at all; every lookup fails today.

**Independent Test**: Convert an Elsa 3 definition whose root contains one node of each mapped built-in activity
against a catalog populated by CLR reconciliation of the real Elsa 4 activity assemblies; every node resolves, and
each renamed member arrives under its Elsa 4 name.

**Acceptance Scenarios**:

1. **Given** an Elsa 3 node of type `Elsa.WriteLine`, **When** it is converted, **Then** the node references the
   catalog version of `Elsa.Activities.Primitives.Activities.WriteLine` and its `text` binding is kept.
2. **Given** a `Delay` node binding `TimeSpan`, a `Cron` node binding `CronExpression`, a `ForEach` node binding
   `Items`, a `WriteHttpResponse` node binding `Content` and `ResponseHeaders`, a `DispatchWorkflow` node binding
   `Input` and a `RunJavaScript` node capturing `Result`, **When** they are converted, **Then** the bindings arrive as
   `Duration`, `Expression`, `Collection`, `Body` and `Headers`, `Inputs` and `Value`.
3. **Given** the catalog holds versions `1.0.0` and `1.1.0+abc` of a mapped activity, **When** a node of that type is
   converted, **Then** it references `1.1.0+abc` (highest by SemVer, build metadata ignored for ordering), whatever
   integer version the Elsa 3 node carries.
4. **Given** a `For` node with no `OuterBoundInclusive` value, **When** it is converted, **Then** the Elsa 4 node
   binds `EndInclusive` to `true`, so the loop keeps Elsa 3's inclusive upper bound.
5. **Given** a `SetVariable`, `SetOutput`, `Correlate`, `SetName` or `Finish` node, **When** it is converted, **Then**
   the node uses the matching intrinsic version id and its values land in the intrinsic's inputs.
6. **Given** an `HttpEndpoint` node, **When** it is converted, **Then** the Elsa 4 node binds `ResponseMode` to `Sync`,
   so a later `WriteHttpResponse` answers the request as it did in Elsa 3.
7. **Given** an `If` node with `then` and `else` port activities, **When** it is converted, **Then** both arrive in
   the `If.Then` and `If.Else` child slots.
8. **Given** a `FlowDecision` or `FlowSwitch` node, **When** it is converted, **Then** it becomes an `If` or
   `Switch` node with its condition or cases carried over. (Its connections are #2705's.)

---

### User Story 2 - Unconvertible input yields a stable diagnostic, never an exception (Priority: P1)

A migrator, or spec 235's start-time reconciliation, converts a definition that cannot be converted: an activity with
no Elsa 4 counterpart, a type nobody mapped, a member the map does not account for. The converter returns a result
with diagnostics that carry a stable code, the node, the property where relevant, and guidance. Nothing throws, so the
caller can skip the item and report it.

**Why this priority**: Spec 235 skips and reports by code; an exception aborts a whole start. Equal to P1 because
P1 alone would turn today's generic failure into many unexplained ones.

**Independent Test**: Feed one definition per unconvertible cause; each returns a failed result with exactly the
expected code and location, and no exception escapes.

**Acceptance Scenarios**:

1. **Given** a `StartAt`, `StateMachine`, `ParallelForEach`, `RunTask`, `BulkDispatchWorkflows`, `DownloadHttpFile`
   or `WriteFileHttpResponse` node, **When** it is converted, **Then** the result fails with the "no Elsa 4
   counterpart" code naming the node and the Elsa 3 type.
2. **Given** a node whose type is neither mapped, nor claimed by an alias, nor an Elsa 4 catalog key, **When** it is
   converted, **Then** the result fails with the "unmapped activity type" code.
3. **Given** a node with a property that is neither a mapped member nor a declared Elsa 4 member, **When** it is
   converted, **Then** the result fails with the "unknown member" code naming node and property.
4. **Given** a `SendHttpRequest` node whose `ParsedContent` output is captured, **When** it is converted, **Then**
   the result fails with the "member semantics changed" code, because Elsa 4's `ResponseBody` is a raw string.
5. **Given** a `FlowFork` or `FlowJoin` node, **When** it is converted, **Then** the result fails with the "graph conversion pending" code, which #2705 can later replace with a graph rewrite.
6. **Given** a mapped type whose Elsa 4 key has no version in the catalog, **When** it is converted, **Then** the
   result fails with the "activity version unavailable" code naming the Elsa 4 key.
7. **Given** a definition hitting several causes, **When** it is converted, **Then** every cause is reported, in a
   deterministic order.
8. **Given** an `HttpEndpoint` node with `allowedMimeTypes` absent or empty and the expose toggles `false`, **When** it
   is converted, **Then** those members are removed without a diagnostic (R3); **Given** `exposeFileTooLargeOutcome`
   is `true`, **Then** the result fails with "removed input bound to a non-default value".
9. **Given** a `Switch` whose `mode` is a JavaScript expression, **When** it is converted, **Then** the result fails
   with "unsupported mode".
10. **Given** an `HttpEndpoint` node whose `parsedContent` is captured, **When** it is converted, **Then** it converts
    and carries one Warning that Elsa 4 does not parse form posts.

---

### User Story 3 - Rebuilt custom activities are found through an alias on the Elsa 4 activity (Priority: P2)

A team had custom Elsa 3 activities (for example `Acme.SendInvoice`). Elsa 4 does not run Elsa 3 activity code, so the
team rebuilds each one as an Elsa 4 CLR activity (`Acme.Activities.SendInvoice`) and marks it with the Elsa 3 type
name it replaces. After CLR reconciliation, converting an Elsa 3 workflow that uses `Acme.SendInvoice` resolves to the
rebuilt activity. When the team stops converting Elsa 3 workflows, they delete the mark.

**Why this priority**: Real Elsa 3 estates use custom activities, but built-ins come first.

**Independent Test**: Reconcile a test assembly with one activity marked as replacing `Acme.SendInvoice`; convert a
node of that Elsa 3 type and check it resolves to the rebuilt activity; remove the mark in a new activity version,
reconcile, and the node reports "unmapped activity type".

**Acceptance Scenarios**:

1. **Given** a reconciled Elsa 4 activity marked as replacing `Acme.SendInvoice`, **When** a node of type
   `Acme.SendInvoice` is converted, **Then** it resolves to that activity's highest catalog version and its
   properties bind to same-named members (case-insensitively).
2. **Given** two reconciled activities both marked as replacing `Acme.SendInvoice`, **When** such a node is converted,
   **Then** the result fails with the "ambiguous activity alias" code naming both.
3. **Given** an Elsa 3 type that is already a full Elsa 4 catalog key, **When** it is converted, **Then** it resolves
   to that key without any mark.

---

### User Story 4 - The collection import lowers value flow like the single-definition import (Priority: P2)

A migrator imports a collection of Elsa 3 workflows that includes reusable workflows. Memory references (variables,
activity results, loop values) in every selected definition are lowered to Elsa 4 bindings exactly as in a
single-definition import, including on nodes rewritten to reusable activities, and lowering errors stop the apply with
diagnostics instead of persisting broken bindings.

**Why this priority**: The collection path is what spec 235 builds on; today it persists unlowered Elsa 3 memory
references.

**Independent Test**: Apply a collection whose items use each value-flow case the single-definition tests cover
(literal, variable, result, loop, JavaScript, Liquid); the stored bindings equal what single-definition import
produces for the same definition.

**Acceptance Scenarios**:

1. **Given** a selected definition that reads a variable and a prior activity's result, **When** the collection is
   applied, **Then** both bindings are lowered to Elsa 4 variable/result bindings.
2. **Given** a node rewritten to a reusable activity that consumes another node's result, **When** the collection is
   applied, **Then** lowering uses the reusable activity's declared inputs and outputs and does not look the Elsa 3
   definition id up in the catalog.
3. **Given** a selected definition with a dangling memory reference, **When** the collection is applied, **Then**
   apply fails with `VF-IMP-001` for that item and persists nothing.
4. **Given** a collection item, **When** it is materialized, **Then** the caller's collection is not modified.
5. **Given** a reusable workflow whose body sets its declared output `CustomOutput` with `SetOutput`, **When** the
   collection is applied, **Then** the graph manifest maps `CustomOutput` to a synthesized variable that the converted
   `SetOutput` node writes, and the wrapper workflow exposes `CustomOutput`.

---

### Edge Cases

- An Elsa 3 node with no `version` converts like any other; the Elsa 3 integer version never selects the Elsa 4
  version.
- A catalog version string that is not valid SemVer is ignored when picking the highest; if none is valid the result
  is "activity version unavailable".
- A property whose name differs only by case from a mapped Elsa 3 member is treated as that member (Elsa 3 JSON is
  camelCase).
- A member is removed without a diagnostic only where the Member decisions section lists it by name.
- A child activity in a port property (`then`, `else`, `body`, …) is carried into the matching Elsa 4 child slot or
  makes the item unconvertible; it is never skipped.
- An alias that names a type the built-in map also covers: the built-in map wins; the alias is not consulted.
- `ExecuteWorkflow` converts to `DispatchWorkflow` with `WaitForCompletion` set to `true` (parity §2.6) and a Warning
  that it becomes a durable dispatch; a captured `Result` or an expression-valued target makes it unconvertible.
- Engine-internal Elsa 3 classes (`Workflow`, `CompositeWithResult`, `NotFoundActivity`, `Start`, `End`,
  `DynamicActivity`, `Inline`) appearing as nodes follow member-map.md; `NotFoundActivity` (Elsa 3's marker for an
  activity it could not resolve) is always unconvertible.
- A removed input (R3) bound by an expression is unconvertible even if the expression would evaluate to the default:
  the converter does not evaluate expressions.
- Elsa 3 JSON keys are camelCase while most Elsa 4 input keys are PascalCase and are matched ordinally at publication;
  the converter emits the exact Elsa 4 key.
- Every Flowchart node, with or without connections, reports "graph conversion pending" until #2705: a native Elsa 4
  Flowchart structure needs connections and a start node, which are #2705's. Its children are still converted per
  node (FR-006), so #2705 adds only the graph. Excessive nesting and two properties binding one input also return
  diagnostics instead of throwing.
- Cancellation still throws `OperationCanceledException`.

## Requirements *(mandatory)*

### Functional Requirements

**Map**

- **FR-001**: The converter MUST own one Elsa 3 → Elsa 4 activity map. Each entry is keyed by Elsa 3 type name and
  gives one target: an Elsa 4 catalog activity type key, an intrinsic version id, a "deferred to graph conversion"
  marker (FlowFork, FlowJoin), or a "no Elsa 4 counterpart" marker with its reason.
- **FR-002**: The map MUST implement the decision record [member-map.md](member-map.md) exactly: every Elsa 3 input,
  output, port, serialized property and outcome of every activity listed there is classified by one of its rules
  (R1–R9, W). There is no "dropped" class: a member is removed without a diagnostic only under R3/R4 conditions or when
  listed under R9. A test MUST fail when any member of any Elsa 3 activity in member-map.md (the parity evidence plus
  the members and activities the evidence misses, such as `SetOutput` and plain properties) is unclassified, or any
  activity has no entry.
- **FR-003**: The map MUST cover every activity in member-map.md, including the parity report's §2.8 renames, the §3
  activities as "no Elsa 4 counterpart", FlowDecision → If and FlowSwitch → Switch, the intrinsics (SetVariable,
  SetOutput, Correlate, SetName, Finish, and Complete/End where equivalent), FlowSendHttpRequest → SendHttpRequest and
  ExecuteWorkflow → DispatchWorkflow.
- **FR-004**: The converter MUST apply each rule's outcome: R2 emits the Elsa 3 effective value explicitly (for example
  `For.EndInclusive = true`, `HttpEndpoint.ResponseMode = Sync`, `Delay.Duration = 00:01:00`); R3, R4 and R5 convert
  only when their condition holds and otherwise report the item unconvertible; R7 generates structure deterministically
  (Parallel branch names, `VariableReference` scopes, a synthesized Switch `Value` that returns the label of the first
  true case); references to loop `CurrentValue` lower to the Elsa 4 scoped variables. A captured
  `SendHttpRequest.ParsedContent` is "member semantics changed".
- **FR-004a**: Switch and FlowSwitch convert only in MatchFirst mode given as a literal or absent; `MatchAny`, or a mode
  set by an expression, is unconvertible. In a plain (non-reusable) workflow, End and Complete convert to the finish
  intrinsic only when the node is in the workflow's root flowchart and Complete has at most one literal outcome;
  otherwise they are unconvertible. Reusable workflows follow FR-004e.
- **FR-004e**: In a reusable workflow, `SetOutput` naming one of the workflow's declared outputs MUST become a write to
  a synthesized graph variable, and the graph manifest's `outputMappings` MUST map each declared output to its
  variable. The direct-start wrapper MUST carry the graph activity's outputs into its workflow outputs
  (member-map.md, "SetOutput in reusable workflows"). `Complete` and `End` inside a reusable workflow MUST NOT become
  the finish intrinsic; their outcome MUST reach the graph activity's boundary outcome. Where Elsa 4 cannot express
  that (several outcomes, or an outcome set by an expression; settled in the plan, D-2), the item is unconvertible
  with `ELS3-ACTIVITY-UNSUPPORTED-MODE`.
- **FR-004b**: For each node that uses a member or activity marked **W** in member-map.md, conversion MUST emit one
  Warning diagnostic per documented runtime difference, with a stable code naming the difference. Warnings do not make
  the item unconvertible.
- **FR-004c**: A required Elsa 4 input that the converted node would leave unbound (member-map.md, last section) makes
  the item unconvertible.
- **FR-004d**: The map MUST carry, per activity, the Elsa 3 → Elsa 4 outcome renames (for example `Done` → `True` and
  `False` on If, `Failed to connect` → `Failed`) for #2705 to apply to connections (R8).
- **FR-005**: The map MUST be a fixed, reviewable artefact in the converter, with no ordering or environment
  dependence. Any change to it changes converter output, so it is subject to spec 235's converter revision rule (R
  bump plus golden-output re-record). This spec does not introduce R.
- **FR-006**: The map MUST be usable per node, independently of the workflow-level importer, so that a Flowchart
  importer (#2705) can map Flowchart child nodes and replace the "graph conversion pending" outcome for FlowFork
  and FlowJoin. A node converted on its own MUST be lowered or refused exactly as it would be within whole-definition
  conversion (plan D-18).

**Resolution**

- **FR-007**: A node's Elsa 4 activity MUST be resolved in this order: built-in map entry; else an Elsa 4 catalog
  activity that declares it replaces the node's Elsa 3 type (FR-009); else the Elsa 3 type used verbatim as a catalog
  key; else the "unmapped activity type" diagnostic.
- **FR-008**: For a catalog target, the converter MUST pick the highest valid-SemVer version of that type key in the
  catalog (build metadata ignored for ordering). The Elsa 3 node's integer version MUST NOT select the Elsa 4 version.
- **FR-009**: An Elsa 4 CLR activity MUST be able to declare, by an attribute on its class, one or more Elsa 3 type
  names it replaces. CLR reconciliation MUST record the declaration in the catalog so the converter can find it
  without loading assemblies. Because the declaration is part of the activity version's content, adding or removing
  it requires a new activity version (otherwise reconciliation refuses the changed content, by design); the converter
  reads the declaration from each definition's highest version only, so removing it in a new version ends the alias
  for new conversions.
  Two activities claiming one Elsa 3 type MUST yield "ambiguous activity alias".
- **FR-010**: For an aliased or verbatim-key activity, Elsa 3 properties MUST bind to Elsa 4 members by name,
  case-insensitively; a leftover property is "unknown member".
- **FR-011**: Value-flow lowering and node mapping MUST use the same resolution for a given node, so they cannot
  disagree about its target.

**Diagnostics**

- **FR-012**: No conversion failure caused by input or catalog content MUST escape either path as an exception. Each
  cause MUST produce an Error diagnostic with a stable code, the definition id and version, the node id, the property
  where relevant, the Elsa 3 type, and guidance.
- **FR-013**: New codes MUST be added for at least: unmapped activity type, ambiguous activity alias, no Elsa 4
  counterpart, activity version unavailable, unknown member, member semantics changed, value outside the Elsa 4
  contract (R5), captured output not representable (R4), removed input bound to a non-default value (R3), required
  input unbound, unsupported mode (Switch, End/Complete), graph conversion pending (Flowchart, FlowFork, FlowJoin, Start),
  nesting too deep, duplicate input binding, and one Warning code per runtime difference (W). Existing `VF-IMP-*` and
  `ELS3-REUSABLE-*` codes MUST be kept where they already apply. Codes MUST follow the existing `ELS3-<AREA>-<REASON>`
  form.
- **FR-014**: Diagnostics for one definition MUST be complete (all causes, not just the first) and deterministically
  ordered.
- **FR-015**: The converter MUST expose one public operation that converts one Elsa 3 definition into an Elsa 4
  workflow state plus diagnostics, taking the reusable-reference replacements as an optional input. Both the
  single-definition import and the collection materializer MUST use it; spec 235 and #2705 build on it.

**Collection path**

- **FR-016**: The collection materializer MUST run memory-reference collection and value-flow lowering on a private
  clone of each selected definition before mapping, through FR-015.
- **FR-017**: During lowering, a node the plan rewrites to a reusable activity MUST take its inputs and outputs from
  the replacement and MUST NOT be looked up in the catalog.
- **FR-018**: If any selected item yields an Error diagnostic, apply MUST fail with the existing
  `ReusableActivityImportValidationException` carrying all items' diagnostics, and persist nothing.
- **FR-019**: `ReusableActivityCollectionAnalyzer` and the materializer's building helpers (`BuildActivity`,
  `BuildWorkflow`, `BuildWrapperState`, `BuildGraphManifest`) MUST NOT be restructured; spec 235 moves them.

**Determinism**

- **FR-020**: For the same input and the same catalog content, conversion MUST produce byte-identical output and
  identical diagnostics.

### Key Entities

- **Activity map entry**: Elsa 3 type name → target (catalog type key, intrinsic version id, deferred, or no
  counterpart with reason) plus the classification of each Elsa 3 member.
- **Member rule**: Elsa 3 member → one of the rules R1–R9 or W in member-map.md, with its Elsa 4 member, slot or outcome where one exists.
- **Elsa 3 alias declaration**: on an Elsa 4 CLR activity, the Elsa 3 type names it replaces; recorded in the catalog
  at CLR reconciliation.
- **Conversion result**: Elsa 4 workflow state or nothing, plus ordered diagnostics.
- **Conversion diagnostic**: the existing `Elsa3MigrationDiagnostic` (severity, code, message, path, guidance,
  metadata).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-006**: A reusable workflow that sets a declared output with `SetOutput`, used as an activity by another
  workflow, gives the calling workflow that output value after conversion, and leaves the calling workflow's own
  outputs unchanged. Within this spec it is proven by provider validation of the converted reusable workflow.
  Compilation needs the activity published, so the compile and run-time checks are proven once #2796 publishes
  imported reusables.

- **SC-001**: Every activity and member in member-map.md is classified by the map; the coverage test reports 0
  unclassified.
- **SC-002**: A definition using one node of each convertible built-in activity, with every member bound in a
  convertible way, converts with 0 Error diagnostics (Warnings only where member-map.md marks W) against a
  catalog built by CLR reconciliation of the real Elsa 4 activity assemblies (not a stub that answers any key).
- **SC-003**: For each unconvertible cause in FR-013, a test input produces exactly that code, and 0 exceptions escape
  either conversion path across the whole Elsa 3 test suite.
- **SC-004**: For every value-flow case covered by single-definition import tests, collection apply stores identical
  bindings.
- **SC-005**: Converting the same input twice against the same catalog yields byte-identical output.

## Assumptions

- Scope is the five Elsa 3 modules in the parity evidence. Activities of other Elsa 3 modules (Email, Sql, MassTransit,
  …) are not in the built-in map; they convert only through an alias (US3) or report "unmapped activity type".
- Member-level facts come from elsa-core `5b0c2d73` (the parity report's baseline) and the current Elsa 4 source; see
  member-map.md. `SetOutput` lives in Elsa 3's `Elsa.Workflows.Management` module and is in scope.
- Behavioural differences that only show at run time are converted with a Warning (W) rather than refused; spec 235's
  strict option can turn them into failures.
- FR-009 needs a small change outside the extension: the CLR scanner (`Elsa.Activities.Design.Reconciliation.Clr`)
  records the alias, for example as a design facet, which needs no schema change. The plan decides whether the
  attribute is Elsa 3 specific or a generic "replaces type" alias, and where it lives.
- Value-flow lowering itself (the `VF-IMP-*` rules) is unchanged apart from using the shared resolution and the
  loop-variable rule.
- The single-definition import's refusal of reusable definitions (`ELS3-REUSABLE-SELECTION-INVALID`) is unchanged.

## Dependencies and notes for spec 235

- **Converter output depends on catalog content, not only on input and R.** FR-008 (highest installed version) and
  FR-009 (aliases) mean that upgrading Elsa 4 activity packages, or adding or removing an alias, changes a converted
  workflow's content without a change to R. Spec 235's "same input and R → same rows" and its handling of
  `ActivityVersionHashMismatchException` must account for that. This spec records the dependency; spec 235 resolves
  it.
- **What the JTest suite needs beyond this spec.** Of the 36 JTest definitions, 34 have Flowchart connections (#2705),
  and most use Elsa 3 JavaScript host functions the rewriter refuses (#2717). Some also use activities with no Elsa 4
  counterpart (ParallelForEach, BulkDispatchWorkflows) or a Switch mode set by an expression; those stay unconvertible
  by design and are reported.
- #2705 replaces the "graph conversion pending" outcome and uses FR-006 and
  FR-015.

- **Engine gap #2750: resolved.** Planning found that Elsa 4 could not carry a `Complete`/`End` outcome from inside a
  reusable workflow to the graph activity's boundary outcome (FR-004e). Implementation was held for
  [#2750](https://github.com/elsa-workflows/elsa-foundation/issues/2750) (maintainer decision, 2026-10-14). Spec 237
  (PR #2791, merged 2026-10-15) added the `Complete` activity, which this spec emits (plan D-1 to D-10).
- **Imported reusables are not yet published.** The collection import writes catalog rows without a publication, so
  imported reusable activities do not run as graphs until
  [#2796](https://github.com/elsa-workflows/elsa-foundation/issues/2796) (research R-10). This spec verifies its
  reusable output by provider validation. Compilation and SC-006's run-time check are met when #2796 lands.

## Out of Scope

- Flowchart connections, ports and outcomes, and FlowFork/FlowJoin graph rewrites (#2705).
- Converter revision R, the golden-output test and the `N.R.0` version scheme (spec 235).
- Moving the analyzer or materializer helpers into a shared package (spec 235).
- Converting activities with no Elsa 4 counterpart, or adding Elsa 4 counterparts for them (#1118).
- Lowering Elsa 3 JavaScript host functions beyond `getVariable`/`getInput`/`getOutput` (#2717).
- Carrying Elsa 3 designer positions into the Elsa 4 layout sibling.
- Migrating Elsa 3 workflow instances (spec 014 boundary).
