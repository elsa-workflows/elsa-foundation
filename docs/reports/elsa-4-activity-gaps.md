# Elsa 3 → Elsa 4 — Activities Not Yet Ported

> Small inventory focused on **activities** (the workflow building blocks), not modules/infra.
>
> - **Elsa 4 column** is verified against this repo (`src/Elsa/Activities` on `main`).
> - **Elsa 3 column** is the canonical activity library from the sources reviewed in `elsa-4-gap-analysis.md` (elsa-core 3.7/3.8 + elsa-extensions 3.7).
> - This is a superset view of gap-analysis §3 and §4, narrowed to concrete activity types.

## What Elsa 4 actually has today

> **Update (2026-06-29):** the **entire Section-1 control-flow + composite + leaf activity library is now ported & merged** under PRD #255, and the Section-1 acceptance gate (#269) is satisfied: a single workflow combining `If` + `ForEach` + `SetVariable` runs to `Completed` through the real `Elsa.Activities.Testing` `WorkflowExecutionHarness` (in-process agent + scheduler + Jint JS evaluation) **and** through the server-level `ExecuteWorkflowRequestHandler` → dispatcher → drain path. See `tests/Elsa/Activities/Runtime/Tests/ActivityLibraryAcceptanceTests.cs`. The earlier "only four activities exist" framing below is superseded; Sections 2–4 (StateMachine, sub-workflow, timing/triggers, integrations) remain genuinely missing.

The structural composites and primitive that existed at the start of the effort:

| Activity | Kind | Status |
|---|---|---|
| `WriteLine` | primitive | ✅ working (`Activities/Primitives/Activities/WriteLine.cs`) |
| `Flowchart` | structural composite | ✅ working — executes end-to-end on the in-process path (fork/join/decision/merge policies) |
| `Sequence` | structural composite | ✅ working (`Activities/Sequence/Activities/Sequence.cs`) |
| `WorkflowDefinitionActivity` | composition (sub-workflow) | ⛔ stub — `Execute` throws `NotSupportedException` (gap-analysis 1.10) |

…have since been joined by the full Section-1 control-flow, composite, and data-leaf activity set (see the table in §1, each row links its merged issue). The supporting test harness (#258) and the Seam-C variable write-back / caller-supplied inputs keystone (#286) are also merged.

Everything else under `src/Elsa/Activities` is infrastructure (Design API, reconciliation, runtime core, constructors, scheduler handlers), **not** runnable activities.

---

## 1. Control-flow & primitive activities — *core, belong in elsa-foundation*

These were the highest-priority gaps. **All of them are now ported & merged** (PRD #255); the runtime engine already supported the scopes they need (iteration via `IterationId`, parallel branches via `BranchId`), and the activities themselves have landed. The composition of `If` + `ForEach` + `SetVariable` is proven end-to-end by the Section-1 acceptance gate (#269).

| Elsa 3 activity | Purpose | Elsa 4 |
|---|---|---|
| `If` | boolean branch | ✅ ported (#257, `Elsa.Activities.If`) |
| `Switch` / `FlowSwitch` | multi-way branch on expression | ✅ ported (#263, `Elsa.Activities.Switch`) |
| `ForEach` | iterate a collection | ✅ ported (#264, `Elsa.Activities.ForEach`) |
| `For` | counted loop | ✅ ported (#265, `Elsa.Activities.For`) |
| `While` / `Do`(While) | conditional loop | ✅ ported (`While` #266, `Do`/DoWhile #267) |
| `Parallel` (Fork + Join) | concurrent branches, join/await | ✅ ported (#268, `Elsa.Activities.Parallel`) |
| `Break` / `Complete` / `Finish` | early loop/workflow exit | ✅ ported (`Break` #299; `Finish`/`Complete` #292, with terminal-status drainer guard #293) |
| `Fault` / `Throw` | raise a fault/incident | ✅ ported (#257, `Fault` leaf alongside `If`) |
| `SetVariable` / `SetVariables` | assign workflow variables | ✅ ported (#260, `Elsa.Activities.Primitives`; durable via #286 write-back) |
| `SetName` / `SetOutput` | set instance name / workflow output | ✅ ported (#260; `SetInstanceName`/`SetWorkflowOutput` runtime intents) |
| `Correlate` | set correlation id | ✅ ported (#292, leaf alongside `Finish`) |
| `Inline` / `RunInline` | inline C# code activity | ✅ ported (#262, `Inline` leaf) |
| `WriteLines` / `ReadLine` | console I/O primitives | ✅ ported (#262, `WriteLines` + `ReadLine` alongside the existing `WriteLine`) |

## 2. Composite workflow shapes — *core*

| Elsa 3 activity | Purpose | Elsa 4 |
|---|---|---|
| `Sequence` | run children in order | ✅ present |
| `Flowchart` | graph of connected activities | ✅ present |
| `StateMachine` | states + event-driven transitions | ❌ missing (gap-analysis 3.1) |
| Sub-workflow (`WorkflowDefinitionActivity` / `DispatchWorkflow` / `RunWorkflow`) | run a referenced workflow as an activity | ⛔ stub (1.10) |

## 3. Timing / triggers / events — *core (engine-adjacent)*

| Elsa 3 activity | Purpose | Elsa 4 |
|---|---|---|
| `Delay` | suspend for a duration | ❌ missing |
| `Timer` | recurring interval trigger | ❌ missing |
| `Cron` | cron-schedule trigger | ❌ missing |
| `StartAt` | run at an absolute time | ❌ missing |
| `Event` / `PublishSignal` / signal-received | generic event wait / signal | ❌ missing (bookmark/resume *infra* exists; no author-facing event activities) |

> Note: the bookmark + `ResumeTarget` machinery these depend on **is** implemented (gap-analysis 1.5). The activities that create those bookmarks are not.

## 4. Built-in I/O & integration activities — *will live in a separate extensions repo*

Per the gap-analysis priority notes, these are expected to land in a future `elsa-foundation-extensions` workspace, **not** in `elsa-foundation` itself. Listed for completeness; none exist in Elsa 4 today.

| Area (Elsa 3 source) | Representative activities | Elsa 4 |
|---|---|---|
| HTTP (`Elsa.Http`) | `HttpEndpoint`, `SendHttpRequest`, `WriteHttpResponse`, `HttpRedirect` | ❌ (HTTP module exists; activities don't) |
| Email (`Elsa.Email`) | `SendEmail` | ❌ |
| Messaging (`Elsa.MassTransit`, Kafka, Azure SB) | `SendMessage`, `PublishMessage`, `ReceiveMessage` | ❌ |
| SQL (`elsa-extensions`) | execute query / non-query | ❌ |
| File / IO | read/write file, watch directory | ❌ |
| CSV | read/write CSV, bind rows | ❌ |
| Compression | GZip/Zip compress/decompress | ❌ |
| Command line | run shell command | ❌ |
| Slack / Telnyx / GitHub / Azure Storage | extension connectors | ❌ |

---

## Priority read

1. **Section 1 (control-flow primitives) is done.** `If`, `Switch`, `ForEach`, `For`, `While`, `Do`, `Parallel`, `Break`, `Finish`/`Complete`, `Fault`, `Correlate`, `Inline`, `WriteLines`, `ReadLine`, and `SetVariable`/`SetVariables`/`SetName`/`SetOutput` are all ported & merged, and their composition is proven by the #269 acceptance gate.
2. **Sub-workflow (1.10)** and **StateMachine (3.1)** are the two remaining composite gaps; sub-workflow is a known stub on the critical path.
3. **Section 3 (timing/triggers)** and **Section 4 (integrations)** are still missing — Section 4 is deliberately out of scope for this repo and tracked for the extensions workspace.

> Resolved caveat: the earlier runtime review noted server-side execution was blocked by the Groundwork checkpoint writer dropping post-commit intents (runs stalled at `Running`/0 activities). That outbox/write-back seam is now fixed (Seam-C keystone #286, building on #254), and the #269 acceptance gate exercises a composed workflow through the server-level `ExecuteWorkflowRequestHandler` path to `Completed`.
