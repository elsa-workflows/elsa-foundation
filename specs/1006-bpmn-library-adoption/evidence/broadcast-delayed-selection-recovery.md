# Actual delayed first selection and start-created wait exclusion

Source parent `3299556ab5e4341c9676e281c0a3264fcfec8e31`. The [machine receipt](broadcast-delayed-selection-recovery.json)
binds the [delayed host observations](broadcast-delayed-selection-host.json) and the
[post-cohort](broadcast-delayed-selection-after-cohort.json)/
[partial callback](broadcast-delayed-selection-after-resume.json) regressions. **Other T051 controls,
historical upgraded-host recovery, final Studio and delivery remain open.**

The first source-built Workbench reads the actual committed publisher source, then pauses before its
first recipient lookup. Its marker and checked stopped state have a declared protocol but no cohort.
Only after observing that marker does the runner create a second two-catch listener and another
signal-start binding through normal authenticated APIs. Neither start artifact has an execution before
the crash; all four physical waits are still suspended. That canonical read and subsequent API order
establish that the publisher commit precedes the added recipients. `RecordedAt` is a record timestamp,
not a substitute for this causal ordering. The two later waits and later binding also have timestamps
after the marker.

An uninstrumented standard host restarts the same private database, waits for the actual source deadline
and reclaims under a distinct owner and fence **1 → 2**. Its first saved cohort contains **both start
bindings and all four exact prior bookmark IDs/generations**. Both waiter workflows complete their
catches and parallel joins. Each selected start uses its original deterministic key and creates exactly
one execution. This proves the settled delivery-time rule: recipients added after publisher commit,
before first matching, join that event.

Each signal-start workflow then waits on the **same published signal**. Those matching waits are created
by delivery after the cohort is selected. Both remain physically suspended at attempt one and are absent
from the saved resume set. This proves pre-start wait exclusion through actual actors and persisted
checkpoints. The source becomes Delivered with its first selected cohort unchanged. All normal workflow
API observations have zero incidents.

| Verification | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Actual delayed selection and start-created matching wait exclusion | 1 | 0 | 0 |
| Actual post-cohort crash regression | 1 | 0 | 0 |
| Actual partial callback/rearm crash regression | 1 | 0 | 0 |
| Explicit observer controls on the four-resume/two-start fixture | 8 | 0 | 0 |
| Separate probe/standard builds and checked reader build | 3 | 0 | — |

All three runs verify the same **1,371 source bindings** and complete probe/standard/reader manifests
before and after execution. PE metadata requires probe presence and its absence from the standard
build. The two regression receipts reference identical verified manifests in the delayed receipt;
content hashes bind all three. Only owned case processes/roots/ports were used and stopped; the
existing user-facing backend and Studio remained running. Whole databases and identity configuration
remain private. Production Runtime/provider/Activities source and the frozen authoring fingerprint
are unchanged. Three independent component source/actual-evidence/portable-receipt audits are clean;
full-PR rounds remain zero. [Runner instructions](../../../e2e-tests/bpmn/RecoveryProbe/README.md)
cover the three modes and explicit observer expected-count options.

Fresh CI on the source parent confirms the preceding fixture/tool repair: Build & test, architecture,
the previously failing PR shards and maps are green. The three public-source-digest GitGuardian incidents
still need legitimate resolution. This new pushed increment needs its own checks; draft full-tier skips
are not qualification and no check or protection is bypassed.

T051 still needs complete/empty competing snapshots, real renew/lost-claim and partial-start cuts,
ACK loss/redrive, same-artifact shedding, retention/GC/republish races and remaining scope controls.
T045 semantics/native-composite, actual historical upgrade, final current Studio, legacy writer and
duplicate-semantics deletion/guards, full-PR review, exact-head required checks, protected merge and
resulting-main verification remain separate gates. ADR 0063 follows delivery. #2898/#2907 and program
production acceptance stay open; Orleans and the task-runner follow-up remain deferred.
