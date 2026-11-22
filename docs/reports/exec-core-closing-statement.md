# G-c: native/BPMN execution-core milestone closing statement

Docs only. No production or test code was changed. Native and BPMN semantics are not merged. A fact proved on one plane is not inferred for the other. Statuses are copied from the cited sources; none are upgraded.

This statement does **not** declare the 8 November 2026 SQLite native/BPMN execution-core milestone done. Program [#2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892) was closed 14 November 2026 as completed; the G-c remaining slice stays owner-authorised (Sipke, 8 November 2026; owner decisions 11 November 2026). See [Owner decision needed](#owner-decision-needed).

**G2.5 on the G-a pin is explicitly not a blanket proved-current.** That verdict is copied from [G-a](exec-core-g2-final-rerun.md#g25-verdict) and is not re-judged here.

## Pins

| Field | Value |
|---|---|
| Sources read on `origin/main` | `d240831adc3cf08575e272be6c299baa3a562e8e` |
| G-a proved-current pin | `ba59f7f1445f310899a954ca1e85700bfaeadd7d` (`ba59f7f14`) |
| G-a report merge | [#4834](https://github.com/elsa-workflows/elsa-foundation/pull/4834) `10f3cfc7a80a3038a20c86ac9d002c168ed24cd8` |
| G-a report | [exec-core-g2-final-rerun.md](exec-core-g2-final-rerun.md) |
| Program | [#2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892) (closed 14 November 2026) |

Later commits on `d240831ad` were not a G2 re-run. **proved-current** in this statement means the G-a re-run on `ba59f7f14` only.

**Limit (every hosted cell cited here):** SQLite Owned Workbench, **one in-process host**, Root composition unless a cited test says otherwise. No PostgreSQL, SQL Server or MySQL cell. No multi-host / clustered placement.

## Status vocabulary (copied, not rewritten)

| Status | Meaning here | Source |
|---|---|---|
| **proved-current** | Named dual-model hosted (or, where G-a already accepted it, dedicated) test ran on `ba59f7f14` and passed. | G-a |
| **proved-historical** | Counted or hosted proof exists on a named earlier SHA; not re-executed on `ba59f7f14`. | G-a / S8 HISTORICAL / PR row |
| **shared-boundary** | Already in the shared host ports; not a new extraction. | G1 |
| **model-specific** | Adapter-level; deliberately different or plane-owned. | G1 |
| **limitation** | Named gap with no product path or no hosted proof, already recorded. | G2 / limitations ledger |
| **deferred** | Owner or Lead record: not pursued in this milestone. | D3 / D2 / #4126 |
| **missing** | Ledger already records no product path / no dual-model test. Still visible. | G-a |
| **not-run** | A named proof exists in the ledger but was not executed on the G-a pin (or no dedicated pass exists). | G-a |
| **unknown** | Similarity of shape or name is not enough. Not proved and not disproved. | G1 |
| **PROBE-ONLY** | Unit/in-memory only; not dual-model hosted proof. Copied as-is. | G2 / G-f |
| **UNMEASURED** | Method limit of an existing measurement. | G3 / G-d |

A **proved-historical** row is not silently promoted to **proved-current**. A **PROBE-ONLY** row is not promoted to hosted proof. An **unknown** stays unknown.

---

## 1. Shared-mechanism extraction (G1): met with zero extractions

Owner-mandated G-c clause (Engineer remaining slice, 11 November): the closing statement records **zero extractions**; shared mechanisms were extracted only where native and BPMN demonstrated the need; **none were extracted**.

Verified against the reports on this write-up's main. **No contradiction.** The record is:

| Claim in the sources | Location |
|---|---|
| Remaining extraction candidates (duplicated in adapters, identical, dual-path tests): **none** | [G1 candidate list](exec-core-g1-mechanism-inventory.md#g1-candidate-list) |
| “No extraction is decided or scheduled.” | [G1 What this does not claim](exec-core-g1-mechanism-inventory.md#what-this-does-not-claim) |
| “No extraction.” | [G2 G2.5 / G3 / G1](exec-core-g2-child-outcome-mapping.md#g25--g3--g1) |
| “No extraction, generic kernel, or merged native/BPMN retry/alteration semantics.” | [limitations / deferrals](exec-core-limitations-deferrals.md#what-this-does-not-claim) |
| G1 extraction candidate? **no** on every adapter-level row in the summary table | [G1 Summary table](exec-core-g1-mechanism-inventory.md#summary-table) |

Three mechanisms are labelled **identical** and “already in the shared host (would satisfy G1; **do not extract again**)”: execution ownership lease / heartbeat / release; checkpoint expected-fence stamp; checkpoint commit-store infrastructure. Those are pre-existing host ports inventoried on `0944325db` ([#4047](https://github.com/elsa-workflows/elsa-foundation/pull/4047) `e962de5901380840aafed6bf44c0eee2071b5c2d`). They are **shared-boundary**, not extractions performed in this milestone. G1 forbids extracting them again. That labelling does not contradict “zero extractions.”

No extraction PR landed under this milestone. A fully generic kernel remains out of scope (9 November owner clarification; [unified-execution.md](../program-goals/unified-execution.md)).

---

## 2. Cancel-admission equivalence: unknown

Owner-mandated G-c clause: cancel-admission equivalence between native and BPMN is **unknown** (not proved, not disproved).

Copied from [G1 §4 Cancellation](exec-core-g1-mechanism-inventory.md#4-cancellation) and [G1 Unknowns](exec-core-g1-mechanism-inventory.md#unknowns-stay-unknown):

> `WorkflowExecutionCommandKind.Cancel` **handling** of a native `WorkflowExecution` is the same host handler. Whether actor-enqueue (`ChildCancelExecutor`) and scheduler-queue (`BpmnNativeWorkDelivery.cs:58`) are equivalent admission is **unknown** (not proved on this SHA). Not a candidate.

G-a did not re-classify that unknown. Hosted cancel *outcomes* on each plane (G2.3 / A4 / B4) are separate from admission-path equivalence and are not used here to infer it.

---

## 3. Definition of done — evidence map

DoD parts (Lead [comment 6801627340](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6801627340); Engineer [comment 6801641695](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6801641695); 8 November finish line on #2892): G1 extract only demonstrated shared mechanisms; G2 explicit cross-model proofs for interrupted calls, retries, cancellation and child outcomes; G3 persistence/coordination overhead; plus BPMN settlement/timeout, pending backend/account switching, access isolation, compatibility/migration/rollback guidance, and cleanup.

Boundary is **shared-boundary** or **model-specific** as G1 uses those words. It is not a proof upgrade.

### Interrupted cross-model calls (G2.1)

| Row | Status | Boundary | Evidence |
|---|---|---|---|
| G2.1 (a) native `RunBpmn` parent; crash after model terminal before parent consume | **proved-current** | model-specific | [#3992](https://github.com/elsa-workflows/elsa-foundation/pull/3992) `eb4c6a6162130123621ee9a2d326256fd6d8c97e`; G-a [#4834](https://github.com/elsa-workflows/elsa-foundation/pull/4834) `10f3cfc7a80a3038a20c86ac9d002c168ed24cd8` on `ba59f7f14`; `RunBpmnPendingParentResultCrashTests` (diagnostic re-run 1/1 after operator-env fail; both results kept); [G-a G2.1](exec-core-g2-final-rerun.md#g21-interrupted-cross-model-calls) |
| G2.1 (b) composite `callActivity` → native child; wait-phase kill and pending-parent-resume kill | **proved-current** | model-specific | [#3995](https://github.com/elsa-workflows/elsa-foundation/pull/3995) `9e52948dd5239e4715e137c14e2546a56c2ab92a`; G-a 2/2; `CallActivityNativeChildCrashTests`; [G-a G2.1](exec-core-g2-final-rerun.md#g21-interrupted-cross-model-calls) |
| G2.1 serviceTask → native worker host-kill after result, before parent consume | **missing** | model-specific | G-a: no class in `tests/**` with that shape; not inferred from `DirectBpmnWorkbenchTests.Direct_service_work_…` (restart-and-complete, not crash-after-result) |

### Retries (G2.2 / G-b)

| Row | Status | Boundary | Evidence |
|---|---|---|---|
| G2.2 / G2.4 A3 `RunBpmn` + BPMN `serviceTask` native worker: `HoldForRetry` / retry / `FinalFault` | **proved-current** | model-specific | [#3993](https://github.com/elsa-workflows/elsa-foundation/pull/3993) `428d9840cc92dd0c0eb00e2d88d380f540e848dc`; G-a 3/3; `RunBpmnChildFaultHeldRetryTests`; [G-a G2.2](exec-core-g2-final-rerun.md#g22-held-retry). **Not D3 evidence.** |
| G-b CompensationScope handler retry of a native child under `callActivity` (spec 261) | **proved-current** | model-specific | [#4129](https://github.com/elsa-workflows/elsa-foundation/pull/4129) `6023308cbb72903f42e06b914908cefd56211ebe`; G-a 1/1; `CallActivityNativeChildHeldRetryTests`; [G2 G-b](exec-core-g2-child-outcome-mapping.md#g-b-callactivity--native-child-held--retried-mapping). Not `RetryServiceAsync`, not `RetryFaultedActivity/1`. |
| L-Gb1 operator `RetryFaultedActivity/1` of a native child under `callActivity` | **missing** / **limitation** (belongs with D3) | model-specific | G-a L-Gb1; [G2 G-b](exec-core-g2-child-outcome-mapping.md#g-b-callactivity--native-child-held--retried-mapping); [limitations L-Gb1](exec-core-limitations-deferrals.md#l-gb1--operator-retryfaultedactivity-under-callactivity-belongs-with-d3) |
| L-Gb2 BPMN `HoldForRetry` / `RetryServiceAsync` on the composite `callActivity` plane | **missing** / **limitation** (no product path) | model-specific | G-a L-Gb2; [G2 G-b](exec-core-g2-child-outcome-mapping.md#g-b-callactivity--native-child-held--retried-mapping); [limitations L-Gb2](exec-core-limitations-deferrals.md#l-gb2--bpmn-holdforretry--retryserviceasync-on-the-callactivity-plane-has-no-product-path) |
| Direct-plane hosted retry-vs-cancel winner (`BpmnServiceRetryCancellationTests`) | **proved-current** on G-a 2/2; **not** G2.2 (a) and **not** L-Gb2 | model-specific | G-a G2.2 note; not a silent upgrade of L-Gb2 |

### Cancellation (G2.3 / child cancel)

| Row | Status | Boundary | Evidence |
|---|---|---|---|
| G2.3 (b) `callActivity` host-kill | **proved-current** | model-specific | same class as G2.1 (b); G-a; `CallActivityNativeChildCrashTests` |
| G2.3 (a) native parent cancel before model admission | **proved-current** | model-specific | G-a 1/1; `RunBpmnAdmissionCancellationTests`; [G-a G2.3](exec-core-g2-final-rerun.md#g23-host-kill--admission-cancel) |
| G2.3 (a) interrupt | **proved-current** | model-specific | same as G2.1 (a) |
| G2.4 A4 child model cancel → native parent `Cancelled` once | **proved-current** | model-specific | [#4028](https://github.com/elsa-workflows/elsa-foundation/pull/4028) `0eff60275f7e0b43b0d8621b4315cbeb565f2a8a`; G-a 1/1; `RunBpmnChildCancelTests` |
| G2.4 B4 native child cancel under `callActivity` | **proved-current** | model-specific | G-a 2/2; `CallActivityNativeChildOutcomeTests`; [G2 B4](exec-core-g2-child-outcome-mapping.md#mapping-table) |
| Actor-enqueue vs scheduler-queue Cancel **admission** equivalence | **unknown** | shared-boundary handler; admission **unknown** | G1 §4 / Unknowns; this statement §2. Not inferred from A4/B4 outcomes |

`DispatchWorkflow` Complete(`Cancelled`) is not `RunBpmn` `ActivityTransition.Cancel` ([G2 planes](exec-core-g2-child-outcome-mapping.md#planes-do-not-collapse)).

### Child outcomes (G2.4)

Copied from G-a’s re-run of the [G2.4 mapping](exec-core-g2-child-outcome-mapping.md#mapping-table). LIMITATION / PROBE-ONLY / not-run rows are not upgraded.

| Row | Status | Boundary | Evidence |
|---|---|---|---|
| A1 success → `RunBpmn` `Done` | **proved-current** | model-specific | G-a: `RunBpmnWorkbenchTests` 9/9, `InstantBpmnWorkbenchTests` 4/4, crash re-run 1/1; historical [#3410](https://github.com/elsa-workflows/elsa-foundation/pull/3410) `9936c891ed90a2c4c9b524d06eaa85317c6eefe7` |
| A2 native worker `Completed` + `BusinessError` | **proved-current** | model-specific | [#4102](https://github.com/elsa-workflows/elsa-foundation/pull/4102) `620966687efdf416598defa007b272fe648328c1`; G-a 2/2; `RunBpmnNativeBusinessErrorTests` |
| A3 technical `Faulted` / `HoldForRetry` / `FinalFault` | **proved-current** | model-specific | see G2.2 / #3993 |
| A4 child cancel | **proved-current** | model-specific | see #4028 |
| B1 native child success under `callActivity` | **proved-current** | model-specific | G-a hosted 2/2 `CallActivityNativeChildCrashTests`. Unit `BpmnCallActivityTests` remains a **probe**, not this proof. |
| B2 native-root **business** error across `DispatchWorkflow` | **missing** / **limitation** (no product path; needs design) | model-specific | G-a; [G2 B2](exec-core-g2-child-outcome-mapping.md#mapping-table); [limitations B2](exec-core-limitations-deferrals.md#b2--no-product-path-needs-design); owner D4. EF `BpmnCalledWorkflowControlEfTests` 32/32 on G-a is **not** B2 (BPMN-root `AuthoredFailureCode`). |
| B3 native child technical `Faulted` with catch-all boundary (hosted dual-model) | **not-run** on `ba59f7f14`; ledger Scenario B remains **proved-historical** on `#1032` (not promoted) | model-specific | Ledger proof is `e2e-tests/bpmn/Test-BpmnCallActivity.ps1` Scenario B, original [#1032](https://github.com/elsa-workflows/elsa-foundation/pull/1032) `57bd65a58e283a5f5baa7b3be0f2c3ad5a53475e`. G-a: e2e **not-run**; probe `BpmnCallActivityTests` 15/15 is not dual-model. |
| B3 uncaught technical (no boundary) | **PROBE-ONLY** / G-f | model-specific | [G2 OPEN rows](exec-core-g2-child-outcome-mapping.md#open-rows-remain-visible); [limitations G-f](exec-core-limitations-deferrals.md#g-f--hosted-b3-without-a-boundary-is-probe-only) |
| B4 native child cancel | **proved-current** | model-specific | see cancellation table |

G-a G2.5 proved-current on `ba59f7f14` (SQLite, one host, Root unless cited): G2.1 (a), G2.1 (b), G2.2 / A3, G-b CompensationScope handler retry, G2.3 (b) and (a) admission, G2.4 A1–A4, B1, B4. That list is **not** every G2.1–G2.4 cell.

### BPMN settlement / timeout

| Row | Status | Boundary | Evidence |
|---|---|---|---|
| Hosted instant/direct settlement after restart (`InstantBpmnWorkbenchTests`, including `First_decision_completion_returns_to_the_original_native_parent_after_restart`) | **proved-current** | model-specific | G-a 4/4; also A1 supporting. Not Studio FR-012. |
| Spec 1184 FR-012 Studio “awaiting settlement” display | **proved-historical** | model-specific (Studio) | [#4125](https://github.com/elsa-workflows/elsa-foundation/pull/4125) `b771a392cbd957f5e20acd8025d730b960d3054e` (reviewed head `6d1f6559347ef1721d28f766d57e2a1cf5fa3b37`); journey `shows Cancelled · awaiting settlement while a native parent remains owed`. G-a did not run Studio/browser; not proved-current. [#3650](https://github.com/elsa-workflows/elsa-foundation/issues/3650) remains open (spec 1184 In progress). |
| FR-012 timeout end-event label | **deferred** (product gap) | model-specific (Studio) | [#4126](https://github.com/elsa-workflows/elsa-foundation/issues/4126) (open); #4125 journey `timeout path reports Completed without labelling the timed-out end event` asserts current behaviour. Engineer D5 follow-up: do not build in this milestone. |
| G-e recorded #3993 post-restart timeout (causal) | current hold-claim path **proved-historical** on `a898bb87f`; 8 November run-1 status **unknown**; G-a re-ran the #3992 crash Fact as **proved-current** | model-specific | [#4167](https://github.com/elsa-workflows/elsa-foundation/pull/4167) `b82487e992a07491a905449000bf4361902ae8dd`; [G3 G-e](exec-core-g3-overhead-counts.md#g-e--recorded-3993-post-restart-timeout-causal). The timeout was `RunBpmnPendingParentResultCrashTests` (#3992), not the #3993 held-retry Facts. Designed `ClaimVisibilityTimeout` fence, not a production defect. |

### Pending backend / account switching (FR-014)

| Row | Status | Boundary | Evidence |
|---|---|---|---|
| FR-014 pending start after an **account** switch | **proved-historical** | model-specific (Studio) | #4125 `b771a392c`; journey `does not apply a pending start after an account switch`. Not re-run on `ba59f7f14`. |
| FR-014 pending start after a **backend** switch | **proved-historical** | model-specific (Studio) | #4125; journey `does not apply a pending start after a backend switch`. Not re-run on `ba59f7f14`. |
| SC-005 Studio backend switching during a pending operation (browser) | **deferred** | model-specific (Studio) | [SC-005 Studio half](exec-core-sc005-authz.md#5-studio-half); [#4107](https://github.com/elsa-workflows/elsa-foundation/issues/4107) (GitHub issue closed 14 November; the deferral remains recorded) |

### Access isolation (SC-005 / D2)

| Row | Status | Boundary | Evidence |
|---|---|---|---|
| HTTP-level non-admin refusal on every registered BPMN endpoint | **proved-historical** | model-specific | [#4105](https://github.com/elsa-workflows/elsa-foundation/pull/4105) `8df9911a1d9c4ad476bfc1126ce33e0b2045eaba`; `BpmnHostingAuthorizationTests.Signed_in_user_without_bpmn_permissions_is_refused_on_every_bpmn_endpoint`; [SC-005 report](exec-core-sc005-authz.md). **Not** in the G-a class list; not proved-current. |
| Tenant half (second tenant cannot read or command the first) | **deferred** | model-specific | D2; #4107; SC-005 §4. Owned Workbench cannot host a second tenant without production host changes. Unit proofs remain labelled **unit**, not hosted. |
| Studio half (read-only/source-owned editing; pending-operation scope in the browser as SC-005) | **deferred** | model-specific | D2; #4107; SC-005 §5. Distinct from the FR-014 #4125 journeys above. |

[#3650](https://github.com/elsa-workflows/elsa-foundation/issues/3650) stays open. Spec 1184 is **In progress**. Security behaviour is never implicitly deferred; the tenant and Studio halves are **explicit** owner deferrals.

### Persistence / coordination overhead (G3)

Not a budget, target, speedup, SLO or throughput claim. Numbers stay in the G3 / G-a reports; they are not restated here as gates.

| Row | Status | Boundary | Evidence |
|---|---|---|---|
| EF command / round-trip / write / lease / fence **counts** on the #4028 child-cancel journey and the #3992 / #3993 / #3995 remaining journeys (SQLite Owned Workbench, one host, Root) | **proved-current** as one G-a execution of the harness; three-run ledger remains **proved-historical** on the G3 trees | n/a (measurement) | [#4039](https://github.com/elsa-workflows/elsa-foundation/pull/4039) `3da38eb53cd10b5f58d883e92cd1bbc25644a744`; [#4044](https://github.com/elsa-workflows/elsa-foundation/pull/4044) `0944325db06b2e0376a6109be6cf430d2b3e09c9`; G-a `G3OverheadCountTests` 1/1 and `G3RemainingJourneyOverheadTests` 3/3; [G3](exec-core-g3-overhead-counts.md); [G-a G3](exec-core-g2-final-rerun.md#g3-overhead-counts-one-run-not-a-budget) |
| G-d: overhead counts **commands**, not checkpoint **commits** | **limitation** | n/a | [limitations G-d](exec-core-limitations-deferrals.md#g-d--overhead-measurement-counts-commands-not-commits-sqlite-transaction-cost-is-unmeasured) |
| SQLite transaction cost | **UNMEASURED** / **limitation** | n/a | G3 Limit: SQLite EF logged no `Database.Transaction` Information lines; tx counters stay 0. Method limit, not a product defect. |
| Other providers / multi-host overhead | **not-run** | n/a | G3 “What this does not prove”; G-a not-run table |

Semantic Cancel **command** counts asserted by the #4028 harness (scheduler Cancel in SQLite `ContentJson` = 1, HTTP cancel applied/replayed/conflicted, native `RunBpmn` `Cancelled` = 1) were observed again on the G-a one-run. They are correctness counts, not a budget.

### Compatibility / migration / rollback guidance

| Row | Status | Boundary | Evidence |
|---|---|---|---|
| S8 stores/tables, zero Runtime/Bpmn migrations since `60d28493`, ApiCompat vs last-published Core packages | **proved-historical** (source on `a9771131c`; no test re-run on that pin) | model-specific publish pipelines | [#4091](https://github.com/elsa-workflows/elsa-foundation/pull/4091) `e81b0da15e5b8f20076927c94375f28f92d31313`; pin `a9771131c37650d518587805abadb6d2b77d18a3`; [S8](exec-core-migration-rollback.md) |
| Operator-facing migration/rollback note | **proved-historical** | model-specific | [native-bpmn-migration-and-rollback.md](native-bpmn-migration-and-rollback.md) on `c1f35d2dc` / interrupted-GC tree `9812e60f54`; [#3988](https://github.com/elsa-workflows/elsa-foundation/pull/3988) `fff9fbf2ce50743226b5390ee89d26b16cc85630` |
| FR-016 `Legacy*` hosting 14/14 | **proved-historical** | model-specific | S8 §4: last counted #3988 `fff9fbf2c` / tree `9812e60f54`. Source identity on the S8 pin **PROVED**; **UNPROVED as a fresh run on `a9771131c`**. G-a re-ran only `LegacyNativeParentUpgradeTests` 2/2 (**proved-current** for that class, not the whole family). |
| SC-004 frozen saved-Studio families / visual journeys | **proved-historical** / **UNPROVED** as a fresh run | model-specific (Studio) | S8 §4: October archive; PR2 visual journeys **NOT RUN** |
| FR-015 BPMN admission-off (keep readers/workers; new start refused) | **proved-current** for `BpmnHumanClaimUpgradeTests` (G-a 3/3); last counted FR-015 hosting pair remains **proved-historical** (#3988 2/0/0) | model-specific | S8 §5; G-a class table. Native Sequence/Flowchart twin flag: S8 **UNPROVED** (no `AdmitNewExecutions` outside BPMN). |
| Live native↔BPMN execution rewrite | **limitation** / unsupported | model-specific | S8 §6; operator note. Accepted work recovers on the model that admitted it. |
| Native ↔ BPMN publish consolidation | deliberately not done | model-specific | S8; operator note. Unfenced `SaveAsync` vs fenced `SaveWithRootWriteLeasesAsync`. |
| Pre-publication retention (D1 option (a)) | **limitation**; interrupted publish retry-safe is **proved-historical**; live race per engine **proved-historical**; no retention guarantee before publication | model-specific | [#4094](https://github.com/elsa-workflows/elsa-foundation/issues/4094) (GitHub issue closed 14 November; the limitation remains); [#4095](https://github.com/elsa-workflows/elsa-foundation/pull/4095) `507fb304a55f67512be42a2021695916b4c1725f`; [#4319](https://github.com/elsa-workflows/elsa-foundation/pull/4319) `ba59f7f1445f310899a954ca1e85700bfaeadd7d` (docs note; this is the G-a pin subject). G-a re-ran the #4095 live publish-vs-GC pair (native 2/2, BPMN 2/2) as **proved-current**. Default Workbench GC pump still off. |

### Cleanup

| Row | Status | Boundary | Evidence |
|---|---|---|---|
| Finish-line “remove demonstrated obsolete code and redundant tests only where coverage remains” | **not-run** as a dedicated cleanup pass | n/a | No `exec-core-*` cleanup ledger. Operator note: “Cleanup in this follow-up: no obsolete tests or production code were deleted.” G-a / G-c docs-only slices deleted none. |

### Shared host ports already present (G1; not this milestone’s extractions)

| Row | Status | Boundary | Evidence |
|---|---|---|---|
| Execution ownership lease / heartbeat / release | **shared-boundary** (already in host; do not extract again) | shared-boundary | G1 §6.1; `RuntimeExecutionLeaseRunner` |
| Checkpoint expected-fence stamp | **shared-boundary** (already in host; do not extract again) | shared-boundary | G1 §6.1 / §7.1 |
| Checkpoint commit-store infrastructure | **shared-boundary** (already in host; payload participation **model-specific**) | shared-boundary | G1 §7.1 |

Dual-path tests named in G1 for those ports are **existence** on `0944325db`, not a G-a re-run of every G1 native class.

### D3 — narrowly validated alteration

| Row | Status | Boundary | Evidence |
|---|---|---|---|
| Narrowly validated alteration (the #2892 BPMN-journey item: a BPMN process runs directly and through `BpmnProcess` with equivalent normalized traces including a **narrowly validated alteration**) | **deferred** | model-specific | Lead: “D3 defers alteration.” [limitations D3](exec-core-limitations-deferrals.md#d3--narrowly-validated-alteration-is-deferred). Distinct from **arbitrary** alteration / live-instance migration, which were already out of scope (specs 1017/1006; [unified-execution.md](../program-goals/unified-execution.md)). |
| Held retry #3993 as D3 evidence | **not D3 evidence** | model-specific | G2 G-b; limitations “Do not count #3993 as D3 evidence.” |

S8 on `a9771131c` listed the narrowly validated alteration as **OPEN** with “no owner deferral cited here.” That pin predates the 11 November D3 record. This statement follows the later ledger (**DEFERRED**), as the limitations report already does. It does not rewrite S8.

---

## 4. Open items kept visible

These stay visible. None are swallowed by G2.5, by Program #2892 closing, or by GitHub issue closures that left the recorded gap in place.

| Item | Status (copied) | Notes |
|---|---|---|
| **B2** native-root business error across `DispatchWorkflow` | **missing**; needs design | Not an untested shipped path. |
| **B3** hosted dual-model (`Test-BpmnCallActivity.ps1` Scenario B) | **not-run** | G-f uncaught B3 stays **PROBE-ONLY**. |
| **G2.1** serviceTask → worker crash-after-result | **missing** | No dedicated class located. |
| **L-Gb1** | **missing** / **limitation** | Belongs with deferred D3. |
| **L-Gb2** | **missing** / **limitation** | No product path on the callActivity plane. |
| **D3** narrowly validated alteration | **deferred** | Definition in §3 D3. #3993 is not D3 evidence. |
| **D2 tenant half** | **deferred** | [#4107](https://github.com/elsa-workflows/elsa-foundation/issues/4107). |
| **#4126** timeout end-event label | **deferred** | Open product gap; FR-012. |
| **#4094** pre-publication retention | **limitation** | No retention guarantee before publication. [#4319](https://github.com/elsa-workflows/elsa-foundation/pull/4319) is the docs note, not a guarantee. |
| Other providers / multi-host | **not-run** | Out of limit. |
| Studio half of SC-005 | **deferred** | #4107; distinct from #4125 FR-014. |
| **G-d** | commands counted; SQLite transactions **UNMEASURED** | Not a budget. |
| Cancel-admission equivalence | **unknown** | §2. |
| Native Sequence/Flowchart FR-015 admission-off twin | S8 **UNPROVED** | Not invented. |
| Dedicated cleanup pass | **not-run** | Operator note: nothing deleted in the #3988 follow-up. |

---

## 5. What this does not claim

- That the 8 November native/BPMN SQLite execution-core milestone is complete.
- A blanket G2.5 proved-current.
- Merged native vs BPMN cancel / retry / alteration / publish semantics.
- That actor-enqueue and scheduler-queue Cancel admission are equivalent.
- A budget, SLO, speedup, or throughput ranking.
- Qualification beyond SQLite Owned Workbench, one in-process host, Root unless a cited test names another parent.
- That probe `BpmnCallActivityTests` or BPMN-root `BpmnCalledWorkflowControlEfTests` prove native-root B2/B3.
- That #4125 Studio journeys are proved-current on `ba59f7f14`.
- Closure of [#3650](https://github.com/elsa-workflows/elsa-foundation/issues/3650), [#4126](https://github.com/elsa-workflows/elsa-foundation/issues/4126), spec 1184, [#2905](https://github.com/elsa-workflows/elsa-foundation/issues/2905), [#2907](https://github.com/elsa-workflows/elsa-foundation/issues/2907), or T039.
- Reopening or completing Program #2892.

## Out of scope (honoured)

No production code. No test code. No assertion change, skip, or quarantine. No force push. No new infrastructure. No issue closed. No CI workflow change.

---

## Owner decision needed

This statement does not complete the milestone. For Sipke to declare the 8 November SQLite native/BPMN execution-core milestone done, each row below would have to be **resolved** (new proof on a named SHA, with statuses copied forward honestly) **or accepted as a recorded limitation / deferral / unknown / not-run** as it stands here.

1. **B2** — missing; needs design (native-root business error across `DispatchWorkflow`).
2. **B3** hosted dual-model — not-run (`Test-BpmnCallActivity.ps1`); G-f uncaught B3 probe-only.
3. **G2.1** serviceTask → native worker crash-after-result — missing.
4. **L-Gb1** and **L-Gb2** — recorded limitations (L-Gb1 grouped with D3; L-Gb2 no product path).
5. **D3** narrowly validated alteration — deferred, with the definition in §3 (the #2892 BPMN-journey item, not arbitrary alteration / live-instance migration; #3993 is not D3 evidence).
6. **D2 tenant half** — deferred ([#4107](https://github.com/elsa-workflows/elsa-foundation/issues/4107)).
7. **#4126** Studio timeout end-event label — deferred product gap.
8. **#4094** pre-publication retention — recorded limitation (no guarantee before publication; #4319 is the public note).
9. **Other providers and multi-host** — not-run; out of the SQLite / one-host limit.
10. **Studio half of SC-005** — deferred.
11. **G-d** — commands counted; SQLite transactions unmeasured.
12. **Cancel-admission equivalence** (actor-enqueue vs scheduler-queue) — **unknown**.
13. **Native FR-015 admission-off twin** — S8 UNPROVED (optional to accept as recorded).
14. **Dedicated cleanup pass** — not-run (optional to accept the operator note that the #3988 follow-up deleted nothing).

No further inference is offered. The owner decides which of these are accepted as recorded and which must still be proved.
