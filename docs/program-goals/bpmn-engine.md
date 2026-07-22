# BPMN Engine

## Status

Active (Phase 3).

## Area

Executable BPMN 2.0 support in the Workflows Runtime: the `BpmnProcess` container engine, BPMN
XML/BPMNDI interchange, and the runtime seams the engine consumes.

## Steward(s)

Sipke plus the active BPMN control-room session and its worker agents.

## Purpose

Give Elsa a first-class executable BPMN engine — authored or imported BPMN 2.0 processes run on the
Workflows Runtime with engine-owned token semantics — while every runtime capability the engine
needs (subtree cancellation, fault absorption, live-child reads, scoped-variable reads) lands as a
general, spoof-proof runtime seam rather than a BPMN-private side channel.

## In scope

- The BPMN runtime module (`src/Elsa/Activities/Bpmn/`): token engine, gateways, events, boundary
  events, multi-instance, cycles, and the Phase 3 constructs (compensation, transactions/cancel
  events, escalation, event subprocesses, call activity, executable collaborations).
- BPMN 2.0 XML + BPMNDI interchange (`src/Elsa/Activities/Bpmn/Interchange/`).
- Runtime seams motivated by the engine but designed provider-neutral (specs 112, 115, 119, 123).

## Out of scope

- Studio authoring UX (separate repository `elsa-foundation-studio`; pulled in only on request).
- Non-BPMN consumers of the runtime seams (they are welcome, but their work belongs to their own
  buckets).

## Active objectives

- Phase 3 delivery, sequenced by the control room: specs 123 (scoped-variable read seam +
  collection-mode multi-instance, PR #965), 124 (compensation, PR #966), 125 (transactions/cancel
  events, PR #970), 126 (runtime seam C — child→parent notification, PR #974), and 127 (escalation, PR #975)
  are merged; next is event subprocesses (riding seam C), then call activity and executable
  collaborations.
- Carried follow-ups from Phase 2: terminate/fault teardown through seam A (`CancelLiveWork` is
  logical-only today), error-code matching for error boundaries, non-interrupting timer repetition,
  `completionCondition`, MI output aggregation, `standardLoopCharacteristics`, unbounded-loop
  guardrails.

## Linked surfaces

- Program record and progress table: [docs/plans/bpmn-phase2-events-tier.md](../plans/bpmn-phase2-events-tier.md)
  (Phases 1–2 complete as of 2026-07-22; carries the per-unit PR table and seam facts).
- Shipped specs: `specs/108`, `112`, `115-runtime-handled-child-fault`, `116`–`122`; in flight:
  `specs/123-runtime-scoped-variable-read`.
- Runtime seam documentation: the runtime `EXTENSION_POINTS.md` entries for
  `RequestChildSubtreeCancellation`, `RequestChildFaultAbsorption`, `GetLiveChildActivities`, and
  the trigger-metadata read seam.

## Current roadmap notes

Phase 3 runs the proven per-slice loop (spec first, worker in isolated worktree, independent
verification, merge-commit after CI green). Recommended order starts small (spec 123 warm-up) and
takes compensation as the first large construct; compensation's reverse-order log is a prerequisite
for transactions/cancel events.

## Drift / review notes

Registered 2026-07-22 after the Phase 3 control-room drift check: three phases of durable
multi-session work had been coordinating solely through the plan document, which met the registry's
own threshold for a named bucket. The plan document remains the detailed program record; this file
is the registry-level surface.

## Removal or completion conditions

Complete when the Phase 3 scope list has shipped or been explicitly dropped, remaining follow-ups
are either shipped or filed as ordinary report findings, and the program record marks the program
closed. If the BPMN module later moves to a dedicated workspace repository, this bucket transfers
or closes in favor of that repository's planning surface.
