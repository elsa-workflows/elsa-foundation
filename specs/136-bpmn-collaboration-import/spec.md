# 136 — BPMN collaboration/pool import: N-process documents, participants, message-flow wiring (BPMN Phase 3, collaborations slice 2 of 2 — final construct slice)

**Status**: Implemented
**Merged**: PR #1003

## Goal

Make multi-pool BPMN documents import honestly. Today the importer reads only `<process>`
elements and **silently keeps one** (no finding — a two-pool collaboration loses a pool without a
trace); `<collaboration>`, `<participant>`, and `<messageFlow>` are invisible; `BpmnPool` and
`BpmnAuthoredStructure.Pools` are dormant never-populated placeholders. This slice:

1. **N-process import (additive API)**: `BpmnImportResult` gains `ProcessNodes` — one imported
   `ActivityNode` per executable process/participant — while the existing `ProcessNode` stays as
   the **selected/primary** node (the current selection rules unchanged: explicit `ProcessId` >
   first executable > first). Existing clients (including Studio, untouched per the program
   brief) keep working byte-identically; multi-pool callers get every pool.
2. **Collaboration reading**: `<collaboration>`/`<participant processRef>` populate `BpmnPool`
   records (pool id, name, process ref) on each imported structure; lanes gain their `PoolId`.
3. **Message-flow wiring metadata**: `<messageFlow>` endpoints resolve to the send/receive
   elements across pools and surface as analysis findings + structure metadata — pairing a send
   element (message throw/end, sendTask — spec 135) with its receive element (message start,
   catch, boundary, receiveTask, message event subprocess) and, where both sides carry message
   names, flagging **name mismatches** (a send that can never reach its intended receiver through
   the name-keyed fabric is a Degraded finding, not a silent wiring loss).

Execution needs nothing new: pools run as **separately published definitions** riding the shipped
name-keyed stimulus fabric (spec 135's send + the receive surfaces from specs 116/117/120/134).
This slice is interchange/model-layer only — zero engine changes, zero runtime changes.

## Context (verified terrain, origin/main ≥ 0cb12f670; line numbers drift — verify at implementation)

- **Importer today**: `definitions.Elements(Model + "process")` collected; selection = explicit
  `options.ProcessId`, else first `isExecutable`, else first; **all others dropped with no
  `BpmnImportIssue`**. `BpmnImportResult(ActivityNode ProcessNode, BpmnImportAnalysis Analysis)` —
  single node. `BpmnImportAnalysis.ProcessIds` already enumerates all discovered ids (the analyze
  pass is N-aware; only import collapses). Endpoints: `AnalyzeBpmnDocumentEndpoint` /
  `ImportBpmnDocumentEndpoint` (single optional `ProcessId`).
- **Models**: `BpmnPool` (doc: "executable collaborations arrive later") constructed nowhere;
  `BpmnAuthoredStructure.Pools` serialized but always `[]`; `BpmnLane.PoolId` always null (lanes
  themselves are read and stamped onto elements).
- **Message-flow endpoints across the shipped surface**: send side = message throw/end +
  `sendTask` (spec 135); receive side = message start events (spec 117), intermediate catches
  (116), message boundaries (120), `receiveTask` (135), message event subprocesses (134). All are
  name-keyed via the root `<message>` index; the fabric delivers by name (broadcast; correlation
  is #1001).
- **The program's interchange discipline**: analyze-then-commit; findings Info/Degraded/Dropped;
  the importer never emits a structure the validator rejects; exporter round-trips what it owns.

## Design decisions

### D1 — Import API (additive)

- `BpmnImportResult` gains `IReadOnlyList<BpmnImportedProcess> ProcessNodes` where
  `BpmnImportedProcess = (ProcessId, ParticipantId?, ParticipantName?, ActivityNode Node)`;
  `ProcessNode` (the primary) remains and equals the selected entry's `Node`. Single-process
  documents produce a one-entry list — byte-identical primary behavior.
- Non-executable processes (`isExecutable="false"`): imported into `ProcessNodes` with an **Info**
  finding (they exist as documentation pools; publishing them is the caller's choice), unless
  malformed (normal degrade rules per process). Each process imports through the existing
  `BuildProcessNode` path independently — findings carry the process id so multi-pool analyses
  stay readable.
- The **silent drop ends**: any process NOT selected as primary now appears in `ProcessNodes` and,
  for old-style single-node consumers, an **Info** finding notes the additional pools.
- Endpoints: the import response includes the new collection (additive DTO field); `ProcessId`
  request param unchanged (selects the primary). No new endpoints, no permission changes
  (`bpmn-interchange.read/manage` cover it).

### D2 — Collaboration/pool/lane modeling

- `<collaboration>` read: each `<participant id name processRef>` becomes a `BpmnPool(poolId,
  name, processRef)` on the **referenced** process's imported structure (`Pools` finally
  populated; a process referenced by no participant gets an empty pool list as today).
  Participants whose `processRef` is missing/unresolvable → Degraded finding.
- `BpmnLane.PoolId`: lanes stamped with their owning participant's pool id when the process is
  referenced by exactly one participant (the common case); ambiguous multi-participant refs leave
  `PoolId` null + Info finding.
- Black-box pools (participant with **no** `processRef` — a common modeling idiom): recorded as a
  pool entry on the collaboration level of the ANALYSIS (finding Info) and as metadata (D3) where
  message flows reference them; they import no process.
- Export: a structure carrying `Pools` emits `<collaboration>` + `<participant>` wrappers
  (deduped by pool id) around the exported process; single-pool round-trip stable; multi-pool
  export of ONE structure emits its own participant only (exporting a full N-pool document from N
  separate structures is out of scope — documented; the export API is per-definition).

### D3 — Message-flow wiring

- `<messageFlow id name sourceRef targetRef messageRef?>` read at collaboration level. Endpoint
  resolution: each ref resolves to an element (in any imported process) or a participant
  (black-box pool). Classification:
  - Both endpoints resolve to elements with message names (via the root message index and the
    elements' definitions): **matched** — recorded in each involved structure's
    `Properties`-style metadata channel (additive `BpmnAuthoredStructure.MessageFlows`:
    `(FlowId, Name?, SourceElementId?, SourcePoolId?, TargetElementId?, TargetPoolId?,
    MessageName?)`) and an Info finding. If the send-side and receive-side message **names
    differ** (or either is name-less where a name is required): **Degraded** finding naming both
    elements — the wire fabric is name-keyed, so this flow cannot function as drawn.
  - An endpoint on a black-box pool: Info (documentation flow).
  - Unresolvable refs: Degraded finding; the flow is recorded nowhere.
- `MessageFlows` metadata is **wiring documentation** — the engine never reads it (execution is
  name-keyed delivery); validators ignore it; exporters re-emit it (round-trip for the flows whose
  endpoints live in the exported structure's own pool, plus collaboration-level re-emission is
  limited per D2's single-structure export note).

### D4 — Stated cuts

Full N-pool document **export** (per-definition export only); executable enforcement of message
flows (they are wiring metadata; the fabric is name-keyed); correlation (#1001); black-box pool
messaging semantics beyond findings; `<conversation>`/`<choreography>` elements (Dropped +
finding); Studio multi-pool import UX (separate repo; the API is additive-ready).

## In scope

- Importer: collaboration/participant/messageFlow reading; N-process import; pool/lane
  population; the new findings; `BpmnImportedProcess`/`ProcessNodes`;
  `BpmnAuthoredStructure.MessageFlows` + `Pools` population (both fields exist or are additive).
- Exporter: participant wrapper + message-flow re-emission per D2/D3 limits.
- Endpoints: additive response field.
- Tests: two-pool executable collaboration → both nodes imported, pools/lanes populated,
  matched message flow recorded on both structures (send=throw/sendTask, receive=start/catch —
  cover two pairings); name-mismatch flow → Degraded; black-box pool → Info; unresolvable
  participant/flow refs → Degraded; single-process document → byte-identical primary + one-entry
  list (regression pin on the analysis/finding set of an existing golden import); non-executable
  pool → Info; endpoint DTO round-trip; export round-trips (single-pool participant wrapper;
  message-flow re-emission); determinism of finding order. Module docs: Interchange README
  (collaboration section), BPMN README pointer, EXTENSION_POINTS untouched (no engine surface).

## Out of scope

Everything in D4; any engine/runtime/validation-semantics change (`BpmnGraph.Validate` untouched —
pools/flows are structure metadata it ignores; verify no validator rejects populated Pools/
MessageFlows fields).

## Functional requirements

**FR-1 — N-process import.** Every `<process>` in a document imports independently into
`ProcessNodes` with per-process findings; the primary selection and its imported bytes are
identical to today; the silent multi-pool drop is gone (finding-covered).

**FR-2 — Pools/lanes.** Participants populate `BpmnPool` on their referenced structure; lanes
carry `PoolId` in the unambiguous case; black-box pools surface as findings.

**FR-3 — Message-flow wiring.** Resolvable flows are recorded on the involved structures with
endpoint/pool/message-name facts; name mismatches degrade loudly; unresolvable refs degrade; the
engine and validator are indifferent to the metadata.

**FR-4 — Compatibility.** Single-process import (result shape, findings, structure payload) is
byte-identical except the additive fields; all existing interchange tests pass unmodified.

**FR-5 — Export.** Per-definition export emits the participant wrapper and its own-pool message
flows; single-pool round-trip stable.

**FR-6 — Determinism.** Finding order, ProcessNodes order (document order), and metadata ordering
are deterministic.

## Invariants that MUST survive

- Interchange-only: zero changes under `Internal/` (engine), zero runtime changes, `BpmnGraph.
  Validate` byte-identical; the importer never emits a structure the validator rejects.
- Additive API/DTO/model growth only; `bpmn-interchange.*` permission names unchanged
  (`EndpointSecurityTests` untouched or additively extended for the DTO).
- Specs 116–135 suites pass unmodified.

## Success criteria

- All FR tests green; the two-pool end-to-end import pin (both pools, wiring recorded, no silent
  drop) and the single-process byte-identical regression pin both green.
- Full test projects green: BPMN, BPMN Interchange, Activities Runtime, Workflows Runtime,
  ControlFlow, Architecture. Full solution build clean.

## Deviations (implementation)

- **`Internal/BpmnStructureHandler.ReplaceChildren` did not carry `MessageFlows`** (resolved in a follow-up).
  `MessageFlows` was added as a trailing optional constructor parameter on `BpmnAuthoredStructure` so the
  positional reconstruction in `BpmnStructureHandler.ReplaceChildren` (under `Internal/`, untouched per this
  unit's zero-`Internal`-changes invariant) kept compiling — at the cost of dropping message-flow metadata on a
  designer child-replacement (it already passed `Pools` positionally, so pools survived it). The spec's
  surfaces — import, export, analysis — never routed through `ReplaceChildren`, so the invariant held for this
  unit. A follow-up threaded `MessageFlows` through the rebuild; `BpmnStructureHandlerTests` pins that a
  `ReplaceChildren` round-trip preserves both `Pools` and `MessageFlows`.
- **Lanes are still not exported.** The exporter emits the participant/pool wrapper and own-pool message flows
  (D2/D3) but continues not to emit `<laneSet>`/`<lane>` (a pre-existing limitation), so lane `PoolId`s do not
  survive an export→import round-trip; the pool wrapper does. Lane emission was out of D2's stated export scope.
- **Mismatch message flows are recorded, not only matched ones.** D3 attaches "recorded in each involved
  structure's `MessageFlows`" to the matched branch; the implementation also records a name-mismatch flow (both
  endpoints are real elements, so the wiring facts are known and worth documenting) while still emitting the
  Degraded finding. Only unresolvable-ref flows are recorded nowhere (as D3 requires).
- **Analyze element counts now span all processes.** `BpmnImportAnalysis.ElementCounts` previously counted only the
  selected process's elements; N-process import counts every process's elements (single-process documents are
  unchanged). This is the N-aware analyze behavior the additive `ProcessNodes` implies.
