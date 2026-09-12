# EF Core persistence legacy-requirement register

Status: active completion evidence for [#1671](https://github.com/elsa-workflows/elsa-foundation/issues/1671).

Live state checked 2026-09-12. This register maps 77 open requirement-bearing/current-program issues
and one closed predecessor that materially constrain the Groundwork-to-EF program. It is deliberately broader than
a title-only `Groundwork` search: provider failures, Runtime semantics, e2e defects, fixture behavior,
and EF defects remain relevant even when their titles do not name the current adapter.

No issue is closed by this inventory. Before later closure, the named EF owner must either prove the
carried requirement, link a successor containing every unresolved criterion, or record the exact
owner-authorized policy retirement. Performance-only criteria retire by owner decision; concurrency,
leases, fencing, idempotency, bounded queries, retention, atomicity, deadlock handling, crash
recovery, and failure behavior do not.

## Governance, pilot, and current program issues

| Issue | Live state | Requirement carried forward | EF owner / closure gate | Disposition |
|---|---|---|---|---|
| [#1626](https://github.com/elsa-workflows/elsa-foundation/issues/1626) Secrets EF persistence pilot and replacement decision | Open | Preserve the pilot's technical evidence and bounded scope; do not treat it as Runtime/MySQL/repository-wide proof | #1679; close as superseded only after all surviving pilot criteria are linked | Supersede later |
| [#1628](https://github.com/elsa-workflows/elsa-foundation/issues/1628) Secrets EF pilot ADR decision and governance reconciliation | Open | Preserve ADR 0072 as bounded history and reconcile #1622/#1623 without restoring old policy | #1666/#1671; ADR 0073 is current authority | Supersede later |
| [#1653](https://github.com/elsa-workflows/elsa-foundation/issues/1653) Secrets production-shaped HTTP CRUD and restart | Open | Real host CRUD, persistence, restart and response behavior | #1679 | Preserve and prove |
| [#1654](https://github.com/elsa-workflows/elsa-foundation/issues/1654) Persistence inventory and transaction boundaries | Open | Verified repository inventory and explicit transaction topology | #1671 records the complete successor evidence | Supersede after #1671 closure |
| [#1657](https://github.com/elsa-workflows/elsa-foundation/issues/1657) Concurrent `dotnet-ef` BuildHost race | Open | Safe concurrent tooling using current compiled artifacts | #1669 prerequisite | Preserve and prove |
| [#1644](https://github.com/elsa-workflows/elsa-foundation/issues/1644) Foundation Host cannot share unsigned CShells assemblies with Nuplane package graphs | Open | Packaged EF shell discovery, assembly identity, readiness failure and restart/reconciliation | #1670/#1679 | Preserve host-composition requirement |
| [#1665](https://github.com/elsa-workflows/elsa-foundation/issues/1665) Replace Groundwork with EF Core persistence | Open | Full terminal requirement audit | Program issue; closes last | Active authority |
| [#1667](https://github.com/elsa-workflows/elsa-foundation/issues/1667) Shared EF foundation and independent modules | Open | Common lifecycle/test kit plus Secrets, Preferences, Diagnostics and Identity migrations | #1678-#1682 | Active epic |
| [#1668](https://github.com/elsa-workflows/elsa-foundation/issues/1668) Retire persistence performance measurement and preserve correctness evidence | Open | Remove all performance infrastructure without losing timing-independent safety or correctness | #1668 with module owners | Active feature |
| [#1669](https://github.com/elsa-workflows/elsa-foundation/issues/1669) Four-provider migration lifecycle and isolation | Open | Provider/artifact pairing, histories, runtime/out-of-process apply, locks, pending model, fresh install and failure-before-activation | #1669 after #1657 | Active spike |
| [#1670](https://github.com/elsa-workflows/elsa-foundation/issues/1670) Default flip, Groundwork/Mongo removal and closure audit | Open | EF default hosts, complete active-surface deletion and final audit | #1670 after all replacements | Active epic |
| [#1671](https://github.com/elsa-workflows/elsa-foundation/issues/1671) Complete ledger and legacy mapping | Open | Entry-level registers and truthful ownership/disposition | Current active leaf | In progress |
| [#1672](https://github.com/elsa-workflows/elsa-foundation/issues/1672) EF Runtime and distributed Runtime | Open | All Runtime/distributed semantics and 32 storage units | #1676 then elaborated children | Active epic |
| [#1673](https://github.com/elsa-workflows/elsa-foundation/issues/1673) Governance, inventory and architecture decisions | Open | Governance plus four spikes | Parent of #1671/#1674-#1676/#1669 | Active epic |
| [#1674](https://github.com/elsa-workflows/elsa-foundation/issues/1674) Cross-module EF transaction topology | Open | All transaction acceptance rows A01-A13 in the surface register | #1674 | Active spike |
| [#1675](https://github.com/elsa-workflows/elsa-foundation/issues/1675) EF Core 10 MySQL feasibility | Open | Exact provider/API/version and complete MySQL capability matrix | #1675 | Active spike |
| [#1676](https://github.com/elsa-workflows/elsa-foundation/issues/1676) Hard EF Runtime proving slice | Open | Claims/leases/fencing/idempotency/checkpoint-outbox/crash/concurrency/SQLite contention | #1676 | Active spike |
| [#1677](https://github.com/elsa-workflows/elsa-foundation/issues/1677) EF Design, Publishing, Elsa3 import and Dashboard | Open | 35 owned units plus cross-module publication/import/dashboard semantics | Elaborate worker-ready children after spikes | Active epic |
| [#1678](https://github.com/elsa-workflows/elsa-foundation/issues/1678) General EF lifecycle policy and four-provider test kit | Open | Reusable lifecycle without a universal domain abstraction | #1678 after spikes | Active feature |
| [#1679](https://github.com/elsa-workflows/elsa-foundation/issues/1679) Four-provider Secrets cutover | Open | Add MySQL, revalidate three providers, flip default, remove adapter | #1679 after spikes | Active feature |
| [#1680](https://github.com/elsa-workflows/elsa-foundation/issues/1680) Studio Preferences EF | Open | Scope/read/write/concurrency, migrations, four providers, host/e2e | #1680 after foundation | Active feature |
| [#1681](https://github.com/elsa-workflows/elsa-foundation/issues/1681) Diagnostics EF | Open | Structured Logs and OpenTelemetry lifecycle, ordering, retention, failure and bounded-query behavior | #1681 after foundation | Active feature |
| [#1682](https://github.com/elsa-workflows/elsa-foundation/issues/1682) Elsa Identity EF | Open | IAM and ASP.NET Identity contracts, tenant/normalized uniqueness/atomicity; separate OpenIddict | #1682 after foundation | Active feature |

## Runtime, dispatch, recovery, and execution evidence

| Issue | Live state | Timing-independent requirement carried forward | EF owner / closure gate | Disposition |
|---|---|---|---|---|
| [#674](https://github.com/elsa-workflows/elsa-foundation/issues/674) Transport-neutral `DispatchWorkflow` | Open | Provider-neutral dispatch contract and durable transport boundary | #1672/#1677; prove on EF before superseding storage-specific work | Preserve |
| [#678](https://github.com/elsa-workflows/elsa-foundation/issues/678) Durable detached dispatch | Open | Durable/inspectable dispatch, admission, status and recovery | #1672; R21 and e2e E04 | Preserve |
| [#679](https://github.com/elsa-workflows/elsa-foundation/issues/679) Successful child and safe outputs | Open | Parent/child completion, safe output propagation and failure behavior | #1672/#1677; dispatch e2e | Preserve |
| [#682](https://github.com/elsa-workflows/elsa-foundation/issues/682) Test-run dispatch scope | Open | Test-scope propagation, isolation, cleanup and cancellation | #1672/#1677; R13/R21 | Preserve |
| [#683](https://github.com/elsa-workflows/elsa-foundation/issues/683) Cross-node dispatched execution | Open | Durable distributed placement/transport, redelivery, restart and fencing | #1672/#1676; D01-D03 | Preserve |
| [#804](https://github.com/elsa-workflows/elsa-foundation/issues/804) Persist and share workflow-definition view presets | Open | Tenant-scoped stable references, permission checks, schema validation and no silent rewriting | #1677 Workflows Design child | Preserve |
| [#1127](https://github.com/elsa-workflows/elsa-foundation/issues/1127) Root completion outcome missing from inspection | Open | Correct activity inspection projection after checkpoint/restart | #1672; R08-R10 | Preserve |
| [#1132](https://github.com/elsa-workflows/elsa-foundation/issues/1132) Runtime Execution Evidence PRD | Open | Provider-neutral durable evidence, ordering, completeness and distributed behavior | #1672; reconcile Groundwork-specific design after proving slice | Preserve; re-elaborate |
| [#1134](https://github.com/elsa-workflows/elsa-foundation/issues/1134) Committed lifecycle evidence and completeness | Open | Deterministic workflow-local lifecycle facts, stable identity/sequence, checkpoint barrier, gap/duplicate semantics, retries and e2e | #1672; retire only its benchmark criterion | Preserve correctness |
| [#1136](https://github.com/elsa-workflows/elsa-foundation/issues/1136) Selected workflow state and values | Open | Bounded opt-in capture, redaction, deterministic sanitization, replay and e2e | #1672; retire only value-capture benchmark criterion | Preserve correctness/security |
| [#1137](https://github.com/elsa-workflows/elsa-foundation/issues/1137) Durable/distributed Execution Evidence with EF Core | Open | Existing EF-specific durable/distributed acceptance remains aligned with the all-EF direction | #1672 after #1134 | Preserve/link into Runtime breakdown |
| [#1138](https://github.com/elsa-workflows/elsa-foundation/issues/1138) Execution Evidence conformance fixtures and J-Test | Open | Provider-neutral fixtures, wire compatibility and end-to-end consumption | #1672 | Preserve |
| [#1198](https://github.com/elsa-workflows/elsa-foundation/issues/1198) Checkpoint-commit adapter leaf | Open | Preserve checkpoint adapter correctness, marker/idempotent replay and fault behavior | #1676/#1672; retire remeasurement criterion | Split: correctness preserve, measurement retire |
| [#1233](https://github.com/elsa-workflows/elsa-foundation/issues/1233) Group-commit coordinator | Open | No current correctness requirement to add group commit; retain explicit transaction/idempotent replay constraints | #1674/#1676; close performance proposal as retired after review | Retire performance proposal |
| [#1239](https://github.com/elsa-workflows/elsa-foundation/issues/1239) Count serialized store round-trips | Open | Marker and lease semantics/idempotent replay remain; counting and before/after measurement retire | #1676 maps semantic constraints | Retire measurement after mapping |
| [#1281](https://github.com/elsa-workflows/elsa-foundation/issues/1281) Design-lane post-commit redrive not scheduled | Open | Split-target failure recovery, automatic redrive and idempotency | #1674/#1677 | Preserve |
| [#1305](https://github.com/elsa-workflows/elsa-foundation/issues/1305) Root-write lease per burst | Open | Lease ownership/loss/renewal/release and two-node correctness remain; round-trip benefit retires | #1676/#1672 | Re-evaluate design after spike |
| [#1308](https://github.com/elsa-workflows/elsa-foundation/issues/1308) Checkpoint writes one projection at a time | Open | Atomic multi-projection checkpoint, partial-failure behavior and idempotent recovery | #1676/#1672; retire round-trip optimization criterion | Split correctness from performance |
| [#1309](https://github.com/elsa-workflows/elsa-foundation/issues/1309) Dead `SchedulerState` write lane | Open | Decide/remove unused R15 lane without changing checkpoint semantics | #1676/#1672 | Preserve decision requirement |
| [#1340](https://github.com/elsa-workflows/elsa-foundation/issues/1340) SQL Server scheduler-claim deadlock | Open | Concurrent claims handle deadlock/conflict safely and preserve fencing | #1676/#1672; four-provider test | Preserve |
| [#1409](https://github.com/elsa-workflows/elsa-foundation/issues/1409) Run history invisible on identity-free engine | Open | Correct persisted history query and authority/tenant behavior under anonymous composition | #1672/#1670 | Preserve |
| [#1449](https://github.com/elsa-workflows/elsa-foundation/issues/1449) Shared cached sessions reject concurrent commands | Open | Context/connection ownership, per-caller concurrency, lifetime/disposal and no leaks | #1674/#1676/#1678 | Preserve as spike acceptance |
| [#1522](https://github.com/elsa-workflows/elsa-foundation/issues/1522) Runtime hot-path god methods | Open | Preserve commit/dispatch ordering invariants during persistence replacement; no drive-by refactor required | #1672; re-evaluate after EF slice | Carry invariant, defer refactor |
| [#1525](https://github.com/elsa-workflows/elsa-foundation/issues/1525) Publishing API god methods | Open | Preserve validation, preflight, commit and error ordering while persistence orchestration changes | #1677 Publishing child | Carry invariant, defer refactor |
| [#1526](https://github.com/elsa-workflows/elsa-foundation/issues/1526) Groundwork Runtime duplication/validation drift | Open | Exact validation behavior must not silently diverge; Groundwork refactor itself becomes obsolete after deletion | #1672/#1670 | Supersede after EF guards prove parity |
| [#1530](https://github.com/elsa-workflows/elsa-foundation/issues/1530) Checkpoint writer/validator duplication | Open | Checkpoint validation stays consistent across state kinds; Groundwork writer refactor becomes obsolete | #1672 | Carry correctness; re-evaluate maintainability |
| [#1531](https://github.com/elsa-workflows/elsa-foundation/issues/1531) Runtime hazard batch | Open | Bounded stimulus dedupe and observable rollback failure are relevant to EF transaction/recovery work | #1672/#1674/#1677; other non-persistence findings remain independently owned | Split and preserve applicable criteria |

## Providers, diagnostics, hosts, tests, and performance-policy reconciliation

| Issue | Live state | Timing-independent requirement carried forward | EF owner / closure gate | Disposition |
|---|---|---|---|---|
| [#420](https://github.com/elsa-workflows/elsa-foundation/issues/420) Diagnostics duplication and OTel store behavior | Open | Normalized/bounded OTel queries, failure logging, option validation and consistent API behavior | #1681; timing claims retire | Preserve correctness; split unrelated DRY work |
| [#646](https://github.com/elsa-workflows/elsa-foundation/issues/646) Groundwork diagnostics performance budgets | Open | Retain provider correctness, concurrency, bounded queries, retention, redaction and failure evidence | #1681/#1668; all native-plan/timing/budget verdict criteria retire by owner policy | Retire measurement after correctness mapping |
| [#1185](https://github.com/elsa-workflows/elsa-foundation/issues/1185) SQL Server design search route failure | Open | Bounded, correct SQL Server search/paging with no provider-plan failure | #1677 child/#1678 | Preserve |
| [#1223](https://github.com/elsa-workflows/elsa-foundation/issues/1223) Hard-stop append test continuation flake | Open | A hard stop settles every accepted append without relying on `IsCompleted` scheduling | #1681; verify existing #1222 fix remains represented | Close later as implemented/superseded, not by migration |
| [#1297](https://github.com/elsa-workflows/elsa-foundation/issues/1297) Groundwork SQLite HTTP endpoint starts no workflow | Open | Real HTTP route starts and persists workflow, returns correct response, survives restart | #1672/#1670; T37 and host e2e | Preserve |
| [#1311](https://github.com/elsa-workflows/elsa-foundation/issues/1311) Serialized SQLite fallback | Open | Correct connection ownership for memory/file SQLite and concurrent callers; no locked/leaked handles | #1674/#1678; retire throughput rationale | Preserve correctness, supersede adapter fix later |
| [#1419](https://github.com/elsa-workflows/elsa-foundation/issues/1419) Five backend e2e failures | Open | Reconcile current SetOutput, alteration-plan and OTel search contracts on rebuilt host/fresh DB | Owning #1672/#1681/#1677 features | Preserve applicable failures |
| [#1421](https://github.com/elsa-workflows/elsa-foundation/issues/1421) SQLite fixture handle leaks | Open | Deterministic pool/context disposal and WAL/SHM cleanup; teardown must not hide real failures | #1678 four-provider test kit | Preserve |
| [#1422](https://github.com/elsa-workflows/elsa-foundation/issues/1422) Container-backed tests hang locally | Open | Reliable, bounded Testcontainers lifecycle and cleanup for relational EF providers | #1678; Mongo portions retire | Preserve relational fixture requirement |
| [#1423](https://github.com/elsa-workflows/elsa-foundation/issues/1423) StorePerformance/baseline/architecture unit failures | Open | Reconcile still-current Minimal API and architecture failures; preserve correctness extracted from StorePerformance | #1668/#1670 and owning modules | Split: performance contracts retire |
| [#1425](https://github.com/elsa-workflows/elsa-foundation/issues/1425) Store-performance adapter and concurrency measurement | Open | Concurrent checkpoint/lease correctness discovered by the adapter remains required | #1676/#1668; adapter/measurement request retires | Retire measurement after extraction |
| [#1521](https://github.com/elsa-workflows/elsa-foundation/issues/1521) OTel EF full-table materialization | Open | Push bounded/filterable queries to the database and use normalized lookup with correct parity | #1681 | Preserve bounded-query correctness; no timing gate |
| [#1529](https://github.com/elsa-workflows/elsa-foundation/issues/1529) Structured Logs/Identity EF bug and observability batch | Open | Batched role claims, error-code classification, exception context/logging and operational visibility | #1681/#1682 | Preserve applicable defects |
| [#1576](https://github.com/elsa-workflows/elsa-foundation/issues/1576) Groundwork validation/performance program | Open | Keep stress/concurrency/recovery correctness not otherwise owned | #1668 maps and retires measurement program; module owners carry correctness | Supersede/retire later |
| [#1594](https://github.com/elsa-workflows/elsa-foundation/issues/1594) Typed native-plan evidence/parser deletion | Open | Provider-neutral functional evidence may move to normal contract tests | #1668/#1670; native-plan admission and parser migration retire with performance policy | Retire after correctness audit |
| [#1611](https://github.com/elsa-workflows/elsa-foundation/issues/1611) Mongo diagnostics measurement child records no commands | Open | Structured-log reopen/read/high-water correctness remains covered relationally | #1681; Mongo measurement/topology retires | Retire Mongo/measurement after mapping |
| [#1659](https://github.com/elsa-workflows/elsa-foundation/issues/1659) Publishing foreign slot ownership | Closed | Preflight refuses foreign-owned slot before any write; activation cannot fail after partial publication | #1674/#1677 must retain regression coverage despite closed state | Closed predecessor; preserve criterion |
| [#1661](https://github.com/elsa-workflows/elsa-foundation/issues/1661) Workbench fails activation after first boot | Open | Second-start payload/options/materialization and shell activation correctness | #1670/#1677; restart test | Preserve |

## Additional performance-labelled issues

These issues are explicitly inventoried because #1668 retires every performance artifact and
requirement, not only files under a performance directory. Each row separates any surviving safety
or semantic obligation from the measurement request.

| Issue | Live state | Surviving correctness or safety obligation | EF owner / closure gate | Disposition |
|---|---|---|---|---|
| [#637](https://github.com/elsa-workflows/elsa-foundation/issues/637) Benchmark HTTP route matching at scale | Open | Routing specificity, template matching and snapshot semantics remain ordinary tests | #1668; route correctness stays with HTTP owners | Retire benchmark |
| [#636](https://github.com/elsa-workflows/elsa-foundation/issues/636) Harden executable cache for distributed and lifecycle pressure | Open | Cancellation, disposal, invalidation and cross-node lifecycle safety | #1672 if EF Runtime changes cache composition; otherwise retain separately | Preserve safety, retire any timing gate |
| [#1198](https://github.com/elsa-workflows/elsa-foundation/issues/1198) Checkpoint adapter and remeasurement | Open | Already mapped above: checkpoint marker/replay/fault correctness | #1676/#1668 | Retire measurement; preserve correctness |
| [#1232](https://github.com/elsa-workflows/elsa-foundation/issues/1232) Intrinsic-versus-activity performance guidance | Open | No timing-based authoring guidance remains authoritative | #1668; outside migration implementation after policy cleanup | Retire guidance request |
| [#1238](https://github.com/elsa-workflows/elsa-foundation/issues/1238) Attribute per-hop CLR activation cost | Open | Activity execution and input-snapshot semantics remain ordinary Runtime tests | #1672/#1668 | Retire timing profile |
| [#1249](https://github.com/elsa-workflows/elsa-foundation/issues/1249) External workflow faults past a concurrency threshold | Open | Lease-loss/fencing behavior must fail safely and recover without corruption | #1676 | Preserve safety; retire threshold measurement |
| [#1273](https://github.com/elsa-workflows/elsa-foundation/issues/1273) Re-capture concurrency curve on a quiet host | Open | Qualitative admission and lease safety only | #1676/#1668 | Retire numeric curve/host measurement |
| [#1306](https://github.com/elsa-workflows/elsa-foundation/issues/1306) Input snapshot materialization is O(state) per activity | Open | Complete and correct visible input state across checkpoint/resume | #1672 | Preserve semantics; retire complexity/timing target |
| [#1307](https://github.com/elsa-workflows/elsa-foundation/issues/1307) Timer/resumption pumps use sequential inline drains | Open | No-loss/no-duplicate timers, resumption and recovery | #1672 | Preserve semantics; retire throughput target |
| [#1310](https://github.com/elsa-workflows/elsa-foundation/issues/1310) Admission initial limit versus measured curve | Open | Safe bounded admission and lease behavior under concurrency | #1672/#1676 | Preserve safety; retire numeric limit claim |
| [#1312](https://github.com/elsa-workflows/elsa-foundation/issues/1312) Fusion fast path applies to almost nothing | Open | Replay safety and durability boundaries remain | #1672/#1668 | Retire timing/fusion decision |

`#1198` appears in both the Runtime mapping and this performance-policy index intentionally; it is
one issue row semantically, so register counts treat its repeated policy pointer as a cross-reference,
not another issue.

## Closure procedure

For each legacy issue, post a direct link to the merged EF PR or successor issue and state which
criteria were implemented, superseded, or retired. Do not say `fixed` when the actual result is
adapter deletion or policy retirement. Closed predecessors such as #1659 still constrain regression
coverage. At final program closure, refresh live issue state and search issue bodies/comments for
new persistence findings; this dated register is the baseline, not a promise that GitHub cannot
change.
