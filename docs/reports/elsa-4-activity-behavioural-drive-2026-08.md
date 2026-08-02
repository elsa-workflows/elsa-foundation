# Elsa 4 activity library — behavioural test drive (2026-08)

> **Scope.** The behavioural half of
> [`elsa-4-activity-contract-parity-2026-07.md`](elsa-4-activity-contract-parity-2026-07.md), which that
> audit could not run: driving the shipped activities through a real workflow engine and comparing what
> the engine actually committed against what each activity's contract declares.
>
> **The question this answers:** *which declared outcomes and outputs are actually reachable?* A contract
> diff proves an outcome is **declared**. Only a run proves it is **reachable**. The gap between the two
> is a declared-but-dead port — a failure mode a contract diff structurally cannot see.
>
> **Baseline.** `elsa-foundation` on `claude/elsa-4-activity-behavioral-bf8375`, off `faf262a9c`.

## What was built

| Artifact | Home |
|---|---|
| Contract-surface snapshot guard (#1119 task 1) | `tests/Elsa/Activities/Design/Tests/Contracts/` |
| In-process behavioural drive (#1119 task 2) | `tests/Elsa/Activities/Behavioral/` |

### Contract-surface snapshot guard

An xUnit test runs the production reflection-only scanner `ClrAssemblyScanner` over all ten shipped
activity assemblies, normalises the result to a total ordinal ordering, and compares it against
`activity-contract-surface.baseline.json`. Any added, removed or altered input, output or outcome port
now lands as a reviewable JSON diff, with the actual document written beside the baseline on failure.

Two decisions worth recording:

- **The scanner, not `ClrActivityContractTestBuilder`.** The builder reads only `ActivityOutcomeAttribute`
  and would silently drop `ActivityValueOutcomes` — losing `SendHttpRequest`'s dynamic per-status ports,
  the exact gap class the guard exists for.
- **SemVer build metadata is stripped from the recorded version.** No activity declares an explicit
  `[Version]`, so the scanner falls back to the assembly informational version, which the SDK stamps with
  the source revision id. Recording it would churn the baseline on every single commit.

Refresh with `ELSA_UPDATE_ACTIVITY_CONTRACT_BASELINE=1`, mirroring the existing
`ELSA_UPDATE_EF_CORE_BASELINE` ratchet.

A sibling guard, `ActivityValueOutcomeSourceTests`, asserts that every `ActivityValueOutcomes` declaration
names a real input **contract key**. The attribute takes a plain string and the publish compiler resolves
it as a contract key, so a property-name spelling compiles, publishes, and produces no ports at all. That
mistake was made and caught while implementing `RunJavaScript`'s outcomes below.

### In-process behavioural drive

Each activity has an `IActivityDrive` that runs it through the real workflow engine on
`WorkflowExecutionHarness`. Observations are read out of the **persisted `ActivityExecutionState`**, not
asserted by the driver: a drive cannot claim an outcome it did not reach. The coverage assertions then
compare observation against the declared contract in both directions —

- every declared outcome was reached;
- every observed outcome is declared;
- every declared output was populated with a non-null value;
- every required input faults cleanly when absent;
- every shipped activity has a drive, and no drive threw.

The assertions run from a collection fixture because they are cross-drive: "every declared outcome of
every activity was reached" cannot be evaluated until all drives have run, and xUnit gives no way to
order a test last.

## Findings

### F1 — `Break` declared no outcomes (fixed)

`verified-by-run`

`Break` always completes with the `Break` outcome, but carried no `[ActivityOutcome]`. The scanner
therefore emitted no `elsa.outcomes` facet, the studio applied its own `Done` default, and the designer
offered a port that can never be taken while hiding the one that always is.

This is precisely the class the audit could not reach: the contract diff saw "no outcomes declared" on
both sides and had nothing to flag. Only running it showed the emitted outcome and the declared set
disagree. Fixed by declaring `[ActivityOutcome(ActivityOutcomes.Break)]`; baseline refreshed.

### F2 — `Fault` has an unreachable `Done` port (recorded, not fixed)

`verified-by-run`

The mirror of F1. `Fault` declares no outcomes and **never completes** — it always returns a fault
transition — so the studio's `Done` default is a port nothing can ever take.

Not fixed, deliberately: the `Done` port is a studio-side default, not something the activity declares,
and adding an outcome the activity would still never emit would make the contract *less* truthful. It is
recorded in the drive as terminal-by-contract. The real fix, if wanted, belongs in how the catalog treats
an activity with no declared outcomes — a studio/catalog decision, not an activity one.

### F3 — requiredness is enforced at admission, not mid-run (no defect)

`verified-by-run`

All eight drivable required inputs are enforced — but not where the drive first looked for them. Omitting one does
not fault the activity; the value-flow guard **rejects the start command outright** with
`VF-ACT-004: Input '<key>' on executable node '<node>' does not accept null or absence`, before the activity is
ever constructed.

Worth recording because the first version of this drive looked only for a committed activity fault, saw none, and
would have reported all nine required inputs as unenforced — a fabricated defect. The drive now accepts either
depth (admission rejection or mid-run fault) and requires the rejection to *name the input*, so an unrelated
failure cannot be mistaken for enforcement.

### F4 — an unbound `JsonElement` input cannot be defaulted

`verified-by-run`

`RunJavaScript.Arguments` declares `DefaultValue = "{}"`, but an unbound `JsonElement` input resolves
through the harness's default path to `default(JsonElement)`, which throws on `Clone()` when the runtime
builds its value envelope. Worked around in the drive by binding the input explicitly. Not investigated
further; it is a latent sharp edge in default-value materialisation for `JsonElement`-typed inputs rather
than an activity-contract gap.

## Coverage

**All 28 shipped activities** are driven through a real workflow, with every declared outcome shown
reachable and every declared output shown populated. `UndrivenCoverage` is empty.

The class is kept rather than deleted, and its guard test inverted: `Undriven_coverage_is_empty` now
asserts the list stays empty, so re-declaring a gap is a deliberate, reviewable act rather than a quiet
way to make a failing assertion go away. Nothing may be added to it to silence a failing assertion — an
outcome that turns out to be genuinely unreachable is a defect in the activity, not an entry in that list.

The last two — `DispatchWorkflow` and `GraphActivity`, the pair #1119 deferred — landed under
[#1124](https://github.com/elsa-workflows/elsa-foundation/issues/1124); see the section below.

## Fixes applied from the contract audit

| Issue | What changed |
|---|---|
| #1117 | `Fault` gains `Code`, `Category`, `FaultType`. Code lands on the returned fault; Category/FaultType ride as classification metadata on the durable record. The four handlers that projected `ActivityFault` → `NormalizedActivityFault` by hand now share one `ToNormalized()`. |
| #1117 | `PublishEvent.IsLocalEvent`. `StimulusDispatchRequest` gains an optional `TargetWorkflowExecutionId`; the router narrows the resume fan-in to it and starts nothing. The target is read from the post-commit intent — the runtime's own record of who committed the send — never from the activity-authored payload, so a local publish cannot be redirected at another instance. |
| #1114 | `RunJavaScript.PossibleOutcomes` via `ActivityValueOutcomes`. **Design call:** the script routes by *returning*, not through a host-injected `setOutcome()`. The sandbox stays closed and side-effect-free, routing stays a pure function of the script's value, and the ports stay statically inspectable at publish time. Elsa 3's `setOutcomes()` (plural) has no counterpart — an Elsa 4 completion carries exactly one outcome. |
| #1113 | The authoring catalog publishes `Finish`, `SetCorrelationId` and `SetInstanceName` — each the replacement for a first-class Elsa 3 activity, previously implemented by the engine and accepted by the REST API but absent from the palette. **Design call:** `Merge`/`Reduce` stay withheld (they execute identically to `Set` today, so three palette entries for one behaviour would mislead); `Control`/`Return` stay withheld (compiler seams with no Elsa 3 counterpart). The tests pin that decision. |
| #1117 | `For.EndInclusive` — **documented, not changed.** Elsa 3's `OuterBoundInclusive` defaults to `true` and this defaults to `false`, so a ported loop runs one fewer iteration. The half-open convention is what the member name reads as and what the rest of the module assumes, so the divergence stands; it is now called out in the activity docs and the module README instead of being silent. |

## REST e2e drive

Run against a from-source `Elsa.Server` on SQLite with a freshly deployed Groundwork schema. Two new suites
close the two gaps the in-process drive structurally could not.

### `SendHttpRequest` dynamic ports are real published ports

`e2e-tests/http/Test-SendHttpRequestStatusOutcomes.ps1`

The attribute existing is not proof the published node has the ports. The in-process drive proves the
activity *emits* an outcome named after the matched status, and the snapshot guard proves the design facet
*advertises* the mechanism — neither proves a published workflow can connect a branch to port `"200"` and
have the runtime take it. This authors a Flowchart wiring one branch per port, publishes it, and runs it:

| ExpectedStatusCodes | Response | Port taken |
|---|---|---|
| `[200, 404]` | 200 | `200` |
| `[404]` | 200 | `Unmatched status code` |

Both routed correctly and the sibling branches did not run. **The per-status ports and the catch-all are
genuinely connectable on the published node.** The second case matters most: it proves the catch-all is a
real port, not just a string in an attribute.

### The intrinsic authoring catalog is discoverable and sufficient

`e2e-tests/get-endpoints/Test-IntrinsicAuthoringCatalog.ps1`

`GET /design/activities/catalog` now offers five intrinsic descriptors — `Set`, `SetOutput`,
`SetCorrelationId`, `SetInstanceName`, `Finish` — and withholds `Merge`, `Reduce`, `Control`, `Return`.
Both directions are asserted, so re-adding a withheld kind is a deliberate change rather than a drift.

The suite then closes the loop: it takes the `SetCorrelationId` descriptor's **own** version id, intrinsic
kind and value-input key, authors a node from them, publishes, runs, and checks the correlation id landed
on the instance. Authoring from the descriptor rather than a hand-written literal is the point — it proves
the published descriptor is sufficient to place a working node, which is exactly what #1113 was about.

### Regression sweep

The existing suites were re-run against the same server to confirm the source changes hold through the real
HTTP + persistence + runtime path — in particular `Fault` (whose inputs changed), `Correlate` (which uses
the `SetCorrelationId` intrinsic) and `For` (whose `EndInclusive` default was reviewed and kept):

`Test-WorkflowFlow`, `Test-SequenceWorkflow`, `Test-IfWorkflow`, `Test-SwitchWorkflow`,
`Test-HttpWorkflow`, `http/Test-HttpMethods`, `http/Test-HttpEcho`, `fault-handling/Test-FaultActivity`,
`events/Test-Event`, `correlate/Test-Correlate`, `javascript/Test-JavaScriptExpressions`,
`orchestration-controls/Test-SuspendResume`, `orchestration-controls/Test-FinishTerminate`,
`orchestration-controls/Test-StimulusRouting`, `branching/Test-ParallelFork`,
`single-outcome/Test-ForLoop` — all pass.

## Closing the last two — `DispatchWorkflow` and `GraphActivity` (#1124)

### The design call: how the drive gets a dispatch-capable harness

`DispatchWorkflow` cannot run on the plain harness, and #1124 offered three ways to fix that: move the
existing 871-line `DispatchWorkflowRuntimeTestFixture` into the shared `tests/Elsa/Activities/Testing/`
library, teach `WorkflowExecutionHarness` a dispatch-capable opt-in, or give the behavioural project its
own duplicate fixture.

Two facts settled it. First, the behavioural project **already** references both
`Elsa.Activities.DispatchWorkflow.Runtime` and `Elsa.Activities.Graph.Runtime` — it must, to enumerate the
whole shipped library. So the dependency the issue worried about is not a cost of driving these two; it is
a cost of *where the code lives*. Second, a `ProjectReference` is unconditional: a `WithDispatch()` builder
step in the shared library would pay exactly the same dependency as moving the fixture there. Option 2 as
literally phrased does not avoid the cost it was proposed to avoid.

What does avoid it is splitting on **what is actually generic**. Everything `DispatchWorkflow` needs beyond
today's harness turns out to name no DispatchWorkflow type at all:

| Added to `WorkflowExecutionHarness` | Why it is generic |
|---|---|
| `PublishAsync` | Saves an executable *and* the live Published source reference the start dispatcher gates on (ADR 0040). Any drive needing a second workflow wants this. |
| `StartPublishedAsync` | Starts through the real `IWorkflowStartDispatcher`. `RunAsync` hand-builds its start envelope and so seeds no durable partition, authority or source provenance — any activity that reads its *parent execution's* durable context, not just its own inputs, cannot run on that path. |
| `SweepAsync` / `SweepUntilQuietAsync` | Runs the post-commit outbox + stranded-work resumption sweep to quiescence. |
| `CancelAsync` | Delivers one Cancel command through the execution agent — the control-plane path. |
| `RetirePublicationAsync` | Retires a source reference, modelling an unpublish between an intent being committed and delivered. |
| `ReadRunAsync` | Reads the persisted run of *any* execution id, not just the harness's own. |

Every type involved lives in `Elsa.Workflows.Runtime.Core`, which the shared library already references, so
**`Elsa.Activities.Testing` gained no new project reference**. Everything DispatchWorkflow-named — the pin
metadata key, the completion resume target, the outcome expectations — stays in the drive. That is option 2
in spirit (pay the dependency only where it is used) without the compile-time reference that would have made
option 2 indistinguishable from option 1.

### `DispatchWorkflow` — all five outcomes, both outputs

Each wait-mode outcome mirrors the child's fate, so each is reached by making a real child meet that fate
and letting the completion enricher, the outbox and the parent-resume path carry it back. Nothing
hand-feeds the parent a resume payload; synthesising one would prove the `switch` in `ResumeAsync` compiles,
not that the runtime can ever deliver that status.

| Outcome | How it is reached |
|---|---|
| `Dispatched` | `WaitForCompletion=false` — completes inline, carrying only `ChildWorkflowExecutionId`. |
| `Completed` | Waited; the child runs a probe leaf and completes. |
| `Faulted` | Waited; the child's root activity faults, ending the child workflow Faulted. |
| `Cancelled` | Waited; the child waits on an event, then takes a real Cancel command through its agent. |
| `DispatchFailed` | Waited; the child's publication is **retired after the parent stages its dispatch**, so the gated child start can never succeed and delivery exhausts. |

`ChildWorkflowExecutionId` is populated by every case; `Result` only by the waited ones, which is the
contract — fire-and-forget has no result to carry.

`DispatchFailed` caps `ChildStartMaxDeliveryAttempts` at 1 (a documented feature setting) so the first
failure is final. The alternative — leaving the default four and advancing a fake clock through the
backoff — measures the same terminal state while adding a simulated clock to the drive.

`WorkflowDefinitionId` is un-deferred and enforced exactly as F3 describes: the start comes back carrying
`VF-ACT-004: Input 'WorkflowDefinitionId' on executable node 'node-dispatch' does not accept null or
absence`, before the activity is constructed. Worth noting the *status* is `AcceptedButFaulted` rather than
`Rejected` — "refused before the activity runs" is the contract; the precise dispatch status is not.

### F5 — one constant id made every two-workflow drive impossible (test infrastructure)

`verified-by-run`

`DeterministicRuntimeExecutionIdGenerator` returned the literal `"command-generated"` for *every* command.
A checkpoint commit id derives from the command that produced it, so the parent's start command and the
dispatched child's start command produced the same commit id, and the child's `WorkflowStarted` checkpoint
died with `RuntimeCheckpointReplayConflictException: … was replayed with a conflicting payload`. The child
never ran; the parent waited forever.

Invisible in every existing test, because until now nothing on this harness started a second workflow — a
directly-authored command carries an authored id, and only a *generated* one hit the collision. Fixed by
numbering the generated ids. Recorded because it is the reason this gap looked harder than it was: the
blocker was one line of shared test infrastructure, not the dispatch lifecycle.

### `GraphActivity` — `Done` through a real run

The graph boundary is not a CLR activity the harness can reflect over: it is activated from a pinned
`elsa.graph-activity/1` descriptor rather than from a type, and declares no atomic result type. The drive
builds the node with the descriptor and contract `GraphActivityProvider` emits, and the runtime activates it
through the real `GraphActivityActivationStrategy`. Two cases: an unmapped boundary completing with the
implicit `Done`, and a mapped one translating its entry's outcome to an authored boundary port.

What this adds over `GraphActivityExecutionTests` is the engine. Those tests call
`ExecuteStructureAsync`/`OnChildCompletedAsync` on a hand-made context and read the returned transition,
which cannot show that the scheduler ever schedules the entry node or that the selected outcome is ever
*committed*. Here it is read back out of persisted state.

`GraphActivity` joins `Switch`, `BpmnDecision`, `SendHttpRequest` and `RunJavaScript` in the
observed-outcome exemption list: a mapped boundary's ports are authored per-node data pinned at publish
time, not a fixed declared set. Its *declared* side remains the implicit `Done`, which the drive reaches.

### REST e2e — `e2e-tests/composition/Test-DispatchWorkflowOutcomes.ps1`

A direct drive of the outcome matrix, with the BPMN engine taken out from between the assertion and the
activity. Run against a from-source `Elsa.Server` on SQLite with a freshly deployed Groundwork schema:

| Case | Port taken | Parent |
|---|---|---|
| `WaitForCompletion=false` | `Dispatched` | Completed |
| waited, child completes | `Completed` | Completed |
| waited, child faults | `Faulted` | **Completed**, zero incidents |

The third is the one worth having: a faulted child surfaces as an ordinary routable port and the step after the
dispatch still runs, rather than the fault crossing the boundary and taking the parent down with it. The child's
incident stays on the child.

`Cancelled` and `DispatchFailed` are **not reachable over REST** and are deliberately not attempted:
cancellation would need an instance-cancellation control-plane call, which the runtime API does not expose, and
`DispatchFailed` would need child-start delivery to exhaust its retries, which no REST surface can force. Both
are driven in-process. This is stated in the script so the absence reads as a known boundary rather than an
oversight.

### F6 — the workflow root activity's outcome is missing from the inspection projection (#1127)

`verified-by-run`

The first version of that script authored the dispatch node as the workflow **root**, and could not see the
outcome it had definitely taken: the instance detail reported `outcomeNames: []` on a `Completed` node. The
activity-execution detail for the same node carries `runtime.completionOutcomeNames: ["Completed"]` — the
durable record is correct, the projection is not.

It is positional, not activity-specific. Wrapping the node in a `Sequence` moves the loss to the `Sequence`:

```
- node-root      [Completed] Sequence           outcomeNames: []
- node-dispatch  [Completed] DispatchWorkflow   outcomeNames: ["Completed"]
- node-after     [Completed] WriteLine          outcomeNames: ["Done"]
```

A run that never suspends keeps its root outcome; one that suspends and resumes does not. Filed as
[#1127](https://github.com/elsa-workflows/elsa-foundation/issues/1127) rather than fixed here — it is a runtime
read-model defect, not an activity-contract one, and the in-process drive reads `ActivityExecutionState`
directly so it is unaffected. The e2e script nests the node (also the realistic authoring shape) and says why.

Worth noting what caught it: not an assertion, but authoring the *simplest possible* parent. Every existing
suite that drives a dispatch happens to nest it, so nothing had looked at the root position.

### #1117's `DispatchWorkflow` items — assessed, deliberately not landed here

#1124 suggests `ChannelName`/`StartNewTrace` might land alongside Unit A. They should not, and the reason is
this suite's own principle. Both are **feature additions**, not contract corrections: there is no channel
concept anywhere on the dispatch path (`WorkflowDispatchRecord` carries no channel, and nothing routes on
one), and `StartNewTrace` is a telemetry-topology decision about where a dispatched child's trace begins.
Adding either as an input without the mechanism behind it would create exactly the thing this drive exists to
catch — a declared surface an author can bind and nothing will ever read. They need their own unit with a
design call, not a slot in a coverage change.

### Correction — `GraphActivity` inlining is not uncovered over REST

#1124 records "`GraphActivity` inlining has no e2e coverage at all." That is wrong, and worth correcting
rather than acting on. The `e2e-tests/reusable-activities/` suite **is** the graph boundary: reusable
activities are authored as `elsa.activity-graph` graphs, and its eight scripts already cover root placement,
three-layer nesting, exact-version pinning, draft test-runs, and schema-2 mapped boundary outcomes over
REST. `e2e-tests/README.md` categorises that suite as "authoring lifecycle + graph inlining at runtime". No
new script was written for it; duplicating that coverage would have added maintenance without adding signal.

## Still outstanding

In-process coverage is complete. The REST drive is not the full matrix. Not run:

- `BpmnProcess`/`BpmnDecision` over REST beyond what `bpmn/Test-BpmnCallActivity.ps1` reaches;
- `DispatchWorkflow`'s `Cancelled` and `DispatchFailed` over REST — structurally unreachable there today
  (no instance-cancellation endpoint, no way to force delivery exhaustion), so closing this needs an API
  change rather than another script;
- `Timer`/`Cron` recurring firing and `Delay` expiry against a live scheduler;
- the combined scenarios sketched in the original brief (HTTP endpoint → JS transform → `SendHttpRequest`
  → branch per code → `WriteHttpResponse`; `ForEach` containing `If` + `Break` + `Set`; `Parallel` fork
  with a `Delay` in one branch and a `DispatchWorkflow` in the other).

Out of scope for this pass, unchanged from the contract audit: #1116 (HttpEndpoint file uploads +
`DownloadHttpFile`/`WriteFileHttpResponse`) and #1118 (the seven missing activities).

## Routing

Findings here are evidence. Work that gets planned should move to the
[Code Reality And Test Maturity](../program-goals/code-reality-and-test-maturity.md) bucket per
`AGENTS.md`.
