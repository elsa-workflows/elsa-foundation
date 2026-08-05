# Elsa 3 → Elsa 4 — activity contract parity audit (2026-08)

> **Supersedes** [`elsa-4-activity-contract-parity-2026-07.md`](elsa-4-activity-contract-parity-2026-07.md).
> That report's three headline findings have all moved: two are fixed, one still stands. Its numbers
> were correct when written and are wrong now.
>
> **Scope.** Every out-of-the-box activity in `src/Elsa/Activities`, diffed member-by-member against its
> Elsa 3 counterpart in `elsa-core`, plus the engine intrinsics that replaced Elsa 3 activities.
>
> **Evidence.** [`evidence/activity-contract-parity/`](evidence/activity-contract-parity/) — regenerated
> for this report. Reproduce with [`tools/parity/`](../../tools/parity/README.md).
>
> **Baselines.** Elsa 4 = this repo at `76ba13d27`. Elsa 3 = `elsa-workflows/elsa-core` at
> `5429008d98a56afd29b4fd11107f7760710b1a64`, modules `Elsa.Workflows.Core`, `Elsa.Http`,
> `Elsa.Scheduling`, `Elsa.Expressions.JavaScript`, `Elsa.Workflows.Runtime`. Elsa 3 integration
> connectors (Email, MassTransit, SQL, CSV, file IO, Slack, …) are excluded: they are tracked for a
> separate extensions workspace, not for `elsa-foundation`.

## Confidence and what is *not* claimed

This audit is a **declared-contract diff read from source**, not a behavioural test drive. Contracts in
both codebases are attribute-declared, so the diff is faithful about *what an activity exposes*. It does
**not** prove that a declared outcome is reachable at runtime or that a declared output is ever
populated.

The behavioural half **has** since been done, and is a separate report:
[`elsa-4-activity-behavioural-drive-2026-08.md`](elsa-4-activity-behavioural-drive-2026-08.md). All 28
shipped activities are driven through a real engine; `UndrivenCoverage` is empty and guarded. The July
report's "Blocked work" section — written in an environment where `dotnet restore` could not reach
`f.feedz.io` — no longer applies.

---

## What changed since 2026-07

| | 2026-07 | 2026-08 |
|---|---|---|
| Elsa 4 activities | 28 | **28** |
| At full contract parity | 17 | **19** |
| With a member-level gap | 10 rows | **7 rows / 5 distinct activities** |
| Elsa 3 activities with no counterpart | 7 | **7** |
| Elsa 3 activities that became intrinsics | 5 | **5** |

Three verdicts flipped `gap` → `present`, all confirmed in the regenerated findings JSON:

| Activity | Was | Now |
|---|---|---|
| `Fault` | `inputs missing: Code, Category, FaultType` | full parity |
| `PublishEvent` | `inputs missing: IsLocalEvent` | full parity |
| `RunJavaScript` | `inputs missing: PossibleOutcomes` | present (`new in Elsa 4: Arguments, Value`) |

### A tooling fix landed with this report

`extract-elsa4-activity-surface.py` detected requiredness only from Elsa's `[Required]` marker. The
production scanner (`ClrAssemblyScanner.HasRequired`) deliberately honours **both** `[Required]` and the
`RequiredMemberAttribute` the C# compiler emits for `required` members, so an input declared with the
`required` modifier was being reported as optional.

`RunJavaScript.Script` is the only such input in the tree today, and it was under-reported. Fixed here;
the tree now shows nine required inputs rather than eight. No verdict moved — the diff does not compare
requiredness across the two codebases (the Elsa 3 extractor does not track it), so this was a
presentation error in the Elsa 4 inventory, not a false parity claim.

### The July headline findings, restated

1. **"Seven of the nine engine intrinsics are not in the authoring catalog."** ❌ **Stale — fixed by
   #1113.** `IntrinsicAuthoringDescriptorProvider` now publishes **five**: `SetVariable`, `SetOutput`,
   `SetCorrelationId`, `SetInstanceName`, `Finish`. The remaining four (`Merge`, `Reduce`, `Control`,
   `Return`) are **deliberately withheld** — `Merge`/`Reduce` execute identically to `Set` today,
   `Control`/`Return` are compiler seams. Both halves are asserted by
   `e2e-tests/get-endpoints/Test-IntrinsicAuthoringCatalog.ps1`, so re-adding one is a deliberate act.
2. **"`RunJavaScript` lost configurable outcomes."** ❌ **Stale — fixed by #1114.** The activity now
   carries `[ActivityValueOutcomes(PossibleOutcomesInputKey, UnmatchedOutcome = …)]` with dynamic ports
   and a catch-all `"Unmatched"` outcome (`src/Elsa/Activities/Scripting/Activities/RunJavaScript.cs`).
3. **"`HttpEndpoint` has no outcome ports and no file-upload surface."** ✅ **Still stands.** Tracked as
   #1115 (outcome ports) and #1116 (file upload + the two missing HTTP file activities).

---

## 1. Engine intrinsics — resolved

`verified-in-source`

The engine supports nine kinds (`src/Elsa/Workflows/Design/Core/Models/ActivityNode.cs`,
`AuthoredWorkflowIntrinsicKind`). Five are published to the authoring catalog; four are engine-internal
by design.

| Elsa 3 activity | Elsa 4 intrinsic | In catalog? |
|---|---|---|
| `SetVariable` | `elsa.intrinsic.set@1` | yes |
| — | `elsa.intrinsic.set-output@1` | yes |
| `Correlate` | `elsa.intrinsic.set-correlation-id@1` | yes |
| `SetName` | `elsa.intrinsic.set-instance-name@1` | yes |
| `Finish` / `Complete` | `elsa.intrinsic.finish@1` | yes |
| — | `elsa.intrinsic.merge@1` | no — identical to `Set` today |
| — | `elsa.intrinsic.reduce@1` | no — identical to `Set` today |
| — | `elsa.intrinsic.control@1` | no — compiler seam |
| — | `elsa.intrinsic.return@1` | no — compiler seam |

All nine remain authorable via the code-first builder and the REST API; the catalog governs
*discoverability*, not reachability.

---

## 2. Activities with member-level gaps

`mechanical`, from `parity-findings.json`

Five distinct Elsa 4 activities carry a shortfall. `FlowJoin`, `FlowSwitch` and `FlowSendHttpRequest`
are Elsa 3 flow-model variants that map onto the same Elsa 4 activity as their non-flow twin, which is
why seven rows collapse to five activities.

| Elsa 4 activity | Shortfall |
|---|---|
| `HttpEndpoint` | inputs missing: `FileSizeLimit`, `AllowedFileExtensions`, `BlockedFileExtensions`, `AllowedMimeTypes`; outputs missing: `Files`, `File`; outcomes missing: `Request too large`, `File too large`, `Invalid file extension`, `Invalid file MIME type` |
| `SendHttpRequest` | inputs missing: `Authorization`, `DisableAuthorizationHeaderValidation`; outcome missing: `Failed to connect` (from the flow variant) |
| `DispatchWorkflow` | inputs missing: `StartNewTrace`, `ChannelName` |
| `Switch` | input missing: `Mode` (MatchFirst/MatchAny) |
| `Parallel` | input missing: `Mode` (from `FlowJoin`: WaitAll/WaitAllActive/WaitAny) |

**Assessed and rejected as feature additions rather than contract corrections:** `DispatchWorkflow`'s
`ChannelName` (no channel concept exists on the Elsa 4 dispatch path — `WorkflowDispatchRecord` carries
none) and `StartNewTrace`.

---

## 3. Elsa 3 activities with no Elsa 4 counterpart

`mechanical` — unchanged from July. Tracked as **#1118**, which states each row needs its own triage
call before any becomes work.

| Elsa 3 activity | Module | Note |
|---|---|---|
| `StartAt` | Scheduling | absolute-time trigger; `Delay`/`Timer`/`Cron` exist, this does not |
| `StateMachine` | Workflows.Core | states + event-driven transitions |
| `ParallelForEach` | Workflows.Core | `ForEach` and `Parallel` exist separately; no concurrent iteration |
| `RunTask` | Workflows.Runtime | external task/callback pattern |
| `BulkDispatchWorkflows` | Workflows.Runtime | dispatch one child per item, with `Completed`/`Canceled` outcomes |
| `DownloadHttpFile` | Http | pairs with the missing `HttpEndpoint` file surface (§2) |
| `WriteFileHttpResponse` | Http | file/stream responses with resumable download support |

Excluded as engine internals rather than toolbox activities: `Workflow`, `CompositeWithResult`,
`NotFoundActivity`, `Start`, `End`.

---

## 4. Intentional divergences — not gaps

`verified-in-source` — unchanged from July.

| Elsa 3 | Elsa 4 | Why it is not a gap |
|---|---|---|
| `ForEach.CurrentValue` output | `currentItem` scoped variable | `ForEach.CurrentItemVariableName`; `currentIndex` too |
| `For.CurrentValue` output | `index` scoped variable | `For.IndexVariableName` |
| `HttpEndpoint.Headers` / `.QueryStringData` | `Request` output | `HttpRequestModel.Headers` / `.Query` |
| `FlowFork.Branches` input | Parallel child slots | branches are authored structure, not an input |
| `Switch.Output` | case outcome ports | routing replaces the matched-value output |
| `FlowDecision`, `FlowSwitch`, `FlowFork`, `FlowJoin` | `If`, `Switch`, `Parallel` | flow-activity model removed |
| per-variable storage drivers | single durable-value driver | `elsa.json`; see `e2e-tests/README.md` |
| Elsa 3 host JS functions | closed deterministic sandbox | deliberate — see §6 |

---

## 5. Elsa 4 activities with no Elsa 3 counterpart

`BpmnProcess`, `BpmnDecision` (BPMN engine), `Do` (do-while), `WriteLines`.

---

## 6. Beyond the activity surface

A contract diff scoped to activities structurally cannot see these, and they matter more than most rows
above for anyone porting real Elsa 3 workflows. Recorded here so the next reader does not mistake a
clean activity table for a clean migration.

- **The JavaScript sandbox nulls `Date`, `Temporal`, `Intl` and `Math.random`**
  (`IsolatedJintEngine.DisableAmbientCapabilities`). Time, randomness and locale must enter as pinned
  arguments. Elsa 3 additionally exposes ~30 ambient host globals plus lodash/moment, and `getSecret()`
  has no Elsa 4 counterpart. This is deliberate — it is what makes `binding-pure-v1` determinism hold —
  but it breaks ported expressions silently rather than at publish time.
- **Flowchart merge semantics.** Elsa 4 has nine policy kinds (`FlowchartPolicyKinds`) covering most of
  Elsa 3's `MergeMode`: `Race`→`FirstWins`, `Stream`→`ImplicitActivationJoin`,
  `Merge`/`Converge`→`ParallelJoin`/`InclusiveJoin`. Only **`Cascade`** (schedule the target once per
  arriving token, allowing concurrent executions) has no equivalent. Note Elsa 3's token flow is
  opt-in — `Flowchart.UseTokenFlow` defaults to `false`.
- **No instance-cancellation REST endpoint**, so `DispatchWorkflow`'s `Cancelled` and `DispatchFailed`
  outcomes are unreachable over HTTP and are driven in-process only.
- **Feature composition.** Liquid (`PortableLiquidExpressionHandler`) and `ActivitiesScripting` are
  implemented but not composed in the default Workbench shell. A host that does not enable them looks
  like it is missing features it has.
- **C# and Python expressions are absent.** Elsa 3 ships both.

---

## 7. Current Elsa 4 inventory

Required inputs in **bold**; `(Done)` means no outcome is declared, so only the implicit `Done` exists.

| Activity | Module | Trigger | Inputs | Outputs | Outcomes |
|---|---|---|---|---|---|
| `BpmnDecision` | Bpmn | | Outcome | — | (Done) |
| `BpmnProcess` | Bpmn | yes | CanStartWorkflow | — | Done |
| `Do` | ControlFlow | | Condition | — | (Done) |
| `For` | ControlFlow | | Start, End, Step, EndInclusive | — | (Done) |
| `ForEach` | ControlFlow | | Collection | — | (Done) |
| `If` | ControlFlow | | Condition | — | True, False, Break |
| `Parallel` | ControlFlow | | — | — | (Done) |
| `Switch` | ControlFlow | | Value | — | Default, Break + dynamic case ports |
| `While` | ControlFlow | | Condition | — | (Done) |
| `DispatchWorkflow` | DispatchWorkflow | | **WorkflowDefinitionId**, Inputs, WaitForCompletion, CancelChildOnParentCancellation, CorrelationId | ChildWorkflowExecutionId, Result | Dispatched, Completed, Faulted, Cancelled, DispatchFailed |
| `Flowchart` | Flowchart | | — | — | Done, Break |
| `GraphActivity` | Graph | | — | — | (Done) + authored mapped ports |
| `HttpEndpoint` | Http | yes | **Path**, SupportedMethods, CanStartWorkflow, Authorize, Policy, RequestTimeout, RequestSizeLimit, ResponseMode | Request, RouteData, ParsedContent | (Done) |
| `SendHttpRequest` | Http | | **Url**, Method, Content, ContentType, RequestHeaders, ExpectedStatusCodes, Timeout | StatusCode, ResponseBody, ResponseHeaders | Done, Failed, Timeout + dynamic from ExpectedStatusCodes |
| `WriteHttpResponse` | Http | | StatusCode, Body, ContentType, Headers | StatusCode, Headers, Body, ContentType | (Done) |
| `Break` | Primitives | | — | — | Break |
| `Event` | Primitives | yes | **EventName**, CorrelationId, CanStartWorkflow | EventName | (Done) |
| `Fault` | Primitives | | Message, Code, Category, FaultType | — | none (terminal) |
| `Inline` | Primitives | | Expression | — | (Done) |
| `PublishEvent` | Primitives | | **EventName**, CorrelationId, Payload, IsLocalEvent | — | Done |
| `ReadLine` | Primitives | | — | Line | (Done) |
| `WriteLine` | Primitives | | **Text** | — | Done |
| `WriteLines` | Primitives | | Lines | — | (Done) |
| `Cron` | Scheduling | yes | **Expression** | Expression | (Done) |
| `Delay` | Scheduling | | Duration | — | Done |
| `Timer` | Scheduling | yes | **Interval** | Interval | (Done) |
| `RunJavaScript` | Scripting | | **Script**, Arguments, PossibleOutcomes | Value | dynamic from PossibleOutcomes + `Unmatched` |
| `Sequence` | Sequence | | — | — | Done, Break |

---

## Filed issues

| Issue | Covers | State |
|---|---|---|
| [#1113](https://github.com/elsa-workflows/elsa-foundation/issues/1113) | Authoring catalog exposed only 2 of 9 intrinsics | closed — §1 |
| [#1114](https://github.com/elsa-workflows/elsa-foundation/issues/1114) | `RunJavaScript` has no outcomes | closed |
| [#1115](https://github.com/elsa-workflows/elsa-foundation/issues/1115) | `HttpEndpoint` declares no outcome ports | open — §2 |
| [#1116](https://github.com/elsa-workflows/elsa-foundation/issues/1116) | `HttpEndpoint` file-upload surface + the two missing HTTP file activities | open — §2, §3 |
| [#1117](https://github.com/elsa-workflows/elsa-foundation/issues/1117) | Member-level gaps; `Fault` and `PublishEvent` rows are now closed, `SendHttpRequest`/`DispatchWorkflow`/`Switch` remain | open — §2 |
| [#1118](https://github.com/elsa-workflows/elsa-foundation/issues/1118) | Tracking: Elsa 3 activities with no Elsa 4 counterpart | open — §3 |
| [#1119](https://github.com/elsa-workflows/elsa-foundation/issues/1119) | Snapshot guard + behavioural test drive | closed |

## Routing

Findings here are evidence. Work that gets planned should move to the
[Code Reality And Test Maturity](../program-goals/code-reality-and-test-maturity.md) bucket per
`AGENTS.md`. Note there is currently **no program-goal bucket for Elsa 3 → Elsa 4 parity**, and the
open parity issues (#1115–#1118) all sit in `needs-triage` — so nothing in §2, §3 or §6 is queued.
