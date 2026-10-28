# Actual populated prepared proposal loses to an empty winner

Source parent `3c5f2830ead8256e1750a9a7f9be972ccc038dcb`. The [machine receipt](broadcast-empty-winner-recovery.json)
binds the [actual empty-winner race](broadcast-empty-winner-host.json),
[populated competition](broadcast-empty-winner-competing-proposal.json) and
[empty losing proposal](broadcast-empty-winner-competing-empty-proposal.json) regressions.
This qualifies the opposite competition direction; broader T051 and delivery gates remain open.

The old actual host prepares **one start/two waits** while its durable source has no cohort. Through
normal host APIs, the runner then submits a CancelWorkflow alteration plan and unpublishes that start's
publication slot. The plan completes with exactly one successful target; the workflow and both exact
prepared catches are Cancelled, with zero incidents. The publishing response has no active publication.

Before suspending the old process, the checked observer independently verifies the physical result:
cohort absent, canceled original workflow with no bookmarks, no active matching binding in the complete
source-stimulus index, and only unmatched later waits. These normal operations remove recipients;
no application row, claim, clock or membership identity is edited by the test.

After safe owned suspension outside both SQLite writers, a standard peer uses the shared runtime and
membership stores and lock directory. Actual checked membership proves distinct incarnations, old
initially live then naturally expired, peer live. The observer captures the distinct source owner/fence
**1 → 2**. The peer installs and settles a **nonnull zero-start/zero-resume winner**, with version-one
retention acceptance. All checked workflow projections and the complete inventory stay unchanged;
no start execution appears.

Only after the empty source is Delivered does the runner resume the old process. Its populated proposal
actually enters provider installation and receives `RuntimePostCommitOutboxStaleClaimException`, with
cancellation not requested. The complete source, workflow observations and workflow inventory remain
identical after this rejection. Thus an old populated candidate cannot replace the accepted empty winner
or execute its withdrawn recipients.

| Verification | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Actual populated prepared loser/empty saved winner | 1 | 0 | 0 |
| Actual populated competition and empty losing proposal regressions | 2 | 0 | 0 |
| Checked source/membership observer controls, expected zero recipients | 13 | 0 | 0 |
| Owned process/pause/broker controls | 10 | 0 | 0 |
| Actual runner cleanup failure controls | 3 | 0 | 0 |

All three runs verify identical source/build manifests and **1,379 current source bindings** each before
and after execution. The additional bindings include the invoked public alteration helper and shared
API/lifecycle helpers. Standard assembly metadata confirms no recovery probe. Production, probe and reader source and
the dependency graph are unchanged; only the test scenario, helper composition and instructions changed.
Architecture 590/0/0, locked Server restore and maps remain inherited from the earlier delivery rather
than rerun results. Frozen authoring stays unchanged. Whole databases/configuration stay private, and
only owned case hosts are stopped; the existing user backend and Studio remain running.

Three independent component source/actual/portable audits are clean; full-PR rounds remain zero.
Actual renewed claims, partial starts, ACK loss/redrive, remaining scope and retention/GC/republish
cases remain T051 work. T045/native-composite, actual historical upgraded-host recovery, final current
Studio, legacy writer/duplicate semantics deletion and guards, full-PR review, green exact-head checks,
protected merge/resulting-main and separate ADR 0063 remain open. #2898/#2907/program production
acceptance remains open; Orleans/task-runner are deferred. Public-source-digest GitGuardian incidents
still require legitimate resolution before delivery.
