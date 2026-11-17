# Research: Background eligibility

Read in code on the initiative branch at `2c84e802f`, and in S-10's implementation branch
(`claude/3052-s10-impl-dc93e8`, PR #4316) where noted. Nothing here was run.

## R-1 Where the rules live today

- **Publication**: `ExecutableNodeCompiler.CompileNode` (`Essentials/src/Workflows/Publishing/Elsa.Workflows.Publishing/Services/ExecutableNodeCompiler.cs:110-120`)
  refuses undefined placement, intrinsics and reusable boundaries; `ValidateBackgroundExecution` (`:241-294`) refuses
  non-CLR types, structure and children, the five child handlers, `IStatefulActivity`, checkpoint participants, a
  missing contract, a missing `[BackgroundExecutionSafe]`, `object`/`Object`/`Elsa.Any` results, `Stream` and
  transient-resource results, non-persistable and sensitive or encryption-required results and projections.
- **Run time**: `BackgroundActivityEligibility.Validate(ExecutableNode)` and `Validate(IActivity)`
  (`Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/BackgroundActivityEligibility.cs:16-33`), called
  by `BackgroundActivityCoordinator.GetResultOrAdmitAsync` (`:31`) and `BackgroundActivityWorker` (`:148`, `:175`). It
  checks placement, the declaration, intrinsic/structure/children, persistable and not hidden results, and the leaf
  interfaces on the activated object. **Missing at run time**: C3 (typed result) and the `Stream`/transient-resource
  part of C4.
- **C6**: `BackgroundActivityWorker.cs:181-183` refuses a body that staged a dispatch or published stimuli, with
  `VF-BG-001` and no criterion.
- **C9**: nowhere. `RuntimeContainerScopeService.cs:425-439` leaves the variable seam empty on the worker, so reads
  return nothing.
- **C10**: partly. `RuntimeInputBindingCompiler.EnsureSecretBindingsAdmissible` (`:484`) refuses secret bindings on
  non-CLR consumers and checkpoint participants; `RuntimeActivityInputSnapshotMaterializer.cs:31-33` withholds secret
  values and resolves them at activation. No check refuses a stream or transient-resource **input** on a background
  node; whether such an input fails at materialization or on the worker is unverified (T008 drives it first).
- **Tests**: two compiler tests mention `VF-BG-001` (`WorkflowExecutableCompilerTests.cs`); worker tests cover the
  happy path and lease renewal. No test refuses each criterion at both points.

**Decision**: move every check above into one component; keep the call sites as thin calls.

## R-2 Where the component lives

**Decision**: `Elsa.Workflows.Runtime.Core`, a public static class `BackgroundEligibility` with two entry points
(publication and run time) over one ordered criterion list.

**Rationale**: ADR 0093 decision 3 names the project. It already holds `ExecutableNode`,
`IRuntimeStructuralActivity`, the child handlers, `IRuntimeActivityCheckpointParticipant` and
`IRuntimeScopedVariableReader`, and it references `Elsa.Activities.Runtime.Core` (the attribute, `IStatefulActivity`,
`ActivityContract`). Publishing and `Elsa.Activities.Runtime` both reference it, so no reference is added.

**Alternatives considered**: a service behind an interface (a replacement contract) — rejected: nothing should replace
eligibility per host, and §2.24.2 would need a reason. Keeping it in `Elsa.Activities.Runtime` — rejected: Publishing
does not reference that project and must not.

## R-3 What publication knows that run time does not

Publication has the CLR type and the authored node (`ActivityNode`, its intrinsic and reusable-boundary flags);
run time has the pinned `ExecutableNode` (contract, structure, child slots, intrinsic kind) and, on the worker, the
activated object. Both can see the pinned contract's result type alias, policies and representations, so C3, C4 and C5
can be decided from the contract alone at both points. The CLR result type (`Stream`, `object`) is the one
publication-only fact; at run time the activated object's type gives the same answer.

**Decision**: one input record (`BackgroundEligibilitySubject`) built by each caller from what it has; criteria read
only the record. C1/C2's interface checks read a CLR type, which both points have (`ClrActivityTypeResolver` at
publication; the activated object or the well-known type registry at run time).

## R-4 Built-in activity assessment

Every activity type in `Essentials/src` and `Extensions/src` was read against C1–C10 (31 leaves, 16 structural types,
9 intrinsics, 9 stateful activities). The verdicts are in the spec's *Built-in assessment* table. Findings that shape
this plan:

- **Only `AgentModelTurn` is marked today** (`Extensions/Agent/src/Elsa.Agent/Activities/AgentModelTurn.cs:10`). Its
  node and contract are built by hand with `supportsBackgroundExecution: true` and `Background` placement
  (`Extensions/Agent/src/Design/Services/AgentActivityProvider.cs:86-91`, unchanged on `main` apart from the folder
  move). Template providers compile their own nodes, and `ActivityTemplatePlacer` copies `ExecutionPlacement` without
  an eligibility check (`ActivityTemplatePlacer.cs:372, 554, 634`), so `ExecutableNodeCompiler` never checks it. The gap
  is the template path, not the agent (FR-012, Q9).
- **`BpmnProcess` is the only shipped `IRuntimeScopedVariableReader`** and it is structural, so C9 refuses no built-in
  today; it protects custom activities.
- **`WriteHttpResponse`** is delivered only from a result committed during the synchronous inline drain
  (`Essentials/src/Activities/Http/Services/HttpResponseInstructionDelivery.cs:15-16`). **Trigger activities** get
  `triggerPayload: null` on the worker (`BackgroundActivityWorker.cs:178-179`). No criterion covers either (Q8).
- **`PublishEvent`** stages through the same stager the worker inspects, so C6 refuses it after the body, by design.
- **Unknowns** (T022): the connection tenant partition on a worker (`HttpConnectionApplicator.cs:40` reads
  `IWorkflowExecutionPartitionAccessor.Current`); secret-bound inputs without `[RefusesSecretBinding]` (for example
  `RunJavaScript.Arguments`) on the worker; `DownloadFile` results against `BG-ACT-006`.
- **Issue candidates**: M365 `CreateEvent` (Graph `transactionId` from `InvocationId`), M365 `UploadFile` (conflict
  behaviour and its `byte[]` input), Slack `AddReaction` (`already_reacted` as success), `EvaluateDecision` (pin
  re-verification on a worker). Optional: an opt-in `Idempotency-Key` from `InvocationId` on `SendHttpRequest`.

## R-5 Diagnostic shape

**Decision**: keep the exception types each caller throws today (`InvalidOperationException` at publication,
`BackgroundActivityValidationException` at run time), with message `VF-BG-001 (C<n>): Background execution is not
eligible for activity node '<id>': <reason>.` The component returns a refusal value (criterion, reason); callers wrap
it. A `Criterion` property on `BackgroundActivityValidationException` carries it structured (Q2).

**Alternative**: one code per criterion — rejected (Q2).

## R-6 Causal order across providers (ADR 0093 decision 6)

The in-memory queue (`InMemoryWorkflowSchedulerWorkQueue`) is a FIFO `Queue<>` per workflow execution; the EF queue
orders by `WorkOrderKey` (`EfSchedulerWorkQueueStore.cs:191, 212`). The background result work item is created with the
admission's `occurredAt` (`BackgroundActivityCoordinator.cs:50-59`). So the EF queue may place a result before work
enqueued after admission, the in-memory queue after it. ADR 0093 decision 6 allows this.

**Decision**: no change to either queue. Evidence comes from a test pair on both providers (T024–T026). The commit
order is read from S-10's write history (`VariableWriteRecord` on `ActivityExecutionState`, PR #4316) and the
checkpoint sequence (Q7).

## R-7 What S-10 already covers

PR #4316 builds versions, read sets, the commit rule and the write history; its US5 (admission refresh, background
lost-update warning, tasks T010a, T029–T032) is deferred to a later S-10 PR. Shared files: PR #4316 edits
`ExecutableNodeCompiler.cs` (node metadata, not the background check), and S-10's US5 will edit
`BackgroundActivityCoordinator.cs` (admission). This spec changes one call in each, so whichever lands second merges
the initiative branch first; no logic overlaps.

## Implementation findings (2026-11-17)

- **T008 (stream input, driven).** A background node whose `Stream` input is bound to a variable **published** before
  this change: no check looked at inputs. The contract pins the input under its canonical CLR alias
  `System.IO.Stream`, which `ValueRepresentationDefaults.Infer` does not map to `TransientResource`, so C10 reads both
  spellings. It is now refused at publication naming C10 (`BackgroundEligibilityPublicationTests`).
- **T011.** `BpmnProcess` is still the only shipped `IRuntimeScopedVariableReader`; it fails C1 first. C9 refuses no
  built-in.
- **T022, by reading the code and one run.**
  - Tenant partition on a worker: `HttpConnectionApplicator` and `ActivitySecretInputResolver` read
    `IWorkflowExecutionPartitionAccessor.Current`, which comes from the persistence access context. The worker runs the
    body inside `scopeFactory.CreateAsync(claimScope)`, so both resolve the claim's partition. Not run against a live
    connector.
  - Secret-bound `RunJavaScript.Arguments`: publication admits it in the background (C10 lets a secret reference
    through; the snapshot keeps the withheld envelope) — `BuiltInBackgroundAssessmentTests`. Activation-time
    resolution on the worker uses the same resolver as inline.
  - `DownloadFile` above the receipt limit: `BackgroundActivityCompletionCodec` refuses a result above
    `MaximumResultBytes` (1 MiB default) with `BG-ACT-006`, a clear fault. Accepted; documented on the mark.
  - None failed, so all 15 are marked (Q10).
- **Declaration order (data-model deviation).** The declaration check runs **after** the engine-checked criteria, not
  before C1, so an unmarked activity that also fails C1–C10 is refused naming that criterion (US5 acceptance 2:
  `Sequence` → C1, `Inline` → C3). An unmarked activity that passes them all is refused as "does not declare
  BackgroundExecutionSafe" (`WriteHttpResponse`, `SendHttpRequest`, `ReadLine`, `PublishEvent`, `Timer`).
- **Template placement (FR-012).** `ActivityTemplatePlacer` resolves a template node's CLR type from its pinned
  activation capability, as the worker does, and runs `CheckPublication`. A frame root is **not** treated as a
  reusable boundary: a source-owned occurrence's template root is the authored node itself (a background leaf inside
  a graph activity). An authored node that references a reusable definition is still refused C1 by the workflow
  compiler. A publishing shell need not host the runtime type a template provider pins (AgentDesign runs without
  AgentRuntime): placement then checks every criterion the pinned contract and node shape show (C1 shape, C3–C5, C10,
  the declaration) and leaves the CLR-type criteria (C1 interfaces, C2, C9) to admission and the worker, which always
  re-check them (`BackgroundEligibilitySubject.TypeCheckedAtRunTime`). An authored node still needs its CLR type at
  publication. This is the one place FR-012's "same publication check" is narrower; review round 1.
- **Versions (T020).** Marking changes only the runtime contract fingerprint; the design catalog hash does not read the
  attribute. The eight connector reads carry `[Version]` and move 1.0.0 → 1.1.0. Primitives, Scripting and BPMN use the
  assembly version, which the per-package versioning tool moves. The activity contract surface baseline does not
  record the flag, so it is unchanged.
- **C9 signals of ADR 0090.** Neither `[AccessesUndeclaredVariables]` nor `VariableReference<T>` exists in code (checked
  on the initiative branch and S-10's branch), so C9 enforces `IRuntimeScopedVariableReader` only. It is one public
  predicate, `BackgroundEligibility.AccessesVariablesByName(Type)`, where both signals join when ADR 0090 decision 4 is
  implemented.
- **Codec checks are not eligibility.** `BackgroundActivityCompletionCodec` still refuses a non-persistable or hidden
  result policy with `BG-ACT-004` when it serializes a receipt. Eligibility refuses those nodes first (C4, C5), so the
  codec check is unreachable for them; it stays as the serializer's own invariant (it never writes a value it cannot
  store or protect). Lifting C4 or C5 (ADR 0093 *Limits*) therefore also means giving the codec a protected transport,
  which is the point of the lift.
- **Boundary contract stamping (fixed, review round 1).** `ActivityTemplatePlacer.StampBoundaryContract` rebuilt the
  contract without `SupportsBackgroundExecution`, so a background leaf placed as an occurrence root (a background
  step inside a graph activity) was pinned as not background-safe and refused at admission. It now keeps the flag,
  and the placer checks the node as pinned (occurrence bindings, stamped contract), not the template's node.
