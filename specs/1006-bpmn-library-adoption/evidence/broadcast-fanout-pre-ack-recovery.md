# Actual complete fan-out before the source acknowledgment write

Source parent `8a558c7fb8e9f80ba191dc6eaf1bb874363a37c6`. The [machine receipt](broadcast-fanout-pre-ack-recovery.json)
binds the [pre-acknowledgment crash](broadcast-fanout-pre-ack-host.json),
[partial-start](broadcast-fanout-pre-ack-after-start.json) and
[partial-rearm](broadcast-fanout-pre-ack-after-resume.json) regressions.
This proves a crash before source acknowledgment recording. Ambiguous acknowledgment after a
committed source-completion write remains a separate cut.

The default-off probe forwards the actual selected intent dispatcher, including required-source
capability and ordinary intent dispatch. Only after the real PublishStimulus context handler returns
does it reread the canonical source and pause. The processor is still awaiting that dispatcher call,
so it cannot record source success. Other intent kinds retain their ordinary path.

The runner independently waits for effects: both exact original catches and their join are Completed,
and the saved keyed start has a real suspended child. It creates later matching waits/binding, then
records complete incident-free API instance/activity views for both recipient workflows. Checked
physical state verifies the retired original waits and completed callback attempts, the parked start,
the accepted cohort, and a source that is still **Delivering**. A handler return or queued command
acknowledgment alone is insufficient. Only the owned probe process is killed.

A standard host with no probe reopens the same private SQLite database. The checked observer captures
its distinct source owner/fence **1 → 2** after real lease expiry. It reuses the identical saved cohort
and finally records Delivered. Both recipient workflows retain their entire checked projections and
full API instance/activity records. Callback attempts remain two; the parked start retains its exact
physical wait and checkpoint records. The later binding produces no start, and later waits keep their
original IDs, generations and activity facts. The publisher's own queued continuation may finish;
it is not a recipient claimed to remain unchanged.

| Verification | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Actual complete fan-out/crash before source ACK write | 1 | 0 | 0 |
| Actual partial-start and partial-rearm regressions | 2 | 0 | 0 |
| Checked source observer controls | 8 | 0 | 0 |
| Owned process/pause/broker controls | 10 | 0 | 0 |
| Actual runner cleanup failure controls | 3 | 0 | 0 |
| Separate locked probe and standard builds | 2 | 0 | — |

All three cases verify identical source/build manifests and **1,379 current source bindings** before
and after execution. Fresh host builds have zero errors; existing warnings remain in private logs.
Normal assembly metadata confirms that no recovery probe is present. Production, observer and
project graph source are unchanged; the frozen authoring fingerprint is preserved. Architecture
590/0/0, locked solution restore and map checks remain inherited rather than rerun results. Whole
databases and identity configuration stay private; only owned case processes are stopped.

Three independent component source/actual/portable audits are clean; full-PR rounds remain zero.
Ambiguous committed source acknowledgment/redrive, actual pre-dispatch renewal, same-artifact and
remaining scope/retention/GC/republish cases remain T051 work. T045/native-composite, actual historical
upgraded-host recovery, final current Studio, legacy writer/duplicate semantics deletion and guards,
full-PR review, green exact-head checks, protected merge/resulting-main and separate ADR 0063 remain
open. #2898/#2907/program production acceptance remains open; Orleans/task-runner are deferred.
Public-source-digest GitGuardian incidents still require legitimate resolution before delivery.
