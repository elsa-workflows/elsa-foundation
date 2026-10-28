# Background Execution Eligibility Is One Set Of Criteria, Declared By The Activity And Enforced In One Place

Status: proposed (2026-10-28). Joey Barten (@j03y-nxxbz) drafted it from analysis A-2 on epic
[#3052](https://github.com/elsa-workflows/elsa-foundation/issues/3052). Acceptance needs agreement with Sipke Schoorstra
(@sfmskywalker) and Frans van Ek (@FransVanEk).

Part of epic #3052. It records the eligibility rules of spec 241
([native durable background activities](../../specs/241-native-durable-offload/spec.md), #2914) as an architecture
decision, adds three criteria, and requires the rules to live in one place. It depends on
[ADR 0092](0092-variable-reads-and-writes-share-one-versioned-lifecycle.md) (proposed) for how a background step's
reads and writes are protected.

## Context

### How background steps work today

Spec 241 (approved) built background execution. In short:

1. **Who turns it on.** Three parties must agree:
   - the **host** enables the `BackgroundActivities` feature (otherwise admission fails with BG-ACT-001);
   - the **activity author** marks the activity type `[BackgroundExecutionSafe]`, which is pinned into its contract as
     `SupportsBackgroundExecution`;
   - the **workflow author** sets `ExecutionPlacement = Background` on the node. The default is `Inline`.
2. **Inputs are read on the coordinator.** The step's inputs are materialized and committed in its `StartActivity` work
   item, exactly as for an inline step.
3. **Admission.** The `InvokeActivity` work item does not run the body. It commits an admission (the work, with the
   input snapshot) atomically with the workflow state, and that commit is always flushed immediately. The drain
   continues with other work; the workflow is not suspended.
4. **The worker.** A worker claims the work with a lease, activates the activity from the input snapshot only, and runs
   its body. The body has no access to variables and may not dispatch workflows or publish stimuli. The worker records
   the result and enqueues a result work item.
5. **The result.** The coordinator validates the receipt and applies the result like an inline completion: output
   captures, outcome and the next steps, in one commit, again flushed immediately.
6. **Failure.** If a worker is lost, its lease expires and another worker runs the body again, so a body runs **at
   least once**; its result is applied once. A cancelled activity or a finished workflow cancels its background work
   in the same commit. A fault becomes an incident like an inline fault.

### What is wrong with the rules today

- **They live in two places that differ.** Publish checks them in `ExecutableNodeCompiler` (`:109-120` and `:209-262`);
  run time checks them again in `BackgroundActivityEligibility.Validate` (`:16-33`). The two lists are written
  separately and have drifted: publish refuses untyped `object`/`Any` results and stream or transient-resource results,
  and the run-time check does not (it checks only that results are persistable and not hidden). A new criterion has to
  be added twice.
- **Some requirements are stated only in spec 241.** Its contract says the body can run again after a lease is lost
  and must use the invocation identity for idempotency, that parallel workers get no permission to write shared
  variables, and (FR-008) that cancellation is cooperative. None of this is on `[BackgroundExecutionSafe]`, where an
  activity author makes the promise, and none of it is a marking criterion.
- **No shipped activity is marked.** Only test fixtures carry `[BackgroundExecutionSafe]`. Which built-in activities
  qualify has never been decided. No Studio control for placement was found either.
- **Variable access is not enforced.** The body sees no variables because the worker has no variable scope. A leaf
  that is `[BackgroundExecutionSafe]` and also reads variables by name (`IRuntimeScopedVariableReader`) passes both
  checks, and on the worker its reads silently return nothing (`RuntimeContainerScopeService.cs:425-439`). ADR 0090
  (proposed) would add a variable session, and A-3 asks whether scripts may write variables.

## Decision

### 1. Eligibility is a set of criteria, not a list of activities

Architects decide the criteria. Activity authors declare that their activity type meets them with
`[BackgroundExecutionSafe]`. Which activities are background-safe follows from applying the criteria; nobody maintains
a list.

*Why.* A list has to be updated for every new activity and module. Criteria are decided once and applied everywhere.

### 2. The criteria

An activity type may be marked `[BackgroundExecutionSafe]`, and a node placed in the background, only when all of these
hold. The **Check** column says who guarantees it:
- **E**: the engine enforces it at publish and again at run time;
- **R**: the engine can only check it at run time, after the body ran;
- **D**: the activity author declares it with the attribute, because no code can prove it.

| # | Criterion | Check | Today |
|---|---|---|---|
| C1 | **A leaf.** A CLR activity that is not an intrinsic, not a reusable activity boundary, has no children, and is not a structural activity or a child completion, fault, outcome or notification handler. | E | enforced |
| C2 | **Completes in one go.** It does not suspend (`IStatefulActivity`) and does not take part in checkpoints. | E | enforced |
| C3 | **Typed result.** It has a pinned, typed result contract; the result is not `object` or `Any`. | E | publish only |
| C4 | **Durable result.** No stream, transient-resource or non-persistable result or projection. | E | publish checks all three; run time checks persistability only |
| C5 | **No hidden result.** No sensitive or encryption-required result or projection. | E | enforced |
| C6 | **No coordinator effects from the body.** The body does not dispatch workflows or publish stimuli; those belong to the coordinator. | R | enforced at run time |
| C7 | **Tolerates running more than once.** After a worker is lost the body runs again. Its external effects are idempotent, or keyed on the stable invocation identity (`InvocationId`), as D-2 requires of every external effect. | D | in spec 241, not on the attribute |
| C8 | **Cancels cooperatively.** The body observes its cancellation token and stops. A body that ignores it cannot change the outcome (spec 241 FR-008), but it keeps a worker busy. | D | in spec 241, not on the attribute |
| C9 | **Variables only through inputs and results.** The body does not read or write workflow variables. Variables reach it through its inputs, read on the coordinator; its result reaches variables through output captures, applied on the coordinator. Both are protected by the lost-update rule of ADR 0092. Enforced by refusing a background placement for an activity that reads variables by name (`IRuntimeScopedVariableReader`), and later for one with ADR 0090's `[AccessesUndeclaredVariables]` or a writable `VariableReference<T>` input. | E | **new**: true by construction, not enforced |

C9 holds until A-3 and ADR 0090 decide otherwise. If they give background steps a variable session, that session
reads and writes through the ADR 0092 mechanism, and C9 changes in this ADR, not elsewhere.

### 3. The enforced criteria live in one place

One eligibility component defines the enforced criteria (C1–C5 and C9 for publication and the run-time re-check, C6
for run time only), and both publication and the worker call it. A new criterion is one change. A publish-time check
that needs design-time information (for example the CLR result type) and a run-time check that needs the activated
object may be two methods of that one component, but not two separate lists. The component lives in
`Elsa.Activities.Runtime.Core`, which Publishing already references, so the Design/Runtime direction is kept.

The declared criteria (C7, C8) are documented on `[BackgroundExecutionSafe]` itself, so an activity author reads the
promise where they make it.

### 4. Which built-in activities are marked follows from the criteria

The implementing spec applies C1–C9 to every built-in activity and marks those that qualify. An activity that meets C1–C6
and C9 but whose author cannot promise C7 and C8 is not marked. A candidate that falls short of a criterion only
because of how it is implemented gets an issue, not an exception.

### 5. Placement stays the workflow author's choice

`[BackgroundExecutionSafe]` says an activity *may* run in the background; the workflow author decides per node whether
it does. Publication refuses a background placement that breaks an enforced criterion, with VF-BG-001 naming the
criterion.

## Consequences

- **Spec 241's contract** (`contracts/background-activity.md`) points to this ADR for eligibility.
- **`[BackgroundExecutionSafe]`** gets documentation stating C7 and C8.
- **ADR 0092** covers what happens when a background step's inputs were read before another branch changed them: the
  same lost-update rule as for inline steps, with a refresh before admission when the base is already stale.
- **Studio** needs a way to set placement on eligible nodes. Not part of this ADR.

## Required evidence for the implementing spec

- One eligibility component used by publish and run time; a test that every E criterion is refused at both, and C6
  at run time.
- A background placement of an `IRuntimeScopedVariableReader` activity is refused (C9).
- VF-BG-001 names the failed criterion.
- `[BackgroundExecutionSafe]` documents C7 and C8.
- The built-in activities are assessed against C1–C9, and the result is recorded in the spec.
- Applying a background result happens in the same order relative to other queued work on every persistence provider.
  The A-2 analysis found by reading code that it may not: the result work item keeps its admission time, which the EF
  queue orders by and the in-memory queue does not. Not reproduced by a test yet.

## Alternatives considered

**A list of allowed activities kept by the architects.** Simple to read, but every new activity or module needs an
update, and nothing explains why an activity is on it. Rejected for the criteria.

**Let the engine prove C7 and C8.** No code can prove that an external effect is idempotent or that a body honours
cancellation. They stay declarations, with documentation as the guardrail.

**Allow variable access from the worker now.** Waits for A-3 and ADR 0090. A worker reading variables would read
state that can be older than the coordinator's; ADR 0092's lost-update rule would cover the write side, but the
question of whether it should exist belongs to A-3.

## Questions for the reviewers

@sfmskywalker @FransVanEk:

1. **Criteria instead of a list** (decision 1). Agree?
2. **The new criteria C7, C8 and C9.** Are they right, and is anything missing? For example, a bound on how long a body
   may run, or a rule about which services a body may use.
3. **One place** (decision 3). Any reason the publish and run-time checks must stay separate lists?
4. **C9 and A-3.** Sipke, does C9 match how you see A-3 going, or do you expect background steps to get a variable
   session?
5. **Provider order.** Must a background result be applied in the same order on every provider, or is the order
   between unordered branches undefined anyway (D-8) and only the write history needs to show it?
6. **Inputs.** Results must be durable and not hidden (C4, C5), but the input snapshot that travels to the worker is
   not checked the same way. Should there be an input-side criterion, or is the asymmetry intended (inputs stay inside
   the admission record, under the same protection as any committed snapshot)?
