# Elsa 3 → Elsa 4 — activity contract parity audit (2026-07)

> **Scope.** Every out-of-the-box activity in `src/Elsa/Activities`, diffed member-by-member against its
> Elsa 3 counterpart in `elsa-core`, plus the engine intrinsics that replaced Elsa 3 activities.
> The question this answers: *which activities are missing inputs, missing outputs, missing outcomes, or
> missing entirely?*
>
> **Supersedes** [`elsa-4-activity-gaps.md`](elsa-4-activity-gaps.md), which is stale — it lists the HTTP
> activities, `Delay`, `Timer` and `Cron` as missing; all four exist today.
>
> **Evidence.** [`evidence/activity-contract-parity/`](evidence/activity-contract-parity/) — the extracted
> Elsa 3 and Elsa 4 surfaces and the machine-readable findings. Regenerate with
> [`tools/parity/`](../../tools/parity/README.md).
>
> **Baselines.** Elsa 4 = this repo at `acc00611`. Elsa 3 = `elsa-workflows/elsa-core` at
> `5b0c2d7359ecc5bb8e77603dab43e90a6608b195`, modules `Elsa.Workflows.Core`, `Elsa.Http`,
> `Elsa.Scheduling`, `Elsa.Expressions.JavaScript`, `Elsa.Workflows.Runtime`. Elsa 3 integration
> connectors (Email, MassTransit, SQL, CSV, file IO, Slack, …) are excluded: they are tracked for a
> separate extensions workspace, not for `elsa-foundation`.

## Confidence and what is *not* claimed

This audit is a **declared-contract diff read from source**, not a behavioural test drive. Contracts in
both codebases are attribute-declared, so the diff is faithful about *what an activity exposes*. It does
**not** prove that a declared outcome is reachable at runtime or that a declared output is ever
populated.

The planned second half — driving every activity through a real workflow on `WorkflowExecutionHarness`
and through the REST e2e path — **could not be executed here**: the repo's projects all reference
`CShells`/`Nuplane`/`Groundwork` packages from `f.feedz.io`, which this environment's network policy
blocks, so `dotnet restore` fails for every project (`NU1301`) and nothing can be built or run. See
[Blocked work](#blocked-work).

Findings below are labelled `verified-in-source` (both sides read directly) or `mechanical` (from the
extractor only).

---

## Headline

| | Count |
|---|---|
| Elsa 4 activities today | 28 |
| Activities at full contract parity | 17 |
| Activities with a member-level gap | 10 |
| Elsa 3 activities with no Elsa 4 counterpart | 7 |
| Elsa 3 activities that became engine intrinsics | 5 |

**The three findings that matter most:**

1. **Seven of the nine engine intrinsics are not in the authoring catalog** — so `Finish`, `Complete`,
   `Correlate` and `SetName` have no discoverable Elsa 4 replacement, even though the engine implements
   them. (§1)
2. **`RunJavaScript` lost configurable outcomes.** Elsa 3 has `PossibleOutcomes` + `setOutcome()`;
   Elsa 4's has no outcomes at all. This is the same gap class as the `SendHttpRequest` example that
   prompted this audit — except that one is already fixed. (§2.1)
3. **`HttpEndpoint` has no outcome ports and no file-upload surface.** Elsa 3 exposes four opt-in
   outcomes and multipart handling; Elsa 4 exposes neither, and its `RequestSizeLimit` input has no
   outcome to route to when it trips. (§2.2)

---

## 1. Engine intrinsics: implemented but not authorable

`verified-in-source`

ADR 0045 replaced several Elsa 3 activities with engine intrinsics. The engine supports **nine** kinds
(`src/Elsa/Workflows/Design/Core/Models/ActivityNode.cs`, `AuthoredWorkflowIntrinsicKind`):

`Set`, `Merge`, `Reduce`, `Return`, `Control`, `SetCorrelationId`, `SetInstanceName`, `SetOutput`, `Finish`

The authoring catalog publishes **two**. `IntrinsicAuthoringDescriptorProvider`
(`src/Elsa/Activities/Design/Api/Services/IntrinsicAuthoringDescriptorProvider.cs`) returns exactly
`SetVariableDescriptor()` and `SetOutputDescriptor()`, and it is the only
`IBuiltInAuthoringDescriptorProvider` registered (`ActivitiesDesignApiFeature.cs:96`).

The intrinsics *are* reachable — `e2e-tests/correlate/Test-Correlate.ps1` authors one by posting
`activityVersionId = "elsa.intrinsic.set-correlation-id@1"` directly, and publish accepts it. So this is
precisely: **the engine implements it, the REST API accepts it, the designer catalog does not offer it.**

| Elsa 3 activity | Elsa 4 intrinsic | In catalog? |
|---|---|---|
| `SetVariable` | `elsa.intrinsic.set@1` | yes |
| — | `elsa.intrinsic.set-output@1` | yes |
| `Correlate` | `elsa.intrinsic.set-correlation-id@1` | **no** |
| `SetName` | `elsa.intrinsic.set-instance-name@1` | **no** |
| `Finish` / `Complete` | `elsa.intrinsic.finish@1` | **no** |
| — | `elsa.intrinsic.return@1` | **no** |
| — | `elsa.intrinsic.control@1` | **no** |
| — | `elsa.intrinsic.merge@1` | **no** |
| — | `elsa.intrinsic.reduce@1` | **no** |

> Elsa 3's `Complete` also takes an `Outcomes` input (finish with named outcomes). Whether
> `elsa.intrinsic.finish@1` carries an equivalent needs checking when the descriptors are added.

---

## 2. Activities with member-level gaps

### 2.1 `RunJavaScript` — no configurable outcomes, no outcomes at all

`verified-in-source`

| | Elsa 3 | Elsa 4 |
|---|---|---|
| Inputs | `Script`, **`PossibleOutcomes`** | `Script`, `Arguments` |
| Outputs | `Result` | `Value` (renamed) |
| Outcomes | dynamic, from `PossibleOutcomes` | none (implicit `Done`) |

Elsa 3 (`src/modules/Elsa.Expressions.JavaScript/Activities/RunJavaScript/RunJavaScript.cs`) declares
`PossibleOutcomes` with `UIHint = InputUIHints.DynamicOutcomes` and exposes `setOutcome()` /
`setOutcomes()` to the script; the activity completes with whatever the script chose.

Elsa 4 (`src/Elsa/Activities/Scripting/Activities/RunJavaScript.cs`) has neither. A script can compute a
value but cannot route the workflow.

The mechanism to fix this already exists in the repo: `ActivityValueOutcomesAttribute`, exactly as
`SendHttpRequest` uses it for `ExpectedStatusCodes`. Elsa 4 also adds `Arguments`, which Elsa 3 lacks.

### 2.2 `HttpEndpoint` — no outcome ports, no file uploads

`verified-in-source`

| | Elsa 3 | Elsa 4 |
|---|---|---|
| Outcomes | `Request too large`, `File too large`, `Invalid file extension`, `Invalid file MIME type` (each opt-in via an `Expose…Outcome` toggle) | **none** |
| File inputs | `FileSizeLimit`, `AllowedFileExtensions`, `BlockedFileExtensions`, `AllowedMimeTypes` | **none** |
| File outputs | `Files`, `File` | **none** |
| Request data | `ParsedContent`, `RouteData`, `QueryStringData`, `Headers` | `Request`, `RouteData`, `ParsedContent` |

Two distinct gaps:

- **No outcome ports.** Elsa 4 declares `RequestSizeLimit` but has no outcome to branch to when it is
  exceeded, so an oversized request cannot be handled in-graph. The `Expose…Outcome` pattern in Elsa 3
  is the same "configurable outcomes" shape as §2.1.
- **No multipart/file-upload support** at all.

Not a gap: Elsa 3's separate `QueryStringData` and `Headers` outputs are carried by Elsa 4's `Request`
output (`HttpRequestModel.Query` / `.Headers`) — a deliberate consolidation into one atomic result.

Also worth noting for authoring ergonomics: Elsa 4's `Request` and `RouteData` outputs are both
non-optional, which `e2e-tests/README.md` records as forcing both to be bound.

### 2.3 `SendHttpRequest` — the status-code outcomes are done; auth and parsed content are not

`verified-in-source`

The motivating example for this audit is **already implemented**: Elsa 4's `SendHttpRequest` carries
`[ActivityValueOutcomes(nameof(ExpectedStatusCodes), UnmatchedOutcome = "Unmatched status code")]`, which
matches Elsa 3's `FlowSendHttpRequest` behaviour (per-status outcome ports plus a catch-all). Elsa 4 also
adds a `Timeout` input and a `Timeout` outcome that Elsa 3 lacks.

Remaining gaps:

| Elsa 3 member | Kind | Status in Elsa 4 |
|---|---|---|
| `Authorization` | input | missing — no way to set an Authorization header without hand-writing `RequestHeaders` |
| `DisableAuthorizationHeaderValidation` | input | missing |
| `ParsedContent` (`object?`) | output | Elsa 4 returns `ResponseBody` as a raw `string`; no parsed/typed projection |
| `Content` accepts `object`/`byte[]`/`Stream` | input type | Elsa 4's `Content` is `string` only |

Naming: Elsa 3's transport-failure outcome is `Failed to connect`; Elsa 4 calls it `Failed`. Cosmetic,
but it is a breaking rename for anyone porting a graph.

### 2.4 `Fault` — three of four inputs missing

`verified-in-source`

Elsa 3: `Code`, `Category`, `FaultType`, `Message`. Elsa 4: `Message` only
(`src/Elsa/Activities/Primitives/Activities/Fault.cs`). Elsa 4 faults cannot be classified or coded,
which affects anything that wants to route or report on fault type.

The cleanest small fix in this report.

### 2.5 `Switch` — no `Mode`, no matched-value output

`mechanical`, cross-checked in source

Elsa 3 `Switch` has a `Mode` input (`MatchFirst` / `MatchAny`) and an `Output<object> Output` carrying the
matched value. Elsa 4 `Switch` has `Value` plus case slots and `Default`/`Break` outcomes — no `Mode`.

`e2e-tests/README.md` already classifies `MatchAny` as an architectural gap tied to removing the
flow-activity model. That classification is worth revisiting: `Switch` is not a flow activity, and
"run every matching case" is a behaviour, not a graph-model artefact. Flagged as an open question rather
than a defect.

The missing `Output` is **not** a gap — Elsa 4 routes on case outcome ports instead.

### 2.6 `DispatchWorkflow` — no channel or trace control

`verified-in-source`

| Elsa 3 member | Status in Elsa 4 |
|---|---|
| `ChannelName` | missing — cannot target a dispatch channel |
| `StartNewTrace` | missing |
| `Input` | renamed to `Inputs` |

Elsa 4 adds `CancelChildOnParentCancellation` and a `ChildWorkflowExecutionId` output, and exposes a much
richer outcome set (`Dispatched`, `Completed`, `Faulted`, `Cancelled`, `DispatchFailed`) than Elsa 3.
Elsa 3's separate `ExecuteWorkflow` activity is subsumed by `WaitForCompletion`.

### 2.7 `PublishEvent` — no `IsLocalEvent`

`verified-in-source`

Elsa 3 can scope an event to the local instance. Elsa 4 always publishes through the stimulus stager.

### 2.8 Renames that will break ported graphs

`mechanical`

| Activity | Elsa 3 | Elsa 4 |
|---|---|---|
| `Delay` | `TimeSpan` | `Duration` |
| `Cron` | `CronExpression` | `Expression` |
| `ForEach` | `Items` | `Collection` |
| `For` | `OuterBoundInclusive` | `EndInclusive` |
| `WriteHttpResponse` | `Content`, `ResponseHeaders` | `Body`, `Headers` |
| `DispatchWorkflow` | `Input` | `Inputs` |
| `RunJavaScript` | `Result` | `Value` |
| `SendHttpRequest` | `ParsedContent` | `ResponseBody` (and a type change) |

**`For` also changed a default**: Elsa 3's `OuterBoundInclusive` defaults to `true`; Elsa 4's
`EndInclusive` defaults to `false`. A ported loop silently runs one fewer iteration.

---

## 3. Elsa 3 activities with no Elsa 4 counterpart

`mechanical`

| Elsa 3 activity | Module | Note |
|---|---|---|
| `StartAt` | Scheduling | absolute-time trigger; `Delay`/`Timer`/`Cron` exist, this does not |
| `StateMachine` | Workflows.Core | states + event-driven transitions; already tracked as a known gap |
| `ParallelForEach` | Workflows.Core | `ForEach` and `Parallel` exist separately; no concurrent iteration |
| `RunTask` | Workflows.Runtime | external task/callback pattern |
| `BulkDispatchWorkflows` | Workflows.Runtime | dispatch one child per item, with `Completed`/`Canceled` outcomes |
| `DownloadHttpFile` | Http | pairs with the missing `HttpEndpoint` file surface (§2.2) |
| `WriteFileHttpResponse` | Http | file/stream responses with resumable download support |

Excluded as engine internals rather than toolbox activities: `Workflow`, `CompositeWithResult`,
`NotFoundActivity`, `Start`, `End`.

---

## 4. Intentional divergences — not gaps

`verified-in-source`

| Elsa 3 | Elsa 4 | Why it is not a gap |
|---|---|---|
| `ForEach.CurrentValue` output | `currentItem` scoped variable | `ForEach.CurrentItemVariableName`; `currentIndex` too |
| `For.CurrentValue` output | `index` scoped variable | `For.IndexVariableName` |
| `HttpEndpoint.Headers` / `.QueryStringData` | `Request` output | `HttpRequestModel.Headers` / `.Query` |
| `FlowFork.Branches` input | Parallel child slots | branches are authored structure, not an input |
| `Switch.Output` | case outcome ports | routing replaces the matched-value output |
| `FlowDecision`, `FlowSwitch`, `FlowFork`, `FlowJoin` | `If`, `Switch`, `Parallel` | flow-activity model removed |
| per-variable storage drivers | single durable-value driver | `elsa.json`; see `e2e-tests/README.md` |
| Elsa 3 host JS functions | closed deterministic sandbox | deliberate |

---

## 5. Elsa 4 activities with no Elsa 3 counterpart

`BpmnProcess`, `BpmnDecision` (BPMN engine), `Do` (do-while), `WriteLines`.

---

## 6. Current Elsa 4 inventory

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
| `Switch` | ControlFlow | | Value | — | Default, Break |
| `While` | ControlFlow | | Condition | — | (Done) |
| `DispatchWorkflow` | DispatchWorkflow | | **WorkflowDefinitionId**, Inputs, WaitForCompletion, CancelChildOnParentCancellation, CorrelationId | ChildWorkflowExecutionId, Result | Dispatched, Completed, Faulted, Cancelled, DispatchFailed |
| `Flowchart` | Flowchart | | — | — | Done, Break |
| `GraphActivity` | Graph | | — | — | (Done) |
| `HttpEndpoint` | Http | yes | **Path**, SupportedMethods, CanStartWorkflow, Authorize, Policy, RequestTimeout, RequestSizeLimit, ResponseMode | Request, RouteData, ParsedContent | (Done) |
| `SendHttpRequest` | Http | | **Url**, Method, Content, ContentType, RequestHeaders, ExpectedStatusCodes, Timeout | StatusCode, ResponseBody, ResponseHeaders | Done, Failed, Timeout + dynamic from ExpectedStatusCodes |
| `WriteHttpResponse` | Http | | StatusCode, Body, ContentType, Headers | StatusCode, Headers, Body, ContentType | (Done) |
| `Break` | Primitives | | — | — | (Done) |
| `Event` | Primitives | yes | **EventName**, CorrelationId, CanStartWorkflow | EventName | (Done) |
| `Fault` | Primitives | | Message | — | (Done) |
| `Inline` | Primitives | | Expression | — | (Done) |
| `PublishEvent` | Primitives | | **EventName**, CorrelationId, Payload | — | Done |
| `ReadLine` | Primitives | | — | Line | (Done) |
| `WriteLine` | Primitives | | **Text** | — | Done |
| `WriteLines` | Primitives | | Lines | — | (Done) |
| `Cron` | Scheduling | yes | **Expression** | Expression | (Done) |
| `Delay` | Scheduling | | Duration | — | Done |
| `Timer` | Scheduling | yes | **Interval** | Interval | (Done) |
| `RunJavaScript` | Scripting | | Script, Arguments | Value | (Done) |
| `Sequence` | Sequence | | — | — | Done, Break |

`PublishEvent` exists, contradicting the note in `e2e-tests/README.md` that Foundation has no
`PublishEvent` activity — that note is stale.

---

## 7. Behavioural half — what the drive found (2026-08)

The blocked work below has since been done in a local environment with full package access. See
[`elsa-4-activity-behavioural-drive-2026-08.md`](elsa-4-activity-behavioural-drive-2026-08.md) for the
run record; the headline is that driving the activities surfaced one defect a contract diff structurally
could not:

- **`Break` declared no outcomes at all.** It always completes with the `Break` outcome, but carried no
  `[ActivityOutcome]`, so the scanner emitted no outcomes facet, the studio fell back to its own `Done`
  default, and the designer showed a port that can never be taken while hiding the one that always is.
  Fixed.
- **`Fault` has an unreachable `Done` port** for the same reason in reverse: it declares no outcomes and
  never completes, so the studio's `Done` default is a port nothing can ever take. Recorded as a
  studio-side default rather than an activity defect.

## Blocked work

> **Superseded (2026-08).** Items 1, 2 and 4 are done; item 3 is not. See §7 and the
> [behavioural drive report](elsa-4-activity-behavioural-drive-2026-08.md).

The behavioural half of this audit was **not done**, and could not be started in the environment this
audit ran in.

Every project in the repo transitively references `CShells`, `Nuplane` or `Groundwork` packages served
from `f.feedz.io`. That host is unreachable through that environment's egress policy, so
`dotnet restore` fails with `NU1301` for every project — including a single leaf like
`src/Elsa/Activities/Http/Elsa.Activities.Http.csproj`. `api.nuget.org` is reachable; `f.feedz.io` is
not. Nothing could be compiled, so no test, no harness run and no server run was possible.

Still outstanding, in priority order:

1. ~~**Contract-surface snapshot guard.**~~ **Done** — `tests/Elsa/Activities/Design/Tests/Contracts/`.
2. ~~**In-process test drive** on `WorkflowExecutionHarness`.~~ **Done** —
   `tests/Elsa/Activities/Behavioral/`, with two documented gaps (`GraphActivity`, `DispatchWorkflow`).
3. **REST e2e test drive** for triggers, suspend/resume, dispatch, the `SendHttpRequest` dynamic ports,
   and the intrinsic authoring catalog (§1). **Still outstanding.**
4. ~~**Fixes.** §2.4 (`Fault` inputs), §2.7 (`PublishEvent.IsLocalEvent`) and §2.1 (`RunJavaScript`
   outcomes).~~ **Done**, plus §1 (three intrinsic descriptors) and §2.8 (`For.EndInclusive`, documented
   rather than changed).

## Filed issues

| Issue | Covers |
|---|---|
| [#1113](https://github.com/elsa-workflows/elsa-foundation/issues/1113) | Authoring catalog exposes only 2 of 9 intrinsics (§1) |
| [#1114](https://github.com/elsa-workflows/elsa-foundation/issues/1114) | `RunJavaScript` has no outcomes (§2.1) |
| [#1115](https://github.com/elsa-workflows/elsa-foundation/issues/1115) | `HttpEndpoint` declares no outcome ports (§2.2) |
| [#1116](https://github.com/elsa-workflows/elsa-foundation/issues/1116) | `HttpEndpoint` file-upload surface + the two missing HTTP file activities (§2.2, §3) |
| [#1117](https://github.com/elsa-workflows/elsa-foundation/issues/1117) | Member-level gaps on `Fault`, `SendHttpRequest`, `DispatchWorkflow`, `PublishEvent`, `Switch`, and the `For.EndInclusive` default change (§2.3–§2.8) |
| [#1118](https://github.com/elsa-workflows/elsa-foundation/issues/1118) | Tracking: Elsa 3 activities with no Elsa 4 counterpart (§3) |
| [#1119](https://github.com/elsa-workflows/elsa-foundation/issues/1119) | The blocked work: snapshot guard + behavioural test drive |

## Routing

Findings here are evidence. Work that gets planned should move to the
[Code Reality And Test Maturity](../program-goals/code-reality-and-test-maturity.md) bucket per
`AGENTS.md`.
