# T044 — BPMN called-control adapter

This receipt covers the adapter component on the source hashes in
[the machine-readable receipt](called-control-adapter.json). It does not establish the actual
admitted called-workflow journeys, fresh-provider recovery or concurrent claims required by T045.

## Implemented boundary

`BpmnProcess` declares the generic `elsa.bpmn` version-1 consumer. Newly compiled call nodes opt into
control only beneath a pinned root with that declaration. Existing accepted executables and ordinary
tasks keep their prior dispatch path. The default adapter resolves the admitted boundary before
semantic effects. An admitted root emits outward signals and terminal authored classification in
one ordinal control batch with its state; it emits no duplicate local parent notification.

The consumer validates the exact local call node, handle, iteration and work token. It folds the
contiguous authenticated prefix through released BPMN 0.5.0 before applying commands or pruning.
Authored error codes precede the ordinary completed leaf's outcome even when actual child cancellation
wins its status. No foreign incident or local leaf fault is invented or absorbed. Retirement from
an earlier accepted interrupt suppresses later notices while still settling the terminal barrier.

## Recovery fixes discovered by controls

Normal activation claims now retain their source until the final decision commits. This allows a
pre-acceptance unavailable control dependency to retry without losing Invoke/Complete/Notify payloads.
Background admission still uses its separate atomic admission path. Clean activation disposal
preserves the unavailable signal; a disposal failure retains ordinary fault handling and both causes.

Retaining claims exposed an inactive coalescing overlay leaving an acknowledged durable copy behind.
Acknowledgement now removes only its accepted exact durable ID. Stale completion cannot delete a
successor's source, and siblings remain available. The former post-boundary durable-claim test depended
on the obsolete accepted copy; it now verifies a distinct sibling's durable claim and completion.
Replay fingerprints and conflict checks remain unchanged.

One shared policy persists delayed unavailable releases for ResumeBookmark, InvokeActivity,
CompleteActivity, NotifyParentActivity, DeliverChildControl and Checkpoint. Producer, notification
and opted-in terminal enrichment classify source unavailability before persistence, including
terminal outbox/output/incident reads. Known contradictions remain refusals and requested cancellation
propagates. Ordinary legacy terminal behavior is preserved.

A separate terminal workflow checkpoint may fail after the root completion has already committed.
The legitimate intermediate state retains the completed root and its prior nonterminal batch, keeps
the workflow Running, and retains the exact Checkpoint source. The fresh terminal ordinal appears
only when that checkpoint succeeds. These controls also prove an immediate sweep cannot bypass the
configured retry delay.

## Finite verification

| Whole affected project | Passed |
|---|---:|
| BPMN runtime | 297 |
| DispatchWorkflow | 339 |
| Activities runtime | 664 |
| Workflow runtime | 2,661 |
| Runtime EF | 1,022 |
| BPMN interchange | 130 |

The 24 adapter cases cover exact authored code/cancellation precedence; signal-plus-failure in one
batch and a contiguous prefix; both escalation interruption modes; prior-callback retirement;
terminal settlement of interrupted work; new/legacy declaration admission; sibling/missing-token/
foreign-protocol refusal; root whole-batch production; and actual in-process Invoke, completion and
notification source outages. Root producer cases construct admitted authority, so they are not
actual DispatchWorkflow authority/recovery evidence. Four full Dispatch feature outage cases cover
producer75, notification90, terminal100 boundary reads and terminal source-outbox reads.

Five restored mutations produced behavioral failures: omit prior retirement (1), apply commands during
folding (2), consume the activation source early (1), omit accepted inactive-overlay cleanup (1),
and omit the terminal-source availability boundary (1 failing/3 passing). Every mutated file was
restored byte-identically. The frozen authoring fingerprint remains
`41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92`.
The first whole-EF run found three ordinary fusion regressions from forcing legacy queue checkpoints
through a durable claim boundary. The fallback preserves the established ResumeBookmark boundary
and leaves other ordinary work coalescible;
without visibility support, unavailable work retains its source but cannot promise the configured
delay. Visibility-capable providers retain checkpoint delay support. The complete affected EF
project now passes. Six added orchestrator controls reject false delayed deferral on
visibility-incapable overlay claims, preserve the exact source and original unavailable signal,
and retry through a reopened provider; both active and deactivated overlays are covered.

Existing analyzer/NuGet warnings are not a clean-warning claim.

## Review and remaining gates

Independent correctness, standards and acceptance component reviews are clean after fixes. These are
component reviews, not full-PR approval; the bounded full-PR loop has not started. T045 actual admitted
host/fresh-EF recovery remains open, as do T024 combined outcomes, T029 ordinary compensation, FR-015
durable broadcast cohorts, duplicate semantic deletion, final current-host/Studio verification,
required exact-head checks and resulting-main verification. ADR0063 and production #2898/#2907 remain open.
