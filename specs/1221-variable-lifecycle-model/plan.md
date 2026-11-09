# Implementation Plan: Variable lifecycle model

**Branch**: `claude/3052-s10-variable-lifecycle-f662ac` | **Date**: 2026-11-09 | **Spec**: [spec.md](spec.md)

**Status**: provisional, like the spec. The ADR PR it waits on (ADR 0092, #3411 + #3778) is in the spec's *Approval*
section. Nothing here is implemented until #3052's specs are defined, this spec is approved and the initiative branch
lands.

## Summary

Give every variable one versioned lifecycle so a write is judged against the read it was built on, as ADR 0092 (#3778
text) says. The technical approach (research R-1 to R-10):

- **Per-variable version + digest.** `VariableFrameState` carries, per variable, a version counter and a value digest
  over ADR 0091's canonical form, computed once per applied write, beside (not replacing) the frame revision.
- **Read sets.** Input materialization records an input read set (committed with the input snapshot, carried to
  background); the variable session records session reads per attempt. Each entry is (variable, version, digest).
- **The commit rule** runs at every write site — captures (`RuntimeOutputCaptureProjection`), intrinsics
  (`WorkflowIntrinsicExecutor`), session writes and alterations — identically for frames and rows, inline and
  background: blind → apply; base current → apply; base old but digest equal → apply + record; base old + digest differ
  → lost update.
- **Fault + LastWins.** The default faults through the existing incident path; `LastWins` (per activity type or per
  node) applies and is recorded. No new recovery mechanism.
- **Operator actions split.** `ActivitySuccessorStaging` is fixed so retry keeps the `InvocationId` + snapshot,
  re-execute/reschedule makes a new invocation with fresh inputs, and accept-recorded-completion applies the stored
  transition without the body. Amends ADR 0045 §3. A re-entered successor does not re-activate owned frames.
- **Background refresh (D-9).** The coordinator refreshes a stale input read set before admission as a new evaluation,
  opt-out per node, both snapshots kept and linked.
- **No inline gap (D-10).** Extend the one-work-item sequence (proven for `ReplaySafe` by `ReplaySafeFusionDriver`) to
  `External` leaves, keeping the `ActivityAttemptClaimed` claim/flush as intermediate commits (Open question A / PoC).
- **Suspended keeps base (D-11); `ModifyVariable` per-variable version (D-12); write history (D-13)** recorded as a
  durable data model that S-6 reads; physical storage chosen by measurement (Open question B).

## Technical Context

**Language/Version**: C# / .NET 10 (`global.json` SDK).

**Primary Dependencies**: System.Text.Json (digest over ADR 0091 canonical form), EF Core (runtime store), CShells
features; no new external dependency.

**Storage**: runtime EF store and the in-memory store (variable frames/rows, activity execution state, background work
receipts, the write history). A document/schema version increment for frame state carrying per-variable version/digest
and for the write-history record; old records upcast.

**Testing**: xUnit (no FluentAssertions). Reproduction drives (parallel `External` lost update; retry-uses-stale),
commit-rule unit tests, scheduling/fusion tests for the no-gap PoC, alteration/recovery tests, background-admission
refresh tests, write-history data tests, and a large-payload/hot-loop benchmark.

**Target Platform**: Elsa 4 server hosts (Foundation host, Workbench).

**Project Type**: libraries — `Elsa.Workflows.Runtime.Core` (models), `Elsa.Workflows.Runtime` (work handlers,
intrinsics, alterations, fusion), `Elsa.Activities.Runtime` (captures, invoke handler).

**Performance Goals**: one digest per applied write; two stored values per read (no hashing on read); one digest
comparison only on a version mismatch. The read set and write history must stay within an agreed overhead budget under
large payloads and hot loops (SC-005; budget set by Open question B's measurement).

**Constraints**:
- Runtime takes no Design reference (§E2.2); all machinery is runtime-side.
- Records written before the change stay readable (frame state and write history upcast).
- Tracking covers Elsa's instrumented access paths only; arbitrary host access is out of scope and not claimed.
- Background *eligibility* (ADR 0093 → S-11) is not decided here; only the refresh that applies once admitted.

**Scale/Scope**: per-variable version/digest in `VariableFrameState` and the row store; read-set capture at one input
site (`WorkflowStartActivitySchedulerWorkHandler`) and the session; the commit rule at four write sites
(`RuntimeOutputCaptureProjection`, `WorkflowIntrinsicExecutor`, session writes, `ModifyVariableAlterationHandler`); the
operator-action split in `ActivitySuccessorStaging`; the refresh on the background admission path; the no-gap change in
the start/fusion path; and a new write-history store.

## Constitution Check

*Loaded: `.specify/memory/constitution.md`, `.specify/memory/constitution-framework.md` and
`docs/contributing/review-checklist.md`. Re-checked after Phase 1: unchanged.*

| Gate | Status | Notes |
|---|---|---|
| §2.24.2 / §2.24.3 sanctioned patterns | PASS | No new pattern. Version/digest and read sets extend the existing frame/revision and input-snapshot machinery; the commit rule is a check at existing write sites. |
| §E2.2 Runtime takes no Design reference | PASS | All types and sites are in the runtime projects; no Design dependency. |
| §2.17 duplication beats dependency | PASS | Version/digest and read-set types live once in `Elsa.Workflows.Runtime.Core`, which the write sites already reference. |
| §E2.6.1 executable-always-runs | PASS | Executables published before stay runnable: frame state without a version reads as version 0 / no base (upcast); a blind write path always applies. |
| §E5 / §4.2 versioning | ACTION | `VariableFrameState` shape, the write-history record and `ActivitySuccessorStaging` behavior change before GA. Document/schema version increments with upcasts (T-series tasks). The ADR 0045 §3 amendment is a contract change carried by ADR 0092. |
| §E7 CI tiers | PASS | No new tier; reproduction and recovery tests run in the existing Full tier. |
| §2.22 docs | ACTION | `docs/runtime-fault-behavior.md` and variable/lifecycle docs change in this delivery. |
| §2.23.1–§2.23.2 tests | PASS | One test per acceptance scenario, plus failure paths (lost-update fault, refused acceptance of a faulted body, refresh opt-out) and the reproduction drives from the ADR's evidence list. |
| ADR 0032 R1/R2 (profiles, claim/flush) | WAIT | The no-gap change for `External` must keep claim/fence/durability (Open question A). The PoC confirms it or the finding reopens ADR 0092 decision 10 on #3411. |
| ADR 0045 §3/§4, ADR 0048 §4, ADR 0090 §2/§6/§7 | WAIT | Applied as ADR 0092 (#3778) amends them; this plan follows any change before agreement. |
| ADR 0091 canonical form | PASS | The digest is taken over ADR 0091's canonical form (decision 7 there), so it compares meaning. |

## Project Structure

### Documentation (this feature)

```text
specs/1221-variable-lifecycle-model/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── commit-rule.md
│   └── operator-actions.md
├── checklists/
│   └── requirements.md
└── tasks.md             # Phase 2 output (/speckit-tasks)
```

### Source Code (repository root)

```text
Essentials/src/Workflows/Runtime/Core/
├── Models/VariableFrameState.cs          # per-variable version + digest
└── Models/                               # read-set, base, write-history record types (new)

Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/
├── WorkHandlers/WorkflowStartActivitySchedulerWorkHandler.cs   # input read set; successor re-entry guard
├── WorkHandlers/ReplaySafeFusionDriver.cs                      # no-gap: extend one-work-item to External
├── Values/WorkflowIntrinsicExecutor.cs                         # Set/Merge/Reduce commit rule
├── Values/VariableFrameFactory.cs                              # version/digest on write
├── Alterations/ActivitySuccessorStaging.cs                     # retry / re-execute / accept split
└── Alterations/Handlers/ModifyVariableAlterationHandler.cs     # per-variable version check

Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/
├── RuntimeOutputCaptureProjection.cs                           # capture commit rule + base
└── WorkflowInvokeActivitySchedulerWorkHandler.cs              # capture commit site

Essentials/tests/...                                            # reproduction drives + unit/integration tests
```

**Structure Decision**: libraries only, all runtime-side. The data model (version, digest, read set, base,
write-history record) lands in `Elsa.Workflows.Runtime.Core/Models`; the commit rule and the operator-action split land
in `Elsa.Workflows.Runtime` and `Elsa.Activities.Runtime` at the write and staging sites named above. No Studio work
(inspection surfaces are S-6).

## Decisions

| # | Decision | Detail |
|---|---|---|
| P-1 | Version + digest on the frame | FR-001. `VariableFrameState` gains, per variable, `Version` (counter) and `Digest` (over ADR 0091 canonical form), set once per applied write in `VariableFrameFactory`. Frame revision stays for ordering; the version is per variable (Sipke R4). Rows mirror this. |
| P-2 | Read set types and capture | FR-002–FR-004. New `ReadSetEntry(VariableRef, Version, Digest)`; `InputReadSet` committed with the input snapshot (travels to background); `SessionReads` per attempt. Captured at `WorkflowStartActivitySchedulerWorkHandler` (input) and the session read path. Only actual, instrumented reads. |
| P-3 | Base representation | FR-005–FR-008. A write's base is the read-set entry it was built on. Input bases are immutable per invocation; a newer session read does not erase an older input dependency. The invocation's own writes advance their own base, persisted with the invocation. **Engineering determination (no ADR position):** where provenance is ambiguous, bind to the *oldest in-use* dependency (conservative). |
| P-4 | The commit rule | FR-009, FR-010. One `VariableCommitRule` evaluated at every write site (capture, intrinsic, session, alteration): blind → apply; base==current → apply; base old + digest equal → apply + record mismatch; base old + digest differ → lost update. Identical for frames/rows, inline/background. |
| P-5 | Fault + LastWins policy | FR-011–FR-013. Default `Fault` with a diagnostic (variable, versions, writers); pending changes not applied. `LastWins` per activity type (attribute) and per node (setting); always recorded. Routes through the existing incident strategy; the incident records the rejected completion. |
| P-6 | Operator-action split | FR-014–FR-017. Three actions in `ActivitySuccessorStaging`: *retry* (same `InvocationId` + snapshot, new attempt), *re-execute/reschedule* (new `InvocationId`, fresh inputs, new input read set — as Set/Merge/Reduce successors already do), *accept recorded completion* (apply stored transition, revalidate, duplicate-safe, no body). Fixes the current new-id-copies-snapshot bug; adds an owned-frame re-activation guard. Amends ADR 0045 §3. |
| P-7 | Background refresh (D-9) | FR-018–FR-020. On admission the coordinator compares the input read set to current versions; a changed input triggers a new *evaluation* (same `InvocationId`, own sequence + idempotency keys). Opt-out per node. Both snapshots kept and linked with the reason; the worker's snapshot immutable thereafter. Covers only Start→admission. |
| P-8 | No inline gap (D-10) | FR-021, Open question A. Extend the one-work-item dispatch (Schedule/Start/Invoke) proven for `ReplaySafe` (`ReplaySafeFusionDriver`) to `External` leaves, keeping `ActivityAttemptClaimed` claim/flush as intermediate commits so no other branch's work item interleaves and no DB transaction wraps user code. Proven by the PoC in research R-7; a failure is a finding on #3411. |
| P-9 | Suspension + alterations | FR-022, FR-023. Resume keeps snapshot + input read set (own-write bases advanced), fresh session, no auto-refresh; faults only on an explicit stale-changed write. `ModifyVariableAlterationHandler` records the variable's version and applies only if unchanged (replaces the root-frame revision check); a read alone never faults. |
| P-10 | Write history (D-13) | FR-024–FR-027, Open question B. A durable write-history record (mandatory metadata always; values opt-in + retention; operational data retained for enabled merge/recovery). Preserves every intermediate write incl. `0→1→0` in one buffered segment. Physical storage (rows vs. derived from checkpoint) chosen by the R-9 measurement. Feeds S-6; not ADR 0084's journal. |

## Complexity Tracking

> No constitution violations requiring justification. The two ACTION gates (§E5 versioning, §2.22 docs) are handled by
> versioning/upcast tasks and doc tasks; the three WAIT gates are the provisional dependence on ADR 0092 (#3778) and the
> Open-question-A feasibility against ADR 0032 R2, both tracked on #3411.
