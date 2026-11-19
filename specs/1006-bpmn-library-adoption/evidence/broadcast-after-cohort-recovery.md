# Actual host recovery after recipient selection

Source parent `b9e98f985ed5e5ee4f046c39b45ad19d10bac269`. The [machine receipt](broadcast-after-cohort-recovery.json)
and [actual host observations](broadcast-after-cohort-host.json) bind this bounded T051 case. **Other T051
cuts, legacy upgraded-host recovery, final Studio, duplicate-semantics deletion and delivery remain open.**

An isolated source-built Workbench published one signal-start workflow and armed two parallel signal
catches. Its actual publisher, committer, processor, checked EF store and router committed the complete
first-delivery recipient cohort. A default-off test feature paused after that provider call returned.
The runner then created a later signal-start binding and another two-catch listener, and killed only its
owned process. A checked snapshot proved the source was still Delivering under fence 1, its selected
keyed start did not exist, and both exact selected waits retained their captured physical generations.

The standard Workbench binary contains no probe feature. It restarted on the same SQLite database,
waited for the real persisted lease deadline and reclaimed the source under a different owner and
fence 2. A read-only observer began before startup, pinned that actual claimed SQLite view and backed
it up coherently; the real checked provider verified the owner/fence and source/cohort. Missing this
view fails the proof even if final delivery succeeds. No clock, deadline, source, bookmark or actor state
was edited to obtain recovery.

Normal delivery then settled the source as Delivered with the identical saved cohort. Both selected
catch activity IDs completed, their selected bookmarks disappeared and the parallel join completed.
Exactly the original deterministic keyed start reached its next signal wait, proved by its suspended
child and physical bookmark. The later binding created no execution. The later listener retained its
exact bookmark IDs, generations, activity statuses and attempt counts. Normal APIs reported zero
incidents for the original waiter, publisher, keyed start and later listener. These are actual callback
and checkpoint observations; mailbox acceptance alone is insufficient.

| Verification | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Final actual post-cohort crash → standard-host recovery case | 1 | 0 | 0 |
| Explicit owned-fixture observer controls | 8 | 0 | 0 |
| Isolated locked Release Workbench builds | 2 | 0 | — |
| Locked checked-reader build | 1 | 0 | — |

The observer controls cover unchanged snapshot bytes, wrong expected hash, live sidecar, missing exact
source, divergent source projection, changed cohort fingerprint, missing publisher and a pinned WAL view
while another writer commits. They qualify observation mechanics, not host execution by themselves.
The fixture is supplied explicitly; generic discovery skips it rather than reading arbitrary local data.

The final run checks **1,371 source bindings**, the full current parent commit, probe/standard/reader
DLL and dependency manifests before and after execution, and PE metadata proving probe presence and
standard-build absence. All three released BPMN packages are 0.5.0. The frozen authoring fingerprint
remains `41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92`.
Private whole databases and identity configuration are not published. The existing user-facing backend
and Studio were left running; all recovery-case hosts were separately owned and stopped.

Three independent component reviews found proof and harness issues that were fixed: exact keyed start
identity, completed selected children, unchanged later generations/activity facts, actual parked child,
complete source/dependency binding, feature extensibility and an observer-test import. Two initial
harness attempts rejected asynchronous admission and the composite parent's valid Running status;
these assumptions were corrected. Three subsequent actual recovery runs passed; the committed receipt
binds the final corrected case. This is not a full-PR review round. Runtime/provider/Activities production
source is unchanged from the prior 5,410-case whole-suite pass; those suites were not repeated here.

[Runner/build instructions](../../../e2e-tests/bpmn/RecoveryProbe/README.md) describe the default-off
instrumentation, process ownership and checked snapshot boundaries. T051 still requires delayed first
delivery, partial recipient/rearm, competing/empty selection, renewed/lost claims, ACK loss/redrive,
republish/retention/GC races and the remaining scope/key controls. T045 semantics, real historical
upgrade, final current Studio, deletion guards, full-PR review, exact-head checks, protected merge and
resulting-main verification remain separate gates. ADR 0063 is accepted only afterward; #2898/#2907
and program production acceptance remain open. Orleans and the task-runner follow-up remain deferred.
