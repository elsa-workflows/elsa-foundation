# GA test drive: Core Preview map

24 November 2026, `main` at `1980bbe7e6`. Read from the source only; nothing was run. An independent check of the
claims against the source, ADR 0099 and #4229 corrected the first draft.

The test drive is seven HTTP-driven workflows (W1–W7), built in the designer and then exercised through management
and runtime checks. This map says, for each step, what the Core Preview needs before the step can run, and which
release 1 milestone on [#4229](https://github.com/elsa-workflows/elsa-foundation/issues/4229) delivers it.

## Summary

- **None of the seven workflows can run on the Core Preview today.** The Workbench preview registers only
  `direct-approval` workflows built in C#: one by default, up to five with the job settings
  ([WorkbenchCorePreview.cs](../../../apps/Elsa.Workbench/CorePreview/WorkbenchCorePreview.cs)). Its only HTTP route
  is a read-only instance list. The kernel README says: "Existing workflow and Studio endpoints still use their
  current runtime."
- **The Native model is further along than the preview shows.** `Elsa.Workflows.Runtime.Native.Model` already has
  Sequence, If, Parallel, ForEach, While, Flowchart (on the graph engine), DispatchWorkflow, HttpEndpoint (as a start
  and as a mid-flow wait), WriteHttpResponse, SendHttpRequest (with an effect key), Delay, Event, PublishEvent and
  SetVariable. Containers end on a `Break` outcome, and a leaf can return a fault. Incidents, retry, migration plans
  and a retention pass are in code.
- **Three gaps are stated as intent but have no acceptance case.** They are the main result of this map:
  1. **Designer definitions on the kernel.** ADR 0099 says "the activity library and definitions carry over with an
     adapted activity context" and that "the route table is built from publications". K6 carries the adapted
     activity context, and K10 asks for "composed Studio journeys". No acceptance case says: a workflow built in the
     designer and published through the definitions API starts from its HTTP route and runs on the kernel. Today
     nothing does this; `Wf` in `Build.cs` is "Definitions in code: what a designer would produce, without the
     designer."
  2. **The activity library through the adapted context.** The designer's toolbox has Switch, For, Fault, Complete
     and RunJavaScript, which the Native model lacks (NATIVE.md lists Switch, For and Do as not done). ADR 0099 plans
     to carry them over through K6's adapted context, not to rewrite each one. No K6 case names which designer
     activities must run on the kernel. Compensation is out of release 1 by owner decision (StateMachine page,
     decision 4), so the designer has to hide CompensationScope and Compensate on a kernel host. JavaScript carries
     over under K12 item 3.
  3. **The HTTP surface of the Native activities.**
     - The request is `(Method, Path, Body)`: no query string, headers or route values. The response is
       `(StatusCode, ContentType, Body)`: no headers.
     - Routes match literal paths only, so `/orders/{id}` cannot be expressed.
     - A start route registers only when the HttpEndpoint is the root or first in a root Sequence, so a Flowchart
       workflow, which is what the designer produces, gets no start route (`HttpRoutes.cs` lines 21–22).
     - A second workflow on the same route replaces the first without an error (`_starts[key] = ...`).
     - `SendHttpRequest` takes only Url, Method and Body. The workflow never receives the response, and the
       Workbench preview passes no outbound sender, so the request is never sent.
     K8 covers delivery, the reply transport and timeouts, not these authoring features.

Without these cases, K13 could pass with every gate green while a designer user still cannot build W1. The
program plan now carries one case for each gap: gap 2 in the K6 row, gap 3 in the K8 row and gap 1 in the K10 row.
How route values reach the request, whether `SendHttpRequest` returns its response, and which activities make the
release 1 list are owner decisions still to come.

## Status of the milestones this map relies on

| Milestone | State on 24 November |
|---|---|
| K4 providers | In progress; SQL Server, MySQL and SQLite are critical-path step 3 |
| K5 profiles | Not accepted; #5579 graceful stop is still a draft |
| K6 native model, K7 graph engine, Flowchart and StateMachine | Not accepted. Native and graph code exists on main; product wiring is open |
| K8 synchronous HTTP | Not accepted. Kestrel edge checks run in tests (`--public-http-edge-check`); the Workbench has no native route |
| K9 read models | Not accepted. The preview list endpoint exists: PostgreSQL only, 25 rows a page, no Studio view |
| K10 Studio on the projector | Not started |
| K11 operations | Not accepted. Critical-path step 1, the PostgreSQL Park line, is at arrivals (#5661); migration plans, retention, incidents and retry exist in code |

## Workflows

✅ in the Native model today · 🟡 partly there · ❌ missing · **No case** = stated intent, no acceptance case ·
**Out** = outside release 1 by owner decision

| Workflow | Step | Native model today | Missing | Milestone |
|---|---|---|---|---|
| All | Designed in Studio, published, runs | ❌ | Publications to the kernel, with their routes | K6, K10 (**No case**) |
| All | HTTP request reaches the kernel in Workbench | 🟡 test-only Kestrel edge | A shipped route host in the Workbench | K8 |
| All | Flowchart root starts from its HttpEndpoint | ❌ only a root or first-in-Sequence endpoint registers | Start routes found inside a Flowchart | **No case** |
| W1 `GET /hello?name=` | Read a query parameter | ❌ | Query string on `HttpRequest` | **No case** |
| W1 | Expression builds the greeting | 🟡 built-in language | JavaScript, as the designer offers it | K12 item 3 |
| W2 `POST /orders` | Read the JSON body | ✅ | | |
| W2 | Branch with `If` | ✅ | | K6 |
| W2 | Branch with `Switch` | ❌ | Switch through the adapted context | K6 (**No case**) |
| W2 | 201/400 with headers | 🟡 status and content type | Response headers | **No case** |
| W3 `/orders/{id}/enrich` | Route value `{id}` | ❌ | Route templates in `HttpRoutes` | **No case** |
| W3 | Parallel calls, join | ✅ Parallel, Flowchart joins | | K6, K7 |
| W3 | Read the downstream response, react to a 500 | ❌ | A response result; a shipped HTTP sender | **No case** (K6 wires only the effect key) |
| W3 | Timeout on the call | ❌ | Timeout input | **No case** |
| W4 `/orders/{id}/ship` | Respond 202, then continue | ✅ WriteHttpResponse does not end the workflow | | K8 |
| W4 | `Delay` 2 min, survive restart | ✅ timer as message to self; restart proven for an approval wait | Proof on a designed workflow | K5, K6 |
| W4 | Child via `DispatchWorkflow` | ✅ | Parked-child registry | K6 |
| W5 `/orders/{id}/cancel` | Resume the waiting W4 | 🟡 Event and mid-flow HttpEndpoint exist; no correlation input, so an event name per order is the workaround | Route values or a correlation key | K8 (**No case** for route values) |
| W6 `POST /batch` | ForEach over 1,000 items into children | ✅ ForEach, DispatchWorkflow | Fan-out limits on a provider | K6 (large fan-out decision 5), K12 |
| W7 payment | `Fault` raises an incident | 🟡 thrown errors and leaf faults become incidents | A Fault activity in the toolbox | K6 (**No case**) |
| W7 | Compensation | ❌ | | **Out** (release 1 has no compensation port) |

## Designer pass

| Check | Core Preview | Milestone |
|---|---|---|
| Build, validate and lay out a workflow | Core-independent; works today on the old core | n/a |
| Test-run from the designer | ❌ no path from a draft to the kernel | K10 (**No case**) |
| Toolbox shows only what the kernel runs | ❌ the toolbox lists activities the kernel lacks or will not have (compensation) | **No case** |
| Expression editor | 🟡 JavaScript in the designer, built-in language in the kernel | K12 item 3 |

## Management pass

| Check | Core Preview | Milestone |
|---|---|---|
| Publish v2 while v1 instances wait | ❌ blocked by gap 1; the kernel keeps a model state version per instance | K6/K10 (**No case**), then K11 |
| Migrate an instance to v2 | 🟡 migration plans in `WorkflowActor` | K11 |
| Instance list with filters | 🟡 `GET /runtime/core-preview/workflows/instances`, filters on definition and status | K9, K10 |
| Timeline and journal view | ❌ in Studio | K10 (tail read, catch-up) |
| Incident view and retry | 🟡 incidents and retry in the model | K11 (parked-work inspect, retry, discard), K10 |
| Cancel or terminate | 🟡 terminate exists in the kernel; terminate of a parked root comes later in critical-path step 1 | K11 |
| Delete and retention | 🟡 retention pass in code | K11 |
| Export, import, copy, rename definitions | Core-independent, but what is exported cannot run on the kernel | K6/K10 (**No case**) |

## Runtime pass

| Check | Core Preview | Milestone |
|---|---|---|
| Restart the host during a wait | ✅ for the built-in approval workflow | K5 for designed workflows |
| Kill the host while a request waits | 🟡 503 and 504 cases in the edge checks | K8 |
| About 200 requests a second on W1 | ❌ no admission or route host | K11 (admission), K12 (budgets) |
| Duplicate POST | 🟡 replay dedup by event id proven; HTTP start dedup needs a caller key | K8 |
| Large request body | ❌ bodies sit in state; bodies by reference not done | K11 (values by reference) |
| Two workflows on one route | ❌ the later one silently wins | **No case** |
| 404 and 405 | 🟡 an unmatched event returns 404 in the edge check; no 405 | K8 |
| Suspends before responding | ✅ 500 when a held response's turn parks; 504 at the reply deadline | K8 |
| SQLite Embedded, then PostgreSQL with two nodes | 🟡 PostgreSQL only in the preview; Cluster demo exists separately | K4, K5 |

## Sources

- [apps/Elsa.Workbench/CorePreview/](../../../apps/Elsa.Workbench/CorePreview/)
- [essentials/src/Workflows/Runtime/Native/Model/Activities.cs](../../../essentials/src/Workflows/Runtime/Native/Model/Activities.cs),
  [HttpRoutes.cs](../../../essentials/src/Workflows/Runtime/Native/Model/HttpRoutes.cs),
  [Expression.cs](../../../essentials/src/Workflows/Runtime/Native/Model/Expression.cs),
  [Build.cs](../../../essentials/src/Workflows/Runtime/Native/Model/Build.cs)
- [essentials/src/Workflows/Runtime/Native/Activities/Activities.cs](../../../essentials/src/Workflows/Runtime/Native/Activities/Activities.cs)
- [ADR 0099](../../adr/0099-clean-slate-execution-kernel.md) (clean break; ADR 0043 kept)
- [StateMachine on the kernel](../../architecture/statemachine-on-the-kernel.md), owner decision 4 (no compensation
  port in release 1)
- [prototypes/kernel-model/NATIVE.md](../../../prototypes/kernel-model/NATIVE.md) section 4 and "not done" list;
  [README.md](../../../prototypes/kernel-model/README.md#workbench-opt-in)
- [docs/program-goals/clean-slate-kernel.md](../../program-goals/clean-slate-kernel.md) milestones and release 1
  qualification
