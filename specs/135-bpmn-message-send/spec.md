# 135 — BPMN message send surface: PublishEvent primitive (durable-first stimulus publish) + message throw/end events and send/receive tasks (BPMN Phase 3, collaborations slice 1 of 2)

## Goal

Give running workflows a **message send surface** — the one missing half of executable
collaborations. A new **`PublishEvent`** primitive (sibling of `Event`) publishes a named-event
stimulus **durably-first**: it stages a post-commit intent that, after the activity's own
checkpoint commits, calls `IStimulusRouter.RouteAsync` in `StartAndResume` mode — so one send both
**starts** every published workflow whose message start event listens on that name and **resumes**
every waiting catch (intermediate, boundary, event subprocess). The BPMN module then models
**message throw events** (intermediate throw + message end event) and — sharing the same synthesis
— **`sendTask`** (binds a synthesized `PublishEvent` child) and **`receiveTask`** (binds a
synthesized `Event` catch child, the receive twin), completing point-to-point pool messaging
between separately published processes. Collaboration/pool **import** (N-definition split,
`<messageFlow>` wiring) is slice 2.

Delivery is **broadcast-by-name** this slice: the receive-side correlation gap (no bookmark
anywhere carries correlation metadata — filed as issue #1001) makes correlation narrowing
inert end-to-end, so `PublishEvent` carries an optional `CorrelationId` input that is threaded
into the dispatch (future-proof, works the moment #1001 lands) but cross-instance narrowing is a
documented cut.

## Context (verified terrain, origin/main ≥ 96fda25be; line numbers drift — verify at implementation)

- **The fabric is shipped and battle-tested.** `EventStimulus` (`Primitives`): type `"Event"`,
  hash = SHA-256 of the **name only** — deliberately cross-workflow (the collaboration feature).
  `StimulusRouter.RouteAsync` snapshot-then-start-then-resume; `StartAndResume` proven to do both
  in one delivery (`Route_StartAndResume_DoesBoth`); BPMN message starts share the exact
  type/hash pair via the parity-pinned duplicate `BpmnMessageStartStimulus` (the duplication keeps
  the BPMN envelope free of the Primitives activities package — keep it that way).
- **No in-workflow publisher exists.** The router is called only by the API dispatch handler, the
  HTTP endpoint middleware, and the recurring-trigger pump. Nothing in any activity reaches
  `RouteAsync`.
- **The durable rail exists.** `RuntimePostCommitIntent` + the durable `RuntimePostCommitOutbox`
  (claim/complete/redrive, retry-until-acknowledged) with pluggable intent kinds
  (`RuntimePostCommitIntentKinds` has `EnqueueSchedulerWork`; DispatchWorkflow defines its own
  `StartChildIntentKind` and stages via `IWorkflowDispatchStager.StageWorkflowDispatch` on
  `IRuntimeActivityExecutionContext`). A send intent mirrors this: new intent kind + a post-commit
  handler that calls the router. Fire-and-continue: the activity completes immediately; delivery
  happens after its commit, retried by the outbox.
- **Synthesis is loose-coupled.** BPMN catch children are synthesized `ActivityNode`s referencing
  catalog rows by placeholder version id (`Elsa.Delay`/`Elsa.Event`) — no compile-time reference.
  A send child (`Elsa.PublishEvent`) rides the identical pattern in reverse. The throw-event
  element surface exists (escalation/compensation); a message throw currently drops at import.
- **Correlation gap (issue #1001)**: `FindWaitingAsync` narrows on bookmark
  `Metadata[CorrelationId]`, but no registration anywhere writes it. Narrowed sends match zero
  bookmarks; un-narrowed sends broadcast to all same-name waiters. Cut per Goal.
- **Send-before-receiver-ready**: the router snapshots waiters at delivery time; a send with no
  published start listener and no parked catch is **lost** (fire-and-forget, no rendezvous). This
  is the shipped fabric's semantics — documented, not changed.
- **DispatchWorkflow precedent** for staging shape: builds intent + stages via the context's
  stager inside execution; payload delivery post-commit (`DispatchWorkflow.cs` ~194-221).

## Design decisions

### D1 — `PublishEvent` primitive (Primitives package, runtime-side slice of it)

- `PublishEvent` activity (sibling of `Event`, `ActivityType "Elsa.PublishEvent"`): inputs
  `EventName` (required literal-or-expression string), `CorrelationId` (optional; defaults null —
  broadcast; threaded into the dispatch request for #1001-readiness), `Payload` (optional
  `JsonElement?` — carried as the stimulus payload the way the API dispatch surface carries one).
- Execution: validates the resolved name (non-empty), builds a `RuntimePostCommitIntent` of new
  kind **`PublishStimulusIntentKind`** carrying `(stimulusType: "Event",
  stimulusHash: EventStimulus.Hash(name), eventName, correlationId?, payload?)`, stages it via the
  existing context staging surface (the DispatchWorkflow stager precedent — reuse the same seam;
  if the stager is dispatch-specific, a parallel minimal stager registration in the same shape;
  tripwire below), and **completes immediately** with outcome `Done` (fire-and-continue).
- A new post-commit intent **handler** (registered beside the dispatch intent handler) calls
  `IStimulusRouter.RouteAsync(StartAndResume)` with the carried values. Outbox retry semantics
  apply as shipped; delivery failures follow the outbox's retry/poison discipline (no new policy).
- Home: the runtime half lives with the other Primitives runtime activities; the intent
  kind/handler live wherever `StartChildIntentKind`'s handler lives (mirror the layering exactly).
  **Tripwire 1**: if the staging seam (`IWorkflowDispatchStager` or its underlying checkpoint
  channel) cannot carry a non-dispatch intent without modification, STOP and report the exact
  surface — a minimal additive generalization may be acceptable but report before implementing.

### D2 — BPMN message throw/end events + send/receive tasks

- **Message throw**: `ResolveIntermediateThrowEvent` gains the message definition (family
  `IntermediateThrowEventMessage`); `ResolveEndEvent` gains message (family `EndEventMessage`).
  Both are **bound-child** shapes (unlike escalation's engine-command shape): the element binds a
  synthesized `PublishEvent` child (`ChildNodeId`), behavior = the plain schedule-child →
  child-completes → route/consume path (`TaskBehavior`-like for the throw; end consumes). No new
  engine commands, no new state. The child's fire-and-continue completion IS the throw semantics.
  Validation: message throw/end requires a bound child + a message definition with `name`;
  the send name rides the definition properties as usual.
- **`sendTask`** / **`receiveTask`**: new element types resolving to the task family;
  `sendTask` with a message definition-or-attribute name binds a synthesized `PublishEvent`;
  `receiveTask` binds a synthesized `Event` catch child (the `intermediateCatchEvent` message
  twin — same suspension machinery). Both are boundary hosts and MI-legal (task family). If
  authoring binds a different child, it behaves as authored (the callActivity no-type-check
  precedent).
- Synthesized nodes: `node-{id}` (throw/end) with `LiteralArgument("EventName", name)` (+
  correlation/payload unset this slice), placeholder version id const `Elsa.PublishEvent`;
  receiveTask synthesis reuses `BuildEventCatchChild` verbatim.

### D3 — Interchange

- **Import**: `intermediateThrowEvent`/`endEvent` with `messageEventDefinition` (root
  message-index name resolution as usual) → message throw/end with synthesized `PublishEvent`
  child; ref-less/name-less → Degraded (throw Dropped + cascade; end → none end + finding).
  `<sendTask messageRef=…>`/`<receiveTask messageRef=…>` → bound synthesis as above; name-less
  sendTask/receiveTask → imports as a plain **unbound task** + Info finding (the serviceTask
  precedent). Signal throw events remain unsupported (Dropped + finding — stated cut; the fabric
  is identical but the slice stays message-scoped).
- **Export**: message throw/end emit `messageEventDefinition` with deduped root `<message>`
  declarations (spec-118 pattern); sendTask/receiveTask emit their element with `messageRef`;
  synthesized children never export. Round-trips: message throw, message end, sendTask,
  receiveTask.
- Importer never emits a graph the validator rejects.

### D4 — Stated cuts

Correlation narrowing end-to-end (issue #1001; `CorrelationId` input threads through but matches
nothing until the runtime stamps bookmarks); signal throw events; send payload mapping from
process variables (Payload input exists on the primitive; BPMN synthesis leaves it unset —
expression-bound payloads are authorable on the bound child, not importer-synthesized);
collaboration/pool/`<messageFlow>` import (slice 2, spec 136); send rendezvous/buffering (the
fabric is fire-and-forget — documented); cross-shell/distributed delivery guarantees beyond what
the router/outbox already provide.

## In scope

- `PublishEvent` + intent kind + post-commit handler + registrations (D1) — the ONE
  outside-BPMN-module surface of this unit, mirrored on the DispatchWorkflow layering.
- BPMN: throw/end message families + validation; sendTask/receiveTask element types; synthesis;
  placeholder version id const (D2).
- Interchange per D3. Tests + module docs (BPMN README/EXTENSION_POINTS, Interchange README,
  Primitives docs alongside `Event`'s).
- Tests: `PublishEvent` unit tests (durable-first: intent staged not direct-routed — assert via
  the outbox/commit shape; fire-and-continue completion; name validation); end-to-end
  cross-workflow: workflow A `PublishEvent` → separately published workflow B message START
  starts AND a parked workflow C `Event` catch resumes (the StartAndResume pin — reuse the
  router/dispatch test fixtures; if the full cross-workflow harness is disproportionate inside
  the Primitives/BPMN test projects, the StimulusRouter test project's fixtures are the home —
  tripwire 2: report placement rather than shimming); BPMN: message throw routes onward
  immediately while the send lands post-commit; message end consumes; sendTask/receiveTask
  round-trip execution (receiveTask suspends on the Event bookmark, resumes via
  `ResumeAsync`); boundary/MI composition sanity on sendTask (one test); interchange round-trips
  + degrades; determinism.

## Out of scope

Everything in D4; changes to `StimulusRouter`/lookup semantics; the #1001 fix.

## Functional requirements

**FR-1 — Durable-first publish.** `PublishEvent` never calls the router in-execution: the intent
rides the activity's own commit; delivery occurs post-commit with outbox retry semantics; the
activity completes `Done` immediately.

**FR-2 — StartAndResume delivery.** One send starts every matching published message-start
workflow AND resumes every parked same-name catch (pinned end-to-end).

**FR-3 — BPMN throw shapes.** Message throw routes its outbound after scheduling the send child's
completion (fire-and-continue through the ordinary child path); message end consumes; both carry
the definition's `name` into the synthesized child.

**FR-4 — Send/receive tasks.** `sendTask` publishes and routes; `receiveTask` suspends on the
shipped Event bookmark and resumes on delivery; both compose with boundaries/MI as task-family
hosts.

**FR-5 — Correlation readiness.** `PublishEvent.CorrelationId`, when authored, threads into the
dispatch request verbatim (asserted), with the broadcast-until-#1001 behavior documented.

**FR-6 — Interchange.** D3 round-trips and degrades; the importer never emits a
validator-rejected graph.

**FR-7 — Discipline.** BPMN: schema v1 additive (element types, families, version-id const,
diagnostics if any); no new state records/token statuses/engine commands; behaviors decision-only
(the throw shapes are plain bound-child schedules). Primitives/runtime: no changes to existing
payload shapes, `CanHandle` predicates, frozen names, or router semantics — the intent kind +
handler + activity are purely additive. Specs 119–134 suites pass unmodified.

## Success criteria

- The cross-workflow StartAndResume pin green; durable-first pinned (no in-execution route call);
  all BPMN/interchange tests green.
- Full test projects green: BPMN, BPMN Interchange, Activities Runtime, Workflows Runtime,
  ControlFlow, Architecture, plus the Primitives and router-adjacent test projects touched.
  Full solution build clean.
