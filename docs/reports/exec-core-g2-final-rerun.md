# G-a: final cross-model (native ↔ BPMN) re-run on current main

Docs only. One full re-run of the named G2 / D / G evidence classes on a pinned `origin/main` tip, so milestone G2.5 can be marked **proved-current** or **explicitly not**. No production or test code was changed. Assertions were not weakened, skipped, quarantined, or forced. Native and BPMN semantics are not merged. A fact proved on one plane is not inferred for the other.

Program: [#2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892) (closed 14 November 2026; G-a remaining slice still owner-authorised, Sipke 8 November 2026). Mapping sources used as-is: [G2.4 child-outcome mapping](exec-core-g2-child-outcome-mapping.md), [G3 overhead counts](exec-core-g3-overhead-counts.md), [limitations / deferrals](exec-core-limitations-deferrals.md) (#4317), [S8 migration/rollback](exec-core-migration-rollback.md), [operator note](native-bpmn-migration-and-rollback.md).

**G2.5 on this pin: not a blanket proved-current.** Every named hosted class that has a dual-model proof on this tree passed on this SHA (after one recorded diagnostic re-run of `#3992`). Rows that already had **no product path** or whose hosted dual-model proof is the PowerShell e2e script stay **missing** / **not-run**. See [G2.5](#g25-verdict).

## Pin

| Field | Value |
|---|---|
| Pinned `origin/main` SHA | `ba59f7f1445f310899a954ca1e85700bfaeadd7d` |
| Short | `ba59f7f14` |
| Subject | `docs: note no pre-publication retention guarantee for native and direct-BPMN publish (#4319)` |
| Commit date | 2026-11-19 11:20:20 +0100 |
| Branch during the run | `cursor/exec-core-g2-final-rerun-0e59` at that SHA (no rebase mid-run) |
| Run window (UTC) | 2026-11-19T10:26:32Z start → InstantBpmnWorkbenchTests finished ~10:54Z |

The tree was not rebased, reset, or force-pushed during the run.

## Environment

| Field | Value |
|---|---|
| OS | Ubuntu 24.04.5 LTS (`Linux cursor 6.12.113+ x86_64`) |
| CPU | 4 cores (`nproc`) |
| Memory | ~16 GiB |
| .NET SDK | 10.0.404 (`2ab2854867`) at `$HOME/.dotnet` (this VM snapshot had no SDK; installed once, not a product change) |
| Host runtime | 10.0.15 (`8e0f8ed231`) |
| RID | `linux-x64` |
| DB provider | SQLite (Owned Workbench default; EF tests in-process SQLite) |
| Host count | one in-process Owned Workbench per hosted Fact (tests are sequential; no second host, no cluster) |
| Composition | Root unless a cited theory names Sequence / Flowchart / CompensationScope |
| Evidence restore | `python3 tools/bpmn/restore_evidence.py` (gitignored spec 1017 fixtures; no `--all`) |
| Hosted evidence dir | `ELSA_BPMN_HOST_EVIDENCE_DIRECTORY=/tmp/g2-rerun/host-evidence` |
| Hosted source pin | `ELSA_BPMN_HOST_SOURCE_REVISION=ba59f7f1445f310899a954ca1e85700bfaeadd7d` (set for the diagnostic re-run and all remaining hosted classes; **not** set on the first `#3992` attempt) |
| G3 receipts | `ELSA_G3_EVIDENCE_DIRECTORY=/tmp/g2-rerun/g3` |

No PostgreSQL, SQL Server, MySQL, Docker, Testcontainers, or cloud spend. No new infrastructure.

## Main CI for the pinned SHA

| Field | Value |
|---|---|
| Workflow | `CI` (`.github/workflows/ci.yml`) |
| Event | `push` to `main` |
| Run id | [44504727102](https://github.com/elsa-workflows/elsa-foundation/actions/runs/44504727102) |
| Status / conclusion | `completed` / **`success`** |
| Created / updated | 2026-11-19T10:20:24Z / 2026-11-19T10:40:00Z |

Observed check conclusions on `ba59f7f1445f310899a954ca1e85700bfaeadd7d` (not inferred):

| Check | Conclusion |
|---|---|
| `Build & test` | success |
| `Generated maps fresh` | success |
| `Full tier result` | success |
| `Architecture guards` | success |
| `Core-only build & test` | success |
| `Plan tiers` | success |
| `PR shard (pr-1/2/3)` | success |
| `Packages` run [44504727140](https://github.com/elsa-workflows/elsa-foundation/actions/runs/44504727140) | success |
| `Plan full tier` | skipped |
| `Integration shard` | skipped |
| `EF container shard` | skipped |

This report does **not** claim that integration-tier or EF-container shards executed on this push. The `CI` workflow conclusion is `success`.

Open [`main is red: CI` #4795](https://github.com/elsa-workflows/elsa-foundation/issues/4795) remains open. Its body names an earlier commit (`d49aad9cdae8bc40647494d7dc842c1d704c0467`), not this pin. A later green run does not close that issue automatically. This slice does not close it.

## Exact commands

Working directory: repository root. Configuration: `-c Debug`. Tests used `--no-build` after the four projects had been built once.

Restore (required by hosted Workbench fixtures):

```bash
python3 tools/bpmn/restore_evidence.py
```

Build:

```bash
dotnet build essentials/tests/Activities/Bpmn/Tests/Elsa.Activities.Bpmn.Tests.csproj -c Debug
dotnet build essentials/tests/Workflows/Publishing/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests.csproj -c Debug
dotnet build essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj -c Debug
dotnet build essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj -c Debug
```

Each class was listed then executed once with the same filter (`dotnet test … --list-tests --filter …` then `dotnet test … --filter …`). Filter shape:

```text
dotnet test <csproj> --no-build -c Debug --filter FullyQualifiedName~<Class>
  --logger trx;LogFileName=<Class>.trx
  --logger console;verbosity=detailed
  --results-directory /tmp/g2-rerun/results -v n
```

The `#4095` BPMN live publish-vs-GC pair used:

```text
--filter FullyQualifiedName~BpmnDirectExecutionTests.Bpmn_publish_interrupted|FullyQualifiedName~BpmnDirectExecutionTests.Bpmn_live_publish
```

Hosted classes after the first `#3992` attempt ran with:

```bash
export ELSA_BPMN_HOST_EVIDENCE_DIRECTORY=/tmp/g2-rerun/host-evidence
export ELSA_BPMN_HOST_SOURCE_REVISION=ba59f7f1445f310899a954ca1e85700bfaeadd7d
export ELSA_G3_EVIDENCE_DIRECTORY=/tmp/g2-rerun/g3
```

No retry loop. One diagnostic re-run of a single failing test is recorded below (both results).

On-disk project paths are `essentials/…` (lowercase). Historical reports often write `Essentials/…`.

## Per-class results

`listed` is `--list-tests` count. `passed` / `failed` / `skipped` and `vstest time` come from the console summary (`Total tests` / `Passed` / `Failed` / `Total time`). When the logger omitted `Skipped`, it is recorded as 0. `wall` is operator wall-clock around `dotnet test`. `listed` matched executed totals on every class.

| Class | Project | listed | passed | failed | skipped | vstest time | wall | Result |
|---|---|---|---|---|---|---|---|---|
| `BpmnCallActivityTests` | `Elsa.Activities.Bpmn.Tests` | 15 | 15 | 0 | 0 | 3.2939 s | 3.9 s | pass (probe; not dual-model hosted) |
| `BpmnCalledControlAdapterTests` | `Elsa.Activities.Bpmn.Tests` | 24 | 24 | 0 | 0 | 3.7662 s | 4.4 s | pass (unit adapter) |
| `BpmnDirectExecutionTests` interrupted/live GC (`Bpmn_publish_interrupted`, `Bpmn_live_publish`) | `Elsa.Activities.Bpmn.Tests` | 2 | 2 | 0 | 0 | 4.8621 s | 5.5 s | pass (#4095 BPMN) |
| `InterruptedPublicationGcTests` | Publishing EF | 2 | 2 | 0 | 0 | 2.9551 s | 3.7 s | pass (#4095 native) |
| `CombinedChildOutcomeEfTests` | Runtime EF | 3 | 3 | 0 | 0 | 10.4055 s | 11.2 s | pass |
| `BpmnCalledWorkflowControlEfTests` | Runtime EF | 32 | 32 | 0 | 0 | 1.6684 min | 100.8 s | pass (BPMN-root `AuthoredFailureCode`; not native-root B2) |
| `RunBpmnPendingParentResultCrashTests` (attempt 1) | Hosting | 1 | 0 | 1 | 0 | 1.5539 min | 93.9 s | **fail — operator env; see Findings** |
| `RunBpmnPendingParentResultCrashTests` (diagnostic re-run) | Hosting | 1 | 1 | 0 | 0 | 1.5121 min | 91.3 s | pass |
| `CallActivityNativeChildCrashTests` | Hosting | 2 | 2 | 0 | 0 | 1.1722 min | 71.0 s | pass |
| `CallActivityNativeChildOutcomeTests` | Hosting | 2 | 2 | 0 | 0 | 43.1447 s | 43.8 s | pass |
| `CallActivityNativeChildHeldRetryTests` | Hosting | 1 | 1 | 0 | 0 | 20.7696 s | 21.5 s | pass |
| `RunBpmnChildFaultHeldRetryTests` | Hosting | 3 | 3 | 0 | 0 | 52.6361 s | 53.3 s | pass |
| `RunBpmnChildCancelTests` | Hosting | 1 | 1 | 0 | 0 | 40.2502 s | 40.9 s | pass |
| `RunBpmnAdmissionCancellationTests` | Hosting | 1 | 1 | 0 | 0 | 43.9331 s | 44.6 s | pass |
| `BpmnServiceRetryCancellationTests` | Hosting | 2 | 2 | 0 | 0 | 31.9705 s | 32.7 s | pass |
| `RunBpmnNativeBusinessErrorTests` | Hosting | 2 | 2 | 0 | 0 | 36.1154 s | 36.7 s | pass |
| `LegacyNativeParentUpgradeTests` | Hosting | 2 | 2 | 0 | 0 | 2.0492 min | 123.6 s | pass |
| `BpmnHumanClaimUpgradeTests` | Hosting | 3 | 3 | 0 | 0 | 1.6455 min | 99.4 s | pass |
| `G3OverheadCountTests` | Hosting | 1 | 1 | 0 | 0 | 30.7189 s | 31.3 s | pass (counts only) |
| `G3RemainingJourneyOverheadTests` | Hosting | 3 | 3 | 0 | 0 | 2.1094 min | 127.2 s | pass (counts only; extra to #4039) |
| `RunBpmnWorkbenchTests` | Hosting | 9 | 9 | 0 | 0 | 6.1756 min | 371.1 s | pass |
| `DirectBpmnWorkbenchTests` | Hosting | 2 | 2 | 0 | 0 | 43.7206 s | 44.3 s | pass |
| `InstantBpmnWorkbenchTests` | Hosting | 4 | 4 | 0 | 0 | 2.6006 min | 156.7 s | pass |

Hosted Workbench classes ran without extra infrastructure.

Totals excluding the operator-failed first `#3992` attempt: **117 listed / 117 passed / 0 failed / 0 skipped**. Including that attempt: 118 executed, 117 passed, 1 failed (operator).

## G2.1–G2.4 row mapping (this pin)

Status vocabulary for **this re-run** (not a rewrite of the earlier G2 ledger):

- **proved-current** — the named dual-model hosted (or, where the ledger already accepted it, dedicated) test ran on `ba59f7f14` and passed.
- **failed** — the named test ran and failed a product assertion. (None.)
- **not-run** — a named proof exists in the ledger but was not executed here.
- **missing** — the ledger already records no product path / no dual-model test. Still visible. Not inferred.

Limit of every hosted cell: SQLite Owned Workbench, one in-process host, Root unless the cited test names another parent. No other provider.

Planes are not collapsed:

- **(a)** native parent → BPMN child via `RunBpmn` (`RuntimeModelExecution`).
- **(b)** BPMN `callActivity` → `DispatchWorkflow` → native child (`WorkflowExecution`).
- **serviceTask → native worker** is the direct/embedded native-work path (`StartNativeWork.v1`), not (b).

### G2.1 interrupted cross-model calls

| Row | Direction | Named proof | This run | Status |
|---|---|---|---|---|
| G2.1 (a) | native `RunBpmn` parent; crash after model terminal before parent consume (`#3992`) | `RunBpmnPendingParentResultCrashTests.A_real_native_RunBpmn_recovers_once_when_the_host_crashes_after_the_model_result_before_parent_consumption` | diagnostic re-run pass (1/1). Attempt 1 failed after product assertions at evidence embed (see Findings). Hosted receipt `sourceRevision` = this pin. | **proved-current** |
| G2.1 (b) | composite `callActivity` → native child; wait-phase kill and pending-parent-resume kill (`#3995`) | `CallActivityNativeChildCrashTests` (2 Facts) | 2/2 pass | **proved-current** |
| G2.1 serviceTask → native worker host-kill after result, before parent consume | same interrupt shape on the native-work path | no class in `tests/**` with that shape was located | — | **missing** (not inferred from `DirectBpmnWorkbenchTests.Direct_service_work_…`, which is restart-and-complete, not crash-after-result) |

### G2.2 held retry

| Row | Direction | Named proof | This run | Status |
|---|---|---|---|---|
| G2.2 / G2.4 A3 | (a) `RunBpmn` + BPMN `serviceTask` bound to a native worker: `HoldForRetry` / retry / `FinalFault` (`#3993`) | `RunBpmnChildFaultHeldRetryTests` (3 Facts) | 3/3 pass | **proved-current** |
| G-b | (b) native child under `callActivity`: CompensationScope handler retry (spec 261), not `RetryServiceAsync` | `CallActivityNativeChildHeldRetryTests` (1 Fact) | 1/1 pass | **proved-current** |
| L-Gb1 | operator `RetryFaultedActivity/1` of a native child under `callActivity` | none (limitation; belongs with deferred D3) | — | **missing** |
| L-Gb2 | BPMN `HoldForRetry` / `RetryServiceAsync` on the composite `callActivity` plane | none (no product path) | — | **missing** |

`BpmnServiceRetryCancellationTests` (2/2 pass) is hosted retry-vs-cancel winner on a held **direct** service failure. It is not G2.2 (a) and is not L-Gb2.

### G2.3 host-kill / admission cancel

| Row | Direction | Named proof | This run | Status |
|---|---|---|---|---|
| G2.3 (b) | `callActivity` host-kill (`#3995`; same class as G2.1 (b)) | `CallActivityNativeChildCrashTests` | 2/2 pass | **proved-current** |
| G2.3 (a) admission | native parent cancel before model admission | `RunBpmnAdmissionCancellationTests` | 1/1 pass | **proved-current** |
| G2.3 (a) interrupt | same as G2.1 (a) | `RunBpmnPendingParentResultCrashTests` | see G2.1 (a) | **proved-current** |

If G2.3 is read as “only `#3995`”, the (a) admission row is supporting D/G evidence, not a second G2.3 definition.

### G2.4 child outcomes

Copied from [exec-core-g2-child-outcome-mapping.md](exec-core-g2-child-outcome-mapping.md); status is **this re-run**, not a silent upgrade of LIMITATION / PROBE-ONLY rows.

| Row | Direction | Child → parent | Named proof | This run | Status |
|---|---|---|---|---|---|
| A1 | (a) | success → `RunBpmn` `Done`, Root `Completed` | `RunBpmnWorkbenchTests` (`cancel: false`, including `parent: "Root"`); also Instant `First_decision_completion_…`; crash class after consume | 9/9, 4/4, crash re-run 1/1 | **proved-current** |
| A2 | (a) | native worker `Completed` + `BusinessError` | `RunBpmnNativeBusinessErrorTests` (matching boundary; unmatched faults the model) | 2/2 | **proved-current** |
| A3 | (a) | technical `Faulted` / `HoldForRetry` / `FinalFault` | `RunBpmnChildFaultHeldRetryTests` | 3/3 | **proved-current** |
| A4 | (a) | child cancel → parent `Cancelled` once | `RunBpmnChildCancelTests` | 1/1 | **proved-current** |
| B1 | (b) | native child success | `CallActivityNativeChildCrashTests` (hosted). Unit `BpmnCallActivityTests.NonFailureOutcome_RoutesNormalOutbound` is a **probe** (15/15 pass; not this proof). | 2/2 hosted | **proved-current** |
| B2 | (b) | native-root **business** error across `DispatchWorkflow` | no product path (`AuthoredFailureCode` is BPMN-root only; EF `BpmnCalledWorkflowControlEfTests` is that other child type) | EF 32/32 pass is **not** B2 | **missing** (LIMITATION; not an untested shipped path) |
| B3 | (b) | native child technical `Faulted` with catch-all boundary | ledger proof is `e2e-tests/bpmn/Test-BpmnCallActivity.ps1` Scenario B, not a `tests/**` class. Probe `BpmnCallActivityTests.FailureOutcome_WithErrorBoundary_FiresBoundary_NoIncident` ran (not dual-model). | e2e **not-run**; probe 15/15 | **not-run** (hosted dual-model). Uncaught B3 remains **PROBE-ONLY** / G-f. |
| B4 | (b) | native child cancel | `CallActivityNativeChildOutcomeTests` (boundary recover; uncaught faults composite) | 2/2 | **proved-current** |

Related D/G classes that ran and are **not** silent G2.4 upgrades: `LegacyNativeParentUpgradeTests` (2/2), `BpmnHumanClaimUpgradeTests` (3/3), `CombinedChildOutcomeEfTests` (3/3), `#4095` live publish-vs-GC (native 2/2, BPMN 2/2).

## G2.5 verdict

**Explicitly not a blanket proved-current** for every G2.1–G2.4 cell.

Proved-current on `ba59f7f1445f310899a954ca1e85700bfaeadd7d` (SQLite, one host, Root unless cited): G2.1 (a), G2.1 (b), G2.2 / A3, G-b CompensationScope handler retry, G2.3 (b) and (a) admission, G2.4 A1–A4, B1, B4.

Still visible, not inferred:

| Cell | Why G2.5 cannot swallow it |
|---|---|
| B2 | **missing** — no product path. Needs design. |
| B3 hosted dual-model | **not-run** — `Test-BpmnCallActivity.ps1` was not executed (outside `tests/**`; no new infrastructure). Probe-only C# is not this proof. G-f uncaught B3 stays PROBE-ONLY. |
| G2.1 serviceTask → worker crash-after-result | **missing** — no dedicated class located. |
| L-Gb1 / L-Gb2 | **missing** — recorded limitations. |
| Other providers / multi-host | **not-run** — out of limit. |

## G3 overhead counts (one run; not a budget)

`#4039` harness plus the `#4044` remaining-journey class, one execution each. Receipts under `/tmp/g2-rerun/g3`. Transactions stayed 0 (SQLite EF logged no `Database.Transaction` Information lines; method limit). Runtime columns are `elsa_runtime_*` only.

| Journey | Receipt | All-EF RT | Runtime RT | All-EF writes | Runtime writes | Lease | Fence | vstest wall of Fact |
|---|---|---|---|---|---|---|---|---|
| `#4028` child-to-parent cancel | `g3-child-cancel.json` | 26841 | 989 | 25041 | 200 | 48 | 84 | 30 s (`G3OverheadCountTests`) |
| `#3995` callActivity pending-parent-resume | `g3-callactivity-pending-parent-resume.json` | 24023 | 844 | 22313 | 245 | 18 | 54 | 25 s |
| `#3993` held native-worker retry → Done | `g3-held-native-service-retry.json` | 26944 | 2684 | 24230 | 748 | 121 | 248 | 20 s |
| `#3992` pending-parent-result crash | `g3-pending-parent-result-crash.json` | 269392 | 3363 | 260842 | 187 | 55 | 329 | 1 m 20 s |

`#4028` semantic Cancel counts on this run (asserted by the harness, not a budget): scheduler Cancel commands issued (SQLite `ContentJson`) = 1; HTTP cancel applied / replayed / conflicted = 1 / 1 / 1; native `RunBpmn` `Cancelled` = 1.

The `#3992` all-EF spike is the same method limit recorded in G3 slice 6: Information-level logging during the one-minute `ClaimVisibilityTimeout` wait. Not a product defect and not a threshold.

## Findings

### 1. First `#3992` attempt failed after product assertions (operator environment)

Not a product defect on this pin. Recorded because the first execution exited 1.

Class: `RunBpmnPendingParentResultCrashTests`. vstest 1.5539 min. Exit 1.

```text
Failed Elsa.Activities.Bpmn.Hosting.Tests.RunBpmnPendingParentResultCrashTests.A_real_native_RunBpmn_recovers_once_when_the_host_crashes_after_the_model_result_before_parent_consumption [1 m 32 s]
Error Message:
 System.InvalidOperationException : Embedding evidence requires its exact executing source.
Stack Trace:
   at Elsa.Activities.Bpmn.Hosting.Tests.RunBpmnPendingParentResultCrashTests.A_real_native_RunBpmn_recovers_once_when_the_host_crashes_after_the_model_result_before_parent_consumption() in essentials/tests/Activities/Bpmn/Hosting/Tests/RunBpmnPendingParentResultCrashTests.cs:line 177
```

Cause: `ELSA_BPMN_HOST_EVIDENCE_DIRECTORY` was set so the Fact would write receipts, but `ELSA_BPMN_HOST_SOURCE_REVISION` was unset. Lines 165–174 (restart, `Completed` / `Done`, retained embedding) had already asserted. Line 177 throws before writing `case.json`.

Diagnostic re-run (allowed once; both results kept): same filter, `ELSA_BPMN_HOST_SOURCE_REVISION=ba59f7f1445f310899a954ca1e85700bfaeadd7d`. vstest 1.5121 min. **1/1 passed.** Receipt `host-evidence/root-pending-parent-result-crash/case.json` has `"sourceRevision": "ba59f7f1445f310899a954ca1e85700bfaeadd7d"`.

No product or test change. No second re-run of any other class.

### 2. No other failing class

Every other named class exited 0 with listed = passed.

## Not run (and why)

| Item | Why |
|---|---|
| `e2e-tests/bpmn/Test-BpmnCallActivity.ps1` (B3 hosted dual-model) | Not a `tests/**` class. User list named C# classes. No new infrastructure. |
| Other persistence providers | Limit is SQLite. No Docker / Testcontainers / cloud. |
| Multi-host / clustered placement | Limit is one in-process host. |
| Studio / browser / composed UX | Out of this slice. |
| Architecture-guard / maps-check on this docs PR | Not part of the evidence re-run; docs-only CI fast path applies to the PR itself. |

## What this does not claim

- Closure of Program [#2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892), G-c, D3, B2 design, L-Gb1, L-Gb2, G-f, or `#4795`.
- Merged native vs BPMN cancel / retry / alteration semantics.
- A budget, SLO, or G3 speedup.
- That probe `BpmnCallActivityTests` or BPMN-root `BpmnCalledWorkflowControlEfTests` prove native-root B2/B3.
- Qualification beyond SQLite Owned Workbench, one host.

## Out of scope (honoured)

No production code. No test code. No assertion change, skip, or quarantine. No force push. No new infrastructure. No issue closed.
