# M0 native integration witness work unit

**Status:** Delegated engineering work under DE1/DE2; review before executing the fixture. This is a research witness plan, not the production StateMachine implementation plan or final engine/schema approval.

**Authority:** Supervisory delegation recorded on [Program #3815](https://github.com/elsa-workflows/elsa-foundation/issues/3815#issuecomment-6646432172), preserving OD1–OD49. The supervisor separately relayed Sipke's authority to make necessary decisions and proceed without waiting for his approval. Record delegated decisions as such; do not fabricate human inspection or another designated approver's agreement.

**Route:** StateMachine program M0, feature #3820, existing owned worktree and draft PR #3840. [Product specification](spec.md) remains provisional as a complete contract; [research P1–P6](research.md#m0-engineering-proposal-for-review) is the provisional engineering basis. The prototype proves only the observations listed below.

## Goal and boundary

Exercise the native runtime, actual SQLite persistence, ordinary child work and normal operator retry while a small structural parent retains input/phase state. Separate process generations reopen the same owned database; no cached parent instance, in-memory workflow store or custom scheduler may supply recovery state.

The first carrier is an ordinary `Event` child, re-armed by the structural parent after each completion. The fixture must distinguish bookmark transport dispatch from the parent's committed semantic receipt. If this does not provide sufficient continuous machine-directed admission or an authoritative admission result, report the gap and qualify a reviewed shared extension or the existing model-hosting alternative. Do not turn a partial carrier proof into a complete inbox claim.

No production activity module, public endpoint, database migration, Studio designer or Saga type is added by this work unit. The fixture is a non-packable executable under `witness/`, using the existing native execution test harness, EF runtime aggregate and migration startup. Its public CLR fixture types exist only so the normal CLR activity binder can construct them.

## Concrete fixture

- A structural parent schedules ordinary Exit work, then an ordinary typed waiting Action. A separate ordinary Event child carries business inputs while Action waits. Phase and input state use `RuntimeStructuralContinuation.WithState` and the native activity state store.
- Fixture inputs have an ID and public JSON content. Pending capacity is C = 2. The parent records accepted order/content and final status, keeps the input selected/in flight within that capacity, and preserves unrelated waiting work. Fixture values are not production defaults or a chosen public schema.
- After Action succeeds, two ordinary guard/probe children carry the same immutable scheduling input ID. Reverse their scheduling order in one case. The parent groups their callbacks by that ID and records the child-first selection independently of callback order. This is a provenance/adapter witness, not qualification of the complete statechart selection algorithm or all expression bindings.
- An injected transient guard failure leaves the parent awaiting intervention through the existing child-fault protocol. Normal `RetryFaultedActivity/1` must retry that child while preserving Exit, Action, the successful sibling and the selected input's identity. No whole-parent retry is substituted. The first development run disproved the assumption that this retry requires `IRuntimeAcceptsRescheduledFaultedChild`: that marker governs `RescheduleActivity/1`, while the normal retry handler checks its own fault/intervention/parent eligibility. Keep an additional successful recovery run without the reschedule marker to preserve that distinction.
- A separate Action variant faults when its first wait is resumed, preserving Exit and queued inputs. Capture that original exact bookmark, retry the failed Action through normal operator recovery, and capture the successor's distinct waiting bookmark. Delivering the saved old bookmark must not advance the replacement Action; only its new exact bookmark can complete it. A guard retry after Action already succeeded is not a substitute for this case.
- Every generation creates a fresh native host/service provider. The executable invokes later generations as owned child processes over the same SQLite file; process exit clears actors and singleton fixture controls. A generation may read only state committed by the prior generation.
- Generate new activity identities from disjoint per-generation ranges while retaining the same workflow/material and persisted root invocation. Derive retry scope/authority from the actual fixture execution; do not accidentally target another partition or reuse an earlier child's identity.

## Observations and negative controls

| Witness | Required actual observation | What it does not establish |
|---|---|---|
| N1 — waiting phase | Exit completed once, Action waits, two distinct inputs are committed without evaluation; another input reports full; identical/conflicting IDs remain distinguishable | No production ingress/public API contract or universal input-retention policy |
| N2 — restart and drain | A new process sees the pending Action and ordered receipts; finishing Action drains the accepted inputs in order | No complete hierarchical/parallel StateMachine implementation |
| N3 — guard recovery | A real fault/incident and normal operator retry preserve the completed sibling/phases and input scheduling provenance | No claim that inline expression faults already have the same recovery seam without an adapter |
| N4 — logical cause | Both guard callbacks retain the same scheduled input ID despite reverse source order; a later input is never substituted for it | No equivalence between arbitrary composite triggers and a single logical event without further qualification |
| N5 — retired wait | A saved old Action bookmark cannot complete a replacement Action after retry; only the new exact wait can do so | No retargeting of old completions to new visits |
| N6 — terminal state | Completed/cancelled native state and retained private receipts can be read in a later process; queued versus selected inputs remain distinguishable | No receipt SLA after deletion or outside normal retention guarantees |

Mutation/negative control: omit the parent's structural state update or corrupt the callback's expected input ID in an isolated local run. The state read/provenance positive assertion must fail with the intended diagnostic; an unrelated crash is not a successful control. Removing the reschedule marker is not a valid negative control for `RetryFaultedActivity/1`, as the executed development run and handler source demonstrate. These controls establish dependence on real checkpoint/provenance mechanisms; they do not prove production behavior that has not been implemented.

### Observed development gaps

The first development batch passes waiting-phase admission, fresh-process FIFO drain and guard-child retry with scheduling provenance. It does **not** qualify the entire witness. Native structural completion explicitly clears private state, so `Complete().WithState(...)` does not retain a terminal receipt archive. Whole-workflow cancellation retains the prior private state but does not itself change fixture `Queued` receipts into explicit final `NotProcessed:Cancelled` dispositions. N6 remains failed/partial until an actual supported closeout/read contract is qualified. Preserve these counterexamples; do not retain all completed private state or change cleanup/privacy rules to make the test pass.

The initial Action fault injection also exposed a fixture serialization mistake: the ordinary typed trigger binder deserializes the trigger with its declared JSON names, while the fixture sent camel-case fields without declaring those names. The fixture now declares its trigger JSON property names explicitly. That initial run is not an Action-retry pass. Re-execute the actual fault/retry/retired-bookmark case after correction.

### Next bounded probe: existing terminal-result seam

The clean-head [b337 evidence](evidence/m0-native-witness-b337.json) now proves N1–N5 within their stated limits, including the corrected real Action fault/retry. Twenty separate child processes produced thirteen expected results and seven intentionally retained positive-case failures around N6. The runner exits nonzero. These are process/assertion outcomes, not a passing production test suite.

Before proposing any shared runtime extension or switching to model hosting, qualify the existing `IRuntimeActivityCheckpointParticipant.PrepareCompletionCheckpointAsync` seam used by GraphActivity. Structural completion projects its supplied typed result atomically into the normal completion envelope, then clears private state. A fixture root with a declared typed result can export a **bounded receipt-only** terminal result containing invocation identity and final receipt IDs/order/disposition/reason, without copying its private phase state or business payload. It stages no independent store writes. Recompute this result in the same activation that selects Complete; a process-local field may carry that decision to the immediate completion hook but must never supply recovery truth across a wait/restart. An aborted checkpoint must still leave the normal runtime responsible for re-driving its durable source.

For cancellation, qualify a canonical receipt projection from **one retained root activity state**, rather than combining independently read workflow and activity snapshots: its terminal status, completion timestamp, committed queue/selection and receipts are atomically persisted by the native cancellation planner. Only unselected queued entries become `NotProcessed:Cancelled`; selected work must remain distinguishable as interrupted, with no claim that external effects were undone. A completed root reads its receipt-only completion result; an active root reads its committed private state. The cancellation projection is a deterministic reading of durable terminal facts, not a new evaluator, scheduler, acknowledgement authority or mutation of private-state cleanup.

Add cold completion/cancellation reads, including cancellation with a selected guard awaiting recovery and another queued event. Prove that normal completion still clears private state, the terminal result contains no raw business content, queued cancellation differs from selected interruption, and every receipt remains linked to the original root invocation. Add isolated controls disabling the terminal-result contribution and cancellation projection; each corresponding positive assertion must fail for its intended reason. Retention remains bounded by normal retained activity/completion records in this fixture; no production receipt retention SLA, terminal deduplication or complete ingress guarantee is inferred.

Independent correctness/architecture, standards and acceptance review must assess this concrete probe before implementation. If it fails, compare model hosting's retained model state/input receipts against its still-unproved ordinary-child/binding/compensation bridge. Do not change completed-private-state cleanup, invent another persistence authority or select the fallback from source preference alone.

The [executed 873 receipt](evidence/m0-native-witness-873.json) now records that reviewed probe: 34 distinct process runs, thirty successful runs plus four intended negative-control failures, all expected, runner exit 0. N1–N6 pass within the stated limits. The earlier failed b337 observations remain historical counterexamples; the fixture now uses the existing completion-result seam and terminal cancellation projection rather than retaining completed private state.

### Next bounded probe: admission during recovery, competition and ordinary nesting

Continue the native-first comparison using the same fixture and existing runtime owners; do not introduce a public API or choose production placement from the sequential carrier pass alone.

**N7 — admission evidence.** Fault the guard with only input A selected, leaving one pending-capacity slot free. While the parent remains awaiting normal recovery, admit B through its ordinary Event carrier and prove B is committed without evaluation; a further input must report full. In a separate case, capture the same physical carrier wait for two concurrent callers competing for that last slot. Retain both transport results and the subsequent native receipt/decision evidence. A stale/consumed physical carrier does not mean semantic full or accepted: a sender may make a fresh carrier attempt with the same business ID, and may report acceptance only from a committed matching receipt. Bound fixture retries and report unresolved delivery as unknown, never accepted or definitely rejected. Do not retarget the saved Action bookmark used by N5.

After normal guard recovery, prove there was no hidden third pending slot, no repeated A selection, and that the accepted order remains authoritative. Exercise identical and conflicting accepted-ID retries while recovery blocks evaluation. Fill the separate retained-receipt budget after inputs settle and prove it refuses a new ID even when pending capacity is free, while an existing identical retry still returns the retained result. A previously full/rejected ID may later be admitted once capacity permits; rejection does not reserve its ID or position. Add a capacity mutation that omits selected work from accounting and require the corresponding positive bound assertion to fail.

Ignore one delivery response after its request completed, close that owned process, and resolve the same business ID from committed native state in a new process before retrying identically. This qualifies a lost-response retry after an actual committed admission; it is **not** a crash/ambiguous-provider-commit or exactly-once-effects claim. Keep true ambiguous-commit storage-failure qualification open unless an actual after-commit fault injection is added under a separately reviewed concrete plan. The concurrent callers share one normally coordinated fixture host; distributed multi-host exclusion is not inferred.

**N8 — ordinary nested caller and result binding.** Add only the existing Sequence activity module to this non-packable executable. In a separate authored executable, put the same native fixture inside an ordinary Sequence, followed by an ordinary typed-result consumer bound through the normal activity-result reference contract. Publish/start with the normal harness source lifecycle, restart while the nested machine waits, complete it, and prove the surrounding Sequence continues once and the consumer receives its declared typed terminal result. Compare original workflow/machine/child identities, normal outcome and committed completion evidence after another process restart. The fixture's typed receipt result remains a public-data probe, not a chosen production StateMachine output schema.

This step qualifies a root/nested native composition and ordinary result-binding seam; it does not establish state-visit variable scopes, all expression/secret-withholding paths, mapped production outcomes, Studio publication or the saga topology. Keep those obligations explicit in M0/production planning. No new broad host/provider/Studio build begins under the current shared-machine hold. Independently review this plan before extending the fixture, then review its exact implementation and capture actual clean-source results and limits.

## Execution and evidence

1. Review this plan with the repository's independent correctness, standards and spec-conformance lenses. Resolve concrete defects using source and failure scenarios.
2. Build only the focused executable, through `dotnet` on PATH, once. Reuse that output for its child process generations. No broad Workbench/Studio build or provider matrix starts before the coordinator releases that shared-machine validation turn.
3. Run the bounded SQLite witness against owned temporary paths and capture source head, assembly paths, persistence mode, process generations, exact workflow/activity/input identities, receipts, incidents, retry jobs and relevant activity states in JSON.
4. If a build slot wrapper is present, respect it. The current task PATH resolves the dotnet host directly; keep MSBuild concurrency narrow and do not bypass a wrapper if one becomes available. Record load; do not classify timing-shaped failures under contention as product failures without a lower-load check.
5. Record each N1–N6 as passed, failed, partial or not executed with concrete evidence. A source observation, successful transport enqueue or a small model assertion is not a passed integration witness.
6. Amend research from the result, document the smallest required shared change or fallback, and perform another independent review. Then advance the full specification and production plan/tasks through normal gates under delegation. Preserve actual designated non-author approval requirements if a real dependency introduces them.

## Proposed artifacts

- `witness/StateMachine.M0.Witness.csproj`: non-packable focused executable; no production project references it.
- `witness/Program.cs`: process-generation runner and assertions using actual runtime stores/dispatchers.
- `witness/NativeProbe.cs`: small CLR fixture activities and their native continuation data.
- `evidence/m0-native-witness.json`: captured results and limits, added only after execution. Do not prefill passing observations.

The exact execution commands and counts belong in the evidence/PR receipt after they run. Shared-machine broad validation and full first-release qualification remain separate delivery gates.
