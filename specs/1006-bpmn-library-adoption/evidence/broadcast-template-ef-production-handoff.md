# Production EF publication handoff to strict live-root admission

Source parent `05b0efd4b093da8bf4f7aa85b89d803d41aa5385`.
The [machine receipt](broadcast-template-ef-production-handoff.json) binds the shared Runtime unit, both
production commands, their controls and contracts. **T049 remains open: activity draft Test Run handoffs,
native/default composition, old-writer exclusion and qualified cohort GC are unfinished. Saved publication
stays disabled. This increment does not establish host or Studio recovery.**

Both existing EF publication commands already enter `EfActivityPublicationRuntimeCommit`. Its public
phase-one method now invokes the qualified strict mode using the selected template store's provider clock,
while preserving its boolean creation result. No parallel transaction protocol or caller-side split write
is added. Complete live-reference identity/sidecars/expiry, physical source lookup, exact dependencies,
native revision CAS and persisted-winner preservation follow the preceding atomic-admission increment.
The shared commit applies the clean/owned Runtime-context check before tracker cleanup, including public
phase-one calls. Pending caller writes and local/enlisted/ambient transactions cannot be discarded or
committed as publication work.

Final restored whole-project results:

| Check | Passed | Failed / skipped |
|---|---:|---:|
| Runtime EF | 1,450 | 0 / 0 |
| Publishing EF | 171 | 0 / 0 |
| Publishing API | 863 | 0 / 0 |
| Publishing | 61 | 0 / 0 |

Ten new actual multi-context SQLite cases exercise the two actual production commands: draft publication
with Runtime/Design/receipt phases and source-owned publication with Runtime/Design phases. A preexisting
expired reference, a changed expiry and a changed layout refuse before any Design publication or receipt,
with no partial Runtime template/hash claim and unchanged source payload. Dirty Runtime contexts and
caller transactions survive refusal, while fresh contexts show no new source/Design/receipt effects and
unchanged persisted material revision. Existing positive, interrupted phase/retry and idempotency scenarios
also pass in the whole Publishing EF suite.

Two additional SQLite cases exercise the public phase-one method with the selected FakeTimeProvider. An
actual existing-material read advances only that clock by two minutes. An expiry still live by wall time
but expired by provider time refuses without SaveChanges; a valid twin commits once. Fresh state verifies
source presence/absence and the exact material revision. This closes a review-identified claim gap: the
actual command negatives alone used wall time and would not detect replacing the selected provider clock
with TimeProvider.System. It is finite shared-unit evidence, not a fresh-host proof.

Three behavioral mutations were detected and exact source bytes restored: disabling strict production
mode, replacing the provider clock with wall time, and removing the shared caller-state boundary. Each
runs the two phase-one clock cases and ten actual command cases; expected failures occur in the relevant
project rather than requiring every mutation to fail both suites. No compile-error mutation is counted.
Every discovered method maps to TRX: Runtime EF 1,450, Publishing EF 171 and Publishing 61 list/execute
exactly; Publishing API 843−5+25=863. Core API +0/0 and schema remain unchanged; generated filters pass.
Frozen authoring remains `41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92`.

The machine receipt records final component source/receipt reviews and separate parent remote checks.
These are not full-PR rounds; full-PR rounds remain zero. Draft Full tier skips are not qualification.
Older GitGuardian public source-digest incident38996611 remains unresolved; no check/protection bypass.

Next: replace the split activity draft Test Run template/reference write with the actual winning atomic
handoff, preserve its authoring/state behavior, qualify native bindings/default cohort GC and enforce old
writer/quiescence gates. T050 exact bookmark/immutable input/schema activation, T051 actual host/fresh-provider
recovery, other stories, duplicate-semantics deletion/guards, final current host/Studio, full-PR review/
exact-head checks/resulting-main and ADR0063 remain open. #2898/#2907/program production acceptance stays
open; Orleans/task-runner remain deferred.
