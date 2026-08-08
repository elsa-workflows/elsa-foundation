# 118 — BPMN interchange eventDefinition wiring (timer/message/signal) (BPMN Phase 2, interchange slice)

**Status**: Implemented
**Merged**: PR #940

## Goal

Make the executable event constructs of specs 116 (intermediate catch events) and 117 (event-defined
start events) **importable and exportable** through the BPMN 2.0 interchange surface
(`Elsa.Activities.Bpmn.Interchange`). Today the importer drops `intermediateCatchEvent` entirely and
degrades every event-defined `startEvent` to a plain none start; the exporter emits start events bare and
has no representation for catch events or message/signal/timer definitions. This slice closes that gap:
`messageEventDefinition`/`signalEventDefinition`/`timerEventDefinition` on start and catch events
round-trip losslessly onto the native `elsa.bpmn.structure` payload (populated `BpmnEventDefinition`
properties, plus — for catch events — a synthesized bound child in the `Bpmn.Activities` slot), and the
imported process publishes and validates through the **real** runtime seams with no hand editing. It
consumes the property-key convention spec 117 set (`BpmnEventDefinitionProperties.Name/Interval/Cron`)
and the runtime contracts specs 116/117 froze; it changes **no** runtime code.

## Context (what exists today)

- **Importer** (`Services/BpmnDocumentImporter.cs`). Element dispatch is a `switch (localName)` in
  `BuildProcessNode`. `case "startEvent"` detects `*EventDefinition` children via the reusable
  `IsEventDefinition` helper but **ignores** them, adds a `Degraded` issue, and imports a plain none
  start. There is **no** `intermediateCatchEvent` case → it falls to `default`, which emits a `Dropped`
  issue and then cascade-drops the element's sequence flows (an unresolved source/target ref). `case
  "endEvent"` is the only existing XML→`BpmnEventDefinition` mapping (terminate only) — the pattern to
  extend. `case "subProcess"` is the only precedent that synthesizes a child `ActivityNode` (node id
  `node-{id}`, added to `childActivities` → `BpmnAuthoredStructure.Activities`) and binds it via
  `childNodeId`. Root-level `<message>`/`<signal>` declarations are **not** parsed today (`ImportCore`
  reads only `process` + diagram); `BpmnXmlNames` has no message/signal/timer names, and nothing in
  `src/` reads `messageRef`/`signalRef`/`timeDuration`/`timeCycle`/`timeDate`.
- **Exporter** (`Services/BpmnDocumentExporter.cs`). `AppendContainerContent` emits `startEvent` bare
  (event definitions dropped); `BuildEndEvent` emits only `terminateEventDefinition`; an unknown element
  type hits the `_` fallback (an `intermediateCatchEvent` would emit as an empty element). Root
  message/signal declarations are never emitted. `SynthesizeBounds` sizes 36×36 only for
  `StartEvent`/`EndEvent`. The exporter iterates **elements only**; a catch event's bound child is not
  exported as a separate node.
- **Runtime contracts this must satisfy (unchanged by this slice):**
  - `BpmnElementFamilies.ResolveStartEvent`/`ResolveIntermediateCatchEvent` accept **exactly one**
    `timer`/`message`/`signal` event definition; anything else throws a deterministic
    `BpmnExecutionException`.
  - `BpmnGraph.Validate`: an `intermediateCatchEvent` **requires** a bound suspending child
    (`ChildNodeId` resolving into the `Bpmn.Activities` slot, exactly-one binding both ways); a start
    event must **not** bind a child.
  - Property convention (spec 117): message/signal derive the stimulus from
    `BpmnEventDefinitionProperties.Name` (blank name fails publish); a timer start needs exactly one of
    `Interval` (ISO-8601 duration) xor `Cron` (both/neither fails publish).
  - Catch children (spec 116 / BPMN README): timer → `Delay` (`Duration` literal), message/signal →
    `Event` with `EventName` literal and `CanStartWorkflow = false`.
- **Endpoints/permissions** (`Endpoints/`) are unchanged — analyze/import/export stay pure conversions.
  Only the finding inventory the tests assert on moves.

## Design decisions

### D1 — Catch-event import

An `intermediateCatchEvent` with **exactly one** `timer`/`message`/`signal` event definition imports as a
first-class `BpmnElement` (type `intermediateCatchEvent`) carrying a populated `BpmnEventDefinition`,
**plus** a synthesized bound child `ActivityNode` in the `Activities` slot (node id `node-{elementId}`,
mirroring the subprocess convention), bound through the element's `ChildNodeId`:

- **timer** → a `Delay` child whose `Duration` input literal is the resolved ISO-8601 duration (see D3);
- **message/signal** → an `Event` child whose `EventName` input literal is the resolved message/signal
  `name` (see D3), plus a `CanStartWorkflow = false` literal (a mid-flow catch never starts).

The `BpmnEventDefinition` properties are the interchange-canonical config; the child's literals are
**derived from them at import** (the runtime reads only the child's literals — the definition properties
are interchange metadata for catch events). Child `ActivityNode`s carry placeholder activity version ids
(`"Elsa.Delay"` / `"Elsa.Event"`, matching `Event.ActivityType` and the existing `"Elsa.BpmnProcess"`
placeholder convention hosts override downstream). An `intermediateCatchEvent` whose event definitions are
**absent, multiple, or of an unsupported type** (e.g. `link`, `conditional`) → `Dropped` with a finding
and the existing flow-cascade drop (an unbindable catch element cannot form a valid graph).

### D2 — Start-event import

A `startEvent` with **exactly one** `timer`/`message`/`signal` event definition imports with the
populated `BpmnEventDefinition` and **no** child (a pure element, spec 117). **Multiple** definitions, an
**unsupported** definition type, or a definition that resolves to no usable property (an unroutable name;
a non-recurring timer — see D3) → keep today's `Degraded`-to-none-start behavior, with an updated,
deterministic, documented message.

### D3 — Definition property mapping (net-new)

- **`messageEventDefinition` / `signalEventDefinition`.** Parse root-level `<message id name>` /
  `<signal id name>` declarations (children of `<definitions>`) into an `id → name` index; resolve the
  element's `messageRef` / `signalRef` against it → the `Name` property. A **missing ref**, an
  **unresolvable ref**, or a **blank name** → catch: `Dropped`; start: `Degraded`-to-none-start (never
  import an element that would fail publish or produce an unroutable stimulus). Signal and message are
  mechanically identical (both named-event stimuli); the definition type is authoring/interchange
  semantics only.
- **`timerEventDefinition` on a START event.** BPMN timer starts are recurring schedules:
  - `<timeCycle>` → `Cron` when the text is a cron expression, else `Interval` as an ISO-8601 duration.
    **Discriminator (documented, deterministic):** trim the text; if it starts with `'P'` or `'R'` it is
    an ISO-8601 duration (any leading `R…/` repetition prefix is stripped → the `Interval` property);
    otherwise it is a `Cron` expression. Anything that fits neither (empty after trim) → `Degraded`.
  - `<timeDuration>` / `<timeDate>` on a start → `Degraded`-to-none-start with a finding (spec 117
    supports **recurring** starts only; a one-shot delay/date is not a start schedule).
- **`timerEventDefinition` on a CATCH event.** A catch timer is a one-shot relative delay:
  - `<timeDuration>` (ISO-8601 duration) → the `Interval` property **and** the `Delay` child's `Duration`
    literal (the raw ISO-8601 text; the host binder converts it to `TimeSpan`).
  - `<timeCycle>` / `<timeDate>` on a catch → `Dropped` with a finding (a recurring/absolute timer has no
    single-delay catch representation in this slice).

### D4 — Export

Emit event-definition children from the populated `BpmnEventDefinition` properties for event-defined start
and catch elements:

- **message/signal** (start or catch): emit **one** root-level `<message>` / `<signal>` declaration per
  distinct name (deterministic generated id `message-{sanitizedName}` / `signal-{sanitizedName}`, deduped
  by name across the whole structure tree), plus the element's
  `<messageEventDefinition messageRef=…/>` / `<signalEventDefinition signalRef=…/>` referencing that id.
- **timer on a START**: `<timerEventDefinition><timeCycle>…</timeCycle></timerEventDefinition>` — the
  `Cron` expression verbatim, or the `Interval` ISO-8601 duration verbatim (both round-trip through the D3
  start discriminator: a `Cron` text is not `P`/`R`-prefixed, an `Interval` text is).
- **timer on a CATCH**: `<timerEventDefinition><timeDuration>…</timeDuration></timerEventDefinition>` — the
  `Interval` ISO-8601 duration verbatim (round-trips through the D3 catch mapping).
- A catch element's **bound child** (`Delay`/`Event`) is **engine detail and is NOT exported** as a
  separate node (the exporter iterates elements only); the child is re-synthesized on the next import.
- `intermediateCatchEvent` joins `StartEvent`/`EndEvent` in the 36×36 event branch of `SynthesizeBounds`.

### D5 — Round-trip invariant

import → export → import is **stable**: element types, event-definition types, resolved properties, and
(for catch events) the synthesized child bindings regenerate identically. Covered for timer-start-cron,
timer-start-interval, timer-catch-duration, message, and signal in `BpmnRoundTripTests`.

### D6 — Publish-parity guard

At least one test imports an XML document with event-defined starts **and** a catch event and runs the
resulting structure through the **real** runtime seams with no hand editing:

- The imported start elements build an `ExecutableNode` (`BpmnProcess`, `executionType=Trigger`) whose
  `WorkflowTriggerBindingExtractor([new BpmnProcessTriggerStimulusProvider()])` yields exactly the
  expected bindings — one per event-defined start, keyed by the correct `(StimulusType, StimulusHash)`
  with `bpmn.startElementId` metadata.
- The imported catch element + its synthesized child binding form a graph `BpmnGraph.From` accepts (the
  catch event's required-bound-child rule and the pure-start rule both pass).

This proves the interchange output satisfies specs 116/117 without reaching into runtime internals.

## In scope (this slice)

- **Importer**: an `intermediateCatchEvent` case (D1) with child synthesis; the `startEvent` case reads
  event definitions instead of ignoring them (D2); root `<message>`/`<signal>` declaration parsing into an
  id→name index threaded through `BuildProcessNode` (and recursively into nested subprocesses);
  `messageRef`/`signalRef`/`timeDuration`/`timeCycle`/`timeDate` reading with the D3 mappings; new
  `BpmnXmlNames` entries.
- **Exporter**: event-definition emission for start/catch elements (D4); root message/signal declarations
  deduped by name; `intermediateCatchEvent` in the event bounds branch.
- **Tests**: importer (catch import + child synthesis, start import, degrade/drop findings), round-trip
  (D5), publish-parity (D6); the moved finding-inventory expectations.
- **Docs**: Interchange `README.md` + `EXTENSION_POINTS.md` (owned interchange contracts); a one-line
  status note in the BPMN module `README.md` phasing paragraph.

## Out of scope (deferred / stated cuts)

- **Studio authoring UX** for event definitions (this slice is the XML conversion only).
- **Boundary events, event-based gateways, event subprocesses, multi-instance, compensation, error
  events** — later Phase 2 units; they stay `Dropped` on import.
- **`timeDate` absolute timers** and **`timeDuration`/`timeDate` start** schedules (non-recurring) — no
  runtime start representation; they degrade/drop per D3.
- **Payload/correlation mapping**: a message/signal definition maps only its `name`; `CorrelationId` and
  message payload variables are a later mapping unit. The imported `Event` catch child leaves
  `CorrelationId` unset.
- **`extensionElements` vendor-attribute preservation** and the BPMN MIWG conformance corpus (unchanged).
- **Endpoints/permissions** — unchanged; no request/response contract moves.

## Functional requirements

**FR-1 — Catch-event import.** A `<intermediateCatchEvent>` with exactly one `timer`/`message`/`signal`
event definition imports as a `BpmnElement` of type `intermediateCatchEvent` carrying a populated
`BpmnEventDefinition`, bound (`ChildNodeId = node-{id}`) to a synthesized child `ActivityNode` added to
`BpmnAuthoredStructure.Activities`: `Delay` (`Duration` literal) for timer, `Event` (`EventName` literal +
`CanStartWorkflow = false`) for message/signal. Its sequence flows are retained.

**FR-2 — Catch-event drop.** A catch event with zero, multiple, or unsupported event definitions, or one
whose reference/name/timer expression is unresolvable per D3, is `Dropped` with a finding, and its
sequence flows cascade-drop (existing behavior).

**FR-3 — Start-event import.** A `<startEvent>` with exactly one resolvable `timer`/`message`/`signal`
event definition imports as a pure `BpmnElement` (no child) carrying the populated `BpmnEventDefinition`
(`Name` for message/signal; `Interval` xor `Cron` for timer). An unresolvable/unsupported case
`Degraded`-to-none-start with a documented message.

**FR-4 — Message/signal resolution.** Root `<message>`/`<signal>` declarations parse into an id→name
index; `messageRef`/`signalRef` resolve to the `Name` property. A missing/unresolvable ref or blank name
degrades (start) or drops (catch).

**FR-5 — Timer resolution.** Start `<timeCycle>` maps to `Cron`/`Interval` by the D3 discriminator
(`P`/`R`-prefixed ⇒ interval with `R…/` stripped, else cron); start `<timeDuration>`/`<timeDate>` degrade.
Catch `<timeDuration>` maps to `Interval` + the `Delay` `Duration` literal; catch
`<timeCycle>`/`<timeDate>` drop.

**FR-6 — Export event definitions.** Event-defined start and catch elements export their definitions per
D4: message/signal emit a deduped root declaration + `messageRef`/`signalRef`; timer emits `<timeCycle>`
(start) or `<timeDuration>` (catch). Catch bound children are not exported.

**FR-7 — Diagram bounds.** `intermediateCatchEvent` sizes 36×36 in synthesized BPMNDI bounds.

**FR-8 — Round-trip stability.** For each of timer-start-cron, timer-start-interval, timer-catch-duration,
message, signal: export → import → export reproduces the same element types, definition types, resolved
properties, and catch child bindings (D5).

**FR-9 — Publish parity.** An imported document with message/signal/timer starts + a catch event yields
the expected trigger bindings via `WorkflowTriggerBindingExtractor` + `BpmnProcessTriggerStimulusProvider`
and validates via `BpmnGraph.From`, with no hand editing (D6).

## Invariants that MUST survive

- **No runtime change.** This slice touches only `Elsa.Activities.Bpmn.Interchange` (+ its tests) and
  docs. `BpmnElementFamilies`, `BpmnGraph`, `BpmnExecutionEngine`, the trigger/schedule providers, and the
  `Elsa.Bpmn.ExecutionState` schema are consumed, not modified.
- **Analyze == Import inventory.** `Analyze` and `Import` share `ImportCore`; `CountElement` tallies every
  element regardless of drop, and both passes report the same findings.
- **Existing behavior preserved** for: Phase-1 elements, terminate end events, subprocess nesting,
  `elsa:conditionOutcome` flow conditions, BPMNDI shape preservation, and the `boundaryEvent`
  drop-with-issue (the sample corpus's dropped element stays dropped).
- **Determinism.** Generated ids (child node ids `node-{id}`, root declaration ids `message-{name}` /
  `signal-{name}`) are pure functions of element/name; no wall-clock or random identity.
- **Domain project-tree guard** (Interchange nested-project `Compile Remove` pattern) and the endpoint
  request/response contracts are untouched.

## Success criteria

- Importer tests: a timer/message/signal catch event imports as `intermediateCatchEvent` + the right
  synthesized child (type, bound node id, literal inputs); an unbindable catch event (no/multiple/bad
  definition) drops with a finding + flow cascade; a timer/message/signal start imports the populated
  definition with no child; an unresolvable start degrades to none with a documented message;
  `boundaryEvent` still drops; analyze/import inventories agree.
- Round-trip tests (D5): timer-start-cron, timer-start-interval, timer-catch-duration, message, signal all
  reproduce identically across export→import→export.
- Publish-parity test (D6): imported starts extract the expected bindings; the imported catch graph
  validates through `BpmnGraph.From`.
- Full test projects green: BPMN Interchange, BPMN, Architecture. Full solution build clean.
