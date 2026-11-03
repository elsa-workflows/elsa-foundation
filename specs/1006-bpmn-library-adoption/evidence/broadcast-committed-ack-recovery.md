# Actual committed source completion before provider return loss

Source parent `6152c6872d931c70a277fa16742876af1461555a`. The [machine receipt](broadcast-committed-ack-recovery.json)
binds the [committed-completion host observations](broadcast-committed-ack-host.json),
[pre-acknowledgment](broadcast-committed-ack-after-fanout.json) and
[partial-start](broadcast-committed-ack-after-start.json) regressions.
This qualifies the committed-write/provider-return boundary; redrive and broader delivery gates remain open.

A scoped test-only diagnostic observer filters EF's public `TransactionCommitted` event to the actual
shared Runtime context and encoded source row. It unsubscribes on the match or wrapper scope disposal,
then holds the completion call before the provider returns. It does not change provider options, clocks,
claims, application rows or dispatch results. Its cached context identifies the cut; it grants no authority.

The event alone does not qualify. While the old process is alive, a separate coherent snapshot is read
through the actual checked provider. It must show **Delivered**, no owner, a completion timestamp, the
original fence and the accepted cohort. That live observation precedes later fixture writes and the owned
crash. The checked source projection also includes a hash of the entire canonical ContentJson, row revision
and storage schema, covering fields beyond the displayed delivery metadata.

After restart, a standard binary with no probe retains that entire checked projection and canonical payload
hash/revision/schema. No successor claim is expected or fabricated for an already completed row. The runner
waits past the original claim deadline plus two seconds, then invokes a distinct normal publisher. Its actual
checked source progresses to Delivered with an accepted empty cohort. This proves the restarted source
pipeline operates after that deadline rather than passing because it is dormant. Discovery uses encoded
projection identities only to find candidates, then the actual checked reader; missing, multiple or
contradictory candidates refuse.

The old completed source stays at fence one with the same completion timestamp, attempt facts, cohort,
canonical content hash, row revision and schema. Both original recipient workflows retain their complete
checked projections and full API instance/activity records. Their callbacks had already completed and the
keyed start had already parked. Later recipients remain excluded. The only added workflow is the deliberate
post-deadline control publisher; the original publisher's own continuation may finish.

| Verification | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Actual committed source completion/provider return loss | 1 | 0 | 0 |
| Actual pre-ACK and partial-start regressions | 2 | 0 | 0 |
| Checked source and publisher-discovery controls | 12 | 0 | 0 |
| Owned process/pause/broker controls | 10 | 0 | 0 |
| Actual runner cleanup failure controls | 3 | 0 | 0 |
| Separate locked probe/standard/reader builds | 3 | 0 | — |

All three passing runs bind identical source/build manifests and **1,379 current source bindings** before
and after execution. Initial typed queries incorrectly compared logical strings with encoded EF projections;
two positive reader controls failed and no cut fired. Using the public `EfRelationalIdentity` codec fixed
both. A SavedChanges prototype then correctly failed its earliest live check: the actual completion uses
an explicit SaveChanges → Commit transaction, so the event preceded commit and the reader saw Delivering.
Moving the scoped observer to TransactionCommitted produced the independently verified boundary. Those
unsuccessful attempts are not counted as qualification.

Three independent component source/actual/portable audits are clean; full-PR rounds remain zero. Production
source, project graph and frozen authoring remain unchanged. Supporting architecture 590/0/0, locked
solution restore and map checks are inherited rather than rerun. Whole databases/configuration stay private,
and only owned case hosts are stopped; the existing user backend and Studio remain running.

Actual redrive, pre-dispatch renewal, same-artifact behavior and remaining scope/retention/GC/republish cases
remain T051 work. T045/native-composite, actual historical upgraded-host recovery, final current Studio,
legacy writer/duplicate semantics deletion and guards, full-PR review, green exact-head checks, protected
merge/resulting-main and separate ADR 0063 remain open. #2898/#2907/program production acceptance stays
open; Orleans/task-runner deferred. Public-source-digest GitGuardian incidents still need legitimate resolution.
