# Atomic EF template and live-reference admission

Source parent `836e9851a43eec7cf387cc828fcdbf7b68143dce`.
The [machine receipt](broadcast-template-ef-atomic-admission.json) binds the strict path and its reused staging,
contracts, fixtures and catalogs. **T049 stays open: production publication/draft-test handoffs, old-writer/
quiescence gates and qualified default cohort GC are unfinished. Saved publication remains disabled.**

`EfExecutableActivityTemplateStore.SaveWithReferenceAsync` now reuses the existing Runtime publication
transaction and staging. Its strict mode validates the actual physical source row, full identity, sidecars
and expiry, then creates or adopts immutable template material with its live root. Retry CreatedAt and
PublishedAt timestamps may differ; the actual persisted winning payload is returned unchanged. Retirement
cannot be restored and expiry cannot be renewed. Candidate and winner liveness are checked after storage
reads and before SaveChanges/commit. Source and template revision fences, exact dependency admission and
both writes share the unit; no second ledger, schema or Core API is added.

The store captures one access context per call and reuses that snapshot throughout the unit, while a later
call captures the then-current scope. Caller pending writes and local/enlisted/ambient transactions refuse
before any tracker cleanup. Provider/IO and unrequested cancellation failures carry the contextual domain
exception and original cause; requested cancellation propagates. The existing public phase-one CommitAsync
retains its legacy boolean/idempotent mode until production handoffs are migrated.

Review caught an ABI regression in the initial clock injection: an optional fourth parameter preserves
source calls but removes the original CLR constructor. The exact three-argument constructor is now retained
and delegates to the additive clock constructor. A reflection control fails if it disappears; this is a
signature witness, not an upgraded compiled-host run. Tests also found that same-ID/different-hash material
was incorrectly classified as a missing requested hash claim. Staging now validates the existing material's
actual claim/incarnation before reporting the immutable-content conflict.

Final restored whole-project results:

| Check | Passed | Failed / skipped |
|---|---:|---:|
| Runtime EF | 1,448 | 0 / 0 |
| Publishing EF | 161 | 0 / 0 |
| Publishing API | 863 | 0 / 0 |
| Publishing | 61 | 0 / 0 |

The 44 new cases comprise 43 actual SQLite controls and the constructor signature witness. They exercise
persisted-winner preservation, full source payload conflicts, malformed/nonlive proposals, exact missing/
mismatched dependencies, material identity conflicts, physical source projection corruption, wrong artifact,
caller ownership, captured scope, contextual IO/cancellation and both collector race orders. Two cases use
an actual second context committing new/adopted live roots after collector discovery and before its
transaction starts; the collector loses native CAS. Reverse collection removes material first, then atomic
admission recreates it together with a live root. No held-write-transaction peer progress is claimed.

A delay during the existing-material read exhausts source lifetime and asserts no SaveChanges call. Expiry
at SavingChanges and SavedChanges rolls back all rows. Separate failures induce actual SQLite SQL errors
after both material and source rows are visible inside the transaction, before commit; fresh contexts see
no partial root and unchanged adopted material. Another probe advances the actual source revision by SQL
inside the same transaction before its save; source CAS refuses and the unit rolls back. This is a mechanism
probe, not a concurrent peer or process failure.

Actual transaction-committed interception loses the acknowledgement after durability. A fresh context
retries, adopts the persisted winner rather than reminted timestamps, and finds one template/root. This is
finite provider-level acknowledgement-loss evidence. It is not process-kill, upgraded-host, broadcast host,
Studio or production recovery qualification.

Sixteen valid behavioral mutations were detected and exact bytes restored. They cover the binary constructor,
owned context, strict adoption, winning payload, both liveness boundaries, physical source lookup, source CAS,
exact fences, injected clock, immutable-conflict classification, requested/unrequested cancellation, original
cause and operation scope. No compile-error mutation is counted. Every discovered method maps to TRX:
Runtime EF 1,448, Publishing EF 161 and Publishing 61 list/execute exactly; Publishing API 843−5+25=863.
Core API +0/0, schema and frozen authoring remain unchanged; fingerprint
`41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92`. Generated filters pass.

Final component source/receipt reviews are recorded in the machine receipt. They do not consume full-PR
review rounds, which remain zero. Parent head passed remote Build & test, architecture and maps in run40396760674; new source still needs
its own exact-head checks. Draft Full tier skips are not qualification. Older GitGuardian public source-digest incident38996611 remains
unresolved; no check/protection is bypassed.

Next: complete actual publication/draft-test writer handoffs and qualify default cohort GC, including old
writer exclusion. T050 immutable input/exact bookmark/schema activation, T051 actual host/fresh-provider
recovery, other stories, duplicate-semantics deletion/guards, final current host/Studio, full-PR review/
exact-head checks/resulting-main and ADR0063 remain open. #2898/#2907/program production acceptance stays
open; Orleans/task-runner remain deferred.
