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

26 of the 28 activities are driven through a real workflow, with every declared outcome shown reachable
and every declared output shown populated. Two are not, and the gaps are declared in code
(`UndrivenCoverage`) and reported by a dedicated test rather than hidden inside a green run:

| Activity | Why not driven here | Where it is covered |
|---|---|---|
| `DispatchWorkflow` | Refuses to execute against a hand-built node: it requires the exact child-executable pin the publish compiler stamps into node metadata, plus the outbox sweep, child admission and parent-resume path. None of that is stood up by the plain harness. | `tests/Elsa/Activities/DispatchWorkflow/Tests` on that module's own runtime fixture; `e2e-tests` |
| `GraphActivity` | An inlining boundary: driving it needs a published reusable-activity definition version and its provider, not a hand-built node. | `tests/Elsa/Activities/Graph/Tests` |

Folding those two fixtures into this suite is the remaining work for full in-process coverage. Nothing
may be added to `UndrivenCoverage` to silence a failing assertion — an outcome that turns out to be
genuinely unreachable is a defect in the activity, not an entry in that list.

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

## Still outstanding

The e2e drive covered the two publish-time gaps above and a regression sweep, not the full matrix. Not run:

- `DispatchWorkflow` (waited and fire-and-forget), `GraphActivity` inlining, `BpmnProcess`/`BpmnDecision`
  over REST — the same two activities the in-process drive defers on, so they remain the weakest-covered
  pair in the library from this exercise's point of view;
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
