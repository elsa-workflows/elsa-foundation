# Quickstart: Variable lifecycle model

How to exercise the lifecycle once implemented, and how to read the evidence. For reviewers of the provisional spec,
the first two drives are the proof that ADR 0092 works in code.

## Reproduction drives (evidence-first)

1. **Parallel lost update.** Build a workflow with a `Parallel` of two `External` (unclassified) leaves, each taking
   `count` as an input and capturing `count + 1`. Run with `count = 0`.
   - *Before:* result `1`, no diagnostic (silent lost update).
   - *After:* the second commit faults as a lost update (default policy), naming `count`, both versions and the writers.
   - With `ReplaySafe` leaves in a coalescing burst the result is already `2`; after the change it stays correct and the
     no-gap guarantee (US4) is explicit rather than incidental.

2. **Operator correction reaches the step.** Fault a step, correct its variable with `ModifyVariable`, then:
   - *Re-execute* → the step uses the corrected value (new `InvocationId`, fresh inputs).
   - *Retry* → the same snapshot/`InvocationId` is kept (correction not pulled in; this is the right behavior for a
     transient failure).

## Inspecting the lifecycle

- **Versions**: each variable shows its own version and digest in frame/row state; the frame revision is unchanged in
  meaning.
- **Read sets**: an invocation's input read set travels with its snapshot (and background admission); session reads are
  per attempt.
- **Write history**: every applied write and conflict is recorded with its metadata; `LastWins` overwrites and
  lost-update resolutions are visible (the presentation is S-6).

## Policies

- Default is `Fault`. Set `LastWins` on an activity type (aggregators) or per node when an overwrite is intended; it is
  always recorded.

## Background and suspension

- A background step whose input changed between Start and admission is refreshed as a new evaluation (opt-out per node);
  both snapshots are kept and linked.
- A suspended workflow keeps its base on resume; it faults only on an explicit write against a stale, changed base.

## Open questions to watch (provisional)

- **A — no inline gap for `External`:** the PoC proves one-work-item Schedule/Start/Invoke with the claim/flush as
  intermediate commits; if it cannot, the finding goes to #3411.
- **B — write-history storage:** rows vs. derived-from-checkpoint, chosen by the large-payload/hot-loop benchmark;
  mandatory metadata is never optional.
