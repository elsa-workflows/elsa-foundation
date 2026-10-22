# Final packaged native acceptance

The [receipt](native-package-final-receipt.json) records both executed commands, immutable package
inputs, harness hashes, results and seven independently checked read-only SQLite snapshot digests.
Both journeys passed against the previously qualified six-module Workbench package deployment.
No product code changed during this checkpoint. The combined native evidence completes T027–T030;
exact-head PR review/CI and resulting-main verification remain open.

## Flowchart and ordinary outcomes

`Test-DurableRules.ps1` passed five scenarios: Sequence success, linear Flowchart success,
held-sibling Flowchart success, round-budget fault and permanent finite-model refusal. The parallel
Flowchart forks into Rules and an Event. Rules and result capture complete while the Event stays
suspended, the parent remains Running, and the join/following activity are absent. After the owned
host restarts, that state and the observed Rules result remain intact. A public Event stimulus
resumes one branch; the join and following WriteLine each have one completed persisted internal
attempt. A duplicate stimulus resumes zero branches and does not add another attempt. A final
restart preserves completion and the observed result.

The output observation is an untruncated seven-field terminal JSON shape with `Elsa.Any` as its
diagnostic snapshot alias. Success has two committed rounds and exactly `derived,seed`. It is not
a CLR runtime-type claim or a canonical complete terminal-byte comparison; those checks belong to
the persisted C# delivery proof. Internal attempt counts come from the native database, not the
optional public retry-lineage projection.

Two earlier runs exposed incorrect acceptance assertions. One expected a concrete CLR type where
the public snapshot correctly reports its declared `Elsa.Any` alias. The other expected a public
retry-lineage count for ordinary initial executions, where the projection can be null. Source and
retained database inspection established both mismatches. The corrected script preserves the
behavioral assertions and reads authoritative internal attempts. The receipt retains both failed
logs and the final passing command; neither failure is presented as a runtime fix.

## Admission recovery, cancellation and invalid input

`Test-RulesAdmissionRecovery.ps1` publishes with the actual package pin while admissions are
enabled, then restarts with new admissions disabled. Two distinct native starts remain suspended
with attempted, retryable Start intents and no Rules child rows. After another restart, public
`CancelWorkflow` durably cancels the first parent/child and records an unadmitted Rules tombstone.
The second responsibility remains retryable with no child row.

Restoring admission completes the second invocation under its original run identity and package,
asset, language and schema pin. Its delivery is acknowledged, its following WriteLine has one
completed internal attempt, and the first invocation remains cancelled. The final snapshot
explicitly compares both retained implementation pins with their original Start requests. A
separate in-memory check of retained evidence detects a changed selected-asset hash; this is a
harness assertion check, not a new production removal control.

A final `MaxRounds=0` invocation faults through the native boundary with one incident, no
following activity, a delivered Start intent and no fabricated Rules row. Polls wait for the
relevant persisted settlement before stopping the owned host. Five distinct owned processes were
used; successful scenario roots were cleaned up and the evidence snapshots remain outside them.
This is admission unavailability. The [native recovery checkpoint](native-recovery-acceptance-checkpoint.md)
separately proves temporary reader unavailability inside real native resumption.

## Provenance and limits

The deployment is from immutable product source `26c645ba4691ac0634ae3232f9bd3868f1f72ecd` and
publish-state `eb3e12531b0cbd594374df1e49df0fadef9a50f3`; current Rules and Essentials product
sources are unchanged from that package checkpoint. The complete packaging coordinator was not
rerun. Workbench is a mixed host: its six Rules modules are packages and other dependencies remain
source-backed. The [package checkpoint](package-workbench-checkpoint.md) owns that qualification.

All 489 deployment files outside `.nuplane/store-state.json` remained byte-identical in both final
runs. Nuplane's operational state file changed, so the entire deployment is not described as
unchanged. The executing harness files stayed unchanged during each run. This is same-machine
SQLite/process evidence, not arbitrary cluster qualification. External effects remain at least
once. The historical Workbench acquisition timeout remains causally unexplained despite the
passing [full compatibility run](final-compatibility.md).
