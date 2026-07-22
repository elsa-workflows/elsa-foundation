# 123 — Runtime scoped-variable read seam + BPMN collection-mode multi-instance (BPMN Phase 3 warm-up, spec-121 follow-up)

## Goal

Give structural activity evaluations a **read-only, spoof-proof view of container-scoped variable
values**, and use it to make **collection-mode multi-instance executable** — the stated cut spec 121
carried (D1 rule 5). A published BPMN process can then attach
`multiInstanceLoopCharacteristics` with a **collection variable** to a host element: the bound child
runs once per item of the declared container-scoped collection variable's current value (a documented
snapshot at loop start), each instance's iteration frame seeding the item under `ItemVariable`
alongside the existing zero-based `loopIndex`.

The runtime half is a new narrow seam in the spec-117/119 read-seam family: populated by the runtime
from **committed state only**, opt-in via a marker interface, no write surface. It deliberately does
**not** wire the transitional `VariableScope` type (which remains an unwired in-memory view) — it
projects from the durable frame store (`VariableFrameState`/`ValueEnvelope`) through the exact
machinery the leaf input-binding path already uses, so there is one variable-value truth.

Everything else spec 121 built — coordinator/sub-token model, sequential/parallel progression,
`(NodeId, IterationId)` teardown, boundary interplay, error-boundary absorption, determinism — is
reused byte-identically; collection mode only changes how `N` is resolved and what the iteration
frame seeds.

## Context (what exists today, origin/main = 80c7bacac)

- **The blocker, verified.** All three scheduler work handlers pass `variableScope: null` into
  `SimpleActivityExecutionContext.ForExecution(...)`:
  `WorkflowInvokeActivitySchedulerWorkHandler.cs:246`,
  `WorkflowParentActivityCompletionSchedulerWorkHandler.cs:247`,
  `WorkflowResumeBookmarkSchedulerWorkHandler.cs:237`. There is **no** non-null threading anywhere;
  the `VariableScope? VariableScope` property exists only on the concrete context (not on
  `IRuntimeActivityExecutionContext`), and `rg "new VariableScope\(" src` finds no runtime
  constructor — the type (`src/Elsa/Expressions/Core/Models/VariableScope.cs`, ADR 0027) is an
  in-memory design-time view constructed only by unit tests. Leaf activities do not read through it
  either; they read through the frame store (below). So "thread `VariableScope` through" would mean
  wiring a second, previously-dead read view to the durable store — rejected in favor of projecting
  from the one live store.
- **The durable variable store.** `VariableFrameState`
  (`src/Elsa/Workflows/Runtime/Core/Models/VariableFrameState.cs`): `FrameId`, `ScopeId`,
  `ParentFrameId`, `Kind` (`Root`/`Container`/`Iteration`), `Status`, `ActivationId`,
  `Values: IReadOnlyDictionary<string, ValueEnvelope>` keyed by **reference key** (not name).
  `ValueEnvelope`: `Presence` (`Absent`/`ExplicitNull`/`Present`), `InlineValue` (**`JsonElement?`**),
  `ExternalReference`, `Policy`. Frames live on `ActivityExecutionState.VariableFrame` /
  `.IterationVariableFrame` and `WorkflowExecutionState.RootVariableFrame`.
- **Frame lifecycle.** `RuntimeContainerScopeService.ActivateOwnedFramesAsync` materializes a
  container's declared frame when the container starts
  (`WorkflowStartActivitySchedulerWorkHandler.cs:133`), projecting declarations via
  `RuntimeVariableDeclarationProjector` (which is how `BpmnStructure.Variables` becomes a frame —
  `BpmnStructureHandler.SupportsScopedVariables/ProjectScopedVariables`). Mid-run **writes** go
  through `WorkflowIntrinsicExecutor` (`Set`/`Merge`/`Reduce` intrinsics → `frame.Set(key, value,
  revision)` persisted back onto the owning state), so committed frame `Values` ARE the current
  values as of any evaluation's loaded basis.
- **The existing read machinery (leaf path).**
  `RuntimeContainerScopeService.BuildVisibleFramesAsync(workflowExecutionId, activityState, …)`
  walks `ParentActivityExecutionId` upward (cycle/missing-ancestor throws), collects each ancestor's
  active frames, and returns values keyed by `RuntimeVariableValueAddress(scopeId, referenceKey)`;
  leaf input bindings resolve through it (`RuntimeInputBindingResolver.ResolveVariable`, name→key
  precomputed at bind time). `ProjectVisibleVariables`/`ReadScopeVariableValues` +
  `Materialize(ValueEnvelope)` do reference-key→name mapping and envelope-unwrap. The ancestor walk
  IS the ownership guard: an activity can only ever assemble frames on its own lexical ancestor
  chain — that is what makes the new seam spoof-proof for free.
- **Seam precedents on `IRuntimeActivityExecutionContext`**
  (`src/Elsa/Workflows/Runtime/Core/Contracts/IRuntimeActivityExecutionContext.cs`): spec-117
  read-only props `TriggerNodeId`/`TriggerMetadata` (get-only, seeded from committed state); spec-119
  `GetLiveChildActivities()` (marker-gated via `IRuntimeLiveChildActivityConsumer`, empty unless
  populated, "read-only and spoof-proof — projected from committed activity-execution state");
  spec-112/115 staged-request seams (not the shape needed here — this is a pure read).
- **The two BPMN holes, marked in code** (`src/Elsa/Activities/Bpmn/Internal/BpmnExecutionEngine.cs`):
  `StartMultiInstanceLoop` resolves `var total = loopCharacteristics.Cardinality ?? throw …
  collection mode is not executable …` (**:336–337** — where `N = collection length` goes), and
  `BuildMultiInstanceIterationFrame` seeds only `loopIndex` with the literal comment `// Collection
  mode would add the current item under ItemVariable here (deferred this slice).` (**:391–402**).
  `BpmnScheduler.ScheduleChild` already carries `iterationFrame` + `iterationId`; the plumbing below
  the seam is complete. Validation rule 5 (`ValidateMultiInstance`, spec 121 D1) rejects
  collection mode as a stated cut; the importer degrades `elsa:collection`/`elsa:itemVariable` to a
  host without loop characteristics.
- **Loop-start evaluation kinds.** A token can arrive at a multi-instance host during any of the
  three evaluation kinds — process start (invoke), child-completion propagation, and bookmark resume
  — so the seam must be populated on all three handler paths.

## Design decisions

### D1 — The runtime read seam (narrow, opt-in, committed-state-backed)

- **Marker interface** `IRuntimeScopedVariableReader` (runtime Core contracts, spec-119
  `IRuntimeLiveChildActivityConsumer` pattern): an activity implementing it declares it wants
  scoped-variable read access during its structural evaluations. `BpmnProcess` implements it.
- **Interface member** on `IRuntimeActivityExecutionContext`:
  `bool TryReadScopedVariableValue(string variableName, out ValueEnvelope? envelope)`.
  - Returns `true` with the **committed** envelope when `variableName` resolves to a declared
    variable in a **visible active frame** — visibility is the activity's own lexical chain
    (own iteration frame → own container frame → ancestors' frames → workflow root frame),
    **innermost scope wins** for shadowed names (the `VariableScope.TryGetValueByName` precedent).
  - Returns `false` (envelope `null`) when the name resolves to no visible declared variable, and
    **always** when the seam was not populated (non-marker activity, or a handler path that did not
    populate it) — parallel to `GetLiveChildActivities()` returning empty. No throw-on-unpopulated.
  - Read-only: no write counterpart, no enumeration surface (the narrowest seam that unblocks the
    consumer; enumeration can be added by a future unit that needs it, e.g. expression-conditioned
    flows).
- **Population**: all three handlers (`WorkflowInvokeActivitySchedulerWorkHandler`,
  `WorkflowParentActivityCompletionSchedulerWorkHandler`,
  `WorkflowResumeBookmarkSchedulerWorkHandler`), **only** when the executable node's activity
  implements the marker, build the visible name→envelope projection from the **already-loaded
  committed states** via the existing `RuntimeContainerScopeService` machinery
  (`BuildVisibleFramesAsync` + the declaration-projection name mapping — reuse
  `ProjectVisibleVariables`-family helpers; do NOT re-implement envelope-unwrap or name→key
  resolution) and hand it to `SimpleActivityExecutionContext.ForExecution(...)`. The invoke and
  completion handlers already instantiate `RuntimeContainerScopeService`; the resume handler may
  need to (verify — mechanical).
- **Spoof-proofing**: inherited from the ancestor walk — the projection is built from the structural
  activity's own `ActivityExecutionState` chain, so out-of-chain scopes are unreachable by
  construction. The seam exposes committed values only (never staged/in-flight writes); a value
  written by an intrinsic in a concurrent branch becomes visible only once committed, on the next
  evaluation's basis. Document this read-consistency rule on the interface member.
- **`variableScope: null` stays.** The transitional `VariableScope` parameter/property and type are
  untouched (still unwired); retiring them is a separate cleanup concern, out of scope.

### D2 — BPMN collection-mode execution (rides the spec-121 machinery unchanged)

- **Validation**: `ValidateMultiInstance` rule 5 (collection-mode stated cut) is **removed**. Rules
  1–4 stay byte-identical — in particular rule 4 (collection variable must name a declared
  container-scoped variable in `BpmnStructure.Variables`) remains the authoring-time guard.
- **Loop start, collection mode** (`StartMultiInstanceLoop`): the engine reads the collection
  variable **once** via `TryReadScopedVariableValue(loopCharacteristics.CollectionVariable, …)` — a
  **documented snapshot**; later mutations of the variable do not change the loop. Semantics of the
  read result:
  - `false` (not visible/declared — unreachable post-validation for the process's own variables, but
    kept defensive): deterministic `BpmnExecutionException` fault, code-style message naming the
    element and variable (`bpmn.loop.collection-unreadable`).
  - Envelope `Presence` = `ExplicitNull` or `Absent`: **`N = 0`** — the empty-loop path spec 121
    retained (`N == 0`: complete immediately, route outbound via normal behavior) finally becomes
    reachable; it is now tested for real.
  - `Present` with `InlineValue` of `ValueKind.Array`: `N =` array length; the items (in array
    order) are the per-instance values.
  - `Present` with a non-array `InlineValue`: deterministic fault
    (`bpmn.loop.collection-not-a-collection`, naming element + variable + actual kind).
  - `Present` with `ExternalReference` payload: **stated cut** — deterministic fault
    (`bpmn.loop.collection-not-inline`). Tripwire: if the existing `Materialize`/external-reference
    machinery already resolves external payloads to a `JsonElement` on this path trivially, use it
    and drop the cut; report either way.
- **Snapshot storage**: `BpmnLoopState` gains an **additive nullable `Items`**
  (`IReadOnlyList<JsonElement>?` — `null` in cardinality mode, the snapshot in collection mode),
  written via `BpmnStateMutator` at loop start and dropped with the loop record. Required because
  sequential mode schedules instance `k+1` in a **later** evaluation (instance `k`'s completion) and
  the snapshot semantics forbid re-reading the variable; parallel mode reads the same record for
  uniformity. Additive state growth, schema stays version 1.
- **Iteration frame**: `BuildMultiInstanceIterationFrame` seeds, in collection mode,
  `Values = { "loopIndex": <index>, <ItemVariable>: <items[index]> }` — the item as a durable
  `InstanceInline` inline envelope (never transient), typed with the canonical **dynamic** value
  type descriptor (ADR 0035: canonical dynamic type is JSON-node-shaped; mirror how existing
  dynamic/JSON values pick their `ValueTypeDescriptor`). Tripwire: if no clearly-canonical dynamic
  descriptor exists for frame values, stop and report rather than inventing a new descriptor
  convention. `ItemVariable` defaults to `"item"` (spec 121 D1, unchanged); a name collision between
  `ItemVariable` and `"loopIndex"` is rejected at validation (extend rule 2's family with a
  deterministic message — cheap, prevents a silent overwrite).
- Everything downstream — coordinator token, sub-tokens, sequential/parallel progression,
  `(NodeId, IterationId)` teardown keying, boundary arming/teardown, error-boundary absorption,
  cascade cancellation, `bpmn.tokenId` completion routing — is the spec-121 machinery **unchanged**.
  Instance `k`'s child resolves `ItemVariable == items[k]` and `loopIndex == k` through the real
  variable-read evaluator, exactly like FR-3 of spec 121.

### D3 — Interchange (lift the collection-mode degradation)

- **Import**: `elsa:collection`/`elsa:itemVariable` extension attributes on
  `<multiInstanceLoopCharacteristics>` (the spec-121 D4 convention) now import as a real
  collection-mode `BpmnLoopCharacteristics` **when the named variable is declared** as a
  container-scoped variable on the process (`BpmnStructure.Variables`) — the importer must never
  emit a graph `BpmnGraph.Validate` rejects, and rule 4 still rejects undeclared names, so an
  `elsa:collection` naming no declared variable (or empty) **degrades** (host imports without loop
  characteristics + a specific finding), as does `elsa:itemVariable == "loopIndex"` (the new
  validation rule). `standardLoopCharacteristics`, `<dataInputRefs>`/completion forms, non-integer
  `loopCardinality`, unsupported hosts: degradation findings unchanged from spec 121.
- **Export**: a collection-mode element emits
  `<multiInstanceLoopCharacteristics isSequential="…" elsa:collection="…" elsa:itemVariable="…"/>`
  (`elsa:itemVariable` emitted explicitly even at default, mirroring the explicit
  `isSequential="false"` convention). Cardinality emission unchanged. Round-trip
  (import→export→import) is stable for collection mode now.

### D4 — Stated cuts (unchanged from spec 121 unless listed)

`completionCondition`; per-instance data-output aggregation; `standardLoopCharacteristics`;
multi-instance on event-defined elements; expression-conditioned flows; structural-evaluation
**writes** to scoped variables (the seam is read-only); wiring or retiring the `VariableScope` type;
seam-A teardown on `CancelLiveWork` paths (logical-only, unchanged); a variable-enumeration surface
on the context.

## In scope (this slice)

- **Runtime seam (D1)**: `IRuntimeScopedVariableReader` marker; `TryReadScopedVariableValue` on
  `IRuntimeActivityExecutionContext` + backing on `SimpleActivityExecutionContext`; population in the
  three handlers (marker-gated, committed-basis, via `RuntimeContainerScopeService`); runtime
  EXTENSION_POINTS entry.
- **BPMN engine (D2)**: `BpmnProcess : IRuntimeScopedVariableReader`; collection-mode `N`
  resolution + snapshot + faults in `StartMultiInstanceLoop`; `BpmnLoopState.Items` (additive) via
  `BpmnStateMutator`; item seeding in `BuildMultiInstanceIterationFrame`; validation rule 5 removal +
  the `ItemVariable`≠`loopIndex` rule.
- **Interchange (D3)**: collection-mode import (declared-variable guard) + export + round-trip;
  degradation findings for undeclared/reserved names.
- **Tests + module docs**: runtime handler-level seam tests (gating, visibility chain, shadowing,
  committed-write visibility, all three handler paths); BPMN collection-mode execution tests
  (sequential + parallel items observed via capture child, empty/null collection → immediate route,
  non-array fault, snapshot semantics, determinism, boundary/teardown reuse sanity); interchange
  import/round-trip/degrade; BPMN README + EXTENSION_POINTS; Interchange README; runtime
  EXTENSION_POINTS.

## Out of scope

Everything in D4; Studio authoring UX (separate repo); any change to cardinality-mode behavior,
token/loop-state identity derivation, or the spec-119/120/121/122 semantics.

## Functional requirements

**FR-1 — Seam population + gating.** The context's `TryReadScopedVariableValue` is populated during
invoke, child-completion/child-fault, and bookmark-resume evaluations **iff** the executable node's
activity implements `IRuntimeScopedVariableReader`; for every other activity all reads return
`false` and behavior is byte-identical to today (existing suites unmodified).

**FR-2 — Read semantics.** A populated read resolves by variable **name** across the activity's own
visible frame chain only (own iteration → own container → ancestors → root), innermost scope winning
for shadowed names, returning the committed `ValueEnvelope`. Values staged but not committed in the
same evaluation are not visible. Out-of-chain scopes are unreachable (spoof-proof by construction).

**FR-3 — Validation.** Collection-mode loop characteristics validate cleanly (rule 5 gone); rules
1–4 hold byte-identically; `ItemVariable == "loopIndex"` is rejected deterministically.

**FR-4 — Collection loop start.** A token arriving at a collection-mode host reads the collection
variable once (snapshot): inline array → `N = length` + `Items` snapshot on the loop record;
null/absent → `N = 0` → immediate completion + outbound route; non-array → deterministic
`bpmn.loop.collection-not-a-collection` fault; unreadable → deterministic
`bpmn.loop.collection-unreadable` fault. Record ids stay a pure function of `Sequence`.

**FR-5 — Per-instance item.** Instance `k`'s child resolves `ItemVariable == items[k]` and
`loopIndex == k` through the real variable-read evaluator, in both sequential and parallel modes;
sequential later-instance seeding reads `Items` from the loop record, never re-reading the variable.

**FR-6 — Machinery parity.** Sequential progression, parallel concurrency, last-instance outbound
routing, `(NodeId, IterationId)` teardown, boundary arming/teardown, error-boundary absorption, and
cascade cancellation behave identically to cardinality mode (spec-121 FR-4..FR-9) with a collection
in place of a cardinality.

**FR-7 — Snapshot discipline.** Mutating the collection variable after loop start changes neither
`N` nor the items delivered to remaining instances (documented; tested where the harness permits a
mid-loop committed write).

**FR-8 — Determinism.** Identical runs produce identical token/loop-record/iteration ids and
identical instance scheduling order; the `Items` snapshot round-trips deterministically through
state persistence.

**FR-9 — Interchange.** Collection-mode `multiInstanceLoopCharacteristics` with a declared variable
round-trips import→export→import with `isSequential`/`elsa:collection`/`elsa:itemVariable` fidelity;
undeclared/empty `elsa:collection` and reserved `elsa:itemVariable` degrade with specific findings;
the importer never emits loop characteristics the validator rejects.

**FR-10 — Continuation discipline.** Unchanged: seam-A/seam-B staging only at clean
`Complete`/`Defer` exits; a terminal continuation never co-exists with staged child schedules; a
`Fault`/`Cancel` continuation never co-exists with staged seam requests. The new faults are ordinary
deterministic engine faults.

## Invariants that MUST survive

- `Elsa.Bpmn.ExecutionState` stays schema version 1 (`BpmnLoopState.Items` is additive);
  `BpmnStateMutator` remains the sole mutation home; all record ids derive from `Sequence`;
  `Canceled` tokens are never pruned.
- The new seam is **read-only, opt-in (marker-gated), spoof-proof, committed-state-backed**; it adds
  no write surface, no enumeration surface, and no behavior change for non-marker activities. The
  `VariableScope` type and the `variableScope: null` threading are untouched.
- One variable-value truth: the seam projects through the existing `RuntimeContainerScopeService`
  machinery — no re-implemented envelope-unwrap or name→key resolution anywhere in BPMN.
- Behaviors stay decision-only and multi-instance-unaware; the loop lifecycle stays engine-owned.
- Spec 119/120/121/122 suites pass unmodified. Acyclicity/cycle handling (spec 122) unchanged.
- Deterministic ids only; no wall-clock identity. No new HTTP endpoints; domain project-tree naming
  guard and VF-ACT gates hold.

## Success criteria

- Runtime: seam tests pin marker gating (non-marker → always `false`), all three handler paths,
  innermost-wins shadowing, root/container/iteration visibility, committed-write visibility on a
  subsequent evaluation, and out-of-chain unreachability.
- BPMN: sequential collection of 3 items — items observed in order (`items[0..2]` via a capture
  child) one instance at a time; parallel collection of 3 — 3 concurrent same-node instances each
  observing its item; empty + null collection → immediate outbound route; non-array → deterministic
  fault; determinism (identical runs → identical ids); an interrupting-boundary-mid-loop sanity run
  in collection mode (machinery parity).
- Interchange: collection-mode round-trip; undeclared-variable + reserved-item-variable degrades
  with findings.
- Validation: rule-5 removal + the new reserved-name rule covered in `BpmnGraphValidationTests`.
- Full test projects green: BPMN, BPMN Interchange, Activities Runtime, Workflows Runtime,
  ControlFlow, Architecture. Full solution build clean.

## Deviations from the ratified plan

- **`BuildVisibleFramesAsync` gained an `includeOwnContainer` flag (own-container inclusion).** The leaf
  input-binding path reads only the *ancestor* chain (a leaf owns no container frame), but a structural
  scoped-variable reader must also see its **own** container frame as the innermost visible scope (the spec's
  stated chain: own iteration → own container → ancestors → root). `BuildVisibleFramesAsync` therefore gained
  an `includeOwnContainer` parameter (defaulted `false`, so the two existing callers are byte-identical);
  `ProjectScopedVariablesForReaderAsync` passes it `true`. This is a projection extension, not a new seam or a
  re-implemented name→key resolution (tripwire 4: the innermost-wins name mapping is derived from the existing
  `ProjectDeclarations`/`ProjectVisibleVariables` machinery via a shared `EnumerateVisibleVariables` helper).
- **Resume-path seam population is defensive, not independently reachable for `BpmnProcess`.** The seam is
  populated on all three handlers via the one shared `RuntimeContainerScopeService.ProjectScopedVariablesForReaderAsync`
  helper (unit-tested for marker gating, shadowing, visibility, and committed-write). A `BpmnProcess` never
  suspends/resumes itself (only its children do), so a collection loop-start read fires end-to-end on the
  **invoke** path (loop start at process start) and the **child-completion** path (loop start reached after a
  preceding element completes) — both covered by dedicated BPMN execution tests. The resume-handler
  population is exercised through the shared helper's unit tests rather than a resume-specific end-to-end BPMN
  run, because no natural BPMN scenario resumes `BpmnProcess` structurally.
- **Interchange variable representation (`elsa:variable`).** Spec D3 requires collection-mode import/round-trip
  to key on the collection variable being declared in `BpmnStructure.Variables`, but the interchange
  importer/exporter modeled no process variables at all. A minimal elsa-namespaced representation was
  introduced — `<process><extensionElements><elsa:variable name="…"/></extensionElements>` — read into
  `BpmnAuthoredStructure.Variables` as name-only `VariableDefinition`s (type defaulted to `Object`) and emitted
  by the exporter. Interchange needs only name-level fidelity for the declared-variable guard and the
  round-trip (it never executes); runtime execution uses the publish-compiler-lowered
  `RuntimeVariableDeclaration`, not this interchange model.
- **Tripwire outcomes.** (1) Item `ValueTypeDescriptor`: resolved to the canonical dynamic alias `Elsa.Any`
  (ADR 0035), mirroring `ForEach`'s generic-item envelope — no stop-and-report. (2)
  `Present`+`ExternalReference` collection payloads: the existing `Materialize` returns the reference object,
  not a resolved `JsonElement`, so resolving needs new machinery — the `bpmn.loop.collection-not-inline`
  deterministic fault (stated cut) is kept. (3) Resume handler: instantiating `RuntimeContainerScopeService`
  was mechanical (same construction as the invoke/completion handlers). (5) No spec-vs-code contradiction was
  found.
