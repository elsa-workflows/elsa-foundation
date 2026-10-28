# Atomic Test Run template roots and coherent native binding

Source parent `9ac6bd18e3a7c40f9c3e44e25cf2fc74dc4953f7`.
The [machine receipt](broadcast-draft-template-handoff.json) binds the selected-store guards, native group,
Test Run preparation and controls. **T049 remains open: wrapper artifact/source retry and lease fencing,
EF service-level recovery, old-writer exclusion and qualified default cohort GC are unfinished. Saved
broadcast publication stays disabled. This is not upgraded-host, Studio or start-effect qualification.**

Default Runtime DI now owns the three actual native stores in one internal singleton group. Interfaces
and scoped reader aliases reach that state across scopes; separate root providers own separate storage.
The native source store fences the group's artifact store, and templates bind that exact tuple and its
selected clock. Singleton factories do not resolve a host's scoped replacements. Backend ownership records
the group and aliases together: EF replacement removes them, and EF-first composition does not add an
unused native group. The public standalone parameterless template constructor remains unqualified.

Review found a mixed-store hazard in the first binding delta: exact capability returned false, but directly
invoking the native port could still write its owned references while a host selected another source store.
The port cannot infer that unrelated DI selection from its own tuple. Production callers now enforce the
existing selected-store precondition before every atomic/maintenance call: Test Run preparation checks the
actual selected executable/reference stores, and both cohort-collector paths retain their checks. Core XML
and catalogs make the precondition explicit. Functional mixed-DI service and collector controls prove no
atomic/raw template write, hidden root, wrapper/dispatch or unqualified template deletion. This does not
qualify arbitrary manual calls that bypass selected-store preflight.

Activity draft Test Run preparation replaces the split template save/reference helper with the provider's
atomic SaveWithReference operation and uses its actual winning reference for placement. Immutable material
remains content-addressed. Each durable Test Run owns a stable template root derived from its receipt's
run identity and template hash, with the receipt's fixed expiry. A later run cannot borrow an earlier run's
expiry, source identity or layout, and retry cannot restore retirement or extend expiry. Existing hash-only
roots are left untouched. Already-prepared receipts still resume through their saved artifact/source IDs.
The old check-then-create helper is removed; no execution-checkpoint writer is deleted by this increment.

Final restored whole-project results:

| Check | Passed | Failed / skipped |
|---|---:|---:|
| Runtime | 2,940 | 0 / 0 |
| Runtime EF | 1,454 | 0 / 0 |
| Publishing EF | 171 | 0 / 0 |
| Publishing API | 875 | 0 / 0 |
| Publishing | 61 | 0 / 0 |

The 30 new cases comprise 14 native Runtime binding/operation controls, four EF registration controls and
12 native Test Run preparation controls. Runtime checks cover actual shared material/reference state,
reader aliases, provider isolation, selected clock, physical workflow referencers, actual source/artifact
guard interoperation, scoped replacement orderings, ownership removal and functional mixed-collector
retention. EF checks exercise both registration orders and repeated Runtime registration, selected same-
context capability and removal/exclusion of the native group. They do not perform new SQLite state writes.

Test Run cases exercise real default and mixed native DI, independent live/expired/retired prior-run roots,
fixed receipt expiry, refusal to renew/restore a changed root, and convergence of two preparations using an
actual asynchronous barrier. Before/after-atomic failure probes preserve Preparing receipts and retry one
stable native root; the native winning payload is not rewritten. The legacy hash-only-row control is a
constructed domain reference, not an actual legacy execution checkpoint. A recording dispatcher deliberately
loses acknowledgement after preparation; it does not execute a workflow. Neither these in-process probes
nor concurrent preparation establish EF service recovery, fresh-host recovery or exactly-once start effects.

Thirteen valid behavioral mutations were detected and exact source bytes restored. They cover native
binding/fences/clock/provider isolation, wrong aliases, scoped lifetime capture, backend group ownership,
EF-first contamination, production selected-store preflight, receipt-root identity, split-write regression
and unqualified cohort-collector invocation. No compile-error mutation is counted. Final discovery/TRX
reconciliation, unchanged Core API/schema, generated-filter check and frozen authoring file digest are
recorded in the machine receipt; truncated display collisions are not subtracted from execution counts.
Runtime discovery expands 2,929−3+14=2,940 and Publishing API 855−5+25=875; the other three projects
list/execute exactly. Frozen authoring remains
`41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92`.

Final component source/receipt reviews are recorded separately from full-PR rounds, which remain zero.
Parent head passed remote Build & test, architecture and maps in run40401030977. New source needs its own
exact-head checks; draft Full tier skips are not qualification. GitGuardian still fails on the older public
source-digest incident38996611. No check/protection is bypassed.

Next: complete the actual winning wrapper/source handoff, idempotent retry and provider lease fencing,
including EF service recovery. Wrapper material is still content-addressed and may carry first-winner node
metadata; this increment does not establish per-run wrapper provenance/recovery. Then qualify default
cohort composition and old-writer/quiescence gates. T050 exact bookmark/immutable-input/schema activation,
T051 actual host/fresh-provider recovery, other stories, duplicate-semantics deletion/guards, final current
host/Studio, full-PR review/exact-head checks/resulting-main and ADR0063 remain open. #2898/#2907/program
production acceptance stays open; Orleans/task-runner remain deferred.
