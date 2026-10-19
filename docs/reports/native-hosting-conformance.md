# Native hosting conformance: implementation checkpoint

Status: **in progress**, 19 October 2026. [Task #3056](https://github.com/elsa-workflows/elsa-foundation/issues/3056) is the active leaf under feature #2905 and program #2892. Acceptance and remaining work live in [spec 249](../../specs/249-native-hosting-conformance/spec.md) and its [tasks](../../specs/249-native-hosting-conformance/tasks.md); this report records evidence, not new architecture.

## Executed process evidence

| Scenario | Evidence | Boundary |
|---|---|---|
| Admission, body lease reclaim, committed receipt recovery and terminal-history restart | Earlier explicit-profile run: 7 passed, 0 failed/skipped, including both receipt orders under InProcess/Clustered, both cancellation cases and lease-wait arithmetic | Sequential replacement processes; not simultaneous-host takeover |
| Concurrent routing-owner departure and native continuation | Final resolved-EF run: both receipt orders passed | Two owned local POSIX Workbench processes, shared SQLite, native composition |
| Cancellation, late receipt rejection and restart with observation disabled | Same final run: both provider profiles passed | Preserves the existing tombstone and cancelled-history assertions |

The final run totals **4 passed, 0 failed, 0 skipped**. Its command is in [research](../../specs/249-native-hosting-conformance/research.md#concurrent-routing-checkpoint-19-october-2026), with logs/TRX retained as `native-resolved-ef-conformance` in the session evidence directory. Earlier profile evidence is retained separately as `native-profile-recovery`; it is not presented as a later full-matrix rerun.

The concurrent test authors one Sequence → Flowchart → parallel background bodies → final activity definition through REST. Both hosts copy the same package bytes. It asserts the real distributed actor provider, EF membership/placement/transport implementations, and actual provider names and absolute data sources from all four registered scoped EF contexts. The file-lock folder is checked as configuration. Observation never opens connections or emits connection strings.

A real receipt is correlated by work ID and worker PID. A subsequent runtime-resumption command must be newly enqueued after that receipt. Its actual EF transport item ID is followed through survivor lease and acknowledgement. Fresh membership snapshots show both hosts live before the kill. After the kill, the real placement release succeeds before lease expiry, and a separate fresh snapshot taken after that release shows the original owner departed. This observer does not capture the pump's authorizing read. The survivor takes a greater placement token, completes both branches and one native final step, and preserves body invocation/attempt records without re-execution. Its later native execution fence is also greater than the original recorded fence.

Fresh-read authorization belongs to the owning-layer [FailoverTests](../../Essentials/tests/Workflows/Runtime/Distributed/Tests/Placement/FailoverTests.cs): cached-only departure, a restarted or absent host, and failure to obtain a fresh read must not permit reclaim. These use the real placement pump with a modeled fleet and stores; they are not process-level membership evidence.

## Owning-layer qualification

The following runs used Release, `--no-restore -p:WarningsNotAsErrors=NU1603` and the unchanged production sources at base `5e15ddb5d`. The process fixture additions do not alter those sources. Log/TRX stems are retained in the same session evidence directory.

| Project / filter | Passed / failed / skipped | Evidence and boundary |
|---|---|---|
| `Essentials/tests/Workflows/Runtime/Distributed/Tests`, `FullyQualifiedName~Placement` | 83 / 0 / 0 | `native-placement-qualification`: real pump/provider with modeled fleet/stores; FailoverTests, RollingUpgradePlacementTests, DrainingTests and RunnabilityReportTests cover fresh authorization, capability and drain decisions |
| `Essentials/tests/Workflows/Runtime/Distributed/Persistence/EntityFrameworkCore/Tests`, entire project | 107 / 0 / 0 | `native-distributed-ef-qualification`: real SQLite placement/transport persistence, scope separation and backend registration; in-process store tests |
| `Essentials/tests/Cluster/Tests`, entire project | 159 / 0 / 30 | `native-membership-contract-qualification`: provider-neutral contracts, reference fleet, in-process provider and deliberately broken-provider controls; 30 multi-member cases are explicitly inapplicable to the cluster-of-one provider |
| `Essentials/tests/Cluster/EntityFrameworkCore/Tests`, `SqliteEfClusterMembership`, `EfClusterMembershipStoreTests`, `EfClusterMembershipRegistrationTests`, `ClusterMembershipConfigurationTests`, `ClusterMembershipHostStopTests` | 88 / 0 / 1 | `native-membership-ef-qualification`: real SQLite membership with controlled member handles, conformance/incarnation scenarios, configuration/registration and host stop; FR039 fleet-entry diagnostic is skipped because the race-free join never produces that entry; FR004b checks the terminal duplicate-host refusal instead |

Filters in the last row are joined with `FullyQualifiedName~...` and `|`. Skips are unavailable evidence, not passes. These results do not supply the still-pending real graceful admission/handoff case, and they do not qualify other database providers.

Rollback of qualification is limited to its test fixture, tests and documentation; no production provider or persistence format changes here. Disable the opt-in observation to restore the existing fixture profile. The separately delivered task-lifetime prerequisite has its own PR and evidence; reverting it requires rechecking the preserved recovery baseline. Do not remove runtime fencing, membership authorization or receipt guards to make a scenario pass.

## Delivery prerequisite

The activation/task lifetime repair merged in [PR #3077](https://github.com/elsa-workflows/elsa-foundation/pull/3077) as `c6271a8071b303644e5fa5a6bd177f975c521259`. Exact-head PR gates passed. On the merge commit, Studio, CodeQL and maps passed; backend CI failed in the unchanged standalone Jint cancellation test, so downstream architecture/core jobs were skipped.

[PR #3082](https://github.com/elsa-workflows/elsa-foundation/pull/3082) isolates that test from its competing allocation cap and records exception details. Local Jint 84/84, architecture 578/578, maps and the delayed-cancellation adverse control passed at `c93344869`. Production sandbox limits are unchanged. Hosted and resulting-main checks remain pending. The original failed CI log did not identify its exception, so the controlled memory-limit mechanism does not establish that run's exact cause.

## Remaining qualification

- Original uncancelled engine checkpoint rejected by the actual EF stale-token stage, including the restored/bypassed safeguard control.
- Focused observation registration/disposal coverage, graceful handoff and the remaining lower-tier provider/acceptance evidence matrix.
- Integrated review, exact-head CI/Maps and resulting-main verification before closing #3056.

The concurrent routing case begins at a quiescent background wait after the original native execution lease was released. It is not the pending stale-write proof. SQLite local-process results do not qualify other databases, multi-machine partitions, clock skew, Orleans or other execution models. Physical external effects remain at least once. The historical receipt and HTTP timeout causes remain unproven by these passing runs.
