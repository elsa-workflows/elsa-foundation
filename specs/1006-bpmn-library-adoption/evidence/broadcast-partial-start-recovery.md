# Actual crash after one of two saved starts

Source parent `b694efb8c5bda5891a9e96e2b3cc38eacdfd051c`. The [machine receipt](broadcast-partial-start-recovery.json)
binds the [partial-start host observations](broadcast-partial-start-host.json),
[partial callback/rearm](broadcast-partial-start-after-resume.json) and
[post-cohort](broadcast-partial-start-after-cohort.json) regressions.
This qualifies one bounded T051 cut; overall recovery and delivery acceptance remain open.

The first actual Workbench saves a cohort containing two distinct start artifacts/keyed executions and
two exact waits. Its default-off probe forwards the actual selected start dispatcher, preserving the
request, scope, options, cancellation and capability result. Only after a real non-shed Accepted saved
start does it read the canonical source and pause. An acknowledgment alone does not qualify.

The runner waits until that exact keyed workflow has a real suspended `node-park` child, complete API
activity page and physical bookmark. Exactly one saved entry must match the accepted marker. The
other saved target must be absent, and both original catches must remain Suspended with their exact
physical identities. Later matching listeners and another binding are then created through normal APIs.
Only the owned probe process is killed.

A standard host, whose metadata confirms the probe is absent, reopens the same private database. The
checked observer captures its distinct source owner and fence **1 → 2** after the real persisted lease
expires. Recovery reuses the identical cohort. The first keyed start retains its complete checked workflow
projection, API instance, full activity records and parked wait; no checkpoint or invocation drift is accepted.
The second exact keyed execution is created and parks. Each selected artifact has exactly one execution.
Both original catches complete and retire their saved registrations, and their join completes without
incidents. The later binding creates no start, and later waits retain their exact generation/activity facts.

| Verification | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Actual one-of-two starts parked before crash | 1 | 0 | 0 |
| Actual partial-rearm and post-cohort regressions | 2 | 0 | 0 |
| Checked source observer controls, expected two starts/two waits | 8 | 0 | 0 |
| Owned process/pause/broker controls | 10 | 0 | 0 |
| Actual runner cleanup failure controls | 3 | 0 | 0 |
| Separate locked probe and standard builds | 2 | 0 | — |

All three cases verify identical source/build manifests and **1,379 current source bindings** before and
after execution. The fresh host builds have zero errors; existing warnings remain in the private build
logs. Source observer controls explicitly select their eight-case class; membership-reader controls are
outside this single-process crash fixture. Architecture 590/0/0, locked Server restore and map checks
remain inherited from earlier delivery rather than rerun results. No production, reader or dependency
graph source changes are included. The frozen authoring fingerprint stays unchanged.

Three independent component source/actual/portable audits are clean. Review confirmed that the existing
exactly-one accepted-entry check rejects two bindings sharing one artifact-scoped key; the stopped-state
checks also require a different absent saved target. No full-PR round is consumed. Whole databases and
identity configuration remain private. Only owned case hosts were stopped; existing user backend and
Studio remain running.

Actual renewed claims, complete fan-out before lost source acknowledgment/redrive, remaining scopes,
same-artifact behavior and retention/GC/republish races remain T051 work. T045/native-composite,
actual historical upgraded-host recovery, final current Studio, legacy writer/duplicate semantics deletion
and guards, full-PR review, green exact-head checks, protected merge/resulting-main and separate ADR
0063 remain open. #2898/#2907/program production acceptance stays open; Orleans/task-runner deferred.
Public-source-digest GitGuardian incidents still require legitimate resolution before delivery.
