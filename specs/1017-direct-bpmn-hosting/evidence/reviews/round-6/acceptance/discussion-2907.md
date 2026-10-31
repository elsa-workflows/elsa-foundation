# 2907 Feature: First-class BPMN integration and operational extension contracts
[Codex] On behalf of @sfmskywalker.

## Objective
Integrate the proven hosting path with existing BPMN semantics and platform capabilities.

## Scope and constraints
Production integration is owner-approved by [the 18 October continuation](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6249319967); released-library/parity/adoption prerequisites are delivered under #2735. Existing task #2898 now owns the single production-hosting implementation leaf in the shared queue. Full operator-console UX, arbitrary token alteration and live-instance migration are deferred.

## Acceptance criteria
- [x] Link/adopt #2735 and existing BPMN work; no competing semantics backlog. Parity3335/adoption3338/ADR3409 delivered with [final source-bound verification](https://github.com/elsa-workflows/elsa-foundation/pull/3409#issuecomment-6461083215).
- [ ] Declare supported BPMN profile, XML/BPMNDI behavior and task/error/incident/alteration/migration boundaries.
- [ ] Demonstrate service task, human wait, interruption and recovery through supported APIs.

## Validation
Reproducible source-bound evidence, negative/failure cases where behavior is claimed, and independent review. Record limitations explicitly. See the parent program for the full product intent and evidence policy.

## Released-library checkpoint — 25 October 2026

Upstream [release #95](https://github.com/valence-works/bpmn/issues/95) is complete: [v0.5.0](https://github.com/valence-works/bpmn/releases/tag/v0.5.0), source `021d2c5bba494f17d346144d97763cd3b2a13a4b`, payload 1.10.0. All four public package signatures/payloads and both framework consumer executions passed; all eight public PDBs matched their release assemblies. [Permanent qualification receipt](https://github.com/valence-works/bpmn/releases/download/v0.5.0/bpmn-0.5.0-qualification.json); [final evidence](https://github.com/valence-works/bpmn/pull/99#issuecomment-6393844989). PR99 received exact-head Code Review approval and a normal merge; resulting-main and stable-release gates passed.

Historical 25 October handoff: the [artifact/contract handoff to #2735](https://github.com/elsa-workflows/elsa-foundation/issues/2735#issuecomment-6393837314) preserved its existing owner. That prerequisite is now delivered by3335/3338/3409 with [final verification](https://github.com/elsa-workflows/elsa-foundation/pull/3409#issuecomment-6461083215), and the completed claim was released. #2898 is active under its existing issue and current Codex claim. Only the link/adopt criterion is complete; direct hosting, supported operational APIs and their integrated Studio acceptance remain open.

## Readiness

Coordinating scope stays open/Partial. Existing #2898/PR3410 is the single implementation lane. All five round3 findings have corrections/evidence, including four actual embedded ETL-origin cancellation journeys. Final affected qualification and exact-head round4 remain due, with owner/scan/CI/protected delivery holds. Wider ETL/direct-model Studio retain separate acceptance. [Current checkpoint](https://github.com/elsa-workflows/elsa-foundation/pull/3410#issuecomment-6532508249).

## Phase
M3

## Program
https://github.com/elsa-workflows/elsa-foundation/issues/2892

Affected repository: `elsa-workflows/elsa-foundation`; upstream BPMN work retains its existing owner.


<!-- program-links -->
## Parent
https://github.com/elsa-workflows/elsa-foundation/issues/2906

## Dependencies
- https://github.com/elsa-workflows/elsa-foundation/issues/2904


## Approved operational foundation

- [ ] Direct and embedded BPMN use the same released Bpmn.Semantics interpreter and preserve native composite completion/fault/cancellation behavior, including Flowchart; do not infer direct production hosting from the native compatibility witness.
- [ ] Provide supported task claim/completion and external work, durable correlation/timers, incidents/retry, cancellation and inspection above the semantics/hosting boundary. Declare the supported BPMN/XML/BPMNDI profile and exact authorization/idempotency behavior.
- [ ] Prove persisted recovery, replay and parent outcomes through actual production integrations, then integrated Studio authoring/inspection. Neutral public contracts remain provisional until native/BPMN integration evidence supports them.

No competing interpreter or takeover of #2735 is authorized. The earlier research witness and current green upstream PRs are not released-library adoption or production acceptance. No implementation leaf is created by this clarification.


## id=6281858047 elsa-workflows-codex[bot] 2026-10-20T02:23:26Z https://github.com/elsa-workflows/elsa-foundation/issues/2907#issuecomment-6281858047
Clarified owner-approved BPMN operational foundations, explicit deferrals and #2735 ownership; all production acceptance remains unchecked and not ready.

Program metadata reconciliation only. #3056 / PR #3087 remains the sole implementation leaf; no source, claim, review gate or external-release change.

<!-- program-coverage-reconciliation-20261020-2907 -->

## id=6391986221 elsa-workflows-codex[bot] 2026-10-25T01:32:37Z https://github.com/elsa-workflows/elsa-foundation/issues/2907#issuecomment-6391986221
Upstream prerequisite #34 / PR #52 is delivered: Code Review approved exact head `3cbb2c0` and merged it as `f0fc3ac143e242d959fe799abc64fe3af7a1bbdf`. Exact-merge build/test/pack, configured Feedz preview (`0.4.0-preview.53`) and wiki jobs pass; 969 tests pass with no failures/skips. [Final receipt](https://github.com/valence-works/bpmn/pull/52#issuecomment-6391983505).

This supplies refused collection reads with exact host faults and null BPMN ErrorCode. It is not stable-release adoption or proof of the six Foundation acceptance/operational criteria above. Those checkboxes remain open; #2735 retains adoption/parity ownership. Remaining upstream library leaves are being refreshed through the shared queue.

<!-- bpmn2907-pr52-f0fc3ac -->


## id=6392195564 elsa-workflows-codex[bot] 2026-10-25T01:57:17Z https://github.com/elsa-workflows/elsa-foundation/issues/2907#issuecomment-6392195564
Upstream BPMN prerequisites #34/PR52, #35/PR55 and #36/PR56 are now merged and resulting-main verified. Latest merge `a33c10e9abfecbe681ff1cd63e600fd8cfcb0de3` passes 984 tests with no failures/skips; configured Feedz preview and wiki jobs succeeded, NuGet.org skipped. [Latest receipt](https://github.com/valence-works/bpmn/pull/56#issuecomment-6392194091). The ordering contract, refused-read fault isolation and optional scheduling provenance are qualified within their upstream scope.

The next parity leaf #38 is being refreshed through the single shared implementation queue. All six Foundation acceptance/operational checkboxes above remain open; #2735 retains released-library adoption/parity ownership. No Foundation adoption or stable-release claim follows from these upstream previews.

<!-- bpmn2907-three-prerequisites-a33c10e -->

## id=6392529968 elsa-workflows-codex[bot] 2026-10-25T02:36:43Z https://github.com/elsa-workflows/elsa-foundation/issues/2907#issuecomment-6392529968
BPMN source-scenario port #38 / PR93 is merged and exact-merge verified: https://github.com/valence-works/bpmn/pull/93#issuecomment-6392528898. 1,207 tests pass on reviewed head and resulting main; all242 pinned methods accounted (197 ported,23 main,19 host obligations,3 intentional differences). Feedz preview56 exists; NuGet.org skipped and current stable does not contain it. The six Foundation production/adoption acceptance items stay open and #2735 keeps its owner. Active program implementation now resumes #40/PR58; no whole-feature completion claim.

<!-- bpmn38-parent-checkpoint-a8e3105 -->

## id=6392671680 elsa-workflows-codex[bot] 2026-10-25T02:53:43Z https://github.com/elsa-workflows/elsa-foundation/issues/2907#issuecomment-6392671680
Upstream #40/PR58 safe canceled-token pruning is merged and exactmerge verified: https://github.com/valence-works/bpmn/pull/58#issuecomment-6392670507. Current-head and resulting-main gates each pass1,214tests, samples/guard verified, Feedzpreview57 and wiki succeeded; NuGet.org skipped. No unconditional bound or Foundation adoption claim. Foundation #2735 owner and six production acceptance gates remain; approved issue7 is the sole implementation leaf.

<!-- bpmn40-parent-9c023d2 -->

## id=6392871154 elsa-workflows-codex[bot] 2026-10-25T03:17:59Z https://github.com/elsa-workflows/elsa-foundation/issues/2907#issuecomment-6392871154
Upstream #7/PR94 is complete at3db18aa: https://github.com/valence-works/bpmn/pull/94#issuecomment-6392869873. Exacthead and resultingmain each pass1,227tests; Feedzpreview58/wiki succeed, NuGet.org skipped. Default imports preserved, explicit host availability and real Call Activity escalation/ordinary-task compensation witnesses verified. Add both new capabilities to stable-release notes. The reviewer’s optional late Cancelled completion after Call Activity teardown is retained for later host lifecycle qualification, not claimed as new conformance proof. All six Foundation acceptance items remain open; #2735 retains its owner. Active implementation is queued diagnostic follow-up92.

<!-- bpmn7-parent-3db18aa -->

## id=6392888982 elsa-workflows-codex[bot] 2026-10-25T03:20:10Z https://github.com/elsa-workflows/elsa-foundation/issues/2907#issuecomment-6392888982
[Codex] On behalf of @sfmskywalker.

The next stable-library boundary is now durably tracked in https://github.com/valence-works/bpmn/issues/95. Targetv0.5.0 from qualifiedmain, with explicit77wire/76QName investigation,92completion, milestone dispositions, migration/schema/package-backed evidence and actual four-package NuGetverification. Existing release authority applies; no release is claimed yet. This is queued/unclaimed and does not replace #2735 ownership or #2898 selected-release parity. #92 remains the sole implementation leaf.

<!-- bpmn-stable-release-gate-20261025 -->

## id=6393057581 elsa-workflows-codex[bot] 2026-10-25T03:40:53Z https://github.com/elsa-workflows/elsa-foundation/issues/2907#issuecomment-6393057581
BPMN #92 / [PR #96](https://github.com/valence-works/bpmn/pull/96) is delivered: merge `61e0589a494a0817494a690b464d09c5658e5010`, protected cycle 2 clean, resulting-main 1,245 tests green. Structured IDs now identify all nine known-element family refusals; rejection messages and conditions are unchanged. [Final evidence](https://github.com/valence-works/bpmn/pull/96#issuecomment-6393056253). Preview .59 is not stable adoption. #77 is the sole current implementation; release-readiness #95, owner-held #2735, selected-release parity #2898 and all six production acceptance items remain open.

## id=6393160092 elsa-workflows-codex[bot] 2026-10-25T03:53:56Z https://github.com/elsa-workflows/elsa-foundation/issues/2907#issuecomment-6393160092
BPMN #77 / [PR #97](https://github.com/valence-works/bpmn/pull/97) delivered, merge `37a402b8d1dbbfbcc25bb1d1b56367988823a437`; protected cycle2 approved and exact resulting-main 1,251 tests green, preview .60 and wiki push verified. [Receipt](https://github.com/valence-works/bpmn/pull/97#issuecomment-6393158577). Computed HasValue is omitted from host-value JSON while legacy strict reads/C# API remain; published payload roots are unchanged. #76 QName fidelity is the sole current implementation; release #95, owner-held #2735, selected-release #2898 and the six #2907 production acceptance items remain open.

## id=6393410858 elsa-workflows-codex[bot] 2026-10-25T04:24:55Z https://github.com/elsa-workflows/elsa-foundation/issues/2907#issuecomment-6393410858
BPMN #76 / [PR98](https://github.com/valence-works/bpmn/pull/98) delivered: merge `48663f298bb75225b6045446a53ad9282115c83a`, protected cycle3 approved and exact-main1,268 tests/preview61/wiki verified. [Receipt](https://github.com/valence-works/bpmn/pull/98#issuecomment-6393409262). Payload1.10 QName-value context is durable through JSON with documented lexical/refusal limits. Release95 is the sole active leaf and includes one newly reproduced serialization-shadowing repair before publication; no stable-release or Foundation-adoption completion. #2735 ownership/#2898 parity and all six production criteria remain open.

## id=6393861316 elsa-workflows-codex[bot] 2026-10-25T05:20:57Z https://github.com/elsa-workflows/elsa-foundation/issues/2907#issuecomment-6393861316
[Codex] On behalf of @sfmskywalker.

Stable prerequisite delivered: BPMN v0.5.0 at `021d2c5bba494f17d346144d97763cd3b2a13a4b`. PR99 merged with exact-head approval; main and release CI passed 1,271 tests. All four publicly downloaded packages, signatures, both framework executions and all eight public symbols are verified. [Permanent receipt](https://github.com/valence-works/bpmn/releases/download/v0.5.0/bpmn-0.5.0-qualification.json); [final evidence](https://github.com/valence-works/bpmn/pull/99#issuecomment-6393844989).

[Handoff to #2735](https://github.com/elsa-workflows/elsa-foundation/issues/2735#issuecomment-6393837314) preserves its owner. All six production criteria remain open, and #2898 remains blocked on selected-release parity/adoption and scheduling. No Foundation adoption or complete-program claim.

<!-- bpmn95-parent2907-complete -->

## id=6461459702 elsa-workflows-codex[bot] 2026-10-28T05:29:42Z https://github.com/elsa-workflows/elsa-foundation/issues/2907#issuecomment-6461459702
[Codex] On behalf of @sfmskywalker.

The approved [18October continuation](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6249319967) authorizes production direct/embedded BPMN and explicitly defers arbitrary token alteration/live migration; those capabilities are not marked demonstrated. #2735's parity/adoption/ADR stack is now delivered and finally verified ([closeout](https://github.com/elsa-workflows/elsa-foundation/pull/3409#issuecomment-6461083215)); its claim is released.

The next single implementation leaf is existing **#2898**, with winning [Codex claim](https://github.com/elsa-workflows/elsa-foundation/issues/2898#issuecomment-6461197535). The old blocked prerequisite is resolved. Reserve **spec1017-direct-bpmn-hosting**, branch `agent/codex/2898-direct-bpmn-hosting`. Tree/archive and all17open PR file lists were checked: highest existing spec1016; no competing2898PR or human assignment. The ready queue is80 against the repository cap, so this extends the existing task; no new issue or reparenting.

Current-source gap: native checkpoints/actors are typed to native workflow participants; no production execution-model registry/direct host exists. The historical custom-host/ETL experiments remain research evidence. The new witness must use released0.5.0 through the shared fenced checkpoint/outbox authority, model-owned interpretation and real direct/embedded execution, with preserved native/frozen authoring and stored state. Cover restart, both timer/human/late-result orders, real finite ETL success/business-error/fault/cancel and minimal task/incident commands; declare construct/operation cuts. Capture actual current writer checkpoints before changing/deleting their writer. Review spec → plan → tasks before implementation.

#2898 production acceptance stays unchecked until its new evidence exists. #2907's link/adopt2735 criterion is now supported; its direct-host/operations/Studio criteria stay open. Existing native Sequence fault projection is preserved; separate owner decision #3242 is not silently settled here. Broader task administration/operator-console work, arbitrary alteration/migration, Orleans and taskrunnerremain deferred. Programgoalactive.

## id=6526358785 elsa-workflows-codex[bot] 2026-10-30T15:22:14Z https://github.com/elsa-workflows/elsa-foundation/issues/2907#issuecomment-6526358785
[Codex] On behalf of @sfmskywalker.

Existing #2898 / draft PR3410 is at `bbb21b25b1a2fa40c900d4374c2a979ff21377f4` with full-PR round2 running after round1 repairs. Actual direct BPMN recovery and service/human/timer/cancellation/incident API journeys are source-qualified in the [current checkpoint](https://github.com/elsa-workflows/elsa-foundation/pull/3410#issuecomment-6526294297). Both saved Studio families now survive adopted-template upgrade, normal save/publish/run and restart, retaining exact authoring/layout/pins (4 new runs/12 stores/13 controls at4f46ac55c). This is existing embedded Studio compatibility, not first-class direct-model Studio acceptance. The implementation is unmerged; coordinating acceptance remains unchecked until its own delivery/verification. Version-magnitude choice, deferred GitGuardian39189012/scan, exact reviewed-head required checks and protected/post-merge delivery remain open. Existing #3242 fault-policy disposition and Orleans/task-runner deferrals are unchanged. No second implementation claim or competing PR is created.
