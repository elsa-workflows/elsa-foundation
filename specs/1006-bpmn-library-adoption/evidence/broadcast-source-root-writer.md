# Fenced executable source-root publication

Source parent `718528bdc9d1548ad5479a3f1e3c44fb7a43ec82`.
The [machine receipt](broadcast-source-root-writer.json) binds the contract, providers, existing fencing
helpers, catalog and controls. **This port is dormant. Test Run wrapper integration, actual winning
provenance, service recovery, old-writer exclusion and qualified default cohort GC keep T049 open.
Saved broadcast publication stays disabled. No host or Studio recovery is established here.**

The additive source-writer port creates or adopts one live reference under the provider's complete
physical root-closure fence. Capability checks qualify the actual selected executable store; legacy
implementations default to refusal without invoking unfenced Save. Providers verify the full material
pin, exact dependency/grant set, native lease owner/token and current deadline. Adoption returns the
stored winner, preserving source identity, sidecars and expiry. Only retry CreatedAt/PublishedAt may
differ; retirement cannot be restored and expiry cannot be renewed.

Review corrected a provenance coupling: selected publication attribution is independent of deduplicated
material's original producer. The reference joins material through ArtifactId. Publication B can retain
material produced by A while the complete physical material pin is separately validated. Both providers
have positive controls and detected mutations for this distinction.

Native publication takes artifact then source gates and validates current grants after any source-gate
wait. No asynchronous effect runs inside those gates. EF captures one access scope, checks physical
material/source projections before write locks, then fences existing coordination/lease rows and source
snapshot CAS in its own transaction. It checks source and actual fenced lease deadlines before Save and
before commit. Caller pending writes or local/enlisted/ambient transactions are refused before tracker
cleanup. Requested cancellation propagates; provider, IO and unrequested cancellation failures preserve
context and original cause. Original native zero/one-argument and EF three-argument CLR constructors
remain available, with additive selected-clock constructors. No ledger or schema is added.

Final restored whole-project results:

| Check | Passed | Failed / skipped |
|---|---:|---:|
| Runtime | 2,971 | 0 / 0 |
| Runtime EF | 1,505 | 0 / 0 |
| Publishing EF | 171 | 0 / 0 |
| Publishing API | 875 | 0 / 0 |
| Publishing | 61 | 0 / 0 |

The 82 new cases comprise 31 native Runtime and 51 SQLite EF controls. Native cases cover actual lease
release/expiry/recreation, exact pins and closure, immutable source winners, selected clocks, real
source-gate wait expiry and both zero-grace GC race orders. EF cases cover the same identity/lease
boundaries plus caller state, captured scope, corrupt physical projections, source/grant expiry around
actual SaveChanges, post-SQL rollback and four peer-context races after discovery before the owned
transaction. A transaction-committed acknowledgement-loss control retries from a fresh context and proves
one persisted winning source payload. This establishes the provider operation, without claiming service,
process-kill, upgraded-host, Studio or other relational-provider recovery.

All 26 valid behavioral mutations fail and exact source bytes are restored. No compile-error mutation is
counted. The receipt reconciles discovery with executed TRX results, records the additive Core API delta
(two additions, zero removals), unchanged schema, generated-filter check and frozen authoring digest
`41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92`.

Component source/catalog/receipt reviews are separate from full-PR rounds, which remain zero. Parent
Build & test, architecture and maps passed in run40406302850; the new head requires its own checks.
Draft Full tier skips are not qualification. The older GitGuardian public-source-digest finding remains
unresolved; no check or protection is bypassed.

Next: use the port from actual Test Run wrapper preparation with unique invocation leases, actual winning
material/source provenance and idempotent retry, then qualify EF service recovery and default GC/old-writer
gates. T050/T051, other stories, final current host/Studio, duplicate-semantics deletion and guards,
full-PR review/checks/resulting-main and ADR0063 remain open. #2898/#2907/program production acceptance
stays open; Orleans and task-runner remain deferred. No legacy execution-checkpoint writer is deleted.
