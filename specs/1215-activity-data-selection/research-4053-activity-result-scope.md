# Research: the scope in an activity-result binding (#4053)

**Status:** Design note for review. No product code changes. Baseline `aa85848627` (main, 10 Nov 2026).
**Issue:** [#4053](https://github.com/elsa-workflows/elsa-foundation/issues/4053), part of [#3882](https://github.com/elsa-workflows/elsa-foundation/issues/3882); blocks [#3094](https://github.com/elsa-workflows/elsa-foundation/issues/3094).
**Method:** reading code, then real-engine runs for the run-time claims. The runs are
`Essentials/tests/Activities/Behavioral/Tests/ActivityResultOccurrenceTests.cs` (`OccurrenceTests` below);
each pins current behaviour on main. A claim marked **(run)** names its test; one still marked
**(read, not run)** is listed in [section 8](#8-not-established).

Path prefixes used below:

| Prefix | Path |
|---|---|
| `RT/` | `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/` |
| `RTC/` | `Essentials/src/Workflows/Runtime/Core/` |
| `ART/` | `Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/` |
| `FC/` | `Essentials/src/Activities/Flowchart/` |
| `PUB/` | `Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/` |
| `VAL/` | `Essentials/src/Workflows/Design/Validations/Elsa.Workflows.Design.Validations/` |
| `DC/` | `Essentials/src/Workflows/Design/Core/` |

## Summary

- The declared `producerScopeId` is compared with a run-time value that only Flowchart and reusable
  boundaries ever set. Everywhere else the producer has no scope and any non-empty string passes.
- The only value an author can know in advance is Flowchart's root constant `scope:root`. Loop, race and
  reusable-boundary scopes are generated at run time.
- I found no workflow where the declared scope selects the right occurrence and dropping it selects a wrong
  one. In a Flowchart loop it selects the first pass's stale result, where no scope fails as Ambiguous.
- A scope-free read is not safe by itself either: in a looping Flowchart nested in a loop it also reads the
  stale first-pass result, silently. The refusal in the recommendation is therefore required, not optional.
- Recommendation: new direct bindings carry no scope (design B), and the owning structure supplies one new
  static fact, "this producer can run again before the consumer", so that such reads are not offered and
  are refused at publish. Existing documents with a scope are left exactly as they are.
- One acceptance line of #4053 needed the owner's reading: a producer *inside* a Flowchart loop cannot be
  made to resolve correctly by any choice about scope. See [section 6](#6-recommendation). The owner accepted
  that limit for this leaf on #4053.

## 1. What `producerScopeId` is compared with

The resolver reads the producer's scope from `Provenance.ExecutionScopeId`, falling back to the
`flowchart.executionScopeId` metadata (`RT/Services/Values/CausalActivityResultResolver.cs:167-175`). A
declared scope only excludes a producer when both sides are non-null and differ (`:150-152`).

A child's scope is decided in two places:

- `ART/Services/SchedulerWorkItems.cs:222-228`: the scope a parent hands its children is the parent's own
  activity execution id when the parent is a reusable boundary, otherwise the parent's own scope.
- `ART/Services/SchedulerWorkItems.cs:135-151`: that inherited scope wins over whatever the scheduling
  structure put in its provenance.
- `RT/Services/WorkHandlers/WorkflowScheduleActivitySchedulerWorkHandler.cs:217-228`: a node whose executable
  metadata has `activity.definitionVersionId` opens its own scope, equal to its activity execution id. Only
  a placed reusable boundary carries that key (`PUB/Services/ActivityTemplatePlacer.cs:334-337`); ordinary
  catalog activities are not placed (`PUB/Services/WorkflowExecutableCompiler.cs:185-191`).

| Where the producer runs | Scope written | Evidence | Known when authoring? |
|---|---|---|---|
| Workflow root | none | `RT/Services/WorkHandlers/WorkflowStartSchedulerWorkHandler.cs:265,288` | Yes: there is none |
| Child of Sequence, If, Switch | inherited from parent (none at workflow level) | no provenance passed: `Essentials/src/Activities/Sequence/Activities/Sequence.cs:38-45,73-81`, `ControlFlow/If/Activities/If.cs:55`, `ControlFlow/Switch/Activities/Switch.cs:81` | Yes, as "same as parent" |
| Child of While, Do, For, ForEach | inherited; `executionScopeId: null` | `ControlFlow/While/Activities/While.cs:130`, `Do.cs:145`, `For.cs:155`, `ForEach.cs:156` | Yes, as "same as parent" |
| Branch of Parallel | inherited; `executionScopeId: null`, branch id set | `ControlFlow/Parallel/Activities/Parallel.cs:182-190` | Yes, as "same as parent" |
| Compensation step or handler | inherited; `executionScopeId: null` | `Essentials/src/Activities/Compensation/Activities/CompensationScope.cs:942-951,1187` | Yes, as "same as parent" |
| BPMN activity node | inherited; `executionScopeId: null` | `Essentials/src/Activities/Bpmn/Elsa.Activities.Bpmn/Internal/BpmnProcessRuntime.cs:292-294` | Yes, as "same as parent" |
| Flowchart node, root scope | the constant `scope:root` | `FC/Internal/FlowchartStatePersister.cs:27-31`, `FC/Internal/FlowchartScheduler.cs:28-51` | Yes, but it is a private engine constant |
| Flowchart node after a loop-back | `scope:{state.Sequence + 1}`, one per iteration; the loop exit stays in it | `FC/Internal/FlowchartScopeResolver.cs:103-122`, `FC/Internal/FlowchartStateMutator.cs:16-17`, `FC/Internal/FlowchartExecutionEngine.cs:170` | No |
| Flowchart node under a first-wins fork | `scope:{state.Sequence + 1}`; returns to the parent scope afterwards | `FC/Internal/FlowchartPolicyApplier.cs:37-40,204-212`, `FC/Internal/FlowchartScopeResolver.cs:54-61` | No |
| Anything below a Flowchart node | the Flowchart-local scope of that node, inherited downwards | `ART/Services/SchedulerWorkItems.cs:227` | Only for the root scope |
| A placed reusable boundary | its own activity execution id | `RT/.../WorkflowScheduleActivitySchedulerWorkHandler.cs:217-228` | No |
| Node inside a reusable body | the boundary's activity execution id | `Essentials/src/Activities/Graph/Runtime/Activities/GraphActivity.cs:70-77`, `ART/Services/SchedulerWorkItems.cs:225-226` | No, and not compared (below) |

BPMN, compensation and the loops all go through the same resolver: there is one direct call site
(`RT/Resolvers/RuntimeInputBindingResolver.cs:85`).

Inside a closed reusable body the declared scope is already ignored. When the consumer has a nearest closed
boundary, the resolver passes `null` as the declared scope and instead requires producer and consumer to
share that boundary (`RT/Services/Values/CausalActivityResultResolver.cs:73-78`). Spec 265 records why: no
declared scope can match there (`specs/265-graph-activity-scope-reads/research.md:199-210`).

## 2. What the scope comparison protects

After the scope check the resolver still requires, for every candidate: membership of the consumer's causal
lineage (`CausalActivityResultResolver.cs:69,75`), the same nearest closed boundary (`:77`), a committed
completion (`:79`), and branch and iteration frames where a producer frame is either absent or equal to the
consumer's (`:154-159`). More than one remaining candidate fails as Ambiguous; the resolver never picks
(`:84-90`).

So dropping the declared scope can change an outcome in two ways only:

1. Unavailable becomes resolved, when the scope was the only thing excluding the single causal candidate.
2. Resolved becomes Ambiguous, when the scope was the only thing separating two causal candidates.

It cannot turn a resolved occurrence into a different resolved occurrence.

**The one real case of (2) is a Flowchart loop, and there the scope picks the wrong occurrence.**

```text
Flowchart, start A:   A -> B -> C,   C -(again)-> A,   C -(done)-> Z
C.input  <-  ActivityResult { producerNodeId: B, projectionKey: $result, producerScopeId: "scope:root" }
```

- Static analysis admits it. B dominates C, so the read is `Definite` and is not a cyclic back-edge
  (`FC/Internal/FlowchartChildResultAnalysis.cs:16-36`); the analyzer test's `flowchart-cycle` row pins a
  `Definite` source for the two-node version (`Essentials/tests/Workflows/Design/Tests/ActivityDataSelectionAnalyzerTests.cs:20-41`).
- First pass: A1, B1, C1 run in `scope:root` with no iteration id (`FlowchartStatePersister.cs:27-31`).
- `C -> A` is a backward edge, so the second pass runs in a new scope `scope:n` with iteration key `A:1`
  (`FC/Internal/FlowchartPolicyApplier.cs:121-129`, `FlowchartScopeResolver.cs:112-122`).
- Each node is scheduled by the child that just completed (`FlowchartExecutionEngine.cs:121,180-194`), so C2's
  lineage is `B2, A2, C1, B1, A1` and the Flowchart itself.
- Candidates for B: B1 (`scope:root`, no iteration) and B2 (`scope:n`, `A:1`). B1 passes the frame check
  because a producer without an iteration frame is visible to every consumer (`CausalActivityResultResolver.cs:158-159`).
- With the declared `scope:root`: B2 is excluded, B1 is returned. **C2 silently reads the first pass's
  result** and the run completes. (run: `A_loop_reader_that_declares_the_root_scope_reads_the_first_pass_result_on_the_second_pass`)
- With no declared scope: B1 and B2 both remain, so the read fails as **Ambiguous** with exactly those two
  candidates. (run: `A_loop_reader_without_a_scope_cannot_tell_the_two_passes_apart_on_the_second_pass`)

Neither outcome is right. The scope turns a loud failure into a silent stale value.

A failed read is not a workflow fault. The reader's start work item is poisoned: the reader stays
`Scheduled`, one blocking incident is raised on it, and the run stays `Running`, waiting for intervention.
For a read through an expression parameter the incident says only that a result resolution failed; the
reason is redacted. (run: every `OccurrenceTests` case that asserts a blocked reader)

**I could not construct a workflow where the declared scope yields the right occurrence and its absence
does not.** The argument:

- Outside Flowchart and reusable boundaries the producer has no scope, so the check never runs.
- In a Flowchart the only authorable value is `scope:root`. It keeps root-scope occurrences and drops
  loop-iteration and race occurrences. A node runs twice in one lineage only through a loop-back, and the
  root-scope occurrence is then always the oldest one.
- Inside a closed reusable body the check is already skipped.

The declared scope also makes valid reads fail:

- A producer under a first-wins fork runs in a generated scope (`scope:3` in the run), so a reader that
  declares `scope:root` gets Unavailable; the same read without a scope resolves. (run:
  `A_result_produced_under_a_first_wins_fork_is_unavailable_to_a_reader_that_declares_the_root_scope`,
  `..._resolves_for_a_reader_without_a_scope`)
- A node that carries placement's reusable-activity identity has its own execution id as its scope
  (`WorkflowScheduleActivitySchedulerWorkHandler.cs:227-228`). A sibling that declares any scope gets
  Unavailable; without a scope it resolves. (run:
  `The_result_of_a_placed_reusable_activity_is_unavailable_to_a_sibling_that_declares_a_scope`,
  `..._resolves_for_a_sibling_without_a_scope`)
- The Elsa 3 importer writes the enclosing container's node id (section 4). For a producer in a Flowchart
  that is not `scope:root`, so the read should be Unavailable for the same reason as the first-wins case.
  (read, not run: importer output was not executed)

Two controls behave as section 1 predicts. In a Sequence any declared scope resolves, because the producer
has none (run: `A_sequence_reader_resolves_its_prior_sibling_whatever_scope_it_declares`). A producer that
runs once before a looping Flowchart resolves on every pass, with or without a scope (run:
`A_loop_reader_resolves_a_producer_that_ran_once_before_the_flowchart_on_every_pass`).

**A scope-free read can be silently stale too.** Loops cannot nest inside one Flowchart: a loop-back target
may have no other inbound connection (`FC/Internal/FlowchartReachabilityAnalyzer.cs:17-27`), so a loop head is
always an entry. They nest as a looping Flowchart placed in an outer loop:

```text
Outer, start A:   A -> B -> Inner -> G,   G -(again)-> A,   G -(done)-> Z
Inner, start D:   D -> E,   E -(again)-> D          E reads B
```

- Inner's nodes inherit the outer scope but carry only Inner's own iteration key, and none on Inner's first
  pass.
- On the second outer pass B2 has the outer key `A:1`. E's frame is either absent or `D:1`, so B2 never
  matches it; B1 has no frame and always does.
- E reads B1 on all four of its runs, directly with `scope:root` and through a parameter alike, and the run
  completes. (run: `A_reader_in_a_looping_flowchart_nested_in_a_loop_always_reads_the_outer_first_pass_result`)

So "no scope" fails loudly only in the single-level loop. Both loop cases are wrong for the same reason:
the producer runs again inside one activation of the structure that owns it.

## 3. How portable parameters resolve without a scope

`ActivityResultExpressionParameterBinding` has no scope field (`Essentials/src/Expressions/Core/Models/ExpressionDefinition.cs:156-158`).
Its resolver overload passes `producerScopeId: null` (`CausalActivityResultResolver.cs:39-52`), called from
`RT/Services/Values/RuntimePortableExpressionEvaluator.cs:208-210,423`. It relies on:

- **Run time:** lineage, same closed boundary, committed completion, branch and iteration frames, and
  unique-or-fail, exactly as in section 2.
- **Publication:** the shared analyzer checks every direct and portable read with the same rules
  (`VAL/Internal/ActivityDataReadBinding.cs:23-45`): producer present and unambiguous, no crossing out of a
  structured scope without an explicit return, no cyclic back-edge, definitely available unless optional,
  projection declared (`VAL/Services/ActivityDataSelectionAnalyzer.cs:255-290`). It runs on draft validation
  (`VAL/Validators/ValueFlowValidator.cs:37`) and before executable and template writes
  (`PUB/Services/WorkflowExecutableCompiler.cs:245`, `PUB/Services/ActivityTemplateCompiler.cs:546`).

Publication validates nothing about a direct binding's scope: the analyzer only echoes it
(`ActivityDataSelectionAnalyzer.cs:294-295`). So today the direct and portable forms have identical static
guarantees, and the direct form adds one unvalidated run-time comparison. The portable form has the same
loop weakness as section 2: Ambiguous from the second pass of a single loop, and silently stale in the
nested case (both run).

## 4. Who writes `producerScopeId` today

| Author | Value | Evidence |
|---|---|---|
| Code-first builder | the enclosing builder scope id (`root` for the workflow sequence) | `DC/Authoring/WorkflowBuilder.cs:94,99,110,369` |
| Elsa 3 importer | the enclosing container's node id | `Extensions/Elsa3/src/Mapping/Services/Elsa3ValueFlowLowerer.cs:263`, `Elsa3MemoryReferenceGraph.cs:86-88`; wrapper sequence id at `Elsa3ReusableActivityImportMaterializer.cs:238,253` |
| Compensation restart test (#3089) | the literal `scope` | `Essentials/tests/Workbench/Tests/WorkbenchCompensationRecoveryTests.cs:530` |
| Compensation guide | the scope node id `order` | `website/docs/essentials/compensation.md:90` |
| Rules e2e, Sequence | `sequence-root`, `budget-root`, `refusal-root` | `e2e-tests/rules/Test-DurableRules.ps1:338,499,533`, `Test-RulesAdmissionRecovery.ps1:313` |
| Rules e2e, Flowchart | `scope:root` | `e2e-tests/rules/Test-DurableRules.ps1:362,400` |
| BPMN publication test | `process` | `Essentials/tests/Activities/Bpmn/Tests/BpmnDataSelectionPublicationTests.cs:92` |
| Runtime unit tests | `scope:root` even in a Sequence; `authored-node-inner` | `Essentials/tests/Activities/Sequence/Tests/SequenceResultVisibilityTests.cs:88`, `Essentials/tests/Activities/Runtime/Tests/CrossSlotSchedulingExecutionTests.cs:82` |
| Reusable body tests | the producer's own node id, remapped on placement | `Essentials/tests/Workflows/Publishing/Api/Tests/ActivityTemplatePlacementTests.cs:480-481`, `PUB/Services/PlacedReferenceRemapper.cs:86-97` |
| Studio | never | no occurrence under `Studio/src` |

Authors use four different conventions. All of them work only because the producer has no run-time scope;
the single case where the value matters (Flowchart) needs the engine constant.

## 5. Candidate designs

### A. The owning structure supplies the scope; the analyzer returns the binding with it

**Contracts.**
- `DC/Models/ActivityChildResultFacts.cs`: new optional fact, the producer child's execution scope as one of
  "none", a literal, or "not known before run time". Supplied by `IActivityStructureHandler.AssessChildResult`
  (`DC/Contracts/IActivityStructureHandler.cs:59-65`). Design Core tracks no public API file.
- `FC/Internal/FlowchartStructureHandler.cs`: returns the literal `scope:root` only when neither node is on
  a cycle or below a first-wins fork; otherwise "not known".
- `VAL/Services/ActivityDataSelectionAnalyzer.cs`: composes the fact along the ancestor chain, because a
  scope set by a Flowchart is inherited by everything below its nodes. It returns the binding with that
  value and, on validation, refuses a supplied scope that differs from a literal fact.
- Wire, compiler, runtime model and hashes: unchanged.

**Existing documents.** Bytes and hashes unchanged. Behaviour changes for documents that have a wrong scope
in a Flowchart: they now fail at publish instead of at run time. Where the fact is "none", any authored
value must still be accepted (`scope`, `order`, `sequence-root`, `root`), so a wrong value there remains
expressible; it is harmless because it is never compared.

**ADR 0045 and spec 1215.** No ADR change. The binding contract gains a rule that `producerScopeId` is
server-supplied, and the authoring contract a "scope not known" reason.

**Loops and reusable bodies.** A producer inside a loop or race has no literal, so no binding can be
returned and no authored binding can ever work. A reusable boundary read by a sibling has a run-time id, so
the same. A producer before the loop read from inside it gets `scope:root` and resolves. Inside a closed
body any value works, since the scope is not compared.

**The guard test.** `Automatic_result_metadata_never_invents_a_runtime_scope_constraint` splits: Sequence and
top-level Flowchart rows now expect a binding; `flowchart-cycle` and `flowchart-in-for` keep `binding: null`.

**Failure modes.** `scope:root` becomes a document-level contract of a private engine constant: changing
Flowchart's scope ids later breaks stored documents. The section 2 stale read stays reachable for existing
documents and is not detected. The fact for a Sequence child depends on its ancestors, so the composition
logic lives in the analyzer, not in the structure that owns the scope.

**Tests.** Handler fact tests per structure; analyzer tests for composed scopes; publisher refusal of a wrong
Flowchart scope before writes; a real Flowchart run per row.

### B. New direct bindings carry no scope

**Contracts.**
- Wire: `producerScopeId` becomes optional on authored `ActivityResult`. The picker never writes it. A
  document that has one keeps it.
- `PUB/Services/RuntimeInputBindingCompiler.cs:649`: read the scope as optional.
- `RTC/Models/RuntimeInputBinding.cs:197-218`: `ProducerScopeId` becomes `string?`; keep the existing
  constructor and add an overload without the scope. This is a tracked public API change
  (`RTC/PublicAPI.Unshipped.txt:3486,3488`).
- `RT/Services/Values/CausalActivityResultResolver.cs`: no change. A null declared scope already skips the
  comparison (`:150`), which is the path portable parameters and closed bodies use today.
- `VAL/Internal/ActivityDataReadBinding.cs:123-124`: accept a direct binding without a scope.
- `VAL/Services/ActivityDataSelectionAnalyzer.cs:117-133,294-297`: return the scope-free binding for a browsed
  producer when the same resolution that publication runs yields no finding; otherwise `binding: null` with
  the existing reason code. `RuntimeScopeBindingUnavailable` is retired.
- `PUB/Services/PlacedReferenceRemapper.cs:89`: guard the null scope (a dictionary lookup with a null key
  throws).

**Existing documents.** A binding with a scope compiles to the same reference and the same hash line
(`RT/Services/Executables/WorkflowExecutableHasher.cs:136-137`) and resolves as before. A scope-free binding
hashes as `result::{node}:{projection}:{optional}`, which no existing artifact can produce because a scope
was never empty (`RTC/Models/RuntimeInputBinding.cs:207`). Template behaviour hashes serialize the reference
(`Essentials/src/Workflows/Publishing/Core/Services/ExecutableActivityTemplateBehaviorHasher.cs:21-24`), so
they change only for new bindings.

**Older servers.** An older compiler refuses a scope-free binding (`RuntimeInputBindingCompiler.cs:649`) and
an older validator reports `InvalidSource` (`ActivityDataReadBinding.cs:124`), both before any write.

**ADR 0045 and spec 1215.** No ADR change: the ADR requires resolution "relative to the consumer's
structural frame and causal control-flow lineage" and never names a producer scope
(`docs/adr/0045-workflow-value-flow-uses-role-owned-bindings-and-immutable-invocation-records.md:85-87`). The
binding contract's `ActivityResult` and `ActivityResultMember` rows mark `producerScopeId` optional and
legacy; the authoring contract states that the server returns the complete binding.

**Loops and reusable bodies.** Producer before the loop, race-scope producers and reusable boundaries read
by a sibling all resolve, because the lineage holds one candidate (run, section 2). Reusable bodies are
unchanged. A producer on a loop fails as Ambiguous from the second pass, and in the nested case reads the
stale first-pass result without failing (run, section 2). B alone is therefore not safe to ship.

**The guard test.** Its intent survives and gets stricter: the server still invents no scope, and now returns
a binding that has none. The focused-query half (a supplied scope is echoed, never rewritten) stays.

**Failure modes.** New bindings lose the run-time scope comparison, so publication is their only structural
guard beyond lineage and frames; section 2 argues that the comparison guards nothing an author can state. A
loop read still publishes, and then blocks or reads a stale value at run time, unless it is refused
statically (hybrid below). An executable
with a scope-free reference loaded by an older runtime: see section 8.

**Tests.** Reference and hash controls for both shapes; compiler and validator acceptance; publisher
no-write refusals; real runs for Sequence, top-level Flowchart, loop consumer, race producer, reusable
boundary and reusable body; unchanged compensation and legacy-scope regressions.

### Hybrid: B, plus one structure-owned fact that refuses repeating producers

B alone leaves the loop read publishable and broken. The missing guarantee is static and belongs to the
structure: whether the producer can run again within one activation of the structure that owns it. The
fact is assessed where the producer lives, whatever the consumer's depth: in the nested example B is on
the outer cycle, so E's read is refused although E sits in another Flowchart.

- `DC/Models/ActivityChildResultFacts.cs`: add `ProducerMayRepeat` (`bool?`, as an init property so the
  positional record keeps its shape). `FlowchartChildResultAnalysis.Assess` sets it from the reachability it
  already computes: the producer can reach itself (`FC/Internal/FlowchartChildResultAnalysis.cs:25,38-58`).
  The `ChildrenCompletedBefore` adapter sets `false` for a declared prior child
  (`DC/Models/ActivityChildResultFacts.cs:10-15`), which covers Sequence and compensation without touching
  `CompensationScopeStructureHandler`.
- Analyzer: a scope-free direct binding is admitted only when the fact is `false`. A repeating or unknown
  producer is listed with `binding: null` and a reason code, and a stored scope-free binding to it gets a
  finding, so publication refuses it before any write.
- Bindings that carry a scope, and portable parameters, keep today's rules.

## 6. Recommendation

**The hybrid.** New bindings have no scope; the Flowchart handler reports repeating producers; the analyzer
offers and admits a direct binding only when the producer is definitely available and does not repeat.

Correctness:

- Every offered binding resolves to the single causal occurrence, in every structure, with nothing for the
  client to derive and nothing wrong to express.
- It removes the ways a valid read fails today (race producer, reusable boundary; imported Flowchart by
  reading) and refuses both stale loop reads the runs found, single-level and nested.
- Where the runtime cannot identify the right occurrence, the read is refused at authoring and at publish
  instead of at run time.

Architectural fit:

- The structure supplies a control-flow fact it already computes; no private engine id enters a contract.
- One public API change, additive, in a package that already tracks it. No new package, wire discriminant,
  store or resolver rule.

**Strongest argument against.** It does not let an author read "the page fetched in this pass" from a
producer inside a Flowchart loop, which is the most common loop pattern and exactly the variable ceremony
#3094 wants to remove. #4053's first acceptance line names "a Flowchart loop scope". Under this
recommendation that line holds for a consumer in a loop scope reading a producer that ran before the
Flowchart (run, section 2; nothing can precede a loop head inside the same Flowchart); a producer on the
loop is refused, not resolved. Making it resolve needs an occurrence rule for loop passes: the first pass
has no iteration frame, and a nested Flowchart's nodes do not carry the outer iteration key. That is a
runtime semantics change against ADR 0045's "cyclic back-edges carry data through explicit state transfer
rather than a latest-output lookup" (`:160`) and should be its own decision. The owner accepted the limit
for this leaf. A second, smaller objection: new bindings rely on publication alone for
structural admission, as portable parameters already do.

**Why not A.** It cannot return a binding for loop, race or reusable-boundary producers at all, it freezes
`scope:root` into stored documents, and it leaves "any string" valid wherever the producer has no scope.

## 7. Governance and task breakdown

No ADR amendment. Update `contracts/binding-contract.md` (section 1 table and the sentence on picker
output), `contracts/authoring-contract.md` (server returns the complete binding; the repeating-producer
reason), `tasks.md`, and the Validations and Design API `EXTENSION_POINTS.md` entries that describe
`RuntimeScopeBindingUnavailable`.

One PR, in this order:

1. **Characterise first.** Done: `OccurrenceTests`, 14 cases. Every claim that was run held; the nested
   case added a finding (section 2). The tests marked "known defect" change with steps 4 and 5.
2. **Runtime model.** Nullable `ProducerScopeId`, constructor overload, public API file, hash controls for
   both shapes. No resolver change.
3. **Publishing.** Optional scope in `RuntimeInputBindingCompiler`; null guard in `PlacedReferenceRemapper`;
   template and executable hash controls.
4. **Structure fact.** `ProducerMayRepeat` on `ActivityChildResultFacts`; Flowchart and the
   completed-before adapter supply it; handler tests.
5. **Analysis.** Scope-free direct binding in `ActivityDataReadBinding`; analyzer returns it for admissible
   browsed producers and refuses repeating ones; rewrite the guard test; endpoint tests for both resource
   kinds; publisher no-write refusal.
6. **Regressions.** Compensation own/prior forward-result visibility, the #3089 restart test and the rules
   e2e stay green unmodified.
7. **Contracts.** The spec and catalog updates above.

## 8. Not established

- That Elsa 3 imports with a result read inside a Flowchart fail at run time. Importer tests do not execute;
  the claim rests on the importer's value (section 4) and the first-wins run.
- A Graph Activity boundary as the producer. The run uses a leaf that carries the metadata placement writes
  on a boundary (`activity.definitionVersionId`), which is the only thing that opens the scope; a real
  placed graph with an output mapping was not run.
- Publication. The runs execute hand-built executables, as the shared harness does, so the static
  admission described in section 3 was read, not run, for these shapes.
- A producer that first runs after a loop-back, as opposed to under a first-wins fork. It needs a node that
  is skipped on the first pass; the generated-scope mechanism is the one the first-wins run exercises.
- How an older runtime loads an executable whose reference has no scope. The constructor throws on a null
  scope (`RTC/Models/RuntimeInputBinding.cs:207`); I did not read the artifact deserializer to confirm that
  this is the path taken and that it fails before any activity runs.
- Whether a null `ProducerScopeId` is written as `null` or omitted in the stored artifact and inspection
  views (`Essentials/src/Workflows/Runtime/Api/Models/WorkflowExecutableInspectionViews.cs:133`).
- Whether every structure with a custom `AssessChildResult` outside this repository should be treated as
  "may repeat" when it leaves the new fact unset. The recommendation treats unset as not admissible.
