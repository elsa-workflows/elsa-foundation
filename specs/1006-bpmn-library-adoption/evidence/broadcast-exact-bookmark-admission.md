# Exact bookmark admission

Source parent `678a519aebca7ac0e0195ba4a2170ec944152fe2`; the
[machine receipt](broadcast-exact-bookmark-admission.json) binds the actual source and controls.
This is T050 prerequisite/component evidence. **Source-owned selection/routing, publisher activation,
quiescent upgrade admission, durable host recovery and current Studio acceptance remain open.**
The released BPMN adapter remains dormant and the legacy checkpoint writer remains present.

A saved resume now captures the actual bookmark generation, full executable pin, physical/domain
scope, pinned source, registration metadata and payload fingerprint without retaining another payload
copy. EF observes its existing persisted revision. Native owns a monotonic process-wide generation
allocator and immutable bookmark data; delete/recreate, replacement stores and identical CreatedAt
values cannot reuse a captured generation within that provider's lifetime. This does not make native
memory crash durable.

The existing queued resume carries an optional condition. Before validation/callback work, the handler
qualifies its actual store tuple and checks the captured wait and live workflow/activity owner. The
initial trigger-claim checkpoint atomically checks those same facts while deleting the wait and claiming
the activity attempt. Native shares its existing write gate with direct bookmark/activity/workflow
mutations and cleanup. EF checks the original bookmark revision and touches workflow/activity owner
revisions inside the same transaction as the complete checkpoint, outbox and replay marker. Trusted
deadline checks after awaited work prevent expiry during admission. A committed replay takes priority
over predicates that have since retired.

Replaced, expired or ineligible waits cause no callback or replacement consumption. Missing authority
and recognized storage outages retain the queued payload for delayed retry with their original causes;
requested cancellation and immutable corruption remain distinct. Failed marker reconciliation after a
lost actual commit acknowledgment preserves both causes. A claimed redelivery uses the durable
consumed trigger payload and leaves a newly rearmed wait alone. The Test Run delivery receipt joins
the actual initial claim and executable root-write lease.

Original constructors and absent-condition wire remain unchanged. The historical checkpoint
fingerprint branch remains unchanged; a temporary independently compiled copy of parent `678a519`
verified the permanent historical-envelope hash control. New conditional hashes preserve opaque
UTF-16 values/keys and include the complete condition. Core API accounting is **32 additions, zero
removals**. No persistence schema or frozen authoring change is included. Unsupported providers and
opaque/mixed store tuples refuse; known coalescing wrappers qualify their physical stores and preserve
the condition through immediate buffered flushes.

| Verification | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Whole Workflows Runtime | 2,990 | 0 | 0 |
| Whole Runtime EF | 1,623 | 0 | 0 |
| Whole Activities Runtime | 680 | 0 | 0 |
| New cases included in those runs | 89 | 0 | 0 |

The new cases comprise 54 actual native/SQLite provider checkpoint controls, 16 native queued
handler/callback controls, 17 wire/fingerprint controls and two actual default DI compositions. Provider
controls cover whole-checkpoint refusal, ABA/rearm, lifecycle/pin/source/scope changes, dirty/local
transaction refusal, cancellation, actual pre-save failure, post-SQL expiry rollback, actual transaction
commit acknowledgment loss, unavailable reconciliation/first marker reads, fresh-context replay,
native peer-write races and active buffered coalescing. Queued controls include typed callback input,
stale completed owners, replacement between preflight and initial claim, constructed observation,
sibling, capability, scope and checkpoint outages, missing owners/material, claimed redelivery and an
actual native Test Run receipt blocked by a real material deletion guard then accepted after release.
The constructed cuts are explicit component fault injection, not observed production incidents.

Three independent component reviews found and closed the handler stale-cleanup, missing-authority,
mixed-store and remaining pre-admission availability gaps. Final source reviews are clean. Behavioral
mutation results, source restoration, discovery/TRX reconciliation, API accounting and supplementary
checks are recorded in the machine receipt. Full-PR review rounds remain **0**; these component reviews
do not consume or replace the bounded full-PR loop.

The frozen authoring fingerprint remains
`41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92`.
Existing legacy captures remain historical writer evidence. These new tests prove neither upgraded
host recovery nor Studio compatibility, and they do not close #2898, #2907 or the execution-model
program. Orleans and task-runner follow-up remain deferred.
