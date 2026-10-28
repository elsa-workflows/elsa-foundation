# T049: verified cohort installation on the actual source row

**T049 remains open. Saved starts and publisher activation remain disabled.** This increment connects
canonical capture, actual root grants, native material fencing and execution ownership to source-row CAS.
Source parent: `631f5e0a8771a0d57ef075084771402938652c3b`.
The [machine receipt](broadcast-cohort-acceptance.json) binds 22 source/test/catalog/task files, including
the unchanged dispatcher refusal. It does not qualify an upgraded host, recipient dispatch or Studio.

## Acceptance boundary

Both native providers validate the existing committed source, current owner/fence/deadline, authoritative
publisher scope and complete publisher pin. Under real renewable root/closure grants, the ordinary source
selector revalidates every captured start. The lease set includes the publisher pin even for an empty cohort.
The complete execution-dependency set includes the publisher, exact resume targets and deterministic starts
under the original per-artifact key; several bindings sharing one artifact still share that identity.

Native memory requires coherent material/reference, checkpoint/execution and ownership stores. Its selected
coalescing decorator may supply the physical publisher store, bypassing an active uncommitted overlay;
a later foreign replacement refuses. The checkpoint write gate, ownership gate and synchronous native
artifact fence cover recipient tombstone checks and source-row publication. No awaited work occurs inside
the artifact-gated publication callback.

EF binds canonical reference reads to the acceptance access context, even if a supplied native reader over
the same DbContext uses a foreign accessor. Installation requires a clean context and its own transaction.
It takes physical material coordination/lease fences, reads all publisher/workflow facts, then physically
touches sorted existing execution-ownership revisions or inserts missing neutral ownership rows. It reads
no workflow roots after those ownership locks. Source reload, fresh claim validation, whole snapshot and
witness CAS commit in that transaction; rollback removes partial fences. A bounded optimistic conflict
retry revalidates current facts. Source reads after awaited publisher lookup protect winner replay from a
peer takeover while the old claimant's deadline is still future.

The source-row snapshot's additive `RetentionAcceptanceVersion = 1` witnesses this boundary. Original CLR
constructors and unmarked wire remain available. Present witness wire requires a supported version and
structural equivalence includes it. Normal pending/checkpoint writers and immutable atomic failure-follow-up
construction reject caller-minted witnesses before effects. Existing unmarked data-only winners remain
immutable and refuse restamping. An actual committed write with lost acknowledgement leaves its verified
winner intact: a fresh provider returns that exact complete/empty set after reference retirement rather
than recapturing live publications. Construction/deserialization alone grants no independent authority.

The ordinary selector is now a public shared Runtime helper for provider adapters; it keeps one selection
policy. Read failures preserve artifact, required scope and cause in `WorkflowExecutableSourceReadException`;
ordinary eligibility refusals retain their categories, and requested cancellation propagates. Core adds
eight API declarations (four witness/dependency members and four contextual exception declarations),
removing none. Dependency-bearing provider constructors are additive; the original 17-argument optional
native constructor and original EF constructors remain callable.

## Verification

| Restored check | Result |
|---|---:|
| Whole Workflow Runtime | 2875 passed, 0 failed, 0 skipped |
| Whole Runtime EF | 1218 passed, 0 failed, 0 skipped |
| New Runtime / provider cases | 9 / 36 |
| Focused provider plus shared atomic completion | 166 passed |
| Focused source/contract | 79 passed |
| Behavioral mutations | 18 detected, no compilation failures |
| Core API additions / removals | 8 / 0 |

Runtime lists 2864 entries. Three unchanged deferred theories expand to 14 cases, so
2864 − 3 + 14 = 2875. Every TRX result maps to discovery. EF lists and executes 1218 cases.
Existing truncated display-name collisions (four Runtime, two EF) are reported as separate uniqueness
counts, without incorrectly subtracting them from discovery. Final source bytes were restored before the
whole-project runs. Existing warnings remain.

Provider fixtures now seed actual material, references, renewable leases and coherent ownership. They cover
ordinary source eligibility and conflicting/captured-null/ambiguous attribution; missing or retired start and
resume execution dependencies; deletion before acceptance and retention after acceptance; complete publisher
pin changes preserving artifact ID/hash; native and EF grant loss; scope-bound canonical selection; actual
runtime/coalescing DI and a foreign overlay; old constructors/legacy managers; explicit empty/competing winners;
and unknown outcome recovery. The EF acknowledgement-loss interceptor throws **after the actual database
transaction commits**. It then retires the selected reference and proves exact winner reuse through a fresh
DbContext. This is constructed provider failure evidence, not process-kill or production evidence.

Existing GC controls now use the installation's real native material/reference/root-manager composition,
including dependency closure, selected-but-absent target handoff, both reference-guard/acceptance orderings,
and unresolved/FailedFinal/Cancelled retention. The prior explicit outer lease-manager wrapper is no longer needed: actual installation now acquires and fences its own full set.
Deterministic transaction-start and source-read schedules do not claim a peer write inside SQLite's immediate
transaction. The default and active coalescing controls are finite DI/provider checks, not upgraded-host proof.

Component review found and fixed stale EF winner replay, a same-context foreign-scope reference reader,
atomic follow-up witness minting and the newly public selector's failure boundary. Acceptance review requested
explicit full publisher-pin changed/unchanged controls. Targeted guard-removal mutations detect all five.
The 18 mutations also remove canonical selection, each native material fence, recipient tombstone guards,
unmarked-winner refusal, coalescing physical selection, ordinary/atomic pending witness rejection, marker
comparison and source-read context. All fail behaviorally. The whole Runtime run initially exposed two old
fixtures: missing publisher material in a cap-flush control, and a single-constructor assumption. The former
now supplies actual material and compares the returned verified winner; the latter guards the exact preserved
optional CLR signature while permitting the additive overload. Their focused controls and final whole suite pass.

Frozen authoring SHA256 remains byte-identical:
`41aa0bb979ea46fc2533b057e3677f408ab54c3411fecfafaa238a2adae38c92`.
Correctness, standards and acceptance final source and receipt reviews are clean.
Full-PR review remains **zero rounds**.

## Remaining delivery

Saved-start admission must consume the exact verified source/entry through ordinary security/domain admission,
shedding, artifact-scoped keys and duplicate provenance. Final template/unsupported-guard GC composition also
remains T049 work. T050 publisher/schema activation and atomic exact bookmark-generation acceptance,
T051 actual host/fresh-provider recovery, T045/other stories, duplicate-semantics deletion/guards, final current
host/Studio, full PR review, exact-head required checks, resulting-main verification and the third ADR0063 PR
remain open. #2898/#2907 and program production acceptance remain open; Orleans/task-runner stay deferred.
