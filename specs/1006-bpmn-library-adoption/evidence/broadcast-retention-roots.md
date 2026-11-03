# T049: cohort retention roots and guarded reference cleanup

**T049 remains open; saved starts and publisher activation remain disabled.** Source parent:
`f947d3f33acd15fca0d0ce81c871eb685accb03c`. The [machine receipt](broadcast-retention-roots.json)
binds implementation, tests, Core API, catalog and the unchanged dispatcher refusal.

## Retention boundary

The existing source outbox rows carry the roots; no second ledger is introduced. The new provider root
operation traverses every finite same-scope source page, including Delivered cohorts after publisher deletion.
A selected pin remains a root until its source is Delivered **and** its deterministic per-artifact target has a
sticky deletion tombstone. An absent target is still an owed handoff: delivery acknowledgement can precede
start-command execution and creation of the target state. Present terminal target state remains a root through
normal execution retention as well. A tombstone cannot be substituted with publisher absence or a missing row.

Execution deletion guards retain the declared publisher before selection and the exact selected resume/start
execution and dedup state until delivery is Delivered. FailedFinal and Cancelled remain unresolved. These
checks run inside the existing memory deletion gate and EF deletion transaction with its captured ownership
revision. Existing run cleanup preserves outbox rows, allowing a Delivered source to outlive its publisher.
Automatic cohort installation must still acquire/touch the reciprocal ownership authority; that integration
is unfinished, so these read guards alone do not establish race-complete acceptance.

The additive GC constructor includes those roots in initial and guarded final dependency closure reads.
Its capability flag describes that query participation only. Original constructors retain ordinary behavior.
Retired workflow-reference cleanup acquires the native artifact deletion guard, rereads runtime reachability
and calls a new guarded source-writer overload. Its default refuses. Memory performs the physical reference
mutation under the native artifact gate. EF commits snapshot/incarnation/revision-CAS reference deletion
with a revision touch of the existing artifact coordination row. An expired guard's root-lease acquirer must
clear that same revision; whichever operation loses cannot commit its conflicting reference mutation.

The new composition conservatively retains activity templates and references without native artifact guards.
It does not invent a guard across an unrelated material store. Native memory guarded cleanup requires its
actual artifact store; unsupported provider/decorator combinations refuse and retain. T049 must still settle
final material composition before publisher activation. Source scans are deliberately linear in the scope's
outbox history and can retain on uncertainty; no throughput or automatic cleanup claim is made.

Root-provider unavailability preserves actual scope/cause in `StimulusDeliveryRetentionException` (scope is
null only before access establishment). EF guarded source deletion uses its existing contextual persistence
exception with the real reference identity. Requested cancellation propagates. Data/access/unsupported
state refuses; legacy root providers never turn missing support into an empty root set.

## Verification

| Control | Result |
|---|---:|
| Whole Workflow Runtime / listed | 2854 passed / 2843 listed; 3 deferred theories expand to 14 cases |
| Whole Runtime EF / listed | 1168 passed / 1168 listed |
| New Core/GC cases | 3 |
| New actual-provider cases | 38 |
| Behavioral mutations detected and restored | 14 |
| Core API additions / removals | 7 / 0 |

All whole-project runs have zero failures/skips after mutation restoration. A restored Runtime TRX audit maps
every result to discovery: three existing deferred theories list once each and expand to 14 cases, accounting
for the 11-case difference (2843 - 3 + 14 = 2854); no result is unmapped. The machine receipt names those
three theories. Provider controls cover Pending
before claim; Delivering/FailedRetryable/FailedFinal/Cancelled publisher/resume/start dedup refusal; Delivered
publisher deletion before target creation; eventual sticky target release; foreign-scope isolation; present empty
cohorts; source traversal beyond 500 rows; actual memory/EF GC dependency closure and retired-reference
preservation. Native guarded-delete controls exercise valid, wrong-token, replaced, expired-with-root-lease,
restored and retired-again snapshots in both providers, plus provider failure/cause/scope and cancellation.

Four GC interleavings exercise both providers and both orderings with the real full-set lease manager.
Acceptance after initial root discovery but before guard acquisition preserves the newly rooted reference,
pin and dependency. A guard acquired first refuses the attempted leased root write without installing a cohort;
GC may then collect. The fixture explicitly wraps the existing install in the manager; it does not establish
automatic provider lease integration or source-reference capture eligibility. An EF constructed same-transaction
coordination conflict after actual reference DELETE proves rollback and retry. SQLite excludes an actual peer
write at that point, so this control does not qualify that cross-provider peer interleaving or host recovery.

Mutations release pins at Delivered before target tombstones, ignore retirement, release an owed publisher,
treat FailedFinal/Cancelled as resolved, change per-artifact start keys, truncate source traversal, bypass final
reference reachability, bypass each native guard, omit EF's coordination-revision fence, ignore each reference
snapshot, convert unavailability into empty roots and invent a legacy empty-root fallback. Each fails
behaviorally without compilation errors. Every source byte was restored before final whole runs. Existing
warnings remain. Frozen authoring SHA256 stays
`41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92`.

## Review and remaining work

Correctness/acceptance source reviews are clean. Standards review found missing public error documentation
and provider normalization; these were fixed and independently rereviewed clean. All three final receipt reviews are clean.
Full-PR rounds remain zero.

Remaining T049 work is actual provider acceptance under full root leases, authoritative captured-source
validation, ownership-row fencing against recipient deletion, final material/provider composition and actual
saved-start admission preserving ordinary policy/shedding/duplicate behavior and selected provenance.
T050 publisher activation, T051 actual host/fresh-provider recipient/retry/restart/retention, T045 and other
story gates, duplicate-semantics deletion/guards, final current host/Studio, full review/required checks,
resulting-main verification and the third ADR0063 PR remain open. #2898/#2907 and program production
acceptance stay open. Orleans/task-runner remain deferred.
