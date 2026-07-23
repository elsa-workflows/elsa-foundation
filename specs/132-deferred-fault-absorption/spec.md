# 132 — Fix deferred seam-B fault absorption (issue #989): stop the fault-marker metadata leak; un-gate error event subprocesses (runtime fix + BPMN consumer proof)

## Goal

Fix **issue #989**: when a child-fault evaluation stages seam-B `RequestChildFaultAbsorption`
(spec 115) and returns a **Defer** continuation that also schedules children, the absorbed
incident resolves correctly but the composite is later faulted anyway — blaming the **newly
scheduled, innocent child** with a fabricated fault seeded by the original incident. Root cause
(verified): the fault-evaluation work item's fault-scoped `CommandMetadata` keys
(`runtime.childFaulted`, `runtime.incidentId`, the completed-child id) are **inherited wholesale**
by the child work items that only the Defer branch derives; they survive every downstream hop and
make the new child's clean completion re-enter the parent as a fault of the original incident.

The fix is **Shape A — strip fault-scoped keys when deriving non-fault work items** (smallest
blast radius; no legitimate downstream consumer of those keys exists; the spec-112/115 same-drain
invariants are untouched). With the runtime fixed, this unit also **un-gates the spec-128 error
event subprocess** (deleting exactly the one validation rule + one importer drop the stated cut
installed) and adds the **missing regression coverage** on both the runtime and BPMN sides —
including the latent shipped bug this exposed: a spec-120 **error boundary routing to a task**
(absorption + Defer) has been broken on main since spec 120 merged, invisible because every
shipped test routes error boundaries to end events.

## Context (verified root cause, origin/main ≥ 6d621960d; line numbers drift — verify at implementation)

- **The leak.** `ChildFaultParentEvaluation.TryBuildAsync` stamps
  `RuntimeMetadataKeys.ChildFaulted = true`, `RuntimeMetadataKeys.IncidentId`, and
  `CompletedChildActivityExecutionId` into the parent-evaluation work item's `CommandMetadata`
  (~`ChildFaultParentEvaluation.cs:58-61`). In
  `WorkflowParentActivityCompletionSchedulerWorkHandler`, `NewChildActivityScheduleWorkItems`
  (~line 805) copies the source item's `CommandMetadata` wholesale onto **newly scheduled
  children** — reached only from `CommitDeferredParentActivityAsync` (the Defer branch; Complete
  cannot schedule). The keys then survive every hop verbatim:
  `WorkflowScheduleActivitySchedulerWorkHandler` (~366) → `WorkflowInvokeActivitySchedulerWorkHandler`
  (~1282/1059) → `WorkflowCompleteActivitySchedulerWorkHandler` (~137). When the new child
  completes cleanly, `IsChildFaulted`/`ReadIncidentId` (~1099-1106) misclassify the completion as
  a fault of the original incident; the BPMN engine (incident already `Resolved`, no catcher for
  the new child) falls through and fabricates `bpmn.child.faulted` naming the innocent child.
- **The symmetric latent leak.** `NewCompletionWorkItem` (~line 1040) also copies the poison keys
  onto the composite's **upward** completion item. Untested today only because shipped Complete
  topologies end at a root; a fault-handling grandparent would misread it the same way.
- **What is NOT the cause** (verified, keep untouched): absorption incident-resolution is
  symmetric and correct in both commit paths (`incidents: cancellationChanges.Incidents` in both
  `CommitDeferredParentActivityAsync` and `CommitCompletedParentActivityAsync`);
  `BlockingIncidentWorkflowFaultObserver` does not fire (incident resolves in-drain);
  `WasParentCompletionProcessed` dedup is correct.
- **Key readers** (the safety case for stripping): the only readers of `ChildFaulted`/`IncidentId`
  from `CommandMetadata` are the parent-completion handler reading its **own** fault-evaluation
  item (unaffected — that item is built by `ChildFaultParentEvaluation`, not derived) and
  `ReplaySafeFusionDriver` (~266), whose fusability classification **wants** the keys absent on
  ordinary items. Nothing expects fault markers on a `ScheduleActivity` item or an ordinary
  completion chain.
- **Coverage holes** (why this survived): spec-115's `ChildFaultAbsorptionExecutionTests` cover
  absorption+Defer only WITHOUT scheduling in the fault evaluation, and absorption+Complete; no
  test stages absorption + Defer + child schedules. Spec-120's two error-boundary tests both route
  the boundary to an **EndEvent**. Spec-128's tripwire test
  (`BpmnEventSubprocessTests` ~274-312) documents the buggy behavior and is the flip target.
- **The spec-128 stated cut to remove**: one validation rule in `ValidateEventSubprocesses`
  ("error event subprocess … not executable in this slice") + one importer drop
  ("error-triggered … was dropped"); all error engine wiring
  (`AbsorbChildFaultThroughErrorEventSubprocess`, the `OnChildFaultedAsync` branch, exporter
  emission) is already merged inert.

## Design decisions

### D1 — Runtime fix (Shape A: strip fault-scoped keys on derivation)

- In `WorkflowParentActivityCompletionSchedulerWorkHandler`:
  - `NewChildActivityScheduleWorkItems`: the inherited `CommandMetadata` for newly scheduled
    children **omits** `RuntimeMetadataKeys.ChildFaulted`, `RuntimeMetadataKeys.IncidentId`, and
    `RuntimeMetadataKeys.CompletedChildActivityExecutionId` (fault-evaluation identity keys are
    meaningful only on the item they were minted for).
  - `NewCompletionWorkItem`: same strip on the composite's upward completion item (closes the
    latent grandparent leak in the same motion — one shared helper, e.g.
    `WithoutFaultEvaluationMetadata`, single home).
- Nothing else changes: no payload shapes, no `CanHandle` predicates, no drain/observer/intent
  scheduling, no incident machinery. The spec-112/115 same-drain invariants and all absorption
  validation rules are byte-identical.
- **Tripwire**: if implementation finds any additional derivation site that inherits
  `CommandMetadata` from a fault-evaluation item onto a non-fault item (beyond the two named),
  strip there too and report it; if a legitimate consumer of the keys on derived items surfaces,
  STOP and report (contradicts the verified reader inventory).

### D2 — Runtime regression coverage (the missing spec-115 case)

New tests in `ChildFaultAbsorptionExecutionTests` (or a sibling file, same doubles):

1. **Absorption + Defer + child schedule in the fault evaluation** (the #989 shape): parent
   absorbs the fault AND schedules a new child in `OnChildFaultedAsync`; the new child completes
   cleanly; assert the parent receives an ordinary `OnChildCompletedAsync` (not a fault), the
   composite completes, the absorbed incident stays `Resolved`, and no new incident exists.
2. **Derived-metadata hygiene pin**: the child work items derived from a fault evaluation carry
   none of the three fault-scoped keys (direct assertion on the enqueued items — cheap and pins
   the fix shape itself).
3. **Grandparent chain** (latent leak): composite absorbs+Completes under a fault-handling
   grandparent; the grandparent receives the composite's completion as a completion. (If the
   harness cannot build the three-level fault chain cheaply, document-and-skip with a note —
   tripwire, report.)

### D3 — BPMN un-gate + consumer proof

- Delete the spec-128 stated-cut validation rule and importer drop (exactly two sites; exporter
  already emits). The error event subprocess becomes author-reachable and importable.
- Flip the spec-128 documented-out tripwire test into the real end-to-end assertion: child fault
  → error event subprocess absorbs (incident `Resolved`), scope interrupted
  (`StopOtherLiveWork`), body runs, scope completes normally.
- Restore/extend the error interchange round-trip (the degrade test flips back to a round-trip;
  keep a degrade test only for genuinely malformed shapes per spec-128 D7).
- **Add the spec-120 latent-bug regression**: an error boundary whose outbound routes to a
  **task** (not an end event) — fault absorbed, boundary path's task runs and completes, process
  completes. This is the shipped-surface repro of #989, now pinned green forever.

### D4 — Stated cuts

Error-code matching (still cut, unchanged); tier-2 event subprocesses; any broader metadata
inheritance audit beyond fault-scoped keys (a chip may be filed if implementation notices other
suspicious wholesale inheritance, but this unit strips only the verified-poison keys).

## In scope

D1 runtime strip (+ helper), D2 runtime tests, D3 BPMN un-gate + tests, module docs touched by the
un-gate (BPMN README/EXTENSION_POINTS + Interchange README lose the stated-cut notes), and closing
issue #989 via the PR (`Fixes #989`).

## Out of scope

Everything in D4; no changes to seams A/B/C surfaces, payload shapes, `DeliverSignal`, fusion, or
the incident observer.

## Functional requirements

**FR-1 — Fix.** Absorption + Defer + child schedules completes the composite normally: incident
`Resolved`, new child's completion delivered as a completion, no fabricated fault, no observer
fault. Absorption + Complete byte-identical to today.

**FR-2 — Hygiene.** Work items derived from a fault evaluation (child schedules AND the upward
completion item) carry no fault-scoped metadata keys; the fault-evaluation item itself is
untouched.

**FR-3 — Non-regression.** All spec-112/115 validation rules and rejection tests byte-identical;
`ReplaySafeFusionDriver` classification unaffected (its keys-absent expectation now holds more
often, never less); full existing suites pass unmodified.

**FR-4 — BPMN error event subprocess end-to-end** (spec-128 FR-5 finally satisfied): child fault
absorbed via seam B, scope interrupted, body runs, scope completes; validation accepts; import
round-trips.

**FR-5 — Spec-120 latent surface fixed**: error boundary → task routes, runs, completes.

**FR-6 — Determinism.** Identical runs produce identical ids/orderings; the strip is
deterministic (key-set based, no content inspection).

## Invariants that MUST survive

- Same-drain incident semantics (spec 112/115) untouched; absorption stays child-fault-evaluation-
  only, matching-incident-only, at-most-once, Defer/Complete-only.
- No new work-item kinds, no payload changes, no frozen-name changes.
- BPMN: schema v1; the un-gate deletes exactly the stated-cut rule + importer drop; all other
  spec-128 behavior byte-identical; behaviors decision-only.
- Spec 119–128 suites pass unmodified EXCEPT the spec-128 files the un-gate legitimately flips
  (the stated-cut validation/degrade tests and the documented-out tripwire).

## Success criteria

- D2 + D3 tests green, including the two new end-to-end proofs (error event subprocess; error
  boundary → task).
- Full test projects green: Activities Runtime, Workflows Runtime, BPMN, BPMN Interchange,
  ControlFlow, Architecture. Full solution build clean.
- Issue #989 closed by the PR.
