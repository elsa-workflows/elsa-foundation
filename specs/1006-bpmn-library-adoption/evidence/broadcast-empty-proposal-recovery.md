# Actual empty prepared proposal loses to a populated winner

Source parent `fc5948641590f4f74d79e234a2a3f8f91f5b81d6`. The [machine receipt](broadcast-empty-proposal-recovery.json)
binds the [actual empty-proposal race](broadcast-empty-proposal-host.json),
[populated competition regression](broadcast-empty-proposal-competing-proposal.json) and
[saved-empty decision regression](broadcast-empty-proposal-empty-cohort.json).
This qualifies the empty-loser/populated-winner direction only. The opposite populated-loser/empty-winner
race and overall T051 acceptance remain open.

The initial listener and start binding use another event. The actual old host prepares a **nonnull empty
proposal**, while the canonical durable source still has **no cohort**. Only then do APIs create two matching
waits and a binding. After safely suspending its owned process, the runner starts a standard peer on the
same runtime/membership databases and lock directory. Actual checked membership proves distinct
incarnations, natural old-member expiry and a live peer. The distinct successor source claim advances
fence **1 → 2**.

The peer saves and delivers **one start and two exact waits**, with version-one retention acceptance.
The matching listener completes its catches/join; exactly one keyed start parks on the same signal,
and its new matching wait stays outside the saved decision. The original unmatched listener's complete
checked projection is unchanged: both physical waits remain Suspended at attempt one, and its start
artifact creates no execution. These observations do not claim a separate audit of that unmatched
binding's entire stored row.

The old process resumes only after the populated winner is Delivered. Its actual provider installation
attempt receives `RuntimePostCommitOutboxStaleClaimException` without requested cancellation. The
complete checked source, workflow observations and workflow inventory remain identical afterward.
An uninstalled empty proposal cannot erase a winner; separately, the saved-empty regression confirms
that an installed empty decision continues to exclude later matching recipients.

| Verification | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Actual empty prepared loser/populated saved winner | 1 | 0 | 0 |
| Actual populated competition and saved-empty regressions | 2 | 0 | 0 |
| Checked source/membership observer controls | 13 | 0 | 0 |
| Owned process/pause/broker controls | 10 | 0 | 0 |
| Actual runner cleanup failure controls | 3 | 0 | 0 |

All three final cases verify identical source/build manifests and **1,376 current source bindings** each
before and after execution. Separate controllers prevent temporary configuration carry-over. Standard
assembly metadata confirms no recovery probe. The initial exploratory case passed; instructions and
the assertion diagnostic were then narrowed to the observed unmatched-listener/no-start facts, before
these final runs. No production, probe, reader or dependency graph changes are included. Architecture
590/0/0, locked Server restore and maps are inherited from the immediately preceding delivery and are
not presented as rerun results. The frozen authoring fingerprint stays unchanged.

Three independent component source/actual/portable reviews are clean; full-PR rounds remain zero.
Only owned case processes were stopped. Whole databases and identity configuration remain private.
The remaining T051 cases, T045/native-composite, actual historical upgraded-host recovery, final current
Studio, legacy writer/duplicate semantics deletion and guards, full-PR review, green exact-head checks,
protected merge/resulting-main and separate ADR 0063 remain open. #2898/#2907/program production
acceptance remains open; Orleans/task-runner are deferred. Existing public-source-digest GitGuardian
incidents still require legitimate resolution.
