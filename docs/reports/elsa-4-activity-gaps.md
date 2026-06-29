# Elsa 3 → Elsa 4 — Activities Not Yet Ported

> Small inventory focused on **activities** (the workflow building blocks), not modules/infra.
>
> - **Elsa 4 column** is verified against this repo (`src/Elsa/Activities` on `main`).
> - **Elsa 3 column** is the canonical activity library from the sources reviewed in `elsa-4-gap-analysis.md` (elsa-core 3.7/3.8 + elsa-extensions 3.7).
> - This is a superset view of gap-analysis §3 and §4, narrowed to concrete activity types.

## What Elsa 4 actually has today

Only **four** concrete activity types exist in `elsa-foundation`, and one is a stub:

| Activity | Kind | Status |
|---|---|---|
| `WriteLine` | primitive | ✅ working (`Activities/Primitives/Activities/WriteLine.cs`) |
| `Flowchart` | structural composite | ✅ working — executes end-to-end on the in-process path (fork/join/decision/merge policies) |
| `Sequence` | structural composite | ✅ working (`Activities/Sequence/Activities/Sequence.cs`) |
| `WorkflowDefinitionActivity` | composition (sub-workflow) | ⛔ stub — `Execute` throws `NotSupportedException` (gap-analysis 1.10) |

Everything else under `src/Elsa/Activities` is infrastructure (Design API, reconciliation, runtime core, constructors, scheduler handlers), **not** runnable activities.

---

## 1. Control-flow & primitive activities — *core, belong in elsa-foundation*

These are the highest-priority gaps: without them you cannot author non-trivial workflows. The runtime engine already supports the scopes they need (iteration via `IterationId`, parallel branches via `BranchId`), so the blocker is the activities themselves, not the engine.

| Elsa 3 activity | Purpose | Elsa 4 |
|---|---|---|
| `If` | boolean branch | ❌ missing |
| `Switch` / `FlowSwitch` | multi-way branch on expression | ❌ missing (decision *policy* exists for flowcharts, no `Switch` activity) |
| `ForEach` | iterate a collection | ❌ missing |
| `For` | counted loop | ❌ missing |
| `While` / `Do`(While) | conditional loop | ❌ missing |
| `Parallel` (Fork + Join) | concurrent branches, join/await | ❌ missing (flowchart has fork/join *policies*; no standalone activity) |
| `Break` / `Complete` / `Finish` | early loop/workflow exit | ❌ missing |
| `Fault` / `Throw` | raise a fault/incident | ❌ missing |
| `SetVariable` / `SetVariables` | assign workflow variables | ❌ missing |
| `SetName` / `SetOutput` | set instance name / workflow output | ❌ missing |
| `Correlate` | set correlation id | ❌ missing |
| `Inline` / `RunInline` | inline C# code activity | ❌ missing |
| `WriteLines` / `ReadLine` | console I/O primitives | ❌ missing (only single `WriteLine` exists) |

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

1. **Section 1 (control-flow primitives)** is the real unblock for authoring — `If`, `ForEach`, `While`, `Switch`, `SetVariable`, `Fault`, `Parallel`. These are small activities and the engine already supports their execution scopes.
2. **Sub-workflow (1.10)** and **StateMachine (3.1)** are the two composite gaps; sub-workflow is a known stub on the critical path.
3. **Section 4** is deliberately out of scope for this repo and tracked for the extensions workspace.

> Caveat carried from the runtime review: even once these activities exist, server-side execution is currently blocked by the Groundwork checkpoint writer dropping post-commit intents (runs stall at `Running`/0 activities). In-process execution works. See `docs/reports/elsa-4-gap-analysis.md` §1 and the related memory.
