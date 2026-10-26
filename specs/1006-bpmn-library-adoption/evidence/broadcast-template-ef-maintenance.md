# Conditional EF template maintenance

Source parent `3dc906859adabecde538d07b7d29dbba0c54f818`.
The [machine receipt](broadcast-template-ef-maintenance.json) binds implementation, contracts, controls and catalogs.
**This qualifies conditional EF maintenance and the actual collector bridge. EF atomic save/reference
handoffs, complete production writers and qualified default composition still keep T049 open. Publication
stays disabled; upgraded-host recovery and Studio compatibility need their own evidence.**

The coherent EF template store implements conditional doomed-reference and unreferenced-material deletion
using its existing physical template/hash-claim incarnation and revision tokens. It captures a scope once,
validates candidate material and claims, checks unchanged source snapshots and current liveness, discovers
physical workflow/parent-template referencers, then starts its own transaction. All discovery precedes
writes. A referencer commit advances the captured version; collection loses CAS and returns false without
retrying with stale absence facts. Real provider failures roll back the unit and retain contextual domain
failure/cause. Requested cancellation propagates.

Checked maintenance paging locates a row through either current-scope projection and validates both,
physical identity, payload and schema before using absence as deletion authority. It covers workflows,
parent templates and source references, including a reference after the first maximum-sized physical page.
Workflow wrapper/flattened-node metadata and parent direct/closed/local/nested metadata retain material.
A workflow with the same ID as a template is ambiguous and retains both maintenance targets. No remaining
source reference, including retired or expired references, may be skipped before material deletion.

Material deletion accepts the actual immutable object returned by the EF reader. A weak process-local
scope/incarnation observation distinguishes identical recreated material and reconstructed/serialized
copies without adding a schema field, public API or durable cleanup ledger. It is not root authority:
current physical state, content, dependencies, source references and native CAS still decide deletion.
Reference cleanup may advance coordination revision without invalidating an unchanged material observation.
The methods refuse pending caller writes and local/enlisted/ambient transactions before clearing unchanged
tracked reads, so maintenance cannot discard or commit a caller's work.

Final restored whole-project results:

| Check | Passed | Failed / skipped |
|---|---:|---:|
| Runtime EF | 1,404 | 0 / 0 |
| Publishing EF | 161 | 0 / 0 |
| Publishing API | 863 | 0 / 0 |
| Publishing | 61 | 0 / 0 |

Forty new conditional-maintenance SQLite cases plus one added EF collector case cover the supported
boundary. Six cases use an actual second EF context committing workflow/parent/source referencers after
collector discovery and **before its transaction starts**; the actual conditional operation loses CAS.
They do not establish concurrent peer progress while SQLite holds a write transaction. The reverse order
collects the exact child first, then refuses parent publication without a partial root; retry succeeds only
after material recreation. Real post-SQL/precommit failure checks rollback with a fresh context and the
preserved SQLite cause. Other cases exercise all referencer routes, restored/replaced source snapshots,
liveness, identical recreation, cross-scope observations, dirty/local/ambient callers, scope corruption,
physical tail paging, ambiguous kind, copied reader models and contextual IO/cancellation.

The existing cohort collector control now runs against both native and actual EF template stores. It
cleans unrelated material, retains a Delivering cohort, retains a Delivered cohort while its target is
absent after publisher deletion, then collects only after sticky target retirement and follow-up sweeps.
This is an actual provider/collector scenario, not process-kill or upgraded-host recovery evidence.

Eighteen behavioral mutations were detected and exact source bytes restored. They cover stale discovery
CAS, workflow/parent routes, remaining references, source snapshot/liveness, material incarnation/reader
observation, ambient/dirty caller guards, contradictory scope filtering, paging, ambiguous kind and bridge
capability. A draft mutation with an invalid property name did not compile; it was corrected, rerun and
only the behavioral failure is counted. Discovery and TRX methods reconcile: Runtime EF 1,404, Publishing
EF 161 and Publishing 61 list/execute exactly; Publishing API 843−5+25=863. Core API +0/0 and schema remain
unchanged; generated filters pass. Frozen authoring remains
`41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92`.

Review/testing fixed clean-reader tracker collisions, scope-projection false absence and ambient/enlisted
transaction ownership. Three final component source/receipt reviews are recorded in the machine receipt;
these are not full-PR review rounds. Full-PR rounds remain zero. Parent head passed remote Build & test,
architecture and maps in run40391898125; this new source needs its own remote checks. Full tier has not run
on the draft. Older GitGuardian public source-digest incident38996611 remains unresolved; no bypass.

Next: EF atomic save/reference admission, complete publication/draft-test handoffs, old-writer/quiescence
gates and qualified default cohort GC. T050 exact bookmark/immutable-input/schema activation, T051 actual
host/fresh-provider recovery, other stories, duplicate-semantics deletion/guards, final current host/Studio,
full-PR review/exact-head checks/resulting-main and ADR0063 remain open. #2898/#2907/program production
acceptance stays open. Orleans/task-runner remain deferred.
