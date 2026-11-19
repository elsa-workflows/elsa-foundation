# Actual partial callback and listener rearm recovery

Source parent `8a8d69fe8671b6e75f9212dc35ec902e6362dbd1`. The [machine receipt](broadcast-partial-resume-recovery.json)
binds the [partial host observations](broadcast-partial-resume-host.json) and
[default-case regression](broadcast-partial-resume-default-regression.json). **These are bounded T051
cases; broader recovery, historical upgrade, final Studio and delivery remain open.**

The actual Workbench imports a parallel pair of signal catches whose outgoing flows loop back to the
same authored listeners. It commits one complete source cohort with one keyed start and two exact
waits. The default-off probe pauses after the first real resume dispatch is accepted. Acceptance is
not the proof: normal actor execution must complete that exact child and persist a distinct new
physical wait for its same authored listener before the runner kills the process.

Checked stopped-state observations show the first original child Completed with two attempts, its
replacement wait suspended under a different bookmark ID/generation, and the other original child
still Suspended with one attempt and its captured wait intact. The original keyed start has already
executed to its exact parked child. This establishes a real partial effect before source acknowledgment.
A later two-listener workflow and different signal-start binding are also created after selection.

The standard host binary contains no probe. It restarts the same private SQLite database, waits for
the real source deadline and reclaims under a different owner and fence 2 after fence 1. The checked
observer captures that actual successor claim through a pinned coherent SQLite snapshot. Delivery
settles the unchanged cohort. The completed original child's full selected-activity projection remains
identical at two attempts; the remaining original child advances from one to two attempts and completes.
Both later waits are suspended. In particular, the first rearmed wait keeps its exact ID, generation,
activity status and attempt count. The keyed start keeps its original physical park registration; no
second keyed execution is created. The later listener's exact waits remain unchanged and the later
binding creates no execution. All four normal workflow API observations have zero incidents.

| Verification | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Actual partial callback/rearm crash → standard-host recovery | 1 | 0 | 0 |
| Actual post-cohort crash regression with the same source/builds | 1 | 0 | 0 |
| Explicit observer controls on the partial/rearm fixture | 8 | 0 | 0 |
| Isolated locked probe/standard builds and checked reader build | 3 | 0 | — |

Both cases verify **1,371 source bindings** and complete probe/standard/reader manifests before and
after execution. The regression's portable receipt references the identical verified manifests in
the partial host receipt rather than repeating them. Both remain linked by content hashes. Separate
owned hosts/ports/content roots were used and stopped; the existing user backend and Studio remained
running. No whole database or identity configuration is published. No production Runtime/provider/
Activities source changed, so the inherited 5,410-case whole-suite result was not rerun here. Locked
builds succeeded with existing warnings; the frozen authoring fingerprint is unchanged.

Three independent component source/evidence reviews are clean. Review added exact preservation of
the already-completed original activity/attempts and exactly one resume attempt for the remaining
original activity. The earlier default case remains the default parameter behavior; use
`-Phase after-resume` for this cut. [Runner instructions](../../../e2e-tests/bpmn/RecoveryProbe/README.md)
explain ownership and observation boundaries. These component reviews do not consume full-PR rounds;
that count remains zero under the six-round bound.

Delayed first-delivery inclusion, competing/empty cohorts, renewed/lost claims, ACK loss/redrive and
retention/GC/republish races remain T051 gates. Remaining T045 semantics/native-composite stories,
actual historical upgraded-host recovery, final current Studio, duplicate-semantics deletion/guards,
full-PR review, green exact-head required checks, protected merge and resulting-main verification stay
open. ADR 0063 acceptance follows delivery. #2898/#2907 and program production acceptance remain
open; Orleans and the task-runner follow-up remain deferred.
