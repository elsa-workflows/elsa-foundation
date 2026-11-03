# EF template referencer and source writer fences

Source parent `9be5b0a01509bf59222a6074c5311191c1e42ec9`.
The [machine receipt](broadcast-template-ef-writer-fences.json) binds twelve source/contract/fixture files.
**This qualifies EF writer participation, not conditional EF template collection. T049 remains open and
publisher activation stays disabled.**

New workflow/template referencers and source-reference creation now stage revision touches on present native
template rows in their own existing transaction. Workflows cover wrapper and fully flattened nested-node
metadata. Parent templates cover direct/closed identities and local/nested metadata. Shared edges touch
one row once per commit. Source creation resolves the actual template ID without trusting source-kind
labels. The existing template incarnation/revision and hash-claim material are reused; no schema, Core API
or second cleanup ledger is added.

Physical primary keys establish template ownership; hash lookups use the canonical hash-claim key and its
material key. Projections, schema readability and matching incarnation are validated after lookup, so a bad
projection cannot silently become an optional missing import. Missing-claim checks refuse contradictory
present material. Raw parent-first staging and inlined imports remain available without installed template
material. Strong root publication requires its exact dependencies to exist with matching hashes.

Publication adoption stages both template and source-row CAS fences, including when logical material/root
creation is already done. It preserves the winning source/template payload and incarnation while advancing
coordination revisions. Its boolean still describes logical creation. Template and source access contexts
are captured/rebound once per operation; different scopes refuse before effects. Moving original accessors
during awaited storage cannot redirect that unit, and a subsequent operation captures its new scope.

All discovery precedes staged, physical-key-ordered revision updates. No dependency scan is performed after
SaveChanges acquires write locks. These changes are the writer half of conditional collection: a future
collector using an older native row revision must lose its delete CAS after a referencer commits. All relevant
writers and activation/quiescence gates still require qualification; older nonparticipating writers cannot
be assumed safe merely because these new writers pass.

Final restored whole-project results:

| Check | Passed | Failed / skipped |
|---|---:|---:|
| Runtime EF | 1,363 | 0 / 0 |
| Publishing EF | 161 | 0 / 0 |
| Publishing API | 863 | 0 / 0 |
| Publishing | 61 | 0 / 0 |

The 45 new actual SQLite cases cover workflow/parent routes, shared-edge deduplication, scope isolation,
winning publication adoption, real post-SQL/before-commit provider failure and rollback, and rejection by a
real EF delete CAS using an older physical row snapshot. Twenty workflow/source corruption cases cover
schema, identity/hash/scope projections, missing/foreign hash claims and incarnation mismatch. Other
controls verify mixed-scope refusal, per-operation frozen scope during awaited storage, actual source-row
revision conflict, inert staging/import compatibility and missing/mismatched exact dependency refusal.

The source-adoption conflict control advances actual SQL state inside the **same** transaction before
SaveChanges. It is a mechanism probe, not a concurrent peer. The stale-template delete is a real EF removal
using an older row snapshot, not the unimplemented conditional maintenance API. No process-kill, upgraded
host, Studio or production recovery claim follows from either control. The Publishing suite's existing
runtime-only export/import probe remains an ordinary scenario, not broadcast host qualification.

Every discovered method maps to TRX results: Runtime EF 1,363 and Publishing EF 161 list/execute exactly;
Publishing API lists 843 with five deferred theories expanding to 25 executions (843−5+25=863); Publishing
lists/executes 61. Truncated display collisions are not subtracted. Eighteen behavioral mutations were
detected and restored, covering writer participation, revision/source CAS, workflow and parent routes,
exact dependency admission, projection/orphan validation and captured scope. No compile-error mutation is
counted. Generated filters pass their local check; Core API +0/0 and schema are unchanged. Frozen authoring
remains `41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92`.

Review/testing corrected physical-projection false absence, shared-context/different-scope publication and
an inappropriate raw-I/O exception expectation. Rollback now induces an actual SQLite provider failure and
asserts contextual domain wrapping with its preserved cause. Unreadable schema versions assert schema skew
rather than malformed-data failure. The existing interrupted-publication retry control now requires exactly
one coordination revision advance while strengthening byte-exact payload/incarnation preservation. All three
final source and receipt reviews are clean. Full-PR rounds remain zero.

The prior native-maintenance head passed remote Build & test, architecture and maps in run40385441056.
This new source needs its own remote checks. Full tier has not run on the draft. GitGuardian's older public
source-digest incident38996611 remains unresolved; no check/protection is bypassed.

Next: conditional EF template/reference maintenance using these writer fences, actual GC-wins admission
controls, complete publication/draft-test handoffs and qualified default cohort GC. T050 immutable input/
exact bookmark/schema activation, T051 actual host/fresh-provider recovery, T045/other stories, deletion/
guards, final current host/Studio, full review/exact-head checks/resulting-main and third ADR0063 PR remain
open. #2898/#2907/program production acceptance stays open; Orleans/task-runner deferred.
