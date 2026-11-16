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

*Filled from the code assessment; the verdicts go to the spec's *Built-in assessment* table.*

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
