# Actual competing broadcast proposal rejection

Source parent `44acc05163fe2ac7cbdeb687b2488d6c8c099350`. The [machine receipt](broadcast-competing-proposal-recovery.json)
binds the [competing-host observations](broadcast-competing-proposal-host.json) and four real crash regressions.
This qualifies the bounded prepared stale-proposal case. T051, historical upgraded-host recovery, final Studio
and production acceptance remain open.

An owned source-built Workbench prepares one start and two exact waits, then pauses before installing
its cohort. Normal APIs create another matching listener and binding. The runner reserves both runtime
and membership SQLite writers, validates the owned process birth and exact arguments again, suspends
that process and releases both reservations without editing application rows or clocks.

A standard peer, whose binary contains no recovery probe, uses the same runtime/membership databases
and shared lock directory. The observer checks coherent private snapshots through the actual EF
membership parser: distinct host IDs and incarnations, old member initially live, then naturally expired
while the peer remains live. It also captures the actual successor source owner and fence **1 → 2**.

The peer installs its different complete winner: two starts and four original physical waits. Both listeners
complete their exact catches/join. Each artifact has one deterministic keyed execution parked on the
same signal, whose newly created matching wait is excluded from this event. All observed workflows
have zero incidents.

Only after the winner is Delivered does the runner resume the old process. Qualification requires the
old call to actually enter the provider, with cancellation not requested, and receive
`RuntimePostCommitOutboxStaleClaimException` for the original source/owner/fence. Cancellation before
that call does not qualify. The complete checked source, workflow observations and workflow inventory
remain identical after rejection. The old prepared proposal neither replaces the winner nor adds effects.

| Verification | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Actual competing prepared proposal | 1 | 0 | 0 |
| Actual empty/delayed/post-cohort/partial-rearm regressions | 4 | 0 | 0 |
| Checked source and membership observer controls | 13 | 0 | 0 |
| Owned pause/reservation/broker cleanup controls | 10 | 0 | 0 |
| Actual runner cleanup failure controls | 3 | 0 | 0 |
| Whole architecture project | 590 | 0 | 0 |
| Locked Server restore and generated map check | 2 | 0 | — |

Each final host case uses a separate PowerShell controller and identical source/build manifests, checked
before and after execution. The machine receipt records the exact binding count. Probe and standard
builds are separate; assembly metadata confirms that the standard binary has no probe. Whole databases
and identity configuration stay private. Only minimal observations and content manifests are portable.

Verification exposed and corrected fixture mechanics before qualification: cluster startup requires a
shared lock directory; a Python broker keeps stopped-child handling outside the .NET controller; fresh
controllers prevent temporary membership configuration from carrying into another case. Independent
review also found that one cleanup exception could skip the remaining cleanup and receipt. The actual
prior finally block fails the injected control; repaired observer/peer/broker failure cases all emit a failed
receipt while attempting each owned cleanup. These unsuccessful attempts are not counted as passes.

Three independent component reviews cover the source, actual evidence and portable receipt. No full-PR
round is consumed. Production source and old external package records are unchanged; the reader adds
only required Cluster project references. Frozen authoring is unchanged. Existing user backend and
Studio processes remain running.

Empty competing decisions, real renewed claims, partial starts, ACK loss/redrive, remaining scopes and
retention/GC/republish races remain T051 work. T045/native-composite, historical upgraded-host recovery,
final current Studio, legacy writer/duplicate semantics deletion and guards, full-PR review, green
exact-head checks, protected merge/resulting-main and the separate ADR 0063 PR remain open.
#2898/#2907/program production acceptance remains open; Orleans/task-runner are deferred. Existing
public-source-digest GitGuardian incidents require legitimate resolution before delivery.
