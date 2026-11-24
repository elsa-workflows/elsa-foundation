# GA test drive: three owner decisions for the kernel

24 November 2026, read at `main` `92bc067918`; the facts below are updated for #5706 and #5711. Part of [#4229](https://github.com/elsa-workflows/elsa-foundation/issues/4229).
**Decided by the owner on 24 November 2026:** card 1 option A, card 2 option A, card 3 option B. Each card ends with its
decision. Read from the source only; nothing was run and no code changes.

The GA test drive's [gap map](core-preview-map.md) found that no designer-built
HTTP workflow runs on the Core Preview yet. Query strings and headers landed with
[#5706](https://github.com/elsa-workflows/elsa-foundation/pull/5706), and Flowchart start routes with
[#5711](https://github.com/elsa-workflows/elsa-foundation/pull/5711); duplicate-route refusal is still open. Three gaps
needed an owner decision first. Cards 1 and 2 could touch the foundation contract or the side-effect rules, and card 3
sets the release 1 scope:

| # | Decision | Owner's decision (24 November) | Milestone | Contract change | Size |
|---|---|---|---|---|---|
| 1 | Route templates (`/orders/{id}`) | **A**, as recommended. Start routes subscribe on the template key; a mid-flow wait subscribes on its resolved path | K8 | None | M |
| 2 | The `SendHttpRequest` response | **A**, as recommended. Run it as a Background job whose result completes the activity | K6 | None | M |
| 3 | The release 1 activity list | **B**; A was recommended. 26 of 34 toolbox activities run, Timer and Cron on a layer 3 schedule actor; 8 are hidden through the host availability baseline | K6, K10 | None | L, in small PRs |

None of the decided options changes the accepted K2 contracts
([foundation contract](../../architecture/foundation-contract.md)). Cards 1 and 2 each name the one option that would; no card 3 option does.

## 1. Route templates

### Today

- `HttpRoutes.Key(method, path)` builds the literal key `http:{METHOD}:{path}`
  ([HttpRoutes.cs](../../../essentials/src/Workflows/Runtime/Native/Model/HttpRoutes.cs)).
- A waiting `HttpEndpoint` subscribes on that literal key
  ([NativeModel.cs:456–457](../../../essentials/src/Workflows/Runtime/Native/Model/NativeModel.cs)).
- `HttpRequest` has the method, path, body, query string and headers since #5706
  ([Activities.cs:490](../../../essentials/src/Workflows/Runtime/Native/Model/Activities.cs)). It has no route values.
- The foundation matches event keys exactly. A key is an opaque string, tenant-qualified as `Q(t, key)`, at most
  1,024 UTF-8 bytes (`FoundationMetadataLimits.NameBytes`).

### The old core

- It matches a request against templates with ASP.NET's `TemplateMatcher`. Overlaps are ordered by
  `RouteTemplateSpecificity`: a literal segment beats a parameter, which beats a catch-all.
- It hashes the normalized `(template, method)` into one stimulus. Literal segments and parameter names are
  lower-cased; constraint bodies are kept as written.
- Route values reach the request model (`HttpEndpointRequestModelReader`).
- Two definitions on one template are refused per request with 409 `ambiguous-endpoint`
  ([HttpEndpointMiddleware.cs:250](../../../essentials/src/Activities/Http/Middleware/HttpEndpointMiddleware.cs)).
- A mid-flow resume goes to every bookmark on the template's stimulus. Route values do not pick the instance. On the
  old core, `POST /orders/42/cancel` resumes every order that waits on `orders/{id}/cancel`.

### Elsa 3

Elsa 3 (`Elsa.Http`'s `RouteTable`, `RouteMatcher` and `HttpWorkflowsMiddleware`) matches the path with the same
`TemplateMatcher`, taking the first hit in an unordered table. It hashes `(template, method)` for starts and resumes
alike, and picks the waiting instance by an `X-Correlation-ID` header or `correlationId` query value, or by
`X-Workflow-Instance-ID`. More than one matching trigger or bookmark is an error per request. Route values, query and
headers are the endpoint's `RouteData`, `QueryStringData` and `Headers` outputs. **Option A keeps** the template
matcher and the route values on the request. **It improves** three things: specificity orders the matches, conflicts
are refused at publish, and the route value picks the instance without a correlation header. **It drops** the
correlation and instance-id headers for HTTP resumes in release 1; option C's caller key is how they can come back.

### Options

**A. Template key for starts, resolved key for waits (recommended).**

- The route table holds normalized templates. A start route subscribes on `http:GET:orders/{id}`.
- A mid-flow `HttpEndpoint` keeps its template in `Path` and gets a `RouteValues` input, for example
  `{ id: Variables.OrderId }`. It subscribes on the resolved path, `http:POST:orders/42/cancel`.
- The HTTP adapter matches a request against the templates, most specific first.
  - For a start template, it sends to the template key.
  - For a wait template, it sends to the resolved key, so only the instance waiting for order 42 receives it.
- **Route values** reach `HttpRequest` as a new `RouteValues` field (`IReadOnlyDictionary<string, string>`), decoded
  once by the adapter, for starts and resumes alike. Parameter names are lower-cased, as on the old core; values keep
  their case.
- **Template and literal conflicts.** These are checked at publish, beside the duplicate-route refusal the parallel
  session builds:
  - Two start routes with the same method and template shape are refused. `orders/{id}` and `orders/{key}` have the
    same shape.
  - Overlapping shapes are allowed, and the more specific one wins: `orders/list` beats `orders/{id}`.
  - A shape that is a start route in one definition and a mid-flow wait in another is refused, so each request needs
    one lookup. If the owner wants both on one route, the adapter can try the resume key first and fall back to the
    start key on "no waiter". FC-C2 makes that retry safe; it costs one extra call.
  - Release 1 supports `{name}`, `{name?}` and `{*rest}` in start routes. Inline constraints such as `{id:int}` are
    refused at publish. A wait template must be fully bound, so optional and catch-all parameters are refused there.
- **Key length.** A resolved key carries caller data, so it can grow. Keep the readable key up to 512 bytes. Above
  that, both sides use `http:{METHOD}:h:` plus the SHA-256 of the normalized path. Kestrel already answers 414 above
  its request-line limit (8 KB by default). PostgreSQL and SQLite have room for a 1,024-byte key; SQL Server's tighter
  index width is work after release 1.
- **Sensitive route values** (an e-mail address in the path) end up in subscription rows. A per-route option to hash
  the key with the tenant's HMAC, as [tenancy](../../architecture/tenancy.md) does for sensitive correlation keys, can
  follow in 1.x.
- **The reserved event-id prefix.** The adapter picks each request's event id. Route values never go into it. When
  the adapter uses the client's `Idempotency-Key` header to deduplicate a repeated POST, it sends `http/` plus the
  value. A raw value starting with `~` or `key/` would otherwise be refused with `InvalidEventId` (R7-M1). A duplicate
  is the same `(key, id)` pair, so key normalization must be stable.
- **Where it lives.** The template parser and matcher belong in the HTTP adapter, reusing `Elsa.Http`'s `RouteMatcher`
  and `RouteTemplateSpecificity`. The Native model keeps templates and keys, and stays free of ASP.NET.
- **Contract:** none. Keys stay opaque; `Subscribe`, `SetStartAsync` and `RequestAsync` are used as they are.

**B. Template key for starts and waits.** Waits subscribe on `http:POST:orders/{id}/cancel`, and every waiter checks
the route value in its own turn. This matches the old core. Each request wakes every waiting order, which is O(n)
turns. The first live output answers the caller even when it comes from the wrong order, so W5 needs a workaround
again. Contract: none. Not recommended.

**C. Route values name the instance.** `{id}` maps to the instance's caller key, and the adapter sends to
`Target.Key("order-42")`. No subscription is involved. This requires every such workflow to start under a caller key,
which adds a new authoring concept and ties HTTP routing to the [key binding rules](../../architecture/tenancy.md).
Contract: none. It fits a later "instance key" feature better than release 1.

**D. Pattern subscriptions in the foundation.** `Subscribe` accepts a template and the foundation matches paths. This
changes FC-6 and FC-7, which match exact keys, and every foundation adapter would have to match patterns. **It changes
the K2 contract.** Not recommended.

### Recommendation

Option A, in K8, with an acceptance case: W3's `/orders/{id}/enrich` starts with `id` in `HttpRequest.RouteValues`;
W5's `/orders/{id}/cancel` resumes only the waiting W4 for that order; and the route table refuses a template shape and
method that another definition holds. Size M: about 400 to 600 lines with tests, on top of #5706 and #5711.

### Decision

Owner, 24 November 2026: **option A**, resolved waits, as recommended. Its acceptance case is in the K8 row of the
[program plan](../../program-goals/clean-slate-kernel.md#milestones).

## 2. The `SendHttpRequest` response

### Today

- The Native `SendHttpRequest` is `[External]`. It emits an `HttpSendEffect` with its effect key in the
  `Idempotency-Key` header and completes at once
  ([Native Activities.cs:42](../../../essentials/src/Workflows/Runtime/Native/Activities/Activities.cs)).
- The host callback sends the request during the turn. The workflow never sees the response, and the Workbench
  preview passes no sender.
- The old core's `SendHttpRequest` awaits the response inside the activity. It returns status, headers and body. Its
  outcomes are `Done` when no codes are set, or one port per expected status code and `Unmatched status code`, then
  `Failed` and `Timeout`, plus a `Timeout`
  input and credentials through a `Connection`. W3 needs all of this.
- **Elsa 3** (`SendHttpRequestBase`, `FlowSendHttpRequest`) also awaits the call inside the activity. It sets
  `StatusCode`, `ParsedContent` and `ResponseHeaders` outputs, and completes on `Done` plus one outcome per expected
  status code or `Unmatched status code`, or on `Failed to connect` or `Timeout`. An opt-in `EnableResiliency` retries 408, 409, 429, 500,
  502, 503 and 504 through Polly, with no idempotency key, and `Authorization` is a plain input. **Option A keeps** the
  outputs and outcomes. **It improves** where the call runs: in a job, so the instance stays responsive, parallel
  calls run side by side and every send carries the effect key. **It drops** the in-activity Polly retries for
  release 1, since resending a POST is only safe with a key; a job-level retry with the same `Idempotency-Key` can
  follow in 1.x. It also drops the plain `Authorization` input in favour of the `Connection`, as the old core already
  did.

The rules that bind every option:

- Side effects are at least once in every mode, and a receiver deduplicates by the effect key (kernel default 1,
  [decisions](../../architecture/decisions.md#kernel-defaults)).
- A job runs at least once, and its result changes state once (FC-5).
- `StartJob(key, arguments, timeout)` cancels the job at its deadline and delivers a timeout error (owner decision 6).
- A job's effect key survives its re-runs. Each physical attempt gets a new `AttemptId`
  ([job workers](../../architecture/job-workers.md#cooperative-cancellation-of-recovered-attempts)).
- A result too large for its row becomes the fault "result too large" and is not re-run.
- Values by reference are cut from release 1 (trimmed scope), so a response body lives in the instance's state and
  is rewritten at every commit.

### Options

**A. A Background job whose result completes the activity (recommended).** `SendHttpRequest` becomes `[Background]`,
as `CallService` is ([Native Activities.cs:78](../../../essentials/src/Workflows/Runtime/Native/Activities/Activities.cs);
the model's background path is [NativeModel.cs:281](../../../essentials/src/Workflows/Runtime/Native/Model/NativeModel.cs)).
The turn commits the job ticket with the evaluated inputs and the effect key (`BackgroundArgs` already carries both).
The job sends the request. Its result, `{ status, headers, body }`, comes back as `job-finished`, and the activity
completes with the old core's outcomes.

- **Timeouts.** The `Timeout` input, defaulting to `HttpActivityOptions.DefaultTimeout`, becomes the `StartJob`
  timeout and the HTTP client's timeout. Either one firing completes the activity on `Timeout`. It does not fault.
  The background path takes this evaluated timeout in place of the node's fixed `JobTimeout`, which is part of the
  work.
  The race between a timeout and a completion is already a K6 acceptance case.
- **Retries.** The job makes one HTTP attempt. The foundation re-runs it only after an owner loss, with the same
  `Idempotency-Key`. A non-2xx status is an outcome, and a transport error is `Failed`, so the workflow decides what
  to do. A fault, such as a bad URL, goes to the Native model's incident retry. A retried activity is a new attempt,
  so it gets a new effect key. A retry input with backoff can follow in 1.x.
- **Response size.** The job reads the body up to `MaxResponseBytes`, a host option with a 256 KiB default. A larger
  body completes on a new `ResponseTooLarge` outcome, with status and headers and no body. The foundation's
  "result too large" fault stays the backstop. When values by reference ship, the cap can rise.
- **`Idempotency-Key`.** The effect key, unless the author sets that header. GET and HEAD send it too; it is harmless.
- **Secrets.** The ticket's arguments are stored, so credentials must not be in them. Credentials come through a
  `Connection` reference that the job resolves when it runs, as on the old core. Authored headers refuse secret
  bindings.
- **Parallel calls** (W3) run as parallel jobs. An Ephemeral instance that waits only on its own jobs may stay in memory
  up to the short wait and the request's deadline (FC-2), so a fast synchronous proxy writes nothing.
- **Cost.** In Balanced the ticket rides the turn's commit, and the result adds one turn and one commit.
- **Contract:** none. `StartJob`, `IJob` and `JobFinished` exist. The fire-and-forget `HttpSendEffect` stays for
  `SendEmail` and `SendNotification`.

**B. An External effect whose result is recorded in a later turn.** The turn records the request. After the commit,
the host sends it and posts the response to the instance's exact ref, and the activity waits for that message.

- If the process dies between the commit and the post, nothing durable says the call is outstanding. The instance
  waits until a timer fires. A resend on that timer duplicates what the job ticket already does.
- Making it durable needs a new foundation obligation: an outbound call with a reply. **That changes the K2
  contract** (a new `IActorTurn` member, its `ITurn` view and an FC clause). Not recommended.

**C. Keep fire-and-forget and add a request-and-response activity.** `SendHttpRequest` stays as it is. A second
activity, for example `CallHttpEndpoint`, is option A. The toolbox then has two HTTP senders. Old-core definitions
that read `SendHttpRequest`'s outputs would need a mapping on import. Contract: none. It only pays off if
fire-and-forget calls are common enough to justify a second activity. A fire-and-forget call also runs fine as A,
with no output read.

**D. Await the response inside the turn.** An `[External]` leaf awaits the call, as on the old core. It needs no job
and no extra commit. The instance takes no cancel or timer during the call, the call holds the turn under the lease
rules, and W3's parallel calls run one after another. Contract: none. Not recommended beyond short calls.

### Recommendation

Option A, in K6. The K6 row already names the effect key in the built-in HTTP activity and the job timeout races. Add
an acceptance case: W3 reads a downstream 500 on its status port, hits its timeout, and gets one `Idempotency-Key`
across a job re-run after an owner loss. The Workbench host composes the HTTP client for the job body, which closes
the map's "no outbound sender" gap. Size M: about 400 to 600 lines with tests.

### Decision

Owner, 24 November 2026: **option A**, a Background job, as recommended. Its acceptance case is in the K6 row of the
[program plan](../../program-goals/clean-slate-kernel.md#milestones).

## 3. The release 1 activity list

### How hiding works today

Studio's toolbox already has a layered availability policy ([ADR 0026](../../adr/0026-activity-availability-policy-stack.md)):

- The catalog is the base.
- The host baseline comes from `Elsa:Activities:Availability`: `Include` and `Exclude` lists of activity types and
  named `Sets` (`ActivityAvailabilityOptions`, read by `DefaultActivityAvailabilityEvaluator`).
- Management settings can narrow the baseline further.

The picker shows only addable activities. ADR 0026 also says availability decides what can be added; it does not
decide what is valid or what runs. An imported or older definition can still hold a hidden activity, so a kernel host
also needs a publish check.

[#5701](https://github.com/elsa-workflows/elsa-foundation/pull/5701) leaves toolbox filtering on a kernel host to this
decision.

**Elsa 3** hides an activity through the descriptor's `IsBrowsable` flag: `[Browsable(false)]` on the class, or an
`IActivityDescriptorModifier` that reads a host option, as the C# module does for `RunCSharp`. A hidden activity stays
registered and runs if a definition holds it. **Option A keeps** the host deciding what the toolbox shows. **It
improves** on that with configuration in place of code, through the availability baseline, and with a publish check,
which Elsa 3 has no need for because every registered activity runs there. **It drops** nothing that Elsa 3 users
rely on.

### Inventory

The 34 product activity classes under `essentials/src/Activities/**`. The `Set` variable intrinsic belongs to the
engine and is not counted; the Native model has it as `SetVariable`. "Native" means the Native model has it today.
"Port" means it runs through K6's adapted activity context.

| # | Activity | Package | Release 1 | Native today | Note |
|---|---|---|---|---|---|
| 1 | Sequence | Sequence | Run | ✅ | |
| 2 | If | ControlFlow | Run | ✅ | |
| 3 | Switch | ControlFlow | Run | Port | W2 |
| 4 | For | ControlFlow | Run | Port | |
| 5 | ForEach | ControlFlow | Run | ✅ | 1,000-item cap (large fan-out) |
| 6 | While | ControlFlow | Run | ✅ | |
| 7 | Do | ControlFlow | Run | Port | |
| 8 | Parallel | ControlFlow | Run | ✅ | |
| 9 | Break | Primitives | Run | Port | Containers already end on `Break`; only the leaf is missing |
| 10 | Flowchart | Flowchart | Run | ✅ | K7, G1–G5 |
| 11 | GraphActivity | Graph | Run | Port | Reusable activity boundary |
| 12 | Complete | Graph | Run | Port | Ends its nearest GraphActivity |
| 13 | WriteLine | Primitives | Run | ✅ | |
| 14 | WriteLines | Primitives | Run | Port | Trivial |
| 15 | Fault | Primitives | Run | Port | A leaf fault already raises an incident; W7 |
| 16 | Inline | Primitives | Run | Port | An expression result; JavaScript under K12 item 3 |
| 17 | RunJavaScript | Scripting | Run | Port | K12 item 3 |
| 18 | Event | Primitives | Run | ✅ | |
| 19 | PublishEvent | Primitives | Run | ✅ | |
| 20 | Delay | Scheduling | Run | ✅ | Timer as a message to self |
| 21 | DispatchWorkflow | DispatchWorkflow | Run | ✅ | |
| 22 | HttpEndpoint | Http | Run | ✅ | Route templates: card 1 |
| 23 | WriteHttpResponse | Http | Run | ✅ | Headers since #5706 |
| 24 | SendHttpRequest | Http | Run | 🟡 no response | Card 2 |
| 25 | CompensationScope | Compensation | Hide | ❌ | No compensation port in release 1 ([StateMachine page](../../architecture/statemachine-on-the-kernel.md), decision 4) |
| 26 | Compensate | Compensation | Hide | ❌ | Same |
| 27 | Confirm | Compensation | Hide | ❌ | Same |
| 28 | BpmnProcess | Bpmn | Hide | ❌ | BPMN is a release 1 non-goal ([ADR 0099](../../adr/0099-clean-slate-execution-kernel.md#non-goals-for-release-1)) |
| 29 | BpmnDecision | Bpmn | Hide | ❌ | Same |
| 30 | RunBpmn | Bpmn | Hide | ❌ | Same |
| 31 | Timer | Scheduling | Run (decision B) | New | Recurring start trigger on the layer 3 schedule actor |
| 32 | Cron | Scheduling | Run (decision B) | New | Same |
| 33 | CreateUserTask | Primitives | Hide | ❌ | A wait plus a resume, as Event is, but its user-task store and API sit on the old core |
| 34 | ReadLine | Primitives | Hide | ❌ | Console input has no meaning on a server host |

**Count, as decided (option B):** 26 run and 8 are hidden. Of the 26, 14 are in the Native model today, 10 are ports,
and Timer and Cron are new on the schedule actor. Option A, the recommendation, had 24 run and 10 hidden.

### Options

**A. Run the 24 and hide the 10 (recommended).** This covers all seven test-drive workflows except W7's compensation,
which is out by owner decision, and every basic control-flow and HTTP activity a designer user expects. Hiding uses
the existing host baseline: a kernel host defines a named set, for example
`Elsa:Activities:Availability:Sets:kernel-unsupported`, and lists it under `Exclude:Sets`, so Studio needs no code
change. The kernel's publication path refuses a
definition that holds a hidden type, with an error that names the node. Contract: none.

**B. A + Timer and Cron.** It adds a layer 3 schedule actor: a self-timer per schedule that publishes to the
workflow's start key at each occurrence. This is buildable on the contract as it stands, with no change. It adds an
M-sized slice and its restart and duplicate-firing cases to K6, and no test-drive workflow needs it. Choose this if
recurring starts are a release 1 product promise.

**C. Only what the Native model has today (14 run, 20 hidden).** This is the smallest. W2 (`Switch`) and W7 (`Fault`)
cannot be built, and the designer loses For, Do, Break and JavaScript. Not recommended.

**D. Hide through a capability flag on each activity descriptor.** This replaces A's set: Studio filters by what the
host's runtime declares it can run. It is more precise when both cores run side by side, and it adds descriptor
metadata, an API field and Studio code. The host set in A does the same job until the switch, after which the old
core is gone. Not recommended.

### Recommendation

Option A. The ports land in K6 as small PRs, about one container or leaf each: Switch, For, Do, Break, WriteLines and
Fault are S; GraphActivity with Complete is M; Inline and RunJavaScript land in K6, and K12 item 3 qualifies their
JavaScript semantics. The `kernel-unsupported` set and the kernel registry's refusal land in K6. The publish refusal
through the definitions API lands in K10 with the definitions-to-kernel path. K10 adds a journey: on a kernel host the toolbox
shows the 24, and publishing a definition with `CompensationScope` is refused with the node named. Size L in all,
about ten PRs.

### Decision

Owner, 24 November 2026: **option B**, A plus Timer and Cron. The recommendation was A.

- **26 run, 8 hidden.** The hidden set is the three compensation activities, the three BPMN activities,
  CreateUserTask and ReadLine.
- **The schedule actor.** Timer and Cron run on a layer 3 schedule actor: one self-timer per schedule, publishing to
  the workflow's start key at each occurrence. It uses the contract as it stands. K6 carries its restart and
  duplicate-firing cases. It adds one M-sized slice.
- **The rest is as in A.** The ports, the `kernel-unsupported` set and the kernel registry's refusal land in K6. The
  publish refusal lands in K10 with the definitions-to-kernel path, and the K10 journey shows the 26.

The acceptance cases are in the K6 and K10 rows of the
[program plan](../../program-goals/clean-slate-kernel.md#milestones).

## Sources

- [HttpRoutes.cs](../../../essentials/src/Workflows/Runtime/Native/Model/HttpRoutes.cs),
  [NativeModel.cs](../../../essentials/src/Workflows/Runtime/Native/Model/NativeModel.cs),
  [Native model Activities.cs](../../../essentials/src/Workflows/Runtime/Native/Model/Activities.cs),
  [Native Activities.cs](../../../essentials/src/Workflows/Runtime/Native/Activities/Activities.cs)
- Old core: [HttpEndpointMiddleware.cs](../../../essentials/src/Activities/Http/Middleware/HttpEndpointMiddleware.cs),
  [HttpEndpointStimulus.cs](../../../essentials/src/Activities/Http/Activities/HttpEndpointStimulus.cs),
  [RouteTemplateSpecificity.cs](../../../essentials/src/Http/Elsa.Http/Services/RouteTemplateSpecificity.cs),
  [SendHttpRequest.cs](../../../essentials/src/Activities/Http/Activities/SendHttpRequest.cs)
- [Foundation contract](../../architecture/foundation-contract.md) (FC-2, FC-5, FC-6, FC-7, FC-C2),
  [FoundationMetadataLimits.cs](../../../essentials/src/ActorFoundation/BuiltIn/Durable/FoundationMetadataLimits.cs)
- [Job workers](../../architecture/job-workers.md), [kernel defaults](../../architecture/decisions.md#kernel-defaults),
  [tenancy](../../architecture/tenancy.md)
- [ADR 0099](../../adr/0099-clean-slate-execution-kernel.md), [ADR 0026](../../adr/0026-activity-availability-policy-stack.md),
  [StateMachine on the kernel](../../architecture/statemachine-on-the-kernel.md),
  [program plan, trimmed scope](../../program-goals/clean-slate-kernel.md#trimmed-scope-owner-24-november-2026)
- [Activity availability options](../../../essentials/src/Activities/Design/Core/Options/ActivityAvailabilityOptions.cs)
