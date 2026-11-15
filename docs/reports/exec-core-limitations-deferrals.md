# Execution-core milestone: limitations and deferrals

Point-in-time ledger for the 8 November 2026 native/BPMN SQLite execution-core milestone under Program [#2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892). Docs only. No production or test change. Native and BPMN semantics are not merged. A fact proved on one model is not inferred for the other.

| Field | Value |
|---|---|
| Pinned main SHA | `8ac3fa8ef26302b60387563c2328f03a93e59674` (`origin/main` at this write-up) |
| Commit | `[Claude] Keep run canvas zoom controls off activity cards (#4196) (#4211)` (2026-11-13) |
| Method | Reconcile the existing `docs/reports/exec-core-*.md` ledgers, the 11 November owner/Lead/Engineer records on #2892, and issues [#4094](https://github.com/elsa-workflows/elsa-foundation/issues/4094), [#4107](https://github.com/elsa-workflows/elsa-foundation/issues/4107), [#4126](https://github.com/elsa-workflows/elsa-foundation/issues/4126), plus PRs [#3992](https://github.com/elsa-workflows/elsa-foundation/pull/3992), [#3993](https://github.com/elsa-workflows/elsa-foundation/pull/3993), [#4102](https://github.com/elsa-workflows/elsa-foundation/pull/4102), [#4129](https://github.com/elsa-workflows/elsa-foundation/pull/4129), [#4167](https://github.com/elsa-workflows/elsa-foundation/pull/4167). No test re-run. No new claim. |

This statement does not complete Program [#2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892). It does not close [#4094](https://github.com/elsa-workflows/elsa-foundation/issues/4094), [#4107](https://github.com/elsa-workflows/elsa-foundation/issues/4107), or [#4126](https://github.com/elsa-workflows/elsa-foundation/issues/4126). It does not write G-a (final proof re-run) or G-c (closing statement); those remain **PENDING**.

**Limit (every hosted cell cited here):** SQLite Owned Workbench, **one in-process host**, Root composition unless a cited test says otherwise. No PostgreSQL, SQL Server or MySQL cell. No multi-host / clustered placement.

**Status vocabulary:** **DEFERRED** (owner or Lead record: not pursued in this milestone); **LIMITATION** (named gap with no product path or no hosted proof, already recorded); **PROBE-ONLY** (unit/in-memory only; not dual-model hosted proof); **UNMEASURED** (method limit of an existing measurement); **PENDING** (named remaining slice, not this report).

---

## Ledger

| Item | Status on this pin | Sources |
|---|---|---|
| **D3** narrowly validated alteration | **DEFERRED** | Lead [comment 6801627340](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6801627340); Engineer [comment 6801641695](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6801641695); G2 G-b / Limitations; S8 §6 (OPEN on its earlier pin — see Reconciliation) |
| Held retry [#3993](https://github.com/elsa-workflows/elsa-foundation/pull/3993) | **not D3 evidence** | G2 G-b; G1 §5; PR #3993 |
| **G-d** overhead counts commands, not commits; SQLite transaction cost unmeasured | **LIMITATION** / **UNMEASURED** | G3 Limit, Method, Environment; Engineer remaining slice G-d |
| **G-f** hosted B3 without a boundary | **PROBE-ONLY** | G2 B3 row, OPEN rows; Engineer remaining slice G-f |
| **B2** callActivity native business error | **LIMITATION** (no product path; needs design) | G2 B2; PR #4102; Lead D4 |
| **L-Gb1** operator `RetryFaultedActivity` under callActivity | **LIMITATION** (belongs with D3) | G2 G-b; PR #4129; Engineer L-Gb1 |
| **L-Gb2** BPMN `HoldForRetry` / `RetryServiceAsync` on the callActivity plane | **LIMITATION** (no product path) | G2 G-b; PR #4129; Engineer L-Gb2 |
| **G-e** recorded #3993 post-restart timeout explanation | **PENDING** ([#4167](https://github.com/elsa-workflows/elsa-foundation/pull/4167) open on this pin) | PR #4167; Engineer current-state table |
| **G-a** final proof re-run on main | **PENDING** (not this report) | Engineer remaining slices; Lead next steps |
| **G-c** closing statement | **PENDING** (not this report) | Engineer remaining slices; Lead next steps |
| Pre-publication retention | already filed [#4094](https://github.com/elsa-workflows/elsa-foundation/issues/4094) | S8 §6 / §7; issue #4094 |
| SC-005 tenant + Studio halves | already filed [#4107](https://github.com/elsa-workflows/elsa-foundation/issues/4107) | SC-005 report; issue #4107 |
| Studio timeout end-event label (FR-012) | already filed [#4126](https://github.com/elsa-workflows/elsa-foundation/issues/4126) | issue #4126; Engineer D5 follow-up |

---

## D3 — narrowly validated alteration is deferred

### Term

The phrase **narrowly validated alteration** is the BPMN-journey item in Program [#2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892):

> A BPMN process runs directly and through BpmnProcess with equivalent normalized semantic traces: fork/join, restart, human wait, interrupting timer, late result, worker incident and a **narrowly validated alteration**.

That journey item is distinct from **arbitrary** alteration and live-instance migration, which were already out of this milestone's product scope:

- [unified-execution.md](../program-goals/unified-execution.md) approved scope: “Arbitrary token alteration, live-instance migration and a full operator console are deferred, not demonstrated.”
- [spec 1017](../../specs/1017-direct-bpmn-hosting/spec.md) Assumptions: “Arbitrary alteration, live migration, full operator-console UX, further Orleans and the task-runner follow-up remain deferred.”
- S8 §6 ([exec-core-migration-rollback.md](exec-core-migration-rollback.md)): “Arbitrary alteration/live-instance migration stay out of scope (specs 1017/1006). The ‘narrowly validated alteration’ in the #2892 BPMN journey has **no current-pin evidence** in this statement and **no owner deferral cited here**. **OPEN.**” (pin `a9771131c`, before the 11 November D3 record below.)

G1 §5 ([exec-core-g1-mechanism-inventory.md](exec-core-g1-mechanism-inventory.md)) names the native operator surface that *is* an alteration: `RetryFaultedActivityAlterationHandler` — “operator alteration of one **faulted activity** in a running native run; requires `WaitForIntervention`.” G1 classifies that native alteration vs BPMN `RetryServiceAsync` as **deliberately different**. G2 names owner decision D3 as that ‘narrowly validated alteration’ work.

### Deferral (this milestone)

[Elsa 4 Lead, 11 November 2026](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6801627340): “Owner decisions D1–D5 (Nov 11) are already adopted. … **D3 defers alteration.**”

[Elsa 4 Engineer, 11 November 2026](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6801641695) lists “**D3 deferral text**” as a remaining named slice (this report). G2 Limitations and PR [#4129](https://github.com/elsa-workflows/elsa-foundation/pull/4129) already record D3 as “Not pursued in this milestone.”

**Status on this pin: DEFERRED.** This milestone does not claim a hosted narrowly-validated-alteration proof. Arbitrary alteration / live-instance migration remain out of scope as already recorded.

### #3993 is not D3 evidence

Held retry [#3993](https://github.com/elsa-workflows/elsa-foundation/pull/3993) (`RunBpmnChildFaultHeldRetryTests`) is G2.4 row **A3**: native parent + BPMN child via `RunBpmn` → `HoldForRetry` / `RetryServiceAsync` / `FinalFault`. It is **not** owner decision D3.

G2 G-b ([exec-core-g2-child-outcome-mapping.md](exec-core-g2-child-outcome-mapping.md)):

> `#3993` remains the native-parent + BPMN-child plane (`RunBpmn` → `HoldForRetry` → `RetryServiceAsync`) and is **not** owner decision D3 (narrowly validated alteration). Native and BPMN retry admission stay deliberately different (G1): this Fact is CompensationScope bounded handler retry (spec 261), not `RetryServiceAsync`, not `RetryFaultedActivity/1`.

PR #3993 itself claims G2.2 held retry / G2.4 child-fault on SQLite Owned Workbench, one host, Root; it does not claim alteration. G1 §5: BPMN direct retry is `BpmnModelRuntime.RetryServiceAsync` (new physical attempt id; does not restart the process), not `RetryFaultedActivity`.

**Do not count #3993 as D3 evidence.**

### Recorded #3993 timeout (G-e)

The 8 November related-filter timeout recorded during #3993 validation is a **separate** G-e item, not D3.

On this pin, [#4167](https://github.com/elsa-workflows/elsa-foundation/pull/4167) (`🔥 docs(exec-core): G-e causal note for the recorded #3993 post-restart timeout`) is **open** (head `ce81311aebb9f6c1505fe470fb77e9db1d7255bb`). Engineer current-state table and Lead open list both still name #4167. **Cite #4167 as pending.** This report does not copy or replace that write-up.

---

## G-d — overhead measurement counts commands, not commits; SQLite transaction cost is unmeasured

G3 ([exec-core-g3-overhead-counts.md](exec-core-g3-overhead-counts.md)) is “Not a budget, target, speedup or throughput claim.” Engineer remaining slice **G-d**: “state that overhead counts commands, not commits.”

What G3 counted:

- Metric families reused from `CommandCaptureInterceptor` / `EfCommandBudgetTests`: “every EF command = round-trip; INSERT/UPDATE/DELETE = write.”
- Parser: EF Core `Database.Command` / `Database.Transaction` Information logs → round-trips, writes, transactions, lease ops, fence/claim ops.
- Semantic Cancel counts for #4028 are scheduler Cancel **commands** in SQLite `ContentJson` (`WorkflowExecutionCommandKind.Cancel`), not checkpoint-commit counts.

What G3 did **not** count as commits: Runtime checkpoint commits (`RuntimeCheckpointCommitter` / store). G3’s numbers are EF SQL commands (round-trips/writes), not journal commits.

SQLite transaction cost is **UNMEASURED**:

- G3 Limit: “Transactions are **unmeasured** (SQLite EF logged no `Database.Transaction` Information lines, so tx counters stay 0).”
- Per-step and journey totals: transactions started/committed/rolled back were **0** on every run. G3: “this is a method limit, not a product defect.”
- Slice 6 repeats: “Transactions started/committed/rolled back were **0** on every run (unmeasured; SQLite EF logged no transaction Information lines).”

Provider limit remains SQLite Owned Workbench, one in-process host, Root. G3 “What this does not prove” already excludes other providers, multi-host, and a budget/SLO.

---

## G-f — hosted B3 without a boundary is probe-only

G2.4 row **B3** (callActivity → native child **TECHNICAL** failure) is **PROVEN** only for the catch-all error-boundary path: `e2e-tests/bpmn/Test-BpmnCallActivity.ps1` Scenario B ([#1032](https://github.com/elsa-workflows/elsa-foundation/pull/1032) `57bd65a58e283a5f5baa7b3be0f2c3ad5a53475e`). Engineer remaining slice **G-f**: “hosted B3 without a boundary is probe-only.”

G2 B3: “Uncaught `bpmn.call-activity.faulted` is probe-only (`FailureOutcome_NoCatcher_FaultsCompositeWithPinnedCode`); not claimed as hosted native-root proof.”

G2 OPEN rows:

| Row | Why it stays OPEN |
|---|---|
| B3 uncaught technical (no boundary) | Hosted native Fault child without a catcher is not in `Test-BpmnCallActivity.ps1` (Scenario B has a boundary). Probe-only. |

G2 G-b: “Immediate native `Fault` under callActivity (no hold) remains **B3** (`Test-BpmnCallActivity.ps1` Scenario B). This section does not re-prove B3.”

**Status: PROBE-ONLY** for hosted B3 without a boundary. Not a hosted native-root proof. SQLite / one host / Root as in G2 Limit.

---

## B2 — no product path; needs design

G2.4 row **B2** (callActivity → native child **BUSINESS** error):

> **no product path, needs design** … Nothing carries a business outcome from a **native-root** child across `DispatchWorkflow` (status only: `Completed`/`Faulted`/`Cancelled`/`DispatchFailed`). `AuthoredFailureCode` is read only for BPMN-root children (`BpmnProcessRuntime.cs:137`). Do not merge with A2, B3 (`Faulted`), or BPMN-root `BpmnCalledWorkflowControlEfTests`. Recorded as a limitation, not an untested shipped path.

Owner 11 November D4, as recorded by PR [#4102](https://github.com/elsa-workflows/elsa-foundation/pull/4102): “prove A2 with a test-only emitter; record B2 as no product path.” Lead: “D4 keeps A2 open with a test-only proof, and **B2 needs a design.**” After #4102 merged (`620966687`), G2 marks A2 **PROVEN** (hosted test-only) and B2 **LIMITATION**. This report does not re-open A2 and does not add a B2 design.

**Status: LIMITATION.** Needs design. Not an untested shipped path.

---

## L-Gb1 — operator `RetryFaultedActivity` under callActivity (belongs with D3)

G2 G-b / PR [#4129](https://github.com/elsa-workflows/elsa-foundation/pull/4129) (merged `6023308cbb72903f42e06b914908cefd56211ebe`):

| Case | Status |
|---|---|
| Operator `RetryFaultedActivity/1` of a native child under callActivity | **LIMITATION.** Different mechanism from BPMN `RetryServiceAsync`. Belongs with the deferred D3 'narrowly validated alteration' work. Not pursued in this milestone. |

Engineer: “**#4129 named limitations,** with no slice for either. **L-Gb1** is operator RetryFaultedActivity under callActivity, which belongs with the deferred D3 alteration work.” Lead open list: “Operator RetryFaultedActivity under callActivity … recorded as limitations that belong with the deferred D3 work.”

G1 §5: native `RetryFaultedActivity` vs BPMN `RetryServiceAsync` are **deliberately different**; “Tests (composite callActivity child retry): **none found** (outbox sweep only)” on the G1 pin. G-b later proved CompensationScope **handler** retry (spec 261), which G2 says is not `RetryFaultedActivity/1`.

**Status: LIMITATION.** Group with D3. Not pursued in this milestone.

---

## L-Gb2 — BPMN `HoldForRetry` / `RetryServiceAsync` on the callActivity plane has no product path

G2 G-b / PR #4129:

| Case | Status |
|---|---|
| BPMN `HoldForRetry` / `RetryServiceAsync` on the composite callActivity plane | **LIMITATION.** No product path on that plane. Not pursued. |

Engineer: “**L-Gb2** is BPMN HoldForRetry/RetryServiceAsync on the callActivity plane, which has no product path.” Lead: “BPMN HoldForRetry on callActivity [is] recorded as [a] limitation.”

G1 §5: “BPMN composite `callActivity`: No held-child-execution retry analogous to HoldForRetry.” Direct-plane `CallActivity` is refused at publish (G1 / G2 Limit). `#3993` / A3 remains the `RunBpmn` (native-parent) plane, not this composite callActivity plane.

**Status: LIMITATION.** No product path. Not pursued.

---

## Already-filed limitation issues (link only)

These stay open. This report does not close them and does not add evidence.

| Issue | What it records | Sources |
|---|---|---|
| [#4094](https://github.com/elsa-workflows/elsa-foundation/issues/4094) | No pre-publication retention guarantee for native and direct-BPMN publish (D1 option (a)). Interrupted publish is retry-safe (historical #3988, SQLite). Live race on SQLite / one host: both engines refused; no dangling reference (#4095 `507fb304a`). Other providers / multi-host unproven. Default Workbench GC pump off. | Issue body; S8 §6 / §7; comments on #4094 |
| [#4107](https://github.com/elsa-workflows/elsa-foundation/issues/4107) | Spec 1184 SC-005. Non-admin HTTP half proven on Owned Workbench SQLite / one host (`BpmnHostingAuthorizationTests`, #4105 `8df9911a1`). **Tenant half** and **Studio half** explicitly deferred. | Issue body; [exec-core-sc005-authz.md](exec-core-sc005-authz.md); #4107 comment |
| [#4126](https://github.com/elsa-workflows/elsa-foundation/issues/4126) | Studio: timeout path reports `Completed` without labelling which end event was reached (spec 1184 FR-012). **Deferred** product gap; do not build in this milestone. | Issue body; Engineer D5 follow-up |

---

## Pending (not this report)

| Slice | Why it stays PENDING |
|---|---|
| **G-a** | Engineer: “a final re-run on main, at the very end.” Lead next step 3: “re-run the proofs on final main, and verify main CI once.” G2: “G2.5 re-run on the milestone's final main commit is **not** this slice.” |
| **G-c** | Engineer: “the closing statement (zero extractions; cancel-admission equivalence ‘unknown’).” Lead next step 4: “write the closing statement against G1–G3, listing every missing proof explicitly.” G1 already records remaining extraction candidates: **none**, and actor-enqueue vs scheduler-queue Cancel as **unknown**. |
| **G-e** | [#4167](https://github.com/elsa-workflows/elsa-foundation/pull/4167) still open on this pin. |

---

## Reconciliation (sequential pins, not a new claim)

S8 on `a9771131c` listed G2.4 A2/B2 as **OPEN** (“No non-ETL business-error path”) and the narrowly validated alteration as **OPEN** with “no owner deferral cited here.” That pin predates D4 [#4102](https://github.com/elsa-workflows/elsa-foundation/pull/4102) (`620966687`) and the 11 November Lead/Engineer D3 record. Later sources:

- D4 / G2: A2 **PROVEN** (hosted test-only); B2 **LIMITATION** (no product path, needs design).
- G-b / #4129: D3 treated as deferred; L-Gb1 / L-Gb2 **LIMITATION**.
- Lead: “D3 defers alteration.” Engineer remaining slice: this D3 deferral text, plus G-d and G-f.

This report follows the later ledger for D3, B2, L-Gb1, L-Gb2, G-d, and G-f. It does not rewrite S8, G1, G2, G3, or SC-005. It does not re-open A2.

G1 on `0944325db` said composite callActivity child retry tests were “none found”; G-b later added CompensationScope handler-retry hosted proof. Those are different mechanisms (G2: not `RetryFaultedActivity/1`, not `RetryServiceAsync`). L-Gb1 / L-Gb2 remain limitations.

---

## SQLite / one-host limit (explicit)

Every hosted cell cited by G1, G2, G3, S8, SC-005, #3992, #3993, #4094, #4105/#4107, and #4129 is **SQLite Owned Workbench, one in-process host**, unless a cited test says otherwise. PostgreSQL, SQL Server, MySQL, multi-machine partition, clock skew, and Orleans are **not** qualified by this statement. S8 §6 already records that limit for interrupted-publish GC, FR-015 hosting, G2.4 hosted cells, and the #3988 operator note.

---

## What this does not claim

- No new proof, test, or production behaviour.
- No G-a re-run and no G-c closing statement.
- No G-e causal write-up (#4167 pending).
- No A2 re-classification.
- No B2 design.
- No extraction, generic kernel, or merged native/BPMN retry/alteration semantics.
- No closure of #2892, #4094, #4107, #4126, #2905, #2907, or T039.

## Out of scope (honoured)

No production, test, workflow, or other-doc change except the reports index link. [#3204](https://github.com/elsa-workflows/elsa-foundation/issues/3204), ETL [#3804](https://github.com/elsa-workflows/elsa-foundation/issues/3804)/[#3789](https://github.com/elsa-workflows/elsa-foundation/issues/3789), [#4038](https://github.com/elsa-workflows/elsa-foundation/issues/4038) untouched. No issue closed.
