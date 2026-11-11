# Quickstart: Variable lifecycle model

How to exercise the lifecycle once implemented, and how to read the evidence. For reviewers of the provisional spec,
the first two drives are the proof that ADR 0092 works in code.

## Reproduction drives (evidence-first)

1. **Parallel lost update.** Build a workflow with a `Parallel` of two `External` (unclassified) leaves, each taking
   `count` as an input and capturing `count + 1`. Run with `count = 0`.
   - *Before:* result `1`, no diagnostic (silent lost update).
   - *After:* inline, the result is `2` (no gap between read and commit). With two background branches, the result is
     `1` and the second commit raises a lost-update warning (instance diagnostic in Studio and a write-history entry)
     naming `count`, both versions and both writers, with the overwritten value kept so it can be restored.
   - With `ReplaySafe` leaves in a coalescing burst the result is already `2`; after the change it stays correct and the
     no-gap guarantee (US4) is explicit rather than incidental.

2. **Operator correction reaches the step.** Fault a step, correct its variable with `ModifyVariable`, then:
   - *Re-execute* → the step uses the corrected value (new `InvocationId`, fresh inputs).
   - *Retry* → the same snapshot/`InvocationId` is kept (correction not pulled in; this is the right behavior for a
     transient failure).

## Inspecting the lifecycle

- **Versions**: each variable shows its own version and digest in frame/row state; the frame revision is unchanged in
  meaning. Read sets record versions only, plus digests for `Fault` nodes.
- **Read sets**: an invocation's input read set travels with its snapshot (and background admission); session reads are
  per attempt.
- **Write history**: every applied write and lost update is recorded with its metadata; lost-update warnings name both
  writers and keep the overwritten value (the presentation is S-6).

## Policies

- Default: last write wins with a lost-update warning (light tracking, versions only).
- Opt in to `Fault` on an activity type or per node for strict read-modify-write (a counter, a balance): a real lost
  update faults through the incident path; `0 → 1 → 0` applies and is recorded. Spec 266's unordered-writer warning
  points here.

## Background and suspension

- A background step whose input changed between Start and admission is refreshed as a new evaluation (opt-out per node);
  both snapshots are kept and linked.
- A suspended workflow keeps its base on resume; an explicit write on a stale base meets the commit rule (a warning by
  default, a fault under `Fault`).

## Open questions to watch (provisional)

- **A — no inline gap for `External`:** the PoC proves one-work-item Schedule/Start/Invoke with the claim/flush as
  intermediate commits; if it cannot, the finding goes to #3411 and reopens the default (last write wins with a
  warning), which depends on it.
- **B — write-history storage:** rows vs. derived-from-checkpoint, chosen by the large-payload/hot-loop benchmark;
  mandatory metadata is never optional.
