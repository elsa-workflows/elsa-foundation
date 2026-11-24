# GA test drive: Core Preview map

24 November 2026. The source was read at `main` `1980bbe7e6`, with rows marked where later merges changed them; nothing was run. Milestones follow the trimmed
release 1 scope the owner set the same day ([clean-slate kernel plan](../../program-goals/clean-slate-kernel.md#trimmed-scope-owner-24-november-2026)).
Independent checks of the claims against the source, ADR 0099 and #4229 corrected the drafts.

The test drive is seven HTTP-driven workflows (W1–W7), built in the designer and then exercised through management
and runtime checks. This map says, for each step, what the Core Preview needs before the step can run, and which
release 1 milestone on [#4229](https://github.com/elsa-workflows/elsa-foundation/issues/4229) delivers it.

## Summary

- **None of the seven workflows can run on the Core Preview today.** The Workbench preview registers only
  `direct-approval` workflows built in C#: one by default, up to five with the job settings
  ([WorkbenchCorePreview.cs](../../../apps/Elsa.Workbench.Host/CorePreview/WorkbenchCorePreview.cs)). Its only HTTP route
  is a read-only instance list. The kernel README says: "Existing workflow and Studio endpoints still use their
  current runtime."
- **The Native model is further along than the preview shows.** `Elsa.Workflows.Runtime.Native.Model` already has
  Sequence, If, Parallel, ForEach, While, Flowchart (on the graph engine), DispatchWorkflow, HttpEndpoint (as a start
  and as a mid-flow wait) and WriteHttpResponse. `Elsa.Workflows.Runtime.Native.Activities` has SendHttpRequest (with
  an effect key), Delay, Event, PublishEvent and SetVariable. Containers end on a `Break` outcome, and a leaf can return a fault. Incidents, retry, migration plans
  and a retention pass are in code.
- **Three gaps were stated as intent but had no acceptance case on 24 November.** They are the main result of this map:
  1. **Designer definitions on the kernel.** ADR 0099 says "the activity library and definitions carry over with an
     adapted activity context" and that "the route table is built from publications". K6 carries the adapted
     activity context, and K10 asks for "composed Studio journeys". No acceptance case says: a workflow built in the
     designer and published through the definitions API starts from its HTTP route and runs on the kernel. Today
     nothing does this; `Wf` in `Build.cs` is "Definitions in code: what a designer would produce, without the
     designer."
  2. **The activity library through the adapted context.** The designer's toolbox has Switch, For, Fault, Complete
     and RunJavaScript, which the Native model lacks (NATIVE.md lists Switch, For and Do as not done). ADR 0099 says
     the activity library carries over with an adapted activity context, which K6 delivers; the owner's card 3 calls
     each such activity a port. No K6 case names which designer activities must run on the kernel. Compensation is
     out of release 1 by owner decision (StateMachine page, decision 4), so CompensationScope and Compensate will not
     run on a kernel host. JavaScript carries
     over under K12 item 3.
  3. **The HTTP surface of the Native activities.**
     - The request was `(Method, Path, Body)`: no query string, headers or route values. The response was
       `(StatusCode, ContentType, Body)`: no headers. [#5706](https://github.com/elsa-workflows/elsa-foundation/pull/5706)
       has since added the query string and headers to the request and headers to the response.
     - Routes match literal paths only, so `/orders/{id}` cannot be expressed.
     - A start route registers only when the HttpEndpoint is the root or first in a root Sequence, so a Flowchart
       workflow, which is what the designer produces, gets no start route (`HttpRoutes.cs` lines 21–22).
     - A second workflow on the same route replaces the first without an error (the start-route assignment in `HttpRoutes.cs`, line 23).
     - `SendHttpRequest` takes only Url, Method and Body. The workflow never receives the response, and the
       Workbench preview passes no outbound sender, so the request is never sent.
     K8 covered delivery, the reply transport and timeouts, and none of these authoring features.

Without these cases, K13 could pass with every gate green while a designer user still cannot build W1. The
program plan now carries one case for each gap: gap 2 in the K6 row, gap 3 in the K8 row and gap 1 in the K10 row.
Route templates carry over from the old core, which matches `orders/{id}`. Later the same day the owner decided the
three open design cards ([#4229](https://github.com/elsa-workflows/elsa-foundation/issues/4229#issuecomment-7145147047),
design in [#5702](https://github.com/elsa-workflows/elsa-foundation/pull/5702)): route templates by resolved waits,
`SendHttpRequest` as a Background job, and 26 activities on the kernel with 8 hidden from the toolbox.

## Status of the milestones this map relies on

| Milestone | State on 24 November |
|---|---|
| K4 providers | In progress, on PostgreSQL and SQLite; SQL Server and MySQL move after the release |
| K5 profiles | Not accepted; #5579 graceful stop is still a draft |
| K6 native model, K7 Flowchart G1 to G5 | Not accepted. Native and graph code exists on main; product wiring is open. The StateMachine moves after the release |
| K8 synchronous HTTP | Not accepted. Kestrel edge checks run in tests (`--public-http-edge-check`); the Workbench has no native route |
| K9 read models | Not accepted. The preview list endpoint exists: PostgreSQL only, 25 rows a page, no Studio view |
| K10 Studio on the projector | Not started; its StateMachine journeys move after the release |
| K11 operations | Not accepted. Kept: admission, retention and delete, basic traces and metrics, parked-work inspection and the one-page recover. Moved after the release: the migration-plan runner, values by reference, Park discard and retry selection |

## Workflows

✅ in the Native model today · 🟡 partly there · ❌ missing · **No case** = no acceptance case in the plan, even with this map's three cases ·
**Out** = outside release 1 by owner decision

| Workflow | Step | Native model today | Missing | Milestone |
|---|---|---|---|---|
| All | Designed in Studio, published, runs | ❌ | Publications to the kernel, with their routes | K10 |
| All | HTTP request reaches the kernel in Workbench | 🟡 test-only Kestrel edge | A shipped route host in the Workbench | K10 |
| All | Flowchart root starts from its HttpEndpoint (built in code until K10) | ❌ only a root or first-in-Sequence endpoint registers | Start routes found inside a Flowchart | K8 |
| W1 `GET /hello?name=` | Read a query parameter | ✅ since #5706 | | K8 |
| W1 | Expression builds the greeting | 🟡 built-in language | JavaScript, as the designer offers it | K12 item 3 |
| W2 `POST /orders` | Read the JSON body | ✅ | | K8 |
| W2 | Branch with `If` | ✅ | | K6 |
| W2 | Branch with `Switch` | ❌ | Switch through the adapted context | K6 |
| W2 | 201/400 with headers | ✅ since #5706 | | K8 |
| W3 `/orders/{id}/enrich` | Route value `{id}` | ❌ | Route templates in `HttpRoutes` | K8 |
| W3 | Parallel calls, join | ✅ Parallel, Flowchart joins | | K6, K7 |
| W3 | Read the downstream response, react to a 500 | ❌ | A response result; a shipped HTTP sender | **No case**; the owner decided a Background job for K6 (card 2) |
| W3 | Timeout on the call | ❌ | Timeout input | **No case**; decided with card 2 for K6 |
| W4 `/orders/{id}/ship` | Respond 202, then continue | ✅ WriteHttpResponse does not end the workflow | | K8 |
| W4 | `Delay` 2 min, survive restart | ✅ timer as message to self; restart proven for an approval wait | Proof on a designed workflow | **No case** |
| W4 | Child via `DispatchWorkflow` | ✅ | Parked-child registry | K6 |
| W5 `/orders/{id}/cancel` | Resume the waiting W4 | 🟡 Event and mid-flow HttpEndpoint exist; no correlation input, so an event name per order is the workaround | Route values or a correlation key | K8 for route values; **No case** for resuming a wait on a template route, decided for K8 (card 1) |
| W6 `POST /batch` | ForEach over 1,000 items into children | ✅ ForEach, DispatchWorkflow | Fan-out limits on a provider | K6 (large fan-out decision 5), K12 |
| W7 payment | `Fault` raises an incident | 🟡 thrown errors and leaf faults become incidents | A Fault activity in the toolbox | K6 |
| W7 | Compensation | ❌ | | **Out** (release 1 has no compensation port) |

## Designer pass

| Check | Core Preview | Milestone |
|---|---|---|
| Build, validate and lay out a workflow | Core-independent; works today on the old core | n/a |
| Test-run from the designer | ❌ no path from a draft to the kernel | **No case** |
| Toolbox shows only what the kernel runs | ❌ the toolbox lists activities the kernel lacks or will not have (compensation) | **No case**; the owner decided on a kernel-unsupported set (#4229) |
| Expression editor | 🟡 JavaScript in the designer, built-in language in the kernel | K12 item 3 |

## Management pass

| Check | Core Preview | Milestone |
|---|---|---|
| Publish v2 while v1 instances wait | ❌ blocked by gap 1; the kernel keeps a model state version per instance | **No case** |
| Migrate an instance to v2 | 🟡 migration plans in `WorkflowActor` | **Out**, by Claude's reading of the trimmed scope (moves with the plan runner) |
| Instance list with filters | 🟡 `GET /runtime/core-preview/workflows/instances`, filters on definition and status | K9, K10 |
| Timeline and journal view | ❌ in Studio | K10 (tail read, catch-up) |
| Incident view and retry | 🟡 incidents and retry in the model | K11 (inspection and the one-page recover), K10; Park retry selection and discard are **Out** |
| Cancel or terminate | 🟡 terminate exists in the kernel; terminate of a parked root comes later in critical-path step 1 | Composition slice S5, by Claude's reading of the trimmed scope |
| Delete and retention | 🟡 retention pass in code | K11 |
| Export, import, copy, rename definitions | Core-independent, but what is exported cannot run on the kernel | **No case** |

## Runtime pass

| Check | Core Preview | Milestone |
|---|---|---|
| Restart the host during a wait | ✅ for the built-in approval workflow | **No case** for designed workflows |
| Kill the host while a request waits | 🟡 503 and 504 cases in the edge checks | K8 |
| About 200 requests a second on W1 | ❌ no admission or route host | K11 (admission), K12 item 1 (sustained arrivals) |
| Duplicate POST | 🟡 replay dedup by event id proven; HTTP start dedup needs a caller key | **No case** |
| Large request body | ❌ bodies sit in state, under the checkpoint cap | **Out** (values by reference) |
| Two workflows on one route | ❌ the later one silently wins | K8 (refused when the route table is built) |
| 404 and 405 | 🟡 an unmatched event returns 404 in the edge check; no 405 | **No case** |
| Suspends before responding | ✅ 500 when a held response's turn parks; 504 at the reply deadline | K8 |
| SQLite Embedded, then PostgreSQL with two nodes | 🟡 PostgreSQL only in the preview; Cluster demo exists separately | K4, K5; SQL Server and MySQL are **Out** |

## Sources

- [apps/Elsa.Workbench.Host/CorePreview/](../../../apps/Elsa.Workbench.Host/CorePreview/)
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
