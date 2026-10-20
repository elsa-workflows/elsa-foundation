# Native hosting conformance: implementation checkpoint

Status: **in progress**, 19 October 2026. [Task #3056](https://github.com/elsa-workflows/elsa-foundation/issues/3056) is the active leaf under feature #2905 and program #2892. Acceptance and remaining work live in [spec 249](../../specs/249-native-hosting-conformance/spec.md) and its [tasks](../../specs/249-native-hosting-conformance/tasks.md); this report records evidence, not new architecture.

## Integrated source and retained artifacts

The integrated matrix passed **11/11, zero failures/skips**, on source commit [`09a7e7dfc47da7df12fc75af6c44015b57bebc44`](https://github.com/elsa-workflows/elsa-foundation/commit/09a7e7dfc47da7df12fc75af6c44015b57bebc44), including main `e996bf02a2d58e213b937fba108be58fdee09cd3`. Only documentation changed while it ran. Configuration is `Release`, target `net10.0`, on local macOS ARM64. `WorkbenchBuild.AssemblyPath` selects the host from its own build output with the test assembly's configuration and target framework; `NativeHostingScenario.CreatePackage` packages the corresponding fixture DLL and committed manifest. The build log records those output paths.

Captured SHA-256 values for that run:

| Artifact (repository-relative) | SHA-256 |
|---|---|
| `Essentials/apps/Elsa.Workbench/bin/Release/net10.0/Elsa.Workbench.dll` | `28b4ce771ac2bc8fbf2435e1ad1df0cc89d40298a653e1a6a05fabfca3001212` |
| `Essentials/tests/Workbench/Tests/bin/Release/net10.0/Elsa.Workbench.Tests.dll` | `5af3fde0faaea85c745467918bcfa2fc45d63a5ef61319065b39f1c524fa04ba` |
| `Essentials/tests/Workbench/Fixtures/BackgroundActivities/bin/Release/net10.0/Elsa.Workbench.Tests.BackgroundActivities.dll` | `70245c1c601c0c683f1f3cd40c4bf00e65cf7264a57fac9b6371c0861a3d06b8` |
| `Essentials/tests/Workbench/Fixtures/BackgroundActivities/elsa-package.json` | `6cff2ebdf32af0d341a1c01033d060110b9d6cc740b782de8374eb01f84d43ef` |

The [machine-readable run record](../../specs/249-native-hosting-conformance/evidence/native-integrated-20261019.json) binds the TRX run ID, times, per-case results and file hashes to these source/build identities.

Local retained evidence root: `/tmp/elsa-program-20261019/native-hosting-baseline/`. The integrated output is in `native-integrated-matrix.log`, its VSTest results in `native-integrated-matrix/native-integrated-matrix.trx`, and captured identity in `native-integrated-source-identity.json`. These are local diagnostic artifacts, not downloadable CI artifacts. The exact source pin, commands and conclusions are retained here; the qualification PR records hosted check URLs separately. Earlier aliases below identify incremental runs and are not retroactively assigned this source SHA.

Each scenario owns fresh content roots, SQLite databases, authentication keys, lock directories and ephemeral loopback ports. `NativeHostingFixture` selects `InProcess` or `Clustered`; the clustered profile configures actual EF membership, placement and command transport. Each scenario creates one uniquely versioned package, computes its SHA-256 and verifies identical copied bytes on its hosts. Independent scenarios do not claim the same ZIP hash. Scenario roots are removed by owned cleanup; the recorded DLL hashes identify the build inputs rather than retained per-case package archives.

## Final review corrections

Root and independent review identified stale report wording and missing durable source identity; both are corrected above. The first integrated architecture run passed 576 tests and failed two path/folder guards for the new fixture unit-test project. It was relocated to the canonical `Essentials/tests/Workbench/BackgroundActivities/Tests` layout in `7537b3e4ce8e249d8cd8c9ed9385a511fdb1eae4`; no guard was changed. The relocated project passed **44/44, zero failures/skips**, in `native-final-fixture-layout`. The next architecture run exposed its stale dependency-map entry (577 passed / 1 failed). After deliberate map regeneration, the full architecture gate passed **578/578, zero failures/skips**, in `native-final-architecture-restored-maps`; map freshness, generated solution filters and diff checks passed.

The successful concurrent-routing cases originally checked their actual IDs but did not retain them after owned journal cleanup. The same commit adds a structured successful-run receipt without changing assertions, timing limits or production behavior. Both receipt orders passed again, **2/2, zero failures/skips**, in `native-final-routing-receipts`. [Their retained record](../../specs/249-native-hosting-conformance/evidence/native-routing-receipts-20261019.json) contains the exact source/build and TRX identities, actual process and host IDs, package hash, receipt digest, correlated send/lease/ack, placement release, execution fences and native completion. This focused follow-up is separate from the earlier 11-case matrix.

Final acceptance review clarified FR-008: the shared recovery/cancellation scenarios are reusable through host-profile selection, while the separately scoped Orleans adapter must add its own composition and provider-specific routing/fencing proof. This changes no runtime code or qualification scope. The implementation PR carries terminal spec status and checklist cleanup for their atomic landing; the open delivery gates below remain explicit.

## Executed process evidence

| Scenario | Evidence | Boundary |
|---|---|---|
| Admission, body lease reclaim, committed receipt recovery and terminal-history restart | Earlier explicit-profile run: 7 passed, 0 failed/skipped, including both receipt orders under InProcess/Clustered, both cancellation cases and lease-wait arithmetic | Sequential replacement processes; not simultaneous-host takeover |
| Concurrent routing-owner departure and native continuation | Earlier resolved-EF run: both receipt orders passed | Two owned local POSIX Workbench processes, shared SQLite, native composition |
| Cancellation, late receipt rejection and restart with observation disabled | Same earlier resolved-EF run: both provider profiles passed | Preserves the existing tombstone and cancelled-history assertions |

The earlier resolved-EF run totals **4 passed, 0 failed, 0 skipped**. Its command is in [research](../../specs/249-native-hosting-conformance/research.md#concurrent-routing-checkpoint-19-october-2026), with logs/TRX retained as `native-resolved-ef-conformance` in the session evidence directory. Earlier profile evidence is retained separately as `native-profile-recovery`; it is not presented as a later full-matrix rerun.

The concurrent test authors one Sequence → Flowchart → parallel background bodies → final activity definition through REST. Both hosts copy the same package bytes. It asserts the real distributed actor provider, EF membership/placement/transport implementations, and actual provider names and absolute data sources from all four registered scoped EF contexts. The file-lock folder is checked as configuration. Observation never opens connections or emits connection strings.

A real receipt is correlated by work ID and worker PID. A subsequent runtime-resumption command must be newly enqueued after that receipt. Its actual EF transport item ID is followed through survivor lease and acknowledgement. Fresh membership snapshots show both hosts live before the kill. After the kill, the real placement release succeeds before lease expiry, and a separate fresh snapshot taken after that release shows the original owner departed. This observer does not capture the pump's authorizing read. The survivor takes a greater placement token, completes both branches and one native final step, and preserves body invocation/attempt records without re-execution. Its later native execution fence is also greater than the original recorded fence.

Fresh-read authorization belongs to the owning-layer [FailoverTests](../../Essentials/tests/Workflows/Runtime/Distributed/Tests/Placement/FailoverTests.cs): cached-only departure, a restarted or absent host, and failure to obtain a fresh read must not permit reclaim. These use the real placement pump with a modeled fleet and stores; they are not process-level membership evidence.

## Stale-write and graceful-drain checkpoint

The strengthened focused run passed **2/2**, zero failures/skips (`native-stale-graceful-diagnostics`): original uncancelled stale-checkpoint rejection and graceful handoff. The stale case uses the actual coalescing/EF path, the original commit identity/fingerprint/token, a real persisted live successor, the exact `StaleToken` exception and unchanged durable activity/receipt/scheduler/marker state. It also observes the original heartbeat gate and real cleanup outcome, then completes native recovery with no incidents. The measured scheduler row is unclaimed; this does not invent a durable scheduler claim.

Graceful handoff observes SIGTERM, the actual released placement lease and a fresh post-release Draining/live owner snapshot. The active survivor starts new directly addressed work while the owner is held in its bounded stop, then completes that work and the already accepted workflow after clean owner exit. This proves survivor admission and completion, while owning-layer tests supply routing exclusion semantics.

The targeted EF fence-removal control failed for the intended reason: the stale original committed its marker, Completed activity and Accepted receipt under a live higher successor fence. Production source was restored and its diff verified empty. The rebuilt `native-stale-graceful-final` run passed both cases again, **2/2 with zero failures/skips**. The integrated 11-case run above also passed both cases on the committed source after the prerequisite main merge. [Research](../../specs/249-native-hosting-conformance/research.md#original-stale-write-and-graceful-drain-validation-19-october-2026) records identities, earlier invalid controls and an earlier unexplained incident assertion. Those failures are retained rather than represented as passing proof.

## Owning-layer qualification

The following runs used Release, `--no-restore -p:WarningsNotAsErrors=NU1603` and the unchanged production sources at base `5e15ddb5d`. The process fixture additions do not alter those sources. Log/TRX stems are retained in the same session evidence directory.

| Project / filter | Passed / failed / skipped | Evidence and boundary |
|---|---|---|
| `Essentials/tests/Workflows/Runtime/Distributed/Tests`, `FullyQualifiedName~Placement` | 83 / 0 / 0 | `native-placement-qualification`: real pump/provider with modeled fleet/stores; FailoverTests, RollingUpgradePlacementTests, DrainingTests and RunnabilityReportTests cover fresh authorization, capability and drain decisions |
| `Essentials/tests/Workflows/Runtime/Distributed/Persistence/EntityFrameworkCore/Tests`, entire project | 107 / 0 / 0 | `native-distributed-ef-qualification`: real SQLite placement/transport persistence, scope separation and backend registration; in-process store tests |
| `Essentials/tests/Cluster/Tests`, entire project | 159 / 0 / 30 | `native-membership-contract-qualification`: provider-neutral contracts, reference fleet, in-process provider and deliberately broken-provider controls; 30 multi-member cases are explicitly inapplicable to the cluster-of-one provider |
| `Essentials/tests/Cluster/EntityFrameworkCore/Tests`, `SqliteEfClusterMembership`, `EfClusterMembershipStoreTests`, `EfClusterMembershipRegistrationTests`, `ClusterMembershipConfigurationTests`, `ClusterMembershipHostStopTests` | 88 / 0 / 1 | `native-membership-ef-qualification`: real SQLite membership with controlled member handles, conformance/incarnation scenarios, configuration/registration and host stop; FR039 fleet-entry diagnostic is skipped because the race-free join never produces that entry; FR004b checks the terminal duplicate-host refusal instead |
| `Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests`, `BackgroundActivityWorkTests`, `BackgroundActivityWorkCheckpointAtomicityTests`, `NativeBackgroundActivityFlowchartEfTests` | 26 / 0 / 0 | `native-acceptance-ef-qualification`: real SQLite receipt acceptance, duplicates/conflicts, checkpoint atomicity and native Flowchart continuation in process; does not claim duplicate network delivery between hosts |

Multiple filters in a row are joined with `FullyQualifiedName~...` and `|`. Skips are unavailable evidence, not passes. These owning-layer results are separate from the real graceful admission/handoff evidence above, and they do not qualify other database providers.

Rollback of qualification is limited to its test fixture, tests and documentation; no production provider or persistence format changes here. Disable the opt-in observation to restore the existing fixture profile. The separately delivered task-lifetime prerequisite has its own PR and evidence; reverting it requires rechecking the preserved recovery baseline. Do not remove runtime fencing, membership authorization or receipt guards to make a scenario pass.

## Fixture registration and disposal

The dedicated `Essentials/tests/Workbench/BackgroundActivities/Tests` project passed **44/44**, zero failures/skips, after the canonical project relocation in `native-final-fixture-layout`. These are in-process dependency-injection tests, not distributed execution evidence. They cover required scoped registrations, backend ownership and type checks, invalid observation configuration, stale-witness coalescer/EF holder selection, and exactly-once synchronous/asynchronous disposal through retained holders. No private-constructor reflection is used.

## Delivery prerequisite

The activation/task lifetime repair merged in [PR #3077](https://github.com/elsa-workflows/elsa-foundation/pull/3077) as `c6271a8071b303644e5fa5a6bd177f975c521259`. Exact-head PR gates passed. On the merge commit, Studio, CodeQL and maps passed; backend CI failed in the unchanged standalone Jint cancellation test, so downstream architecture/core jobs were skipped.

[PR #3082](https://github.com/elsa-workflows/elsa-foundation/pull/3082) isolates that test from its competing allocation cap and records exception details. Local Jint 84/84, architecture 578/578, maps and the delayed-cancellation adverse control passed at `c93344869`. Production sandbox limits are unchanged. Exact-head backend, core, architecture, Studio and maps passed, and the repair merged as `e996bf02a`. Exact-merge [CI 39329100787](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39329100787), [Studio 39329100635](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39329100635) and [CodeQL 39329100537](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39329100537) passed on `e996bf02a2d58e213b937fba108be58fdee09cd3`. Root independently checked the complete CI logs: backend 16,642 passed / 0 failed / 32 skipped; core 4,021 / 0 / 1; architecture 578 / 0 / 0. These per-job counts overlap. Generated maps passed; scheduled-only EF container jobs were skipped. The original failed CI log did not identify its exception, so the controlled memory-limit mechanism does not establish that run's exact cause.

## Hosted CI deadline correction

PR-head [CI 39336931642](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39336931642) on `b4d576e624ee8854822171dd3662ae02b4145727` was cancelled at the backend job's 30-minute execution limit. Root and independent log review found 90 completed suite summaries totaling **16,643 passed / 0 failed / 32 skipped**, including fixture registration/disposal **44/44**. These are partial results: `Elsa.Workbench.Tests` began but has no completion summary, and downstream core-only and architecture checks were skipped. Studio and maps passed on that head. [The retained deadline record](../../specs/249-native-hosting-conformance/evidence/native-ci-budget-20261019.json) preserves the job/annotation identities, partial counts and log hash.

The correction raises only the existing backend job budget to 45 minutes and removes an obsolete end-to-end duration promise. It preserves the complete filter/build/test invocation, individual scenario limits, required check identity, dependencies and triggers. Fresh complete CI must establish the missing result; no assertion failure was observed before cancellation, and no passing outcome is inferred from that absence. The historical receipt/HTTP cause limits remain unchanged.

## Completed hosted failure and inspection prerequisite

The next run, [CI 39341212215](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39341212215), completed with a test failure on head `b656e3a8ed8bc59d02291da4af3912fd16b7139b`, before the 45-minute deadline. Its 91 completed assembly summaries report **16,695 passed / 1 failed / 32 skipped**. The Workbench suite reports 52 passed / 1 failed / 0 skipped; fixture registration/disposal passed 44/44. Downstream core-only and architecture jobs were skipped. This is a failed gate, distinct from the earlier deadline cancellation.

The failed theory row was `Killed_workers_are_reclaimed_with_stable_invocations_and_one_native_continuation(Clustered, left, right)`. After the second branch was released, the instance-detail GET returned HTTP 400. The old helper used `GetFromJsonAsync`, so the retained exception omitted the response body and host output. [The failure record](../../specs/249-native-hosting-conformance/evidence/native-http-failure-20261020.json) preserves its identity and evidence limits. The original incident's exact cause remains unproven.

Separate [bug #3090](https://github.com/elsa-workflows/elsa-foundation/issues/3090) deterministically reproduced a real inspection count/page race on unchanged main `42226851b`: an independent write between count and page reads violated the exact-count invariant. [PR #3091](https://github.com/elsa-workflows/elsa-foundation/pull/3091) repairs that mechanism with a coherent short transaction, explicit caller-transaction handling and actual SQLite/PostgreSQL/SQL Server/MySQL evidence. Its reviewed head `4931083ca` passed all PR gates and merged as `8df68f10e2625d505e41a16428070a0e7cf48ad4`. The qualification branch integrates that repair with a normal merge. Its resulting-main verification and this branch's fresh integrated validation remain pending; neither the separate reproduction nor a later green run can recover the missing historical response.

The shared scenario sender now preserves failed GET method/path, status, response body and owned-host output before parsing, with immediate failure rather than a retry or ignored HTTP400. Both GET paths and existing POST use the same helper. Its focused diagnostic and successful-parsing tests passed 2/2. Temporarily removing the body from the actual helper made the diagnostic case fail at its missing-body assertion; restoration passed 2/2. [The focused record](../../specs/249-native-hosting-conformance/evidence/native-http-diagnostics-20261020.json) distinguishes that actual-helper control from the earlier direct `GetFromJsonAsync` baseline. The integrated recovery results are recorded below; final review remains open, and no completion is credited to the store repair alone.

## Integrated repair validation, 20 October

The complete affected Workbench project passed **55/55, zero failures/skips**, at source `1b48aebd157a82d34d2539f9113590aebae160b0`, which includes the normal merge of main `8df68f10e`. Code and configuration were unchanged during execution; only quickstart Markdown changed. The run contains all eleven native process/lease cases and both diagnostic cases, including the previously failed Clustered left-then-right recovery row, both concurrent routing orders, the original uncancelled stale checkpoint and graceful handoff. These subsets are part of the 55, not additional unique tests. Passing that row does not establish the old HTTP400's omitted cause.

The fixture project then passed **44/44**, and the architecture project passed **578/578** after the required project-graph restore; all had zero failures/skips. Generated-map freshness and diff checks passed. Root inspected the terminal TRX results and confirmed the tested source/build digests remained unchanged after these sequential phases. [The retained integration record](../../specs/249-native-hosting-conformance/evidence/native-diagnostics-integrated-20261020.json) binds commands, source, binaries, TRX identities and the thirteen selected native/diagnostic outcomes. The later receipt/checklist changes are documentation only. The final review and fresh exact-head hosted/resulting-main gates remain open.

## Remaining delivery gates

Final PR review, exact-head CI/Maps and resulting-main verification remain required before reporting #3056 fully verified. Local gate completion does not mark the broader program complete.

The concurrent routing case begins at a quiescent background wait after the original native execution lease was released. The original stale-write case above supplies separate active-lease fencing evidence. SQLite local-process results do not qualify other databases, multi-machine partitions, clock skew, Orleans or other execution models. Physical external effects remain at least once. The historical receipt and HTTP timeout causes remain unproven by these passing runs.
